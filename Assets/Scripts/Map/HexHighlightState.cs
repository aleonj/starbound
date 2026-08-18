namespace StarBound.Map
{
    // What a hex's highlight border (if any) communicates — see
    // HexTileView.Initialize / HighlightMaterials. LegalTarget is "you
    // could move here"; Pending is "this is what Confirm will actually
    // move to," distinct so a player can tell the two apart at a glance.
    // Waypoint is unrelated to this turn's movement — it marks a job's
    // destination (active or offered-but-not-yet-accepted).
    public enum HexHighlightState
    {
        None,
        LegalTarget,
        Pending,
        Waypoint
    }
}
