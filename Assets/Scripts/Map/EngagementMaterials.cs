using System.Collections.Generic;
using UnityEngine;
using StarBound.Core;

namespace StarBound.Map
{
    // Tier-colored EngagementBeacon materials (easy/medium/hard) — see
    // Assets/Shaders/EngagementBeacon.shader.
    public static class EngagementMaterials
    {
        private static readonly Dictionary<EngagementTier, Color> Colors = new()
        {
            { EngagementTier.Easy, new Color(0.25f, 0.85f, 0.30f) },
            { EngagementTier.Medium, new Color(0.95f, 0.60f, 0.10f) },
            { EngagementTier.Hard, new Color(0.90f, 0.10f, 0.10f) },
        };

        private static readonly Dictionary<EngagementTier, Material> Cache = new();

        public static Material Get(EngagementTier tier)
        {
            // See TerrainMaterials.Get — same stale-cache-across-Play-
            // sessions issue when Domain Reload is disabled.
            if (!Cache.TryGetValue(tier, out var material) || material == null)
            {
                material = CreateMaterial(Colors[tier]);
                Cache[tier] = material;
            }

            return material;
        }

        private static Material waypointBeacon;
        private static Material goalBeacon;

        // Same EngagementBeacon shader/mesh as a real tier marker (see
        // HexMarkerOverride) — colors kept identical to the old
        // HighlightMaterials.WaypointHighlight/GoalTargetHighlight ring
        // colors this replaces, so the meaning ("violet = job waypoint,"
        // "magenta = race goal") carries over for anyone already used to it.
        public static Material WaypointBeacon =>
            waypointBeacon = waypointBeacon != null ? waypointBeacon : CreateMaterial(new Color(0.65f, 0.35f, 0.95f));

        public static Material GoalBeacon =>
            goalBeacon = goalBeacon != null ? goalBeacon : CreateMaterial(new Color(0.9f, 0.15f, 0.55f));

        private static Material CreateMaterial(Color color) => new(Shader.Find("StarBound/EngagementBeacon"))
        {
            color = color
        };
    }
}
