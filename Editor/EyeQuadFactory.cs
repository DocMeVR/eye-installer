using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Flat quad for the parallax shader, laid out like Unity's built-in Quad: the front faces -Z, UV0 runs
    /// +X and +Y with the pupil at 0.5, tangent +X with w = -1 so the shaders' bitangent runs +Y with v. Seen from
    /// its front the picture reads like the shader preview. Verts span ±0.5, so a uniform scale of S makes the eye S wide.</summary>
    public static class EyeQuadFactory
    {
        public static Mesh Create(string name = "DocMe_EyeQuad")
        {
            var m = new Mesh { name = name };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var tan = new Vector4(1f, 0f, 0f, -1f);
            m.tangents = new[] { tan, tan, tan, tan };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 }; // clockwise seen from -Z, so Cull Back eye shaders draw the front
            m.RecalculateBounds();
            return m;
        }
    }
}
