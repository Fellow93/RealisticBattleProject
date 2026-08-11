using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    public static class SpoilsUpgradePatches
    {
        private struct UpgradeOption
        {
            public CharacterObject Target;
            public CharacterObject UpgradeTarget;
            public int Count;
            /// <summary>The whole batch, not one man: spoils make the leading men free.</summary>
            public int TotalGoldCost;
            public int XpCost;
            public int StackSize;
            public float Chance;
        }

        /// <summary>
        /// Reimplements PartyUpgraderCampaignBehavior.UpgradeReadyTroops so AI upgrades draw down
        /// the spoils they were discounted for. Gold affordability, already checked here against the
        /// spoils-discounted price, is what actually limits the AI. The vanilla helpers cannot be
        /// patched directly: they are private and pass a private nested struct, which no patch
        /// signature can name.
        /// </summary>
        [HarmonyPatch(typeof(PartyUpgraderCampaignBehavior))]
        [HarmonyPatch("UpgradeReadyTroops")]
        private class OverrideUpgradeReadyTroops
        {
            // SupplyTown gate: the town outfitting the party currently being processed. Resolved once at
            // the top of each Prefix pass and read back in ApplyEffects; the campaign tick is
            // single-threaded so this scratch field is safe.
            private static Town _supplyTown;

            // Whether this party may buy the GOLD leg of its upgrades today: false when the SupplyTown gate
            // is on and no town supplies the party. Spoils-covered men -- who re-arm from the loot in their
            // own purse, off no town's shelves -- promote regardless, so the gate closes only the gold leg.
            // Resolved with _supplyTown at the top of each pass; single-threaded tick, so safe as scratch.
            private static bool _supplyGoldAllowed;

            // The gold this party may still spend on upgrades today, resolved once at the top of each Prefix
            // pass and drawn down in ApplyEffects as each stack is billed. int.MaxValue means the party's cap
            // is unlimited (the default), in which case nothing below changes behaviour. Single-threaded tick,
            // so this scratch field is as safe as _supplyTown beside it.
            private static int _upgradeGoldBudgetRemaining;

            // Set within a pass when the daily budget -- not the purse -- was what trimmed an upgrade batch,
            // so one line can be logged per party per pass rather than per stack. Reset with the budget above.
            private static bool _budgetBitThisPass;

            private static bool Prefix(PartyBase party)
            {
                // Vanilla dereferences party.MobileParty unguarded further down, so anything that
                // survives it today must be a mobile party. Leave the rest to vanilla.
                if (!SpoilsPool.IsEnabled || party.MobileParty == null)
                {
                    return true;
                }
                if (party == PartyBase.MainParty || !party.IsActive)
                {
                    return false;
                }

                // SupplyTown gate: resolve the town that will outfit this party's GOLD-bought upgrades. When
                // the feature is on and nothing supplies the party -- neither a friendly settlement it is
                // stationed in nor a friendly city within reach -- the gold leg is closed for the day, but the
                // party is NOT skipped: men the spoils stockpile makes free still promote, re-arming from the
                // loot in their own purse rather than off a town's shelves, so distance never gates them. Only
                // gold-bought promotions need a supplier. One call does the gate and the town.
                _supplyTown = null;
                _supplyGoldAllowed = true;
                if (UpgradeSupply.IsEnabled && !UpgradeSupply.CanUpgradeNear(party.MobileParty, out _supplyTown))
                {
                    _supplyGoldAllowed = false;
                    // Once per party per day, and only when it actually has troops that could promote, so the
                    // reason an AI army's GOLD upgrades are stalled is visible without flooding the log.
                    if (SpoilsLog.IsEnabled && HasUpgradeableTroop(party.MemberRoster))
                    {
                        SpoilsLog.LogOnce("nosupply-" + party.Id + "-" + (int)(CampaignTime.Now.ToHours / 24),
                            "UPGRADE", party, SpoilsLog.Describe(party) + " held off gold upgrades: no friendly town within "
                            + RBMConfig.RBMConfig.troopUpgradeSupplyRadius + " units (spoils-covered promotions still allowed)");
                    }
                }

                // How much gold this party is still allowed to spend on upgrades today. Drawn down per
                // stack in ApplyEffects; int.MaxValue when the player has set no cap, which leaves the
                // affordability maths below untouched.
                _upgradeGoldBudgetRemaining = PartyUpgradeBudget.GetRemainingDailyBudget(party);
                _budgetBitThisPass = false;

                TroopRoster memberRoster = party.MemberRoster;
                PartyTroopUpgradeModel upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (int i = 0; i < memberRoster.Count; i++)
                {
                    TroopRosterElement element = memberRoster.GetElementCopyAtIndex(i);
                    if (!upgradeModel.IsTroopUpgradeable(party, element.Character))
                    {
                        continue;
                    }
                    List<UpgradeOption> options = GetPossibleUpgradeTargets(party, element);
                    if (options.Count > 0)
                    {
                        UpgradeTroop(party, i, SelectPossibleUpgrade(options));
                    }
                }

                // One line per party per day when the daily upgrade budget actually held the party back:
                // it had men it could otherwise have paid to promote, but its cap was spent. Keyed per party
                // per day so the daily tick and any post-battle passes the same day collapse to a single line.
                if (_budgetBitThisPass && SpoilsLog.IsEnabled)
                {
                    SpoilsLog.LogOnce("upgcap-" + party.Id + "-" + (int)(CampaignTime.Now.ToHours / 24),
                        "UPGRADE", party, SpoilsLog.Describe(party) + " held back upgrades: its daily upgrade budget of "
                        + PartyUpgradeBudget.GetFiniteCap(party) + " gold is spent (" + party.MobileParty.PartyTradeGold
                        + " gold still in purse)");
                }
                return false;
            }

            // A cheap upper bound on "would this party like to upgrade": any non-hero stack that has an
            // upgrade path. Skips the XP/gold checks -- it only gates a once-a-day denial log, so a party
            // of green troops it slightly over-reports is fine, and a party of top-tier men is skipped.
            private static bool HasUpgradeableTroop(TroopRoster roster)
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    CharacterObject character = roster.GetElementCopyAtIndex(i).Character;
                    if (character != null && !character.IsHero && character.UpgradeTargets != null && character.UpgradeTargets.Length > 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static List<UpgradeOption> GetPossibleUpgradeTargets(PartyBase party, TroopRosterElement element)
            {
                PartyWageModel wageModel = Campaign.Current.Models.PartyWageModel;
                PartyTroopUpgradeModel upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                List<UpgradeOption> options = new List<UpgradeOption>();
                CharacterObject character = element.Character;
                if (element.Number - element.WoundedNumber <= 0)
                {
                    return options;
                }

                for (int i = 0; i < character.UpgradeTargets.Length; i++)
                {
                    int count = element.Number - element.WoundedNumber;
                    CharacterObject upgradeTarget = character.UpgradeTargets[i];

                    int xpCost = character.GetUpgradeXpCost(party, i);
                    if (xpCost > 0)
                    {
                        count = MathF.Min(count, element.Xp / xpCost);
                        if (count == 0)
                        {
                            continue;
                        }
                    }

                    // Vanilla divides by the wage delta unguarded -- safe there because its wages are a
                    // function of tier, so a higher-tier target always costs strictly more. RBM prices wages
                    // off equipment value, where a tier-up can be wage-neutral (or cheaper), so the delta can
                    // be 0 and the division must be gated. A non-positive delta needs no clamp at all: the
                    // upgrade cannot push the party further past its payment limit.
                    int wageDelta = wageModel.GetCharacterWage(upgradeTarget) - wageModel.GetCharacterWage(character);
                    if (wageDelta > 0 && upgradeTarget.Tier > character.Tier && party.MobileParty.HasLimitedWage() && party.MobileParty.TotalWage + count * wageDelta > party.MobileParty.PaymentLimit)
                    {
                        count = MathF.Max(0, MathF.Min(count, (party.MobileParty.PaymentLimit - party.MobileParty.TotalWage) / wageDelta));
                        if (count == 0)
                        {
                            continue;
                        }
                    }

                    // Vanilla clamps by perManPrice * count. Spoils make the first men free, so the
                    // batch price is fullPrice * (count - coveredMen) and the party can afford
                    // coveredMen + gold/fullPrice of them.
                    int fullGold = RBMCampaignPatches.GetFullUpgradeGoldCost(party, character, upgradeTarget);
                    if (party.LeaderHero != null && fullGold > 0)
                    {
                        float coveredMen = SpoilsPool.GetCoveredMen(party, character, upgradeTarget);
                        int affordable;
                        if (_supplyGoldAllowed)
                        {
                            // The gold leg is limited by the smaller of the purse and the party's remaining daily
                            // upgrade budget: the player's clan-screen cap on gold spent, spoils-covered men aside.
                            // With no cap the remaining budget is int.MaxValue, so this is just PartyTradeGold.
                            int tradeGold = party.MobileParty.PartyTradeGold;
                            float goldPool = MathF.Min(tradeGold, _upgradeGoldBudgetRemaining);
                            affordable = (int)(coveredMen + goldPool / (float)fullGold);
                            // The budget bit here iff a finite cap left less room than the purse held AND that
                            // trimmed the batch -- i.e. the party had the gold to promote more men but its cap
                            // stopped it. Flagged for a single per-party line after the pass, not logged per stack.
                            if (_upgradeGoldBudgetRemaining != int.MaxValue && _upgradeGoldBudgetRemaining < tradeGold && affordable < count)
                            {
                                _budgetBitThisPass = true;
                            }
                        }
                        else
                        {
                            // No supply town: the gold leg is closed. Only the men the spoils stockpile makes
                            // free may promote -- they re-arm from their own purse, off no town's shelves, so
                            // the location gate does not touch them. The gold-buyers wait for a town.
                            affordable = (int)coveredMen;
                        }
                        count = MathF.Min(count, affordable);
                        if (count <= 0)
                        {
                            continue;
                        }
                    }
                    // A garrison has neither an owner to bill nor -- now it is out of the spoils economy --
                    // a purse of its own: its promotions come out of the fief's treasury. That is a GOLD
                    // leg, so it is gated by supply exactly as a lord's gold-buyers are (a stationed garrison
                    // is supplied by its own settlement, so this only bites the pathological unsupplied case).
                    // Clamp the batch to what the treasury can spare above the reserve it keeps to go on
                    // paying the garrison's wages, and require it to hold ten times a man's promotion first.
                    else if (party.MobileParty.IsGarrison && fullGold > 0)
                    {
                        if (!_supplyGoldAllowed)
                        {
                            continue;
                        }
                        Settlement fief = GarrisonFiefOf(party);
                        int wealth = (fief != null) ? SettlementWealth.GetSettlementWealth(fief) : 0;
                        int reserve = party.MobileParty.TotalWage * GarrisonRecruitCost.GarrisonReserveDays;
                        int spendable = MathF.Max(0, wealth - reserve);
                        int affordable = spendable / fullGold;
                        if (wealth < fullGold * GarrisonRecruitCost.GarrisonSpawnReserveMult)
                        {
                            affordable = 0;
                        }
                        count = MathF.Min(count, affordable);
                        if (count <= 0)
                        {
                            continue;
                        }
                    }

                    if ((!party.Culture.IsBandit || upgradeTarget.Culture.IsBandit) && (character.Occupation != Occupation.Bandit || upgradeModel.CanPartyUpgradeTroopToTarget(party, character, upgradeTarget)))
                    {
                        options.Add(new UpgradeOption
                        {
                            Target = character,
                            UpgradeTarget = upgradeTarget,
                            Count = count,
                            TotalGoldCost = RBMCampaignPatches.GetBatchUpgradeGoldCost(party, character, upgradeTarget, count),
                            XpCost = xpCost,
                            StackSize = element.Number,
                            Chance = upgradeModel.GetUpgradeChanceForTroopUpgrade(party, character, i)
                        });
                    }
                }
                return options;
            }

            private static UpgradeOption SelectPossibleUpgrade(List<UpgradeOption> options)
            {
                UpgradeOption result = options[0];
                if (options.Count > 1)
                {
                    float totalChance = 0f;
                    foreach (UpgradeOption option in options)
                    {
                        totalChance += option.Chance;
                    }
                    float roll = totalChance * MBRandom.RandomFloat;
                    foreach (UpgradeOption option in options)
                    {
                        roll -= option.Chance;
                        if (roll <= 0f)
                        {
                            result = option;
                            break;
                        }
                    }
                }
                return result;
            }

            /// <summary>The fief a garrison belongs to -- the town or castle it holds -- or null.</summary>
            private static Settlement GarrisonFiefOf(PartyBase party)
            {
                MobileParty mobileParty = party.MobileParty;
                Settlement settlement = (mobileParty != null) ? (mobileParty.CurrentSettlement ?? mobileParty.HomeSettlement) : null;
                return (settlement != null && (settlement.IsTown || settlement.IsCastle)) ? settlement : null;
            }

            private static void UpgradeTroop(PartyBase party, int rosterIndex, UpgradeOption option)
            {
                TroopRoster memberRoster = party.MemberRoster;
                memberRoster.SetElementXp(rosterIndex, memberRoster.GetElementXp(rosterIndex) - option.XpCost * option.Count);
                // Drawn down before the roster shrinks, against the same stockpile the price used.
                int spoilsSpend = SpoilsPool.GetBatchSpoilsSpend(party, option.Target, option.UpgradeTarget, option.Count);
                // A cheaper-kitted upgrade salvages its surplus into the upgradeTarget stack's purse.
                int spoilsCredit = SpoilsPool.GetSpoilsCreditForUpgrade(option.Target, option.UpgradeTarget) * option.Count;
                SpoilsPool.AddSpoils(party, option.Target, -spoilsSpend);
                memberRoster.AddToCounts(option.Target, -option.Count);
                memberRoster.AddToCounts(option.UpgradeTarget, option.Count);
                SpoilsPool.AddSpoils(party, option.UpgradeTarget, spoilsCredit);
                // The leaving men take their share of the purse left after their upgrade is paid for.
                int carried = SpoilsPool.CarrySpoilsOnUpgrade(party, option.Target, option.UpgradeTarget, option.Count, option.StackSize);
                if (SpoilsLog.IsEnabled)
                {
                    SpoilsLog.Log("UPGRADE", party, SpoilsLog.Describe(party) + " upgraded " + option.Count + "x "
                        + SpoilsLog.Describe(option.Target) + " -> " + SpoilsLog.Describe(option.UpgradeTarget)
                        + " | stack was " + option.StackSize
                        + ", free " + SpoilsPool.GetFreeUpgradeCount(party, option.Target, option.UpgradeTarget)
                        + ", spoils spent " + spoilsSpend
                        + (spoilsCredit > 0 ? ", salvaged " + spoilsCredit + " into " + SpoilsLog.Describe(option.UpgradeTarget) + "'s purse" : "")
                        + (carried > 0 ? ", carried " + carried + " of the purse along" : "")
                        + ", pool " + SpoilsPool.GetSpoils(party, option.Target)
                        + ", gold " + option.TotalGoldCost + ", xp " + (option.XpCost * option.Count));
                }
                SpoilsPool.ClearSpoilsIfStackGone(party, option.Target);
                if (option.Count > 0)
                {
                    ApplyEffects(party, option);
                }
            }

            private static void ApplyEffects(PartyBase party, UpgradeOption option)
            {
                Hero payer = (party.Owner != null && party.Owner.IsAlive) ? party.Owner : party.LeaderHero;
                // What was actually billed to a hero, which is nothing at all for a party that has none.
                // Only this reaches the town below -- charging one sum and paying over another would mint
                // the difference, and paying over a bill nobody was sent would mint the whole of it.
                int goldCharged = 0;
                if (payer != null && payer.IsAlive)
                {
                    SkillLevelingManager.OnUpgradeTroops(party, option.Target, option.UpgradeTarget, option.Count);
                    GiveGoldAction.ApplyBetweenCharacters(payer, null, option.TotalGoldCost, true);
                    goldCharged = option.TotalGoldCost;
                }
                else if (party.MobileParty.IsGarrison)
                {
                    // No owner to send the bill to: the fief buys its own garrison's promotion out of its
                    // treasury. The clamp in GetPossibleUpgradeTargets kept the batch inside what it holds,
                    // so this debit is what reaches the town below -- treasury out, the armourers who did
                    // the work paid in (SupplyUpgradeFromTown credits the town's citizens the same sum).
                    Settlement fief = GarrisonFiefOf(party);
                    if (fief != null)
                    {
                        goldCharged = SettlementWealth.Debit(fief, option.TotalGoldCost, SettlementWealth.Source.Upgrade);
                    }
                }

                // Draw the gold just billed against this party's daily upgrade budget, so a later stack in
                // the same pass -- or a second pass after a battle the same day -- sees the reduced ceiling.
                // No-op for an uncapped party (the store ignores the spend and the scratch stays MaxValue).
                if (goldCharged > 0)
                {
                    PartyUpgradeBudget.RecordDailySpend(party, goldCharged);
                    if (_upgradeGoldBudgetRemaining != int.MaxValue)
                    {
                        _upgradeGoldBudgetRemaining = MathF.Max(0, _upgradeGoldBudgetRemaining - goldCharged);
                    }
                }

                // The GOLD leg of the promotion goes over to the town that armed the gold-buyers -- the gold
                // destroyed by the null-recipient call above. The spoils leg drawn from the men's own purse
                // in UpgradeTroop is deliberately NOT handed over: men the stockpile covered re-armed from
                // their own loot, so their promotion takes nothing off the town's shelves and adds nothing
                // to its citizens' wealth. With the SupplyTown gate on, value-appropriate kit leaves the
                // town's market for the gold-buyers only; with it off the gold still lands, which is why the
                // town is resolved here rather than taken from the gate's own _supplyTown alone.
                //
                // Still OUTSIDE the payer check: a party with no hero to bill has goldCharged == 0 and so
                // hands over nothing, but the call keeps the item draw and logging paths uniform.
                if (UpgradeSupply.PaymentEnabled)
                {
                    Town market = (_supplyTown != null) ? _supplyTown : UpgradeSupply.ResolveMarketTown(party.MobileParty);
                    UpgradeSupply.SupplyUpgradeFromTown(market, party, option.Target, option.UpgradeTarget,
                        option.Count, goldCharged);
                }
            }
        }
    }
}
