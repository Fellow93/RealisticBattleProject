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
        /// Sweeps every stack's surplus -- what it holds over its ceiling -- into its keeper's purse as
        /// gold. A point of spoils is a gold piece, so the coin handed up equals the spoils drawn down;
        /// the party's own treasury is the richer for it. Runs on the daily tick, after the day's wage
        /// has landed, so a stack cannot spill money it was about to be given anyway.
        /// </summary>
        /// <remarks>
        /// Silent, the way the wage deposit is: a daily trickle of background economy, not a windfall
        /// worth a message. The summary is logged under SPILL for every party -- the party column tells
        /// the player's from an AI lord's -- while the per-stack detail is the player's own. Applies to
        /// every party -- an AI lord's surplus funds his own upgrades from the same treasury they draw on.
        /// <para>
        /// troopSpoilsGoldSpillFraction ceilings how much each man's share can hand up in a day, priced
        /// as a share of his battle kit -- the same way a wage is -- rather than a flat sum, so a
        /// better-armed man hands up more and the trickle scales with the troop. However deep a stack's
        /// surplus, only this much per man drains up daily, and a large overflow empties over many days.
        /// 0 keeps spoils a closed loop, spent only on troops, food and drink.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Who the surplus a party spills is paid to: its owner if one is alive, else the hero leading it.
        /// The finance breakdown projects the spill off the same rule, so the line the player is shown and
        /// the gold the tick actually mints can never name a different pocket. Null when no one can be paid.
        /// </summary>
        public static Hero GetSpillPayee(PartyBase party)
        {
            if (party == null)
            {
                return null;
            }
            Hero payee = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
            return (payee != null && payee.IsAlive) ? payee : null;
        }

        public static void SpillSurplusToGold(PartyBase party)
        {
            float spillFraction = RBMConfig.RBMConfig.troopSpoilsGoldSpillFraction;
            if (spillFraction <= 0f)
            {
                return;
            }
            Hero payee = GetSpillPayee(party);
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
                int spill = GetStackDailySpill(party, element.Character, element.Number, spillFraction);
                if (spill <= 0)
                {
                    continue;
                }
                if (SpoilsLog.Verbose && party == PartyBase.MainParty)
                {
                    int purse = GetSpoils(party, element.Character);
                    int cap = GetSpoilsCap(party, element.Character);
                    SpoilsLog.LogVerbose("SPILL", party, SpoilsLog.Describe(element.Character) + " x" + element.Number
                        + ": over cap by " + (purse - cap) + ", trickled up " + spill + " as gold (pool " + purse
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
            // Logged for every party -- an AI lord's surplus is real gold moving into his purse the same
            // as the player's -- with the party column (MAIN/AI) telling them apart. The per-stack detail
            // above stays main-only so the daily tick does not write a line per soldier across the world.
            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("SPILL", party, SpoilsLog.Describe(party) + " handed up " + spilledTotal
                    + " gold in surplus spoils to " + payee.Name);
            }
        }

        /// <summary>
        /// The gold one stack would hand up on a daily tick: its surplus over the cap, ceilinged by a
        /// share of the man's battle kit per head. Zero when the stack sits at or under its cap. The one
        /// place the daily trickle is priced, so the spill that drains the pool and the projection the
        /// finance breakdown shows can never quote a different number. Read-only.
        /// </summary>
        private static int GetStackDailySpill(PartyBase party, CharacterObject character, int number, float spillFraction)
        {
            int surplus = GetSpoils(party, character) - GetSpoilsCap(party, character);
            if (surplus <= 0)
            {
                return 0;
            }
            // The daily ceiling, priced as a share of the man's battle kit -- horse and harness included,
            // the way a wage is -- and scaled by the number of men in the stack: the surplus drains up no
            // faster than this, so a deep purse trickles out over many days, and a richer-equipped stack
            // hands up more than a levy sitting on the same overflow.
            int perManPerDay = MathF.Round(GetEquipmentValueWithMount(character) * spillFraction);
            int dailyCap = perManPerDay * MathF.Max(1, number);
            return MathF.Max(0, MathF.Min(surplus, dailyCap));
        }

        /// <summary>
        /// What a party's stacks would, between them, hand up as gold on the next daily tick, priced from
        /// the pools as they stand. The finance breakdown draws its "troop spoils" line from this, so the
        /// coin the player is shown to expect is the coin the tick will actually spill. Read-only -- it
        /// touches no purse, so it is safe to call as often as a tooltip is drawn.
        /// </summary>
        public static int ProjectDailySpill(PartyBase party)
        {
            float spillFraction = RBMConfig.RBMConfig.troopSpoilsGoldSpillFraction;
            if (!IsEnabled || spillFraction <= 0f || party == null)
            {
                return 0;
            }
            TroopRoster roster = party.MemberRoster;
            if (roster == null)
            {
                return 0;
            }
            int projected = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character.IsHero)
                {
                    continue;
                }
                projected += GetStackDailySpill(party, element.Character, element.Number, spillFraction);
            }
            return projected;
        }
    }
}
