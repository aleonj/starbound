using System;
using StarBound.Core;

namespace StarBound.Movement
{
    // Fixed face table for one physical d6. Index 0 = face "1" ... index 5
    // = face "6". Which face shows which number doesn't matter for
    // gameplay, only the resulting terrain type per roll.
    public sealed class MovementDie
    {
        private readonly TerrainType[] faces;

        public MovementDie(params TerrainType[] faces)
        {
            if (faces == null || faces.Length != 6)
                throw new ArgumentException("A d6 must have exactly 6 faces.", nameof(faces));

            this.faces = faces;
        }

        public TerrainType FaceAt(int rollResult)
        {
            if (rollResult < 1 || rollResult > 6)
                throw new ArgumentOutOfRangeException(nameof(rollResult));

            return faces[rollResult - 1];
        }
    }
}
