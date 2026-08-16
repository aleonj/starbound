using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;
using StarBound.Movement;
using StarBound.Multiplayer;
using StarBound.Shop;

namespace StarBound.Tests
{
    // Aggregated outcome + balance telemetry from one simulated match —
    // see MatchSimulationTests for how these get used. A uniformly-random
    // bot's numbers here are a "does the rules engine behave sanely across
    // varied play" signal, NOT a tuned-balance signal: the bot fights,
    // shops, and moves with zero strategy, so its win rates and pacing
    // will look different from a real player who picks fights they can
    // win and spends deliberately. Read the report with that in mind.
    public class MatchSimulationResult
    {
        public int Seed;
        public bool Completed;
        public int TotalTurns;
        public int EngagementsWon;
        public int EngagementsLost;
        public int EngagementsEscaped;
        public int IntegrityWipes;
        public int IdleTurns; // turns that ended with the full action budget unused
        public int FinalWinnerMoney;
        public int PvEWins;
        public int PvPWins; // never counts toward victory — see Match.AttackOpponent

        public int TotalEngagements => EngagementsWon + EngagementsLost + EngagementsEscaped;
    }

    // Plays one full match start-to-finish using only production Match
    // API calls, choosing (mostly) at random among whatever's currently
    // legal — weighted toward survival (see BuildLegalActions' repair/buy
    // weighting) and, since game-progression landed, toward finishing
    // whatever the active goal is (see GoalPursuitWeight), since without
    // that bias Medium/Hard never unlock and no match can complete within
    // the turn cap. Still explores far more state combinations than
    // anyone would hand-write — the same idea as a fuzzer, applied to
    // this turn-based rules engine specifically.
    public static class RandomMatchBot
    {
        // Safety net against a pathological "always something free to do"
        // loop within a single turn — not expected to ever actually bind,
        // since the action budget (2 per turn) already caps real progress.
        private const int MaxActionAttemptsPerTurn = 10;

        // Chance to voluntarily stop taking actions early even when more
        // are legal, so idle/under-used turns get exercised too, not just
        // the "genuinely nothing left to do" case.
        private const double StopEarlyChance = 0.1;

        // Landing on the other player is easy to stumble into just by both
        // sides wandering, and a PvP win never counts toward victory — an
        // unweighted bot spent >95% of its combat time on PvP in testing,
        // crowding out real progress. Down-weighted so it still happens,
        // just not constantly.
        private const double AttackOpponentChance = 0.1;

        // A move that shortens the distance to the active progression
        // goal's target hex gets this many extra copies in the action
        // pool (see BuildLegalActions) — without it, the bot has no
        // reason to ever actually finish a goal, so Medium/Hard never
        // unlock and the match can't be won within the turn cap. Not
        // pathfinding — it only prefers a closer neighbor when one of the
        // dice already rolled happens to offer one.
        private const int GoalPursuitWeight = 5;

        public static MatchSimulationResult PlayFullMatch(int seed, MapSize mapSize, Difficulty difficulty, int turnCap)
        {
            var rng = new Random(seed);
            var p1 = new Player("p1", "One", new Ship(cargoCapacity: 3, EconomyConstants.StartingMoney));
            var p2 = new Player("p2", "Two", new Ship(cargoCapacity: 3, EconomyConstants.StartingMoney));
            var match = MatchFactory.CreateMatch(mapSize, difficulty, seed, p1, p2);

            var result = new MatchSimulationResult { Seed = seed };

            while (!match.IsComplete && result.TotalTurns < turnCap)
            {
                result.TotalTurns++;
                PlayOneTurn(match, rng, result);
            }

            result.Completed = match.IsComplete;
            if (match.IsComplete)
                result.FinalWinnerMoney = match.Winner.Ship.Money;

            return result;
        }

        private static void PlayOneTurn(Match match, Random rng, MatchSimulationResult result)
        {
            match.RollDice(rng);

            var attempts = 0;
            while (!match.IsInEngagement && match.ActionsRemaining > 0 && attempts < MaxActionAttemptsPerTurn)
            {
                if (rng.NextDouble() < StopEarlyChance)
                    break;

                attempts++;
                var actions = BuildLegalActions(match, rng);
                if (actions.Count == 0)
                    break;

                actions[rng.Next(actions.Count)]();
            }

            if (match.IsInEngagement)
            {
                ResolveEngagementRandomly(match, rng);
                RecordEngagementOutcome(match, result);

                var outcome = match.ActiveEngagement.Outcome;
                var wasPvP = match.ActiveEngagement.IsPvP;
                var possiblyWiped = outcome == EngagementOutcome.PlayerLost ? match.CurrentPlayer
                    : wasPvP ? match.OtherPlayer : null;
                var wasDepleted = possiblyWiped != null && possiblyWiped.Ship.IsIntegrityDepleted;
                if (outcome == EngagementOutcome.PlayerWon)
                {
                    if (wasPvP) result.PvPWins++;
                    else result.PvEWins++;
                }

                match.ResolveActiveEngagement(rng);

                if (wasDepleted)
                    result.IntegrityWipes++;
            }
            else if (match.ActionsRemaining == Match.ActionsPerTurn)
            {
                result.IdleTurns++;
            }

            if (match.CanEndTurn)
                match.EndTurn();
        }

        // One concrete choice per currently-legal option (e.g. one entry
        // per die+target pair, one per shop item) rather than one entry
        // per action *kind* — so kinds with more options (movement,
        // shopping) get picked proportionally more often. Fine for a
        // coverage tool; not meant to be a uniform-per-kind distribution.
        private static List<Action> BuildLegalActions(Match match, Random rng)
        {
            var actions = new List<Action>();
            var player = match.CurrentPlayer;
            var ship = player.Ship;

            var goal = match.ActiveGoal;

            if (match.CanMove)
            {
                foreach (var die in match.CurrentHand.UnspentDice)
                foreach (var target in ComputeLegalTargets(match, die))
                {
                    actions.Add(() => match.Move(die, target, rng));

                    // See GoalPursuitWeight — prefer whichever legal move
                    // actually closes distance to the goal's target hex,
                    // for either goal type (both carry a TargetHex).
                    if (goal != null &&
                        HexMath.Distance(target, goal.TargetHex) < HexMath.Distance(player.Position, goal.TargetHex))
                    {
                        for (var i = 0; i < GoalPursuitWeight; i++)
                            actions.Add(() => match.Move(die, target, rng));
                    }
                }
            }

            if (match.CanShop)
            {
                // Weighted toward survival: repairing when damaged and
                // buying stat upgrades when flush get added several times
                // (raising their odds of being picked) rather than sitting
                // as a single option among a large random pool — "mildly
                // strategic", not deterministic.
                var repairWeight = ship.GetStat(CoreStat.Hull) < Ship.DefaultStatValue ||
                    ship.GetStat(CoreStat.Energy) < Ship.DefaultStatValue ? 4 : 1;
                for (var i = 0; i < repairWeight; i++)
                {
                    actions.Add(() => match.RepairStat(CoreStat.Hull));
                    actions.Add(() => match.RepairStat(CoreStat.Energy));
                }

                // Reserve whatever a TravelAndPay goal still needs so
                // shopping doesn't spend the bot back below the amount
                // required to ever complete it.
                var moneyToPreserve = goal is { Type: MatchGoalType.TravelAndPay } ? goal.MoneyRequired : 0;

                var buyWeight = ship.Money >= 100 && ship.CanHoldAnotherItem ? 4 : 1;
                foreach (var item in ShopOfferGenerator.GenerateOffer(rng))
                {
                    if (ship.Money - item.Price < moneyToPreserve)
                        continue;

                    for (var i = 0; i < buyWeight; i++)
                        actions.Add(() => match.BuyItem(item));
                }

                foreach (var held in ship.HeldItems.ToList())
                    actions.Add(() => match.SellItem(held));
            }

            if (match.CanAcceptJob && player.ActiveJob == null)
            {
                foreach (var job in JobOfferGenerator.GenerateOffer(rng, player.Position, match.Map))
                    actions.Add(() => match.AcceptJob(job));
            }

            if (match.CanDeliverJob)
                actions.Add(() => match.DeliverJob());

            if (match.CanMineAsteroid)
                actions.Add(() => match.MineAsteroid());

            if (match.CanAttackOpponent && rng.NextDouble() < AttackOpponentChance)
                actions.Add(() => match.AttackOpponent());

            foreach (var held in ship.HeldItems.ToList())
            {
                if (match.CanTradeWithOpponent(held))
                    actions.Add(() => match.TradeItemToOpponent(held));
                if (match.CanUseItem(held))
                    actions.Add(() => match.UseItem(held));
            }

            if (match.CanTravelWormhole)
            {
                foreach (var destination in match.OtherWormholeDestinations.ToList())
                    actions.Add(() => match.TravelToWormhole(destination, rng));
            }

            return actions;
        }

        // Mirrors MatchHud.ComputeLegalTargets (a MonoBehaviour method we
        // can't call directly from EditMode tests) against the real
        // adjacency/terrain-matching rules.
        private static List<HexCoordinate> ComputeLegalTargets(Match match, RolledDie die)
        {
            var results = new List<HexCoordinate>();
            foreach (var neighbor in match.Map.GetNeighborCoordinates(match.CurrentPlayer.Position))
            {
                if (match.Map.TryGetHex(neighbor, out var hex) &&
                    (hex.Terrain == die.Terrain || hex.Terrain == TerrainType.PlanetOrStarport))
                {
                    results.Add(neighbor);
                }
            }

            return results;
        }

        // Mirrors the escape/initiative/attack state machine MatchHud's
        // DrawEngagementPanel drives, with randomized escape/brace choices.
        // Capped well above the ~20-round bound this session's other tests
        // use for a *guaranteed* (stat-boosted) outcome — an even fight
        // between un-boosted ships can genuinely run longer than that, and
        // this bot fights plenty of those.
        private static void ResolveEngagementRandomly(Match match, Random rng)
        {
            var session = match.ActiveEngagement;
            var rounds = 0;
            while (session.Outcome == EngagementOutcome.InProgress && rounds < 200)
            {
                if (session.CanAttemptEscape && rng.NextDouble() < EscapeChance(session))
                    session.AttemptEscape(rng);
                else if (!session.IsAwaitingAttackResolution)
                    session.ResolveInitiative(rng);
                else
                    session.ResolveAttack(rng, wantsBrace: rng.NextDouble() < 0.5);

                rounds++;
            }
        }

        // "Mildly strategic": more likely to run from a fight that looks
        // bad on paper, rather than a flat chance every round. Still often
        // loses anyway — fleeing is itself a contested Speed roll against
        // the same stat-advantaged opponent, so it's not a reliable out
        // against a high-tier NPC (see the [Multiplayer] game-progression
        // story this finding fed into).
        private static double EscapeChance(EngagementSession session)
        {
            var playerPower = session.PlayerShip.GetStat(CoreStat.Weapons) + session.PlayerShip.GetStat(CoreStat.Shields) + session.PlayerShip.GetStat(CoreStat.Speed);
            var opponentPower = session.Opponent.GetStat(CoreStat.Weapons) + session.Opponent.GetStat(CoreStat.Shields) + session.Opponent.GetStat(CoreStat.Speed);
            if (playerPower <= 0)
                return 0.9;

            var ratio = (double)opponentPower / playerPower;
            if (ratio > 1.5) return 0.85;
            if (ratio > 1.15) return 0.5;
            return 0.1;
        }

        private static void RecordEngagementOutcome(Match match, MatchSimulationResult result)
        {
            switch (match.ActiveEngagement.Outcome)
            {
                case EngagementOutcome.PlayerWon:
                    result.EngagementsWon++;
                    break;
                case EngagementOutcome.PlayerLost:
                    result.EngagementsLost++;
                    break;
                case EngagementOutcome.PlayerEscaped:
                    result.EngagementsEscaped++;
                    break;
            }
        }
    }
}
