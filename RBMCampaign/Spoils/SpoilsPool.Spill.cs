using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A stack keeps only what it can still put to use -- enough to finish its own upgrades and a
    /// small purse besides -- and hands the rest up to its keeper as coin. A veteran stack with
    /// nothing left to buy stops sitting on dead loot and starts paying a dividend instead, so an
    /// elite army you have stopped upgrading becomes an asset rather than a pile of stranded spoils.
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
        /// a war chest scaled to its tier. A top-tier troop has no upgrade to save for, so its ceiling
        /// is just that war chest and nearly all its loot and wage become coin.
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
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    dearestUpgrade = MathF.Max(dearestUpgrade, GetSpoilsCostForUpgrade(character, targets[i]));
                }
            }
            return (dearestUpgrade + GetWarChestPerMan(character)) * stackSize;
        }

        /// <summary>
        /// Sweeps every stack's surplus -- what it holds over its ceiling -- into its keeper's purse as
        /// gold. A point of spoils is a gold piece, so the coin handed up equals the spoils drawn down;
        /// the party's own treasury is the richer for it. Runs on the daily tick, after the day's wage
        /// has landed, so a stack cannot spill money it was about to be given anyway.
        /// </summary>
        /// <remarks>
        /// Silent, the way the wage deposit is: a daily trickle of background economy, not a windfall
        /// worth a message. It is logged under SPILL for the player's own party. Applies to every
        /// party -- an AI lord's surplus funds his own upgrades from the same treasury they draw on.
        /// <para>
        /// troopSpoilsGoldSpillMultiplier scales how much of the surplus is handed up each day: 1 sweeps
        /// it all, 0 keeps spoils a closed loop, and a value between drains the overflow down to the cap
        /// over several days rather than in one.
        /// </para>
        /// </remarks>
        public static void SpillSurplusToGold(PartyBase party)
        {
            float spillFraction = MathF.Clamp(RBMConfig.RBMConfig.troopSpoilsGoldSpillMultiplier, 0f, 1f);
            if (spillFraction <= 0f)
            {
                return;
            }
            Hero payee = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
            TroopRoster roster = party.MemberRoster;
            if (payee == null || !payee.IsAlive || roster == null)
            {
                return;
            }

            int spilledTotal = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                int purse = GetSpoils(party, element.Character);
                int cap = GetSpoilsCap(party, element.Character);
                int surplus = purse - cap;
                if (surplus <= 0)
                {
                    continue;
                }
                int spill = MathF.Round(surplus * spillFraction);
                if (spill <= 0)
                {
                    continue;
                }
                if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
                {
                    SpoilsLog.Log("SPILL", party, SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": over cap by " + surplus + ", handed up " + spill + " as gold (pool " + purse
                        + " -> " + (purse - spill) + ", cap " + cap + ")");
                }
                AddSpoils(party, element.Character, -spill);
                spilledTotal += spill;
            }

            if (spilledTotal <= 0)
            {
                return;
            }
            // Null giver mints the coin into the payee's purse, the mirror of how an upgrade pays gold
            // out to a null receiver.
            GiveGoldAction.ApplyBetweenCharacters(null, payee, spilledTotal, true);
            if (SpoilsLog.IsEnabled && party == PartyBase.MainParty)
            {
                SpoilsLog.Log("SPILL", party, SpoilsLog.Describe(party) + " handed up " + spilledTotal
                    + " gold in surplus spoils to " + payee.Name);
            }
        }
    }
}
