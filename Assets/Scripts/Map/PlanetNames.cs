using System;

namespace StarBound.Map
{
    // Hand-authored name pool for planets/starports — maps only ever have
    // a handful (budget tops out around 13 on the largest/easiest map),
    // so a procedural generator would be overkill. First-pass flavor,
    // easy to extend.
    public static class PlanetNames
    {
        private static readonly string[] Pool =
        {
            "Kestrel Station", "Meridian", "Vantage Point", "Halcyon", "Ironhold",
            "Driftwatch", "New Meridian", "Solace", "Farrow's Landing", "Beacon Reach",
            "Cinder", "Perch", "Outer Vale", "Amaranth", "Shepherd's Rest",
            "Tanager", "Wren's Crossing", "Ashport", "Corvid Station", "Sable Reach",
            "Marrow", "Talon's Edge", "Greywater", "Nightfall", "Sunder",
            "Bastion", "Thistledown", "Lowlight", "Verge", "Ember Reach",
            "Quillon", "Windrow",
        };

        // Deterministic given rng — a partial Fisher-Yates shuffle of a
        // cloned pool, returning the first `count` entries. Deliberately
        // doesn't handle count > Pool.Length gracefully (would throw) —
        // that would mean map budgets grew past what this pool was
        // authored for, worth surfacing loudly rather than silently
        // duplicating names.
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
