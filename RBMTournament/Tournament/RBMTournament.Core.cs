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

            int playerTier = playerLevelTier > playerArmorTier ? playerLevelTier : playerArmorTier;
            playerTier = MBMath.ClampInt(playerTier, 1, 6);
            return playerTier;
        }
    }
}
