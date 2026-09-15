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
        [HarmonyPatch(typeof(TournamentFightMissionController))]
        private class TournamentFightMissionControllerPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("Simulate")]
            private static bool SimulatePrefix(ref TournamentFightMissionController __instance, ref TournamentMatch ____match, ref CultureObject ____culture, ref bool ____isSimulated,
                ref List<TournamentParticipant> ____aliveParticipants, ref List<TournamentTeam> ____aliveTeams)
            {
                ____isSimulated = false;
                if (__instance.Mission.Agents.Count == 0)
                {
                    ____aliveParticipants = ____match.Participants.ToList();
                    ____aliveTeams = ____match.Teams.ToList();
                }
                if (____aliveParticipants == null || ____aliveTeams == null)
                {
                    return true;
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
                    if (attackPoints <= 0)
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
                    bool checkifteamdead = (bool)method.Invoke(__instance, new object[] { tournamentParticipant3.Team });

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
            private static void PrepareForMatchPrefix(ref TournamentFightMissionController __instance, ref TournamentMatch ____match, ref CultureObject ____culture)
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
                                    if (item.PrimaryWeapon.WeaponClass == WeaponClass.OneHandedAxe || item.PrimaryWeapon.WeaponClass == WeaponClass.Mace)
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
                    }
                    else if (!item.IsCraftedByPlayer && item.Type == ItemObject.ItemTypeEnum.Polearm && item.PrimaryWeapon.SwingDamage > 0)
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
                                if ((teamSize == 1 || teamSize == 2) && randomFloat >= 0.5f)
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
    }
}
