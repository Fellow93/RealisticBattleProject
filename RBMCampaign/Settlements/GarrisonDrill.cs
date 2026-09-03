using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Wealth-funded garrison drill: the fief's treasury trains the men on its walls.
    ///
    /// Vanilla's <c>DefaultPartyTrainingModel.GetEffectiveDailyExperience</c> gates its base daily XP on
    /// <c>IsLordParty</c>, so a garrison earns NOTHING a day beyond a governor's perks -- it only ever
    /// tiers up off the XP of battles fought on its own walls. That is why garrisons sit at recruit tier
    /// for years while the lord's field party climbs. RBM replaces that with a drill term keyed to the
    /// same treasury that pays the garrison's wages (<see cref="GarrisonUpkeep"/>): a fief whose wealth
    /// covers its whole daily bill for the recruit reserve (<see cref="GarrisonRecruitCost.GarrisonReserveDays"/>
    /// days) drills its men like a lord's party does; twice that cover drills them twice as fast, up to
    /// <see cref="MaxFactor"/>; a fief under the reserve drills proportionally less, and a broke one not
    /// at all. The promotion itself is still bought out of the treasury by the upgrade pass
    /// (SpoilsUpgradePatches), so a rich fief both trains AND arms its garrison, a poor one does neither.
    /// </summary>
    public static class GarrisonDrill
    {
        /// <summary>Lord-party base XP a day per man: <c>BaseXp + Tier × TierXp</c> -- vanilla's own non-leader figures.</summary>
        public const float BaseXp = 10f;
        public const float TierXp = 2f;

        /// <summary>Most a flush fief can multiply the base drill by.</summary>
        public const float MaxFactor = 2f;

        public static bool IsEnabled
        {
            get { return GarrisonRecruitCost.IsEnabled; }
        }

        /// <summary>
        /// Treasury cover of the recruit reserve, clamped to [0, <see cref="MaxFactor"/>]: 1.0 means the
        /// fief holds exactly the reserve it must keep to recruit at all.
        /// </summary>
        public static float DrillFactor(Settlement settlement)
        {
            if (settlement == null || !(settlement.IsTown || settlement.IsCastle))
            {
                return 0f;
            }
            int reserve = GarrisonRecruitCost.FullDailyBill(settlement) * GarrisonRecruitCost.GarrisonReserveDays;
            if (reserve <= 0)
            {
                return 1f;
            }
            float factor = SettlementWealth.GetSettlementWealth(settlement) / (float)reserve;
            return MBMath.ClampFloat(factor, 0f, MaxFactor);
        }

        private static Settlement FiefOf(MobileParty garrison)
        {
            Settlement settlement = garrison.CurrentSettlement ?? garrison.HomeSettlement;
            return (settlement != null && (settlement.IsTown || settlement.IsCastle)) ? settlement : null;
        }

        [HarmonyPatch(typeof(DefaultPartyTrainingModel), "GetEffectiveDailyExperience")]
        private static class DrillXpPatch
        {
            private static readonly TextObject DrillText = new TextObject("{=rbm_garrison_drill}Garrison drill");
            private static readonly TextObject TrainingFieldText = new TextObject("{=!}Training field");

            private static void Postfix(MobileParty mobileParty, TroopRosterElement troop, ref ExplainedNumber __result)
            {
                // The watch drills on the same ground the garrison does, and vanilla gives it no daily XP
                // either -- so a settlement's militia party is treated here exactly as its garrison is.
                if (!IsEnabled || mobileParty == null || (!mobileParty.IsGarrison && !mobileParty.IsMilitia)
                    || troop.Character == null || troop.Character.IsHero || mobileParty.MapEvent != null)
                {
                    return;
                }
                Settlement fief = FiefOf(mobileParty);
                if (fief == null)
                {
                    return;
                }

                // Training Fields. Vanilla's own ExperiencePerDay effect is 1/2/3 a day, which is nothing at
                // all beside a battle; at ten times that it is a real reason to build the thing. Unlike the
                // drill term this is NOT scaled by the treasury -- the ground and the sergeants are already
                // paid for, standing there whether the fief is rich this week or not.
                int trainingFields = BuildingEffects.TrainingFields(fief.Town);
                if (trainingFields > 0)
                {
                    __result.Add(trainingFields * 10f, TrainingFieldText);
                }

                float factor = DrillFactor(fief);
                if (factor <= 0f)
                {
                    return;
                }
                float xp = (BaseXp + troop.Character.Tier * TierXp) * factor;
                __result.Add(xp, DrillText);

                // One line a day per garrison, not one per stack: log off the first roster entry only.
                if (EconomyLog.IsEnabled && mobileParty.MemberRoster.Count > 0
                    && mobileParty.MemberRoster.GetCharacterAtIndex(0) == troop.Character)
                {
                    EconomyLog.Log("GARRISONDRILL", fief.Name.ToString(),
                        "drill factor " + factor.ToString("0.00")
                        + " (wealth " + SettlementWealth.GetSettlementWealth(fief)
                        + " / reserve " + GarrisonRecruitCost.FullDailyBill(fief) * GarrisonRecruitCost.GarrisonReserveDays + ")"
                        + ", " + troop.Character.Name + " +" + xp.ToString("0.0") + " xp/man");
                }
            }
        }
    }
}
