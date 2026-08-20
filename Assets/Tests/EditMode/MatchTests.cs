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

            Assert.AreEqual(200, p1.Ship.Money);
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
        public void CanTravelWormhole_TrueEvenWhenNotOnAWormholeHex()
        {
            // Deliberately NOT tied to standing on a wormhole hex — an
            // expensive device purchase shouldn't be unusable for turns at
            // a time just because the player hasn't happened to land on
            // one. (0, 0) here is ordinary ClearSpace.
            var (match, p1, _) = BuildMatch();
            p1.Ship.TryAddItem(ItemPool.WormholeDevice);

            Assert.IsTrue(match.CanTravelWormhole);
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
        // EngagementsPerProgressionEvent's threshold on the second win, so
        // whatever event fires is decided by resolutionSeed (the only rng
        // MatchProgressionService.FireEvent actually consumes here, since
        // the first win doesn't cross the threshold). Leaves p1 as
        // CurrentPlayer with a fresh, unspent action budget, sitting at
        // (1,-1).
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
        public void MinefieldDamageVariable_EnteringAMinesHex_DealsOneHullDamage()
        {
            var (match, p1, _) = BuildMatchWithVariable(MatchVariable.MinefieldDamage);

            match.RollDice(new Random(1));
            match.Move(new RolledDie(0, TerrainType.Mines), new HexCoordinate(1, 0), new Random(1));

            Assert.AreEqual(Ship.DefaultStatValue - 1, p1.Ship.GetStat(CoreStat.Hull));
        }

        [Test]
        public void TradeBoomVariable_EnteringATradelaneHex_WaivesTheToll()
        {
            var (match, p1, _) = BuildMatchWithVariable(MatchVariable.TradeBoom);
            Assert.AreEqual(0, p1.Ship.Money); // no starting money — an unwaived toll would block this move outright

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
