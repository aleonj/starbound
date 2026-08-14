using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class TerrainDistributionTableTests
    {
        [Test]
        public void HazardProportions_IncreaseWithDifficulty()
        {
            var easy = TerrainDistributionTable.For(Difficulty.Easy);
            var medium = TerrainDistributionTable.For(Difficulty.Medium);
            var hard = TerrainDistributionTable.For(Difficulty.Hard);

            float Hazards(TerrainDistribution d) => d.Asteroids + d.Mines + d.Debris;

            Assert.Less(Hazards(easy), Hazards(medium));
            Assert.Less(Hazards(medium), Hazards(hard));
        }
    }
}
