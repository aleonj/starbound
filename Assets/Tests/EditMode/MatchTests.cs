using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;
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

            match.RollDice(new Random(1));

            Assert.IsTrue(match.CanDeliverJob);

            match.DeliverJob();

            Assert.AreEqual(60, p1.Ship.Money);
            Assert.IsNull(p1.ActiveJob);
            Assert.AreEqual(1, match.ActionsRemaining);
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
            match.RollDice(new Random(1));

            match.MineAsteroid();

            Assert.IsTrue(p1.HasMinedCargo);
            Assert.AreEqual(1, match.ActionsRemaining);
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
            match.RollDice(new Random(1));
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
            // action (e.g. moving) available afterward.
            var (match, p1, _) = BuildMatch();
            match.RollDice(new Random(1));
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
            var (match, _, _) = BuildMatch();
            match.RollDice(new Random(1));
            var job = new JobDefinition(JobType.Transport, new HexCoordinate(2, 0), 60);

            var result = match.AcceptJob(job);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, match.ActionsRemaining);
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
            match.RollDice(new Random(1));
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

            match.RollDice(new Random(1));
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

            match.RollDice(new Random(1));
            match.TradeItemToOpponent(item); // uses 1 of 2 actions
            Assert.AreEqual(1, match.ActionsRemaining);

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
    }
}
