using NUnit.Framework;
using StarBound.Combat;
using StarBound.Core;

namespace StarBound.Tests
{
    public class OpponentStatViewTests
    {
        [Test]
        public void Constructor_CopiesEveryDisplayedStat()
        {
            var ship = new Ship(cargoCapacity: 0);
            ship.ApplyStatDelta(CoreStat.Hull, 2);
            ship.ApplyStatDelta(CoreStat.Weapons, 1);
            ship.ApplyStatDelta(CoreStat.Shields, -1);
            ship.ApplyStatDelta(CoreStat.Speed, 3);
            ship.ApplyStatDelta(CoreStat.Energy, -2); // deliberately not reflected anywhere on the view

            var view = new OpponentStatView(ship);

            Assert.AreEqual(5, view.Hull);
            Assert.AreEqual(4, view.Weapons);
            Assert.AreEqual(2, view.Shields);
            Assert.AreEqual(6, view.Speed);
        }
    }
}
