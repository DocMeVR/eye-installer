using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocMe.HideIslands;
using UnityEditor;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Rebuilds an eye install, and the hidden parts on top of it, on the current source model, and
    /// tells the hide tool which vertices are the new eye.</summary>
    class EyeReapplyHandler : IReapplyHandler
    {
        [InitializeOnLoadMethod]
        static void Init()
        {
            DocMeReapply.Register(new EyeReapplyHandler());
            HideIslandsCore.ProtectedVertices.Add(NewEyeVertices);
        }

        static IEnumerable<int> NewEyeVertices(GameObject avatar, SkinnedMeshRenderer smr, Mesh baseMesh)
        {
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            if (eye == null) return null;
            foreach (var h in eye.hiddenRenderers)
                if (h.reshaded && h.renderer == smr && h.modifiedMesh != null && h.modifiedMesh == baseMesh)
                    return h.modifiedMesh.GetTriangles(h.modifiedMesh.subMeshCount - 1);
            return null;
        }

        public int Order => 0;
        public bool Claims(GameObject avatar) => avatar != null && avatar.GetComponent<DocMeEyeInstall>() != null;
        public IEnumerable<GameObject> FindInstalled() => Object.FindObjectsOfType<DocMeEyeInstall>(true).Select(m => m.gameObject);

        public IEnumerable<string> SourcePaths(GameObject avatar)
        {
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            if (eye != null) foreach (var h in eye.hiddenRenderers) yield return h.sourcePath;
        }

        public bool IsStale(GameObject avatar)
        {
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            return eye != null && !string.IsNullOrEmpty(eye.sourceHash) &&
                   eye.sourceHash != SourceModel.Hash(eye.hiddenRenderers.Select(h => h.sourcePath));
        }

        public bool TryGetSource(GameObject avatar, Mesh generated, out string path, out string meshName)
        {
            path = meshName = null;
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            if (eye == null) return false;
            foreach (var h in eye.hiddenRenderers)
                if (h.modifiedMesh == generated && !string.IsNullOrEmpty(h.sourcePath)) { path = h.sourcePath; meshName = h.sourceMeshName; return true; }
            return false;
        }

        public bool ReferencesMesh(GameObject avatar, Mesh mesh)
        {
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            if (eye == null) return false;
            foreach (var h in eye.hiddenRenderers) if (h.modifiedMesh == mesh || h.originalMesh == mesh) return true;
            foreach (var c in eye.createdEyes)
            {
                var mf = c.go != null ? c.go.GetComponent<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh == mesh) return true;
            }
            return false;
        }

        public bool HasMissingMesh(GameObject avatar)
        {
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            if (eye == null) return false;
            foreach (var h in eye.hiddenRenderers) if (h.modifiedMesh == null || (h.renderer != null && h.renderer.sharedMesh == null)) return true;
            foreach (var c in eye.createdEyes)
            {
                var mf = c.go != null ? c.go.GetComponent<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh == null) return true;
            }
            return false;
        }

        /// <summary>Eyes and hidden parts come off and go back on in the order they were applied.</summary>
        public string Reapply(GameObject avatar)
        {
            var sb = new StringBuilder();
            var eye = avatar.GetComponent<DocMeEyeInstall>();
            var hide = avatar.GetComponent<DocMeIslandHide>();
            if (eye == null && hide == null) return "Nothing installed.";
            if ((eye != null && eye.appliedAt == 0) || (hide != null && hide.appliedAt == 0))
                return "⚠ Installed with an older version that didn't record its settings. Revert and install once more; after that it re-applies by itself.";

            var eyeRecipe = eye != null ? eye.recipe : null;
            long eyeAt = eye != null ? eye.appliedAt : 0;
            var hidePicks = hide != null ? new List<DocMePick>(hide.picks) : null;
            bool hideDelete = hide != null && hide.deleteGeometry, hideSink = hide == null || hide.sinkIntoHead;
            long hideAt = hide != null ? hide.appliedAt : 0;

            HideIslandsCore.Revert(avatar);
            EyeInstallerCore.Revert(avatar);

            void Eyes()
            {
                if (eyeRecipe == null) return;
                var eyes = EyeAvatarDetector.Detect(avatar, out _);
                var cache = new SourceModel.BakeCache();
                int lost = 0;
                foreach (var p in eyeRecipe.picks)
                {
                    var d = eyes.Find(x => x.label == p.label);
                    if (d != null && SourceModel.TryResolve(avatar, p, cache, out var seed)) d.seeds.Add(seed);
                    else lost++;
                }
                EyeInstallerCore.Install(avatar, eyes, EyeInstallerCore.FromRecipe(eyeRecipe));
                sb.AppendLine(avatar.GetComponent<DocMeEyeInstall>() != null ? "Eyes re-applied." : "⚠ Eyes could not be re-applied.");
                if (lost > 0) sb.AppendLine($"⚠ {lost} eye {(lost == 1 ? "pick" : "picks")} no longer match the mesh and were dropped (auto detection used where none were left).");
            }

            void Hide()
            {
                if (hidePicks == null || hidePicks.Count == 0) return;
                sb.AppendLine(HideIslandsCore.Reapply(avatar, hidePicks, hideDelete, hideSink));
            }

            if (hideAt != 0 && hideAt < eyeAt) { Hide(); Eyes(); } else { Eyes(); Hide(); }
            return sb.ToString().TrimEnd();
        }
    }
}
