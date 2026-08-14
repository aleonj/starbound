using StarBound.Core;

namespace StarBound.Movement
{
    // The five physical movement dice every ship rolls each turn. Each
    // die's face weighting is fixed and reflects how inherently difficult
    // that terrain type is to fly through, independent of match difficulty
    // (which instead governs map terrain density and engagement rates
    // elsewhere — see TerrainDistributionTable). The 5 dice are NOT
    // identical: each is individually weighted (not uniform), but across
    // all 5 together every non-planet terrain type stays reachable.
    // Planet/starport never appears on a face — it's reached via the
    // any-die exception instead (see ShipMover).
    // Placeholder balance numbers, tunable here.
    public static class MovementDiceSet
    {
        public static readonly MovementDie[] Dice =
        {
            new(TerrainType.ClearSpace, TerrainType.ClearSpace, TerrainType.Tradelane, TerrainType.Tradelane, TerrainType.Asteroids, TerrainType.Debris),
            new(TerrainType.ClearSpace, TerrainType.ClearSpace, TerrainType.Tradelane, TerrainType.Asteroids, TerrainType.Asteroids, TerrainType.Mines),
            new(TerrainType.ClearSpace, TerrainType.Tradelane, TerrainType.Tradelane, TerrainType.Asteroids, TerrainType.Debris, TerrainType.Wormhole),
            new(TerrainType.ClearSpace, TerrainType.Tradelane, TerrainType.Asteroids, TerrainType.Debris, TerrainType.Mines, TerrainType.Wormhole),
            new(TerrainType.ClearSpace, TerrainType.ClearSpace, TerrainType.Tradelane, TerrainType.Debris, TerrainType.Mines, TerrainType.Wormhole),
        };
    }
}
