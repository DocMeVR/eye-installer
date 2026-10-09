using System.Collections.Generic;
using DocMe.HideIslands;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>
    /// "Deform with face" path: keep the original iris verts (they carry the bone weights and
    /// blendshapes, so they blink/squish with the face) and re-shader them in place: move them
    /// into their own submesh with centred iris UV0 + matching tangents. Vertex count/order are
    /// unchanged, so all other blendshapes/weights stay valid.
    /// </summary>
    public static class EyeMeshReshader
    {
        /// <summary>Returns a mesh copy with the eye verts in a new last submesh carrying centred iris
        /// UVs + tangents, optionally flattened (forward-facing) and scaled. The caller appends the
        /// procedural material to that submesh.</summary>
        public static Mesh Reshade(SkinnedMeshRenderer smr, List<(EyeData eye, List<int> verts)> selections,
                                   bool flatten, float widthFactor, float heightFactor, float rollDeg,
                                   float depthOffset, float verticalOffset, float horizontalOffset,
                                   bool dropBackFaces, out Material[] originalMaterials, int evenShapes = 0)
        {
            originalMaterials = smr.sharedMaterials;
            var src = smr.sharedMesh;
            var work = Object.Instantiate(src);
            work.name = src.name + "_DocMeEyes";

            var verts = work.vertices;
            if (work.normals == null || work.normals.Length != verts.Length) work.RecalculateNormals();
            var normals = work.normals;
            var uvs = new List<Vector2>();
            work.GetUVs(0, uvs);
            if (uvs.Count != verts.Length) { uvs = new List<Vector2>(verts.Length); for (int i = 0; i < verts.Length; i++) uvs.Add(Vector2.zero); }

            // Rest world positions (current pose), so we flatten in WORLD space against the
            // avatar's forward, then map back through each vert's bone: that way the eye is flat
            // and faces straight forward AFTER skinning (the eye bone's rest tilt won't re-tilt it).
            var world = MeshBake.BakeWorldVertices(smr, out var baked);
            var bakedNormals = baked.normals;
            var bakeRot = smr.transform.rotation;
            Object.DestroyImmediate(baked);

            var bw = work.boneWeights;
            var bind = work.bindposes;
            var bones = smr.bones;
            bool canRebind = bw != null && bw.Length == verts.Length && bind != null && bones != null && bind.Length == bones.Length && bones.Length > 0;

            // Per-bone skin matrix (bind-local vertex -> world). The full per-vertex inverse uses
            // the actual weighted blend of these (exact linear-blend skinning), so even 50/50
            // eye+head verts map back correctly and stay perfectly forward-facing.
            var boneMat = canRebind ? new Matrix4x4[bones.Length] : null;
            if (canRebind)
                for (int b = 0; b < bones.Length; b++)
                    if (bones[b] != null) boneMat[b] = bones[b].localToWorldMatrix * bind[b];

            // Optimizers / armature merges can leave null entries in bones[].
            bool Missing(int idx, float weight) => weight != 0f && (idx < 0 || idx >= bones.Length || bones[idx] == null);

            Matrix4x4 SkinMat(int i)
            {
                if (!canRebind) return smr.transform.localToWorldMatrix;
                var w = bw[i];
                if (Missing(w.boneIndex0, w.weight0) || Missing(w.boneIndex1, w.weight1) ||
                    Missing(w.boneIndex2, w.weight2) || Missing(w.boneIndex3, w.weight3))
                    return smr.transform.localToWorldMatrix;
                var m = new Matrix4x4();
                for (int c = 0; c < 4; c++)
                {
                    Vector4 col = w.weight0 != 0f ? boneMat[w.boneIndex0].GetColumn(c) * w.weight0 : Vector4.zero;
                    if (w.weight1 != 0f) col += boneMat[w.boneIndex1].GetColumn(c) * w.weight1;
                    if (w.weight2 != 0f) col += boneMat[w.boneIndex2].GetColumn(c) * w.weight2;
                    if (w.weight3 != 0f) col += boneMat[w.boneIndex3].GetColumn(c) * w.weight3;
                    m.SetColumn(c, col);
                }
                return Mathf.Abs(m.determinant) > 1e-12f ? m : smr.transform.localToWorldMatrix;
            }

            Matrix4x4 InvBind(int i)
            {
                var w = bw[i];
                if (Missing(w.boneIndex0, w.weight0) || Missing(w.boneIndex1, w.weight1) ||
                    Missing(w.boneIndex2, w.weight2) || Missing(w.boneIndex3, w.weight3))
                    return smr.transform.worldToLocalMatrix;

                var m = new Matrix4x4();
                for (int c = 0; c < 4; c++)
                {
                    Vector4 col = w.weight0 != 0f ? boneMat[w.boneIndex0].GetColumn(c) * w.weight0 : Vector4.zero;
                    if (w.weight1 != 0f) col += boneMat[w.boneIndex1].GetColumn(c) * w.weight1;
                    if (w.weight2 != 0f) col += boneMat[w.boneIndex2].GetColumn(c) * w.weight2;
                    if (w.weight3 != 0f) col += boneMat[w.boneIndex3].GetColumn(c) * w.weight3;
                    m.SetColumn(c, col);
                }
                // A degenerate blend would make .inverse a zero matrix (verts -> origin); fall back
                // to plain renderer-local space for that vert instead.
                return Mathf.Abs(m.determinant) > 1e-12f ? m.inverse : smr.transform.worldToLocalMatrix;
            }

            var irisSetBuild = new HashSet<int>();
            var groups = new List<EyeGroup>();
            var irisFwd = new Dictionary<int, Vector3>();   // view axis of the eye each iris vert belongs to

            foreach (var (eye, vlist) in selections)
            {
                if (vlist.Count == 0) continue;

                // World frame: the avatar's forward is the view axis; up is world-up.
                Vector3 fwd = eye.forwardWorld.normalized;
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
                if (up.sqrMagnitude < 1e-4f) up = Vector3.up;
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                if (Mathf.Abs(rollDeg) > 1e-4f)
                {
                    Quaternion q = Quaternion.AngleAxis(rollDeg, fwd);
                    right = q * right; up = q * up;
                }

                Vector3 cen = Vector3.zero;
                foreach (int i in vlist) cen += world[i];
                cen /= vlist.Count;

                float r = 1e-4f;
                foreach (int i in vlist)
                {
                    Vector3 rel = world[i] - cen;
                    float u = Vector3.Dot(rel, right), v = Vector3.Dot(rel, up);
                    r = Mathf.Max(r, Mathf.Sqrt(u * u + v * v));
                    irisFwd[i] = fwd;
                }
                float diam = 2f * r;
                var group = new EyeGroup { verts = vlist, right = right, up = up, fwd = fwd };
                groups.Add(group);

                foreach (int i in vlist)
                {
                    Vector3 rel = world[i] - cen;
                    float u = Vector3.Dot(rel, right), v = Vector3.Dot(rel, up), depth = Vector3.Dot(rel, fwd);

                    // UV from the UNSCALED in-plane coords, so the iris always fills [0,1].
                    uvs[i] = new Vector2(u / diam + 0.5f, v / diam + 0.5f);

                    // Flatten (drop depth) + scale, in WORLD, then map back to bind-local.
                    float keepDepth = flatten ? 0f : depth;
                    Vector3 flatWorld = cen + right * (u * widthFactor) + up * (v * heightFactor) + fwd * keepDepth
                                      + fwd * depthOffset + up * verticalOffset + eye.outwardWorld * horizontalOffset;

                    if (canRebind)
                    {
                        var minv = InvBind(i);
                        verts[i] = minv.MultiplyPoint3x4(flatWorld);
                        if (flatten && i < normals.Length) normals[i] = minv.MultiplyVector(fwd).normalized;
                    }
                    else
                    {
                        verts[i] = smr.transform.InverseTransformPoint(flatWorld);
                        if (flatten && i < normals.Length) normals[i] = smr.transform.InverseTransformDirection(fwd).normalized;
                    }
                    irisSetBuild.Add(i);
                    group.rest[i] = flatWorld;
                    group.skin[i] = SkinMat(i);
                }
            }

            work.vertices = verts;
            if (evenShapes > 0) EvenOutBlendShapes(work, groups, evenShapes == 2);
            if (normals.Length == verts.Length) work.normals = normals;
            work.SetUVs(0, uvs);

            bool hasBakedNormals = bakedNormals != null && bakedNormals.Length == world.Length;
            bool FacesAway(int a, int b, int c)
            {
                Vector3 n = hasBakedNormals
                    ? bakeRot * (bakedNormals[a] + bakedNormals[b] + bakedNormals[c])
                    : Vector3.Cross(world[b] - world[a], world[c] - world[a]);
                return Vector3.Dot(n, irisFwd[a]) < 0f;
            }

            var irisSet = irisSetBuild;
            int subCount = work.subMeshCount;
            var keep = new List<int>[subCount];
            for (int s = 0; s < subCount; s++) keep[s] = new List<int>();
            var irisTris = new List<int>();

            for (int s = 0; s < subCount; s++)
            {
                var tris = work.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (irisSet.Contains(a) && irisSet.Contains(b) && irisSet.Contains(c))
                    {
                        // Judge by facing, not depth: a concave dish is deeper in the middle but has no back half.
                        if (dropBackFaces && flatten && FacesAway(a, b, c)) continue;

                        irisTris.Add(a); irisTris.Add(b); irisTris.Add(c);
                    }
                    else
                    { keep[s].Add(a); keep[s].Add(b); keep[s].Add(c); }
                }
            }

            work.subMeshCount = subCount + 1;
            for (int s = 0; s < subCount; s++) work.SetTriangles(keep[s], s);
            work.SetTriangles(irisTris, subCount);

            ComputeTangents(work, uvs, irisTris, irisSet);

            work.RecalculateBounds();
            return work;
        }

        /// <summary>Standard per-vertex tangents from UV0, computed only over the iris triangles so
        /// the rest of the body's tangents stay exactly as imported.</summary>
        class EyeGroup
        {
            public List<int> verts;
            public Vector3 right, up, fwd;
            public Dictionary<int, Vector3> rest = new Dictionary<int, Vector3>();      // flattened world position
            public Dictionary<int, Matrix4x4> skin = new Dictionary<int, Matrix4x4>();  // bind-local -> world
        }

        /// <summary>
        /// Rewrites every blendshape so it moves each eye as one rigid sheet: a translation plus a
        /// horizontal and a vertical scale about the eye centre, fitted to what the shape did to the
        /// original verts. A shape that only pinched a corner would otherwise warp the flat iris,
        /// and the parallax reads that warp as the eye bending.
        ///   useMax  scale = ratio of the widest extent (a partial pinch counts in full), else a
        ///           least-squares fit over all iris verts (a partial pinch is averaged down).
        /// </summary>
        static void EvenOutBlendShapes(Mesh work, List<EyeGroup> groups, bool useMax)
        {
            int n = work.vertexCount, count = work.blendShapeCount;
            if (count == 0 || groups.Count == 0) return;

            var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
            var shapes = new List<(string name, List<(float w, Vector3[] v, Vector3[] nrm, Vector3[] tan)> frames)>();

            for (int s = 0; s < count; s++)
            {
                var frames = new List<(float, Vector3[], Vector3[], Vector3[])>();
                for (int f = 0; f < work.GetBlendShapeFrameCount(s); f++)
                {
                    work.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    bool anyN = false, anyT = false;
                    for (int i = 0; i < n && !(anyN && anyT); i++) { anyN |= dn[i] != Vector3.zero; anyT |= dt[i] != Vector3.zero; }

                    foreach (var g in groups) EvenOut(g, dv, dn, dt, useMax);

                    frames.Add((work.GetBlendShapeFrameWeight(s, f), (Vector3[])dv.Clone(), anyN ? (Vector3[])dn.Clone() : null, anyT ? (Vector3[])dt.Clone() : null));
                }
                shapes.Add((work.GetBlendShapeName(s), frames));
            }

            work.ClearBlendShapes();
            foreach (var (name, frames) in shapes)
                foreach (var (w, v, nrm, tan) in frames)
                    work.AddBlendShapeFrame(name, w, v, nrm, tan);
        }

        static void EvenOut(EyeGroup g, Vector3[] dv, Vector3[] dn, Vector3[] dt, bool useMax)
        {
            var verts = g.verts;
            Vector3 cen = Vector3.zero;
            foreach (int i in verts) cen += g.rest[i];
            cen /= verts.Count;

            // Only the verts the shape actually moves say what it does. A pupil-dilation shape moves a
            // small inner ring; averaging that over the whole iris would erase it.
            var moved = new List<int>();
            var wd = new Dictionary<int, Vector3>(verts.Count);
            float rMax = 0f;
            foreach (int i in verts)
            {
                rMax = Mathf.Max(rMax, (g.rest[i] - cen).magnitude);
                if (dv[i].sqrMagnitude < 1e-12f) continue;
                moved.Add(i);
                wd[i] = g.skin[i].MultiplyVector(dv[i]);
            }
            if (moved.Count == 0) return;

            // Scale per axis from how far each moved vert sits from the centre before vs after.
            //   Mean: least squares over the moved verts (shift falls out as the leftover).
            //   Max:  the mean motion is taken off first, then the most extreme ratio wins, so a
            //         pupil ring that shrinks 20% shrinks the whole eye 20%.
            // Verts near the centre can't tell scale from shift, so they don't vote on the ratio.
            float minArm = rMax * 0.15f;
            Vector3 t0 = Vector3.zero;
            if (useMax) { foreach (int i in moved) t0 += wd[i]; t0 /= moved.Count; }

            float suNum = 0f, suDen = 0f, svNum = 0f, svDen = 0f, suExt = 1f, svExt = 1f;
            foreach (int i in moved)
            {
                Vector3 rel = g.rest[i] - cen, d = wd[i] - t0;
                float u = Vector3.Dot(rel, g.right), v = Vector3.Dot(rel, g.up);
                float du = Vector3.Dot(d, g.right), dvv = Vector3.Dot(d, g.up);
                if (Mathf.Abs(u) > minArm)
                {
                    suNum += u * (u + du); suDen += u * u;
                    float k = (u + du) / u; if (k > 0f && Mathf.Abs(Mathf.Log(k)) > Mathf.Abs(Mathf.Log(suExt))) suExt = k;
                }
                if (Mathf.Abs(v) > minArm)
                {
                    svNum += v * (v + dvv); svDen += v * v;
                    float k = (v + dvv) / v; if (k > 0f && Mathf.Abs(Mathf.Log(k)) > Mathf.Abs(Mathf.Log(svExt))) svExt = k;
                }
            }
            float su = useMax ? suExt : (suDen > 1e-12f ? suNum / suDen : 1f);
            float sv = useMax ? svExt : (svDen > 1e-12f ? svNum / svDen : 1f);

            // Shift = what the moved verts did beyond that scale.
            Vector3 t = Vector3.zero;
            foreach (int i in moved)
            {
                Vector3 rel = g.rest[i] - cen;
                t += wd[i] - g.right * (Vector3.Dot(rel, g.right) * (su - 1f)) - g.up * (Vector3.Dot(rel, g.up) * (sv - 1f));
            }
            t /= moved.Count;

            foreach (int i in verts)
            {
                Vector3 rel = g.rest[i] - cen;
                Vector3 nw = t + g.right * (Vector3.Dot(rel, g.right) * (su - 1f)) + g.up * (Vector3.Dot(rel, g.up) * (sv - 1f));
                dv[i] = g.skin[i].inverse.MultiplyVector(nw);
                dn[i] = Vector3.zero;   // the sheet stays flat: no normal / tangent change
                dt[i] = Vector3.zero;
            }
        }

        static void ComputeTangents(Mesh work, List<Vector2> uvs, List<int> irisTris, HashSet<int> irisSet)
        {
            var verts = work.vertices;
            var normals = work.normals;
            var tangents = work.tangents;
            if (tangents == null || tangents.Length != verts.Length)
            {
                // The source mesh has no tangent stream, but the parallax eye needs one (it's a
                // whole-mesh stream, so we can't add it for the eye only). Derive valid tangents
                // for every vertex from UV0 + normals: zero-filling instead would give the rest of
                // the body a degenerate TBN and break shaders like Poiyomi.
                work.RecalculateTangents();
                tangents = work.tangents;
                if (tangents == null || tangents.Length != verts.Length) tangents = new Vector4[verts.Length];
            }

            var tan = new Vector3[verts.Length];
            var bit = new Vector3[verts.Length];

            for (int t = 0; t < irisTris.Count; t += 3)
            {
                int i0 = irisTris[t], i1 = irisTris[t + 1], i2 = irisTris[t + 2];
                Vector3 p0 = verts[i0], p1 = verts[i1], p2 = verts[i2];
                Vector2 w0 = uvs[i0], w1 = uvs[i1], w2 = uvs[i2];

                Vector3 e1 = p1 - p0, e2 = p2 - p0;
                Vector2 du1 = w1 - w0, du2 = w2 - w0;
                float denom = du1.x * du2.y - du2.x * du1.y;
                if (Mathf.Abs(denom) < 1e-12f) continue;
                float r = 1f / denom;
                Vector3 sdir = (e1 * du2.y - e2 * du1.y) * r;
                Vector3 tdir = (e2 * du1.x - e1 * du2.x) * r;

                tan[i0] += sdir; tan[i1] += sdir; tan[i2] += sdir;
                bit[i0] += tdir; bit[i1] += tdir; bit[i2] += tdir;
            }

            foreach (int i in irisSet)
            {
                Vector3 n = normals != null && i < normals.Length ? normals[i] : Vector3.forward;
                Vector3 t = tan[i];
                t = (t - n * Vector3.Dot(n, t)); // Gram-Schmidt
                if (t.sqrMagnitude < 1e-12f) t = Vector3.Cross(n, Vector3.up);
                t.Normalize();
                float w = Vector3.Dot(Vector3.Cross(n, t), bit[i]) < 0f ? -1f : 1f;
                tangents[i] = new Vector4(t.x, t.y, t.z, w);
            }

            work.tangents = tangents;
        }
    }
}
