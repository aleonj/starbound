using System;
using System.Linq;
using NUnit.Framework;
using StarBound.Core;
using StarBound.Shop;

namespace StarBound.Tests
{
    public class ShopOfferGeneratorTests
    {
        [Test]
        public void GenerateOffer_ReturnsConfiguredOfferSize()
        {
            var offer = ShopOfferGenerator.GenerateOffer(new Random(1));

            Assert.AreEqual(ShopOfferGenerator.OfferSize, offer.Count);
        }

        [Test]
        public void GenerateOffer_ReturnsDistinctItems()
        {
            var offer = ShopOfferGenerator.GenerateOffer(new Random(1));

            Assert.AreEqual(offer.Count, offer.Distinct().Count());
        }

        [Test]
        public void GenerateOffer_IsDeterministicForSameSeed()
        {
            var offerA = ShopOfferGenerator.GenerateOffer(new Random(7));
            var offerB = ShopOfferGenerator.GenerateOffer(new Random(7));

            CollectionAssert.AreEqual(
                offerA.Select(i => i.Name).ToList(),
                offerB.Select(i => i.Name).ToList());
        }

        [Test]
        public void GenerateOffer_CanProduceDifferentResultsAcrossSeeds()
        {
            ItemDefinition[] firstOffer = null;
            var sawDifferentOffer = false;

            for (var seed = 0; seed < 20 && !sawDifferentOffer; seed++)
            {
                var offer = ShopOfferGenerator.GenerateOffer(new Random(seed)).ToArray();
                if (firstOffer == null)
                    firstOffer = offer;
                else if (!offer.SequenceEqual(firstOffer))
                    sawDifferentOffer = true;
            }

            Assert.IsTrue(sawDifferentOffer, "GenerateOffer appears to always return the same fixed slice.");
        }
    }
}
