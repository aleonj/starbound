using StarBound.Core;

namespace StarBound.Movement
{
    public class RolledDie
    {
        public int DieIndex { get; }
        public TerrainType Terrain { get; }
        public bool IsSpent { get; private set; }

        public RolledDie(int dieIndex, TerrainType terrain)
        {
            DieIndex = dieIndex;
            Terrain = terrain;
        }

        public void MarkSpent() => IsSpent = true;
    }
}
