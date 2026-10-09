using UnityEngine;

namespace DocMe.EyeInstaller
{
    /// <summary>Flat quad for the parallax shader: centred iris UV0 (pupil at 0.5), normal +Z,
    /// tangent +X. Verts span ±0.5 so a uniform scale of S makes the eye S wide.</summary>
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
            m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            var tan = new Vector4(1f, 0f, 0f, -1f);
            m.tangents = new[] { tan, tan, tan, tan };
            m.triangles = new[] { 0, 1, 2, 2, 1, 3 }; // clockwise seen from +Z, so Cull Back eye shaders render the front
            m.RecalculateBounds();
            return m;
        }
    }
}
