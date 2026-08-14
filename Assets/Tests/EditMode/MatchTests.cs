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

        private static (Match match, Player p1) BuildPlanetContinuationMatch()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.PlanetOrStarport)); // adjacent to origin
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.ClearSpace)); // adjacent to the planet

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            return (new Match(map, p1, p2), p1);
        }

        [Test]
        public void Move_MultipleTimesInSameTurn_ChainsSuccessfully()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));

            var first = match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            var second = match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(2, 0), new Random(1));

            Assert.IsTrue(first.Success);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(new HexCoordinate(2, 0), p1.Position);
            Assert.IsTrue(match.CanMove); // still open — ordinary/planet terrain never locks movement on its own
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

            match.ResolveActiveEngagement(new Random(1));

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

            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }

            match.ResolveActiveEngagement(new Random(1));

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

            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }

            match.ResolveActiveEngagement(new Random(1));

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

            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerLost, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            Assert.AreEqual(0, p1.Ship.Money);
            Assert.AreEqual(new HexCoordinate(2, 0), p1.Position);

            // Losing didn't defeat the opponent — it's still guarding the hex.
            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(0, -1), out var engagementHex));
            Assert.AreEqual(EngagementTier.Easy, engagementHex.Engagement);
        }

        [Test]
        public void ResolveActiveEngagement_PlayerEscapes_LeavesMarkerOnTheMap()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            match.ActiveEngagement.AttemptEscape(new Random(1));

            Assert.AreEqual(EngagementOutcome.PlayerEscaped, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(0, -1), out var engagementHex));
            Assert.AreEqual(EngagementTier.Easy, engagementHex.Engagement);
        }

        [Test]
        public void CanAttackOpponent_TrueWhenPlayersShareAHex()
        {
            var (match, _, _) = BuildMatch(); // p1 and p2 both start at (0,0)

            Assert.IsTrue(match.IsOnOpponentHex);
            Assert.IsTrue(match.CanAttackOpponent);
        }

        [Test]
        public void AttackOpponent_WhenNotSharingAHex_Throws()
        {
            var (match, _, p2) = BuildMatch();
            p2.Position = new HexCoordinate(1, 0);

            Assert.IsFalse(match.CanAttackOpponent);
            Assert.Throws<InvalidOperationException>(() => match.AttackOpponent());
        }

        [Test]
        public void AttackOpponent_StartsAPvPEngagementAgainstTheOtherPlayersRealShip()
        {
            var (match, _, p2) = BuildMatch();

            match.AttackOpponent();

            Assert.IsTrue(match.IsInEngagement);
            Assert.IsTrue(match.ActiveEngagement.IsPvP);
            Assert.AreSame(p2.Ship, match.ActiveEngagement.Opponent);
            Assert.IsFalse(match.CanMove);
        }

        [Test]
        public void ResolveActiveEngagement_PvPPlayerWins_DoesNotRecordTierWinAndAppliesPenaltyToDefeatedOpponent()
        {
            var (match, p1, p2) = BuildMatch();
            match.AttackOpponent();

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);
            p2.Ship.AddMoney(50);

            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            Assert.AreEqual(0, p1.EasyEngagementWins);
            Assert.IsFalse(p1.HasWonMatch);
            Assert.AreEqual(0, p2.Ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(0, p2.Ship.Money); // integrity penalty clears money/items
            Assert.AreEqual(new HexCoordinate(2, 0), p2.Position); // relocated to nearest planet
        }

        [Test]
        public void ResolveActiveEngagement_PvP_DoesNotClearAnUnrelatedEngagementMarkerOnTheSharedHex()
        {
            var (match, p1, _) = BuildMatch();
            match.Map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });

            match.AttackOpponent();
            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }

            match.ResolveActiveEngagement(new Random(1));

            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(0, 0), out var hex));
            Assert.AreEqual(EngagementTier.Easy, hex.Engagement);
        }

        [Test]
        public void ResolveActiveEngagement_PlayerEscapes_RelocatesToARandomAdjacentHexWithoutASeparateMove()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            match.ActiveEngagement.AttemptEscape(new Random(1));

            Assert.AreEqual(EngagementOutcome.PlayerEscaped, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            // Relocated automatically — the only two engagement-free
            // neighbors of (0, -1) on this map are (-1, 0) and (0, 0).
            var validDestinations = new[] { new HexCoordinate(-1, 0), new HexCoordinate(0, 0) };
            CollectionAssert.Contains(validDestinations, p1.Position);
            Assert.IsTrue(match.CanEndTurn);
            Assert.DoesNotThrow(() => match.EndTurn());
        }

        [Test]
        public void ResolveActiveEngagement_PlayerEscapes_StaysPutWhenNoSafeAdjacentHexExists()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            // Deliberately the ONLY hex on the map — ShipMover never checks
            // whether the 'from' hex exists, only that the target does, so
            // the initial move still works. Every neighbor of (0, -1),
            // including (0, 0) itself, is absent from the map, so there's
            // nowhere for the escape relocation to send the player.

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            match.ActiveEngagement.AttemptEscape(new Random(1));
            match.ResolveActiveEngagement(new Random(1));

            Assert.AreEqual(new HexCoordinate(0, -1), p1.Position); // nowhere to go — stayed put
            Assert.IsTrue(match.CanEndTurn);
        }

        [Test]
        public void CanShop_BlockedAfterEngagementResolvesThisTurn_ResetsNextTurn()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            Assert.IsTrue(match.CanShop);

            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }

            match.ResolveActiveEngagement(new Random(1));

            Assert.IsFalse(match.CanShop);

            match.EndTurn();

            Assert.IsFalse(match.CanShop); // new turn — must roll dice first
            match.RollDice(new Random(1));
            Assert.IsTrue(match.CanShop);
        }

        [Test]
        public void Move_OntoPlanetWithNoEngagement_LeavesMovementOpen()
        {
            var (match, _) = BuildPlanetContinuationMatch();
            match.RollDice(new Random(1));

            var result = match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(match.CanMove);
            Assert.DoesNotThrow(() =>
                match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(2, 0), new Random(1)));
        }

        [Test]
        public void Move_OntoOrdinaryTerrainAfterPlanet_StillLeavesMovementOpen()
        {
            var (match, _) = BuildPlanetContinuationMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(2, 0), new Random(1));

            // (2, 0) is ordinary ClearSpace, not a planet — movement should
            // still be open, since only an engagement or EnterMarket locks it.
            Assert.IsTrue(match.CanMove);
        }

        [Test]
        public void EnterMarket_LocksMovementForRestOfTurn()
        {
            var (match, _) = BuildPlanetContinuationMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(match.CanMove);

            match.EnterMarket();

            Assert.IsFalse(match.CanMove);
            Assert.Throws<InvalidOperationException>(() =>
                match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(2, 0), new Random(1)));
            Assert.IsTrue(match.CanEndTurn);
        }

        [Test]
        public void CanShop_FalseBeforeRollingDice_EvenWhenAlreadyOnAPlanet()
        {
            // Covers starting a turn already docked (e.g. match start, per
            // MatchFactory's per-player planet starts, or having ended a
            // prior turn on a planet) — shopping still requires taking an
            // action (rolling dice) first, not just standing on the hex.
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(1, 0) };
            var match = new Match(map, p1, p2);

            Assert.IsTrue(match.IsCurrentPlayerOnPlanet);
            Assert.IsFalse(match.CanShop);
            Assert.Throws<InvalidOperationException>(() => match.EnterMarket());

            match.RollDice(new Random(1));

            Assert.IsTrue(match.CanShop);
            Assert.DoesNotThrow(() => match.EnterMarket());
        }
    }
}
