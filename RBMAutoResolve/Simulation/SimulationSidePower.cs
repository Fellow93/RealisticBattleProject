using System;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace RBMAutoResolve
{
    /// <summary>
    /// What the map believes one side of a battle is worth: every party on it priced by
    /// <see cref="PartyBase.CalculateCurrentStrength"/> and summed. Whichever military power model is installed answers
    /// that -- RBM Campaign's equipment-based troop power when it is on (it patches GetPowerOfParty), vanilla's tier
    /// curve when it is off -- so this needs no path of its own for either, and the auto-resolve log's "power:" line
    /// reads right with RBM Campaign on or off. RBM Campaign's StrategicTroopPower.SidePower and its raid-decision
    /// landing discount forward here, so there is one copy of each.
    /// </summary>
    public static class SimulationSidePower
    {
        /// <summary>
        /// The least an amphibious raider's land strength can be discounted to. A party at sea whose hulls are all
        /// deep-water can put no one ashore and by the letter of the cap is worth nothing on land -- true, but a raw
        /// zero is a brittle thing to hand a scoring model (it turns "a poor raid" into "no party at all"), so the
        /// discount bottoms out here and leaves a token weight. It never fires for a party with a shallow hull, which
        /// is every party that would actually be choosing to raid.
        /// </summary>
        private const float MinLandingFactor = 0.1f;

        /// <summary>
        /// The whole strength of one side, as above. Zero for a missing or empty side; never throws, since it is only
        /// ever asked for a log line or a prompt.
        /// </summary>
        public static float Of(MapEventSide side)
        {
            return Of(side, false);
        }

        /// <summary>
        /// As above, but with <paramref name="discountLanding"/> each party is priced at only the share of it that can
        /// come off its ships (see <see cref="LandingFactor"/>) -- what War Sails' sim actually puts on the beach.
        /// Passed true only for the ATTACKER of a naval raid; the defenders stand on land and are always priced whole.
        /// </summary>
        public static float Of(MapEventSide side, bool discountLanding)
        {
            if (side == null)
            {
                return 0f;
            }
            float total = 0f;
            foreach (MapEventParty mapEventParty in side.Parties)
            {
                PartyBase party = (mapEventParty != null) ? mapEventParty.Party : null;
                if (party == null)
                {
                    continue;
                }
                try
                {
                    float strength = party.CalculateCurrentStrength();
                    if (discountLanding)
                    {
                        strength *= LandingFactor(party);
                    }
                    total += strength;
                }
                catch (Exception)
                {
                }
            }
            return total;
        }

        /// <summary>
        /// The share of <paramref name="party"/>'s men it can put ashore in one wave: its shallow-draft hulls' main-deck
        /// crew over its healthy men, clamped to [<see cref="MinLandingFactor"/>, 1]. War Sails caps a beach landing's
        /// party to that deck crew (NavalDLCCombatSimulationModel.GetParticipatingTroopCount). A party with no
        /// shallow hulls gets the floor, so callers ask only about a party that really is landing from the water.
        /// </summary>
        public static float LandingFactor(PartyBase party)
        {
            if (party == null || party.MemberRoster == null)
            {
                return 1f;
            }
            return LandingFactorFromCapacity(ShallowLandingCapacity(party), party.MemberRoster.TotalHealthyCount);
        }

        /// <summary>The men a party can put ashore in one wave: its main-deck crew across every shallow-draft hull it
        /// owns. Mirrors NavalDLC's GetShallowShipDeckCrewCapacity. Zero for a party with no shallow hulls (and so for
        /// every party when War Sails is absent, whose ship list is empty).</summary>
        private static int ShallowLandingCapacity(PartyBase party)
        {
            MBReadOnlyList<Ship> ships = (party != null) ? party.Ships : null;
            if (ships == null || ships.Count == 0)
            {
                return 0;
            }

            int landingCapacity = 0;
            foreach (Ship ship in ships)
            {
                if (ship != null && ship.ShipHull != null && ship.ShipHull.CanNavigateShallowWater)
                {
                    landingCapacity += ship.MainDeckCrewCapacity;
                }
            }
            return landingCapacity;
        }

        /// <summary>The fraction of a party's strength that fits into <paramref name="landingCapacity"/> beach slots,
        /// clamped to [<see cref="MinLandingFactor"/>, 1]. A party that can land everyone -- or has no men to land --
        /// is undiscounted.</summary>
        private static float LandingFactorFromCapacity(int landingCapacity, int healthyMen)
        {
            if (healthyMen <= 0 || landingCapacity >= healthyMen)
            {
                return 1f;
            }
            return MathF.Max(MinLandingFactor, (float)landingCapacity / healthyMen);
        }
    }
}
