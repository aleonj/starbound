namespace StarBound.Combat
{
    public enum EngagementOutcome
    {
        InProgress,
        PlayerWon,
        PlayerLost,
        PlayerEscaped,
        // PvP-only — the opponent successfully fled via
        // EngagementSession.AttemptOpponentEscape. Never produced by an
        // NPC engagement.
        OpponentEscaped
    }
}
