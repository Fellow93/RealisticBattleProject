using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    /// <summary>
    /// Perk applications that vanilla performs inside SandboxStrikeMagnitudeModel
    /// (CalculateStrikeMagnitudeForSwing / ForThrust / ForMissile). RBM's magnitude
    /// patches bypass that model and call CombatStatCalculator or its own physics
    /// directly, so these are re-applied here at the same points vanilla would.
    /// Mirrors decompiled/SandBox/SandBox.GameComponents/SandboxStrikeMagnitudeModel.cs.
    /// </summary>
    public static partial class MagnitudeChanges
    {
        /// <summary>
        /// Vanilla folds movement-speed perks (RecklessCharge, DashAndSlash, SurgingBlow,
        /// NomadicTraditions, Lancer, UnstoppableForce) into the extra linear speed that
        /// feeds a swing or thrust. Returns the perk-adjusted extra linear speed.
        /// </summary>
        public static float ApplyMeleeSpeedPerks(in AttackInformation attackInformation, WeaponComponentData usage, float extraLinearSpeed)
        {
            CharacterObject character = attackInformation.AttackerAgentCharacter as CharacterObject;
            if (character == null || usage == null || extraLinearSpeed <= 0f)
            {
                return extraLinearSpeed;
            }
            CharacterObject captain = attackInformation.AttackerCaptainCharacter as CharacterObject;
            BattleEnvironment env = attackInformation.AttackerBattleEnvironment;
            bool mounted = attackInformation.DoesAttackerHaveMountAgent;
            SkillObject relevantSkill = usage.RelevantSkill;

            ExplainedNumber bonuses = new ExplainedNumber(extraLinearSpeed);
            if (mounted)
            {
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Riding.NomadicTraditions, env, captain, ref bonuses);
            }
            else
            {
                if (relevantSkill == DefaultSkills.TwoHanded)
                {
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.TwoHanded.RecklessCharge, env, character, true, ref bonuses);
                }
                PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Roguery.DashAndSlash, env, character, true, ref bonuses);
                PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Athletics.SurgingBlow, env, character, true, ref bonuses);
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Athletics.SurgingBlow, env, captain, ref bonuses);
            }
            if (relevantSkill == DefaultSkills.Polearm)
            {
                PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Polearm.Lancer, env, captain, ref bonuses);
                if (mounted)
                {
                    PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Polearm.Lancer, env, character, true, ref bonuses);
                    PerkHelper.AddPerkBonusFromCaptain(DefaultPerks.Polearm.UnstoppableForce, env, captain, ref bonuses);
                }
            }
            return bonuses.ResultNumber;
        }

        /// <summary>
        /// Crafting.SharpenedEdge (swing) / Crafting.SharpenedTip (thrust) on player-crafted weapons.
        /// </summary>
        public static float ApplyCraftedWeaponPerk(in AttackInformation attackInformation, ItemObject item, float magnitude, bool isThrust)
        {
            CharacterObject character = attackInformation.AttackerAgentCharacter as CharacterObject;
            if (character == null || item == null || !item.IsCraftedByPlayer)
            {
                return magnitude;
            }
            ExplainedNumber bonuses = new ExplainedNumber(magnitude);
            PerkObject perk = isThrust ? DefaultPerks.Crafting.SharpenedTip : DefaultPerks.Crafting.SharpenedEdge;
            PerkHelper.AddPerkBonusForCharacter(perk, attackInformation.AttackerBattleEnvironment, character, true, ref bonuses);
            return bonuses.ResultNumber;
        }

        /// <summary>
        /// Throwing.RunningThrow: for heroes throwing sling/stone/axe/knife/javelin ammo, the
        /// speed gained over the weapon's base launch speed is scaled up by the perk bonus.
        /// Returns the perk-adjusted missile speed.
        /// </summary>
        public static float ApplyRunningThrowPerk(in AttackInformation attackInformation, WeaponComponentData usage, float missileSpeed, float missileStartingBaseSpeed)
        {
            float excess = missileSpeed - missileStartingBaseSpeed;
            if (excess <= 0f || usage == null)
            {
                return missileSpeed;
            }
            CharacterObject character = attackInformation.AttackerAgentCharacter as CharacterObject;
            if (character == null || !character.IsHero)
            {
                return missileSpeed;
            }
            WeaponClass ammoClass = usage.AmmoClass;
            if (ammoClass != WeaponClass.Sling && ammoClass != WeaponClass.Stone && ammoClass != WeaponClass.ThrowingAxe &&
                ammoClass != WeaponClass.ThrowingKnife && ammoClass != WeaponClass.Javelin)
            {
                return missileSpeed;
            }
            ExplainedNumber bonuses = new ExplainedNumber(0f, false, null);
            PerkHelper.AddPerkBonusForCharacter(DefaultPerks.Throwing.RunningThrow, attackInformation.AttackerBattleEnvironment, character, true, ref bonuses);
            return missileSpeed + excess * bonuses.ResultNumber;
        }
    }
}
