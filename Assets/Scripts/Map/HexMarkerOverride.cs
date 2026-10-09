namespace StarBound.Map
{
    // What the marker/beacon channel shows when there's no real engagement
    // tier on the hex (see HexTileView.SetState) — a job waypoint or the
    // race goal, rendered with the SAME beacon mesh/shader an undefeated
    // engagement's tier marker uses (EngagementBeacon), just a different
    // color, rather than the highlight-ring channel. A real engagement
    // tier always takes priority over either of these when both would
    // otherwise apply to the same hex (e.g. a DefeatNamedTarget goal's
    // target, or a BountyHunting job's destination, are both themselves
    // marked-engagement hexes) — the tier beacon is the more important,
    // truthful thing to show there. GoalTarget takes priority over
    // Waypoint on the rare hex where both would otherwise apply.
    public enum HexMarkerOverride
    {
        None,
        Waypoint,
        GoalTarget
    }
}
