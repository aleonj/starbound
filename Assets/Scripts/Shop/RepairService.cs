using System;
using StarBound.Core;

namespace StarBound.Shop
{
    public static class RepairService
    {
        // Placeholder balance, tunable here.
        public const int CostPerPoint = 15;

        // Restores Hull or Energy by one point, up to the default value (3)
        // — Integrity stats never get a permanent item-driven ceiling, see
        // ItemPool, so the default is the repair ceiling.
        public static RepairResult TryRepairOnePoint(Ship ship, CoreStat stat)
        {
            if (stat != CoreStat.Hull && stat != CoreStat.Energy)
                throw new ArgumentException("Only Hull and Energy can be repaired.", nameof(stat));

            if (ship.GetStat(stat) >= Ship.DefaultStatValue)
                return RepairResult.Failed(RepairFailureReason.AlreadyAtMax);

            if (!ship.TrySpendMoney(CostPerPoint))
                return RepairResult.Failed(RepairFailureReason.InsufficientFunds);

            ship.ApplyStatDelta(stat, 1);
            return RepairResult.Succeeded();
        }
    }
}
