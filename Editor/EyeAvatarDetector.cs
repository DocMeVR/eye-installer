using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>One clicked island reference: a vertex on a renderer to flood-fill and collapse.</summary>
    public struct EyeSeed
    {
        public SkinnedMeshRenderer renderer;
        public int vertex;
        public Vector3 point; // world-space hit, for drawing the marker
    }

    /// <summary>Everything the installer learned about one eye.</summary>
    public class EyeData
    {
        public string label;                          // "L" / "R"
        public Transform bone;                        // eye bone (the eyeball's pivot, driven by eye tracking)
        public SkinnedMeshRenderer renderer;          // mesh the eyeball island lives in (null if not found/readable)
        public List<int> island = new List<int>();    // vert indices weighted to this eye bone

        // World-space frame used to place the quad.
        public Vector3 centerWorld;                   // eyeball centre (~ eye bone)
        public Vector3 forwardWorld;                  // outward (the eye's view axis)
        public Vector3 upWorld;
        public Vector3 outwardWorld;                  // horizontal, pointing AWAY from the face centre
        public float radiusWorld;                     // iris radius (in-plane)
        public float frontDepthWorld;                 // forward distance from centre to the face skin surface

        // Optional manual seeds from Scene-view clicks: each one flood-fills the island that
        // contains that vertex on that renderer (instead of guessing by footprint). Multiple
        // seeds let one eye collapse several separate islands (iris + highlight + extras).
        public List<EyeSeed> seeds = new List<EyeSeed>();

        public bool HasIsland => renderer != null && island.Count > 0;
        public bool HasSeed => seeds != null && seeds.Count > 0;
    }

    /// <summary>Finds the eye bones, then the eye geometry weighted to them, and builds a
    /// symmetric per-eye frame for placement.</summary>
    public static class EyeAvatarDetector
    {
        // A vert belongs to the eye if at least this much of its weight is on the eye bone.
        // 0.5 inclusive handles face-tracking re-rigs that split the eyeball 50/50 with the head.
        public const float DefaultWeightThreshold = 0.5f;

        public static List<EyeData> Detect(GameObject avatar, out string log, float weightThreshold = DefaultWeightThreshold)
        {
            var sb = new StringBuilder();
            var result = new List<EyeData>();

            Transform left = null, right = null;
            FindEyeBones(avatar, ref left, ref right, sb);

            foreach (var pair in new[] { (left, "L"), (right, "R") })
            {
                var bone = pair.Item1;
                var name = pair.Item2;
                if (bone == null) { sb.AppendLine($"[{name}] no eye bone found"); continue; }

                var d = new EyeData { label = name, bone = bone };
                FindIsland(avatar, d, sb, weightThreshold);
                ComputeFrame(avatar, d, sb);
                result.Add(d);
            }

            // The two eyes must look identical, but per-eye detection differs a few %
            // (asymmetric weighting). Make them a common size and mirror-symmetric.
            UnifyEyeSize(result, sb);
            SymmetrizeCenters(avatar, result, sb);

            log = sb.ToString();
            return result;
        }

        /// <summary>Average the detected iris radii so both eyes are sized identically.</summary>
        public static void UnifyEyeSize(List<EyeData> eyes, StringBuilder sb = null)
        {
            float sum = 0f; int n = 0;
            foreach (var d in eyes) if (d.radiusWorld > 0f) { sum += d.radiusWorld; n++; }
            if (n == 0) return;
            float avg = sum / n;
            foreach (var d in eyes) d.radiusWorld = avg;
            sb?.AppendLine($"Unified eye radius = {avg:F4}m");
        }

        /// <summary>
        /// Mirror the two eye centres across the avatar's sagittal plane (local X = 0): equal
        /// height (Y), equal depth (Z), equal-and-opposite horizontal offset (X). Also gives both
        /// the same forward/up so they're oriented consistently. Depth along forward is unified
        /// later at placement time.
        /// </summary>
        public static void SymmetrizeCenters(GameObject avatar, List<EyeData> eyes, StringBuilder sb = null)
        {
            if (eyes.Count != 2) return;
            var a = avatar.transform;
            Vector3 p0 = a.InverseTransformPoint(eyes[0].centerWorld);
            Vector3 p1 = a.InverseTransformPoint(eyes[1].centerWorld);

            float absX = (Mathf.Abs(p0.x) + Mathf.Abs(p1.x)) * 0.5f;
            float y = (p0.y + p1.y) * 0.5f;
            float z = (p0.z + p1.z) * 0.5f;

            // Assign sides by relative X so the two eyes always land on opposite sides (avoids
            // Mathf.Sign(0)=+1 putting both on the same side).
            float s0 = p0.x <= p1.x ? -1f : 1f;
            float s1 = -s0;
            eyes[0].centerWorld = a.TransformPoint(new Vector3(s0 * absX, y, z));
            eyes[1].centerWorld = a.TransformPoint(new Vector3(s1 * absX, y, z));

            // Outward = the avatar's right axis, signed per side, so a horizontal nudge moves the
            // eyes apart / toward the face centre symmetrically.
            eyes[0].outwardWorld = a.TransformDirection(Vector3.right * s0);
            eyes[1].outwardWorld = a.TransformDirection(Vector3.right * s1);

            Vector3 fwd = (eyes[0].forwardWorld + eyes[1].forwardWorld).normalized;
            if (fwd.sqrMagnitude < 1e-6f) fwd = a.forward;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
            if (up.sqrMagnitude < 1e-4f) up = Vector3.up;
            foreach (var d in eyes) { d.forwardWorld = fwd; d.upWorld = up; }

            sb?.AppendLine($"Symmetrized centres: |x|={absX:F4} y={y:F4} z={z:F4}");
        }

        static void FindEyeBones(GameObject avatar, ref Transform left, ref Transform right, StringBuilder sb)
        {
            // 1) Humanoid rig is the authoritative source when the eyes are mapped.
            var animator = avatar.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                left  = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                right = animator.GetBoneTransform(HumanBodyBones.RightEye);
                if (left != null || right != null)
                    sb.AppendLine($"Eye bones via humanoid rig: L={Name(left)} R={Name(right)}");
            }

            // 2) Name fallback for rigs that left the eye bones unmapped in the humanoid avatar.
            if (left == null || right == null)
            {
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (!n.Contains("eye")) continue;
                    if (n.Contains("lid") || n.Contains("brow") || n.Contains("lash") || n.Contains("white")) continue;

                    bool isLeft  = n.Contains("left")  || n.EndsWith("_l") || n.EndsWith(".l") || n.Contains("eye_l") || n.Contains("eyel");
                    bool isRight = n.Contains("right") || n.EndsWith("_r") || n.EndsWith(".r") || n.Contains("eye_r") || n.Contains("eyer");

                    if (isLeft && left == null) left = t;
                    else if (isRight && right == null) right = t;
                }
                sb.AppendLine($"Eye bones after name search: L={Name(left)} R={Name(right)}");
            }
        }

        static void FindIsland(GameObject avatar, EyeData d, StringBuilder sb, float weightThreshold)
        {
            SkinnedMeshRenderer best = null;
            List<int> bestIsland = null;

            foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;

                int boneIndex = System.Array.IndexOf(smr.bones, d.bone);
                if (boneIndex < 0) continue; // this renderer isn't skinned to the eye bone

                if (!mesh.isReadable)
                {
                    sb.AppendLine($"[{d.label}] {smr.name}: mesh not readable: can't isolate eye island (collapse disabled, will cover-and-occlude)");
                    continue;
                }

                var bw = mesh.boneWeights;
                var island = new List<int>();
                for (int i = 0; i < bw.Length; i++)
                    if (WeightOnBone(bw[i], boneIndex) >= weightThreshold) island.Add(i);

                if (island.Count > 0 && (bestIsland == null || island.Count > bestIsland.Count))
                {
                    best = smr;
                    bestIsland = island;
                }
            }

            if (best != null) { d.renderer = best; d.island = bestIsland; }
        }

        static void ComputeFrame(GameObject avatar, EyeData d, StringBuilder sb)
        {
            // Defaults: pivot at the eye bone, looking out the avatar's front.
            d.centerWorld  = d.bone.position;
            d.forwardWorld = avatar.transform.forward;
            d.radiusWorld  = 0.015f * Mathf.Abs(avatar.transform.lossyScale.x); // ~1.5cm guess, user-tuneable
            d.frontDepthWorld = d.radiusWorld * 1.8f;                            // fallback if we can't measure

            // Up = world up, made orthogonal to forward.
            d.upWorld = Vector3.ProjectOnPlane(Vector3.up, d.forwardWorld).normalized;
            if (d.upWorld.sqrMagnitude < 1e-4f) d.upWorld = Vector3.up;
            d.outwardWorld = avatar.transform.right; // refined (with correct side) in SymmetrizeCenters

            if (d.HasIsland)
            {
                var world = EyeMeshUtil.BakeWorldVertices(d.renderer, out var baked);

                // Centre = island centroid (≈ the eye bone). Forward stays the avatar's forward;
                // averaging a sphere's vertex normals would cancel out.
                Vector3 center = Vector3.zero;
                foreach (int i in d.island) center += world[i];
                center /= d.island.Count;
                d.centerWorld = center;

                // Radius = furthest island vert in the plane perpendicular to forward (iris radius).
                float r = 0f;
                foreach (int i in d.island)
                {
                    Vector3 v = world[i] - center;
                    v -= Vector3.Project(v, d.forwardWorld);
                    r = Mathf.Max(r, v.magnitude);
                }
                if (r > 1e-5f) d.radiusWorld = r;

                // Face-front depth: furthest-forward vert within the iris footprint (where the
                // visible flat iris sits). Scans all verts, since the iris is often head-weighted skin.
                float front = -1e9f;
                float foot = Mathf.Max(r * 1.2f, 0.012f);
                for (int i = 0; i < world.Length; i++)
                {
                    Vector3 w = world[i] - center;
                    float depth = Vector3.Dot(w, d.forwardWorld);
                    Vector3 planar = w - depth * d.forwardWorld;
                    if (planar.magnitude < foot && depth > front) front = depth;
                }
                if (front > -1e8f) d.frontDepthWorld = front;

                Object.DestroyImmediate(baked);
            }

            sb.AppendLine($"[{d.label}] bone={d.bone.name} renderer={(d.renderer != null ? d.renderer.name : "-")} " +
                          $"island={d.island.Count} radius={d.radiusWorld:F4}m front={d.frontDepthWorld:F4}m");

            CrossCheckAtPivot(avatar, d, sb);
        }

        /// <summary>Eye-bone weights can include geometry that isn't the visible eye (e.g. alternate eye
        /// shapes parked inside the head). If they disagree with the mesh in front of the pivot, trust the mesh.</summary>
        static void CrossCheckAtPivot(GameObject avatar, EyeData d, StringBuilder sb)
        {
            if (d.renderer == null) return;

            float reach = 0.35f * Mathf.Max(Mathf.Abs(avatar.transform.lossyScale.y), 1e-3f);
            var ray = new Ray(d.bone.position + d.forwardWorld * reach, -d.forwardWorld);

            List<int> island = null;
            int tried = 0;
            foreach (var hit in MeshPickUtil.RaycastAll(d.renderer, ray))
            {
                if (tried++ >= 6) break;
                island = EyeMeshHider.SelectIslandFromSeed(d.renderer, hit.vertex, d.centerWorld, d.forwardWorld, out _, out _);
                if (island.Count > 0) break; // nearest hit that's a small island, not face skin
            }
            if (island == null || island.Count == 0) return;

            var world = EyeMeshUtil.BakeWorldVertices(d.renderer, out var baked);
            Object.DestroyImmediate(baked);

            Vector3 c = Vector3.zero;
            foreach (int i in island) c += world[i];
            c /= island.Count;

            float r = 0f, front = 0f;
            foreach (int i in island)
            {
                Vector3 v = world[i] - c;
                float depth = Vector3.Dot(v, d.forwardWorld);
                r = Mathf.Max(r, (v - depth * d.forwardWorld).magnitude);
                front = Mathf.Max(front, depth);
            }
            if (r < 1e-5f) return;

            Vector3 offset = Vector3.ProjectOnPlane(c - d.centerWorld, d.forwardWorld);
            if (d.HasIsland && offset.magnitude <= r)
            {
                sb.AppendLine($"[{d.label}] pivot check: weights agree with the mesh at the pivot");
                return;
            }

            d.island = island;
            d.centerWorld = c;
            d.radiusWorld = r;
            d.frontDepthWorld = front;
            sb.AppendLine($"[{d.label}] pivot check: weight-based centre was {offset.magnitude:F4}m off the eye at the pivot: " +
                          $"using that island instead (island={island.Count} radius={r:F4}m)");
        }

        static float WeightOnBone(BoneWeight w, int idx)
        {
            float s = 0f;
            if (w.boneIndex0 == idx) s += w.weight0;
            if (w.boneIndex1 == idx) s += w.weight1;
            if (w.boneIndex2 == idx) s += w.weight2;
            if (w.boneIndex3 == idx) s += w.weight3;
            return s;
        }

        static string Name(Transform t) => t != null ? t.name : "-";
    }
}
