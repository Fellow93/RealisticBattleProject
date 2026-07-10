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
    public static class GearUpgradePatches
    {
        private struct UpgradeOption
        {
            public CharacterObject Target;
            public CharacterObject UpgradeTarget;
            public int Count;
            public int GoldCost;
            public int XpCost;
            public int StackSize;
            public float Chance;
        }

        /// <summary>
        /// Reimplements PartyUpgraderCampaignBehavior.UpgradeReadyTroops so AI upgrades draw down
        /// the gear they were discounted for. Gold affordability, already checked here against the
        /// gear-discounted price, is what actually limits the AI. The vanilla helpers cannot be
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
                if (!GearPool.IsEnabled || party.MobileParty == null)
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

                    int goldCost = character.GetUpgradeGoldCost(party, i);
                    if (party.LeaderHero != null && goldCost != 0 && count * goldCost > party.MobileParty.PartyTradeGold)
                    {
                        count = party.MobileParty.PartyTradeGold / goldCost;
                        if (count == 0)
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
                            GoldCost = goldCost,
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
                // Priced before the roster shrinks, matching the gold cost the upgrade was quoted at.
                int gearSpend = GearPool.GetGearSpend(party, option.Target, option.UpgradeTarget, option.Count, option.StackSize);
                GearPool.AddGear(party, option.Target, -gearSpend);
                memberRoster.AddToCounts(option.Target, -option.Count);
                memberRoster.AddToCounts(option.UpgradeTarget, option.Count);
                GearPool.ClearGearIfStackGone(party, option.Target);
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
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, option.GoldCost * option.Count, true);
                }
            }
        }
    }

    /// <summary>
    /// Counts the men an open party screen has promoted but not yet paid for. Gear is only charged
    /// when the player confirms, so until then those men still hold a share of the stack's gear and
    /// must keep counting toward it.
    /// </summary>
    public static class PartyScreenStagedUpgrades
    {
        private static readonly Dictionary<CharacterObject, int> _staged = new Dictionary<CharacterObject, int>();

        public static int GetStagedCount(PartyBase party, CharacterObject character)
        {
            int staged;
            return (party == PartyBase.MainParty && _staged.TryGetValue(character, out staged)) ? staged : 0;
        }

        // If a clear is ever missed the next screen open resets it, and until then upgrades are
        // quoted slightly high rather than the gear pool being corrupted.
        private static void Clear()
        {
            _staged.Clear();
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("UpgradeTroop")]
        private class TrackStagedUpgrade
        {
            private static void Postfix(PartyScreenLogic.PartyCommand command)
            {
                if (!GearPool.IsEnabled)
                {
                    return;
                }
                int staged;
                _staged.TryGetValue(command.Character, out staged);
                _staged[command.Character] = staged + command.TotalNumber;
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("Reset")]
        private class ClearOnReset
        {
            private static void Postfix()
            {
                Clear();
            }
        }

        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("ResetToLastSavedPartyScreenData")]
        private class ClearOnResetToLastSaved
        {
            private static void Postfix()
            {
                Clear();
            }
        }

        // Runs after DoneLogic has fired PlayerUpgradedTroopsEvent, so the gear is already charged.
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("OnPartyScreenClosed")]
        private class ClearOnClose
        {
            private static void Postfix()
            {
                Clear();
            }
        }
    }
}
