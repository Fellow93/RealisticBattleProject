using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A stack holds only what it can still put to use -- enough to finish its own upgrades, plus a
    /// war chest scaled to its tier. This ceiling governs how much a stack keeps before its upkeep
    /// spends the rest on food and drink.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// The war chest a stack holds on top of what its upgrades need, so a stack that spills one day
        /// is not left penniless before the next market. Configured per man per tier and scaled by the
        /// troop's tier, so a veteran keeps a deeper purse than a recruit -- fitting for a top-tier
        /// stack that has no upgrade left to save for and would otherwise keep nothing at all.
        /// </summary>
        private static int GetWarChestPerMan(CharacterObject character)
        {
            return RBMConfig.RBMConfig.troopSpoilsWarChestGoldPerTier * MathF.Max(1, character.Tier);
        }

        /// <summary>
        /// The most a stack can usefully hold: enough to upgrade every man down its dearest path, plus
        /// a war chest scaled to its tier. A top-tier troop has no upgrade to save for, so in its place
        /// its own equipment sets the headroom -- an elite is worth its kit and holds a purse to match,
        /// rather than collapsing to the war chest alone.
        /// </summary>
        public static int GetSpoilsCap(PartyBase party, CharacterObject character)
        {
            int stackSize = GetStackSize(party, character);
            if (stackSize <= 0)
            {
                return 0;
            }
            int dearestUpgrade = 0;
            CharacterObject[] targets = character.UpgradeTargets;
            if (targets != null && targets.Length > 0)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    dearestUpgrade = MathF.Max(dearestUpgrade, GetSpoilsCostForUpgrade(party, character, targets[i]));
                }
            }
            else
            {
                // No dearer kit to save for -- a top-tier troop. Value its own equipment in the upgrade's
                // place, scaled by the same multiplier an upgrade cost uses, so cap and upgrade price are
                // quoted in the one coin and an elite's ceiling scales with how dear its gear is.
                dearestUpgrade = MathF.Round(GetEquipmentValue(character) * RBMConfig.RBMConfig.troopUpgradeCostMultiplier);
            }
            return (dearestUpgrade + GetWarChestPerMan(character)) * stackSize;
        }

        /// <summary>
        /// Who a party's spoils gold is paid to: its owner if one is alive, else the hero leading it.
        /// Null when no one can be paid.
        /// </summary>
        public static Hero GetPartyPayee(PartyBase party)
        {
            if (party == null)
            {
                return null;
            }
            Hero payee = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
            return (payee != null && payee.IsAlive) ? payee : null;
        }
    }
}
