using System;

namespace StarBound.Map
{
    // Hand-authored name pool for Starports — deliberately a different
    // convention from PlanetNames' bare evocative place names: every
    // entry here is "<designation> <facility type>" (Anchorage, Terminal,
    // Station, Platform, Docks, Waystation, Depot, Yard, Outpost), so a
    // Starport reads as a built, man-made installation rather than a
    // world. Root words are kept distinct from PlanetNames' pool so a
    // Planet and a Starport never appear to share a name on the same map.
    public static class StarportNames
    {
        private static readonly string[] Pool =
        {
            "Kestrel Anchorage", "Corvid Terminal", "Gantry Station", "Rivet Platform",
            "Foundry Docks", "Ledger Waystation", "Harrow's Depot", "Ferro Yard",
            "Dunmore Outpost", "Stonebridge Station", "Corrigan's Anchorage", "Talbrook Terminal",
            "Windmere Platform", "Hollowmast Docks", "Cargill Waystation", "Pellham Depot",
            "Rennick Yard", "Anchorfall Station", "Mallow Point Terminal", "Brackwell Outpost",
        };

        // Same deterministic partial-shuffle approach as PlanetNames.DrawUnique
        // — see that method's own comment for why count > Pool.Length is
        // deliberately left to throw rather than silently duplicate.
        public static string[] DrawUnique(Random rng, int count)
        {
            var shuffled = (string[])Pool.Clone();
            for (var i = 0; i < count; i++)
            {
                var swapIndex = i + rng.Next(shuffled.Length - i);
                (shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
            }

            var result = new string[count];
            Array.Copy(shuffled, result, count);
            return result;
        }
    }
}
