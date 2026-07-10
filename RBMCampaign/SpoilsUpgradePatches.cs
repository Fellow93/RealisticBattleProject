using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
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

    /// <summary>
    /// Reserves the spoils an open party screen has promised but not yet charged, and corrects the gold
    /// it staged. Vanilla asks the model for one per-man price and multiplies it by the batch size,
    /// which overcharges: spoils are consumed a man at a time, so the men the stockpile reaches go free
    /// and only the rest pay.
    /// </summary>
    public static class PartyScreenStagedUpgrades
    {
        private static readonly Dictionary<CharacterObject, int> _stagedSpoils = new Dictionary<CharacterObject, int>();

        /// <summary>Spoils promised to upgrades the player has queued but not yet confirmed.</summary>
        public static int GetStagedSpoils(PartyBase party, CharacterObject character)
        {
            int staged;
            return (party == PartyBase.MainParty && _stagedSpoils.TryGetValue(character, out staged)) ? staged : 0;
        }

        /// <summary>Hands the reservation over on commit, so it is spent exactly once.</summary>
        public static int ConsumeStagedSpoils(PartyBase party, CharacterObject character)
        {
            if (party != PartyBase.MainParty)
            {
                return 0;
            }
            int staged;
            if (!_stagedSpoils.TryGetValue(character, out staged))
            {
                return 0;
            }
            _stagedSpoils.Remove(character);
            return staged;
        }

        // If a clear is ever missed the next screen open resets it, and until then upgrades are
        // quoted slightly high rather than the spoils pool being corrupted.
        private static void Clear()
        {
            _stagedSpoils.Clear();
        }

        /// <summary>
        /// Runs before vanilla rather than after it. UpgradeTroop ends by invoking UpdateDelegate,
        /// which is what drives PartyCharacterVM.InitializeUpgrades and so recomputes the quoted
        /// price and its tooltip. Reserving from a Postfix would leave that recomputation reading a
        /// stockpile the upgrade had already claimed, and the screen would quote the man who just
        /// left the roster.
        /// </summary>
        [HarmonyPatch(typeof(PartyScreenLogic))]
        [HarmonyPatch("UpgradeTroop")]
        private class TrackStagedUpgrade
        {
            private static readonly MethodInfo SetPartyGoldChangeAmount =
                AccessTools.Method(typeof(PartyScreenLogic), "SetPartyGoldChangeAmount");

            private static void Prefix(PartyScreenLogic __instance, PartyScreenLogic.PartyCommand command)
            {
                // Vanilla bails on an invalid command without touching gold or roster, so the
                // reservation must not happen either. ValidateCommand is pure, so asking twice is free.
                if (!SpoilsPool.IsEnabled || !__instance.ValidateCommand(command))
                {
                    return;
                }
                PartyBase party = PartyBase.MainParty;
                CharacterObject character = command.Character;
                CharacterObject upgradeTarget = character.UpgradeTargets[command.UpgradeTarget];
                int count = command.TotalNumber;

                // Priced against the stockpile as it stands, before this batch draws on it.
                int spend = SpoilsPool.GetBatchSpoilsSpend(party, character, upgradeTarget, count);
                int actualGold = RBMCampaignPatches.GetBatchUpgradeGoldCost(party, character, upgradeTarget, count);

                int staged;
                _stagedSpoils.TryGetValue(character, out staged);
                _stagedSpoils[character] = staged + spend;

                // Vanilla is about to subtract perManPrice * count, and it will quote that per-man
                // price against the stockpile the reservation above just depleted. Mirror the read it
                // is going to make, then pre-credit the difference so its subtraction lands on
                // actualGold. Reading before the reservation would mirror a price vanilla never uses.
                int chargedByVanilla = character.GetUpgradeGoldCost(party, command.UpgradeTarget) * count;
                int correction = chargedByVanilla - actualGold;
                if (correction != 0 && SetPartyGoldChangeAmount != null)
                {
                    SetPartyGoldChangeAmount.Invoke(__instance, new object[] { __instance.CurrentData.PartyGoldChangeAmount + correction });
                }

                SpoilsLog.Log("UPGRADE", PartyBase.MainParty, "party screen staged " + count + "x " + SpoilsLog.Describe(character)
                    + " -> " + SpoilsLog.Describe(upgradeTarget)
                    + "| spoils reserved " + spend + " (total " + _stagedSpoils[character] + ")"
                    + ", gold " + actualGold + " (vanilla will charge " + chargedByVanilla + ")");
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

        // Runs after DoneLogic has fired PlayerUpgradedTroopsEvent, so the spoils are already charged.
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
