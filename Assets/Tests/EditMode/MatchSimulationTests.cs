using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    // Runs a batch of full matches driven entirely by RandomMatchBot (see
    // Support/RandomMatchBot.cs) — a legal, mildly-strategic-but-not-smart
    // player — once per Test Runner pass, shared across both tests below
    // via OneTimeSetUp so the "how many matches" budget (~20-30, agreed
    // with the user to keep routine Test Runner passes fast) isn't doubled
    // by having two [Test] methods each re-run the full batch.
    //
    // IMPORTANT, verified by actually running this logic outside Unity
    // (the production gameplay code has no UnityEngine dependency, so it
    // compiles and runs standalone) before landing it here: with engagement
    // tiers all available from turn one and no way to avoid a Hard-tier
    // NPC (roughly double a base player's stats) other than fleeing —
    // itself a contested roll against the same opponent — a bot with no
    // deep strategy essentially never reaches match completion. That's not
    // a bug in this suite; it directly motivated the new
    // "[Multiplayer] Game progression: phase in Medium/Hard engagements"
    // backlog story. So completion rate here is a reported balance metric,
    // not a pass/fail gate — see SimulatedMatches_ReportsBalanceMetrics.
    public class MatchSimulationTests
    {
        // Kept modest deliberately: empirically (see above), a much larger
        // cap doesn't meaningfully raise the completion rate under today's
        // rules, so there's no value in spending extra Test Runner time on it.
        private const int TurnCap = 200;

        private List<MatchSimulationResult> results;

        private static IEnumerable<(int Seed, MapSize Size, Difficulty Difficulty)> BuildSimulationRuns()
        {
            var sizes = new[] { MapSize.Small, MapSize.Medium, MapSize.Large };
            var difficulties = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard };

            // 3 sizes * 3 difficulties * 3 repeats = 27 runs — the agreed
            // ~20-30 matches per Test Runner pass.
            for (var repeat = 0; repeat < 3; repeat++)
            {
                foreach (var size in sizes)
                {
                    foreach (var difficulty in difficulties)
                    {
                        var seed = (int)size * 1000 + (int)difficulty * 100 + repeat;
                        yield return (seed, size, difficulty);
                    }
                }
            }
        }

        [OneTimeSetUp]
        public void RunSimulations()
        {
            results = new List<MatchSimulationResult>();
            var failures = new List<string>();

            // Collected rather than failed immediately, so one broken seed
            // doesn't stop the rest of the batch from running — any other
            // broken seeds surface in the same pass instead of one at a time.
            foreach (var run in BuildSimulationRuns())
            {
                try
                {
                    results.Add(RandomMatchBot.PlayFullMatch(run.Seed, run.Size, run.Difficulty, TurnCap));
                }
                catch (Exception exception)
                {
                    failures.Add($"seed={run.Seed}, size={run.Size}, difficulty={run.Difficulty}: {exception}");
                }
            }

            // This is the real correctness gate: a thrown exception means a
            // genuinely stuck/invalid state, not just a match that ran out
            // of turns (see the class doc comment on why completion itself
            // isn't asserted here).
            if (failures.Count > 0)
                Assert.Fail("Simulated match(es) threw:\n\n" + string.Join("\n\n", failures));
        }

        [Test]
        public void SimulatedMatches_ProduceSaneTelemetry()
        {
            foreach (var result in results)
            {
                Assert.Greater(result.TotalTurns, 0, $"Seed {result.Seed} recorded zero turns.");
                Assert.LessOrEqual(result.TotalTurns, TurnCap, $"Seed {result.Seed} exceeded the turn cap.");
                Assert.GreaterOrEqual(result.EngagementsWon, 0);
                Assert.GreaterOrEqual(result.EngagementsLost, 0);
                Assert.GreaterOrEqual(result.EngagementsEscaped, 0);
                Assert.GreaterOrEqual(result.IntegrityWipes, 0);
                Assert.GreaterOrEqual(result.IdleTurns, 0);
                Assert.LessOrEqual(result.IdleTurns, result.TotalTurns,
                    $"Seed {result.Seed} recorded more idle turns than turns played.");
            }
        }

        [Test]
        public void SimulatedMatches_ReportsBalanceMetrics()
        {
            // Computed over ALL runs, not just completed ones — see the
            // class doc comment; under today's rules, completion is itself
            // one of the metrics being reported, not a filter.
            var totalEngagements = results.Sum(r => r.TotalEngagements);
            var winRate = totalEngagements == 0 ? 0.0 : (double)results.Sum(r => r.EngagementsWon) / totalEngagements;
            var lossRate = totalEngagements == 0 ? 0.0 : (double)results.Sum(r => r.EngagementsLost) / totalEngagements;
            var escapeRate = totalEngagements == 0 ? 0.0 : (double)results.Sum(r => r.EngagementsEscaped) / totalEngagements;
            var completed = results.Where(r => r.Completed).ToList();

            TestContext.WriteLine("=== Random-play balance report ===");
            TestContext.WriteLine("A mildly-strategic-but-not-smart bot's numbers are a \"does the rules");
            TestContext.WriteLine("engine behave sanely across varied play\" signal, NOT a tuned-balance");
            TestContext.WriteLine("signal — see the [Multiplayer] game-progression backlog story for the");
            TestContext.WriteLine("balance finding this simulator already surfaced (Hard-tier NPCs roughly");
            TestContext.WriteLine("double a base player's stats, with no reliable way to avoid a fight once");
            TestContext.WriteLine("triggered — so low/zero completion here is expected for now, not a bug).");
            TestContext.WriteLine($"Matches completed: {completed.Count}/{results.Count} ({TurnCap}-turn cap)");
            TestContext.WriteLine($"Turns played — min: {results.Min(r => r.TotalTurns)}, max: {results.Max(r => r.TotalTurns)}, avg: {results.Average(r => r.TotalTurns):F1}");
            TestContext.WriteLine($"Engagements per match — avg: {results.Average(r => r.TotalEngagements):F1}, total: {totalEngagements}");
            TestContext.WriteLine($"Engagement outcomes — won: {winRate:P0}, lost: {lossRate:P0}, escaped: {escapeRate:P0}");
            TestContext.WriteLine($"PvE vs PvP wins — PvE: {results.Sum(r => r.PvEWins)}, PvP: {results.Sum(r => r.PvPWins)} (PvP never counts toward victory)");
            TestContext.WriteLine($"Zero-integrity wipes per match — avg: {results.Average(r => r.IntegrityWipes):F2}, total: {results.Sum(r => r.IntegrityWipes)}");
            TestContext.WriteLine($"Idle turns per match (full budget unused) — avg: {results.Average(r => r.IdleTurns):F1}");
            if (completed.Count > 0)
                TestContext.WriteLine($"Winner's final money (completed matches only) — min: {completed.Min(r => r.FinalWinnerMoney)}, max: {completed.Max(r => r.FinalWinnerMoney)}, avg: {completed.Average(r => r.FinalWinnerMoney):F0}");

            // Intentionally loose — smoke-test guardrails against
            // unambiguous breakage, not tuned balance targets (the report
            // above, read by a human, is what should actually judge
            // balance while the economy is still being tuned). Completion
            // rate is deliberately NOT asserted here — see class doc comment.
            if (totalEngagements > 0)
            {
                Assert.Greater(winRate, 0.0, "Zero engagement wins across every simulated match — combat may never resolve in the player's favor.");
                Assert.Less(winRate, 1.0, "100% engagement win rate across every simulated match — combat may never resolve against the player.");
            }
        }
    }
}
