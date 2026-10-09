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
        // AsteroidStorm's own risk-raising event, same shape as
        // MinefieldDamage but for the other hazard terrain.
        public const double AsteroidDamageChanceDuringStorm = 0.35; // ~1 in 3
        // CalmSpace applies to BOTH hazard terrains uniformly — a rare
        // "safe stretch" event, lower than either terrain's own base rate.
        public const double CalmSpaceHazardChance = 0.03; // 1 in ~33
    }
}
