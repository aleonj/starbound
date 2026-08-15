using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;
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
            match.EnterMarket();

            var item = ItemPool.Items.First(i => i.Kind == ItemKind.Permanent && i.AffectedStat == CoreStat.Weapons);
            var purchase = ShopService.TryPurchase(p1.Ship, item);
            Assert.IsTrue(purchase.Success);
            Assert.AreEqual(3 + item.StatDelta, p1.Ship.GetStat(CoreStat.Weapons));

            var sale = ShopService.TrySell(p1.Ship, item);
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
            Assert.AreEqual(0, p2.Ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(0, p2.Ship.Money); // zero-Hull penalty wiped them out
        }

        [Test]
        public void FullMatchToVictory_ThirdHardWinEndsTheMatchAndBlocksFurtherActions()
        {
            var (match, p1, _) = BuildScenarioMatch();
            p1.RecordEngagementWin(EngagementTier.Hard);
            p1.RecordEngagementWin(EngagementTier.Hard);
            match.Map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard });

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
    }
}
