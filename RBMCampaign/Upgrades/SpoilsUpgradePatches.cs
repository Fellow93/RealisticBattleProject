using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
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

                    if (upgradeTarget.Tier > character.Tier && party.MobileParty.HasLimitedWage() && party.MobileParty.TotalWage + count * (wageModel.GetCharacterWage(upgradeTarget) - wageModel.GetCharacterWage(character)) > party.MobileParty.PaymentLimit)
                    {
                        count = MathF.Max(0, MathF.Min(count, (party.MobileParty.PaymentLimit - party.MobileParty.TotalWage) / (wageModel.GetCharacterWage(upgradeTarget) - wageModel.GetCharacterWage(character))));
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
                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("UPGRADE", party, SpoilsLog.Describe(party) + " upgraded " + option.Count + "x "
                        + SpoilsLog.Describe(option.Target) + " -> " + SpoilsLog.Describe(option.UpgradeTarget)
                        + " | stack was " + option.StackSize
                        + ", free " + SpoilsPool.GetFreeUpgradeCount(party, option.Target, option.UpgradeTarget)
                        + ", spoils spent " + spoilsSpend + ", pool " + SpoilsPool.GetSpoils(party, option.Target)
                        + " -> " + (SpoilsPool.GetSpoils(party, option.Target) - spoilsSpend)
                        + ", gold " + option.TotalGoldCost + ", xp " + (option.XpCost * option.Count));
                }
                SpoilsPool.AddSpoils(party, option.Target, -spoilsSpend);
                memberRoster.AddToCounts(option.Target, -option.Count);
                memberRoster.AddToCounts(option.UpgradeTarget, option.Count);
                SpoilsPool.ClearSpoilsIfStackGone(party, option.Target);
                if (option.Count > 0)
                {
                    ApplyEffects(party, option);
                }
            }

            private static void ApplyEffects(PartyBase party, UpgradeOption option)
            {
                Hero payer = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
                if (payer != null && payer.IsAlive)
                {
                    SkillLevelingManager.OnUpgradeTroops(party, option.Target, option.UpgradeTarget, option.Count);
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, option.TotalGoldCost, true);
                }
            }
        }
    }
}
