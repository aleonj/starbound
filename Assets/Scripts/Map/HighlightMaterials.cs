using UnityEngine;

namespace StarBound.Map
{
    public static class HighlightMaterials
    {
        private static Material targetHighlight;
        private static Material pendingHighlight;
        private static Material flashHighlight;

        // See TerrainMaterials.Get — `??=` alone isn't enough because a
        // destroyed UnityEngine.Object isn't a real C# null reference, so
        // it survives across Play sessions when Domain Reload is disabled.
        public static Material TargetHighlight =>
            targetHighlight = targetHighlight != null ? targetHighlight : CreateMaterial(new Color(1f, 1f, 0.3f));

        // Distinct from TargetHighlight — this hex is what Confirm will
        // actually move to, not just a legal option. Placeholder color,
        // tunable here.
        public static Material PendingHighlight =>
            pendingHighlight = pendingHighlight != null ? pendingHighlight : CreateMaterial(new Color(0.3f, 0.9f, 1f));

        // Waypoint/GoalTarget used to live here too — see
        // EngagementMaterials.WaypointBeacon/GoalBeacon, which replaced
        // them (user-requested: those should look like an undefeated
        // engagement's marker, not a highlight ring).

        // Base color is mostly a fallback — HexTileView.Flash overrides
        // _Color (and _ThrobSpeed/_MinRadius/_MaxRadius/_MaxAlpha) via a
        // MaterialPropertyBlock every time it's actually used, since a
        // one-shot flash needs a caller-chosen color and a fading alpha,
        // not a single static tint like the highlights above.
        public static Material FlashHighlight =>
            flashHighlight = flashHighlight != null ? flashHighlight : CreateMaterial(new Color(1f, 0.92f, 0.55f));

        private static Material CreateMaterial(Color color) => new(Shader.Find("StarBound/HexHighlight"))
        {
            color = color
        };
    }
}
