using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Movement;
using StarBound.Multiplayer;
using StarBound.Shop;

namespace StarBound.Tests
{
    // Each test here plays out a whole realistic sequence of actions a
    // player would take through the HUD — not isolated units, but the
    // same Match/JobService/ShopService calls strung together the way an
    // actual turn or two would. The per-class unit tests elsewhere already
    // cover individual mechanics in isolation; this file exists to catch
    // regressions in how those mechanics compose across a real playthrough
    // (e.g. "does delivering re-open the shop", "does a mid-turn hazard
    // still leave the job intact"), which is exactly the kind of thing
    // that's easy to break without noticing when changing one system at
    // a time.
    public class FullGameScenarioTests
    {
        // A straight line of hexes plus one marked encounter, deliberately
        // simple so each scenario's travel path is easy to reason about:
        // (0,0) planet -- (1,0) clear -- (2,0) asteroids -- (3,0) planet
        //    |
        // (0,-1) marked encounter, (-1,0) clear (room to escape into)
        private static (Match match, Player p1, Player p2) BuildScenarioMatch()
        {
            var map = new GameMap(radius: 4, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.Asteroids));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(-1, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(3, 0) };

            return (new Match(map, p1, p2), p1, p2);
        }

        private static void PlayOutEngagement(Match match, Random rng)
        {
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }
        }

        [Test]
        public void FullMiningJob_TravelMineThenDeliverAcrossTwoTurns_AwardsRewardAndReopensTheShop()
        {
            var (match, p1, _) = BuildScenarioMatch();
            var job = new JobDefinition(JobType.Mining, new HexCoordinate(3, 0), 90);
            JobService.TryAcceptJob(p1, job);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            match.Move(new RolledDie(1, TerrainType.Asteroids), new HexCoordinate(2, 0), new Random(1));

            Assert.IsTrue(match.CanMineAsteroid);
            match.MineAsteroid();

            Assert.IsTrue(p1.HasMinedCargo);
            Assert.IsFalse(match.CanMove); // mining ends movement for the turn, same as delivering

            match.EndTurn(); // p2's turn — passes without acting
            match.EndTurn(); // back to p1, fresh turn

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.PlanetOrStarport), new HexCoordinate(3, 0), new Random(1));

            Assert.IsTrue(match.CanDeliverJob);
            match.DeliverJob();

            Assert.AreEqual(90, p1.Ship.Money);
            Assert.IsNull(p1.ActiveJob);
            Assert.IsFalse(p1.HasMinedCargo);

            // Delivering locked movement, but nothing here counts as an
            // engagement, so the shop should still be open this turn.
            Assert.IsTrue(match.CanShop);
        }

        [Test]
        public void FullTransportJob_ReachesDestinationInOneTurn_NoMiningStepRequired()
        {
            var (match, p1, _) = BuildScenarioMatch();
            var job = new JobDefinition(JobType.Transport, new HexCoordinate(3, 0), 60);
            JobService.TryAcceptJob(p1, job);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            match.Move(new RolledDie(1, TerrainType.Asteroids), new HexCoordinate(2, 0), new Random(1));
            match.Move(new RolledDie(2, TerrainType.PlanetOrStarport), new HexCoordinate(3, 0), new Random(1));

            Assert.IsTrue(match.CanDeliverJob);
            match.DeliverJob();

            Assert.AreEqual(60, p1.Ship.Money);
            Assert.IsNull(p1.ActiveJob);
        }

        [Test]
        public void FullBountyJob_WinTheMarkedFight_PaysOutAndClearsTheMarker()
        {
            var (match, p1, _) = BuildScenarioMatch();
            JobService.TryAcceptJob(p1, new JobDefinition(JobType.BountyHunting, new HexCoordinate(0, -1), 200, EngagementTier.Easy));

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            PlayOutEngagement(match, new Random(1));
            Assert.AreEqual(EngagementOutcome.PlayerWon, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            Assert.AreEqual(200, p1.Ship.Money);
            Assert.IsNull(p1.ActiveJob);
            Assert.AreEqual(1, p1.EasyEngagementWins);
            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(0, -1), out var hex));
            Assert.IsFalse(hex.HasEngagement); // the pirate's actually gone, not just the job
        }

        [Test]
        public void ShopVisit_BuyThenSellAPermanentItem_StatAndMoneyRoundTripCorrectly()
        {
            var (match, p1, _) = BuildScenarioMatch();
            p1.Ship.AddMoney(200);
            var moneyBeforeShopping = p1.Ship.Money;

            match.RollDice(new Random(1));

            var item = ItemPool.Items.First(i => i.Kind == ItemKind.Permanent && i.AffectedStat == CoreStat.Weapons);
            var purchase = match.BuyItem(item);
            Assert.IsTrue(purchase.Success);
            Assert.AreEqual(3 + item.StatDelta!.Value, p1.Ship.GetStat(CoreStat.Weapons));

            var sale = match.SellItem(item);
            Assert.IsTrue(sale.Success);

            Assert.AreEqual(3, p1.Ship.GetStat(CoreStat.Weapons)); // bonus gone once sold
            Assert.AreEqual(moneyBeforeShopping - item.Price / 2, p1.Ship.Money); // paid full price, refunded half
        }

        [Test]
        public void PvPEncounter_DefeatingTheOtherPlayer_DamagesThemWithoutCountingTowardVictory()
        {
            var (match, p1, p2) = BuildScenarioMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            match.Move(new RolledDie(1, TerrainType.Asteroids), new HexCoordinate(2, 0), new Random(1));
            match.Move(new RolledDie(2, TerrainType.PlanetOrStarport), new HexCoordinate(3, 0), new Random(1));

            Assert.IsTrue(match.CanAttackOpponent);
            match.AttackOpponent();
            Assert.IsTrue(match.ActiveEngagement.IsPvP);

            PlayOutEngagement(match, new Random(1));
            Assert.AreEqual(EngagementOutcome.PlayerWon, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            Assert.AreEqual(0, p1.EasyEngagementWins);
            Assert.IsFalse(p1.HasWonMatch);
            // Zero-Hull penalty wiped them out — money's gone, and Hull is
            // reset back to the default (not left stuck at 0, see
            // Ship.ResetIntegrityStats) so they can actually keep playing.
            Assert.AreEqual(Ship.DefaultStatValue, p2.Ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(0, p2.Ship.Money);
        }

        [Test]
        public void FullMatchToVictory_ThirdHardWinEndsTheMatchAndBlocksFurtherActions()
        {
            var (match, p1, _) = BuildScenarioMatch();
            p1.RecordEngagementWin(EngagementTier.Hard);
            p1.RecordEngagementWin(EngagementTier.Hard);
            match.Map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard });
            match.UnlockTier(EngagementTier.Hard); // this test is about the win-counting/victory rule, not progression phasing

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            PlayOutEngagement(match, new Random(1));
            match.ResolveActiveEngagement(new Random(1));

            Assert.IsTrue(match.IsComplete);
            Assert.AreEqual(p1, match.Winner);
            Assert.Throws<InvalidOperationException>(() => match.RollDice(new Random(1)));
            Assert.Throws<InvalidOperationException>(() => match.EndTurn());
        }

        [Test]
        public void ConsumableItem_UsedMidEngagement_HealsWithoutEndingTheFight()
        {
            var (match, p1, _) = BuildScenarioMatch();
            var repairKit = ItemPool.Items.First(i => i.Kind == ItemKind.Consumable && i.AffectedStat == CoreStat.Hull);
            p1.Ship.TryAddItem(repairKit);
            p1.Ship.ApplyStatDelta(CoreStat.Hull, -2); // Hull = 1, so the heal is visible

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            Assert.IsTrue(match.CanUseItem(repairKit));

            match.UseItem(repairKit);

            Assert.AreEqual(3, p1.Ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(0, p1.Ship.HeldItems.Count);
            Assert.IsTrue(match.IsInEngagement); // using the item didn't resolve or end the fight
        }

        [Test]
        public void DiscoveryThenWormholeTravel_EscapedMarkerStaysDiscoveredAndDeviceUnlocksTravel()
        {
            var map = new GameMap(radius: 6, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(5, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(6, 0), TerrainType.Wormhole));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.AddMoney(1000);

            // Buy the device before setting off.
            match.RollDice(new Random(1));
            var purchase = match.BuyItem(ItemPool.WormholeDevice);
            Assert.IsTrue(purchase.Success);
            match.EndTurn();
            match.EndTurn();

            // Land on the marked hex and escape — discovery should survive
            // the escape (only a win clears a marker from view).
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            Assert.IsTrue(match.IsInEngagement);
            match.ActiveEngagement.AttemptEscape(new Random(1));
            Assert.AreEqual(EngagementOutcome.PlayerEscaped, match.ActiveEngagement.Outcome);
            match.ResolveActiveEngagement(new Random(1));

            CollectionAssert.Contains(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(1, 0));

            match.EndTurn();
            match.EndTurn();

            // Travel via wormhole using the device bought earlier — from
            // wherever the escape happened to leave the player, with no
            // need to ever have physically reached a wormhole hex first.
            match.RollDice(new Random(1));
            Assert.IsTrue(match.CanTravelWormhole);

            match.TravelToWormhole(new HexCoordinate(5, 0), new Random(1));

            Assert.AreEqual(new HexCoordinate(5, 0), p1.Position);
        }

        [Test]
        public void TurnEconomy_ShopThenMoveAndMoveThenShop_BothOrderingsWorkAcrossTwoTurns()
        {
            var (match, p1, _) = BuildScenarioMatch();
            p1.Ship.AddMoney(200);
            var item = ItemPool.Items.First(i => i.Kind == ItemKind.Permanent && i.AffectedStat == CoreStat.Weapons);

            // Turn 1: "take a job or buy something from the shop, and then move."
            // Rolled right before the move, not at the top — rolling now
            // spends the Move session's own action (see Match.RollDice),
            // so rolling before the shop visit would make the shop the one
            // that runs out of budget instead of leaving room for both.
            var purchase = match.BuyItem(item);
            Assert.IsTrue(purchase.Success);
            Assert.AreEqual(1, match.ActionsRemaining);

            match.RollDice(new Random(1));
            var move = match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            Assert.IsTrue(move.Success);
            Assert.AreEqual(0, match.ActionsRemaining);
            Assert.IsTrue(match.CanEndTurn);

            match.EndTurn(); // p2's turn — passes without acting
            match.EndTurn(); // back to p1, fresh turn

            // Turn 2: "move to a planet and then take a job or buy something."
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.Asteroids), new HexCoordinate(2, 0), new Random(1));
            match.Move(new RolledDie(1, TerrainType.PlanetOrStarport), new HexCoordinate(3, 0), new Random(1));
            Assert.AreEqual(1, match.ActionsRemaining); // Move paid once — both dice were free after

            Assert.IsTrue(match.CanShop);
            var secondPurchase = match.BuyItem(item);
            Assert.IsTrue(secondPurchase.Success);
            Assert.AreEqual(0, match.ActionsRemaining);

            // The market session stays open (buying a second item here is
            // still free — it's the same visit), but choosing to shop
            // closed out the Move session, so movement is done for the
            // turn even though it was already paid for once.
            Assert.IsTrue(match.CanShop);
            Assert.IsFalse(match.CanMove);
            var thirdPurchase = match.BuyItem(item);
            Assert.IsTrue(thirdPurchase.Success);
            Assert.AreEqual(0, match.ActionsRemaining);

            // A genuinely new, unpaid category (e.g. attacking) is blocked
            // once the budget is exhausted.
            Assert.IsFalse(match.CanAttackOpponent);
            Assert.IsTrue(match.CanEndTurn);
        }

        [Test]
        public void PvPTrade_BuyThenTravelThenTrade_TransfersItemAndMoneyWithoutAffectingVictory()
        {
            var (match, p1, p2) = BuildScenarioMatch();
            p1.Ship.AddMoney(200);
            p2.Ship.AddMoney(200);
            var item = ItemPool.Items.First(i => i.Kind == ItemKind.Permanent && i.AffectedStat == CoreStat.Weapons);

            // Turn 1: buy at home, then travel the whole way to p2's planet
            // in one Move session (buying already used the market action,
            // so the 3-hex chain here is the turn's other action) — rolling
            // before or after the purchase both work equally, since Move
            // stays available once paid for regardless of what else
            // happens in between (see Match.CanMove).
            match.RollDice(new Random(1));
            match.BuyItem(item);
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            match.Move(new RolledDie(1, TerrainType.Asteroids), new HexCoordinate(2, 0), new Random(1));
            match.Move(new RolledDie(2, TerrainType.PlanetOrStarport), new HexCoordinate(3, 0), new Random(1));
            Assert.AreEqual(0, match.ActionsRemaining);

            match.EndTurn(); // p2 passes
            match.EndTurn(); // back to p1, fresh turn, still on p2's hex

            Assert.IsTrue(match.IsOnOpponentHex);
            Assert.IsTrue(match.CanTradeWithOpponent(item));

            match.TradeItemToOpponent(item);

            Assert.AreEqual(0, p1.Ship.HeldItems.Count);
            Assert.AreEqual(1, p2.Ship.HeldItems.Count);
            Assert.AreEqual(200 - item.Price + item.Price / 2, p1.Ship.Money); // paid full price, got half back
            Assert.AreEqual(200 - item.Price / 2, p2.Ship.Money); // paid half price
            Assert.AreEqual(0, p1.EasyEngagementWins);
            Assert.IsFalse(p1.HasWonMatch);
            Assert.IsFalse(match.IsInEngagement); // trading never starts a fight
        }

        [Test]
        public void PassAndPlay_BothPlayersActAcrossSeveralTurns_ActionBudgetsResetIndependently()
        {
            var (match, p1, p2) = BuildScenarioMatch();
            p1.Ship.AddMoney(100);

            // Turn 1 (p1): shop only, deliberately leaving an action spare.
            // Not rolling — shopping doesn't need a hand, and rolling now
            // spends an action of its own (see Match.RollDice), which
            // would leave nothing spare to assert below.
            Assert.AreEqual(p1, match.CurrentPlayer);
            var item = ItemPool.Items.First(i => i.Kind == ItemKind.Permanent && i.AffectedStat == CoreStat.Shields);
            match.BuyItem(item);
            Assert.AreEqual(1, match.ActionsRemaining);
            match.EndTurn();

            // Turn 2 (p2): a completely fresh budget, unaffected by p1's
            // spend — move toward p1.
            Assert.AreEqual(p2, match.CurrentPlayer);
            Assert.AreEqual(Match.ActionsPerTurn, match.ActionsRemaining);
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.Asteroids), new HexCoordinate(2, 0), new Random(1));
            match.EndTurn();

            // Turn 3 (p1): fresh budget again — move to meet p2 halfway.
            Assert.AreEqual(p1, match.CurrentPlayer);
            Assert.AreEqual(Match.ActionsPerTurn, match.ActionsRemaining);
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            match.EndTurn();

            // Turn 4 (p2): closes the gap, landing on p1's hex.
            Assert.AreEqual(p2, match.CurrentPlayer);
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(match.IsOnOpponentHex);
            Assert.AreEqual(1, p1.Ship.HeldItems.Count); // p1's turn-1 purchase persisted across every handoff
        }

        [Test]
        public void Move_OntoTradelane_DeductsTheTollAndTheMoveSessionStaysOpen()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.Tradelane));
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.ClearSpace));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.AddMoney(10);

            match.RollDice(new Random(1));
            var tollResult = match.Move(new RolledDie(0, TerrainType.Tradelane), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(tollResult.Success);
            Assert.AreEqual(10 - TollPricing.TradelaneTollPerHex, p1.Ship.Money);
            Assert.IsTrue(match.CanMove); // Move session stays open regardless of the toll

            var secondResult = match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(2, 0), new Random(1));

            Assert.IsTrue(secondResult.Success);
            Assert.AreEqual(1, match.ActionsRemaining); // both hexes were still just the one Move action
        }

        [Test]
        public void FullMatchToVictory_ThreeRealHardEngagementsFoughtOutInSequence()
        {
            var map = new GameMap(radius: 4, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard });
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard });
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard });
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            match.UnlockTier(EngagementTier.Hard); // this test is about grinding out real fights, not progression phasing
            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            var targets = new[] { new HexCoordinate(1, 0), new HexCoordinate(2, 0), new HexCoordinate(3, 0) };
            for (var i = 0; i < targets.Length; i++)
            {
                match.RollDice(new Random(1));
                match.Move(new RolledDie(0, TerrainType.ClearSpace), targets[i], new Random(1));

                Assert.IsTrue(match.IsInEngagement);
                PlayOutEngagement(match, new Random(1));
                Assert.AreEqual(EngagementOutcome.PlayerWon, match.ActiveEngagement.Outcome);

                match.ResolveActiveEngagement(new Random(1));

                if (i < targets.Length - 1)
                {
                    match.EndTurn(); // p2 passes
                    match.EndTurn(); // back to p1, fresh turn
                }
            }

            Assert.AreEqual(3, p1.HardEngagementWins);
            Assert.IsTrue(match.IsComplete);
            Assert.AreEqual(p1, match.Winner);
        }

        [Test]
        public void Discovery_BothPlayersLandOnTheSameMarkedHex_EachOnlySeesItOnceTheyveBeenThereThemselves()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.PlanetOrStarport));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(2, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape

            // p1 discovers it first, on their own turn — escapes so the
            // marker survives for p2 to find independently later.
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            Assert.IsTrue(match.IsInEngagement);
            match.ActiveEngagement.AttemptEscape(new Random(1));
            Assert.AreEqual(EngagementOutcome.PlayerEscaped, match.ActiveEngagement.Outcome);
            match.ResolveActiveEngagement(new Random(1));

            CollectionAssert.Contains(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(1, 0));
            Assert.IsFalse(p2.DiscoveredEngagementHexes.Contains(new HexCoordinate(1, 0)));

            match.EndTurn(); // now p2's turn

            // The marker is still physically there (p1 only escaped, didn't
            // win) but remains undiscovered on p2's own view.
            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(1, 0), out var hex));
            Assert.IsTrue(hex.HasEngagement);
            Assert.IsFalse(p2.DiscoveredEngagementHexes.Contains(new HexCoordinate(1, 0)));

            // p2 now lands on the same hex independently.
            p2.Ship.ApplyStatDelta(CoreStat.Speed, 20);
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            CollectionAssert.Contains(p2.DiscoveredEngagementHexes.ToList(), new HexCoordinate(1, 0));
        }

        [Test]
        public void PhaseGating_InertMediumMarkerStaysInertUntilAProgressionGoalUnlocksIt()
        {
            var map = new GameMap(radius: 4, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(1, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Medium });
            // Filler, so a randomly-targeted TravelAndPay goal has plenty
            // of other places to land besides the Medium marker itself.
            map.SetHex(new Hex(new HexCoordinate(2, -1), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(2, -2), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, -2), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(-1, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            Assert.AreEqual(EngagementTier.Easy, match.MaxUnlockedTier);

            // Win the two pre-placed Easy markers back to back — the
            // second win crosses the progression threshold and fires an
            // event (see MatchProgressionService).
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));
            PlayOutEngagement(match, new Random(1));
            match.ResolveActiveEngagement(new Random(1));
            match.EndTurn();
            match.EndTurn();

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, -1), new Random(1));
            PlayOutEngagement(match, new Random(1));
            match.ResolveActiveEngagement(new Random(1));

            Assert.IsNotNull(match.ActiveGoal);
            Assert.AreEqual(EngagementTier.Easy, match.MaxUnlockedTier, "An event firing alone doesn't unlock anything — only completing its goal does.");

            // The pre-placed Medium marker is one hop from here — landing
            // on it now must NOT trigger, even with an event already fired.
            match.EndTurn();
            match.EndTurn();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsFalse(match.IsInEngagement, "Medium is still locked — nothing above it can trigger yet.");
            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(1, 0), out var mediumHex));
            Assert.IsTrue(mediumHex.HasEngagement, "The marker must survive the visit, ready for once Medium unlocks.");
            CollectionAssert.DoesNotContain(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(1, 0),
                "A locked-tier marker should stay fully hidden on this visit, not just non-triggering.");

            // Complete whichever goal fired — the exact type is a coin
            // flip (see MatchProgressionService), so this handles both.
            var goal = match.ActiveGoal;
            match.Map.TryGetHex(goal.TargetHex, out var targetHex);
            var approach = HexMath.Neighbors(goal.TargetHex).First(n => match.Map.TryGetHex(n, out _));
            p1.Position = approach;
            if (goal.Type == MatchGoalType.TravelAndPay)
                p1.Ship.AddMoney(goal.MoneyRequired + 10); // buffer for a possible Tradelane toll — not present on this map, but keeps the pattern consistent

            // Still the same turn, same open Move session — no fresh roll
            // needed (or allowed) to keep moving.
            match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));

            if (match.IsInEngagement)
            {
                // Either a DefeatNamedTarget goal (its marker sits at the
                // player's current — still Easy — tier, so it triggers via
                // ordinary gating, no exemption involved) or TravelAndPay
                // happened to land straight on the now-freshly-unlocked
                // Medium marker in this same arrival.
                PlayOutEngagement(match, new Random(1));
                match.ResolveActiveEngagement(new Random(1));
            }

            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier);
            Assert.IsNull(match.ActiveGoal);

            if (!match.Map.TryGetHex(new HexCoordinate(1, 0), out var mediumHexAfter) || !mediumHexAfter.HasEngagement)
                return; // already resolved as part of completing the goal above

            // Revisit the same static marker now that Medium is unlocked.
            match.EndTurn();
            match.EndTurn();
            p1.Position = new HexCoordinate(1, -1);
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(match.IsInEngagement, "Medium is unlocked now — the same marker must finally trigger.");
            CollectionAssert.Contains(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(1, 0),
                "Now that Medium is unlocked, this visit should discover it same as any other marker.");
        }
    }
}
