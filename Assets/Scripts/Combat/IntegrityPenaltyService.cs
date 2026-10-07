using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Combat
{
    // Applies whenever a player's Hull or Energy reaches 0, in or out of
    // combat — not wired into EngagementSession automatically, since the
    // rule applies more broadly than just combat. Callers should invoke
    // this after anything that can deplete Hull/Energy.
    public static class IntegrityPenaltyService
    {
        // moneyLost lets a PvP caller (see Match.ResolveActiveEngagement)
        // hand the destroyed player's money to whoever beat them instead
        // of it just vanishing — the hazard-damage call site (asteroids/
        // mines, no "winner" to credit) simply ignores it via `out _`.
        public static bool ApplyIfDepleted(Player player, GameMap map, Random rng, out int moneyLost)
        {
            moneyLost = 0;
            if (!player.Ship.IsIntegrityDepleted)
                return false;

            moneyLost = player.Ship.Money;
            player.Ship.ClearMoneyAndNonPermanentItems();
            player.Ship.ResetIntegrityStats();

            var previousPosition = player.Position;

            // Destroyed while already standing on a planet/starport (the
            // common case: a PvP loss on a shared planet/starbase) —
            // "nearest" planet would trivially resolve back to that same
            // hex (distance 0), reading as if nothing happened at all.
            // Send them to a random DIFFERENT one instead.
            var wasOnPlanetOrStarport = map.TryGetHex(previousPosition, out var currentHex)
                && currentHex.Terrain == TerrainType.PlanetOrStarport;

            var destination = wasOnPlanetOrStarport
                ? FindRandomOtherPlanetOrStarport(map, previousPosition, rng)
                : FindNearestPlanetOrStarport(map, previousPosition);

            if (destination == null)
                return true; // no planet/starport anywhere on the map — nothing to relocate to.

            player.Position = destination.Value;
            // Tells the map view to skip the normal travel glide for
            // this position change and materialize the marker instead —
            // see Player.JustTeleported.
            player.MarkTeleported();

            var place = map.TryGetHex(destination.Value, out var destinationHex) && !string.IsNullOrEmpty(destinationHex.Name)
                ? destinationHex.Name
                : destination.Value.ToString();
            // Money/non-permanent items were already cleared above
            // (ClearMoneyAndNonPermanentItems) the instant this player
            // was found depleted — the notice used to go out without
            // ever actually saying so, leaving a player who tows in with
            // 0 money no idea why.
            var moneyLostClause = moneyLost > 0 ? " All your money has been lost." : string.Empty;
            player.SetPendingTurnStartNotice(
                $"Your ship was destroyed! You've been towed to {place}, with Hull and Energy restored.{moneyLostClause}");

            return true;
        }

        private static HexCoordinate? FindNearestPlanetOrStarport(GameMap map, HexCoordinate from)
        {
            HexCoordinate? nearest = null;
            var nearestDistance = int.MaxValue;

            foreach (var hex in map.Hexes)
            {
                if (hex.Terrain != TerrainType.PlanetOrStarport)
                    continue;

                var distance = HexMath.Distance(from, hex.Coordinate);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = hex.Coordinate;
                }
            }

            return nearest;
        }

        private static HexCoordinate? FindRandomOtherPlanetOrStarport(GameMap map, HexCoordinate from, Random rng)
        {
            var candidates = map.Hexes
                .Where(hex => hex.Terrain == TerrainType.PlanetOrStarport && hex.Coordinate != from)
                .Select(hex => hex.Coordinate)
                .ToList();

            // Only one planet/starport on the whole map (the one the
            // player's already standing on) — nowhere else to send them.
            if (candidates.Count == 0)
                return null;

            return candidates[rng.Next(candidates.Count)];
        }
    }
}
