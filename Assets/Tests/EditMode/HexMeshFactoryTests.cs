using NUnit.Framework;
using StarBound.Map;

namespace StarBound.Tests
{
    public class HexMeshFactoryTests
    {
        [Test]
        public void CreateFlatTopHex_HasSevenVerticesAndSixTriangles()
        {
            var mesh = HexMeshFactory.CreateFlatTopHex(1f);

            Assert.AreEqual(7, mesh.vertexCount);
            Assert.AreEqual(18, mesh.triangles.Length); // 6 triangles * 3 indices
        }

        [Test]
        public void CreateFlatTopHex_BoundsMatchGivenRadius()
        {
            var mesh = HexMeshFactory.CreateFlatTopHex(2f);

            Assert.AreEqual(4f, mesh.bounds.size.x, 0.001f);
        }
    }
}
