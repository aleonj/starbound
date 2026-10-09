using System;
using StarBound.Core;

namespace StarBound.Shop
{
    public static class RepairService
    {
        // Placeholder balance, tunable here.
        public const int CostPerPoint = 15;
        // RepairDiscount event — a fraction of CostPerPoint, not a flat
        // override, so it stays proportional if CostPerPoint is retuned.
        public const double RepairDiscountMultiplier = 0.5;

        // Restores Hull or Energy by one point, up to each stat's own
        // ceiling — Integrity stats never get a permanent item-driven
        // ceiling, see ItemPool, so these are the repair ceiling. Hull's
        // is the shared DefaultStatValue (3); Energy's is its own,
        // higher MaxEnergyValue (5, one per movement die — see [Combat]
        // Energy overhaul), not DefaultStatValue. costPerPoint defaults
        // to the normal price — callers pass an event-adjusted value
        // (see Match.RepairStat/MatchVariable.RepairDiscount) only when
        // one is actually active, so every existing call site keeps
        // working unchanged.
        public static RepairResult TryRepairOnePoint(Ship ship, CoreStat stat, int costPerPoint = CostPerPoint)
        {
            if (stat != CoreStat.Hull && stat != CoreStat.Energy)
                throw new ArgumentException("Only Hull and Energy can be repaired.", nameof(stat));

            var max = stat == CoreStat.Energy ? Ship.MaxEnergyValue : Ship.DefaultStatValue;
            if (ship.GetStat(stat) >= max)
                return RepairResult.Failed(RepairFailureReason.AlreadyAtMax);

            if (!ship.TrySpendMoney(costPerPoint))
                return RepairResult.Failed(RepairFailureReason.InsufficientFunds);

            ship.ApplyStatDelta(stat, 1);
            return RepairResult.Succeeded();
        }
    }
}
