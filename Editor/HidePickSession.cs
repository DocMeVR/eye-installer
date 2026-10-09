using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>The click-to-hide workflow shared by the installer's Done page and the standalone
    /// Hide Mesh Islands window: every change re-applies the whole pick list, so undo is one pick fewer.</summary>
    public class HidePickSession
    {
        public enum AddResult { Hidden, IsNewEye, TooBig }

        public GameObject avatar;
        public bool deleteGeometry;
        public bool sinkIntoHead = true;

        readonly List<EyeSeed> picks = new List<EyeSeed>();
        public IReadOnlyList<EyeSeed> Picks => picks;

        /// <summary>Parts hidden on the avatar right now (stored on its marker).</summary>
        public int Count
        {
            get { var h = avatar != null ? avatar.GetComponent<DocMeIslandHide>() : null; return h != null ? h.picks.Count : 0; }
        }

        public AddResult Add(EyeSeed seed)
        {
            if (picks.Count == 0 && Count > 0) Reload();
            picks.Add(seed);
            bool ok = HideIslandsCore.Apply(avatar, picks, deleteGeometry, sinkIntoHead, out _, out _, out int skipped);
            if (Count >= picks.Count) { SceneView.RepaintAll(); return AddResult.Hidden; }

            picks.RemoveAt(picks.Count - 1);
            if (!ok) Reapply();
            SceneView.RepaintAll();
            return skipped > 0 ? AddResult.IsNewEye : AddResult.TooBig;
        }

        public void UndoLast()
        {
            if (picks.Count == 0) { Reload(); if (picks.Count == 0) return; }
            picks.RemoveAt(picks.Count - 1);
            Reapply();
        }

        public void Clear()
        {
            picks.Clear();
            HideIslandsCore.Revert(avatar);
            SceneView.RepaintAll();
        }

        public void Reapply()
        {
            if (picks.Count == 0) HideIslandsCore.Revert(avatar);
            else HideIslandsCore.Apply(avatar, picks, deleteGeometry, sinkIntoHead, out _, out _, out _);
            SceneView.RepaintAll();
        }

        /// <summary>Drop the in-memory list (the avatar changed); what is on the avatar stays.</summary>
        public void Forget() => picks.Clear();

        /// <summary>Picks stored on the avatar, turned back into clickable seeds. Resolved against the mesh
        /// as it was before hiding, since the hidden pieces are gone from the current one.</summary>
        public void Reload()
        {
            picks.Clear();
            var h = avatar != null ? avatar.GetComponent<DocMeIslandHide>() : null;
            if (h == null) return;
            deleteGeometry = h.deleteGeometry;
            sinkIntoHead = h.sinkIntoHead;
            var swapped = new List<(SkinnedMeshRenderer r, Mesh m)>();
            foreach (var e in h.entries)
                if (e.renderer != null && e.originalMesh != null) { swapped.Add((e.renderer, e.renderer.sharedMesh)); e.renderer.sharedMesh = e.originalMesh; }
            try
            {
                var cache = new InstallSource.BakeCache();
                foreach (var p in h.picks)
                    if (InstallSource.TryResolve(avatar, p, cache, out var seed)) picks.Add(seed);
            }
            finally { foreach (var (r, m) in swapped) r.sharedMesh = m; }
        }
    }
}
