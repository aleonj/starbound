using System.Collections.Generic;
using System.Linq;
using StarBound.Core;

namespace StarBound.Map
{
    // Whether a hex's engagement marker should actually be drawn for a
    // given viewer. A cleared marker (hex.Engagement == None) is invisible
    // to everyone regardless of discovery — nothing extra to "forget".
    public static class EngagementVisibility
    {
        public static EngagementTier GetVisibleTier(Hex hex, IReadOnlyCollection<HexCoordinate> discovered) =>
            discovered.Contains(hex.Coordinate) ? hex.Engagement : EngagementTier.None;
    }
}
