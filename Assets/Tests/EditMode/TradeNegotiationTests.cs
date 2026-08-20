using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;
using StarBound.Multiplayer;

namespace StarBound.Tests
{
    public class TradeNegotiationTests
    {
        // Both players start on (0,0) — already sharing a hex, matching
        // IsOnOpponentHex/CanAttackOpponent's own gate, same as MatchTests'
        // own BuildMatch (kept as a local copy here so this file has no
        // ordering dependency on MatchTests).
        private static (Match match, Player p1, Player p2) BuildMatch()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            return (new Match(map, p1, p2), p1, p2);
        }

        [Test]
        public void CanProposeTrade_TrueWhenSharingHexWithActionsAvailable()
        {
            var (match, _, _) = BuildMatch();

            Assert.IsTrue(match.CanProposeTrade);
        }

        [Test]
        public void CanProposeTrade_FalseWhenNotSharingHex()
        {
            var (match, p1, _) = BuildMatch();
            p1.Position = new HexCoordinate(1, 0);

            Assert.IsFalse(match.CanProposeTrade);
        }

        [Test]
        public void ProposeTrade_ValidOffer_CreatesNegotiationAndConsumesOneAction()
        {
            var (match, p1, p2) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            p2.Ship.AddMoney(100);

            var result = match.ProposeTrade(new[] { item }, System.Array.Empty<ItemDefinition>(), 0, 20);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(match.IsNegotiatingTrade);
            Assert.AreEqual(1, match.ActionsRemaining);
            Assert.AreEqual(p1, match.ActiveTradeNegotiation.Initiator);
            Assert.AreEqual(p2, match.ActiveTradeNegotiation.Opponent);
            CollectionAssert.AreEqual(new[] { item }, match.ActiveTradeNegotiation.InitiatorGives);
            Assert.AreEqual(20, match.ActiveTradeNegotiation.OpponentMoney);
        }

        [Test]
        public void ProposeTrade_ItemNotActuallyHeld_FailsWithoutConsumingAction()
        {
            var (match, _, _) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);

            var result = match.ProposeTrade(new[] { item }, System.Array.Empty<ItemDefinition>(), 0, 0);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TradeProposalFailureReason.ItemNotHeld, result.FailureReason);
            Assert.IsFalse(match.IsNegotiatingTrade);
            Assert.AreEqual(Match.ActionsPerTurn, match.ActionsRemaining);
        }

        [Test]
        public void ProposeTrade_OpponentCannotAffordTheRequestedMoney_Fails()
        {
            var (match, p1, p2) = BuildMatch();
            p2.Ship.AddMoney(10);

            var result = match.ProposeTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 50);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TradeProposalFailureReason.InsufficientFunds, result.FailureReason);
            Assert.IsFalse(match.IsNegotiatingTrade);
        }

        [Test]
        public void ProposeTrade_WouldOverflowOpponentCargo_Fails()
        {
            var (match, p1, p2) = BuildMatch();
            // Opponent's cargo (capacity 3) is already full.
            for (var i = 0; i < 3; i++)
                p2.Ship.TryAddItem(new ItemDefinition($"Filler {i}", CoreStat.Weapons, 1, 10, ItemKind.Permanent));
            var offered = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(offered);

            var result = match.ProposeTrade(new[] { offered }, System.Array.Empty<ItemDefinition>(), 0, 0);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TradeProposalFailureReason.CargoFull, result.FailureReason);
        }

        [Test]
        public void ProposeTrade_WhileAlreadyNegotiating_Throws()
        {
            var (match, _, _) = BuildMatch();
            match.ProposeTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 0);

            Assert.Throws<System.InvalidOperationException>(() =>
                match.ProposeTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 0));
        }

        [Test]
        public void AcceptTrade_TransfersEveryItemAndBothMoneyAmountsThenClearsNegotiation()
        {
            var (match, p1, p2) = BuildMatch();
            var fromP1 = new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 100, ItemKind.Consumable);
            var fromP2 = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(fromP1);
            p2.Ship.TryAddItem(fromP2);
            p1.Ship.AddMoney(100);
            p2.Ship.AddMoney(100);

            match.ProposeTrade(new[] { fromP1 }, new[] { fromP2 }, initiatorMoney: 30, opponentMoney: 10);
            var result = match.AcceptTrade();

            Assert.IsTrue(result.Success);
            Assert.IsFalse(match.IsNegotiatingTrade);
            Assert.IsFalse(p1.Ship.HeldItems.Contains(fromP1));
            Assert.IsTrue(p2.Ship.HeldItems.Contains(fromP1));
            Assert.IsFalse(p2.Ship.HeldItems.Contains(fromP2));
            Assert.IsTrue(p1.Ship.HeldItems.Contains(fromP2));
            // p1 paid 30, received 10 back = net -20; p2 is the mirror.
            Assert.AreEqual(80, p1.Ship.Money);
            Assert.AreEqual(120, p2.Ship.Money);
        }

        [Test]
        public void RejectTrade_ClearsNegotiationWithNoTransfers()
        {
            var (match, p1, p2) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            p2.Ship.AddMoney(100);
            match.ProposeTrade(new[] { item }, System.Array.Empty<ItemDefinition>(), 0, 20);

            match.RejectTrade();

            Assert.IsFalse(match.IsNegotiatingTrade);
            Assert.IsTrue(p1.Ship.HeldItems.Contains(item));
            Assert.AreEqual(100, p2.Ship.Money);
            // The initiator's action isn't refunded — proposing was the
            // commitment, not the outcome (see Match.ProposeTrade).
            Assert.AreEqual(1, match.ActionsRemaining);
        }

        [Test]
        public void CounterTrade_ReplacesTermsAndMarksAsCountered()
        {
            var (match, p1, p2) = BuildMatch();
            var originalItem = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            var counterItem = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(originalItem);
            p2.Ship.TryAddItem(counterItem);
            match.ProposeTrade(new[] { originalItem }, System.Array.Empty<ItemDefinition>(), 0, 0);

            var result = match.CounterTrade(System.Array.Empty<ItemDefinition>(), new[] { counterItem }, 0, 0);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(match.ActiveTradeNegotiation.HasBeenCountered);
            CollectionAssert.AreEqual(new[] { counterItem }, match.ActiveTradeNegotiation.OpponentGives);
            CollectionAssert.IsEmpty(match.ActiveTradeNegotiation.InitiatorGives);
        }

        [Test]
        public void CounterTrade_CalledTwice_SecondCallThrows()
        {
            var (match, p1, _) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            match.ProposeTrade(new[] { item }, System.Array.Empty<ItemDefinition>(), 0, 0);
            match.CounterTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 0);

            Assert.Throws<System.InvalidOperationException>(() =>
                match.CounterTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 0));
        }

        [Test]
        public void ProposeCounterAccept_TransfersTheCounteredTermsNotTheOriginalOffer()
        {
            var (match, p1, p2) = BuildMatch();
            var originalItem = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            var counterItem = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(originalItem);
            p2.Ship.TryAddItem(counterItem);

            // p1 initially offers originalItem for nothing; p2 counters
            // with "I'll give you counterItem instead, and nothing from
            // your original offer."
            match.ProposeTrade(new[] { originalItem }, System.Array.Empty<ItemDefinition>(), 0, 0);
            match.CounterTrade(System.Array.Empty<ItemDefinition>(), new[] { counterItem }, 0, 0);
            var result = match.AcceptTrade();

            Assert.IsTrue(result.Success);
            // The original offer never happened — p1 keeps their item and
            // p2 never received it.
            Assert.IsTrue(p1.Ship.HeldItems.Contains(originalItem));
            Assert.IsFalse(p2.Ship.HeldItems.Contains(originalItem));
            // The counter's terms did — p1 receives counterItem.
            Assert.IsFalse(p2.Ship.HeldItems.Contains(counterItem));
            Assert.IsTrue(p1.Ship.HeldItems.Contains(counterItem));
        }

        // Representative check of the systematic !IsNegotiatingTrade sweep
        // across CanMove/CanShop/CanAttackOpponent/etc. — not exhaustive
        // per-property, since they all follow the exact same one-line
        // pattern as CanMove here.
        [Test]
        public void CanMove_FalseWhileNegotiatingTrade()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new System.Random(1));
            match.ProposeTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 0);

            Assert.IsFalse(match.CanMove);
        }

        [Test]
        public void CanEndTurn_FalseWhileNegotiatingTrade()
        {
            var (match, _, _) = BuildMatch();
            match.ProposeTrade(System.Array.Empty<ItemDefinition>(), System.Array.Empty<ItemDefinition>(), 0, 0);

            Assert.IsFalse(match.CanEndTurn);
            Assert.Throws<System.InvalidOperationException>(() => match.EndTurn());
        }
    }
}
