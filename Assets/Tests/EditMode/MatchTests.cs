using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Map;
using StarBound.Movement;
using StarBound.Multiplayer;
using StarBound.Shop;

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
        public void Move_OntoEngagementHex_MarksItDiscoveredForTheMovingPlayer()
        {
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));

            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            CollectionAssert.Contains(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(0, -1));
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
            match.UnlockTier(EngagementTier.Hard); // this test is about the win-counting/victory rule, not progression phasing

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
            // Integrity penalty clears money/items and resets Hull back to
            // the default (not left stuck at 0 — see Ship.ResetIntegrityStats).
            Assert.AreEqual(Ship.DefaultStatValue, p2.Ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(0, p2.Ship.Money);
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

            // New turn — fresh budget, and CanShop no longer requires
            // having rolled dice (see CanShop's doc comment).
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

            // (2, 0) is ordinary ClearSpace, not a planet — movement stays
            // open the whole turn once paid for, regardless of terrain.
            Assert.IsTrue(match.CanMove);
        }

        [Test]
        public void BuyItem_AfterMoving_ClosesTheMoveSessionEvenWithNoActionsSpentOnItAgain()
        {
            // "Move to a planet, then buy something" from the user's turn-
            // economy rule. Move paid for its own action, but choosing to
            // shop afterward closes that Move session out for the rest of
            // the turn — movement doesn't come back even though shopping
            // only spent the turn's other action.
            var (match, p1) = BuildPlanetContinuationMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));

            Assert.AreEqual(1, match.ActionsRemaining);

            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.AddMoney(50);
            var result = match.BuyItem(item);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, match.ActionsRemaining);
            Assert.IsFalse(match.CanMove);
            Assert.Throws<InvalidOperationException>(() =>
                match.Move(new RolledDie(1, TerrainType.ClearSpace), new HexCoordinate(2, 0), new Random(1)));
        }

        [Test]
        public void CanShop_TrueEvenBeforeRollingDice()
        {
            // Covers starting a turn already docked (e.g. match start, per
            // MatchFactory's per-player planet starts, or having ended a
            // prior turn on a planet) — a player who doesn't intend to
            // move at all shouldn't be forced to roll movement dice just
            // to shop. The action budget (not a dice-roll prerequisite) is
            // what stops a "free" turn.
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(1, 0) };
            var match = new Match(map, p1, p2);

            Assert.IsTrue(match.IsCurrentPlayerOnPlanet);
            Assert.IsNull(match.CurrentHand);
            Assert.IsTrue(match.CanShop);

            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.AddMoney(50);
            var result = match.BuyItem(item);

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void UseItem_ConsumableUsableMidEngagement()
        {
            var (match, p1, _) = BuildMatch();
            var item = new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 100, ItemKind.Consumable);
            p1.Ship.TryAddItem(item);
            p1.Ship.ApplyStatDelta(CoreStat.Hull, -2); // Hull = 1, so the effect is visible

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            Assert.IsTrue(match.CanUseItem(item));

            match.UseItem(item);

            Assert.AreEqual(3, p1.Ship.GetStat(CoreStat.Hull));
            Assert.AreEqual(0, p1.Ship.HeldItems.Count);
        }

        [Test]
        public void CanUseItem_FalseForAPermanentItem()
        {
            var (match, p1, _) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);

            Assert.IsFalse(match.CanUseItem(item));
        }

        [Test]
        public void UseItem_ThrowsWhenNotHeld()
        {
            var (match, _, _) = BuildMatch();
            var item = new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 100, ItemKind.Consumable);

            Assert.Throws<InvalidOperationException>(() => match.UseItem(item));
        }

        [Test]
        public void TradeItemToOpponent_TransfersItemAndPaysHalfPrice()
        {
            var (match, p1, p2) = BuildMatch(); // p1 and p2 share a hex
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            p2.Ship.AddMoney(100);

            Assert.IsTrue(match.CanTradeWithOpponent(item));

            match.TradeItemToOpponent(item);

            Assert.AreEqual(0, p1.Ship.HeldItems.Count);
            Assert.AreEqual(1, p2.Ship.HeldItems.Count);
            Assert.AreEqual(25, p1.Ship.Money); // received half price
            Assert.AreEqual(75, p2.Ship.Money); // paid half price
        }

        [Test]
        public void CanTradeWithOpponent_FalseWhenOpponentCargoFull()
        {
            var (match, p1, p2) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            for (var i = 0; i < p2.Ship.CargoCapacity; i++)
                p2.Ship.TryAddItem(new ItemDefinition($"Filler {i}", CoreStat.Speed, 1, 10, ItemKind.Permanent));

            Assert.IsFalse(match.CanTradeWithOpponent(item));
        }

        [Test]
        public void CanTradeWithOpponent_FalseWhenOpponentCannotAfford()
        {
            var (match, p1, _) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item); // p2 has 0 money by default

            Assert.IsFalse(match.CanTradeWithOpponent(item));
        }

        [Test]
        public void DeliverJob_AwardsRewardClearsJobAndPaysForOneAction()
        {
            var (match, p1, _) = BuildMatch();
            var job = new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 60);
            p1.AcceptJob(job);
            p1.Position = new HexCoordinate(2, 0);

            // Deliberately not rolling first — CanDeliverJob/DeliverJob
            // don't require it (see CanShop's own doc comment), and
            // rolling now spends the Move session's action (see
            // Match.RollDice), which would eat into the "one action still
            // available afterward" this test is actually about.
            Assert.IsTrue(match.CanDeliverJob);

            match.DeliverJob();

            Assert.AreEqual(60, p1.Ship.Money);
            Assert.IsNull(p1.ActiveJob);
            Assert.AreEqual(1, match.ActionsRemaining);

            // CanMove requires an actual rolled hand, not just a fresh
            // action — roll now, right before checking, since this
            // assertion is specifically about a move being genuinely
            // available afterward, not just budget being left.
            match.RollDice(new Random(1));
            Assert.IsTrue(match.CanMove); // one action still available, e.g. to move afterward
        }

        [Test]
        public void CanDeliverJob_FalseForABountyJobEvenAtTheDestinationHex()
        {
            var (match, p1, _) = BuildMatch();
            var job = new JobDefinition(JobType.BountyHunting, new HexCoordinate(0, -1), 50, EngagementTier.Easy);
            p1.AcceptJob(job);
            p1.Position = new HexCoordinate(0, -1);
            match.RollDice(new Random(1));

            Assert.IsFalse(match.CanDeliverJob);
        }

        [Test]
        public void CanMineAsteroid_TrueWhenOnAsteroidsWithAnUnminedMiningJob()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Asteroids));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));
            match.RollDice(new Random(1));

            Assert.IsTrue(match.CanMineAsteroid);
        }

        [Test]
        public void CanMineAsteroid_FalseWhenNotOnAnAsteroidsHex()
        {
            var (match, p1, _) = BuildMatch(); // (0,0) is ClearSpace
            p1.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(2, 0), 60));
            match.RollDice(new Random(1));

            Assert.IsFalse(match.CanMineAsteroid);
        }

        [Test]
        public void MineAsteroid_SetsHasMinedCargoAndPaysForOneAction()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Asteroids));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));

            // Not rolling first — CanMineAsteroid/MineAsteroid don't
            // require it, and rolling now spends the Move session's own
            // action (see Match.RollDice), which this test's "one action
            // still available" assertion below is specifically about.
            match.MineAsteroid();

            Assert.IsTrue(p1.HasMinedCargo);
            Assert.AreEqual(1, match.ActionsRemaining);

            // Same reasoning as DeliverJob's own fix above — CanMove needs
            // an actual rolled hand, not just budget.
            match.RollDice(new Random(1));
            Assert.IsTrue(match.CanMove); // one action still available
        }

        [Test]
        public void MineAsteroid_ThrowsWhenNotEligible()
        {
            var (match, p1, _) = BuildMatch();
            p1.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(2, 0), 60));
            match.RollDice(new Random(1));

            Assert.Throws<InvalidOperationException>(() => match.MineAsteroid());
        }

        [Test]
        public void CanDeliverJob_FalseForMiningJobUntilCargoIsMined()
        {
            var (match, p1, _) = BuildMatch();
            var job = new JobDefinition(JobType.Mining, new HexCoordinate(2, 0), 60);
            p1.AcceptJob(job);
            p1.Position = new HexCoordinate(2, 0);
            match.RollDice(new Random(1));

            Assert.IsFalse(match.CanDeliverJob);

            p1.MarkCargoMined();

            Assert.IsTrue(match.CanDeliverJob);
        }

        [Test]
        public void ResolveActiveEngagement_BountyJobWon_AwardsRewardAndClearsJob()
        {
            var (match, p1, _) = BuildMatch();
            p1.AcceptJob(new JobDefinition(JobType.BountyHunting, new HexCoordinate(0, -1), 200, EngagementTier.Easy));

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

            Assert.AreEqual(EngagementOutcome.PlayerWon, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            // 200 (bounty reward) + 15 (the plain per-kill reward every
            // Easy-tier PvE win now also pays — see
            // EngagementSession.DefeatRewardMoney) — the two are
            // separate, additive rewards, not mutually exclusive.
            Assert.AreEqual(215, p1.Ship.Money);
            Assert.IsNull(p1.ActiveJob);
        }

        [Test]
        public void ResolveActiveEngagement_BountyJobEscaped_AppliesPenaltyAndClearsJob()
        {
            var (match, p1, _) = BuildMatch();
            p1.AcceptJob(new JobDefinition(JobType.BountyHunting, new HexCoordinate(0, -1), 200, EngagementTier.Easy));

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            match.ActiveEngagement.AttemptEscape(new Random(1));

            Assert.AreEqual(EngagementOutcome.PlayerEscaped, match.ActiveEngagement.Outcome);

            match.ResolveActiveEngagement(new Random(1));

            Assert.AreEqual(0, p1.Ship.Money); // started at 0, penalty floors at 0 rather than going negative
            Assert.IsNull(p1.ActiveJob);
        }

        [Test]
        public void ResolveActiveEngagement_BountyJobLost_ClearsJobWithNoExtraPenalty()
        {
            var (match, p1, _) = BuildMatch();
            p1.AcceptJob(new JobDefinition(JobType.BountyHunting, new HexCoordinate(0, -1), 200, EngagementTier.Easy));

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

            Assert.IsNull(p1.ActiveJob);
        }

        // p1 holds the Wormhole Device by default — CanTravelWormhole now
        // requires it unconditionally, regardless of position.
        private static (Match match, Player p1) BuildWormholeMatch()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            p1.Ship.TryAddItem(ItemPool.WormholeDevice);

            return (new Match(map, p1, p2), p1);
        }

        [Test]
        public void CanTravelWormhole_TrueWhenStandingOnAWormholeWithDiceRolled()
        {
            var (match, _) = BuildWormholeMatch();
            match.RollDice(new Random(1));

            Assert.IsTrue(match.CanTravelWormhole);
        }

        [Test]
        public void CanTravelWormhole_TrueEvenBeforeRollingDice()
        {
            // A player who doesn't intend to move shouldn't be forced to
            // roll movement dice just to travel.
            var (match, _) = BuildWormholeMatch();

            Assert.IsNull(match.CurrentHand);
            Assert.IsTrue(match.CanTravelWormhole);
        }

        [Test]
        public void CanTravelWormhole_FalseWhenNotOnAWormholeHex()
        {
            // Reaching a wormhole hex is now just ordinary movement —
            // MatchHud grants a guaranteed Wormhole-terrain die each roll
            // to anyone holding the device (see OnRollDiceClicked), which
            // flows through the normal Move/ShipMover.TryMove pipeline
            // (already gated on the device, independently of this
            // property). CanTravelWormhole only governs the SECOND half —
            // warping onward once actually standing on one. (0, 0) here is
            // ordinary ClearSpace.
            var (match, p1, _) = BuildMatch();
            p1.Ship.TryAddItem(ItemPool.WormholeDevice);

            Assert.IsFalse(match.CanTravelWormhole);
        }

        [Test]
        public void CanTravelWormhole_FalseWithoutTheDevice()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.Wormhole));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);

            Assert.IsFalse(match.CanTravelWormhole);
        }

        [Test]
        public void OtherWormholeDestinations_ExcludesTheCurrentHex()
        {
            var (match, _) = BuildWormholeMatch();

            CollectionAssert.AreEquivalent(
                new[] { new HexCoordinate(3, 0) },
                match.OtherWormholeDestinations.ToList());
        }

        [Test]
        public void TravelToWormhole_UpdatesPositionAndPaysForOneAction()
        {
            var (match, p1) = BuildWormholeMatch();
            match.RollDice(new Random(1));

            match.TravelToWormhole(new HexCoordinate(3, 0), new Random(1));

            Assert.AreEqual(new HexCoordinate(3, 0), p1.Position);
            Assert.AreEqual(1, match.ActionsRemaining);
            Assert.IsTrue(match.CanMove); // one action still available
        }

        [Test]
        public void TravelToWormhole_TwiceInOneTurn_SecondJumpIsFreeAsPartOfTheSameMoveSession()
        {
            // Wormhole travel shares the Move session — a second jump
            // right after the first is just more movement, not a
            // separate action, same as spending a second die would be.
            var map = new GameMap(radius: 6, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(6, 0), TerrainType.Wormhole));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.TryAddItem(ItemPool.WormholeDevice);
            match.RollDice(new Random(1));

            match.TravelToWormhole(new HexCoordinate(3, 0), new Random(1));
            Assert.AreEqual(1, match.ActionsRemaining);

            match.TravelToWormhole(new HexCoordinate(6, 0), new Random(1));

            Assert.AreEqual(new HexCoordinate(6, 0), p1.Position);
            Assert.AreEqual(1, match.ActionsRemaining); // still just the one Move action
            Assert.IsTrue(match.CanTravelWormhole); // session stays open — could jump again
        }

        [Test]
        public void TravelToWormhole_ThenDieMove_BothShareTheSameMoveAction()
        {
            // Mixing a wormhole jump with ordinary dice movement in either
            // order within one turn should still be just the one action.
            var map = new GameMap(radius: 6, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(4, 0), TerrainType.ClearSpace));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.TryAddItem(ItemPool.WormholeDevice);
            match.RollDice(new Random(1));

            match.TravelToWormhole(new HexCoordinate(3, 0), new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(4, 0), new Random(1));

            Assert.AreEqual(new HexCoordinate(4, 0), p1.Position);
            Assert.AreEqual(1, match.ActionsRemaining);
        }

        [Test]
        public void TravelToWormhole_ThrowsForANonWormholeDestination()
        {
            var (match, _) = BuildWormholeMatch();
            match.RollDice(new Random(1));

            Assert.Throws<InvalidOperationException>(() =>
                match.TravelToWormhole(new HexCoordinate(1, 0), new Random(1)));
        }

        [Test]
        public void TravelToWormhole_ThrowsForTheCurrentHexAsDestination()
        {
            var (match, _) = BuildWormholeMatch();
            match.RollDice(new Random(1));

            Assert.Throws<InvalidOperationException>(() =>
                match.TravelToWormhole(new HexCoordinate(0, 0), new Random(1)));
        }

        [Test]
        public void TravelToWormhole_ThrowsWhenNotEligible()
        {
            var (match, _) = BuildWormholeMatch();
            match.AttackOpponent(); // p1/p2 share (0,0) in this fixture — starts a fight, which blocks travel

            Assert.Throws<InvalidOperationException>(() =>
                match.TravelToWormhole(new HexCoordinate(3, 0), new Random(1)));
        }

        [Test]
        public void TravelToWormhole_DiscoversAndTriggersAnEngagementAtTheDestination()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Wormhole));
            map.SetHex(new Hex(new HexCoordinate(3, 0), TerrainType.Wormhole) { Engagement = EngagementTier.Easy });
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.Ship.TryAddItem(ItemPool.WormholeDevice);
            match.RollDice(new Random(1));

            match.TravelToWormhole(new HexCoordinate(3, 0), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            CollectionAssert.Contains(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(3, 0));
        }

        [Test]
        public void BuyItem_SecondPurchaseInSameVisit_IsFree()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.AddMoney(1000);
            var item1 = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            var item2 = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);

            match.BuyItem(item1);
            Assert.AreEqual(1, match.ActionsRemaining);

            match.BuyItem(item2);

            Assert.AreEqual(1, match.ActionsRemaining); // still just the one bundled market action
        }

        [Test]
        public void AcceptJob_AfterAlreadyShopping_SpendsTheOtherAction()
        {
            // Buying something and accepting a job are two separate
            // actions, not one bundled market visit — matches the
            // playtesting report that this combo shouldn't leave a third
            // action (e.g. moving) available afterward. Not rolling dice
            // here — rolling now spends an action of its own (see
            // Match.RollDice), and Buy+AcceptJob already exhausts the
            // 2-action budget on their own.
            var (match, p1, _) = BuildMatch();
            p1.Ship.AddMoney(50);
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            match.BuyItem(item);
            Assert.AreEqual(1, match.ActionsRemaining);

            var job = new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 60);
            var result = match.AcceptJob(job);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, match.ActionsRemaining);
            Assert.IsFalse(match.CanMove);
        }

        [Test]
        public void AcceptJob_BeforeMoving_StillLeavesMoveAvailable()
        {
            // Rolled AFTER accepting, not before — rolling now spends the
            // Move session's own action (see Match.RollDice), so rolling
            // first would make AcceptJob the one that runs out of budget
            // instead of the other way around. This order is exactly what
            // the test name is about: accept the job with your first
            // action, then roll and confirm the second is still there for
            // an actual move.
            var (match, _, _) = BuildMatch();
            var job = new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 60);

            var result = match.AcceptJob(job);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, match.ActionsRemaining);

            match.RollDice(new Random(1));
            Assert.IsTrue(match.CanMove);
        }

        [Test]
        public void Move_ThenBuyItem_ThenAcceptJob_IsBlockedOnTheThirdAction()
        {
            // Reproduces the exact playtesting reports: move + buy leaves
            // no room for a job, and buy + job leaves no room to move.
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, 0), new Random(1));
            p1.Ship.AddMoney(50);
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            match.BuyItem(item);

            Assert.AreEqual(0, match.ActionsRemaining);
            Assert.IsFalse(match.CanAcceptJob);
            Assert.Throws<InvalidOperationException>(() =>
                match.AcceptJob(new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 60)));
        }

        [Test]
        public void BuyItem_Fails_DoesNotSpendAnAction()
        {
            var (match, _, _) = BuildMatch();
            var tooExpensive = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 999_999, ItemKind.Permanent);

            var result = match.BuyItem(tooExpensive);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(Match.ActionsPerTurn, match.ActionsRemaining);
        }

        [Test]
        public void ActionBudget_ExhaustedAfterTwoActions_BlocksEveryRemainingAction()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.Asteroids));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.PlanetOrStarport));
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var match = new Match(map, p1, p2);
            p1.AcceptJob(new JobDefinition(JobType.Mining, new HexCoordinate(1, 0), 50));
            var tradedItem = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(tradedItem);
            p1.Ship.TryAddItem(ItemPool.WormholeDevice); // so the CanTravelWormhole check below is meaningful
            p2.Ship.AddMoney(100);

            // Not rolling dice — none of the actions below require a hand,
            // and rolling now spends the Move session's own action (see
            // Match.RollDice), which would leave only one of these two
            // actions affordable instead of both.
            match.TradeItemToOpponent(tradedItem); // action 1
            Assert.AreEqual(1, match.ActionsRemaining);

            match.MineAsteroid(); // action 2

            Assert.AreEqual(0, match.ActionsRemaining);
            Assert.IsFalse(match.CanMove);
            Assert.IsFalse(match.CanShop);
            Assert.IsFalse(match.CanAcceptJob);
            Assert.IsFalse(match.CanAttackOpponent); // still shares a hex with p2
            Assert.IsFalse(match.CanTravelWormhole);

            var anotherItem = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(anotherItem);
            Assert.IsFalse(match.CanTradeWithOpponent(anotherItem));

            // Using a consumable is the one thing that stays free regardless.
            var consumable = new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 100, ItemKind.Consumable);
            p1.Ship.TryAddItem(consumable);
            Assert.IsTrue(match.CanUseItem(consumable));
        }

        [Test]
        public void Move_TriggersEngagement_ZeroesActionsRemainingRegardlessOfPriorSpend()
        {
            var (match, p1, p2) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            p2.Ship.AddMoney(100);

            match.TradeItemToOpponent(item); // uses 1 of 2 actions
            Assert.AreEqual(1, match.ActionsRemaining);

            // Rolled here, right before moving, rather than at the top —
            // rolling now spends the Move session's own action (see
            // Match.RollDice), so rolling before the trade would have made
            // the trade the one that runs out of budget instead.
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            Assert.AreEqual(0, match.ActionsRemaining); // ambush spends whatever's left, not just +1
        }

        [Test]
        public void AttackOpponent_ZeroesActionsRemainingEvenWithNoPriorSpend()
        {
            var (match, _, _) = BuildMatch();

            match.AttackOpponent();

            Assert.AreEqual(0, match.ActionsRemaining);
        }

        [Test]
        public void TradeItemToOpponent_SpendsOneAction()
        {
            var (match, p1, p2) = BuildMatch();
            var item = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item);
            p2.Ship.AddMoney(100);

            match.TradeItemToOpponent(item);

            Assert.AreEqual(1, match.ActionsRemaining);
        }

        [Test]
        public void TradeItemToOpponent_Twice_SpendsBothActions()
        {
            var (match, p1, p2) = BuildMatch();
            var item1 = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            var item2 = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item1);
            p1.Ship.TryAddItem(item2);
            p2.Ship.AddMoney(100);

            match.TradeItemToOpponent(item1);
            Assert.AreEqual(1, match.ActionsRemaining);

            match.TradeItemToOpponent(item2);

            Assert.AreEqual(0, match.ActionsRemaining);
            Assert.IsFalse(match.CanTradeWithOpponent(item2));
        }

        [Test]
        public void UseItem_StillWorksAfterActionBudgetIsExhausted()
        {
            var (match, p1, p2) = BuildMatch();
            var item1 = new ItemDefinition("Weapon Upgrade", CoreStat.Weapons, 1, 50, ItemKind.Permanent);
            var item2 = new ItemDefinition("Shield Booster", CoreStat.Shields, 1, 50, ItemKind.Permanent);
            p1.Ship.TryAddItem(item1);
            p1.Ship.TryAddItem(item2);
            p2.Ship.AddMoney(100);
            match.TradeItemToOpponent(item1);
            match.TradeItemToOpponent(item2);
            Assert.AreEqual(0, match.ActionsRemaining);

            var consumable = new ItemDefinition("Repair Kit", CoreStat.Hull, 2, 100, ItemKind.Consumable);
            p1.Ship.TryAddItem(consumable);
            p1.Ship.ApplyStatDelta(CoreStat.Hull, -2);

            Assert.IsTrue(match.CanUseItem(consumable));
            match.UseItem(consumable);

            Assert.AreEqual(3, p1.Ship.GetStat(CoreStat.Hull));
        }

        // --- Progression (MatchProgressionService) ---

        // Two Easy markers, both reachable from one another and from
        // (1,-1) — see the neighbor math worked out for this layout —
        // plus a Mines and a Tradelane hex for exercising each of the two
        // starter variables once an event has fired.
        private static (Match match, Player p1, Player p2) BuildProgressionMatch()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(1, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.Mines));
            map.SetHex(new Hex(new HexCoordinate(1, -2), TerrainType.Tradelane));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };

            return (new Match(map, p1, p2), p1, p2);
        }

        // Grinds out a guaranteed win against whatever NPC is currently
        // active — same stat-boost technique as ResolveActiveEngagement_PlayerWins_RecordsWinAndClearsMarker.
        private static void ForceWin(Match match)
        {
            var rng = new Random(1);
            var rounds = 0;
            while (match.ActiveEngagement.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                match.ActiveEngagement.ResolveInitiative(rng);
                match.ActiveEngagement.ResolveAttack(rng);
                rounds++;
            }
        }

        // Wins the two pre-placed Easy engagements back to back — crossing
        // EngagementsPerProgressionEvent's (goal) AND EngagementsPerVariableEvent's
        // (variable, decoupled but same starting count/threshold so they
        // still cross together here) thresholds on the second win, so
        // whatever goal/variable fires is decided by resolutionSeed (the
        // only rng FireGoalEvent/PickVariable actually consume here,
        // since the first win doesn't cross either threshold). Leaves p1
        // as CurrentPlayer with a fresh, unspent action budget, sitting
        // at (1,-1).
        private static void WinTwoEasyEngagementsAndFireAnEvent(Match match, Player p1, int resolutionSeed)
        {
            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));
            ForceWin(match);
            match.ResolveActiveEngagement(new Random(1));

            match.EndTurn(); // p2's turn
            match.EndTurn(); // back to p1, fresh actions

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(1, -1), new Random(1));
            ForceWin(match);
            match.ResolveActiveEngagement(new Random(resolutionSeed));

            match.EndTurn();
            match.EndTurn();
        }

        // MatchProgressionService's variable/goal-type picks are flat
        // coin flips, not a stat contest — there's no boost to force a
        // specific outcome the way ForceWin does for combat, so this
        // searches for a resolution seed that lands on the desired
        // variable instead. ~100 tries makes a false failure astronomically
        // unlikely for a 1-in-2 draw.
        private static (Match match, Player p1, Player p2) BuildMatchWithVariable(MatchVariable desired)
        {
            for (var seed = 1; seed <= 100; seed++)
            {
                var (match, p1, p2) = BuildProgressionMatch();
                WinTwoEasyEngagementsAndFireAnEvent(match, p1, seed);
                if (match.ActiveVariable == desired)
                    return (match, p1, p2);
            }

            Assert.Fail($"Never rolled {desired} in 100 attempts — check MatchProgressionService's variable table.");
            return default;
        }

        private static (Match match, Player p1, Player p2) BuildMatchWithGoalType(MatchGoalType desired)
        {
            for (var seed = 1; seed <= 100; seed++)
            {
                var (match, p1, p2) = BuildProgressionMatch();
                WinTwoEasyEngagementsAndFireAnEvent(match, p1, seed);
                if (match.ActiveGoal.Type == desired)
                    return (match, p1, p2);
            }

            Assert.Fail($"Never rolled {desired} in 100 attempts — check MatchProgressionService's goal-type draw.");
            return default;
        }

        [Test]
        public void Move_OntoMarkerAboveMaxUnlockedTier_DoesNotTriggerAndMarkerStays()
        {
            var (match, p1, _) = BuildMatch(); // MaxUnlockedTier starts at Easy
            match.Map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Medium });
            match.RollDice(new Random(1));

            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1));

            Assert.IsFalse(match.IsInEngagement, "A Medium marker shouldn't trigger while only Easy is unlocked.");
            Assert.IsTrue(match.Map.TryGetHex(new HexCoordinate(0, -1), out var hex));
            Assert.IsTrue(hex.HasEngagement, "The marker should stay in place for once Medium unlocks.");
            CollectionAssert.DoesNotContain(p1.DiscoveredEngagementHexes.ToList(), new HexCoordinate(0, -1),
                "A locked-tier marker should stay fully hidden, not just non-triggering.");
        }

        [Test]
        public void ProgressionEvent_FiresAfterTwoSuccessfulEngagements()
        {
            var (match, p1, _) = BuildProgressionMatch();

            WinTwoEasyEngagementsAndFireAnEvent(match, p1, resolutionSeed: 1);

            Assert.AreNotEqual(MatchVariable.None, match.ActiveVariable);
            Assert.IsNotNull(match.ActiveGoal);
            Assert.AreEqual(EngagementTier.Easy, match.MaxUnlockedTier, "Firing the event alone shouldn't advance the phase — only completing its goal does.");
        }

        [Test]
        public void ProgressionEvent_FiresAfterTwoWins_NotifiesBothPlayers()
        {
            var (match, p1, p2) = BuildProgressionMatch();

            WinTwoEasyEngagementsAndFireAnEvent(match, p1, resolutionSeed: 1);

            // Both players, not just whoever won the fight — ActiveVariable
            // is shared match state (see Match.HandleProgressionOnEngagementWin).
            Assert.IsFalse(string.IsNullOrEmpty(p1.PendingVariableEventNotice));
            Assert.IsFalse(string.IsNullOrEmpty(p2.PendingVariableEventNotice));
            StringAssert.Contains(MatchVariableDescriptions.Describe(match.ActiveVariable), p1.PendingVariableEventNotice);
        }

        [Test]
        public void ProgressionEvent_FiresAfterTwoWins_NotifiesBothPlayersOfTheNewGoalToo()
        {
            // User-reported: the variable event got announced but the goal
            // that fires in lockstep with it (see
            // HandleProgressionOnEngagementWin) did not — same gap
            // PendingVariableEventNotice closed, now closed for goals too.
            var (match, p1, p2) = BuildProgressionMatch();

            WinTwoEasyEngagementsAndFireAnEvent(match, p1, resolutionSeed: 1);

            Assert.IsFalse(string.IsNullOrEmpty(p1.PendingGoalNotice));
            Assert.IsFalse(string.IsNullOrEmpty(p2.PendingGoalNotice));
            StringAssert.Contains(MatchGoalDescriptions.Describe(match.Map, match.ActiveGoal), p1.PendingGoalNotice);
        }

        [Test]
        public void VariableEvent_KeepsFiringPastHardTier_EvenThoughGoalEventsStopThere()
        {
            // User-requested: variable events should be decoupled from
            // goal events entirely — in particular, they must NOT get
            // stuck permanently once Hard tier unlocks and stops goal
            // events from firing (MaxUnlockedTier < Hard gates goal
            // firing, deliberately NOT variable firing — see
            // HandleProgressionOnEngagementWin's own comment).
            var (match, p1, _) = BuildProgressionMatch();
            match.UnlockTier(EngagementTier.Hard);

            WinTwoEasyEngagementsAndFireAnEvent(match, p1, resolutionSeed: 1);

            Assert.AreNotEqual(MatchVariable.None, match.ActiveVariable,
                "Variable events should still fire even though Hard tier is already unlocked.");
            Assert.IsNull(match.ActiveGoal,
                "Goal events stay gated on MaxUnlockedTier < Hard — unaffected by decoupling variable events from that gate.");
        }

        // Mines/Asteroids damage is a per-arrival chance now (see
        // HazardChances), not a guaranteed hit — these retry move-rng
        // seeds until they see the outcome under test, rather than
        // assuming a specific seed always lands one way. Same retry
        // idiom BuildMatchWithVariable/BuildMatchWithGoalType above
        // already use for a different kind of randomness.
        [Test]
        public void Mines_CanDealHullDamageOnArrival()
        {
            for (var seed = 1; seed <= 200; seed++)
            {
                var (match, p1, _) = BuildProgressionMatch(); // ActiveVariable defaults to None — baseline chance applies
                match.RollDice(new Random(1));
                var result = match.Move(new RolledDie(0, TerrainType.Mines), new HexCoordinate(1, 0), new Random(seed));

                if (p1.Ship.GetStat(CoreStat.Hull) == Ship.DefaultStatValue - 1)
                {
                    // The caller (MatchHud) needs this to actually tell
                    // the player damage happened — it used to be taken
                    // completely silently.
                    Assert.IsTrue(result.HazardHit);
                    return;
                }
            }

            Assert.Fail("Never rolled hull damage in 200 attempts at the baseline 1-in-5 Mines chance.");
        }

        [Test]
        public void Mines_CanAvoidHullDamageOnArrival()
        {
            // The old behavior made Mines damage guaranteed once a
            // specific variable was active and impossible otherwise —
            // this confirms the new baseline chance genuinely isn't
            // guaranteed either way.
            for (var seed = 1; seed <= 200; seed++)
            {
                var (match, p1, _) = BuildProgressionMatch();
                match.RollDice(new Random(1));
                var result = match.Move(new RolledDie(0, TerrainType.Mines), new HexCoordinate(1, 0), new Random(seed));

                if (p1.Ship.GetStat(CoreStat.Hull) == Ship.DefaultStatValue)
                {
                    Assert.IsFalse(result.HazardHit);
                    return;
                }
            }

            Assert.Fail("Hull damage occurred in all 200 attempts — Mines shouldn't be a guaranteed hit at a 1-in-5 chance.");
        }

        [Test]
        public void Asteroids_CanDealHullDamageOnArrival()
        {
            for (var seed = 1; seed <= 300; seed++)
            {
                var map = new GameMap(radius: 2, Difficulty.Medium);
                map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace));
                map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.Asteroids));
                var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
                var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3)) { Position = new HexCoordinate(0, 0) };
                var match = new Match(map, p1, p2);

                match.RollDice(new Random(1));
                match.Move(new RolledDie(0, TerrainType.Asteroids), new HexCoordinate(1, 0), new Random(seed));

                if (p1.Ship.GetStat(CoreStat.Hull) == Ship.DefaultStatValue - 1)
                    return;
            }

            Assert.Fail("Never rolled hull damage in 300 attempts at the 1-in-10 Asteroids chance.");
        }

        [Test]
        public void MinefieldDamageVariable_RaisesTheOddsOfHullDamageOnArrival()
        {
            // Boosted to 1 in 2 while the alert is active — still a
            // chance, not guaranteed, so this retries whole match setups
            // (each already its own retry loop to land MinefieldDamage —
            // see BuildMatchWithVariable) until one attempt lands a hit.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var (match, p1, _) = BuildMatchWithVariable(MatchVariable.MinefieldDamage);
                match.RollDice(new Random(1));
                match.Move(new RolledDie(0, TerrainType.Mines), new HexCoordinate(1, 0), new Random(attempt + 1));

                if (p1.Ship.GetStat(CoreStat.Hull) == Ship.DefaultStatValue - 1)
                    return;
            }

            Assert.Fail("Never rolled hull damage in 30 attempts at the boosted 1-in-2 Minefield Alert chance.");
        }

        [Test]
        public void TradeBoomVariable_EnteringATradelaneHex_WaivesTheToll()
        {
            var (match, p1, _) = BuildMatchWithVariable(MatchVariable.TradeBoom);
            // BuildMatchWithVariable wins 2 Easy PvE fights to fire the
            // event (see WinTwoEasyEngagementsAndFireAnEvent), which now
            // also pays the plain per-kill reward — spent back down to 0
            // here so the toll-waiver proof below still holds: with ANY
            // money left, a successful move wouldn't prove the toll was
            // actually waived, just that the toll (only 1) was affordable
            // regardless.
            p1.Ship.TrySpendMoney(p1.Ship.Money);
            Assert.AreEqual(0, p1.Ship.Money); // no money — an unwaived toll would block this move outright

            match.RollDice(new Random(1));
            var result = match.Move(new RolledDie(0, TerrainType.Tradelane), new HexCoordinate(1, -2), new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, p1.Ship.Money);
        }

        [Test]
        public void TravelAndPayGoal_CompletingIt_AwardsRewardAdvancesPhaseAndKeepsTheVariable()
        {
            var (match, p1, _) = BuildMatchWithGoalType(MatchGoalType.TravelAndPay);
            var goal = match.ActiveGoal;
            var variableBeforeCompletion = match.ActiveVariable;

            var approach = HexMath.Neighbors(goal.TargetHex).First(n => match.Map.TryGetHex(n, out _));
            match.Map.TryGetHex(goal.TargetHex, out var targetHex);
            p1.Position = approach;
            // A bit more than MoneyRequired: if the target happens to be
            // the Tradelane hex and TradeBoom isn't the active variable,
            // the toll gets deducted by ShipMover before the goal's own
            // money check runs — this buffer keeps the test robust to
            // that combination rather than asserting an exact amount.
            p1.Ship.AddMoney(goal.MoneyRequired + 10);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));

            Assert.IsNull(match.ActiveGoal);
            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier);
            Assert.GreaterOrEqual(p1.Ship.Money, goal.RewardMoney);
            Assert.AreEqual(variableBeforeCompletion, match.ActiveVariable, "The variable persists until the *next* event, not until the goal is claimed.");
        }

        [Test]
        public void DefeatNamedTargetGoal_CompletingIt_AwardsRewardAndAdvancesPhase()
        {
            var (match, p1, _) = BuildMatchWithGoalType(MatchGoalType.DefeatNamedTarget);
            var goal = match.ActiveGoal;
            Assert.AreEqual(EngagementTier.Easy, match.MaxUnlockedTier);
            match.Map.TryGetHex(goal.TargetHex, out var targetHex);
            Assert.AreEqual(EngagementTier.Easy, targetHex.Engagement, "The goal's own marker is placed at the player's CURRENT tier — a winnable fight, not a preview of the next one.");
            p1.Ship.AddMoney(10); // covers a possible Tradelane toll on the approach — see the TravelAndPay test for why

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));
            Assert.IsTrue(match.IsInEngagement, "The goal's own marker triggers via ordinary gating — it's never above MaxUnlockedTier.");

            ForceWin(match);
            match.ResolveActiveEngagement(new Random(1));

            Assert.IsNull(match.ActiveGoal);
            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier);
            Assert.GreaterOrEqual(p1.Ship.Money, goal.RewardMoney);
        }

        [Test]
        public void DefeatNamedTargetGoal_TriggeringIt_MarksTheSessionAsTheGoalTarget()
        {
            // User-reported: found the goal's marked engagement but "the
            // engagement screen didn't mention it." See
            // Match.HandleArrival's own comment on why this is gated to
            // DefeatNamedTarget specifically, not just "coordinates
            // happen to match" (the companion TravelAndPay test below
            // confirms the negative case).
            var (match, p1, _) = BuildMatchWithGoalType(MatchGoalType.DefeatNamedTarget);
            var goal = match.ActiveGoal;
            match.Map.TryGetHex(goal.TargetHex, out var targetHex);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));

            Assert.IsTrue(match.IsInEngagement);
            Assert.IsTrue(match.ActiveEngagement.IsGoalTarget);
        }

        [Test]
        public void TravelAndPayGoal_CoincidentallyOverlappingAnEngagementHex_DoesNotMarkTheSessionAsTheGoalTarget()
        {
            // TravelAndPay's target hex has no exclusion against already-
            // marked hexes (see MatchProgressionService.BuildTravelAndPayGoal),
            // so it COULD coincidentally be one — but fighting (and
            // winning) that engagement does nothing toward completing a
            // TravelAndPay goal, only arrival + having enough money does.
            // Tagging the fight screen "Race Goal" there would wrongly
            // imply this fight matters for it (see Match.HandleArrival's
            // own comment). Retrofitting an Easy marker onto the goal's
            // own target hex — whichever hex MatchProgressionService
            // happened to pick — deterministically constructs the
            // coincidence regardless of seed. Deliberately left with 0
            // money (the default) — enough to trigger the engagement, not
            // enough to ALSO complete the goal on this same arrival (see
            // TryCompleteTravelAndPayGoal), which would null out
            // ActiveGoal before the engagement check even runs and mask
            // what this test is actually trying to isolate: the Type
            // check, not just "no goal was active any more."
            var (match, p1, _) = BuildMatchWithGoalType(MatchGoalType.TravelAndPay);
            var goal = match.ActiveGoal;
            match.Map.TryGetHex(goal.TargetHex, out var targetHex);
            targetHex.Engagement = EngagementTier.Easy;

            p1.Position = HexMath.Neighbors(goal.TargetHex).First(n => match.Map.TryGetHex(n, out _));
            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));

            Assert.IsNotNull(match.ActiveGoal, "The goal must still be active (insufficient funds to complete it) for this test to actually isolate the Type check.");
            Assert.IsTrue(match.IsInEngagement, "The retrofitted Easy marker should still trigger normally.");
            Assert.IsFalse(match.ActiveEngagement.IsGoalTarget);
        }

        // --- Tier-unlock readiness gate (Match.CompleteGoal / IsReadyForTier) ---

        private static void SetAllPerformanceStats(Ship ship, int value)
        {
            foreach (var stat in new[] { CoreStat.Weapons, CoreStat.Shields, CoreStat.Speed })
                ship.ApplyStatDelta(stat, value - ship.GetStat(stat));
        }

        // Completes whichever goal type MatchProgressionService happened
        // to draw, teleporting p1 to an existing neighbor of the goal's
        // target rather than assuming p1 is already adjacent (same
        // technique as TravelAndPayGoal_CompletingIt_... above), so
        // callers don't need to track exact positions through a second
        // progression cycle. applyFinalStats runs immediately before the
        // step that actually triggers CompleteGoal's readiness check
        // (ResolveActiveEngagement for DefeatNamedTarget, or the Move call
        // itself for TravelAndPay) — letting a test guarantee the fight's
        // own win with one set of stats and then test the gate with a
        // deliberately different one, since a DefeatNamedTarget win and a
        // low readiness stat would otherwise conflict.
        private static void CompleteActiveGoal(Match match, Player p1, Action applyFinalStats = null)
        {
            var goal = match.ActiveGoal;
            match.Map.TryGetHex(goal.TargetHex, out var targetHex);
            p1.Position = HexMath.Neighbors(goal.TargetHex).First(n => match.Map.TryGetHex(n, out _));

            if (goal.Type == MatchGoalType.TravelAndPay)
            {
                p1.Ship.AddMoney(goal.MoneyRequired + 10);
                applyFinalStats?.Invoke();
                match.RollDice(new Random(1));
                match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));
            }
            else
            {
                p1.Ship.AddMoney(10); // covers a possible Tradelane toll on the approach
                p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
                p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
                p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guarantee the win itself
                match.RollDice(new Random(1));
                match.Move(new RolledDie(0, targetHex.Terrain), goal.TargetHex, new Random(1));
                ForceWin(match);
                applyFinalStats?.Invoke(); // set the stats actually under test, right before the gate reads them
                match.ResolveActiveEngagement(new Random(1));
            }
        }

        // Reaches Medium (its floor is 3 — always trivially met at
        // baseline, see EngagementDefinitionTable) then fires a SECOND
        // progression event there, leaving an ActiveGoal whose completion
        // is the one that actually attempts to unlock Hard (floor 5) —
        // exactly the transition IsReadyForTier exists to gate. Re-arms
        // the two Easy markers at their original coordinates for the
        // second win-counting cycle, since the originals were cleared by
        // the first.
        private static (Match match, Player p1, Player p2) BuildMatchWithPendingHardUnlockGoal(int seed)
        {
            var (match, p1, p2) = BuildProgressionMatch();
            WinTwoEasyEngagementsAndFireAnEvent(match, p1, resolutionSeed: seed);
            CompleteActiveGoal(match, p1);
            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier);

            // This first transition also sets PendingTierUnlockNotice on
            // both players (see CompleteGoal) — consumed here to simulate
            // the UI already having shown it, so callers testing the
            // SECOND (Hard) transition start from a clean slate rather
            // than seeing this stale "Medium unlocked" leftover.
            p1.ConsumePendingTierUnlockNotice();
            p2.ConsumePendingTierUnlockNotice();

            match.EndTurn();
            match.EndTurn();
            p1.Position = new HexCoordinate(0, 0);
            match.Map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });
            match.Map.SetHex(new Hex(new HexCoordinate(1, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });

            WinTwoEasyEngagementsAndFireAnEvent(match, p1, resolutionSeed: seed);
            Assert.IsNotNull(match.ActiveGoal, "Firing the second event alone shouldn't advance the phase either.");
            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier);

            return (match, p1, p2);
        }

        [Test]
        public void CompleteGoal_NeitherPlayerMeetsNextTierFloor_DelaysUnlockButStillPaysReward()
        {
            var (match, p1, p2) = BuildMatchWithPendingHardUnlockGoal(seed: 1);
            var hardFloor = EngagementDefinitionTable.For(EngagementTier.Hard).WeaponsRange.Min;
            var reward = match.ActiveGoal.RewardMoney;
            var moneyBefore = p1.Ship.Money;

            CompleteActiveGoal(match, p1, applyFinalStats: () =>
            {
                SetAllPerformanceStats(p1.Ship, hardFloor - 1);
                SetAllPerformanceStats(p2.Ship, hardFloor - 1);
            });

            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier,
                "Neither player meets the Hard floor yet — the shared ceiling shouldn't rise.");
            Assert.IsNull(match.ActiveGoal,
                "The goal itself is still claimed even though the tier didn't advance — this delays, not blocks, progress.");
            Assert.GreaterOrEqual(p1.Ship.Money, moneyBefore + reward);
            Assert.IsNull(p1.PendingTierUnlockNotice, "No unlock happened — neither player should be notified of one.");
            Assert.IsNull(p2.PendingTierUnlockNotice);
        }

        [Test]
        public void CompleteGoal_OnlyAwardeeMeetsNextTierFloor_StillDoesNotAdvanceSharedTier()
        {
            var (match, p1, p2) = BuildMatchWithPendingHardUnlockGoal(seed: 1);
            var hardFloor = EngagementDefinitionTable.For(EngagementTier.Hard).WeaponsRange.Min;

            CompleteActiveGoal(match, p1, applyFinalStats: () =>
            {
                SetAllPerformanceStats(p1.Ship, hardFloor + 5); // the awardee is well-geared
                SetAllPerformanceStats(p2.Ship, hardFloor - 1); // the other player isn't
            });

            Assert.AreEqual(EngagementTier.Medium, match.MaxUnlockedTier,
                "MaxUnlockedTier is shared match state — one player's gear can't open Hard on the other's behalf.");
            Assert.IsNull(p1.PendingTierUnlockNotice, "No unlock happened — neither player should be notified of one.");
            Assert.IsNull(p2.PendingTierUnlockNotice);
        }

        [Test]
        public void CompleteGoal_BothPlayersMeetNextTierFloor_AdvancesSharedTier()
        {
            var (match, p1, p2) = BuildMatchWithPendingHardUnlockGoal(seed: 1);
            var hardFloor = EngagementDefinitionTable.For(EngagementTier.Hard).WeaponsRange.Min;

            CompleteActiveGoal(match, p1, applyFinalStats: () =>
            {
                SetAllPerformanceStats(p1.Ship, hardFloor);
                SetAllPerformanceStats(p2.Ship, hardFloor);
            });

            Assert.AreEqual(EngagementTier.Hard, match.MaxUnlockedTier);
            Assert.AreEqual("Hard engagements are now unlocked!", p1.PendingTierUnlockNotice,
                "Both players should be notified, not just the awardee — the ceiling is shared.");
            Assert.AreEqual("Hard engagements are now unlocked!", p2.PendingTierUnlockNotice);
        }

        // --- Energy as a movement-die economy ([Combat] Energy overhaul) ---

        [Test]
        public void RollDice_WhenEnergyIsZero_ThrowsAndCanRollDiceIsFalse()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue);

            Assert.IsFalse(match.CanRollDice);
            Assert.Throws<InvalidOperationException>(() => match.RollDice(new Random(1)));
        }

        [Test]
        public void Move_DieIndexAtOrBeyondCurrentEnergy_FailsWithNotEnoughEnergy()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -3); // Energy = 2 — only die indices 0 and 1 are usable
            match.RollDice(new Random(1));

            // The target coordinate is irrelevant here — ShipMover.TryMove
            // checks Energy before adjacency/terrain, same ordering as
            // the existing DieAlreadySpent check it sits next to.
            var lockedDie = new RolledDie(dieIndex: 2, TerrainType.ClearSpace);
            var result = match.Move(lockedDie, new HexCoordinate(99, 99), new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailureReason.NotEnoughEnergy, result.FailureReason);
        }

        [Test]
        public void Move_DieIndexWithinCurrentEnergy_Succeeds()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -3); // Energy = 2 — die index 0 is still usable
            match.RollDice(new Random(1));

            var usableDie = new RolledDie(dieIndex: 0, TerrainType.ClearSpace);
            var result = match.Move(usableDie, new HexCoordinate(1, 0), new Random(1));

            Assert.IsTrue(result.Success);
        }

        [Test]
        public void EndTurn_NeverRolled_BanksNothing()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -3); // Energy = 2

            match.EndTurn();

            Assert.AreEqual(2, p1.Ship.GetStat(CoreStat.Energy),
                "Never rolling means no dice were ever made available to under-use — nothing to bank.");
        }

        [Test]
        public void EndTurn_RolledButDidNotMove_BanksOnlyTheDiceWithinTheEnergyCap()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -3); // Energy = 2 — only die indices 0 and 1 were ever usable

            match.RollDice(new Random(1)); // all 5 dice stay unspent — never moved

            match.EndTurn();

            // Only indices 0 and 1 (2 dice) were ever within the Energy
            // cap — indices 2-4 were never usable regardless of choice,
            // so they don't count toward "left unused." min(2, MaxEnergyValue(5) - 2) = 2 banked.
            Assert.AreEqual(4, p1.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void EndTurn_UsedSomeDiceWithinTheEnergyCap_BanksOnlyTheRemainingCappedOnes()
        {
            // Regression coverage for a user-reported bug: Energy = 3
            // (3 usable dice), 2 of those 3 used, 1 left unused — banking
            // used to count ALL 5 rolled dice as "unspent" (including the
            // 2 beyond the cap that were never usable at all), landing at
            // full Energy (5) instead of the correct 3 + 1 = 4.
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -2); // Energy = 3 — dice 0-2 are within the cap, 3-4 never were

            // Marks the REAL hand's dice spent directly, rather than
            // moving through Match.Move with throwaway RolledDie objects
            // (as other tests in this file do for convenience) — Move
            // doesn't actually require the die it's given to be a member
            // of currentHand, so a synthetic die's MarkSpent() never
            // touches the real hand this test needs EndTurn to read.
            var hand = match.RollDice(new Random(1));
            hand.Dice[0].MarkSpent();
            hand.Dice[1].MarkSpent(); // index 2 — the third capped die — is the only one genuinely left unused

            match.EndTurn();

            Assert.AreEqual(4, p1.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void EndTurn_EngagementOccurredThisTurn_DoesNotBankTheDiceItLeftUnspent()
        {
            // An ambush exhausts the rest of the turn's actions (see
            // Match.HandleArrival/ExhaustActions) — the 4 dice left
            // unspent here were never a genuine choice to hold back, so
            // they must not bank Energy the way a deliberately-ended
            // turn with dice to spare would (see
            // Match.ApplyEnergyForEndingTurn's own comment).
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -3); // Energy = 2 — well below max, so a bank would be visible if it happened
            p1.Ship.ApplyStatDelta(CoreStat.Weapons, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Shields, 20);
            p1.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed win within ForceWin's round cap

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.ClearSpace), new HexCoordinate(0, -1), new Random(1)); // ambush
            Assert.IsTrue(match.IsInEngagement);

            ForceWin(match);
            match.ResolveActiveEngagement(new Random(1));
            match.EndTurn();

            Assert.AreEqual(2, p1.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void EndTurn_WhenTurnStartedAtZeroEnergy_FloorResetsInsteadOfBanking()
        {
            var (match, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue); // Energy = 0, drained mid-turn

            match.EndTurn(); // p1's turn ends — it started healthy, so ordinary banking applies (no roll, no change)
            Assert.AreEqual(0, p1.Ship.GetStat(CoreStat.Energy));

            match.EndTurn(); // p2's turn ends — p1's NEXT turn now snapshots Energy=0 at its start
            Assert.IsFalse(match.CanRollDice, "0 Energy at turn start blocks rolling — no movement is possible this turn.");

            match.EndTurn(); // p1's turn ends — THIS turn started at 0, so the floor-reset applies instead

            Assert.AreEqual(2, p1.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void IsIntegrityDepleted_EnergyZero_NeverTriggersDestructionViaNormalPlay()
        {
            // End-to-end guard against a regression — Hull alone can
            // destroy a ship now (see Ship.IsIntegrityDepleted), Energy
            // hitting 0 through ordinary play (brace/failed escape) must
            // not put the match into a destroyed state.
            var (_, p1, _) = BuildMatch();
            p1.Ship.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue);

            Assert.IsFalse(p1.Ship.IsIntegrityDepleted);
        }

        // --- Persistent per-planet shop (PlanetShopService) ---

        private static (Match match, Player p1, Player p2) BuildPlanetShopMatch()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport));
            map.SetHex(new Hex(new HexCoordinate(1, 0), TerrainType.ClearSpace));

            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3, startingMoney: 1000)) { Position = new HexCoordinate(0, 0) };
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3, startingMoney: 1000)) { Position = new HexCoordinate(0, 0) };

            return (new Match(map, p1, p2), p1, p2);
        }

        [Test]
        public void GetShopOffer_NotOnAPlanet_Throws()
        {
            var (match, p1, _) = BuildPlanetShopMatch();
            p1.Position = new HexCoordinate(1, 0);

            Assert.Throws<InvalidOperationException>(() => match.GetShopOffer(new Random(1)));
        }

        [Test]
        public void BuyItem_OnAPlanet_RemovesItemFromTheShelfImmediately()
        {
            var (match, p1, _) = BuildPlanetShopMatch();
            var offer = match.GetShopOffer(new Random(1));
            var bought = offer[0];

            var result = match.BuyItem(bought);

            Assert.IsTrue(result.Success);
            var afterPurchase = match.GetShopOffer(new Random(2));
            Assert.AreEqual(ShopOfferGenerator.OfferSize - 1, afterPurchase.Count);
            CollectionAssert.DoesNotContain(afterPurchase.ToList(), bought);
        }

        [Test]
        public void BuyItem_ThenSameTurn_ShelfStaysShortUntilEndTurn()
        {
            var (match, p1, p2) = BuildPlanetShopMatch();
            var offer = match.GetShopOffer(new Random(1));
            match.BuyItem(offer[0]);

            // Still the same turn — no fresh action to spend, but the
            // shelf query itself doesn't cost one either.
            var stillShort = match.GetShopOffer(new Random(2));
            Assert.AreEqual(ShopOfferGenerator.OfferSize - 1, stillShort.Count);

            match.EndTurn(); // p2's turn — same shared shelf
            var refilled = match.GetShopOffer(new Random(3));
            Assert.AreEqual(ShopOfferGenerator.OfferSize, refilled.Count);
        }

        [Test]
        public void BuyItem_WormholeDevice_DoesNotDisturbThePersistentShelf()
        {
            var (match, p1, _) = BuildPlanetShopMatch();
            var offer = match.GetShopOffer(new Random(1)).ToList();

            var result = match.BuyItem(ItemPool.WormholeDevice);

            Assert.IsTrue(result.Success);
            var afterPurchase = match.GetShopOffer(new Random(2)).ToList();
            CollectionAssert.AreEqual(offer, afterPurchase);
        }
    }
}
