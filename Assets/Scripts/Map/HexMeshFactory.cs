using UnityEngine;

namespace StarBound.Map
{
    public static class HexMeshFactory
    {
        private const int Sides = 6;

        // Flat-top hexagon: corners at 0deg, 60deg, ..., 300deg.
        public static Mesh CreateFlatTopHex(float radius)
        {
            var mesh = new Mesh { name = "HexTile" };

            var vertices = new Vector3[Sides + 1];
            // Radius-independent local coordinate for shaders (e.g.
            // GlassHex) — center at (0.5, 0.5), outer vertices on the unit
            // circle, so (uv - 0.5) * 2 gives unit-circle-ish space without
            // needing the actual radius as a separate shader property.
            var uvs = new Vector2[Sides + 1];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < Sides; i++)
            {
                var angle = Mathf.Deg2Rad * (60f * i);
                vertices[i + 1] = new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), 0f);
                uvs[i + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle));
            }

            var triangles = new int[Sides * 3];
            for (var i = 0; i < Sides; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % Sides + 1;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
