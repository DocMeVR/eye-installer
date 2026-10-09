using System.Collections.Generic;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>A Scene-view pick stored by position (avatar-local) instead of vertex index, so it
    /// survives a reimport that renumbers the mesh.</summary>
    [System.Serializable]
    public class DocMePick
    {
        public string label;                 // "L" / "R" for eye picks, empty for hide picks
        public SkinnedMeshRenderer renderer;
        public Vector3 avatarLocalPoint;
    }

    /// <summary>Everything needed to run the eye install again from scratch.</summary>
    [System.Serializable]
    public class DocMeEyeRecipe
    {
        public Material material;
        public Mesh eyeMesh;
        public bool hideOriginal = true, inSocket = true, deformWithFace, flattenEye = true;
        public bool dropBackFaces = true, sinkIntoHead = true, deleteOriginal;
        public float widthFactor = 1.3f, heightFactor = 1.3f, footprintScale = 0.95f;
        public float depthOffset, verticalOffset, horizontalOffset, rollOffset;
        public int evenEyeShapes;
        public List<SkinnedMeshRenderer> eyeRenderers = new List<SkinnedMeshRenderer>();
        public List<DocMePick> picks = new List<DocMePick>();
    }
}
