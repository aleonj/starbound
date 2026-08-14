using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class HexMathTests
    {
        [Test]
        public void Neighbors_ReturnsSixDistinctCoordinates()
        {
            var origin = new HexCoordinate(0, 0);

            var neighbors = HexMath.Neighbors(origin).ToList();

            Assert.AreEqual(6, neighbors.Count);
            Assert.AreEqual(6, neighbors.Distinct().Count());
        }

        [Test]
        public void Neighbors_AreAllExactlyDistanceOneFromOrigin()
        {
            var origin = new HexCoordinate(3, -2);

            foreach (var neighbor in HexMath.Neighbors(origin))
            {
                Assert.AreEqual(1, HexMath.Distance(origin, neighbor));
            }
        }

        [Test]
        public void Distance_ToSelf_IsZero()
        {
            var coordinate = new HexCoordinate(4, -1);

            Assert.AreEqual(0, HexMath.Distance(coordinate, coordinate));
        }

        [TestCase(0, 0, 1, 0, 1)]
        [TestCase(0, 0, 2, -1, 2)]
        [TestCase(0, 0, -2, 1, 2)]
        [TestCase(0, 0, 3, 3, 6)]
        [TestCase(1, 1, 1, 1, 0)]
        public void Distance_MatchesExpectedHexDistance(int aq, int ar, int bq, int br, int expected)
        {
            var a = new HexCoordinate(aq, ar);
            var b = new HexCoordinate(bq, br);

            Assert.AreEqual(expected, HexMath.Distance(a, b));
        }

        [Test]
        public void Neighbor_InvalidDirection_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HexMath.Neighbor(new HexCoordinate(0, 0), 6));
        }
    }
}
