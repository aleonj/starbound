using System.Collections.Generic;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Map;

namespace StarBound.Tests
{
    public class EngagementVisibilityTests
    {
        [Test]
        public void GetVisibleTier_Undiscovered_ReturnsNoneRegardlessOfActualTier()
        {
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Hard };

            var visibleTier = EngagementVisibility.GetVisibleTier(hex, new HashSet<HexCoordinate>());

            Assert.AreEqual(EngagementTier.None, visibleTier);
        }

        [Test]
        public void GetVisibleTier_Discovered_ReturnsTheRealTier()
        {
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.Medium };
            var discovered = new HashSet<HexCoordinate> { hex.Coordinate };

            var visibleTier = EngagementVisibility.GetVisibleTier(hex, discovered);

            Assert.AreEqual(EngagementTier.Medium, visibleTier);
        }

        [Test]
        public void GetVisibleTier_DiscoveredButCleared_ReturnsNone()
        {
            // Once a marker is actually defeated (win-cleared), it's
            // invisible to everyone — no separate "forget" step needed,
            // since hex.Engagement is already None regardless of discovery.
            var hex = new Hex(new HexCoordinate(0, 0), TerrainType.ClearSpace) { Engagement = EngagementTier.None };
            var discovered = new HashSet<HexCoordinate> { hex.Coordinate };

            var visibleTier = EngagementVisibility.GetVisibleTier(hex, discovered);

            Assert.AreEqual(EngagementTier.None, visibleTier);
        }
    }
}
