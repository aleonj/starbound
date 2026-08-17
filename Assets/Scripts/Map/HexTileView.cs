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

        private MeshRenderer highlightRenderer;
        private MeshRenderer markerRenderer;
        private MaterialPropertyBlock propertyBlock;

        // Called once per tile GameObject, ever — see MapView.Render,
        // which reuses the same HexTileView instance across repeated
        // Render calls instead of destroying/recreating it, so that
        // per-tile animation state (planet rotation, shader time, etc.
        // from future living-galaxy effects) survives a move/turn.
        // Highlight/marker children are always created up front (just
        // inactive) so SetState below never has to create or destroy a
        // GameObject — only toggle/repaint the ones that already exist.
        public void Initialize(TerrainType terrain, float hexRadius)
        {
            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.sharedMesh = GetOrCreate(HexMeshCache, hexRadius * 0.95f);

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = TerrainMaterials.Get(terrain);
            meshRenderer.sortingOrder = 0;

            // Larger and drawn behind the tile, so only its edge shows —
            // reads as a glowing border rather than obscuring the tile.
            var highlight = new GameObject("Highlight", typeof(MeshFilter), typeof(MeshRenderer));
            highlight.transform.SetParent(transform, false);
            highlight.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(HighlightMeshCache, hexRadius * 1.15f);
            highlightRenderer = highlight.GetComponent<MeshRenderer>();
            highlightRenderer.sortingOrder = -1;
            highlight.SetActive(false);

            var marker = new GameObject("EngagementMarker", typeof(MeshFilter), typeof(MeshRenderer));
            marker.transform.SetParent(transform, false);
            marker.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(MarkerMeshCache, hexRadius * 0.35f);
            markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sortingOrder = 1;
            marker.SetActive(false);
        }

        // Called on every Render pass after the first — repaints the
        // existing tile/highlight/marker in place, no GameObjects created
        // or destroyed.
        public void SetState(TerrainType terrain, EngagementTier tier, HexHighlightState highlightState)
        {
            GetComponent<MeshRenderer>().sharedMaterial = TerrainMaterials.Get(terrain);

            highlightRenderer.gameObject.SetActive(highlightState != HexHighlightState.None);
            if (highlightState != HexHighlightState.None)
            {
                highlightRenderer.sharedMaterial = highlightState == HexHighlightState.Pending
                    ? HighlightMaterials.PendingHighlight
                    : HighlightMaterials.TargetHighlight;
            }

            markerRenderer.gameObject.SetActive(tier != EngagementTier.None);
            if (tier != EngagementTier.None)
                markerRenderer.sharedMaterial = EngagementMaterials.Get(tier);
        }

        // Per-instance data for TradelaneConnector.shader — which of the
        // hex's own material (shared across every Tradelane tile via
        // TerrainMaterials) doesn't vary per-instance, so connectivity to
        // specific neighbors has to ride along on a MaterialPropertyBlock
        // instead. Terrain never changes after map generation, so this
        // only needs to be set once, not every SetState call — see
        // MapView.Render, which calls this only when a Tradelane tile is
        // first created.
        public void SetConnectionMask(int mask)
        {
            propertyBlock ??= new MaterialPropertyBlock();
            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_ConnectionMask", mask);
            meshRenderer.SetPropertyBlock(propertyBlock);
        }

        // `== null` (not just TryGetValue) because a destroyed
        // UnityEngine.Object isn't a real C# null reference — with Domain
        // Reload disabled in Enter Play Mode Settings, these static caches
        // survive Stop/Play but the Mesh objects they hold get destroyed,
        // which otherwise makes every hex tile invisible on the next Play.
        private static Mesh GetOrCreate(Dictionary<float, Mesh> cache, float radius)
        {
            if (!cache.TryGetValue(radius, out var mesh) || mesh == null)
            {
                mesh = HexMeshFactory.CreateFlatTopHex(radius);
                cache[radius] = mesh;
            }

            return mesh;
        }
    }
}
