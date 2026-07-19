using HarmonyLib;
using SandBox.Tournaments.MissionLogics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMTournament
{
    internal partial class RBMTournament
    {
        [HarmonyPatch(typeof(TournamentGame))]
        private class TournamentGamePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("UpdateTournamentPrize")]
            private static bool UpdateTournamentPrizePrefix(ref TournamentGame __instance, ref bool includePlayer, ref bool removeCurrentPrize)
            {
                if (__instance.Prize != null)
                {
                    if ((int)__instance.Prize.Tier != calculatePlayerTournamentTier())
                    {
                        return true;
                    }
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(TournamentManager))]
        private class GivePrizeToWinnerPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GivePrizeToWinner")]
            private static bool GivePrizeToWinnerPrefix(ref TournamentManager __instance, ref TournamentGame tournament, ref Hero winner, ref bool isPlayerParticipated)
            {
                if (!isPlayerParticipated)
                {
                    tournament.UpdateTournamentPrize(isPlayerParticipated);
                }
                if (winner.PartyBelongedTo == MobileParty.MainParty)
                {
                    EquipmentElement eePrize = new EquipmentElement(tournament.Prize);
                    IReadOnlyList<ItemModifier> itemModifiers = eePrize.Item?.ItemComponent?.ItemModifierGroup?.ItemModifiers;
                    List<ItemModifier> viableEM = new List<ItemModifier>();
                    if (itemModifiers != null && itemModifiers.Count > 0)
                    {
                        foreach (ItemModifier im in itemModifiers)
                        {
                            if (im.ProductionDropScore > 0 && im.PriceMultiplier >= 1f)
                            {
                                viableEM.Add(im);
                            }
                        }
                        if (viableEM != null && viableEM.Count > 0)
                        {
                            foreach (ItemModifier im in viableEM)
                            {
                                float randomFloat = MBRandom.RandomFloat * 100f;
                                int roll = 100 - MathF.Round(randomFloat);
                                int rollNeeded = 100 - MathF.Round(im.ProductionDropScore);
                                if (roll >= rollNeeded)
                                {
                                    MBTextManager.SetTextVariable("Name", im.Name);
                                    MBTextManager.SetTextVariable("Roll", roll);
                                    MBTextManager.SetTextVariable("Need", rollNeeded);
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_TOU_003}Congratulations, you successfully rolled for {Name} item modifier, rolled:{Roll} needed: {Need}").ToString()));
                                    eePrize.SetModifier(im);
                                    break;
                                }
                                else
                                {
                                    MBTextManager.SetTextVariable("Name", im.Name);
                                    MBTextManager.SetTextVariable("Roll", roll);
                                    MBTextManager.SetTextVariable("Need", rollNeeded);
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_TOU_004}You missed roll for {Name} item modifier, rolled:{Roll} needed: {Need}").ToString()));
                                }
                            }
                        }
                    }
                    winner.PartyBelongedTo.ItemRoster.AddToCounts(eePrize, 1);
                }
                else if (winner.Clan != null)
                {
                    GiveGoldAction.ApplyBetweenCharacters(null, winner.Clan.Leader, tournament.Town.MarketData.GetPrice(tournament.Prize));
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(DefaultTournamentModel))]
        private class DefaultTournamentModelPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetRenownReward")]
            private static bool GetRenownRewardPrefix(ref DefaultTournamentModel __instance, Hero winner, Town town, ref int __result)
            {
                if (winner.IsHumanPlayerCharacter)
                {
                    float baseRenown = 3f;
                    float tournamentTier = (float)calculatePlayerTournamentTier();
                    float tournamentTierBonus = (baseRenown * (tournamentTier - 1f)) / 2f;
                    float gainedRenown = baseRenown + tournamentTierBonus;
                    if (winner.GetPerkValue(DefaultPerks.OneHanded.Duelist))
                    {
                        gainedRenown *= 2f;
                    }
                    __result = MathF.Ceiling(gainedRenown);
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }
    }
}
