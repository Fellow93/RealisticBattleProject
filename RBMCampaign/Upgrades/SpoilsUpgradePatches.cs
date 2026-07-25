using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    public static class SpoilsUpgradePatches
    {
        private struct UpgradeOption
        {
            public CharacterObject Target;
            public CharacterObject UpgradeTarget;
            public int Count;
            /// <summary>The whole batch, not one man: spoils make the leading men free.</summary>
            public int TotalGoldCost;
            public int XpCost;
            public int StackSize;
            public float Chance;
        }

        /// <summary>
        /// Reimplements PartyUpgraderCampaignBehavior.UpgradeReadyTroops so AI upgrades draw down
        /// the spoils they were discounted for. Gold affordability, already checked here against the
        /// spoils-discounted price, is what actually limits the AI. The vanilla helpers cannot be
        /// patched directly: they are private and pass a private nested struct, which no patch
        /// signature can name.
        /// </summary>
        [HarmonyPatch(typeof(PartyUpgraderCampaignBehavior))]
        [HarmonyPatch("UpgradeReadyTroops")]
        private class OverrideUpgradeReadyTroops
        {
            // SupplyTown gate: the town outfitting the party currently being processed. Resolved once at
            // the top of each Prefix pass and read back in ApplyEffects; the campaign tick is
            // single-threaded so this scratch field is safe.
            private static Town _supplyTown;

            private static bool Prefix(PartyBase party)
            {
                // Vanilla dereferences party.MobileParty unguarded further down, so anything that
                // survives it today must be a mobile party. Leave the rest to vanilla.
                if (!SpoilsPool.IsEnabled || party.MobileParty == null)
                {
                    return true;
                }
                if (party == PartyBase.MainParty || !party.IsActive)
                {
                    return false;
                }

                // SupplyTown gate: resolve the town that will outfit this party's upgrades and skip the
                // party when the feature is on and nothing supplies it -- neither a friendly settlement it
                // is stationed in nor a friendly city within reach. One call does the gate and the town.
                _supplyTown = null;
                if (UpgradeSupply.IsEnabled && !UpgradeSupply.CanUpgradeNear(party.MobileParty, out _supplyTown))
                {
                    // Once per party per day, and only when it actually has troops that could promote, so
                    // the reason an AI army is stuck at low tiers is visible without flooding the log.
                    if (SpoilsLog.IsEnabled && HasUpgradeableTroop(party.MemberRoster))
                    {
                        SpoilsLog.LogOnce("nosupply-" + party.Id + "-" + (int)(CampaignTime.Now.ToHours / 24),
                            "UPGRADE", party, SpoilsLog.Describe(party) + " held off upgrading: no friendly town within "
                            + RBMConfig.RBMConfig.troopUpgradeSupplyRadius + " units");
                    }
                    return false;
                }

                TroopRoster memberRoster = party.MemberRoster;
                PartyTroopUpgradeModel upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (int i = 0; i < memberRoster.Count; i++)
                {
                    TroopRosterElement element = memberRoster.GetElementCopyAtIndex(i);
                    if (!upgradeModel.IsTroopUpgradeable(party, element.Character))
                    {
                        continue;
                    }
                    List<UpgradeOption> options = GetPossibleUpgradeTargets(party, element);
                    if (options.Count > 0)
                    {
                        UpgradeTroop(party, i, SelectPossibleUpgrade(options));
                    }
                }
                return false;
            }

            // A cheap upper bound on "would this party like to upgrade": any non-hero stack that has an
            // upgrade path. Skips the XP/gold checks -- it only gates a once-a-day denial log, so a party
            // of green troops it slightly over-reports is fine, and a party of top-tier men is skipped.
            private static bool HasUpgradeableTroop(TroopRoster roster)
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    CharacterObject character = roster.GetElementCopyAtIndex(i).Character;
                    if (character != null && !character.IsHero && character.UpgradeTargets != null && character.UpgradeTargets.Length > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static List<UpgradeOption> GetPossibleUpgradeTargets(PartyBase party, TroopRosterElement element)
            {
                PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
                PartyTroopUpgradeModel upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                List<UpgradeOption> options = new List<UpgradeOption>();
                CharacterObject character = element.Character;
                if (element.Number - element.WoundedNumber <= 0)
                {
                    return options;
                }

                for (int i = 0; i < character.UpgradeTargets.Length; i++)
                {
                    int count = element.Number - element.WoundedNumber;
                    CharacterObject upgradeTarget = character.UpgradeTargets[i];

                    int xpCost = character.GetUpgradeXpCost(party, i);
                    if (xpCost > 0)
                    {
                        count = MathF.Min(count, element.Xp / xpCost);
                        if (count == 0)
                        {
                            continue;
                        }
                    }

                    // Vanilla divides by the wage delta unguarded -- safe there because its wages are a
                    // function of tier, so a higher-tier target always costs strictly more. RBM prices wages
                    // off equipment value, where a tier-up can be wage-neutral (or cheaper), so the delta can
                    // be 0 and the division must be gated. A non-positive delta needs no clamp at all: the
                    // upgrade cannot push the party further past its payment limit.
                    int wageDelta = wageModel.GetCharacterWage(upgradeTarget) - wageModel.GetCharacterWage(character);
                    if (wageDelta > 0 && upgradeTarget.Tier > character.Tier && party.MobileParty.HasLimitedWage() && party.MobileParty.TotalWage + count * wageDelta > party.MobileParty.PaymentLimit)
                    {
                        count = MathF.Max(0, MathF.Min(count, (party.MobileParty.PaymentLimit - party.MobileParty.TotalWage) / wageDelta));
                        if (count == 0)
                        {
                            continue;
                        }
                    }

                    // Vanilla clamps by perManPrice * count. Spoils make the first men free, so the
                    // batch price is fullPrice * (count - coveredMen) and the party can afford
                    // coveredMen + gold/fullPrice of them.
                    int fullGold = RBMCampaignPatches.GetFullUpgradeGoldCost(party, character, upgradeTarget);
                    if (party.LeaderHero != null && fullGold > 0)
                    {
                        float coveredMen = SpoilsPool.GetCoveredMen(party, character, upgradeTarget);
                        int affordable = (int)(coveredMen + party.MobileParty.PartyTradeGold / (float)fullGold);
                        count = MathF.Min(count, affordable);
                        if (count <= 0)
                        {
                            continue;
                        }
                    }

                    if ((!party.Culture.IsBandit || upgradeTarget.Culture.IsBandit) && (character.Occupation != Occupation.Bandit || upgradeModel.CanPartyUpgradeTroopToTarget(party, character, upgradeTarget)))
                    {
                        options.Add(new UpgradeOption
                        {
                            Target = character,
                            UpgradeTarget = upgradeTarget,
                            Count = count,
                            TotalGoldCost = RBMCampaignPatches.GetBatchUpgradeGoldCost(party, character, upgradeTarget, count),
                            XpCost = xpCost,
                            StackSize = element.Number,
                            Chance = upgradeModel.GetUpgradeChanceForTroopUpgrade(party, character, i)
                        });
                    }
                }
                return options;
            }

            private static UpgradeOption SelectPossibleUpgrade(List<UpgradeOption> options)
            {
                UpgradeOption result = options[0];
                if (options.Count > 1)
                {
                    float totalChance = 0f;
                    foreach (UpgradeOption option in options)
                    {
                        totalChance += option.Chance;
                    }
                    float roll = totalChance * MBRandom.RandomFloat;
                    foreach (UpgradeOption option in options)
                    {
                        roll -= option.Chance;
                        if (roll <= 0f)
                        {
                            result = option;
                            break;
                        }
                    }
                }
                return result;
            }

            private static void UpgradeTroop(PartyBase party, int rosterIndex, UpgradeOption option)
            {
                TroopRoster memberRoster = party.MemberRoster;
                memberRoster.SetElementXp(rosterIndex, memberRoster.GetElementXp(rosterIndex) - option.XpCost * option.Count);
                // Drawn down before the roster shrinks, against the same stockpile the price used.
                int spoilsSpend = SpoilsPool.GetBatchSpoilsSpend(party, option.Target, option.UpgradeTarget, option.Count);
                // A cheaper-kitted upgrade salvages its surplus into the upgradeTarget stack's purse.
                int spoilsCredit = SpoilsPool.GetSpoilsCreditForUpgrade(option.Target, option.UpgradeTarget) * option.Count;
                SpoilsPool.AddSpoils(party, option.Target, -spoilsSpend);
                memberRoster.AddToCounts(option.Target, -option.Count);
                memberRoster.AddToCounts(option.UpgradeTarget, option.Count);
                SpoilsPool.AddSpoils(party, option.UpgradeTarget, spoilsCredit);
                // The leaving men take their share of the purse left after their upgrade is paid for.
                int carried = SpoilsPool.CarrySpoilsOnUpgrade(party, option.Target, option.UpgradeTarget, option.Count, option.StackSize);
                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("UPGRADE", party, SpoilsLog.Describe(party) + " upgraded " + option.Count + "x "
                        + SpoilsLog.Describe(option.Target) + " -> " + SpoilsLog.Describe(option.UpgradeTarget)
                        + " | stack was " + option.StackSize
                        + ", free " + SpoilsPool.GetFreeUpgradeCount(party, option.Target, option.UpgradeTarget)
                        + ", spoils spent " + spoilsSpend
                        + (spoilsCredit > 0 ? ", salvaged " + spoilsCredit + " into " + SpoilsLog.Describe(option.UpgradeTarget) + "'s purse" : "")
                        + (carried > 0 ? ", carried " + carried + " of the purse along" : "")
                        + ", pool " + SpoilsPool.GetSpoils(party, option.Target)
                        + ", gold " + option.TotalGoldCost + ", xp " + (option.XpCost * option.Count));
                }
                SpoilsPool.ClearSpoilsIfStackGone(party, option.Target);
                if (option.Count > 0)
                {
                    ApplyEffects(party, option, spoilsSpend);
                }
            }

            private static void ApplyEffects(PartyBase party, UpgradeOption option, int spoilsSpend)
            {
                Hero payer = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
                // What was actually billed to a hero, which is nothing at all for a party that has none.
                // Only this reaches the town below -- charging one sum and paying over another would mint
                // the difference, and paying over a bill nobody was sent would mint the whole of it.
                int goldCharged = 0;
                if (payer != null && payer.IsAlive)
                {
                    SkillLevelingManager.OnUpgradeTroops(party, option.Target, option.UpgradeTarget, option.Count);
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, option.TotalGoldCost, true);
                    goldCharged = option.TotalGoldCost;
                }

                // What the promotion cost goes over to the town that armed the men -- both halves of it:
                // the gold destroyed by the null-recipient call above, and the spoils drawn from the
                // men's own purse in UpgradeTroop. With the SupplyTown gate on, value-appropriate kit
                // leaves that town's market too; with it off the money still lands, which is why the
                // town is resolved here rather than taken from the gate's own _supplyTown alone.
                //
                // OUTSIDE the payer check, deliberately. UpgradeTroop draws the men's spoils whether or not
                // the party has a hero to bill, so a looter band or an ownerless garrison stack paid for its
                // promotion out of its own purse and, while this sat inside the branch above, had that
                // payment destroyed. A party with no payer simply hands over the spoils leg alone.
                if (UpgradeSupply.PaymentEnabled)
                {
                    Town market = (_supplyTown != null) ? _supplyTown : UpgradeSupply.ResolveMarketTown(party.MobileParty);
                    UpgradeSupply.SupplyUpgradeFromTown(market, party, option.Target, option.UpgradeTarget,
                        option.Count, goldCharged + spoilsSpend);
                }
            }
        }
    }
}
