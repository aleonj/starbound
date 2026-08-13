using System;
using System.Collections.Generic;

namespace StarBound.Core
{
    public class Ship
    {
        public const int DefaultStatValue = 3;
        public const int MinStatValue = 0;

        private readonly Dictionary<CoreStat, int> stats = new()
        {
            { CoreStat.Hull, DefaultStatValue },
            { CoreStat.Energy, DefaultStatValue },
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

        public int GetStat(CoreStat stat) => stats[stat];

        public void ApplyStatDelta(CoreStat stat, int delta)
        {
            stats[stat] = Math.Max(MinStatValue, stats[stat] + delta);
        }

        // True when Hull or Energy has hit zero — triggers the
        // [Combat] Zero Hull/Energy penalty rule.
        public bool IsIntegrityDepleted =>
            GetStat(CoreStat.Hull) <= MinStatValue || GetStat(CoreStat.Energy) <= MinStatValue;

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

        // [Combat] Zero Hull/Energy penalty: clear money & items on relocation.
        public void ClearMoneyAndItems()
        {
            Money = 0;
            heldItems.Clear();
        }
    }
}
