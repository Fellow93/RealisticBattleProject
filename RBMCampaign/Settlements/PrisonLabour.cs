using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Makes the men in a fief's cells count for something on both sides of its books.
    ///
    /// Vanilla keeps a settlement's prisoners as inert stock: they occupy a capacity number, a governor
    /// perk may put them to work on a building, and otherwise they neither eat nor earn. That is the one
    /// thing a medieval keep full of captives certainly was not. Under RBM a prisoner is a mouth and a
    /// pair of hands at once -- he is fed out of the fief's own stores (<see cref="FoodPerPrisonerPerDay"/>,
    /// a fifth of a soldier's ration: gruel, not rations) and his labour is worked off against the
    /// settlement's purse (<see cref="IncomePerPrisonerPerDay"/>) in the quarries, the ditches and the mill.
    ///
    /// Towns and castles alike, because both hold prisoners and both feed them. The construction side of
    /// the same men is separate and already in place -- see <see cref="Construction"/>, where a prisoner
    /// lifts the day's labour ceiling and part of his work costs nothing.
    /// </summary>
    public static class PrisonLabour
    {
        /// <summary>
        /// Food a prisoner eats a day. A fifth of what a soldier gets (a garrison man eats one unit per
        /// <c>NumberOfMenOnGarrisonToEatOneFood</c>, i.e. 0.25 at RBM's four): a captive is kept alive, not
        /// kept well.
        /// </summary>
        public const float FoodPerPrisonerPerDay = 0.05f;

        /// <summary>
        /// What a prisoner's day of labour is worth to the fief that holds him, into its own treasury.
        /// Sized well under a free man's wage (the tier-1 foot wage is 20) but far from nothing, so a
        /// full cell block is a genuine reason to take captives home rather than ransom them all.
        /// </summary>
        public const int IncomePerPrisonerPerDay = 30;

        /// <summary>Men in this settlement's cells, 0 for anything that keeps none.</summary>
        public static int Count(Settlement settlement)
        {
            if (settlement == null || settlement.Party == null || settlement.Party.PrisonRoster == null)
            {
                return 0;
            }
            return settlement.Party.PrisonRoster.TotalManCount;
        }

        /// <summary>The food a settlement's prisoners eat today, as a rate rather than whole units.</summary>
        public static float DailyFood(Settlement settlement)
        {
            return Count(settlement) * FoodPerPrisonerPerDay;
        }

        /// <summary>
        /// Credits the day's prison labour to the fief's treasury. Called from the daily settlement pass
        /// alongside the castle's income and the town's mint, so it lands before the day's upkeep and the
        /// wealth tax act on the balance.
        /// </summary>
        public static void OnDailyTick(Settlement settlement)
        {
            if (!RBMConfig.RBMConfig.rbmCampaignEnabled || settlement == null
                || !(settlement.IsTown || settlement.IsCastle))
            {
                return;
            }

            int prisoners = Count(settlement);
            if (prisoners <= 0)
            {
                return;
            }

            int income = prisoners * IncomePerPrisonerPerDay;
            SettlementWealth.Credit(settlement, income, SettlementWealth.Source.PrisonLabour);

            if (EconomyLog.IsEnabled)
            {
                EconomyLog.Log("PRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                    prisoners + " prisoners  ·  +" + income + "d labour  ·  eating "
                    + EconomyLog.Fmt(prisoners * FoodPerPrisonerPerDay) + " food"
                    + "  ·  treasury now " + SettlementWealth.GetSettlementWealth(settlement) + "d");
            }
        }
    }
}
