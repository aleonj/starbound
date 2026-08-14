using System;
using StarBound.Core;

namespace StarBound.Combat
{
    public static class NpcShipGenerator
    {
        public static Ship Generate(EngagementDefinition definition, Random rng)
        {
            var ship = new Ship(cargoCapacity: 0);

            SetStatWithinRange(ship, CoreStat.Hull, definition.HullRange, rng);
            SetStatWithinRange(ship, CoreStat.Weapons, definition.WeaponsRange, rng);
            SetStatWithinRange(ship, CoreStat.Shields, definition.ShieldsRange, rng);
            SetStatWithinRange(ship, CoreStat.Speed, definition.SpeedRange, rng);
            // Energy is left at its default — irrelevant, NPCs never attempt escape.

            return ship;
        }

        private static void SetStatWithinRange(Ship ship, CoreStat stat, (int Min, int Max) range, Random rng)
        {
            var target = rng.Next(range.Min, range.Max + 1);
            var delta = target - ship.GetStat(stat);
            ship.ApplyStatDelta(stat, delta);
        }
    }
}
