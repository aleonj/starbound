namespace StarBound.Multiplayer
{
    // A persisting match-wide modifier introduced by a progression event
    // (see MatchProgressionService). Exactly one is active at a time —
    // not cumulative — and it stays active until the next event replaces
    // it, even across a goal being completed. None means no event has
    // fired yet this match.
    public enum MatchVariable
    {
        None,
        MinefieldDamage,
        TradeBoom
    }
}
