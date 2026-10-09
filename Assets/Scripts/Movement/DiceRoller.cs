using System;
using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Movement
{
    public static class DiceRoller
    {
        // map is optional — legacy/test callers that don't pass one keep
        // the old unfiltered behavior unchanged. When provided, each die
        // only rolls terrain types actually present somewhere on it, so a
        // map generated without e.g. Tradelane never wastes a roll on a
        // face the player could never possibly use (user-reported
        // unfairness — MovementDiceSet's weighting has always been fixed
        // per-die, with zero awareness of what MapGenerator actually put
        // on the board).
        public static DiceHand Roll(Random rng, GameMap map = null)
        {
            var dice = MovementDiceSet.Dice;
            var rolled = new List<RolledDie>(dice.Length);
            var presentTerrain = map != null ? new HashSet<TerrainType>(map.Hexes.Select(h => h.Terrain)) : null;

            for (var i = 0; i < dice.Length; i++)
            {
                var terrain = presentTerrain == null
                    ? dice[i].FaceAt(rng.Next(1, 7))
                    : PickValidFace(dice[i], presentTerrain, rng);
                rolled.Add(new RolledDie(i, terrain));
            }

            return new DiceHand(rolled);
        }

        // Uniform among the die's OWN faces that are valid on this map —
        // preserves that die's relative weighting between whichever
        // terrain types DO exist, rather than looping a blind reroll
        // (which degrades badly on a terrain-sparse map) or substituting
        // a different terrain type entirely.
        private static TerrainType PickValidFace(MovementDie die, HashSet<TerrainType> presentTerrain, Random rng)
        {
            var validFaces = new List<TerrainType>(6);
            for (var faceValue = 1; faceValue <= 6; faceValue++)
            {
                var terrain = die.FaceAt(faceValue);
                if (presentTerrain.Contains(terrain))
                    validFaces.Add(terrain);
            }

            // Pathological: nothing on this die exists anywhere on the
            // map. ClearSpace alone makes this effectively impossible in
            // practice (every generated map has some, and every die has
            // at least one ClearSpace face) — fall back to the die's
            // ordinary unfiltered roll rather than return something undefined.
            return validFaces.Count > 0 ? validFaces[rng.Next(validFaces.Count)] : die.FaceAt(rng.Next(1, 7));
        }
    }
}
