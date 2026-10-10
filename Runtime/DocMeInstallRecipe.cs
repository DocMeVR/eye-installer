using System.Collections.Generic;
using DocMe.HideIslands;
using UnityEngine;

namespace DocMe.EyeInstaller
{
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
        public float rollRightOffset, angleOut, angleUp;
        public int rollMode;   // 0 = both eyes roll the same way (older installs), 1 = mirrored, 2 = independent
        public int evenEyeShapes;
        public List<SkinnedMeshRenderer> eyeRenderers = new List<SkinnedMeshRenderer>();
        public List<DocMePick> picks = new List<DocMePick>();
    }
}
