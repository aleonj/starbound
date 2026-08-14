using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;
using UnityEngine;

namespace StarBound.Tests
{
    public class HexLayoutTests
    {
        [Test]
        public void AxialToWorld_OriginMapsToWorldOrigin()
        {
            var world = HexLayout.AxialToWorld(new HexCoordinate(0, 0), hexRadius: 1f);

            Assert.AreEqual(Vector3.zero, world);
        }

        [Test]
        public void AxialToWorld_NeighborsAreEquidistantFromOrigin()
        {
            var origin = HexLayout.AxialToWorld(new HexCoordinate(0, 0), hexRadius: 1f);

            foreach (var neighbor in HexMath.Neighbors(new HexCoordinate(0, 0)))
            {
                var neighborWorld = HexLayout.AxialToWorld(neighbor, hexRadius: 1f);
                var distance = Vector3.Distance(origin, neighborWorld);

                Assert.AreEqual(Mathf.Sqrt(3f), distance, 0.01f);
            }
        }
    }
}
