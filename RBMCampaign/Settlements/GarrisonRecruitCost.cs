using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// Makes a fief pay to arm the garrison it auto-recruits, and stops it recruiting one it cannot
    /// afford -- the household-retinue side of the settlement economy.
    ///
    /// Vanilla hands a fortification a free man off its notables' volunteers each day and bills the
    /// OWNER clan the recruitment cost (<c>AutoRecruitmentExpenses</c>, drained a fifth a day). RBM's
    /// ledger models the garrison as the settlement's own charge: the fief's treasury buys the man's
    /// kit, the owner is billed nothing, and a treasury too poor to hold a reserve simply does not
    /// recruit that day. That, with the wage bill (<see cref="GarrisonUpkeep"/>) and the affordability
    /// this adds, is what now sizes an AI garrison -- a rich fief fields a deep one, a broke frontier
    /// castle a thin one.
    ///
    /// The gear is priced, not drawn from a market: the spec has a garrison paid for out of castle/city
    /// WEALTH, unlike a militiaman or a recruit who is armed off the stalls. In a town the coin reaches
    /// the armourers (citizen wealth); a castle, having no market, sources the kit from outside and the
    /// coin leaves the ledger.
    /// </summary>
    public static class GarrisonRecruitCost
    {
        /// <summary>Days of the garrison's wage a fief must hold to keep recruiting at all.</summary>
        public const int GarrisonReserveDays = 30;

        /// <summary>Times a man's equipment cost the treasury must hold to arm him.</summary>
        public const int GarrisonSpawnReserveMult = 10;

        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.rbmCampaignEnabled; }
        }

        /// <summary>The fief's own treasury -- Pot B for a castle or a town alike.</summary>
        private static int FiefWealth(Settlement settlement)
        {
            return SettlementWealth.GetSettlementWealth(settlement);
        }

        /// <summary>What the garrison's wages come to a day, read off its own party like the militia's.</summary>
        private static int DailyGarrisonWageBill(Settlement settlement)
        {
            MobileParty garrison = settlement.Town != null ? settlement.Town.GarrisonParty : null;
            return (garrison != null && garrison.IsActive) ? garrison.TotalWage : 0;
        }

        /// <summary>
        /// Gates and prices a fief's daily garrison auto-recruitment. The prefix turns the day's
        /// recruiting off when the treasury cannot hold a month of the garrison's wages; the postfix
        /// takes the owner off the hook, charges the fief for the men actually raised, and turns away a
        /// man it cannot afford or -- if he is mounted and the market has no horse -- cannot mount.
        /// </summary>
        [HarmonyPatch(typeof(GarrisonRecruitmentCampaignBehavior), "TickAutoRecruitmentGarrisonChange")]
        private static class GarrisonRecruitCostPatch
        {
            private static bool Prefix(Town town, out GarrisonRecruitState __state)
            {
                __state = null;
                if (!IsEnabled || town == null || town.Settlement == null)
                {
                    return true;
                }

                Settlement settlement = town.Settlement;
                int wealth = FiefWealth(settlement);
                int reserve = DailyGarrisonWageBill(settlement) * GarrisonReserveDays;
                if (wealth < reserve)
                {
                    // Too poor to keep the garrison it has, let alone add to it. No recruiting today, and
                    // no owner charge either -- the man is simply never raised.
                    return false;
                }

                __state = new GarrisonRecruitState
                {
                    PreExpenses = (settlement.OwnerClan != null) ? settlement.OwnerClan.AutoRecruitmentExpenses : 0,
                    Before = Snapshot(town.GarrisonParty)
                };
                return true;
            }

            private static void Postfix(Town town, GarrisonRecruitState __state)
            {
                if (__state == null || town == null || town.Settlement == null)
                {
                    return;
                }
                Settlement settlement = town.Settlement;

                // Take the owner off the hook: the fief, not the lord, pays for its own garrison's kit,
                // so undo vanilla's accrual against his gold.
                if (settlement.OwnerClan != null)
                {
                    settlement.OwnerClan.AutoRecruitmentExpenses = __state.PreExpenses;
                }

                MobileParty garrison = town.GarrisonParty;
                if (garrison == null || garrison.MemberRoster == null)
                {
                    return;
                }

                foreach (KeyValuePair<CharacterObject, int> added in Diff(__state.Before, garrison))
                {
                    CharacterObject troop = added.Key;
                    int count = added.Value;
                    int cost = SpoilsPool.GetEquipmentValue(troop) * count;

                    // Enough in hand to arm him with a reserve to spare, or he stays home.
                    if (cost > 0 && FiefWealth(settlement) < cost * GarrisonSpawnReserveMult)
                    {
                        garrison.MemberRoster.AddToCounts(troop, -count);
                        continue;
                    }

                    if (cost <= 0)
                    {
                        continue;
                    }

                    int paid = SettlementWealth.Debit(settlement, cost, SettlementWealth.Source.GarrisonRecruit);
                    // In a town the coin reaches the armourers who kitted him; a castle sources the gear
                    // from outside its walls and the coin leaves the ledger.
                    if (paid > 0 && settlement.IsTown)
                    {
                        SettlementWealth.CreditCitizens(settlement, paid, SettlementWealth.Source.GarrisonRecruit);
                    }

                    if (EconomyLog.IsEnabled && paid > 0)
                    {
                        EconomyLog.Log("GARRISON", settlement.Name != null ? settlement.Name.ToString() : settlement.StringId,
                            "armed " + count + "x " + (troop.Name != null ? troop.Name.ToString() : troop.StringId)
                            + " for " + paid + "/" + cost + "d  ·  treasury now " + FiefWealth(settlement) + "d");
                    }
                }
            }
        }

        /// <summary>A garrison roster's non-hero head counts, by troop, before the day's recruiting.</summary>
        private static Dictionary<CharacterObject, int> Snapshot(MobileParty garrison)
        {
            Dictionary<CharacterObject, int> counts = new Dictionary<CharacterObject, int>();
            TroopRoster roster = (garrison != null) ? garrison.MemberRoster : null;
            if (roster == null)
            {
                return counts;
            }
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero)
                {
                    continue;
                }
                counts[element.Character] = element.Number;
            }
            return counts;
        }

        /// <summary>Troops the garrison gained since the snapshot, by how many -- the day's recruits.</summary>
        private static IEnumerable<KeyValuePair<CharacterObject, int>> Diff(Dictionary<CharacterObject, int> before, MobileParty garrison)
        {
            List<KeyValuePair<CharacterObject, int>> gained = new List<KeyValuePair<CharacterObject, int>>();
            TroopRoster roster = garrison.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero)
                {
                    continue;
                }
                int prior;
                before.TryGetValue(element.Character, out prior);
                int delta = element.Number - prior;
                if (delta > 0)
                {
                    gained.Add(new KeyValuePair<CharacterObject, int>(element.Character, delta));
                }
            }
            return gained;
        }
    }

    /// <summary>Carried from the auto-recruit prefix to its postfix: the owner's pre-call accrual and
    /// the garrison roster as it stood, so the day's recruits can be told apart and priced.</summary>
    public class GarrisonRecruitState
    {
        public int PreExpenses;
        public Dictionary<CharacterObject, int> Before;
    }
}
