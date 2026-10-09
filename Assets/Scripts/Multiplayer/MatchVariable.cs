namespace StarBound.Multiplayer
{
    // A persisting match-wide modifier introduced by its own independent
    // progression event (see Match.HandleProgressionOnEngagementWin —
    // variable events fire on their own win-counter, decoupled from goal
    // events, and keep firing for the whole match rather than stopping
    // once Hard tier unlocks). Exactly one is active at a time — not
    // cumulative — and it stays active until the next variable event
    // replaces it, even across a goal being completed. None means no
    // event has fired yet this match.
    //
    // Roughly half risk/negative, half benefit/positive — see
    // MatchProgressionService.Variables for the actual draw pool and
    // MatchHud.DescribeVariable for player-facing text.
    public enum MatchVariable
    {
        None,

        // --- Risk / negative ---

        // Mines hazard chance raised (see HazardChances.MinefieldDamageChanceDuringAlert).
        MinefieldDamage,
        // Asteroids hazard chance raised (see HazardChances.AsteroidDamageChanceDuringStorm).
        AsteroidStorm,
        // Tradelane tolls multiplied, not waived (see TollPricing.FuelShortageTollMultiplier).
        FuelShortage,
        // NPC opponents roll stronger stats for the duration (see EngagementTrigger).
        PirateSurge,
        // Wormhole travel disabled for the duration (see Match.CanTravelWormhole).
        IonStorm,

        // --- Benefit / positive ---

        // Tradelane tolls waived entirely.
        TradeBoom,
        // Both hazard terrains' damage chance lowered (see HazardChances.CalmSpaceHazardChance).
        CalmSpace,
        // Shop item prices discounted at purchase (see ItemPricing.MarketCrashDiscountMultiplier).
        MarketCrash,
        // Repair cost discounted (see RepairService.RepairDiscountMultiplier).
        RepairDiscount,
        // Plain PvE kill reward boosted (see EngagementSession.DefeatRewardMoney).
        SalvageRush,
        // Job board rewards boosted for newly generated offers (see JobOfferGenerator).
        BountySeason
    }
}
