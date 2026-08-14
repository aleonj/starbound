using NUnit.Framework;
using StarBound.Map;

namespace StarBound.Tests
{
    public class MapSizeTests
    {
        [TestCase(MapSize.Small, 4)]
        [TestCase(MapSize.Medium, 6)]
        [TestCase(MapSize.Large, 8)]
        public void ToRadius_MatchesExpectedValue(MapSize size, int expectedRadius)
        {
            Assert.AreEqual(expectedRadius, size.ToRadius());
        }
    }
}
