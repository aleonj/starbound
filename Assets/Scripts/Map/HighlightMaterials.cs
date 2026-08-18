using UnityEngine;

namespace StarBound.Map
{
    public static class HighlightMaterials
    {
        private static Material targetHighlight;
        private static Material pendingHighlight;
        private static Material waypointHighlight;

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

        // Marks a job's destination — deliberately purple, distinct from
        // both movement colors above, since it means something unrelated
        // ("go here eventually for a job") rather than "you can move here
        // this turn."
        public static Material WaypointHighlight =>
            waypointHighlight = waypointHighlight != null ? waypointHighlight : CreateMaterial(new Color(0.65f, 0.35f, 0.95f));

        private static Material CreateMaterial(Color color) => new(Shader.Find("StarBound/HexHighlight"))
        {
            color = color
        };
    }
}
