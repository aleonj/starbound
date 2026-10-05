namespace StarBound.Economy
{
    // Placeholder balance numbers, tunable here — same pattern as
    // TollPricing. Shared between Match's actual hazard-roll logic and
    // MatchHud's hex-info popup text, so the popup can never drift out of
    // sync with the real odds.
    public static class HazardChances
    {
        public const double AsteroidDamageChance = 0.1; // 1 in 10
        public const double MinefieldDamageChance = 0.2; // 1 in 5
        public const double MinefieldDamageChanceDuringAlert = 0.5; // 1 in 2
    }
}
