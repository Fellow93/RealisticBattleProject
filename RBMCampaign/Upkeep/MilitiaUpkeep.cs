using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A settlement keeps its own militia, and now it pays for them. Each day the militia's wages --
    /// priced off their kit like any other troop -- are drawn out of the place they defend: a town or
    /// castle's Prosperity, a village's Hearth, at the same settlementProsperityPerGoldSpent rate that
    /// trade and carousing pour back in. A town that raises more militia than its economy can carry
    /// bleeds for it, and since militia grows with prosperity the drain is self-limiting.
    /// </summary>
    public static class MilitiaUpkeep
    {
        public static void OnDailyTickSettlement(Settlement settlement)
        {
            float rate = RBMConfig.RBMConfig.settlementProsperityPerGoldSpent;
            if (rate <= 0f || settlement == null)
            {
                return;
            }
            // Only places that actually hold prosperity or hearth -- hideouts and the like keep none.
            if (settlement.Town == null && settlement.Village == null)
            {
                return;
            }

            // Militia are part-time defenders, so the place pays only a fraction of their full wage.
            float wageGold = MilitiaWageGold(settlement) * RBMConfig.RBMConfig.militiaWageModifier;
            float drain = wageGold * rate;
            if (drain <= 0f)
            {
                return;
            }

            if (settlement.Town != null)
            {
                settlement.Town.Prosperity = MathF.Max(0f, settlement.Town.Prosperity - drain);
            }
            else
            {
                settlement.Village.Hearth = MathF.Max(0f, settlement.Village.Hearth - drain);
            }

            if (SpoilsLog.IsEnabled)
            {
                // One line per settlement per day -- the daily tick is throttle enough on its own.
                SpoilsLog.Log("MILITIA", settlement.Name + ": " + (int)settlement.Militia + " militia, "
                    + (int)wageGold + " gold in wages -> -" + drain.ToString("0.00")
                    + (settlement.Town != null ? " prosperity" : " hearth"));
            }
        }

        /// <summary>
        /// The gear-based wage of the settlement's standing militia. The gathered militia party, when
        /// one exists, carries the true composition -- elites and all -- so it is priced man by man off
        /// its own roster. Below the threshold where a party forms, the loose head count is priced off
        /// the culture's rank-and-file militia instead.
        /// </summary>
        private static float MilitiaWageGold(Settlement settlement)
        {
            MobileParty militiaParty = settlement.MilitiaPartyComponent?.MobileParty;
            if (militiaParty != null && militiaParty.IsActive && militiaParty.Party.NumberOfAllMembers > 0)
            {
                return RosterWageGold(militiaParty.MemberRoster);
            }
            return AverageMilitiaManWage(settlement.Culture) * settlement.Militia;
        }

        private static float RosterWageGold(TroopRoster roster)
        {
            float sum = 0f;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                if (element.Character == null || element.Character.IsHero)
                {
                    continue;
                }
                // TroopWage routes non-heroes through the gear-based wage model.
                sum += (float)element.Character.TroopWage * element.Number;
            }
            return sum;
        }

        private static float AverageMilitiaManWage(CultureObject culture)
        {
            if (culture == null)
            {
                return 0f;
            }
            float sum = 0f;
            int n = 0;
            AddWage(culture.MeleeMilitiaTroop, ref sum, ref n);
            AddWage(culture.RangedMilitiaTroop, ref sum, ref n);
            return n > 0 ? sum / n : 0f;
        }

        private static void AddWage(CharacterObject troop, ref float sum, ref int n)
        {
            if (troop != null)
            {
                sum += troop.TroopWage;
                n++;
            }
        }
    }
}
