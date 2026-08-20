using System;
using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Combat
{
    // Hand-authored flavor/intro lines shown alongside the opponent stat
    // block when an NPC engagement starts (see EngagementTrigger.TryTrigger)
    // — purely narrative color, no effect on stat resolution. Same
    // "hand-authored string[] pool" convention as PlanetNames.cs; no
    // scriptable objects/external data files.
    public static class EngagementFlavorText
    {
        private static readonly Dictionary<EngagementTier, string[]> Pools = new()
        {
            [EngagementTier.Easy] = new[]
            {
                "A scavenger skiff peels off from a debris field, its patchwork hull glinting as it angles toward you.",
                "Your sensors ping a drifting wreck — then it powers up and turns to face you.",
                "A lone raider, more desperate than dangerous, cuts across your bow demanding a toll.",
                "Something small and fast breaks from the shadow of an asteroid, weapons already hot.",
                "A prospector's boat, spooked by your approach, opens fire first and asks questions never.",
                "An automated sentry drone, its owner long gone, locks onto the nearest heat signature — yours.",
                "A junker crew mistakes you for easy pickings and comes in loud and disorganized.",
                "Static crackles on an open channel — then a warning shot crosses your bow.",
                "A skiff limping on one thruster still manages to bring its guns to bear.",
                "Salvagers protecting a claim they clearly don't legally hold challenge you to back off — or else.",
                "A rust-streaked interceptor drops out of a blind spot, more bark than bite but committed all the same.",
                "Someone's forgotten minefield drone mistakes your transponder for hostile and engages.",
                "A smuggler running light cargo panics at the sight of you and opens fire rather than talk.",
                "A pair of scavenger drones, stitched together from three different wrecks, buzz in for a fight.",
                "A hot-headed freelancer looking to make a name for themselves picks the wrong target: you.",
            },
            [EngagementTier.Medium] = new[]
            {
                "A raider wing peels out of formation, moving with the discipline of people who do this for a living.",
                "A mercenary corvette hails you once, curtly, before its weapons batteries charge.",
                "Rival prospectors, tired of losing claims to newcomers, decide to settle it the hard way.",
                "A pirate frigate flying a hand-painted skull banner blocks your path with clear intent.",
                "A bounty crew, contract in hand, recognizes your ship from a wanted broadcast.",
                "An organized salvage gang, armed and territorial, isn't interested in sharing this wreck field.",
                "A hired gun, paid well and not asking who by, lines up a clean intercept vector.",
                "Two raider craft flank you in a maneuver they've clearly run before.",
                "A privateer, letter of marque or not, decides your cargo is worth the risk.",
                "A well-drilled pirate crew signals for your surrender before opening fire regardless.",
                "Mercenaries under contract to keep this lane \"clear\" consider you the problem to solve.",
                "A rival crew, still bitter about a job you beat them to, comes looking for payback.",
                "An armed convoy escort mistakes your approach for a raid and strikes first.",
                "A syndicate enforcer ship runs you down with the calm of someone who's done this before.",
                "A weathered raider captain, more careful than reckless, probes your defenses before committing.",
            },
            [EngagementTier.Hard] = new[]
            {
                "The wreckage ahead was bait — a notorious ambush crew was waiting for exactly this moment.",
                "A warlord's flagship, scarred from a dozen battles, turns its full attention on you.",
                "Word of your cargo reached the wrong ears — an infamous bounty hunter has come to collect.",
                "A pirate armada's lead ship signals the rest of its fleet the instant it spots you.",
                "Something old and heavily armed drifts out of the dark — a relic no one was supposed to find.",
                "A rogue military vessel, its allegiance long forgotten, doesn't bother with a warning shot.",
                "The self-styled \"Butcher of the Vale\" has been hunting this sector for a name like yours.",
                "A syndicate kill team, sent for you specifically, drops out of stealth on all sides.",
                "An ambush fleet, patient and disciplined, springs the trap the moment you're committed.",
                "A ghost ship long thought lost reactivates its weapons the instant you draw near.",
                "The pirate lord known only as Ash doesn't send warnings — just gunships.",
                "A black-flagged dreadnought, feared across three systems, blocks the only way through.",
                "Whatever's inside that derelict has been waiting a long time for someone like you to find it.",
                "A mercenary company's entire strike wing has been contracted for one target: you.",
                "The notorious raider known as the Hollow Duke greets you with a full broadside, no words at all.",
            },
        };

        // Avoids repeating the immediately-previous pick per tier (per the
        // story's "if practical" acceptance criterion) — small static
        // cache, resets on domain reload, which is fine since this only
        // needs to avoid a same-session immediate repeat, not persist
        // further.
        private static readonly Dictionary<EngagementTier, int> lastPickedIndex = new();

        public static string PickRandom(EngagementTier tier, Random rng)
        {
            var pool = Pools[tier];
            int index;
            do
            {
                index = rng.Next(pool.Length);
            } while (pool.Length > 1 && lastPickedIndex.TryGetValue(tier, out var last) && index == last);

            lastPickedIndex[tier] = index;
            return pool[index];
        }
    }
}
