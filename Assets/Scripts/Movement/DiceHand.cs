using System.Collections.Generic;
using System.Linq;

namespace StarBound.Movement
{
    // This turn's 5 rolled dice. Nothing persists a hand between turns —
    // rolling again simply produces a new one, which is how "unused dice
    // are discarded at end of turn" falls out by construction.
    public class DiceHand
    {
        private readonly List<RolledDie> dice;

        public DiceHand(IEnumerable<RolledDie> dice)
        {
            this.dice = dice.ToList();
        }

        public IReadOnlyList<RolledDie> Dice => dice;
        public IEnumerable<RolledDie> UnspentDice => dice.Where(d => !d.IsSpent);
        public bool HasUnspentDice => UnspentDice.Any();
    }
}
