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
    // History: this simulator originally found engagement tiers all
    // available from turn one, with no reliable way to avoid a Hard-tier
    // NPC (roughly double a base player's stats), meant a bot with no
    // deep strategy essentially never reached match completion. That
    // directly motivated the "[Multiplayer] Game progression" story —
    // Medium/Hard now phase in via Match.MaxUnlockedTier, unlocked by
    // completing a race-to-complete goal, and RandomMatchBot was updated
    // to actually chase the active goal (see GoalPursuitWeight) instead
    // of ignoring it.
    //
    // A DefeatNamedTarget goal originally placed its marker at the tier
    // being unlocked — "prove you're ready for Medium" by beating a
    // Medium-strength NPC at baseline Easy stats — which was self-defeating
    // and got fixed (see MatchProgressionService: it now targets the
    // player's CURRENT tier, a winnable fight). That measurably helps
    // (verified outside Unity: simulated matches now sometimes reach
    // Medium, versus never before), but full completion still doesn't
    // happen within the turn cap — reaching Hard needs a second full
    // phase transition plus 3 real Hard wins, which this bot's win rate
    // doesn't sustain in 200 turns. That's broader combat/pacing balance,
    // not a specific bug, and is being left as a reported finding rather
    // than chased further this pass — see the [Multiplayer]
    // game-progression story for the full writeup and status. Completion
    // rate stays a reported metric, not a pass/fail gate — see
    // SimulatedMatches_ReportsBalanceMetrics.
    public class MatchSimulationTests
    {
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
            TestContext.WriteLine("signal. The bot chases the active progression goal (see");
            TestContext.WriteLine("RandomMatchBot.GoalPursuitWeight) and Medium now unlocks in some runs,");
            TestContext.WriteLine("but full completion still needs a second phase transition to Hard plus");
            TestContext.WriteLine("3 real Hard wins, which this bot's win rate doesn't sustain within the");
            TestContext.WriteLine("turn cap — see the [Multiplayer] game-progression story for this");
            TestContext.WriteLine("finding. Low/zero completion here is expected for now, not a bug.");
            TestContext.WriteLine($"Matches completed: {completed.Count}/{results.Count} ({TurnCap}-turn cap)");
            var tierBreakdown = results.GroupBy(r => r.FinalMaxUnlockedTier).OrderBy(g => g.Key);
            TestContext.WriteLine("Final unlocked tier reached — " + string.Join(", ", tierBreakdown.Select(g => $"{g.Key}: {g.Count()}")));
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
