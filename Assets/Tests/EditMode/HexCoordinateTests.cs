using NUnit.Framework;
using StarBound.Core;

namespace StarBound.Tests
{
    public class HexCoordinateTests
    {
        [Test]
        public void Equality_IsBasedOnQAndR()
        {
            var a = new HexCoordinate(2, -1);
            var b = new HexCoordinate(2, -1);
            var c = new HexCoordinate(1, -1);

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }
    }
}
