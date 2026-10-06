using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using RBMAI;
using StoryMode.GameComponents;
using StoryMode.Missions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.CampaignSystem.ComponentInterfaces.CombatXpModel;
using static TaleWorlds.CampaignSystem.MapEvents.MapEvent;

namespace RBMCombat
{
    internal partial class CampaignChanges
    {
        [HarmonyPatch(typeof(DefaultPartyHealingModel))]
        private class OverrideDefaultPartyHealingModel
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetSurvivalChance")]
            private static bool PrefixGetAiWeight(ref float __result, PartyBase party, CharacterObject character, DamageTypes damageType, PartyBase enemyParty = null)
            {
                if ((character.IsHero && CampaignOptions.BattleDeath == CampaignOptions.Difficulty.VeryEasy) || (character.IsPlayerCharacter && CampaignOptions.BattleDeath == CampaignOptions.Difficulty.Easy))
                {
                    __result = 1f;
                    return false;
                }
                ExplainedNumber stat = new ExplainedNumber(1f);
                if (party != null && party.MobileParty != null)
                {
                    MobileParty mobileParty = party.MobileParty;
                    SkillHelper.AddSkillBonusForParty(DefaultSkillEffects.SurgeonSurvivalBonus, mobileParty, ref stat);
                    if (enemyParty?.MobileParty != null && enemyParty.MobileParty.HasPerk(DefaultPerks.Medicine.DoctorsOath, out _))
                    {
                        SkillHelper.AddSkillBonusForParty(DefaultSkillEffects.SurgeonSurvivalBonus, enemyParty.MobileParty, ref stat);
                    }
                    stat.Add((float)character.Level * 0.02f);
                    if (!character.IsHero && party.MapEvent != null && character.Tier < 3)
                    {
                        PerkHelper.AddPerkBonusForParty(DefaultPerks.Medicine.PhysicianOfPeople, party.MobileParty, isPrimaryBonus: false, ref stat);
                    }
                    if (character.IsHero)
                    {
                        stat.Add(character.GetTotalArmorSum() * 0.01f);
                        stat.Add(character.Age * -0.01f);
                        stat.AddFactor(50f);
                        //stat.AddFactor(49f);
                        //stat.Add(stat.ResultNumber * 50f - stat.ResultNumber);
                    }
                    ExplainedNumber stat2 = new ExplainedNumber(1f / stat.ResultNumber);
                    if (character.IsHero)
                    {
                        if (party.IsMobile && party.MobileParty.HasPerk(DefaultPerks.Medicine.CheatDeath, out _, checkSecondaryRole: true))
                        {
                            stat2.AddFactor(DefaultPerks.Medicine.CheatDeath.SecondaryBonus, DefaultPerks.Medicine.CheatDeath.Name);
                        }
                        if (character.HeroObject.Clan == Clan.PlayerClan)
                        {
                            float clanMemberDeathChanceMultiplier = Campaign.Current.Models.DifficultyModel.GetClanMemberDeathChanceMultiplier();
                            if (!clanMemberDeathChanceMultiplier.ApproximatelyEqualsTo(0f))
                            {
                                stat2.AddFactor(clanMemberDeathChanceMultiplier, GameTexts.FindText("str_game_difficulty"));
                            }
                        }
                    }
                    __result = 1f - MBMath.ClampFloat(stat2.ResultNumber, 0f, 1f);
                    return false;
                }
                if (stat.ResultNumber.ApproximatelyEqualsTo(0f))
                {
                    __result = 0f;
                    return false;
                }
                __result = 1f - 1f / stat.ResultNumber;
                return false;
            }
        }
    }
}
