using System;
using System.Collections.Generic;
using System.Linq;

namespace StarBound.Core
{
    public class Ship
    {
        public const int DefaultStatValue = 3;
        public const int MinStatValue = 0;

        // Energy's own ceiling, separate from DefaultStatValue — it's a
        // movement-die economy now (one point per movement die per
        // turn, see Match.RollDice/EndTurn), not a combat stat scaled
        // against NPC ranges the way Weapons/Shields/Speed are, so it
        // doesn't share their baseline-3 starting point.
        public const int MaxEnergyValue = 5;

        private readonly Dictionary<CoreStat, int> stats = new()
        {
            { CoreStat.Hull, DefaultStatValue },
            { CoreStat.Energy, MaxEnergyValue },
            { CoreStat.Weapons, DefaultStatValue },
            { CoreStat.Shields, DefaultStatValue },
            { CoreStat.Speed, DefaultStatValue },
        };

        private readonly List<ItemDefinition> heldItems = new();

        public int Money { get; private set; }
        public int CargoCapacity { get; }
        public IReadOnlyList<ItemDefinition> HeldItems => heldItems;

        public Ship(int cargoCapacity, int startingMoney = 0)
        {
            if (cargoCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(cargoCapacity));
            if (startingMoney < 0)
                throw new ArgumentOutOfRangeException(nameof(startingMoney));

            CargoCapacity = cargoCapacity;
            Money = startingMoney;
        }

        // Base value plus the live bonus from any held Permanent items
        // affecting this stat — a Permanent item's effect only applies
        // while it's actually held, so selling/trading it away removes
        // the bonus immediately. Consumables never contribute here; their
        // effect is folded into the base stat (via ApplyStatDelta) at the
        // moment they're used, not while merely held.
        public int GetStat(CoreStat stat) =>
            stats[stat] + heldItems
                .Where(item => item.Kind == ItemKind.Permanent && item.AffectedStat == stat)
                .Sum(item => item.StatDelta ?? 0);

        public void ApplyStatDelta(CoreStat stat, int delta)
        {
            stats[stat] = Math.Max(MinStatValue, stats[stat] + delta);
        }

        // True when Hull has hit zero — triggers the [Combat] Zero Hull
        // penalty rule. Energy deliberately does NOT contribute here any
        // more (see [Combat] Energy overhaul) — it's a movement-die
        // economy, not a second destruction condition; hitting 0 Energy
        // costs a turn's movement, not the ship.
        public bool IsIntegrityDepleted =>
            GetStat(CoreStat.Hull) <= MinStatValue;

        public void AddMoney(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Money += amount;
        }

        public bool TrySpendMoney(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (Money < amount)
                return false;

            Money -= amount;
            return true;
        }

        public bool CanHoldAnotherItem => heldItems.Count < CargoCapacity;

        public bool TryAddItem(ItemDefinition item)
        {
            if (!CanHoldAnotherItem)
                return false;

            heldItems.Add(item);
            return true;
        }

        // Used for selling and trading — removing a Permanent item drops
        // its live GetStat bonus immediately, since that bonus is computed
        // from HeldItems rather than baked into the base stat.
        public bool TryRemoveItem(ItemDefinition item) => heldItems.Remove(item);

        // Permanently folds the item's effect into the base stat and
        // destroys it — unlike a Permanent item, a used Consumable can't
        // be resold or traded afterward, since it's no longer held.
        public void UseConsumableItem(ItemDefinition item)
        {
            if (item.Kind != ItemKind.Consumable)
                throw new ArgumentException("Only Consumable items can be used.", nameof(item));
            if (!heldItems.Remove(item))
                throw new InvalidOperationException("This item isn't held.");

            ApplyStatDelta(item.AffectedStat!.Value, item.StatDelta!.Value);
        }

        // Floors at zero, same convention as ApplyStatDelta — used for
        // the bounty-job escape penalty.
        public void ApplyMoneyPenalty(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Money = Math.Max(0, Money - amount);
        }

        // [Combat] Zero Hull/Energy penalty: clear money & non-Permanent
        // items on relocation. Permanent stat upgrades are earned gear,
        // not spending money — deliberately survive a wipe (see the
        // [Multiplayer] game-progression story's 2026-09-29 finding:
        // wiping them on every single loss, even a lucky bad roll at
        // Easy tier, meant no advantage could ever survive long enough to
        // compound toward a genuinely hard fight). Consumables and Unlock
        // items still get cleared, same as money — they're spent
        // resources, not a standing investment.
        public void ClearMoneyAndNonPermanentItems()
        {
            Money = 0;
            heldItems.RemoveAll(item => item.Kind != ItemKind.Permanent);
        }

        // [Combat] Bug: Hull/Energy not reset after zero-integrity penalty
        // — without this, a depleted player was left stuck at 0 Hull
        // with no money to repair (see ClearMoneyAndNonPermanentItems
        // above), unable to meaningfully continue. Resets both stats
        // together, not just Hull (the only one that can trigger this
        // now — see IsIntegrityDepleted), since this is a fresh start
        // after respawning — same "everything reset" treatment as the
        // money/consumables wipe. Energy resets to its own ceiling, not
        // DefaultStatValue — see MaxEnergyValue.
        public void ResetIntegrityStats()
        {
            stats[CoreStat.Hull] = DefaultStatValue;
            stats[CoreStat.Energy] = MaxEnergyValue;
        }
    }
}
