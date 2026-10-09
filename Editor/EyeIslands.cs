using System.Collections.Generic;
using DocMe.HideIslands;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Finds the eye detail islands around a detected eye without a click.</summary>
    public static class EyeIslands
    {
        /// <summary>Eye detail verts to collapse: small islands fully contained near the eye (so the
        /// spanning lash line survives), taken whole. Outputs the average forward depth.</summary>
        public static List<int> SelectEyeVerts(SkinnedMeshRenderer smr, Vector3 centerWorld,
                                               Vector3 forward, float footprintRadius, out float irisDepth)
        {
            irisDepth = 0f;
            var result = new List<int>();
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) return result;

            var world = MeshBake.BakeWorldVertices(smr, out var baked);
            Object.DestroyImmediate(baked);

            float containR = footprintRadius * 1.6f; // an island must fit entirely within this

            bool any = false;
            for (int i = 0; i < world.Length && !any; i++)
                if ((world[i] - centerWorld).sqrMagnitude < containR * containR) any = true;
            if (!any) return result;

            MeshIslands.ConnectedComponents(mesh, world, out int[] compId, out List<int> compSize);

            // Candidates: components with a vertex inside the footprint and small enough to be a detail.
            var candidates = new HashSet<int>();
            for (int i = 0; i < world.Length; i++)
            {
                if (compSize[compId[i]] >= MeshIslands.FaceIslandMin) continue;
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
    }
}
