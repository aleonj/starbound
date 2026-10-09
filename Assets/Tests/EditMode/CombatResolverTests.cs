using System;
using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class CombatResolverTests
    {
        // --- ResolveInitiative (pure — doesn't mutate ships, safe to retry) ---

        [Test]
        public void ResolveInitiative_PlayerHigherSpeed_PlayerAttacksUnlessNaturalOne()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed win unless a natural 1 — margin exceeds any roll swing
            var opponent = new Ship(cargoCapacity: 0);

            for (var seed = 0; seed < 50; seed++)
            {
                var result = CombatResolver.ResolveInitiative(player, opponent, new Random(seed));
                if (result.PlayerSpeedRoll == 1)
                    continue; // the natural-1 fumble case has its own dedicated test

                Assert.AreEqual(RoundAttacker.Player, result.Attacker);
                Assert.IsFalse(result.WasPlayerCriticalFailure);
                return;
            }

            Assert.Fail("Could not find a non-natural-1 roll in 50 attempts.");
        }

        [Test]
        public void ResolveInitiative_OpponentHigherSpeed_OpponentAttacks()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // player's total can never reach this

            var result = CombatResolver.ResolveInitiative(player, opponent, new Random(1));

            Assert.AreEqual(RoundAttacker.Opponent, result.Attacker);
        }

        [Test]
        public void ResolveInitiative_PlayerNaturalOne_ForcesOpponentAttackerRegardlessOfTotal()
        {
            var found = false;
            for (var seed = 0; seed < 200 && !found; seed++)
            {
                var player = new Ship(cargoCapacity: 0);
                player.ApplyStatDelta(CoreStat.Speed, 20); // would otherwise guarantee a player win

                var result = CombatResolver.ResolveInitiative(player, new Ship(cargoCapacity: 0), new Random(seed));

                if (result.PlayerSpeedRoll == 1)
                {
                    found = true;
                    Assert.IsTrue(result.WasPlayerCriticalFailure);
                    Assert.AreEqual(RoundAttacker.Opponent, result.Attacker);
                }
            }

            Assert.IsTrue(found, "Never observed a natural 1 in 200 attempts.");
        }

        [Test]
        public void ResolveInitiative_BothRollsAreWithinD10Range()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            var rng = new Random(1);

            for (var i = 0; i < 100; i++)
            {
                var result = CombatResolver.ResolveInitiative(player, opponent, rng);
                Assert.That(result.PlayerSpeedRoll, Is.InRange(1, 10));
                Assert.That(result.OpponentSpeedRoll, Is.InRange(1, 10));
            }
        }

        // --- ResolveAttack: player attacking (mutates ships — use fresh ones per attempt) ---

        [Test]
        public void ResolveAttack_PlayerAttackerHits_OpponentLosesOneHullUnlessCrit()
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var player = new Ship(cargoCapacity: 0);
                player.ApplyStatDelta(CoreStat.Weapons, 20); // guaranteed hit
                var opponent = new Ship(cargoCapacity: 0);

                var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Player, new Random(seed));
                if (result.AttackRoll == 10)
                    continue; // the crit case has its own dedicated test

                Assert.IsTrue(result.HitLanded);
                Assert.AreEqual(1, result.Damage);
                Assert.AreEqual(2, opponent.GetStat(CoreStat.Hull));
                return;
            }

            Assert.Fail("Could not find a non-natural-10 roll in 50 attempts.");
        }

        [Test]
        public void ResolveAttack_PlayerAttackerMisses_NoHullLostUnlessCrit()
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var player = new Ship(cargoCapacity: 0);
                var opponent = new Ship(cargoCapacity: 0);
                opponent.ApplyStatDelta(CoreStat.Shields, 20); // player's total can never reach this normally

                var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Player, new Random(seed));
                if (result.AttackRoll == 10)
                    continue; // a natural 10 would bypass Shields — not what this test covers

                Assert.IsFalse(result.HitLanded);
                Assert.AreEqual(3, opponent.GetStat(CoreStat.Hull));
                return;
            }

            Assert.Fail("Could not find a non-natural-10 roll in 50 attempts.");
        }

        [Test]
        public void ResolveAttack_PlayerNaturalTen_BypassesShieldsAndDealsTwoDamage()
        {
            var found = false;
            for (var seed = 0; seed < 200 && !found; seed++)
            {
                var player = new Ship(cargoCapacity: 0);
                var opponent = new Ship(cargoCapacity: 0);
                opponent.ApplyStatDelta(CoreStat.Shields, 100); // would always miss without the crit bypass

                var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Player, new Random(seed));

                if (result.AttackRoll == 10)
                {
                    found = true;
                    Assert.IsTrue(result.WasCriticalHit);
                    Assert.IsTrue(result.HitLanded);
                    Assert.AreEqual(2, result.Damage);
                    Assert.AreEqual(1, opponent.GetStat(CoreStat.Hull)); // 3 - 2
                }
            }

            Assert.IsTrue(found, "Never observed a natural 10 in 200 attempts.");
        }

        [Test]
        public void ResolveAttack_DefenderRollsShieldsRatherThanUsingAFlatValue()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            var rng = new Random(1);
            var sawVaryingDefenseRoll = false;
            var previousDefenseRoll = -1;

            for (var i = 0; i < 30; i++)
            {
                var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Player, rng);
                Assert.That(result.DefenseRoll, Is.InRange(1, 10));
                Assert.AreEqual(result.DefenseRoll + opponent.GetStat(CoreStat.Shields), result.DefenseTotal);

                if (previousDefenseRoll != -1 && result.DefenseRoll != previousDefenseRoll)
                    sawVaryingDefenseRoll = true;
                previousDefenseRoll = result.DefenseRoll;
            }

            Assert.IsTrue(sawVaryingDefenseRoll, "Defense roll never varied across 30 attacks — looks like a flat value again.");
        }

        // --- ResolveAttack: opponent attacking (now also rolls) ---

        [Test]
        public void ResolveAttack_OpponentAttackerHits_PlayerLosesHullAndRollsForIt()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Weapons, 20); // guaranteed hit vs player's default Shields

            var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Opponent, new Random(1));

            Assert.That(result.AttackRoll, Is.InRange(1, 10));
            Assert.IsTrue(result.HitLanded);
            Assert.AreEqual(2, player.GetStat(CoreStat.Hull));
        }

        [Test]
        public void ResolveAttack_OpponentAttackerMisses_NoHullLostUnlessCrit()
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var player = new Ship(cargoCapacity: 0);
                player.ApplyStatDelta(CoreStat.Shields, 20); // opponent's total can never reach this normally
                var opponent = new Ship(cargoCapacity: 0);

                var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Opponent, new Random(seed));
                if (result.AttackRoll == 10)
                    continue; // a natural 10 would bypass Shields — not what this test covers

                Assert.IsFalse(result.HitLanded);
                Assert.AreEqual(3, player.GetStat(CoreStat.Hull));
                return;
            }

            Assert.Fail("Could not find a non-natural-10 roll in 50 attempts.");
        }

        [Test]
        public void ResolveAttack_OpponentNaturalTen_BypassesShieldsAndDealsTwoDamage()
        {
            var found = false;
            for (var seed = 0; seed < 200 && !found; seed++)
            {
                var player = new Ship(cargoCapacity: 0);
                player.ApplyStatDelta(CoreStat.Shields, 100); // would always miss without the crit bypass
                var opponent = new Ship(cargoCapacity: 0);

                var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Opponent, new Random(seed));

                if (result.AttackRoll == 10)
                {
                    found = true;
                    Assert.IsTrue(result.WasCriticalHit);
                    Assert.IsTrue(result.HitLanded);
                    Assert.AreEqual(2, result.Damage);
                    Assert.AreEqual(1, player.GetStat(CoreStat.Hull)); // 3 - 2
                }
            }

            Assert.IsTrue(found, "Never observed a natural 10 in 200 attempts.");
        }

        [Test]
        public void ResolveAttack_OpponentAttacksAndPlayerBraces_BoostsShieldsAndCostsEnergy()
        {
            var player = new Ship(cargoCapacity: 0); // Shields default 3, Energy default Ship.MaxEnergyValue
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Opponent, new Random(1), wantsBrace: true);

            Assert.IsTrue(result.DefenderBraced);
            Assert.AreEqual(result.DefenseRoll + 3 + CombatResolver.BraceShieldBonus, result.DefenseTotal);
            Assert.AreEqual(Ship.MaxEnergyValue - CombatResolver.BraceEnergyCost, player.GetStat(CoreStat.Energy));
        }

        // The mirror direction — previously impossible: the player-attacking
        // branch used to have no brace parameter at all, so the opponent
        // could never brace while defending. Now symmetric (see
        // EngagementSession.CanOpponentDecideDefense in the PvP flow).
        [Test]
        public void ResolveAttack_PlayerAttacksAndOpponentBraces_BoostsShieldsAndCostsEnergy()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0); // Shields default 3, Energy default Ship.MaxEnergyValue

            var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Player, new Random(1), wantsBrace: true);

            Assert.IsTrue(result.DefenderBraced);
            Assert.AreEqual(result.DefenseRoll + 3 + CombatResolver.BraceShieldBonus, result.DefenseTotal);
            Assert.AreEqual(Ship.MaxEnergyValue - CombatResolver.BraceEnergyCost, opponent.GetStat(CoreStat.Energy));
        }

        [Test]
        public void ResolveAttack_Brace_CanTurnAHitIntoAMiss()
        {
            // Same seed run twice (fresh ships each time) so both attempts
            // draw the identical roll — isolates brace's effect from luck.
            for (var seed = 0; seed < 50; seed++)
            {
                var unbracedResult = CombatResolver.ResolveAttack(
                    new Ship(cargoCapacity: 0), new Ship(cargoCapacity: 0), RoundAttacker.Opponent, new Random(seed), wantsBrace: false);

                if (unbracedResult.WasCriticalHit || !unbracedResult.HitLanded)
                    continue; // need a roll that hits unbraced to prove brace changes the outcome

                var bracedPlayer = new Ship(cargoCapacity: 0);
                var bracedResult = CombatResolver.ResolveAttack(
                    bracedPlayer, new Ship(cargoCapacity: 0), RoundAttacker.Opponent, new Random(seed), wantsBrace: true);

                if (!bracedResult.HitLanded)
                {
                    Assert.AreEqual(3, bracedPlayer.GetStat(CoreStat.Hull));
                    return;
                }
            }

            Assert.Fail("Could not find a seed where bracing flips a hit into a miss within 50 attempts.");
        }

        [Test]
        public void ResolveAttack_OpponentAttacksWithoutBrace_NoEnergyCostNoBonus()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Opponent, new Random(1), wantsBrace: false);

            Assert.IsFalse(result.DefenderBraced);
            Assert.AreEqual(result.DefenseRoll + 3, result.DefenseTotal);
            Assert.AreEqual(Ship.MaxEnergyValue, player.GetStat(CoreStat.Energy));
        }

        [Test]
        public void ResolveAttack_WantsBraceButNoEnergy_BraceHasNoEffect()
        {
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Energy, -Ship.MaxEnergyValue); // Energy = 0
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveAttack(player, opponent, RoundAttacker.Opponent, new Random(1), wantsBrace: true);

            Assert.IsFalse(result.DefenderBraced);
            Assert.AreEqual(result.DefenseRoll + 3, result.DefenseTotal);
            Assert.AreEqual(0, player.GetStat(CoreStat.Energy));
        }

        // --- ResolveEscapeAttempt (a contested roll both ways; the escapee/other
        // params aren't hardcoded to player/opponent, so EngagementSession can
        // run this for either side — these tests exercise it the same way the
        // player's own AttemptEscape does) ---

        [Test]
        public void ResolveEscapeAttempt_Success_StillCostsOneEnergy()
        {
            // The attempt itself costs Energy now, win or lose — see
            // CombatResolver.DetermineEscapeOutcome's own comment.
            var player = new Ship(cargoCapacity: 0);
            player.ApplyStatDelta(CoreStat.Speed, 20); // guaranteed win — exceeds any opponent roll+stat swing
            var opponent = new Ship(cargoCapacity: 0);

            var result = CombatResolver.ResolveEscapeAttempt(player, opponent, new Random(1));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(Ship.MaxEnergyValue - CombatResolver.EscapeAttemptEnergyCost, player.GetStat(CoreStat.Energy));
        }

        [Test]
        public void ResolveEscapeAttempt_Failure_LosesOneEnergy()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            opponent.ApplyStatDelta(CoreStat.Speed, 20); // player's total can never reach this

            var result = CombatResolver.ResolveEscapeAttempt(player, opponent, new Random(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(Ship.MaxEnergyValue - CombatResolver.EscapeAttemptEnergyCost, player.GetStat(CoreStat.Energy));
        }

        [Test]
        public void ResolveEscapeAttempt_OpponentRollsRatherThanUsingAFlatSpeedValue()
        {
            var player = new Ship(cargoCapacity: 0);
            var opponent = new Ship(cargoCapacity: 0);
            var rng = new Random(1);
            var sawVaryingOpponentRoll = false;
            var previousOpponentRoll = -1;

            for (var i = 0; i < 30; i++)
            {
                var result = CombatResolver.ResolveEscapeAttempt(player, opponent, rng);
                Assert.That(result.OpponentRoll, Is.InRange(1, 10));
                Assert.AreEqual(result.OpponentRoll + opponent.GetStat(CoreStat.Speed), result.OpponentTotal);

                if (previousOpponentRoll != -1 && result.OpponentRoll != previousOpponentRoll)
                    sawVaryingOpponentRoll = true;
                previousOpponentRoll = result.OpponentRoll;
            }

            Assert.IsTrue(sawVaryingOpponentRoll, "Opponent's escape roll never varied across 30 attempts — looks like a flat value again.");
        }
    }
}
