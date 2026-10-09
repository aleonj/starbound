namespace StarBound.Map
{
    // What a hex's highlight border (if any) communicates — see
    // HexTileView.Initialize / HighlightMaterials. LegalTarget is "you
    // could move here"; Pending is "this is what Confirm will actually
    // move to," distinct so a player can tell the two apart at a glance.
    //
    // Waypoint/GoalTarget used to live here too, but a full-hex glowing
    // ring reads as "you can act on this hex right now" — wrong for a
    // job destination or the race goal, which are "something is here"
    // points of interest, not this-turn move options. Both moved to the
    // marker/beacon channel instead (see HexMarkerOverride), the same
    // visual language as an undefeated engagement's tier beacon — user-
    // requested, so all three "something notable is here" cases read
    // consistently.
    public enum HexHighlightState
    {
        None,
        LegalTarget,
        Pending
    }
}
