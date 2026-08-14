using System.Collections.Generic;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class HexTileView : MonoBehaviour
    {
        private static readonly Dictionary<float, Mesh> HexMeshCache = new();
        private static readonly Dictionary<float, Mesh> MarkerMeshCache = new();
        private static readonly Dictionary<float, Mesh> HighlightMeshCache = new();

        public void Initialize(TerrainType terrain, EngagementTier tier, float hexRadius, bool isHighlighted = false)
        {
            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = GetOrCreate(HexMeshCache, hexRadius * 0.95f);

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = TerrainMaterials.Get(terrain);
            meshRenderer.sortingOrder = 0;

            if (isHighlighted)
            {
                // Larger and drawn behind the tile, so only its edge shows —
                // reads as a glowing border rather than obscuring the tile.
                var highlight = new GameObject("Highlight", typeof(MeshFilter), typeof(MeshRenderer));
                highlight.transform.SetParent(transform, false);
                highlight.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(HighlightMeshCache, hexRadius * 1.15f);

                var highlightRenderer = highlight.GetComponent<MeshRenderer>();
                highlightRenderer.sharedMaterial = HighlightMaterials.TargetHighlight;
                highlightRenderer.sortingOrder = -1;
            }

            if (tier == EngagementTier.None)
                return;

            var marker = new GameObject("EngagementMarker", typeof(MeshFilter), typeof(MeshRenderer));
            marker.transform.SetParent(transform, false);
            marker.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(MarkerMeshCache, hexRadius * 0.35f);

            var markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = EngagementMaterials.Get(tier);
            markerRenderer.sortingOrder = 1;
        }

        private static Mesh GetOrCreate(Dictionary<float, Mesh> cache, float radius)
        {
            if (!cache.TryGetValue(radius, out var mesh))
            {
                mesh = HexMeshFactory.CreateFlatTopHex(radius);
                cache[radius] = mesh;
            }

            return mesh;
        }
    }
}
