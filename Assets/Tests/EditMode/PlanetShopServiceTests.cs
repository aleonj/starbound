using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class PlanetShopServiceTests
    {
        private static Hex BuildPlanetHex() => new(new HexCoordinate(0, 0), TerrainType.PlanetOrStarport);

        [Test]
        public void GetOffer_FirstVisit_GeneratesAFullOffer()
        {
            var hex = BuildPlanetHex();

            var offer = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(1));

            Assert.AreEqual(ShopOfferGenerator.OfferSize, offer.Count);
        }

        [Test]
        public void GetOffer_CalledAgainSameTurn_ReturnsTheSameItems()
        {
            var hex = BuildPlanetHex();
            var first = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(1)).ToList();

            var second = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(99));

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void RecordPurchase_RemovesTheItemAndDoesNotRefillSameTurn()
        {
            var hex = BuildPlanetHex();
            var offer = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(1));
            var bought = offer[0];

            PlanetShopService.RecordPurchase(hex, bought, currentTurn: 1);
            var afterPurchase = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(2));

            Assert.AreEqual(ShopOfferGenerator.OfferSize - 1, afterPurchase.Count);
            CollectionAssert.DoesNotContain(afterPurchase.ToList(), bought);
        }

        [Test]
        public void GetOffer_OnADifferentTurnAfterAPurchase_RefillsTheMissingSlot()
        {
            var hex = BuildPlanetHex();
            var offer = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(1));
            var bought = offer[0];
            PlanetShopService.RecordPurchase(hex, bought, currentTurn: 1);

            var nextTurnOffer = PlanetShopService.GetOffer(hex, currentTurn: 2, new Random(2));

            Assert.AreEqual(ShopOfferGenerator.OfferSize, nextTurnOffer.Count);
        }

        [Test]
        public void GetOffer_Refill_NeverDuplicatesAnItemStillOnTheShelf()
        {
            var hex = BuildPlanetHex();
            var offer = PlanetShopService.GetOffer(hex, currentTurn: 1, new Random(1));
            var bought = offer[0];
            var stillThere = offer.Skip(1).ToList();
            PlanetShopService.RecordPurchase(hex, bought, currentTurn: 1);

            var refilled = PlanetShopService.GetOffer(hex, currentTurn: 2, new Random(2)).ToList();

            Assert.AreEqual(refilled.Count, refilled.Distinct().Count());
            CollectionAssert.IsSubsetOf(stillThere, refilled);
        }

        [Test]
        public void GetOffer_TwoDifferentHexes_TrackIndependentOffers()
        {
            var hexOne = BuildPlanetHex();
            var hexTwo = new Hex(new HexCoordinate(5, 0), TerrainType.PlanetOrStarport);

            var offerOne = PlanetShopService.GetOffer(hexOne, currentTurn: 1, new Random(1));
            PlanetShopService.RecordPurchase(hexOne, offerOne[0], currentTurn: 1);

            var offerTwo = PlanetShopService.GetOffer(hexTwo, currentTurn: 1, new Random(1));

            Assert.AreEqual(ShopOfferGenerator.OfferSize, offerTwo.Count,
                "Buying from one planet's shelf must not affect another planet's.");
        }
    }
}
