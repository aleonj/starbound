using System.Collections.Generic;
using UnityEngine;
using StarBound.Map;

namespace StarBound.Demo
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ShipMarkerView : MonoBehaviour
    {
        private static readonly Dictionary<float, Mesh> MeshCache = new();
        private static readonly Dictionary<(Color Color, int HullStyle), Material> MaterialCache = new();

        public void Initialize(float radius, Color color, int hullStyle)
        {
            GetComponent<MeshFilter>().sharedMesh = GetOrCreateMesh(radius);

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetOrCreateMaterial(color, hullStyle);
            meshRenderer.sortingOrder = 2; // above terrain (0) and engagement markers (1)
        }

        // Both caches check `== null` (not just TryGetValue) because a
        // destroyed UnityEngine.Object isn't a real C# null reference —
        // with Domain Reload disabled in Enter Play Mode Settings, these
        // static caches survive Stop/Play but the Mesh/Material objects
        // they hold get destroyed, so a stale-but-non-null entry would
        // otherwise be handed back on the next Play session.
        private static Mesh GetOrCreateMesh(float radius)
        {
            if (!MeshCache.TryGetValue(radius, out var mesh) || mesh == null)
            {
                mesh = HexMeshFactory.CreateFlatTopHex(radius);
                MeshCache[radius] = mesh;
            }

            return mesh;
        }

        private static Material GetOrCreateMaterial(Color color, int hullStyle)
        {
            var key = (color, hullStyle);
            if (!MaterialCache.TryGetValue(key, out var material) || material == null)
            {
                material = new Material(Shader.Find("StarBound/ShipCraft")) { color = color };
                material.SetFloat("_HullStyle", hullStyle);
                MaterialCache[key] = material;
            }

            return material;
        }
    }
}
