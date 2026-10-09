using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class EngagementSessionTests
    {
        private static Player CreatePlayer() => new("p1", "Test Player", new Ship(cargoCapacity: 3));

        private static void BoostAllPerformanceStats(Ship ship, int amount)
        {
            ship.ApplyStatDelta(CoreStat.Weapons, amount);
            ship.ApplyStatDelta(CoreStat.Shields, amount);
            ship.ApplyStatDelta(CoreStat.Speed, amount);
        }

        [Test]
        public void ResolveInitiative_SetsIsAwaitingAttackResolutionAndPendingAttacker()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var initiative = session.ResolveInitiative(new Random(2));

            Assert.IsTrue(session.IsAwaitingAttackResolution);
            Assert.AreEqual(initiative.Attacker, session.PendingAttacker);
        }

        [Test]
        public void ResolveInitiative_CalledTwiceInSameRound_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            session.ResolveInitiative(new Random(1));

            Assert.Throws<InvalidOperationException>(() => session.ResolveInitiative(new Random(1)));
        }

        [Test]
        public void ResolveAttack_WithoutResolvingInitiativeFirst_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);

            Assert.Throws<InvalidOperationException>(() => session.ResolveAttack(new Random(1)));
        }

        [Test]
        public void ResolveAttack_OpponentHullReachesZero_PlayerWinsAndRecordsTierWin()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed hit whenever player has initiative
            var opponent = new Ship(cargoCapacity: 0); // default Hull = 3

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            // Loop rather than a fixed 3 calls: an occasional natural-1
            // fumble can hand the opponent a (harmless, since Shields=23)
            // wasted round, so the exact round count isn't fixed.
            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.AreEqual(1, player.HardEngagementWins);
            // User-reported: a plain kill paid out with no confirmation
            // of what was awarded. LastKillRewardMoney is what the UI
            // reads to build that confirmation (see EngagementScreen.
            // DescribeOutcome) — tier-scaled, see DefeatRewardMoney.
            Assert.AreEqual(60, session.LastKillRewardMoney);
        }

        [Test]
        public void ResolveAttack_PvPOpponentHullReachesZero_PlayerWinsButNoTierWinIsRecorded()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed hit whenever player has initiative
            var opponentShip = new Ship(cargoCapacity: 0); // default Hull = 3, stands in for the other player's real ship

            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);
            var rng = new Random(1);

            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.AreEqual(0, player.HardEngagementWins);
            Assert.IsFalse(player.HasWonMatch);
            Assert.IsNull(session.LastKillRewardMoney, "PvP wins don't pay the plain-kill reward — see ApplyAttackOutcome's own IsPvP gate.");
        }

        [Test]
        public void ResolveAttack_PlayerHullReachesZero_PlayerLoses()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            BoostAllPerformanceStats(opponent, 20); // opponent guaranteed to win initiative and hit every round

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            for (var i = 0; i < 3; i++)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
            }

            Assert.AreEqual(EngagementOutcome.PlayerLost, session.Outcome);
            Assert.IsTrue(player.Ship.IsIntegrityDepleted);
        }

        [Test]
        public void ResolveInitiative_AfterOutcomeDecided_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20);
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.ResolveAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.Throws<InvalidOperationException>(() => session.ResolveInitiative(rng));
            Assert.Throws<InvalidOperationException>(() => session.ResolveAttack(rng));
        }

        [Test]
        public void AttemptEscape_Success_EndsEngagement()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            player.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            var result = session.AttemptEscape(new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(EngagementOutcome.PlayerEscaped, session.Outcome);
        }

        [Test]
        public void AttemptEscape_Failure_EnergyLostAndRoundContinues()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape failure

            var session = new EngagementSession(definition, player, opponent);
            var result = session.AttemptEscape(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
            Assert.AreEqual(Ship.MaxEnergyValue - 1, player.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void AttemptEscape_FailureAtZeroEnergy_NoLongerEndsTheEngagement()
        {
            // Energy is a movement-die economy now, not a second
            // destruction condition — see Ship.IsIntegrityDepleted's own
            // comment. A failed escape at 0 Energy still just costs
            // nothing further (floored at 0) and the round continues.
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            player.Ship.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue); // Energy = 0
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape failure

            var session = new EngagementSession(definition, player, opponent);
            var result = session.AttemptEscape(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, player.Ship.GetStat(CoreStat.Energy));
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
        }

        [Test]
        public void AttemptEscape_TwiceInSameRound_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // stays in progress after the failed attempt

            var session = new EngagementSession(definition, player, opponent);
            session.AttemptEscape(new Random(1));

            Assert.Throws<InvalidOperationException>(() => session.AttemptEscape(new Random(1)));
        }

        [Test]
        public void AttemptEscape_WhileAwaitingAttackResolution_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponent);
            session.ResolveInitiative(new Random(1));

            Assert.IsTrue(session.IsAwaitingAttackResolution);
            Assert.IsFalse(session.CanAttemptEscape);
            Assert.Throws<InvalidOperationException>(() => session.AttemptEscape(new Random(1)));
        }

        [Test]
        public void AttemptEscape_AgainAfterNewRoundStarts_IsAllowed()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // escape always fails, opponent always wins initiative
            opponent.ApplyStatDelta(CoreStat.Hull, 20); // survives the round's attack

            var session = new EngagementSession(definition, player, opponent);
            var rng = new Random(1);

            session.AttemptEscape(rng); // round 1 escape attempt (fails)
            session.ResolveInitiative(rng);
            session.ResolveAttack(rng); // round 1 attack resolves, advances to round 2

            Assert.DoesNotThrow(() => session.AttemptEscape(rng));
        }

        // --- CanOpponentDecideDefense / AttemptOpponentEscape (symmetric PvP) ---

        [Test]
        public void CanOpponentDecideDefense_TrueOnlyForPvPWhenOpponentIsTheDefender()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed to win initiative barring a natural-1 fumble
            var opponentShip = new Ship(cargoCapacity: 0);

            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);
            Assert.IsFalse(session.CanOpponentDecideDefense); // before initiative is even rolled

            var rng = new Random(1);
            var rounds = 0;
            while (session.PendingAttacker != RoundAttacker.Player && rounds < 20)
            {
                session.ResolveInitiative(rng);
                if (session.PendingAttacker != RoundAttacker.Player)
                    session.ResolveAttack(rng); // an occasional natural-1 fumble; retry next round
                rounds++;
            }

            Assert.AreEqual(RoundAttacker.Player, session.PendingAttacker);
            Assert.IsTrue(session.CanOpponentDecideDefense);

            // The identical state, but not PvP — an NPC never gets this,
            // even though it's otherwise the same situation.
            var npcPlayer = CreatePlayer();
            BoostAllPerformanceStats(npcPlayer.Ship, 20);
            var npcSession = new EngagementSession(definition, npcPlayer, new Ship(cargoCapacity: 0));
            var npcRounds = 0;
            while (npcSession.PendingAttacker != RoundAttacker.Player && npcRounds < 20)
            {
                npcSession.ResolveInitiative(rng);
                if (npcSession.PendingAttacker != RoundAttacker.Player)
                    npcSession.ResolveAttack(rng);
                npcRounds++;
            }

            Assert.AreEqual(RoundAttacker.Player, npcSession.PendingAttacker);
            Assert.IsFalse(npcSession.CanOpponentDecideDefense);
        }

        [Test]
        public void AttemptOpponentEscape_WhenCanOpponentDecideDefenseIsFalse_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            Assert.IsFalse(session.CanOpponentDecideDefense);
            Assert.Throws<InvalidOperationException>(() => session.AttemptOpponentEscape(new Random(1)));
        }

        private static EngagementSession CreatePvPSessionWithOpponentDefending(out Ship opponentShip)
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20);
            opponentShip = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);

            var rng = new Random(1);
            var rounds = 0;
            while (session.PendingAttacker != RoundAttacker.Player && rounds < 20)
            {
                session.ResolveInitiative(rng);
                if (session.PendingAttacker != RoundAttacker.Player)
                    session.ResolveAttack(rng);
                rounds++;
            }

            return session;
        }

        [Test]
        public void AttemptOpponentEscape_Success_EndsEngagementWithOpponentEscaped()
        {
            var session = CreatePvPSessionWithOpponentDefending(out var opponentShip);
            opponentShip.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success

            var result = session.AttemptOpponentEscape(new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(EngagementOutcome.OpponentEscaped, session.Outcome);
        }

        [Test]
        public void AttemptOpponentEscape_Failure_EnergyLostAndRoundContinues()
        {
            var session = CreatePvPSessionWithOpponentDefending(out var opponentShip);
            // Player's Speed is already +20 (see CreatePvPSessionWithOpponentDefending),
            // so the opponent's escape attempt is guaranteed to fail.

            var result = session.AttemptOpponentEscape(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
            Assert.AreEqual(Ship.MaxEnergyValue - 1, opponentShip.GetStat(CoreStat.Energy));
            // Regression coverage: a failed flee used to silently discard
            // the attacker's already-won initiative (clearing
            // PendingAttacker unconditionally), forcing a fresh initiative
            // roll next instead of letting the attack actually happen —
            // see ApplyEscapeOutcome's own comment.
            Assert.AreEqual(RoundAttacker.Player, session.PendingAttacker);
            Assert.IsTrue(session.IsAwaitingAttackExecution);
            Assert.AreEqual(RoundAttacker.Player, session.ActiveDecisionMaker, "The UI's own source of truth for whose turn it is.");
        }

        [Test]
        public void AttemptOpponentEscape_Failure_AttackerCanStillExecuteTheAlreadyWonInitiative()
        {
            var session = CreatePvPSessionWithOpponentDefending(out _);

            session.AttemptOpponentEscape(new Random(1)); // guaranteed to fail — see the test above

            // The fix: the attack that was already decided by initiative
            // still goes through, same as if the opponent had declared
            // Hold instead of attempting to flee.
            var result = session.ExecuteAttack(new Random(1));

            Assert.AreEqual(RoundAttacker.Player, result.Attacker);
            Assert.IsNull(session.PendingAttacker);
        }

        [Test]
        public void AttemptOpponentEscape_FailureAtZeroEnergy_NoLongerEndsTheEngagement()
        {
            var session = CreatePvPSessionWithOpponentDefending(out var opponentShip);
            opponentShip.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue); // Energy = 0

            var result = session.AttemptOpponentEscape(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, opponentShip.GetStat(CoreStat.Energy));
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
        }

        // Initiative no longer has a PvP-only split path — it's pure
        // forced RNG with no decision on either side, so PvP uses the
        // same single-call ResolveInitiative NPC fights do (see
        // EngagementSession's own class comment). Coverage for that one
        // shared method lives with the rest of ResolveInitiative's tests
        // above; there's nothing split-specific left to test here.

        // --- DeclareDefense / ExecuteAttack (PvP's split attack resolution) ---

        [Test]
        public void ResolveInitiative_SetsIsAwaitingDefenseDeclarationButNotExecution()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            session.ResolveInitiative(new Random(2));

            Assert.IsTrue(session.IsAwaitingDefenseDeclaration);
            Assert.IsFalse(session.IsAwaitingAttackExecution);
        }

        [Test]
        public void DeclareDefense_SetsIsAwaitingAttackExecutionAndClearsDeclaration()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);
            session.ResolveInitiative(new Random(2));

            session.DeclareDefense(wantsBrace: false);

            Assert.IsFalse(session.IsAwaitingDefenseDeclaration);
            Assert.IsTrue(session.IsAwaitingAttackExecution);
            Assert.IsTrue(session.IsAwaitingAttackResolution); // still mid-round — only ExecuteAttack clears this
        }

        [Test]
        public void DeclareDefense_WithoutInitiativeResolvedFirst_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            Assert.Throws<InvalidOperationException>(() => session.DeclareDefense(wantsBrace: false));
        }

        [Test]
        public void DeclareDefense_CalledTwiceInSameRound_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);
            session.ResolveInitiative(new Random(2));
            session.DeclareDefense(wantsBrace: false);

            Assert.Throws<InvalidOperationException>(() => session.DeclareDefense(wantsBrace: true));
        }

        [Test]
        public void ExecuteAttack_WithoutDeclareDefenseFirst_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);
            session.ResolveInitiative(new Random(2));

            Assert.Throws<InvalidOperationException>(() => session.ExecuteAttack(new Random(1)));
        }

        [Test]
        public void ExecuteAttack_ClearsPendingAttackerAndAwaitingFlags()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);
            session.ResolveInitiative(new Random(2));
            session.DeclareDefense(wantsBrace: false);

            session.ExecuteAttack(new Random(1));

            Assert.IsFalse(session.IsAwaitingAttackResolution);
            Assert.IsFalse(session.IsAwaitingAttackExecution);
            Assert.IsFalse(session.IsAwaitingDefenseDeclaration);
        }

        // Mirrors ResolveAttack_OpponentHullReachesZero_PlayerWinsAndRecordsTierWin
        // but drives the PvP-only Declare/Execute split instead of the
        // combined ResolveAttack — proves ApplyAttackOutcome behaves
        // identically no matter which path fed it.
        [Test]
        public void DeclareDefenseThenExecuteAttack_OpponentHullReachesZero_PlayerWinsButNoTierWinIsRecorded()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed hit whenever player has initiative
            var opponentShip = new Ship(cargoCapacity: 0); // default Hull = 3

            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);
            var rng = new Random(1);

            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.DeclareDefense(wantsBrace: false);
                session.ExecuteAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.AreEqual(0, player.HardEngagementWins);
        }

        // The split path (DeclareDefense then ExecuteAttack) must consume
        // the RNG identically to the combined ResolveAttack and produce
        // the same result — same reasoning as the initiative-roll split's
        // own equivalence test above.
        [Test]
        public void DeclareDefenseThenExecuteAttack_ProducesTheSameResultAsResolveAttack_GivenTheSameSeed()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);

            var combinedSession = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0));
            combinedSession.ResolveInitiative(new Random(2));
            var combinedResult = combinedSession.ResolveAttack(new Random(1), wantsBrace: true);

            var splitSession = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0), isPvP: true);
            splitSession.ResolveInitiative(new Random(2));
            splitSession.DeclareDefense(wantsBrace: true);
            var splitResult = splitSession.ExecuteAttack(new Random(1));

            Assert.AreEqual(combinedResult.Attacker, splitResult.Attacker);
            Assert.AreEqual(combinedResult.AttackRoll, splitResult.AttackRoll);
            Assert.AreEqual(combinedResult.AttackTotal, splitResult.AttackTotal);
            Assert.AreEqual(combinedResult.DefenseRoll, splitResult.DefenseRoll);
            Assert.AreEqual(combinedResult.DefenseTotal, splitResult.DefenseTotal);
            Assert.AreEqual(combinedResult.HitLanded, splitResult.HitLanded);
            Assert.AreEqual(combinedResult.WasCriticalHit, splitResult.WasCriticalHit);
            Assert.AreEqual(combinedResult.Damage, splitResult.Damage);
            Assert.AreEqual(combinedResult.DefenderBraced, splitResult.DefenderBraced);
        }

        // The key behavioral point of the whole split: CanOpponentDecideDefense
        // must turn off the instant the opponent declares, even though
        // PendingAttacker itself doesn't change until ExecuteAttack runs —
        // otherwise the opponent's defense UI would still show as available
        // during the attacker's own ExecuteAttack moment.
        [Test]
        public void CanOpponentDecideDefense_TurnsOffAfterDeclareDefense_EvenBeforeExecuteAttackRuns()
        {
            var session = CreatePvPSessionWithOpponentDefending(out _);
            Assert.IsTrue(session.CanOpponentDecideDefense);

            session.DeclareDefense(wantsBrace: false);

            Assert.IsFalse(session.CanOpponentDecideDefense);
            Assert.IsTrue(session.IsAwaitingAttackExecution);
            Assert.AreEqual(RoundAttacker.Player, session.PendingAttacker); // unchanged until ExecuteAttack
        }

        // --- BeginEscapeAttempt / BeginOpponentEscapeAttempt / ResolveEscapeIntercept (PvP's split escape) ---

        [Test]
        public void BeginEscapeAttempt_SetsIsAwaitingEscapeInterceptAndPendingEscapeeToPlayer()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            session.BeginEscapeAttempt(new Random(1));

            Assert.IsTrue(session.IsAwaitingEscapeIntercept);
            Assert.AreEqual(RoundAttacker.Player, session.PendingEscapee);
            Assert.IsFalse(session.CanAttemptEscape); // consumed this round's attempt already
        }

        [Test]
        public void BeginEscapeAttempt_ChargesEnergyImmediately_BeforeResolveEscapeIntercept()
        {
            // User-reported: the Energy cost should be visible the
            // instant the attempt is made, not only once the other
            // side's own intercept tap finally resolves it.
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            session.BeginEscapeAttempt(new Random(1));

            Assert.AreEqual(Ship.MaxEnergyValue - CombatResolver.EscapeAttemptEnergyCost, player.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void BeginEscapeAttempt_WhenCanAttemptEscapeIsFalse_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);
            session.BeginEscapeAttempt(new Random(1)); // consumes this round's attempt

            Assert.Throws<InvalidOperationException>(() => session.BeginEscapeAttempt(new Random(1)));
        }

        [Test]
        public void ResolveEscapeIntercept_WithoutBeginFirst_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            Assert.Throws<InvalidOperationException>(() => session.ResolveEscapeIntercept(new Random(1)));
        }

        [Test]
        public void ResolveEscapeIntercept_ClearsPendingEscapeeAndAwaitingFlag()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);
            session.BeginEscapeAttempt(new Random(1));

            session.ResolveEscapeIntercept(new Random(1));

            Assert.IsFalse(session.IsAwaitingEscapeIntercept);
            Assert.IsNull(session.PendingEscapee);
        }

        // Mirrors AttemptEscape_Success_EndsEngagement but drives the
        // PvP-only Begin/Resolve split instead of the combined AttemptEscape.
        [Test]
        public void BeginEscapeAttemptThenResolveEscapeIntercept_Success_EndsEngagementWithPlayerEscaped()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            player.Ship.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            session.BeginEscapeAttempt(new Random(1));
            var result = session.ResolveEscapeIntercept(new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(EngagementOutcome.PlayerEscaped, session.Outcome);
        }

        [Test]
        public void BeginEscapeAttemptThenResolveEscapeIntercept_Failure_EnergyLostAndRoundContinues()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape failure
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            session.BeginEscapeAttempt(new Random(1));
            var result = session.ResolveEscapeIntercept(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
            Assert.AreEqual(Ship.MaxEnergyValue - 1, player.Ship.GetStat(CoreStat.Energy));
        }

        [Test]
        public void BeginEscapeAttemptThenResolveEscapeIntercept_FailureAtZeroEnergy_NoLongerEndsTheEngagement()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            player.Ship.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue); // Energy = 0
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape failure
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            session.BeginEscapeAttempt(new Random(1));
            var result = session.ResolveEscapeIntercept(new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, player.Ship.GetStat(CoreStat.Energy));
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
        }

        [Test]
        public void BeginOpponentEscapeAttempt_SetsIsAwaitingEscapeInterceptAndPendingEscapeeToOpponent()
        {
            var session = CreatePvPSessionWithOpponentDefending(out _);

            session.BeginOpponentEscapeAttempt(new Random(1));

            Assert.IsTrue(session.IsAwaitingEscapeIntercept);
            Assert.AreEqual(RoundAttacker.Opponent, session.PendingEscapee);
            Assert.IsFalse(session.CanOpponentDecideDefense); // consumed, same as AttemptOpponentEscape
        }

        [Test]
        public void BeginOpponentEscapeAttempt_ChargesEnergyImmediately_BeforeResolveEscapeIntercept()
        {
            var session = CreatePvPSessionWithOpponentDefending(out var opponentShip);

            session.BeginOpponentEscapeAttempt(new Random(1));

            Assert.AreEqual(Ship.MaxEnergyValue - CombatResolver.EscapeAttemptEnergyCost, opponentShip.GetStat(CoreStat.Energy));
        }

        [Test]
        public void BeginOpponentEscapeAttempt_WhenCanOpponentDecideDefenseIsFalse_Throws()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            var opponent = new Ship(cargoCapacity: 0);
            var session = new EngagementSession(definition, player, opponent, isPvP: true);

            Assert.IsFalse(session.CanOpponentDecideDefense);
            Assert.Throws<InvalidOperationException>(() => session.BeginOpponentEscapeAttempt(new Random(1)));
        }

        // Mirrors AttemptOpponentEscape_Success_EndsEngagementWithOpponentEscaped
        // but drives the split path instead.
        [Test]
        public void BeginOpponentEscapeAttemptThenResolveEscapeIntercept_Success_EndsEngagementWithOpponentEscaped()
        {
            var session = CreatePvPSessionWithOpponentDefending(out var opponentShip);
            opponentShip.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed escape success

            session.BeginOpponentEscapeAttempt(new Random(1));
            var result = session.ResolveEscapeIntercept(new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(EngagementOutcome.OpponentEscaped, session.Outcome);
        }

        // Mirrors AttemptOpponentEscape_Failure_EnergyLostAndRoundContinues's
        // own regression coverage, but via the split PvP path — this one
        // used to be worse than the combined method: BeginOpponentEscapeAttempt
        // cleared PendingAttacker immediately, even before the roll.
        [Test]
        public void BeginOpponentEscapeAttemptThenResolveEscapeIntercept_Failure_AttackerCanStillExecuteTheAlreadyWonInitiative()
        {
            var session = CreatePvPSessionWithOpponentDefending(out _);
            // Player's Speed is already +20 (see CreatePvPSessionWithOpponentDefending),
            // so the opponent's escape attempt is guaranteed to fail.

            session.BeginOpponentEscapeAttempt(new Random(1));
            var escapeResult = session.ResolveEscapeIntercept(new Random(1));

            Assert.IsFalse(escapeResult.Success);
            Assert.AreEqual(EngagementOutcome.InProgress, session.Outcome);
            Assert.AreEqual(RoundAttacker.Player, session.PendingAttacker);
            Assert.IsTrue(session.IsAwaitingAttackExecution);

            var attackResult = session.ExecuteAttack(new Random(1));

            Assert.AreEqual(RoundAttacker.Player, attackResult.Attacker);
        }

        // The split path (Begin then ResolveEscapeIntercept) must consume
        // the RNG identically to the combined AttemptEscape and produce
        // the same result — same reasoning as the initiative/attack
        // splits' own equivalence tests above.
        [Test]
        public void BeginEscapeAttemptThenResolveEscapeIntercept_ProducesTheSameResultAsAttemptEscape_GivenTheSameSeed()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);

            var combinedSession = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0));
            var combinedResult = combinedSession.AttemptEscape(new Random(3));

            var splitSession = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0), isPvP: true);
            var splitRng = new Random(3);
            splitSession.BeginEscapeAttempt(splitRng);
            var splitResult = splitSession.ResolveEscapeIntercept(splitRng);

            Assert.AreEqual(combinedResult.Success, splitResult.Success);
            Assert.AreEqual(combinedResult.Roll, splitResult.Roll);
            Assert.AreEqual(combinedResult.Total, splitResult.Total);
            Assert.AreEqual(combinedResult.OpponentRoll, splitResult.OpponentRoll);
            Assert.AreEqual(combinedResult.OpponentTotal, splitResult.OpponentTotal);
            Assert.AreEqual(combinedSession.Outcome, splitSession.Outcome);
        }

        // --- ActiveDecisionMaker (drives EngagementScreen's shared PvP
        // screen — see the "[Multiplayer] PvP pass-and-play" story) ---

        [Test]
        public void ActiveDecisionMaker_NonPvP_IsNull()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var session = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0));

            Assert.IsNull(session.ActiveDecisionMaker);
        }

        [Test]
        public void ActiveDecisionMaker_PvPPreRound_IsPlayer()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var session = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0), isPvP: true);

            Assert.AreEqual(RoundAttacker.Player, session.ActiveDecisionMaker);
        }

        [Test]
        public void ActiveDecisionMaker_PlayerEscapeAttemptPending_IsOpponent()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var session = new EngagementSession(definition, CreatePlayer(), new Ship(cargoCapacity: 0), isPvP: true);
            session.BeginEscapeAttempt(new Random(1));

            Assert.AreEqual(RoundAttacker.Opponent, session.ActiveDecisionMaker);
        }

        [Test]
        public void ActiveDecisionMaker_OpponentEscapeAttemptPending_IsPlayer()
        {
            var session = CreatePvPSessionWithOpponentDefending(out _);
            session.BeginOpponentEscapeAttempt(new Random(1));

            Assert.AreEqual(RoundAttacker.Player, session.ActiveDecisionMaker);
        }

        [Test]
        public void ActiveDecisionMaker_OpponentMustDeclareDefense_IsOpponent()
        {
            var session = CreatePvPSessionWithOpponentDefending(out _);

            Assert.AreEqual(RoundAttacker.Opponent, session.ActiveDecisionMaker);
        }

        [Test]
        public void ActiveDecisionMaker_PlayerMustDeclareDefense_IsPlayer()
        {
            var session = CreatePvPSessionWithPlayerDefending(out _);

            Assert.AreEqual(RoundAttacker.Player, session.ActiveDecisionMaker);
        }

        // Covers the exact ambiguity EngagementScreen.RebuildActionRow has
        // to resolve too (see its own comment on why the attack-execution
        // branch must be checked before PendingAttacker==Opponent): once
        // defense is declared, PendingAttacker alone no longer tells you
        // whether the PLAYER or the OPPONENT is the one who still needs
        // to act — ActiveDecisionMaker must track it correctly for both.
        [Test]
        public void ActiveDecisionMaker_AwaitingAttackExecution_IsWhicheverSideWonInitiative()
        {
            var opponentDefendingSession = CreatePvPSessionWithOpponentDefending(out _);
            opponentDefendingSession.DeclareDefense(wantsBrace: false);
            Assert.AreEqual(RoundAttacker.Player, opponentDefendingSession.ActiveDecisionMaker);

            var playerDefendingSession = CreatePvPSessionWithPlayerDefending(out _);
            playerDefendingSession.DeclareDefense(wantsBrace: false);
            Assert.AreEqual(RoundAttacker.Opponent, playerDefendingSession.ActiveDecisionMaker);
        }

        [Test]
        public void ActiveDecisionMaker_EngagementFinished_IsNull()
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Hard);
            var player = CreatePlayer();
            BoostAllPerformanceStats(player.Ship, 20); // guaranteed hit whenever player has initiative
            var opponentShip = new Ship(cargoCapacity: 0); // default Hull = 3
            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);
            var rng = new Random(1);

            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 20)
            {
                session.ResolveInitiative(rng);
                session.DeclareDefense(wantsBrace: false);
                session.ExecuteAttack(rng);
                rounds++;
            }

            Assert.AreEqual(EngagementOutcome.PlayerWon, session.Outcome);
            Assert.IsNull(session.ActiveDecisionMaker);
        }

        // Symmetric counterpart to CreatePvPSessionWithOpponentDefending
        // above — boosts the OPPONENT's stats instead, so the player ends
        // up as the one who must declare Brace/Hold.
        private static EngagementSession CreatePvPSessionWithPlayerDefending(out Ship opponentShip)
        {
            var definition = EngagementDefinitionTable.For(EngagementTier.Easy);
            var player = CreatePlayer();
            opponentShip = new Ship(cargoCapacity: 0);
            BoostAllPerformanceStats(opponentShip, 20);
            var session = new EngagementSession(definition, player, opponentShip, isPvP: true);

            var rng = new Random(1);
            var rounds = 0;
            while (session.PendingAttacker != RoundAttacker.Opponent && rounds < 20)
            {
                session.ResolveInitiative(rng);
                if (session.PendingAttacker != RoundAttacker.Opponent)
                    session.ResolveAttack(rng);
                rounds++;
            }

            return session;
        }
    }
}
