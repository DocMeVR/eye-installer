using System.Collections.Generic;
using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Marker + revert record left on the avatar root by the installer. Implements
    /// VRChat's IEditorOnly so the SDK strips it at build time (no upload warning).</summary>
    [DisallowMultipleComponent]
    public class DocMeEyeInstall : MonoBehaviour
#if VRC_SDK_VRCSDK3
        , VRC.SDKBase.IEditorOnly
#endif
    {
        [System.Serializable]
        public class HiddenRenderer
        {
            public SkinnedMeshRenderer renderer;
            public Mesh originalMesh;            // restored on revert
            public Mesh modifiedMesh;            // our copy, deleted on revert
            public string blendShapeName;
            public bool reshaded;                // true = deform mode (swap mesh to preview originals)
            public Material[] originalMaterials; // reshade mode
            public Material[] modifiedMaterials; // reshade mode (show/hide preview)
            public string sourcePath;            // model file the original mesh comes from
            public string sourceMeshName;
        }

        [System.Serializable]
        public class CreatedEye
        {
            public GameObject go;
            public string label;  // "L" / "R"
            public float depth;
        }

        public GameObject avatar;
        public List<CreatedEye> createdEyes = new List<CreatedEye>();
        public List<HiddenRenderer> hiddenRenderers = new List<HiddenRenderer>();

        // Fit values, stored for a consistent re-fit / re-install.
        public float widthFactor = 1.3f;
        public float heightFactor = 1.3f;
        public float forwardOffset = 0.002f;
        public float verticalOffset = 0f;
        public float eyeRadius = 0f; // unified iris radius measured at install (for consistent re-fit)

        public DocMeEyeRecipe recipe = new DocMeEyeRecipe();
        public long appliedAt;       // ticks; orders re-apply against the hide tool
        public string sourceHash;    // import hash of the source model(s) at install time
    }
}
