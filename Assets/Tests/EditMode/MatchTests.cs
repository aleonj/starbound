using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Movement;
using StarBound.Multiplayer;

namespace StarBound.Tests
{
    public class MatchTests
    {
        private static (Match match, Player p1, Player p2) BuildMatch(int radius = 2)
        {
            var map = new GameMap(radius, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(-1, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.PlanetOrStarport));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            return (new Match(map, p1, p2), p1, p2);
        }

        [Test]
        public void Move_SecondVoluntaryMoveInSameTurn_Throws()
        {
            var (match, _, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.Throws<InvalidOperationException>(() =>
                match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(0, 0), new Random(1)));
        }

        [Test]
        public void AfterEngagementOutcomeDecided_StillBlocksActionsUntilResolved()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            match.ActiveEngagement.AttemptEscape(new Random(1));

            // Outcome is decided (PlayerEscaped) but not yet resolved —
            // this used to incorrectly stop blocking here, which is the
            // root cause of the "escape doesn't force a move" bug.
            Assert.IsTrue(match.IsInEngagement);
            Assert.IsFalse(match.CanEndTurn);
            Assert.Throws<InvalidOperationException>(() => match.EndTurn());

            match.ResolveActiveEngagement();

            Assert.IsFalse(match.IsInEngagement);
        }

        [Test]
        public void RollDice_ThenMove_Succeeds_AndTurnCanEnd()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));

            var result = match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(new HexCoordinate(1, 0), p1.Position);
            Assert.IsTrue(match.CanEndTurn);
        }

        [Test]
        public void RollDice_CalledTwiceInSameTurn_Throws()
        {
            var (match, _, _) = BuildMatch();
            match.RollDice(new Random(1));

            Assert.Throws<InvalidOperationException>(() => match.RollDice(new Random(1)));
        }

        [Test]
        public void Move_WithoutRollingFirst_Throws()
        {
            var (match, _, _) = BuildMatch();

            Assert.Throws<InvalidOperationException>(() =>
                match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1)));
        }

        [Test]
        public void EndTurn_SwitchesCurrentPlayerAndDiscardsHand()
        {
            var (match, _, p2) = BuildMatch();
            match.RollDice(new Random(1));

            match.EndTurn();

            Assert.AreEqual(p2, match.CurrentPlayer);
            Assert.IsNull(match.CurrentHand);
        }

        [Test]
        public void Move_OntoEngagementHex_StartsActiveEngagement()
        {
            var (match, _, _) = BuildMatch();
            match.RollDice(new Random(1));

            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            Assert.IsNotNull(match.ActiveEngagement);
        }

        [Test]
        public void WhileInEngagement_MoveAndEndTurnAreBlocked()
        {
            var (match, _, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.Throws<InvalidOperationException>(() =>
                match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(-1, 0), new Random(1)));
            Assert.IsFalse(match.CanEndTurn);
            Assert.Throws<InvalidOperationException>(() => match.EndTurn());
        }

        [Test]
        public void ResolveActiveEngagement_PlayerWins_RecordsWinAndClearsMarker()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress)
                match.ActiveEngagement.ResolveRound(new Random(1));

            match.ResolveActiveEngagement();

            Assert.IsFalse(match.IsInEngagement);
            Assert.IsNull(match.ActiveEngagement);
            Assert.AreEqual(1, p1.EasyEngagementWins);
            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(0, -1), out var hex));
            Assert.IsFalse(hex.HasEngagement);
        }

        [Test]
        public void ResolveActiveEngagement_ThirdHardWin_EndsMatchWithWinner()
        {
            var (match, p1, _) = BuildMatch();
            p1.RecordEngagementWin(EngagementTier.Hard);
            p1.RecordEngagementWin(EngagementTier.Hard);
            match.Map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard });

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress)
                match.ActiveEngagement.ResolveRound(new Random(1));

            match.ResolveActiveEngagement();

            Assert.IsTrue(match.IsComplete);
            Assert.AreEqual(p1, match.Winner);
        }

        [Test]
        public void ResolveActiveEngagement_PlayerLoses_AppliesIntegrityPenaltyWhenDepleted()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            var opponent = match.ActiveEngagement.Opponent;
            opponent.ApplyStatDelta(CoreStat.Weapons, 20);
            opponent.ApplyStatDelta(CoreStat.Shields, 20);
            opponent.ApplyStatDelta(CoreStat.Speed, 20);

            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress)
                match.ActiveEngagement.ResolveRound(new Random(1));

            Assert.AreEqual(EngagementOutcome.PlayerLost, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement();

            Assert.AreEqual(0, p1.Ship.Money);
            Assert.AreEqual(new HexCoordinate(2, 0), p1.Position);
        }

        [Test]
        public void ResolveActiveEngagement_PlayerEscapes_RequiresMoveBeforeEndingTurn()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);
            match.ActiveEngagement.AttemptEscape(new Random(1));

            Assert.AreEqual(EngagementOutcome.PlayerEscaped, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement();

            Assert.IsFalse(match.CanEndTurn);
            Assert.Throws<InvalidOperationException>(() => match.EndTurn());

            match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(-1, 0), new Random(1));

            Assert.IsTrue(match.CanEndTurn);
            Assert.DoesNotThrow(() => match.EndTurn());
        }

        [Test]
        public void ResolveActiveEngagement_PlayerEscapes_WaivesMoveRequirementWhenNoLegalMoveExists()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            // Deliberately the ONLY hex on the map — ShipMover never checks
            // whether the 'from' hex exists, only that the target does, so
            // the initial move still works. Every neighbor of (0, -1),
            // including (0, 0) itself, is absent from the map, so no die
            // (whatever it rolled) can find a legal target there.

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            match.ActiveEngagement.AttemptEscape(new Random(1));
            match.ResolveActiveEngagement();

            Assert.IsFalse(match.MustMoveAfterEscape);
            Assert.IsTrue(match.CanEndTurn);
        }

        [Test]
        public void CanShop_BlockedAfterEngagementResolvesThisTurn_ResetsNextTurn()
        {
            var (match, p1, _) = BuildMatch();
            Assert.IsTrue(match.CanShop);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress)
                match.ActiveEngagement.ResolveRound(new Random(1));

            match.ResolveActiveEngagement();

            Assert.IsFalse(match.CanShop);

            match.EndTurn();

            Assert.IsTrue(match.CanShop);
        }
    }
}
