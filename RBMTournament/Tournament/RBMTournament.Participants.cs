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
        [HarmonyPatch(typeof(FightTournamentGame))]
        public static class FightTournamentGamePatch
        {
            public static List<CharacterObject> FillTroopListUntilTier(CharacterObject starterTroop, int tier)
            {
                List<CharacterObject> troops = new List<CharacterObject>();
                List<CharacterObject> lastUpgradeTargets = new List<CharacterObject>();

                troops.Add(starterTroop);

                lastUpgradeTargets.Clear();
                lastUpgradeTargets.Add(starterTroop);

                for (int i = 1; i < tier; i++)
                {
                    List<CharacterObject> newUpgradeTargets = new List<CharacterObject>();
                    foreach (CharacterObject co in lastUpgradeTargets)
                    {
                        troops.AddRange(co.UpgradeTargets);
                        newUpgradeTargets.AddRange(co.UpgradeTargets);
                    }
                    lastUpgradeTargets = newUpgradeTargets;
                }

                return troops;
            }

            public static void AddTroopsFromCulture(CultureObject culture, ref List<CharacterObject> troops)
            {
                foreach (CharacterObject character in CharacterObject.All)
                {
                    if (character.Occupation == Occupation.Soldier && character.Culture == culture && !character.HiddenInEncyclopedia)
                    {
                        if (!character.Name.Contains("Conspiracy"))
                        {
                            troops.Add(character);
                        }
                    }
                }
            }

            public static void AddMercenaryTroops(ref List<CharacterObject> troops)
            {
                foreach (CharacterObject character in CharacterObject.All)
                {
                    if (character.Occupation == Occupation.Mercenary && !character.HiddenInEncyclopedia)
                    {
                        if (!character.Name.Contains("Conspiracy"))
                        {
                            troops.Add(character);
                        }
                    }
                }
            }


            public static List<CharacterObject> FillTroopListFromCulture(CultureObject culture)
            {
                List<CharacterObject> troops = new List<CharacterObject>();

                AddTroopsFromCulture(culture, ref troops);

                //switch (culture.GetCultureCode())
                //{
                //    case CultureCode.Sturgia:
                //        {
                //            AddTroopsFromSubCulture(CultureCode.Nord, ref troops);
                //            break;
                //        }
                //    case CultureCode.Battania:
                //        {
                //            AddTroopsFromSubCulture(CultureCode.Vakken, ref troops);
                //            break;
                //        }
                //    case CultureCode.Aserai:
                //        {
                //            AddTroopsFromSubCulture(CultureCode.Darshi, ref troops);
                //            break;
                //        }
                //}

                AddMercenaryTroops(ref troops);

                return troops;
            }

            public static int calculateNpcTournamentTier(CharacterObject npc)
            {
                int playerLevelTier = MathF.Min(MathF.Max(MathF.Ceiling(((float)npc.Level - 5f) / 5f), 0), Campaign.Current.Models.CharacterStatsModel.MaxCharacterTier);
                Equipment playerEquipment = npc.RandomBattleEquipment;
                float armorTierSum = 0f;
                int countOfArmor = 0;
                for (EquipmentIndex index = EquipmentIndex.ArmorItemBeginSlot; index < EquipmentIndex.ArmorItemEndSlot; index++)
                {
                    if (playerEquipment[index].Item != null)
                    {
                        armorTierSum += playerEquipment[index].Item.Tierf;
                        countOfArmor++;
                    }
                }
                int playerArmorTier = countOfArmor > 0 ? MathF.Round(armorTierSum / countOfArmor) : 0;

                int playerTier = playerLevelTier > armorTierSum ? playerLevelTier : playerArmorTier;
                playerTier = MBMath.ClampInt(playerTier, 1, 6);
                return playerTier;
            }

            [HarmonyPrefix]
            [HarmonyPatch("GetParticipantCharacters")]
            private static bool GetParticipantCharactersPrefix(ref FightTournamentGame __instance, ref List<CharacterObject> __result, Settlement settlement, bool includePlayer = true)
            {
                List<CharacterObject> list = new List<CharacterObject>();
                if (includePlayer)
                {
                    int playerTier = calculatePlayerTournamentTier();
                    if (playerTier >= 5)
                    {
                        for (int i = 0; i < settlement.Parties.Count; i++)
                        {
                            if (list.Count >= __instance.MaximumParticipantCount)
                            {
                                break;
                            }
                            Hero leaderHero = settlement.Parties[i].LeaderHero;
                            if (leaderHero != null && leaderHero.CharacterObject != null && !leaderHero.CharacterObject.IsPlayerCharacter && calculateNpcTournamentTier(leaderHero.CharacterObject) >= 5)
                            {
                                if (leaderHero.CurrentSettlement != settlement)
                                {
                                    Debug.Print(leaderHero.StringId + " is in settlement.Parties list but current settlement is not, tournament settlement: " + settlement.StringId);
                                }
                                if (!list.Contains(leaderHero.CharacterObject))
                                {
                                    list.Add(leaderHero.CharacterObject);
                                }
                            }
                        }
                        for (int j = 0; j < settlement.HeroesWithoutParty.Count; j++)
                        {
                            if (list.Count >= __instance.MaximumParticipantCount)
                            {
                                break;
                            }
                            Hero hero = settlement.HeroesWithoutParty[j];
                            if (hero != null && hero.CharacterObject != null && !hero.CharacterObject.IsPlayerCharacter && calculateNpcTournamentTier(hero.CharacterObject) >= 5 && hero.IsLord)
                            {
                                if (hero.CurrentSettlement != settlement)
                                {
                                    Debug.Print(hero.StringId + " is in settlement.HeroesWithoutParty list but current settlement is not, tournament settlement: " + settlement.StringId);
                                }
                                if (!list.Contains(hero.CharacterObject))
                                {
                                    list.Add(hero.CharacterObject);
                                }
                            }
                        }
                        for (int k = 0; k < settlement.HeroesWithoutParty.Count; k++)
                        {
                            if (list.Count >= __instance.MaximumParticipantCount)
                            {
                                break;
                            }
                            Hero hero2 = settlement.HeroesWithoutParty[k];
                            if (hero2 != null && hero2.CharacterObject != null && !hero2.CharacterObject.IsPlayerCharacter && calculateNpcTournamentTier(hero2.CharacterObject) >= 5)
                            {
                                if (hero2.CurrentSettlement != settlement)
                                {
                                    Debug.Print(hero2.StringId + " is in settlement.HeroesWithoutParty list but current settlement is not, tournament settlement: " + settlement.StringId);
                                }
                                if (!list.Contains(hero2.CharacterObject))
                                {
                                    list.Add(hero2.CharacterObject);
                                }
                            }
                        }
                        for (int l = 0; l < settlement.Parties.Count; l++)
                        {
                            if (list.Count >= __instance.MaximumParticipantCount)
                            {
                                break;
                            }
                            foreach (TroopRosterElement item2 in settlement.Parties[l].MemberRoster.GetTroopRoster())
                            {
                                if (list.Count >= __instance.MaximumParticipantCount)
                                {
                                    break;
                                }
                                CharacterObject character = item2.Character;
                                if (character != null && character.IsHero && character.HeroObject.Clan == Clan.PlayerClan && !character.IsPlayerCharacter && calculateNpcTournamentTier(character) >= 5)
                                {
                                    if (character.HeroObject.CurrentSettlement != settlement)
                                    {
                                        Debug.Print(character.HeroObject.StringId + " is in settlement.HeroesWithoutParty list but current settlement is not, tournament settlement: " + settlement.StringId);
                                    }
                                    if (!list.Contains(character))
                                    {
                                        list.Add(character);
                                    }
                                }
                            }
                        }
                    }
                    if (playerTier >= 5)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(new  TextObject("{=RBM_TOU_001}Main tournament").ToString()));
                    }
                    else
                    {
                        MBTextManager.SetTextVariable("TIER", playerTier);
                        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_TOU_002}Lower tier tournament: Tier {TIER}").ToString()));
                    }
                    //CultureObject cultureMercenaryObject = Game.Current.ObjectManager.GetObject<CultureObject>("neutral");
                    CultureObject culture = Settlement.CurrentSettlement.Culture;

                    //List<CharacterObject> troops = FillTroopListUntilTier(culture.BasicTroop, playerTier);
                    //List<CharacterObject> eliteTroops = FillTroopListUntilTier(culture.EliteBasicTroop, playerTier);
                    //List<CharacterObject> mercenaryTroops = FillTroopListUntilTier(culture.BasicMercenaryTroop, playerTier);
                    List<CharacterObject> troops = FillTroopListFromCulture(culture);

                    list.Add(CharacterObject.PlayerCharacter);
                    for (int i = 0; i < __instance.MaximumParticipantCount && list.Count < __instance.MaximumParticipantCount; i++)
                    {
                        //float randomFloat = MBRandom.RandomFloat;
                        CharacterObject troopToAdd = null;
                        //if (randomFloat < 0.6f)
                        //{
                        List<CharacterObject> troopsFromTier = troops.FindAll((CharacterObject troop) => troop != null && playerTier >= 5 ? (troop.Tier >= 5) : troop.Tier == playerTier);
                        if (!troopsFromTier.IsEmpty())
                        {
                            troopToAdd = troopsFromTier[MBRandom.RandomInt(troopsFromTier.Count)];
                        }
                        //}
                        //else if (randomFloat < 0.85f)
                        //{
                        //    List<CharacterObject> troopsFromTier = eliteTroops.FindAll((CharacterObject troop) => troop != null && playerTier >= 5 ? (troop.Tier >= 5) : troop.Tier == playerTier);
                        //    if (!troopsFromTier.IsEmpty())
                        //    {
                        //        troopToAdd = troopsFromTier[MBRandom.RandomInt(troopsFromTier.Count)];
                        //    }
                        //}
                        //else
                        //{
                        //    List<CharacterObject> troopsFromTier = mercenaryTroops.FindAll((CharacterObject troop) => troop != null && playerTier >= 5 ? (troop.Tier >= 5) : troop.Tier == playerTier);
                        //    if (!troopsFromTier.IsEmpty())
                        //    {
                        //        troopToAdd = troopsFromTier[MBRandom.RandomInt(troopsFromTier.Count)];
                        //    }
                        //}

                        if (troopToAdd != null)
                        {
                            list.Add(troopToAdd);
                        }
                        else
                        {
                            //List<CharacterObject> troopsFromTier = new List<CharacterObject>();
                            int tier = playerTier;
                            do
                            {
                                troopsFromTier = troops.FindAll((CharacterObject troop) => troop != null && tier >= 5 ? (troop.Tier >= 5) : troop.Tier == tier);
                                tier--;
                                if (tier == 0)
                                {
                                    break;
                                }
                            } while (troopsFromTier.Count <= 0);
                            if (troopsFromTier.Count > 0)
                            {
                                troopToAdd = troopsFromTier[MBRandom.RandomInt(troopsFromTier.Count - 1)];
                                if (troopToAdd != null)
                                {
                                    list.Add(troopToAdd);
                                }
                            }
                        }
                    }
                }
                else
                {
                    return true;
                }
                __result = list;
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("GetTournamentPrize")]
            private static void GetTournamentPrizePostfix(ref FightTournamentGame __instance, ref ItemObject __result, bool includePlayer, int lastRecordedLordCountForTournamentPrize)
            {
                //if (includePlayer)
                //{
                CultureObject culture = __instance.Town.Culture;
                int playerTier = calculatePlayerTournamentTier();
                List<ItemObject> list = new List<ItemObject>();
                foreach (ItemObject item in Items.All)
                {
                    if (!item.NotMerchandise && (item.Type == ItemObject.ItemTypeEnum.Bow || item.Type == ItemObject.ItemTypeEnum.Crossbow || item.Type == ItemObject.ItemTypeEnum.Shield || item.IsCraftedWeapon || item.IsMountable || item.ArmorComponent != null) && !item.IsCraftedByPlayer)
                    {
                        if (item.Culture == culture)
                        {
                            if (playerTier >= 5 ? (int)item.Tier >= playerTier - 1 : (int)item.Tier == playerTier)
                            {
                                list.Add(item);
                            }
                        }
                    }
                }
                if (list.Count > 0)
                {
                    __result = list.GetRandomElement();
                }
                //}
            }
        }
    }
}
