using HarmonyLib;
using SandBox.Tournaments.MissionLogics;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Roster;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using static SandBox.CampaignBehaviors.LordConversationsCampaignBehavior;

namespace RBMTournament
{
    class RBMTournament
    {

        public static int calculatePlayerTournamentTier()
        {
            int playerLevelTier = MathF.Min(MathF.Max(MathF.Ceiling(((float)CharacterObject.PlayerCharacter.Level - 5f) / 5f), 0), Campaign.Current.Models.CharacterStatsModel.MaxCharacterTier);
            Equipment playerEquipment = CharacterObject.PlayerCharacter.RandomBattleEquipment;
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

        [HarmonyPatch(typeof(TournamentFightMissionController))]
        class TournamentFightMissionControllerPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("Simulate")]
            static bool SimulatePrefix(ref TournamentFightMissionController __instance, ref TournamentMatch ____match, ref CultureObject ____culture, ref bool ____isSimulated,
                ref List<TournamentParticipant> ____aliveParticipants, ref List<TournamentTeam> ____aliveTeams)
            {
                ____isSimulated = false;
                if (__instance.Mission.Agents.Count == 0)
                {
                    ____aliveParticipants = ____match.Participants.ToList();
                    ____aliveTeams = ____match.Teams.ToList();
                }
                TournamentParticipant tournamentParticipant = ____aliveParticipants.FirstOrDefault((TournamentParticipant x) => x.Character == CharacterObject.PlayerCharacter);
                if (tournamentParticipant != null)
                {
                    TournamentTeam team = tournamentParticipant.Team;
                    foreach (TournamentParticipant participant in team.Participants)
                    {
                        participant.ResetScore();
                        ____aliveParticipants.Remove(participant);
                    }
                    ____aliveTeams.Remove(team);
                    MethodInfo method2 = typeof(TournamentFightMissionController).GetMethod("AddScoreToRemainingTeams", BindingFlags.NonPublic | BindingFlags.Instance);
                    method2.DeclaringType.GetMethod("AddScoreToRemainingTeams");
                    method2.Invoke(__instance, new object[] { });
                }
                Dictionary<TournamentParticipant, Tuple<float, float>> dictionary = new Dictionary<TournamentParticipant, Tuple<float, float>>();
                foreach (TournamentParticipant aliveParticipant in ____aliveParticipants)
                {
                    aliveParticipant.Character.GetSimulationAttackPower(out var attackPoints, out var defencePoints, aliveParticipant.MatchEquipment);
                    if(attackPoints <= 0)
                    {
                        attackPoints = 1;
                    }
                    if (defencePoints <= 0)
                    {
                        defencePoints = 1;
                    }
                    dictionary.Add(aliveParticipant, new Tuple<float, float>(attackPoints, defencePoints));
                }
                int num = 0;
                while (____aliveParticipants.Count > 1 && ____aliveTeams.Count > 1)
                {
                    num++;
                    num %= ____aliveParticipants.Count;
                    TournamentParticipant tournamentParticipant2 = ____aliveParticipants[num];
                    int num2;
                    TournamentParticipant tournamentParticipant3;
                    do
                    {
                        num2 = MBRandom.RandomInt(____aliveParticipants.Count);
                        tournamentParticipant3 = ____aliveParticipants[num2];
                    }
                    while (tournamentParticipant2 == tournamentParticipant3 || tournamentParticipant2.Team == tournamentParticipant3.Team);
                    if (dictionary[tournamentParticipant3].Item2 - dictionary[tournamentParticipant2].Item1 > 0f)
                    {
                        dictionary[tournamentParticipant3] = new Tuple<float, float>(dictionary[tournamentParticipant3].Item1, dictionary[tournamentParticipant3].Item2 - dictionary[tournamentParticipant2].Item1);
                        continue;
                    }
                    dictionary.Remove(tournamentParticipant3);
                    ____aliveParticipants.Remove(tournamentParticipant3);

                    MethodInfo method = typeof(TournamentFightMissionController).GetMethod("CheckIfTeamIsDead", BindingFlags.NonPublic | BindingFlags.Instance);
                    method.DeclaringType.GetMethod("CheckIfTeamIsDead");
                    bool checkifteamdead = (bool)method.Invoke(__instance, new object[] {tournamentParticipant3.Team });

                    if (checkifteamdead)
                    {
                        ____aliveTeams.Remove(tournamentParticipant3.Team);
                        MethodInfo method2 = typeof(TournamentFightMissionController).GetMethod("AddScoreToRemainingTeams", BindingFlags.NonPublic | BindingFlags.Instance);
                        method2.DeclaringType.GetMethod("AddScoreToRemainingTeams");
                        method2.Invoke(__instance, new object[] { });
                    }
                    if (num2 < num)
                    {
                        num--;
                    }
                }
                ____isSimulated = true;
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("PrepareForMatch")]
            static void PrepareForMatchPrefix(ref TournamentFightMissionController __instance, ref TournamentMatch ____match, ref CultureObject ____culture)
            {
                float randomFloat = MBRandom.RandomFloat;
                int teamSize = ____match.Teams.First().Participants.Count();
                int playerTier = calculatePlayerTournamentTier();
                ItemObject oneHandReplacement = null;
                ItemObject twoHandReplacement = null;
                ItemObject shieldReplacement = null;

                List<ItemObject> shieldList = new List<ItemObject>();
                foreach (ItemObject item in Items.All)
                {
                    if (!item.IsCraftedByPlayer && item.Type == ItemObject.ItemTypeEnum.Shield)
                    {
                        if (item.Culture == ____culture)
                        {
                            if ((int)item.Tier == playerTier - 1)
                            {
                                shieldList.Add(item);
                            }
                        }
                    }
                }
                if (shieldList.Count > 0)
                {
                    shieldReplacement = shieldList.GetRandomElement();
                }

                List<ItemObject> oneHandedList = new List<ItemObject>();
                foreach (ItemObject item in Items.All)
                {
                    if (!item.IsCraftedByPlayer && item.Type == ItemObject.ItemTypeEnum.OneHandedWeapon)
                    {
                        if (item.Culture == ____culture)
                        {
                            if ((int)item.Tier == playerTier - 1)
                            {
                                if (playerTier > 3)
                                {
                                    if(item.PrimaryWeapon.WeaponClass == WeaponClass.OneHandedAxe || item.PrimaryWeapon.WeaponClass == WeaponClass.Mace)
                                    {
                                        oneHandedList.Add(item);
                                    }
                                }
                                else
                                {
                                    oneHandedList.Add(item);
                                }
                            }
                        }
                    }
                }
                if (oneHandedList.Count > 0)
                {
                    oneHandReplacement = oneHandedList.GetRandomElement();
                }

                List<ItemObject> twoHandedList = new List<ItemObject>();
                foreach (ItemObject item in Items.All)
                {
                    if (!item.IsCraftedByPlayer && item.Type == ItemObject.ItemTypeEnum.TwoHandedWeapon)
                    {
                        if (item.Culture == ____culture)
                        {
                            if ((int)item.Tier == playerTier - 1)
                            {
                                twoHandedList.Add(item);
                            }
                        }
                    }else if (!item.IsCraftedByPlayer && item.Type == ItemObject.ItemTypeEnum.Polearm && item.PrimaryWeapon.SwingDamage > 0)
                    {
                        if (item.Culture == ____culture)
                        {
                            if ((int)item.Tier == playerTier - 1)
                            {
                                twoHandedList.Add(item);
                            }
                        }
                    }
                }
                if (twoHandedList.Count > 0)
                {
                    twoHandReplacement = twoHandedList.GetRandomElement();
                }

                foreach (TournamentTeam team in ____match.Teams)
                {
                    foreach (TournamentParticipant participant in team.Participants)
                    {
                        for (EquipmentIndex index = EquipmentIndex.WeaponItemBeginSlot; index < EquipmentIndex.ExtraWeaponSlot; index++)
                        {
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.Bow)
                            {
                                playerTier = calculatePlayerTournamentTier();
                                switch (playerTier)
                                {
                                    case 1:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("hunting_bow")));
                                            break;
                                        }
                                    case 2:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("steppe_bow")));
                                            break;
                                        }
                                    case 3:
                                        {
                                            
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("composite_steppe_bow")));
                                            break;
                                        }
                                    case 4:
                                        {
                                            
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("steppe_war_bow")));
                                            break;
                                        }
                                    case 5:
                                    case 6:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("noble_bow")));
                                            break;
                                        }
                                }
                            }
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.Arrows)
                            {
                                playerTier = calculatePlayerTournamentTier();
                                switch (playerTier)
                                {
                                    case 1:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("range_arrows")));
                                            break;
                                        }
                                    case 2:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("barbed_arrows")));
                                            break;
                                        }
                                    case 3:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("bodkin_arrows_b")));
                                            break;
                                        }
                                    case 4:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("bodkin_arrows_a")));
                                            break;
                                        }
                                    case 5:
                                    case 6:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("piercing_arrows")));
                                            break;
                                        }
                                }
                            }
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.Crossbow)
                            {
                                playerTier = calculatePlayerTournamentTier();
                                switch (playerTier)
                                {
                                    case 1:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("crossbow_a")));
                                            break;
                                        }
                                    case 2:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("crossbow_c")));
                                            break;
                                        }
                                    case 3:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("crossbow_d")));
                                            break;
                                        }
                                    case 4:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("crossbow_f")));
                                            break;
                                        }
                                }
                            }
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.Bolts)
                            {
                                playerTier = calculatePlayerTournamentTier();
                                switch (playerTier)
                                {
                                    case 1:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("tournament_bolts")));
                                            break;
                                        }
                                    case 2:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("bolt_d")));
                                            break;
                                        }
                                    case 3:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("bolt_c")));
                                            break;
                                        }
                                    case 4:
                                        {
                                            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("bolt_a")));
                                            break;
                                        }
                                }
                            }
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.OneHandedWeapon)
                            {
                                if (oneHandReplacement != null)
                                {
                                    participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(oneHandReplacement));
                                }
                            }
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.TwoHandedWeapon)
                            {
                                if (twoHandReplacement != null)
                                {
                                    participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(twoHandReplacement));
                                }
                            }
                            if (participant.MatchEquipment[index].Item != null && participant.MatchEquipment[index].Item.Type == ItemObject.ItemTypeEnum.Shield)
                            {
                                if((teamSize == 1 || teamSize == 2) && randomFloat >= 0.5f)
                                {
                                    participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(null));
                                }
                                else
                                {
                                    if (shieldReplacement != null)
                                    {
                                        participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(index, new EquipmentElement(shieldReplacement));
                                    }
                                }
                            }
                        }
                    }
                }
                //}
            }
        }

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

            public static List<CharacterObject> FillTroopListFromCulture(CultureObject culture)
            {
                List<CharacterObject> troops = new List<CharacterObject>();
                foreach (CharacterObject character in CharacterObject.All)
                {
                    if (character.Occupation == Occupation.Soldier && character.Culture == culture && !character.HiddenInEncylopedia)
                    {
                        troops.Add(character);
                    }
                }

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
            static bool GetParticipantCharactersPrefix(ref FightTournamentGame __instance, ref List<CharacterObject> __result, Settlement settlement, bool includePlayer = true)
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
                        InformationManager.DisplayMessage(new InformationMessage("Main tournament"));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Lower tier tournament: Tier " + playerTier));

                    }
                    //CultureObject cultureMercenaryObject = Game.Current.ObjectManager.GetObject<CultureObject>("neutral");
                    CultureObject culture = Settlement.CurrentSettlement.Culture;

                    //List<CharacterObject> troops = FillTroopListUntilTier(culture.BasicTroop, playerTier);
                    //List<CharacterObject> eliteTroops = FillTroopListUntilTier(culture.EliteBasicTroop, playerTier);
                    //List<CharacterObject> mercenaryTroops = FillTroopListUntilTier(culture.BasicMercenaryTroop, playerTier);
                    List<CharacterObject> troops = FillTroopListFromCulture(culture);

                    list.Add(CharacterObject.PlayerCharacter);
                    for(int i = 0; i < __instance.MaximumParticipantCount && list.Count < __instance.MaximumParticipantCount; i++)
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
                                if(tier == 0)
                                {
                                    break;
                                }
                            } while (troopsFromTier.Count <= 0);
                            if(troopsFromTier.Count > 0)
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
            static void GetTournamentPrizePostfix(ref FightTournamentGame __instance, ref ItemObject __result, bool includePlayer, int lastRecordedLordCountForTournamentPrize)
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

        [HarmonyPatch(typeof(TournamentGame))]
        class TournamentGamePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("UpdateTournamentPrize")]
            static bool UpdateTournamentPrizePrefix(ref TournamentGame __instance, ref bool includePlayer, ref bool removeCurrentPrize)
            {
                if(__instance.Prize != null)
                {
                    if((int)__instance.Prize.Tier != calculatePlayerTournamentTier())
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
        class GivePrizeToWinnerPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GivePrizeToWinner")]
            static bool GivePrizeToWinnerPrefix(ref TournamentManager __instance,ref TournamentGame tournament, ref Hero winner, ref bool isPlayerParticipated)
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
                        foreach(ItemModifier im in itemModifiers)
                        {
                            if(im.ProductionDropScore > 0 && im.PriceMultiplier >= 1f)
                            {
                                viableEM.Add(im);
                            }
                        }
                        if(viableEM != null && viableEM.Count > 0)
                        {
                            foreach (ItemModifier im in viableEM)
                            {
                                float randomFloat = MBRandom.RandomFloat * 100f;
                                int roll = 100 - MathF.Round(randomFloat);
                                int rollNeeded = 100 - MathF.Round(im.ProductionDropScore);
                                if (roll >= rollNeeded)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("Congratulations, you successfully rolled for " + im.Name + " item modifier, rolled:" + roll + " needed: " + rollNeeded));
                                    eePrize.SetModifier(im);
                                    break;
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("You missed roll for " +im.Name+ " item modifier, rolled:" + roll + " needed: " + rollNeeded));
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

        //[HarmonyPatch(typeof(ItemRoster))]
        //class ItemRosterPatch
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("AddToCounts", new Type[] { typeof(ItemObject), typeof(int) })]
        //    static bool AddToCountsPrefix(ref ItemRoster __instance, ItemObject item, int number, ref int __result)
        //    {
        //        if ((new StackTrace()).GetFrame(2).GetMethod().Name.Contains("EndCurrentMatch"))
        //        {
        //            if (number == 0)
        //            {
        //                __result = -1;
        //                return false;
        //            }
        //            List<ItemModifier> positiveIM = new List<ItemModifier>();
        //            float imSum = 0f;
        //            if (item.WeaponComponent != null && item.WeaponComponent.ItemModifierGroup != null)
        //            {
        //                foreach (ItemModifier im in item.WeaponComponent.ItemModifierGroup.ItemModifiers)
        //                {
        //                    if (im.PriceMultiplier >= 1f)
        //                    {
        //                        positiveIM.Add(im);
        //                        imSum += im.PriceMultiplier;
        //                    }
        //                }
        //                if (positiveIM.Count > 0)
        //                {
        //                    foreach (ItemModifier value in item.WeaponComponent.ItemModifierGroup.ItemModifiers)
        //                    {
        //                        ItemModifier imTemp = positiveIM.Find((ItemModifier im) => im != null && value.ItemModifier != null && im.Name.Equals(value.ItemModifier.Name));
        //                        if (imTemp != null)
        //                        {
        //                            float randomF = MBRandom.RandomFloat;
        //                            if (randomF < (value.Probability / 100f))
        //                            {
        //                                __result = __instance.AddToCounts(new EquipmentElement(item, imTemp), number);
        //                                return false;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            else if (item.ArmorComponent != null && item.ArmorComponent.ItemModifierGroup != null)
        //            {
        //                foreach (ItemModifier im in item.ArmorComponent.ItemModifierGroup.ItemModifiers)
        //                {
        //                    if (im.PriceMultiplier >= 1f)
        //                    {
        //                        positiveIM.Add(im);
        //                        imSum += im.PriceMultiplier;
        //                    }
        //                }
        //                if (positiveIM.Count > 0)
        //                {
        //                    float randomF = MBRandom.RandomFloat;
        //                    foreach (ItemModifierProbability value in item.ArmorComponent.ItemModifierGroup.ItemModifiersWithProbability.Values)
        //                    {
        //                        ItemModifier imTemp = positiveIM.Find((ItemModifier im) => im != null && value.ItemModifier != null && im.Name.Equals(value.ItemModifier.Name));
        //                        if (imTemp != null)
        //                        {
        //                            if (randomF < (value.Probability / 100f))
        //                            {
        //                                __result = __instance.AddToCounts(new EquipmentElement(item, imTemp), number);
        //                                return false;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            __result = __instance.AddToCounts(new EquipmentElement(item), number);
        //            return false;
        //        }
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(DefaultTournamentModel))]
        class DefaultTournamentModelPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetRenownReward")]
            static bool GetRenownRewardPrefix(ref DefaultTournamentModel __instance, Hero winner, Town town, ref int __result)
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
