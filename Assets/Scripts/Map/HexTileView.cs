using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class HexTileView : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ThrobSpeedId = Shader.PropertyToID("_ThrobSpeed");
        private static readonly int MinRadiusId = Shader.PropertyToID("_MinRadius");
        private static readonly int MaxRadiusId = Shader.PropertyToID("_MaxRadius");
        private static readonly int MaxAlphaId = Shader.PropertyToID("_MaxAlpha");

        private static readonly Dictionary<float, Mesh> HexMeshCache = new();
        private static readonly Dictionary<float, Mesh> MarkerMeshCache = new();

        private MeshRenderer highlightRenderer;
        private MeshRenderer markerRenderer;
        private MeshRenderer flashRenderer;
        private MaterialPropertyBlock propertyBlock;
        private MaterialPropertyBlock flashPropertyBlock;
        private Coroutine flashCoroutine;

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

            // Drawn on top of everything (see HexHighlight.shader) — its
            // own alpha fades to fully transparent toward the center, so
            // it reads as a glowing border without needing to rely on a
            // smaller opaque tile occluding a larger one underneath. Uses
            // the SAME mesh size as the tile itself (not oversized) so
            // the shader's local coordinate space lines up exactly with
            // the true hex edge — an oversized mesh here would put local
            // radius 1.0 outside the actual hex, letting the ring spill
            // into the neighboring tile.
            var highlight = new GameObject("Highlight", typeof(MeshFilter), typeof(MeshRenderer));
            highlight.transform.SetParent(transform, false);
            highlight.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(HexMeshCache, hexRadius * 0.95f);
            highlightRenderer = highlight.GetComponent<MeshRenderer>();
            highlightRenderer.sortingOrder = 3;
            highlight.SetActive(false);

            var marker = new GameObject("EngagementMarker", typeof(MeshFilter), typeof(MeshRenderer));
            marker.transform.SetParent(transform, false);
            marker.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(MarkerMeshCache, hexRadius * 0.35f);
            markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sortingOrder = 1;
            marker.SetActive(false);

            // One-shot "look here" pulse (see Flash) — a separate
            // renderer from highlightRenderer above so it never fights
            // with SetState's own persistent LegalTarget/Pending/
            // Waypoint repaint logic; drawn above it (sortingOrder 4 vs
            // 3) so a flash stays visible even on a hex that's also
            // currently highlighted for another reason.
            var flash = new GameObject("Flash", typeof(MeshFilter), typeof(MeshRenderer));
            flash.transform.SetParent(transform, false);
            flash.GetComponent<MeshFilter>().sharedMesh = GetOrCreate(HexMeshCache, hexRadius * 0.95f);
            flashRenderer = flash.GetComponent<MeshRenderer>();
            flashRenderer.sharedMaterial = HighlightMaterials.FlashHighlight;
            flashRenderer.sortingOrder = 4;
            flash.SetActive(false);
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
                highlightRenderer.sharedMaterial = highlightState switch
                {
                    HexHighlightState.Pending => HighlightMaterials.PendingHighlight,
                    HexHighlightState.Waypoint => HighlightMaterials.WaypointHighlight,
                    _ => HighlightMaterials.TargetHighlight
                };
            }

            markerRenderer.gameObject.SetActive(tier != EngagementTier.None);
            if (tier != EngagementTier.None)
                markerRenderer.sharedMaterial = EngagementMaterials.Get(tier);
        }

        // A one-shot attention pulse (e.g. "locate my ship") — distinct
        // from SetState's persistent LegalTarget/Pending/Waypoint
        // highlights, which are re-derived every Render call and have no
        // "this decays after N seconds" concept. Safe to run as a
        // coroutine directly on this instance because MapView keeps
        // HexTileView instances alive and reused for the whole match
        // (see MapView.Render's tiles dictionary) rather than destroying
        // and recreating them — a Render call mid-flash won't interrupt it.
        public void Flash(Color color, float duration)
        {
            if (flashCoroutine != null)
                StopCoroutine(flashCoroutine);
            flashCoroutine = StartCoroutine(FlashRoutine(color, duration));
        }

        private IEnumerator FlashRoutine(Color color, float duration)
        {
            flashPropertyBlock ??= new MaterialPropertyBlock();
            flashRenderer.gameObject.SetActive(true);

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var fadeOut = 1f - Mathf.Clamp01(elapsed / duration);

                flashRenderer.GetPropertyBlock(flashPropertyBlock);
                flashPropertyBlock.SetColor(ColorId, color);
                // No throb, fixed near-edge ring — a flash should read as
                // one clean pulse, not the same continuously-throbbing
                // look SetState's persistent highlights use.
                flashPropertyBlock.SetFloat(ThrobSpeedId, 0f);
                flashPropertyBlock.SetFloat(MinRadiusId, 0.92f);
                flashPropertyBlock.SetFloat(MaxRadiusId, 0.92f);
                flashPropertyBlock.SetFloat(MaxAlphaId, fadeOut);
                flashRenderer.SetPropertyBlock(flashPropertyBlock);

                yield return null;
            }

            flashRenderer.gameObject.SetActive(false);
            flashCoroutine = null;
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
