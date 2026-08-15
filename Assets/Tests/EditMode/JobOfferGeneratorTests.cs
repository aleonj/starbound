using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Economy;
using StarBound.Map;

namespace StarBound.Tests
{
    public class JobOfferGeneratorTests
    {
        private static GameMap BuildMap()
        {
            var map = new GameMap(radius: 3, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport)); // current planet
            map.SetHex(new Hex(new HexCoordinate(2, 0), TerrainType.PlanetOrStarport)); // distance 2
            map.SetHex(new Hex(new HexCoordinate(0, -1), TerrainType.ClearSpace) { Engagement = EngagementTier.Easy });

            return map;
        }

        [Test]
        public void GenerateOffer_IncludesMiningAndTransportToOtherPlanetsWithDistanceScaledReward()
        {
            var map = BuildMap();
            var currentPlanet = new HexCoordinate(0, 0);
            var otherPlanet = new HexCoordinate(2, 0);
            var distance = HexMath.Distance(currentPlanet, otherPlanet);

            var offer = JobOfferGenerator.GenerateOffer(new Random(1), currentPlanet, map);

            var mining = offer.Single(j => j.Type == JobType.Mining);
            var transport = offer.Single(j => j.Type == JobType.Transport);
            Assert.AreEqual(otherPlanet, mining.Destination);
            Assert.AreEqual(distance * JobPricing.MiningRewardPerHexDistance, mining.Reward);
            Assert.AreEqual(otherPlanet, transport.Destination);
            Assert.AreEqual(distance * JobPricing.TransportRewardPerHexDistance, transport.Reward);
        }

        [Test]
        public void GenerateOffer_IncludesBountyForMarkedEngagementHex()
        {
            var map = BuildMap();
            var currentPlanet = new HexCoordinate(0, 0);

            var offer = JobOfferGenerator.GenerateOffer(new Random(1), currentPlanet, map);

            var bounty = offer.Single(j => j.Type == JobType.BountyHunting);
            Assert.AreEqual(new HexCoordinate(0, -1), bounty.Destination);
            Assert.AreEqual(EngagementTier.Easy, bounty.BountyTier);
            Assert.AreEqual(JobPricing.BountyReward(EngagementTier.Easy), bounty.Reward);
        }

        [Test]
        public void GenerateOffer_ExcludesTheCurrentPlanetItself()
        {
            var map = BuildMap();
            var currentPlanet = new HexCoordinate(0, 0);

            var offer = JobOfferGenerator.GenerateOffer(new Random(1), currentPlanet, map);

            Assert.IsFalse(offer.Any(j => j.Type != JobType.BountyHunting && j.Destination == currentPlanet));
        }

        [Test]
        public void GenerateOffer_ReturnsFewerThanOfferSizeWhenCandidatePoolIsSmaller()
        {
            var map = new GameMap(radius: 2, Difficulty.Medium);
            map.SetHex(new Hex(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport)); // only the current planet, no other candidates

            var offer = JobOfferGenerator.GenerateOffer(new Random(1), new HexCoordinate(0, 0), map);

            Assert.AreEqual(0, offer.Count);
        }

        [Test]
        public void GenerateOffer_IsDeterministicForSameSeed()
        {
            var map = BuildMap();
            var currentPlanet = new HexCoordinate(0, 0);

            var offerA = JobOfferGenerator.GenerateOffer(new Random(7), currentPlanet, map);
            var offerB = JobOfferGenerator.GenerateOffer(new Random(7), currentPlanet, map);

            CollectionAssert.AreEqual(
                offerA.Select(j => (j.Type, j.Destination, j.Reward)).ToList(),
                offerB.Select(j => (j.Type, j.Destination, j.Reward)).ToList());
        }
    }
}
