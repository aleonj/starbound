using StarBound.Core;

namespace StarBound.Combat
{
    // What the player is shown about the opponent before/during combat.
    // Energy is deliberately omitted — NPCs never attempt escape, so it's
    // not applicable to show.
    public readonly struct OpponentStatView
    {
        public int Hull { get; }
        public int Weapons { get; }
        public int Shields { get; }
        public int Speed { get; }

        public OpponentStatView(Ship opponent)
        {
            Hull = opponent.GetStat(CoreStat.Hull);
            Weapons = opponent.GetStat(CoreStat.Weapons);
            Shields = opponent.GetStat(CoreStat.Shields);
            Speed = opponent.GetStat(CoreStat.Speed);
        }
    }
}
