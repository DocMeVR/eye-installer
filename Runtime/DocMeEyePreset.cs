using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Saved installer settings, reusable across avatars that share a base.
    /// Scene references (avatar, eye renderer) and Scene-view picks are intentionally not stored.</summary>
    [CreateAssetMenu(fileName = "DocMe Eye Preset", menuName = "DocMe/Eye Installer Preset")]
    public class DocMeEyePreset : ScriptableObject
    {
        public Material material;
        public Mesh eyeMesh;

        public bool hideOriginal = true;
        public bool inSocket = true;
        public bool deformWithFace = false;
        public bool flattenEye = true;

        public bool lockAspect = true;
        public float widthFactor = 1.3f;
        public float heightFactor = 1.3f;
        public float depthOffset = 0f;
        public float verticalOffset = 0f;
        public float horizontalOffset = 0f;
        public float rollOffset = 0f;
    }
}
