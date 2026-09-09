using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// What an empty granary does to a fief and the men inside it.
    ///
    /// A fief's food is read as DAYS OF SUPPLY -- its granary divided by what it eats in a day (see
    /// <see cref="RBMTownFoodSupply.GetFoodConsumption"/>) -- and that figure drives three tiers:
    ///
    /// <list type="bullet">
    /// <item>UNDER SEVEN DAYS: prosperity stops growing and the garrison stops recruiting. The fief is
    /// rationing; nothing new is added to the mouths it has to feed.</item>
    /// <item>UNDER THREE DAYS: prosperity falls <see cref="CriticalProsperityLoss"/> a day on top.</item>
    /// <item>STARVING (rations went unmet today, or the granary reads empty): prosperity falls
    /// <see cref="StarvingProsperityLoss"/> a day, and the garrison and militia sicken -- a tenth of the
    /// healthy men are wounded each day and a tenth of the wounded die.</item>
    /// </list>
    ///
    /// The last replaces vanilla's own starving-garrison rule (<c>DefaultPartyHealingModel</c> wounds a
    /// tenth of the garrison through negative healing, kills nobody, and leaves the militia alone), so
    /// that rule is zeroed for a starving fief's garrison and militia in <see cref="StarvingHealingPatch"/>
    /// rather than stacked. Field parties keep vanilla's wounding (a quarter of the regulars a day) and
    /// gain only the deaths: a tenth of their wounded a day while out of food.
    /// </summary>
    internal static class FiefStarvation
    {
        public enum Tier
        {
            Fed,
            /// <summary>Under <see cref="GrowthGateDays"/> of food: no prosperity or garrison growth.</summary>
            Rationing,
            /// <summary>Under <see cref="CriticalDays"/> of food: prosperity falls.</summary>
            Critical,
            /// <summary>Rations unmet or granary empty: prosperity collapses, troops sicken.</summary>
            Starving,
        }

        private const float GrowthGateDays = 7f;
        private const float CriticalDays = 3f;

        /// <summary>Prosperity lost per day, as a share, under <see cref="CriticalDays"/> of food.</summary>
        private const float CriticalProsperityLoss = 0.01f;

        /// <summary>Prosperity lost per day, as a share, while starving.</summary>
        private const float StarvingProsperityLoss = 0.03f;

        /// <summary>Share of a starving fief's healthy garrison and militia wounded each day.</summary>
        private const float DailyWoundShare = 0.1f;

        /// <summary>Share of the wounded who die each day while starving, in a fief or a field party.</summary>
        private const float DailyDeathShare = 0.1f;

        /// <summary>
        /// Days the fief's granary would feed it at today's appetite, or -1 when it is starving. Reads the
        /// stock over consumption rather than over the day's net change, so a town that is eating its way
        /// through a full granary reads as the reserve it actually has, not as the deficit of the moment.
        /// </summary>
        public static float DaysOfFood(Town town)
        {
            if (town == null || !(town.IsTown || town.IsCastle))
            {
                return float.MaxValue;
            }
            if (IsStarving(town))
            {
                return -1f;
            }
            int daily = RBMTownFoodSupply.GetFoodConsumption(town).Total;
            return (daily > 0) ? town.FoodStocks / daily : float.MaxValue;
        }

        /// <summary>
        /// Whether the fief failed to feed itself today. A town's signal is the ration shortfall its food
        /// pass recorded (a town with ten grain and seven hundred mouths went hungry before its market
        /// emptied); a castle has no market pass, so its signal is vanilla's empty granary.
        /// </summary>
        public static bool IsStarving(Town town)
        {
            if (town == null || town.Settlement == null)
            {
                return false;
            }
            if (town.IsTown && RBMTownFoodSupply.UnmetRationsToday(town) > 0)
            {
                return true;
            }
            return town.Settlement.IsStarving;
        }

        public static Tier GetTier(Town town)
        {
            float days = DaysOfFood(town);
            if (days < 0f)
            {
                return Tier.Starving;
            }
            if (days < CriticalDays)
            {
                return Tier.Critical;
            }
            if (days < GrowthGateDays)
            {
                return Tier.Rationing;
            }
            return Tier.Fed;
        }

        /// <summary>True when the fief is too short of food to add prosperity or garrison men.</summary>
        public static bool BlocksGrowth(Town town)
        {
            return town != null && RBMConfig.RBMConfig.rbmCampaignEnabled && GetTier(town) != Tier.Fed;
        }

        /// <summary>The share of its prosperity the fief loses today to hunger; 0 when it has food to spare.</summary>
        public static float ProsperityLossRate(Town town)
        {
            if (town == null || !RBMConfig.RBMConfig.rbmCampaignEnabled)
            {
                return 0f;
            }
            switch (GetTier(town))
            {
                case Tier.Starving: return StarvingProsperityLoss;
                case Tier.Critical: return CriticalProsperityLoss;
                default: return 0f;
            }
        }

        /// <summary>
        /// The daily toll on a starving fief's defenders. Called from the settlement's daily pass, before
        /// the garrison is grown, so the day's losses are on the books when the growth gate is read.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null || settlement.Town == null
                || !(settlement.IsTown || settlement.IsCastle) || !IsStarving(settlement.Town))
            {
                return;
            }

            int woundedGarrison = 0, deadGarrison = 0, woundedMilitia = 0, deadMilitia = 0;
            MobileParty garrison = settlement.Town.GarrisonParty;
            if (garrison != null && garrison.IsActive)
            {
                StarveRoster(garrison.MemberRoster, out woundedGarrison, out deadGarrison);
            }
            MobileParty militia = settlement.MilitiaPartyComponent?.MobileParty;
            if (militia != null && militia.IsActive)
            {
                StarveRoster(militia.MemberRoster, out woundedMilitia, out deadMilitia);
            }

            if (EconomyLog.IsEnabled && (woundedGarrison + deadGarrison + woundedMilitia + deadMilitia) > 0)
            {
                EconomyLog.Log("STARVE", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    "garrison " + woundedGarrison + " wounded, " + deadGarrison + " dead"
                    + "  ·  militia " + woundedMilitia + " wounded, " + deadMilitia + " dead"
                    + "  ·  stock " + EconomyLog.Fmt(settlement.Town.FoodStocks)
                    + (settlement.IsUnderSiege ? "  ·  UNDER SIEGE" : ""));
            }
        }

        /// <summary>
        /// A field party out of food buries a tenth of its wounded each day. Vanilla already wounds a
        /// quarter of its regulars a day (<c>DefaultPartyHealingModel</c>); this adds the deaths. Garrisons
        /// and militias are handled by their fief in <see cref="OnDailyTick"/>.
        /// </summary>
        public static void OnDailyTickParty(MobileParty party)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || party == null || !party.IsActive
                || party.IsGarrison || party.IsMilitia || party.MapEvent != null || !party.Party.IsStarving)
            {
                return;
            }

            int dead = KillWounded(party.MemberRoster);
            if (dead > 0 && party.IsMainParty)
            {
                MBInformationManager.AddQuickInformation(new TaleWorlds.Localization.TextObject(
                    "{=RBM_STARVE_001}" + dead + " of your wounded have died of hunger."));
            }
        }

        /// <summary>
        /// Wounds a tenth of the healthy regulars and kills a tenth of the wounded, per stack, rounding
        /// randomly so a small stack still takes its share over a week. Heroes are untouched.
        /// </summary>
        private static void StarveRoster(TroopRoster roster, out int wounded, out int dead)
        {
            wounded = 0;
            dead = 0;
            if (roster == null)
            {
                return;
            }
            // Backwards: a stack that dies out is removed from the roster, which shifts later indices.
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero || element.Number <= 0)
                {
                    continue;
                }
                int healthy = element.Number - element.WoundedNumber;
                int toWound = MBRandom.RoundRandomized(healthy * DailyWoundShare);
                int toKill = MBRandom.RoundRandomized(element.WoundedNumber * DailyDeathShare);
                toWound = MathF.Min(toWound, healthy);
                toKill = MathF.Min(toKill, element.WoundedNumber);
                if (toWound == 0 && toKill == 0)
                {
                    continue;
                }
                // The dead come off the count and off the wounded tally in the same call, so the wounded
                // never exceed the living: (w + toWound - toKill) <= (n - toKill) because toWound <= n - w.
                roster.AddToCountsAtIndex(i, -toKill, toWound - toKill);
                wounded += toWound;
                dead += toKill;
            }
        }

        private static int KillWounded(TroopRoster roster)
        {
            int dead = 0;
            if (roster == null)
            {
                return 0;
            }
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero || element.WoundedNumber <= 0)
                {
                    continue;
                }
                int toKill = MathF.Min(MBRandom.RoundRandomized(element.WoundedNumber * DailyDeathShare), element.WoundedNumber);
                if (toKill <= 0)
                {
                    continue;
                }
                roster.AddToCountsAtIndex(i, -toKill, -toKill);
                dead += toKill;
            }
            return dead;
        }

        /// <summary>
        /// A starving fief's garrison and militia neither heal nor take vanilla's starvation wounds: the
        /// toll is <see cref="OnDailyTick"/>'s alone. Vanilla wounds a tenth of the garrison a day through
        /// a negative healing rate and still heals the militia at the normal rate; zeroing both keeps the
        /// rule to the one written above rather than that plus this.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyHealingModel), nameof(DefaultPartyHealingModel.GetDailyHealingForRegulars))]
        private static class StarvingHealingPatch
        {
            private static void Postfix(PartyBase party, bool isPrisoners, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || isPrisoners || party == null || !party.IsMobile)
                {
                    return;
                }
                MobileParty mobileParty = party.MobileParty;
                if (mobileParty == null || (!mobileParty.IsGarrison && !mobileParty.IsMilitia))
                {
                    return;
                }
                Town town = mobileParty.CurrentSettlement?.Town ?? mobileParty.HomeSettlement?.Town;
                if (town != null && IsStarving(town))
                {
                    __result = new ExplainedNumber(0f, includeDescriptions);
                }
            }
        }
    }
}
