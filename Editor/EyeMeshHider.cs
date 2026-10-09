using System.Collections.Generic;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>
    /// Isolates the visible eye verts on a skinned mesh by mesh island, not bone weight: anime
    /// eyes are small topological islands (iris/pupil/highlights) separate from the one big head
    /// island. We take the small islands inside the iris footprint, which is weight-agnostic and
    /// leaves the face skin untouched.
    /// </summary>
    public static class EyeMeshHider
    {
        // A connected island this big or bigger is the head/body: never collapse it.
        const int FaceIslandMin = 2000;

        /// <summary>
        /// Appends a blendshape that collapses each island to a single point, so its tris go
        /// degenerate (zero-area, invisible). The point is also sunk *below the surface*: a speck
        /// left exactly on the skin can still show through as a dot or z-fight, so we bury it.
        ///
        /// Sink direction is the island's averaged normal (works for any island on any mesh). When
        /// that cancels out: a full eyeball sphere, say , <paramref name="inwardLocal"/> is used
        /// instead; pass Vector3.zero if there's no better guess and the collapse stays in place.
        /// </summary>
        /// <summary>
        /// Removes the islands' triangles outright. Nothing is left to render, so: unlike the
        /// blendshape collapse: no other (additive) blendshape can drag the geometry back into
        /// view. Vertex count and order are untouched, so every blendshape and bone weight stays
        /// valid; the now-unreferenced vertices simply never rasterise.
        /// </summary>
        public static bool RemoveIslandTriangles(Mesh work, List<List<int>> islands)
        {
            var drop = new HashSet<int>();
            foreach (var idx in islands) if (idx != null) drop.UnionWith(idx);
            if (drop.Count == 0) return false;

            bool removedAny = false;
            for (int s = 0; s < work.subMeshCount; s++)
            {
                var tris = work.GetTriangles(s);
                var keep = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    // Whole-triangle test: islands are connected components, so their tris have all
                    // three corners inside. Never nibbles neighbouring geometry.
                    if (drop.Contains(a) && drop.Contains(b) && drop.Contains(c)) { removedAny = true; continue; }
                    keep.Add(a); keep.Add(b); keep.Add(c);
                }
                if (keep.Count != tris.Length) work.SetTriangles(keep, s);
            }
            return removedAny;
        }

        /// <summary>The bone's origin expressed in the mesh's bind space: the same space
        /// <c>mesh.vertices</c> (and therefore blendshape deltas) live in.</summary>
        public static bool TryGetBoneBindPosition(SkinnedMeshRenderer smr, Transform bone, out Vector3 bindPos)
        {
            bindPos = default;
            if (smr == null || bone == null || smr.sharedMesh == null) return false;
            int i = System.Array.IndexOf(smr.bones, bone);
            var binds = smr.sharedMesh.bindposes;
            if (i < 0 || binds == null || i >= binds.Length) return false;
            bindPos = binds[i].inverse.MultiplyPoint3x4(Vector3.zero);
            return true;
        }

        public static bool AddHideShape(Mesh work, List<List<int>> islands, string shapeName,
                                        Vector3 inwardLocal = default, float sinkFactor = 2f,
                                        Vector3? sinkTargetLocal = null)
        {
            var verts = work.vertices;
            var normals = work.normals;
            bool hasNormals = normals != null && normals.Length == verts.Length;
            var deltas = new Vector3[verts.Length];
            bool any = false;

            foreach (var idx in islands)
            {
                if (idx == null || idx.Count == 0) continue;

                Vector3 c = Vector3.zero;
                foreach (int i in idx) c += verts[i];
                c /= idx.Count;

                float radius = 0f;
                Vector3 outward = Vector3.zero;
                foreach (int i in idx)
                {
                    radius = Mathf.Max(radius, (verts[i] - c).magnitude);
                    if (hasNormals) outward += normals[i];
                }

                Vector3 target;
                if (sinkTargetLocal.HasValue)
                {
                    target = sinkTargetLocal.Value; // e.g. the head bone: definitively inside
                }
                else
                {
                    Vector3 inward = outward.sqrMagnitude > 1e-6f ? -outward.normalized
                                   : inwardLocal.sqrMagnitude > 1e-6f ? inwardLocal.normalized
                                   : Vector3.zero;
                    target = c + inward * Mathf.Max(radius * sinkFactor, 0.02f);
                }
                foreach (int i in idx) deltas[i] = target - verts[i];
                any = true;
            }

            if (any) work.AddBlendShapeFrame(shapeName, 100f, deltas, null, null);
            return any;
        }

        /// <summary>Eye-detail verts to collapse: small islands FULLY contained near the eye (so the
        /// spanning lash line survives), taken whole. Outputs the average forward depth.</summary>
        public static List<int> SelectEyeVerts(SkinnedMeshRenderer smr, Vector3 centerWorld,
                                               Vector3 forward, float footprintRadius, out float irisDepth)
        {
            irisDepth = 0f;
            var result = new List<int>();
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) return result;

            var world = EyeMeshUtil.BakeWorldVertices(smr, out var baked);
            Object.DestroyImmediate(baked);

            float containR = footprintRadius * 1.6f; // an island must fit entirely within this

            // Quick reject: any geometry near the eye at all?
            bool any = false;
            for (int i = 0; i < world.Length && !any; i++)
                if ((world[i] - centerWorld).sqrMagnitude < containR * containR) any = true;
            if (!any) return result;

            ConnectedComponents(mesh, world, out int[] compId, out List<int> compSize);

            // Candidate components = those with a vert inside the footprint and small enough.
            var candidates = new HashSet<int>();
            for (int i = 0; i < world.Length; i++)
            {
                if (compSize[compId[i]] >= FaceIslandMin) continue;
                Vector3 w = world[i] - centerWorld;
                float depth = Vector3.Dot(w, forward);
                Vector3 planar = w - depth * forward;
                if (planar.magnitude < footprintRadius && depth > -0.005f) candidates.Add(compId[i]);
            }

            // Keep only candidates fully contained near the eye (excludes the spanning lash line).
            var maxDist = new Dictionary<int, float>();
            for (int i = 0; i < world.Length; i++)
            {
                int id = compId[i];
                if (!candidates.Contains(id)) continue;
                float dd = (world[i] - centerWorld).magnitude;
                if (!maxDist.TryGetValue(id, out var m) || dd > m) maxDist[id] = dd;
            }
            var take = new HashSet<int>();
            foreach (var kv in maxDist) if (kv.Value <= containR) take.Add(kv.Key);

            float depthSum = 0f;
            for (int i = 0; i < world.Length; i++)
            {
                if (!take.Contains(compId[i])) continue;
                result.Add(i);
                depthSum += Vector3.Dot(world[i] - centerWorld, forward);
            }
            if (result.Count > 0) irisDepth = depthSum / result.Count;
            return result;
        }

        /// <summary>Flood-fill the connected island containing a clicked vertex. Outputs the
        /// island's own radius and forward depth. Returns empty if the flood exceeds the face
        /// threshold (the click missed a small island).</summary>
        public static List<int> SelectIslandFromSeed(SkinnedMeshRenderer smr, int seedVertex,
                                                     Vector3 centerWorld, Vector3 forward,
                                                     out float radius, out float irisDepth)
        {
            radius = 0f; irisDepth = 0f;
            var result = new List<int>();
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable || seedVertex < 0 || seedVertex >= mesh.vertexCount) return result;

            var world = EyeMeshUtil.BakeWorldVertices(smr, out var baked);
            Object.DestroyImmediate(baked);

            var adj = BuildWeldedAdjacency(mesh, world);
            var seen = new HashSet<int> { seedVertex };
            var q = new Queue<int>(); q.Enqueue(seedVertex);
            while (q.Count > 0)
            {
                int v = q.Dequeue(); result.Add(v);
                if (result.Count > FaceIslandMin) { result.Clear(); return result; } // hit the face
                if (adj.TryGetValue(v, out var ns)) foreach (int m in ns) if (seen.Add(m)) q.Enqueue(m);
            }

            // Radius from the island's OWN centroid, so 'biggest island' gives the true iris size.
            Vector3 cen = Vector3.zero;
            foreach (int i in result) cen += world[i];
            cen /= result.Count;

            float depthSum = 0f, maxPlanar = 0f;
            foreach (int i in result)
            {
                depthSum += Vector3.Dot(world[i] - centerWorld, forward); // depth from the eye centre
                Vector3 w = world[i] - cen;
                w -= Vector3.Project(w, forward);                          // planar from the island centroid
                if (w.magnitude > maxPlanar) maxPlanar = w.magnitude;
            }
            irisDepth = depthSum / result.Count;
            radius = maxPlanar;
            return result;
        }

        /// <summary>Connected components (triangle edges + welded coincident positions so UV seams
        /// don't split an island). Outputs per-vert component id and each component's size.</summary>
        /// <summary>Splits a vert selection into its connected islands, largest first.</summary>
        public static List<List<int>> SplitIslands(SkinnedMeshRenderer smr, List<int> verts)
        {
            var result = new List<List<int>>();
            if (smr == null || smr.sharedMesh == null || verts == null || verts.Count == 0) return result;

            var world = EyeMeshUtil.BakeWorldVertices(smr, out var baked);
            Object.DestroyImmediate(baked);
            var adj = BuildWeldedAdjacency(smr.sharedMesh, world);

            var left = new HashSet<int>(verts);
            var q = new Queue<int>();
            foreach (int s in verts)
            {
                if (!left.Remove(s)) continue;
                var island = new List<int>();
                q.Enqueue(s);
                while (q.Count > 0)
                {
                    int v = q.Dequeue(); island.Add(v);
                    if (adj.TryGetValue(v, out var ns)) foreach (int m in ns) if (left.Remove(m)) q.Enqueue(m);
                }
                result.Add(island);
            }
            result.Sort((a, b) => b.Count.CompareTo(a.Count));
            return result;
        }

        static void ConnectedComponents(Mesh mesh, Vector3[] world, out int[] compId, out List<int> sizes)
        {
            int n = world.Length;
            var adj = BuildWeldedAdjacency(mesh, world);

            compId = new int[n];
            for (int i = 0; i < n; i++) compId[i] = -1;
            sizes = new List<int>();
            var q = new Queue<int>();
            for (int s = 0; s < n; s++)
            {
                if (compId[s] >= 0) continue;
                int id = sizes.Count, count = 0;
                compId[s] = id; q.Enqueue(s);
                while (q.Count > 0)
                {
                    int v = q.Dequeue(); count++;
                    if (adj.TryGetValue(v, out var ns))
                        foreach (int m in ns) if (compId[m] < 0) { compId[m] = id; q.Enqueue(m); }
                }
                sizes.Add(count);
            }
        }

        /// <summary>Vertex adjacency from triangle edges, with coincident positions welded so a
        /// UV seam doesn't split an island into two.</summary>
        static Dictionary<int, List<int>> BuildWeldedAdjacency(Mesh mesh, Vector3[] world)
        {
            int n = world.Length;
            var tris = mesh.triangles;
            var adj = new Dictionary<int, List<int>>(n);
            void Link(int a, int b)
            {
                if (!adj.TryGetValue(a, out var l)) { l = new List<int>(); adj[a] = l; }
                if (!l.Contains(b)) l.Add(b);
            }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Link(a, b); Link(b, a); Link(b, c); Link(c, b); Link(a, c); Link(c, a);
            }

            var grid = new Dictionary<Vector3Int, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(world[i].x * 10000f),
                                       Mathf.RoundToInt(world[i].y * 10000f),
                                       Mathf.RoundToInt(world[i].z * 10000f));
                if (!grid.TryGetValue(k, out var l)) { l = new List<int>(); grid[k] = l; }
                l.Add(i);
            }
            foreach (var kv in grid)
            {
                var l = kv.Value;
                for (int a = 0; a < l.Count; a++)
                    for (int b = a + 1; b < l.Count; b++) { Link(l[a], l[b]); Link(l[b], l[a]); }
            }
            return adj;
        }
    }
}
