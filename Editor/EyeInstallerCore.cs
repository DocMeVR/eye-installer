using System.Collections.Generic;
using System.IO;
using UnityEditor;
using DocMe.HideIslands;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>
    /// Install / revert (no UI, so it can later run from an NDMF pass). Two modes: hide the
    /// original eyes and place a quad (collapse mode), or re-shader the original geometry in place
    /// (deform-with-face). Meshes are always instanced before editing, so revert is exact.
    /// </summary>
    public static class EyeInstallerCore
    {
        public const string GenDir = SourceModel.GenDir;
        public const int RollSame = 0, RollMirror = 1, RollIndependent = 2;
        const string HideShape = "DocMe_HideEyes";

        public class Options
        {
            public Material material;
            public Mesh eyeMesh;               // null => generated quad
            public bool hideOriginal = true;   // collapse the original iris so only ours shows
            public bool inSocket = true;       // place at iris depth (behind lids) vs at face front
            // Reuse the original eye geometry (re-shader it in place) instead of collapse + quad,
            // so the new eye keeps the avatar's blendshape deformation (blink squish, etc.).
            public bool deformWithFace = false;
            public bool flattenEye = true;     // (deform mode) project the eye verts onto a plane
            // (deform + flatten) drop the rear half of a domed eye, which would otherwise land on
            // top of the front half once flattened. Off for eyes that are already a flat sheet.
            public bool dropBackFaces = true;
            public float widthFactor = 1.3f;   // horizontal size relative to the detected iris
            public float heightFactor = 1.3f;  // vertical size relative to the detected iris
            public float footprintScale = 0.95f; // iris-footprint radius for the hide selection
            public float depthOffset = 0f;     // extra forward nudge (metres)
            public float verticalOffset = 0f;
            public float horizontalOffset = 0f; // apart (+) / toward face centre (−), metres
            public float rollOffset = 0f;      // roll of the left eye around its view axis (degrees)
            public float rollRightOffset = 0f; // roll of the right eye when rollMode is RollIndependent
            public int rollMode = RollMirror;  // RollSame: both the same way (older installs), RollMirror, RollIndependent
            public float angleOut = 0f;        // turn each eye toward its own outer side (degrees)
            public float angleUp = 0f;         // tilt both eyes up (+) or down (-) (degrees)
            // Which mesh the eyes live in. Null = auto (every active body mesh). Set this to pin
            // the hide to a specific renderer when auto picks the wrong / too many meshes.
            public List<SkinnedMeshRenderer> eyeRenderers;
            // Sink hidden geometry to the head bone instead of just collapsing it at the surface,
            // so nothing can peek through. Off = collapse in place (just below the surface).
            public bool sinkIntoHead = true;
            // Delete the original eye triangles instead of collapsing them. Immune to other
            // (additive) blendshapes shifting the collapsed geometry back into view.
            public bool deleteOriginal = false;
            // (deform mode) rewrite blendshapes so they move the new eye as one flat sheet:
            // 0 = leave as authored, 1 = fit scale over all verts (mean), 2 = widest extent (max).
            public int evenEyeShapes = 0;
        }

        public static void Install(GameObject avatar, List<EyeData> eyes, Options opt)
        {
            if (avatar == null || eyes == null) return;
            InstallInternal(avatar, eyes, opt);

            // Record how to run this install again (after the model is reimported).
            var marker = avatar.GetComponent<DocMeEyeInstall>();
            if (marker == null) return;
            marker.recipe = ToRecipe(avatar, eyes, opt);
            marker.appliedAt = System.DateTime.UtcNow.Ticks;
            var paths = new List<string>();
            foreach (var h in marker.hiddenRenderers)
            {
                SourceModel.Root(avatar, h.originalMesh, out h.sourcePath, out h.sourceMeshName);
                paths.Add(h.sourcePath);
            }
            marker.sourceHash = SourceModel.Hash(paths);
            EditorUtility.SetDirty(marker);
        }

        static DocMeEyeRecipe ToRecipe(GameObject avatar, List<EyeData> eyes, Options o)
        {
            var r = new DocMeEyeRecipe
            {
                material = o.material, eyeMesh = o.eyeMesh,
                hideOriginal = o.hideOriginal, inSocket = o.inSocket, deformWithFace = o.deformWithFace,
                flattenEye = o.flattenEye, dropBackFaces = o.dropBackFaces, sinkIntoHead = o.sinkIntoHead,
                deleteOriginal = o.deleteOriginal, widthFactor = o.widthFactor, heightFactor = o.heightFactor,
                footprintScale = o.footprintScale, depthOffset = o.depthOffset, verticalOffset = o.verticalOffset,
                horizontalOffset = o.horizontalOffset, rollOffset = o.rollOffset, evenEyeShapes = o.evenEyeShapes,
                rollRightOffset = o.rollRightOffset, rollMode = o.rollMode, angleOut = o.angleOut, angleUp = o.angleUp,
            };
            if (o.eyeRenderers != null) r.eyeRenderers.AddRange(o.eyeRenderers);
            foreach (var d in eyes)
                foreach (var seed in d.seeds) r.picks.Add(SourceModel.ToPick(avatar, d.label, seed));
            return r;
        }

        public static Options FromRecipe(DocMeEyeRecipe r) => new Options
        {
            material = r.material, eyeMesh = r.eyeMesh,
            hideOriginal = r.hideOriginal, inSocket = r.inSocket, deformWithFace = r.deformWithFace,
            flattenEye = r.flattenEye, dropBackFaces = r.dropBackFaces, sinkIntoHead = r.sinkIntoHead,
            deleteOriginal = r.deleteOriginal, widthFactor = r.widthFactor, heightFactor = r.heightFactor,
            footprintScale = r.footprintScale, depthOffset = r.depthOffset, verticalOffset = r.verticalOffset,
            horizontalOffset = r.horizontalOffset, rollOffset = r.rollOffset, evenEyeShapes = r.evenEyeShapes,
            rollRightOffset = r.rollRightOffset, rollMode = r.rollMode, angleOut = r.angleOut, angleUp = r.angleUp,
            eyeRenderers = r.eyeRenderers != null && r.eyeRenderers.Count > 0 ? new List<SkinnedMeshRenderer>(r.eyeRenderers) : null,
        };

        static void InstallInternal(GameObject avatar, List<EyeData> eyes, Options opt)
        {
            Undo.RegisterFullObjectHierarchyUndo(avatar, "Install DocMe Eyes");
            SourceModel.EnsureGenDir();

            var marker = avatar.GetComponent<DocMeEyeInstall>();
            if (marker == null) marker = Undo.AddComponent<DocMeEyeInstall>(avatar);
            marker.avatar = avatar;
            marker.widthFactor = opt.widthFactor;
            marker.heightFactor = opt.heightFactor;
            marker.forwardOffset = opt.depthOffset;
            marker.verticalOffset = opt.verticalOffset;

            Run(avatar, eyes, opt, new Sink { marker = marker });
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Same result as Install, but nothing is saved or recorded: meshes stay in memory and the
        /// placed eyes are DontSave objects. Call Restore() on the result to put the avatar back.</summary>
        public static EyePreview Preview(GameObject avatar, List<EyeData> eyes, Options opt)
        {
            var preview = new EyePreview();
            if (avatar == null || eyes == null) return preview;
            Run(avatar, eyes, opt, new Sink { preview = preview });
            return preview;
        }

        public class EyePreview
        {
            public readonly List<DocMeEyeInstall.HiddenRenderer> hidden = new List<DocMeEyeInstall.HiddenRenderer>();
            public readonly List<GameObject> eyes = new List<GameObject>();
            public readonly List<Mesh> meshes = new List<Mesh>();
            public bool IsEmpty => hidden.Count == 0 && eyes.Count == 0;

            public void Restore()
            {
                foreach (var h in hidden)
                {
                    if (h.renderer != null)
                    {
                        if (!string.IsNullOrEmpty(h.blendShapeName) && h.renderer.sharedMesh != null)
                        {
                            int idx = h.renderer.sharedMesh.GetBlendShapeIndex(h.blendShapeName);
                            if (idx >= 0) h.renderer.SetBlendShapeWeight(idx, 0f);
                        }
                        if (h.originalMesh != null) h.renderer.sharedMesh = h.originalMesh;
                        if (h.originalMaterials != null) h.renderer.sharedMaterials = h.originalMaterials;
                    }
                    if (h.modifiedMesh != null) Object.DestroyImmediate(h.modifiedMesh);
                }
                foreach (var go in eyes) if (go != null) Object.DestroyImmediate(go);
                foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
                hidden.Clear(); eyes.Clear(); meshes.Clear();
            }
        }

        // Where results go: onto the avatar's marker as saved assets, or into an in-memory preview.
        class Sink
        {
            public DocMeEyeInstall marker;
            public EyePreview preview;
            public bool IsPreview => marker == null;

            public void Commit(Mesh work)
            {
                if (IsPreview) { work.hideFlags = HideFlags.HideAndDontSave; return; }
                AssetDatabase.CreateAsset(work, AssetDatabase.GenerateUniqueAssetPath(GenDir + "/" + work.name + ".asset"));
            }
            public void Hidden(DocMeEyeInstall.HiddenRenderer h) { if (IsPreview) preview.hidden.Add(h); else marker.hiddenRenderers.Add(h); }
            public void Eye(GameObject go, string label, float depth)
            {
                if (IsPreview) { go.hideFlags = HideFlags.HideAndDontSave; preview.eyes.Add(go); }
                else marker.createdEyes.Add(new DocMeEyeInstall.CreatedEye { go = go, label = label, depth = depth });
            }
            public void EyeRadius(float r) { if (!IsPreview) marker.eyeRadius = r; }
        }

        static void Run(GameObject avatar, List<EyeData> eyes, Options opt, Sink sink)
        {
            // Deform-with-face: re-shader the original geometry in place; no collapse, no quad.
            if (opt.deformWithFace) { ReshadeInPlace(avatar, eyes, opt, sink); return; }

            var placeDepth = new Dictionary<EyeData, float>();
            if (opt.hideOriginal)
            {
                HideOriginalEyes(avatar, eyes, opt, sink, placeDepth);
                EyeAvatarDetector.UnifyEyeSize(eyes); // a clicked iris may refine the radius; keep both equal
            }

            // Fallback depth where we couldn't measure an iris (e.g. hide disabled).
            foreach (var d in eyes)
                if (!placeDepth.ContainsKey(d))
                    placeDepth[d] = opt.inSocket ? d.frontDepthWorld * 0.5f : d.frontDepthWorld;

            // Both eyes share one depth so they sit at the same plane (mirror symmetry).
            if (placeDepth.Count > 0)
            {
                float sum = 0f; foreach (var v in placeDepth.Values) sum += v;
                float avg = sum / placeDepth.Count;
                foreach (var d in new List<EyeData>(placeDepth.Keys)) placeDepth[d] = avg;
            }

            // Remember the (unified) eye radius so Update Fit re-fits to the same size.
            if (eyes.Count > 0) sink.EyeRadius(eyes[0].radiusWorld);

            // One mesh per eye, since each carries its own gaze bias. A custom mesh is copied, never edited.
            foreach (var d in eyes)
            {
                if (d.bone == null) continue;
                var placed = opt.eyeMesh != null ? Object.Instantiate(opt.eyeMesh) : EyeQuadFactory.Create();
                placed.name = (opt.eyeMesh != null ? opt.eyeMesh.name : "DocMe_EyeQuad") + "_" + d.label;
                WriteGazeBias(placed, d, opt);
                sink.Commit(placed);
                if (sink.IsPreview) sink.preview.meshes.Add(placed);
                CreateEyeObject(d, placed, opt, sink, placeDepth[d]);
            }
        }


        /// <summary>Select the eye verts on each body mesh (from clicks, or auto by footprint),
        /// shared by the collapse and reshade paths. Also refines each eye's radius and the per-eye
        /// iris depth (depthSum/depthCnt → average).</summary>
        static Dictionary<SkinnedMeshRenderer, List<(EyeData eye, List<int> verts)>> GatherSelections(
            GameObject avatar, List<EyeData> eyes, Options opt,
            Dictionary<EyeData, float> depthSum, Dictionary<EyeData, int> depthCnt)
        {
            var perRenderer = new Dictionary<SkinnedMeshRenderer, List<(EyeData eye, List<int> verts)>>();
            void Add(SkinnedMeshRenderer r, EyeData d, List<int> verts)
            {
                if (!perRenderer.TryGetValue(r, out var list)) perRenderer[r] = list = new List<(EyeData, List<int>)>();
                list.Add((d, verts));
            }

            // User-pinned renderers, or auto = every active body mesh.
            IEnumerable<SkinnedMeshRenderer> autoRenderers =
                (opt.eyeRenderers != null && opt.eyeRenderers.Count > 0)
                    ? opt.eyeRenderers
                    : avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var d in eyes)
            {
                if (d.HasSeed)
                {
                    // Precise: flood every clicked island. Size from the biggest (the iris).
                    float irisRadius = 0f; int biggest = -1;
                    foreach (var seed in d.seeds)
                    {
                        var smr = seed.renderer;
                        if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                        var verts = MeshIslands.SelectIslandFromSeed(smr, seed.vertex, d.centerWorld, d.forwardWorld, out float r, out float depth);
                        if (verts.Count == 0) continue;
                        Add(smr, d, verts);
                        depthSum[d] = (depthSum.TryGetValue(d, out var s) ? s : 0f) + depth * verts.Count;
                        depthCnt[d] = (depthCnt.TryGetValue(d, out var c) ? c : 0) + verts.Count;
                        if (verts.Count > biggest) { biggest = verts.Count; irisRadius = r; }
                    }
                    if (irisRadius > 1e-5f) d.radiusWorld = irisRadius;
                }
                else
                {
                    // Auto: small fully-contained islands in the iris footprint, on every body mesh.
                    float footprint = Mathf.Max(d.radiusWorld * opt.footprintScale, 0.010f);
                    foreach (var smr in autoRenderers)
                    {
                        // activeSelf, not activeInHierarchy: respects a mesh the user disabled, but
                        // still installs when the whole avatar is just toggled off in the scene.
                        if (smr == null || !smr.gameObject.activeSelf) continue;
                        var mesh = smr.sharedMesh;
                        if (mesh == null || !mesh.isReadable) continue;

                        var verts = EyeIslands.SelectEyeVerts(smr, d.centerWorld, d.forwardWorld, footprint, out float irisDepth);
                        if (verts.Count == 0) continue;
                        Add(smr, d, verts);
                        depthSum[d] = (depthSum.TryGetValue(d, out var s) ? s : 0f) + irisDepth * verts.Count;
                        depthCnt[d] = (depthCnt.TryGetValue(d, out var c) ? c : 0) + verts.Count;
                    }
                }
            }
            return perRenderer;
        }

        static void HideOriginalEyes(GameObject avatar, List<EyeData> eyes, Options opt,
                                     Sink sink, Dictionary<EyeData, float> placeDepth)
        {
            var depthSum = new Dictionary<EyeData, float>();
            var depthCnt = new Dictionary<EyeData, int>();
            var perRenderer = GatherSelections(avatar, eyes, opt, depthSum, depthCnt);
            if (perRenderer.Count == 0)
                Debug.LogWarning("[DocMe Eyes] Hide original eyes: no eye geometry found: the original eyes were NOT hidden.");

            var anim = avatar.GetComponentInChildren<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;

            foreach (var kv in perRenderer)
                CollapseRenderer(kv.Key, kv.Value, sink, opt, head);

            foreach (var d in eyes)
                if (depthCnt.TryGetValue(d, out var c) && c > 0)
                    placeDepth[d] = depthSum[d] / c;
        }

        static void ReshadeInPlace(GameObject avatar, List<EyeData> eyes, Options opt, Sink sink)
        {
            var perRenderer = GatherSelections(avatar, eyes, opt, new Dictionary<EyeData, float>(), new Dictionary<EyeData, int>());
            EyeAvatarDetector.UnifyEyeSize(eyes); // keep both eyes the same UV scale
            if (eyes.Count > 0) sink.EyeRadius(eyes[0].radiusWorld);

            if (perRenderer.Count == 0)
            {
                Debug.LogWarning("[DocMe Eyes] Deform-with-face: no eye geometry found to re-shader.");
                return;
            }

            var anim = avatar.GetComponentInChildren<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;

            foreach (var kv in perRenderer)
            {
                var smr = kv.Key;
                var src = smr.sharedMesh;

                // Largest island per eye is the canvas; smaller overlay pieces (highlights) get hidden
                // rather than flattened on top of it.
                var canvas = new List<(EyeData eye, List<int> verts)>();
                var canvasVerts = new HashSet<int>();
                var extras = new List<List<int>>();
                foreach (var (eye, verts) in kv.Value)
                {
                    var islands = MeshIslands.SplitIslands(smr, verts);
                    if (islands.Count == 0) continue;
                    canvas.Add((eye, islands[0]));
                    canvasVerts.UnionWith(islands[0]);
                    for (int i = 1; i < islands.Count; i++) extras.Add(islands[i]);
                }
                extras.RemoveAll(isl => isl.Exists(canvasVerts.Contains));

                // Evening rewrites every blendshape on the mesh (seconds on a face with hundreds of shapes);
                // the preview only has to show where the eye lands.
                int even = sink.IsPreview ? 0 : opt.evenEyeShapes;
                var work = EyeMeshReshader.Reshade(smr, canvas, opt, even, out Material[] origMats);

                string hideShape = null;
                if (extras.Count > 0 && opt.hideOriginal)
                {
                    if (opt.deleteOriginal)
                        MeshIslands.RemoveIslandTriangles(work, extras);
                    else
                    {
                        Vector3? sinkPos = null;
                        if (opt.sinkIntoHead && MeshIslands.TryGetBoneBindPosition(smr, head, out var hp)) sinkPos = hp;
                        if (MeshIslands.AddHideShape(work, extras, HideShape, default, 2f, sinkPos)) hideShape = HideShape;
                    }
                }

                sink.Commit(work);

                // Put the procedural material on the new (last) iris submesh. Unity allows a mesh to
                // have fewer materials than submeshes, so size the array to the submesh count and
                // fill any gap with the last original material before slotting ours in.
                int irisSub = work.subMeshCount - 1;
                var newMats = new Material[work.subMeshCount];
                for (int i = 0; i < newMats.Length; i++)
                    newMats[i] = i < origMats.Length ? origMats[i]
                               : origMats.Length > 0 ? origMats[origMats.Length - 1] : null;
                newMats[irisSub] = opt.material;

                sink.Hidden(new DocMeEyeInstall.HiddenRenderer
                {
                    renderer = smr, originalMesh = src, modifiedMesh = work, reshaded = true,
                    blendShapeName = hideShape,
                    originalMaterials = origMats, modifiedMaterials = newMats,
                });

                smr.sharedMesh = work;
                smr.sharedMaterials = newMats;
                if (hideShape != null) smr.SetBlendShapeWeight(work.GetBlendShapeIndex(hideShape), 100f);
            }
        }

        static void CollapseRenderer(SkinnedMeshRenderer smr, List<(EyeData eye, List<int> verts)> selections,
                                     Sink sink, Options opt, Transform head)
        {
            var src = smr.sharedMesh;
            var work = Object.Instantiate(src); // deep-copies blendshapes, weights, bindposes
            work.name = src.name + "_DocMeEyes";

            var islands = new List<List<int>>();
            foreach (var (eye, idx) in selections) islands.Add(idx);

            bool ok;
            if (opt.deleteOriginal)
            {
                ok = MeshIslands.RemoveIslandTriangles(work, islands);
            }
            else
            {
                Vector3? sinkPos = null;
                if (opt.sinkIntoHead && MeshIslands.TryGetBoneBindPosition(smr, head, out var hp)) sinkPos = hp;
                ok = MeshIslands.AddHideShape(work, islands, HideShape, default, 2f, sinkPos);
            }
            if (!ok) { Object.DestroyImmediate(work); return; }

            sink.Commit(work);
            sink.Hidden(new DocMeEyeInstall.HiddenRenderer
            {
                renderer = smr,
                originalMesh = src,
                modifiedMesh = work,
                blendShapeName = opt.deleteOriginal ? null : HideShape,
            });

            smr.sharedMesh = work;
            if (!opt.deleteOriginal) smr.SetBlendShapeWeight(work.GetBlendShapeIndex(HideShape), 100f);
        }


        static void CreateEyeObject(EyeData d, Mesh mesh, Options opt, Sink sink, float depth)
        {
            var go = new GameObject($"DocMe_Eye_{d.label}");
            if (!sink.IsPreview) Undo.RegisterCreatedObjectUndo(go, "Create DocMe Eye");
            go.transform.SetParent(d.bone, false); // child of the eye bone -> tracks the eye

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = opt.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            ApplyFit(go.transform, d, opt, depth);
            sink.Eye(go, d.label, depth);
        }

        /// <summary>Position/orient/scale a placed eye. 'depth' is the forward distance from the
        /// eye centre to where the disc sits (iris depth for in-socket; face front for cover).
        /// Scale is bounds-aware: whatever mesh is on the object (the generated quad OR a custom
        /// mesh) is scaled so its in-plane extent matches the detected iris: so any mesh fits.</summary>
        public static void ApplyFit(Transform t, EyeData d, Options opt, float depth)
        {
            // Mesh bounds: the smallest axis is the flat one (the eye's view axis); the other two
            // are in-plane. Works for the generated quad (flat in Z) or any custom flat mesh.
            Vector3 bs = Vector3.one;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) bs = mf.sharedMesh.bounds.size;

            int flat = FlatAxis(bs);
            Vector3 meshNormalLocal = Axis(flat);

            // Aim the flat axis along the eye's view direction, angle and roll included.
            EyeFrame(d, opt, out Vector3 fwd, out Vector3 up, out _);
            t.rotation = FitRotation(meshNormalLocal, fwd, up);

            t.position = d.centerWorld
                       + d.forwardWorld * (depth + opt.depthOffset)
                       + d.upWorld * opt.verticalOffset
                       + d.outwardWorld * opt.horizontalOffset;

            // Width vs height: of the two in-plane mesh axes, the one that ends up more vertical
            // (after orientation) carries height, the other width: so the sliders always mean the
            // same thing regardless of how the mesh was authored.
            int a1 = (flat + 1) % 3, a2 = (flat + 2) % 3;
            float vert1 = Mathf.Abs(Vector3.Dot(t.rotation * Axis(a1), up));
            float vert2 = Mathf.Abs(Vector3.Dot(t.rotation * Axis(a2), up));
            int hAxis = vert1 >= vert2 ? a1 : a2;  // height
            int wAxis = vert1 >= vert2 ? a2 : a1;  // width

            float parentScale = t.parent != null ? Mathf.Abs(t.parent.lossyScale.x) : 1f;
            float extW = Mathf.Max(bs[wAxis], 1e-4f);
            float extH = Mathf.Max(bs[hAxis], 1e-4f);
            float sW = Mathf.Max(d.radiusWorld * 2f * opt.widthFactor,  1e-4f) / extW / Mathf.Max(parentScale, 1e-6f);
            float sH = Mathf.Max(d.radiusWorld * 2f * opt.heightFactor, 1e-4f) / extH / Mathf.Max(parentScale, 1e-6f);

            var ls = Vector3.one;
            ls[wAxis] = sW; ls[hAxis] = sH; ls[flat] = (sW + sH) * 0.5f;
            t.localScale = ls;
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        // The mesh's thinnest axis is its view axis: a quad is flat in Z, a custom eye mesh in whatever it was built with.
        static int FlatAxis(Vector3 bs) => (bs.x <= bs.y && bs.x <= bs.z) ? 0 : (bs.y <= bs.x && bs.y <= bs.z) ? 1 : 2;
        static Quaternion FitRotation(Vector3 meshNormalLocal, Vector3 fwd, Vector3 up)
            => Quaternion.LookRotation(fwd, up) * Quaternion.FromToRotation(meshNormalLocal, Vector3.forward);

        /// <summary>Stores, per vertex in UV3, where the avatar's forward axis points in the eye surface's tangent
        /// space, the way the parallax shaders measure view directions (xy over z). The shader subtracts it, so an
        /// eye the installer turned outward still looks straight ahead and its parallax is measured from the avatar's
        /// forward. Zero when the eye is not angled.</summary>
        public static void WriteGazeBias(Mesh mesh, EyeData d, Options opt)
        {
            if (mesh == null) return;
            int n = mesh.vertexCount;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            bool hasN = normals != null && normals.Length == n, hasT = tangents != null && tangents.Length == n;
            Vector3 meshNormalLocal = Axis(FlatAxis(mesh.bounds.size));
            EyeFrame(d, opt, out Vector3 fwd, out Vector3 up, out _);
            Vector3 fLocal = Quaternion.Inverse(FitRotation(meshNormalLocal, fwd, up)) * d.forwardWorld.normalized;

            var uv3 = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 N = (hasN ? normals[i] : meshNormalLocal).normalized;
                Vector4 t4 = hasT ? tangents[i] : new Vector4(1f, 0f, 0f, -1f);
                Vector3 T = new Vector3(t4.x, t4.y, t4.z);
                if (T.sqrMagnitude < 1e-8f) T = Vector3.Cross(N, Vector3.up);
                if (T.sqrMagnitude < 1e-8f) T = Vector3.right;
                T.Normalize();
                Vector3 B = (Vector3.Cross(N, T) * (t4.w < 0f ? -1f : 1f)).normalized;   // the shaders' bitangent
                uv3.Add(GazeBias(fLocal, T, B, N));
            }
            mesh.SetUVs(3, uv3);
        }

        /// <summary>Tangent-space direction (xy over z) of the axis <paramref name="f"/> in the frame T, B, N.</summary>
        public static Vector2 GazeBias(Vector3 f, Vector3 T, Vector3 B, Vector3 N)
        {
            float z = Vector3.Dot(f, N);
            if (Mathf.Abs(z) < 0.2f) z = z < 0f ? -0.2f : 0.2f;   // nothing sensible past about 80 degrees
            return new Vector2(Vector3.Dot(f, T) / z, Vector3.Dot(f, B) / z);
        }

        /// <summary>Rewrites the gaze bias on a placed eye after its angle changed.</summary>
        public static void RefreshGazeBias(GameObject eye, EyeData d, Options opt)
        {
            var mf = eye != null ? eye.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null || !SourceModel.IsGenerated(AssetDatabase.GetAssetPath(mf.sharedMesh))) return;
            WriteGazeBias(mf.sharedMesh, d, opt);
            EditorUtility.SetDirty(mf.sharedMesh);
        }

        /// <summary>The eye's view axis and in-plane axes after the angle and roll settings. Each eye turns toward
        /// its own outer side, so the pair stays mirror symmetric; in mirror mode the right eye rolls the other
        /// way. Both build modes place the eye in this frame, and the parallax shader works in the surface's own
        /// tangent space, so an angled eye behaves exactly like a straight one.</summary>
        public static void EyeFrame(EyeData d, Options opt, out Vector3 fwd, out Vector3 up, out Vector3 right)
        {
            fwd = d.forwardWorld.normalized;
            Vector3 upRef = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
            if (upRef.sqrMagnitude < 1e-4f) upRef = Vector3.up;
            Vector3 outward = Vector3.ProjectOnPlane(d.outwardWorld, fwd).normalized;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.Cross(upRef, fwd).normalized;

            if (Mathf.Abs(opt.angleOut) > 1e-4f)
            {
                float a = opt.angleOut * Mathf.Deg2Rad;
                fwd = (fwd * Mathf.Cos(a) + outward * Mathf.Sin(a)).normalized;
            }
            if (Mathf.Abs(opt.angleUp) > 1e-4f)
            {
                float b = opt.angleUp * Mathf.Deg2Rad;
                fwd = (fwd * Mathf.Cos(b) + upRef * Mathf.Sin(b)).normalized;
            }
            up = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
            if (up.sqrMagnitude < 1e-4f) up = upRef;
            right = Vector3.Cross(up, fwd).normalized;

            float roll = RollFor(d, opt);
            if (Mathf.Abs(roll) > 1e-4f)
            {
                var q = Quaternion.AngleAxis(roll, fwd);
                right = q * right; up = q * up;
            }
        }

        public static float RollFor(EyeData d, Options opt)
        {
            if (d.label != "R") return opt.rollOffset;
            switch (opt.rollMode)
            {
                case RollMirror: return -opt.rollOffset;
                case RollIndependent: return opt.rollRightOffset;
                default: return opt.rollOffset;
            }
        }


        public static void Revert(GameObject avatar)
        {
            if (avatar == null) return;
            var marker = avatar.GetComponent<DocMeEyeInstall>();
            if (marker == null) { Debug.LogWarning($"[DocMe Eyes] Nothing to revert on '{avatar.name}'."); return; }

            Undo.RegisterFullObjectHierarchyUndo(avatar, "Revert DocMe Eyes");

            // Delete the generated placed meshes, but never the user's own mesh and never one another avatar
            // (a duplicate) still uses.
            var generatedMeshes = new HashSet<string>();
            foreach (var c in marker.createdEyes)
            {
                if (c.go == null) continue;
                var mf = c.go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (SourceModel.IsGenerated(p) && !DocMeReapply.IsMeshInUse(mf.sharedMesh, avatar)) generatedMeshes.Add(p);
                }
                Undo.DestroyObjectImmediate(c.go);
            }
            foreach (var p in generatedMeshes) AssetDatabase.DeleteAsset(p);

            foreach (var h in marker.hiddenRenderers)
            {
                if (h.renderer != null)
                {
                    var restore = h.originalMesh != null ? h.originalMesh : SourceModel.Resolve(h.sourcePath, h.sourceMeshName, h.renderer.name);
                    if (restore != null) h.renderer.sharedMesh = restore;
                }
                if (h.renderer != null && h.originalMaterials != null)
                    h.renderer.sharedMaterials = h.originalMaterials; // reshade mode appended a material

                if (h.modifiedMesh != null && !DocMeReapply.IsMeshInUse(h.modifiedMesh, avatar))
                {
                    string p = AssetDatabase.GetAssetPath(h.modifiedMesh);
                    if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);
                }
            }

            Undo.DestroyObjectImmediate(marker);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DocMe Eyes] Reverted '{avatar.name}'.");
        }

    }
}
