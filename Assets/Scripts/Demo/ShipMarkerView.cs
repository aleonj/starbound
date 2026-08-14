using System.Collections.Generic;
using UnityEngine;
using StarBound.Map;

namespace StarBound.Demo
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ShipMarkerView : MonoBehaviour
    {
        private static readonly Dictionary<float, Mesh> MeshCache = new();
        private static readonly Dictionary<Color, Material> MaterialCache = new();

        public void Initialize(float radius, Color color)
        {
            GetComponent<MeshFilter>().sharedMesh = GetOrCreateMesh(radius);

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetOrCreateMaterial(color);
            meshRenderer.sortingOrder = 2; // above terrain (0) and engagement markers (1)
        }

        private static Mesh GetOrCreateMesh(float radius)
        {
            if (!MeshCache.TryGetValue(radius, out var mesh))
            {
                mesh = HexMeshFactory.CreateFlatTopHex(radius);
                MeshCache[radius] = mesh;
            }

            return mesh;
        }

        private static Material GetOrCreateMaterial(Color color)
        {
            if (!MaterialCache.TryGetValue(color, out var material))
            {
                material = new Material(Shader.Find("Sprites/Default")) { color = color, mainTexture = Texture2D.whiteTexture };
                MaterialCache[color] = material;
            }

            return material;
        }
    }
}
