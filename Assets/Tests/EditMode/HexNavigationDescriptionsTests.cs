using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class HexNavigationDescriptionsTests
    {
        private static GameMap BuildMapWithLandmark(int seed, out HexCoordinate landmark, out HexCoordinate target)
        {
            var map = new GameMap(radius: 3, Difficulty.Medium, seed);
            landmark = new HexCoordinate(0, 0);
            target = new HexCoordinate(2, -1);

            map.SetHex(new Hex(landmark, TerrainType.PlanetOrStarport) { Name = "Meridian" });
            map.SetHex(new Hex(target, TerrainType.ClearSpace));

            return map;
        }

        [Test]
        public void Describe_PlanetHex_ReturnsItsOwnNameDirectly()
        {
            var map = BuildMapWithLandmark(seed: 1, out var landmark, out _);

            Assert.AreEqual("Meridian", HexNavigationDescriptions.Describe(map, landmark));
        }

        [Test]
        public void Describe_OrdinaryHex_IncludesFlavorNameAndLandmarkBearing()
        {
            var map = BuildMapWithLandmark(seed: 1, out _, out var target);

            var description = HexNavigationDescriptions.Describe(map, target);

            StringAssert.Contains("of Meridian", description);
            Assert.IsTrue(
                description.Contains("rimward") || description.Contains("coreward") ||
                description.Contains("spinward") || description.Contains("trailing"),
                $"Expected a spinward/trailing/coreward/rimward bearing, got: {description}");
            Assert.AreNotEqual(0, HexMath.Distance(target, new HexCoordinate(0, 0)),
                "Sanity check on the fixture itself.");
        }

        [Test]
        public void Describe_IsDeterministicForTheSameMapAndCoordinate()
        {
            var map = BuildMapWithLandmark(seed: 42, out _, out var target);

            var first = HexNavigationDescriptions.Describe(map, target);
            var second = HexNavigationDescriptions.Describe(map, target);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void Describe_VariesAcrossDifferentMapSeeds()
        {
            var descriptions = Enumerable.Range(1, 10)
                .Select(seed =>
                {
                    var map = BuildMapWithLandmark(seed, out _, out var target);
                    return HexNavigationDescriptions.Describe(map, target);
                })
                .Distinct()
                .ToList();

            Assert.Greater(descriptions.Count, 1,
                "10 different map seeds produced the exact same flavor name every time — FlavorName likely isn't actually keying off the seed.");
        }

        [Test]
        public void Describe_NoLandmarksOnTheMap_ReturnsFlavorNameAlone()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium, seed: 1);
            var target = new HexCoordinate(0, 0);
            map.SetHex(new Hex(target, TerrainType.ClearSpace));

            var description = HexNavigationDescriptions.Describe(map, target);

            StringAssert.DoesNotContain(" of ", description);
        }
    }
}
