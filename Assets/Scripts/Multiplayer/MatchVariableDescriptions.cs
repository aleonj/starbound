namespace StarBound.Multiplayer
{
    // Player-facing name + one-line mechanical summary for each
    // MatchVariable — shared between Match (the one-time "New Event"
    // notice, see Player.PendingVariableEventNotice) and MatchHud (the
    // persistent status label), so there's only one place to update
    // when a variable's effect or wording changes.
    public static class MatchVariableDescriptions
    {
        public static string Describe(MatchVariable variable) => variable switch
        {
            MatchVariable.MinefieldDamage => "Minefield Alert — Mines hazard damage is more likely.",
            MatchVariable.AsteroidStorm => "Asteroid Storm — Asteroids hazard damage is more likely.",
            MatchVariable.FuelShortage => "Fuel Shortage — Tradelane tolls are tripled.",
            MatchVariable.PirateSurge => "Pirate Surge — NPC opponents roll stronger stats.",
            MatchVariable.IonStorm => "Ion Storm — Wormhole travel is disabled.",
            MatchVariable.TradeBoom => "Trade Boom — Tradelane tolls are waived.",
            MatchVariable.CalmSpace => "Calm Space — hazard damage is far less likely.",
            MatchVariable.MarketCrash => "Market Crash — shop items are discounted.",
            MatchVariable.RepairDiscount => "Repair Discount — repairs cost less.",
            MatchVariable.SalvageRush => "Salvage Rush — defeating an enemy pays more.",
            MatchVariable.BountySeason => "Bounty Season — job board rewards are higher.",
            _ => variable.ToString()
        };
    }
}
