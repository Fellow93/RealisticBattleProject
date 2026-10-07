using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// Where an AI lord's party appears and what it starts with, replacing vanilla's
    /// <c>HeroSpawnCampaignBehavior.SpawnLordParty</c> (game start and every in-play respawn: a lord
    /// released, escaped, or handed a new party slot by his clan).
    ///
    /// WHERE: always a town or castle, never a village. A clan that owns any fortification spawns at one
    /// of its own (unthreatened first, see <see cref="RBMLordSpawnSettlementBehavior"/>); a landless clan
    /// takes vanilla's pick, a village's own castle or town in place of the village, and failing that the
    /// nearest fortification not at war with it.
    ///
    /// WHAT, in play, for a major clan: no men from the party template. The lord takes his starting men
    /// straight out of the garrison of the fortification he spawns at -- 40% of his party limit at his
    /// own clan's fief, free; 20% at anyone else's, paid to that fief at the going recruit price; and a
    /// king 50% wherever he is, never paying. Short of men or gold he takes what there is. The men bring
    /// their share of their troop's spoils with them.
    ///
    /// Unchanged: a new game's lords keep vanilla's starting armies (template plus the 75-90% fill, scattered
    /// round the gate); a minor faction's in-play respawn keeps its template (landless, usually at war --
    /// a lone leader was caught within seconds in testing); the player's clan never comes through here.
    /// </summary>
    internal static class LordRespawn
    {
        /// <summary>Share of his party limit a lord levies from his own clan's fief, free.</summary>
        private const float OwnFiefLevyShare = 0.40f;
        /// <summary>Share of his party limit a lord levies from another's fief, paying that fief.</summary>
        private const float OtherFiefLevyShare = 0.20f;
        /// <summary>Share of his party limit a king levies from any fief, never paying.</summary>
        private const float KingLevyShare = 0.50f;

        // Set only while an in-play respawn of a major-clan lord creates his party; read by the
        // template-fill prefix, which runs inside that call (SpawnLordParty → CreateLordParty →
        // OnMobilePartySetOnCreation → InitializeLordPartyProperties → InitializeMobilePartyAroundPosition).
        private static bool _skipTemplate;

        [HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "SpawnLordParty")]
        private class SpawnLordPartyAtFortification
        {
            private static bool Prefix(Hero hero, bool isNewGame, ref MobileParty __result)
            {
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled || hero == null || hero.Clan == null
                    || hero.Clan == Clan.PlayerClan)
                {
                    return true;
                }
                string why;
                Settlement settlement = ChooseSpawnSettlement(hero, out why);
                if (settlement == null)
                {
                    // No town or castle anywhere will have him: leave it to vanilla.
                    if (SpoilsLog.IsEnabled)
                    {
                        SpoilsLog.Log("SPAWN", hero.Name + " of " + hero.Clan.Name + ": no fortification will have"
                            + " him, vanilla spawns him");
                    }
                    return true;
                }
                if (hero.GovernorOf != null)
                {
                    ChangeGovernorAction.RemoveGovernorOf(hero);
                }

                if (isNewGame)
                {
                    __result = SpawnStartingArmy(hero, settlement);
                    LogSpawn(__result, hero, settlement, why, "new game, starting army");
                    return false;
                }

                bool levy = !hero.Clan.IsMinorFaction;
                _skipTemplate = levy;
                MobileParty party;
                try
                {
                    // At the gate, heading in: he takes his men from this garrison.
                    party = MobilePartyHelper.SpawnLordParty(hero, settlement);
                }
                finally
                {
                    _skipTemplate = false;
                }
                LogSpawn(party, hero, settlement, why,
                    levy ? "respawn, men levied from the garrison" : "respawn, minor faction keeps its band");
                if (levy && party != null)
                {
                    LevyFromGarrison(hero, party, settlement);
                }
                __result = party;
                return false;
            }
        }

        /// <summary>
        /// Skips the party template's men (and ships) for a major-clan lord respawned in play: only the lord
        /// himself, already on the roster, is placed. No recruit seed or spoils entry is ever made for men
        /// who are not there.
        /// </summary>
        [HarmonyPatch(typeof(MobileParty), "InitializeMobilePartyWithPartyTemplate")]
        private class SkipTemplateTroops
        {
            private static bool Prefix(MobileParty __instance, CampaignVec2 position)
            {
                if (!_skipTemplate)
                {
                    return true;
                }
                __instance.InitializeMobilePartyAtPosition(position);
                return false;
            }
        }

        /// <summary>
        /// The town or castle the lord's party appears at. Never a village. <paramref name="why"/> says
        /// which rule picked it, for the SPAWN log line.
        /// </summary>
        private static Settlement ChooseSpawnSettlement(Hero hero, out string why)
        {
            // Vanilla's scored pick, with RBM's postfix already preferring his clan's own unthreatened
            // fortification when it has one.
            Settlement pick = SettlementHelper.GetBestSettlementToSpawnAround(hero);

            // A clan with a town or castle of its own always spawns at one, even if every one of them is
            // besieged or raided (the postfix only takes unthreatened ones).
            Settlement own = NearestOwnFortification(hero, pick);
            if (own != null)
            {
                if (pick != null && pick.IsFortification && pick.OwnerClan == hero.Clan)
                {
                    why = "own fief";
                    return pick;
                }
                why = "own fief, all threatened (vanilla picked " + Name(pick) + ")";
                return own;
            }

            // Landless: vanilla's pick, a village standing in for its own castle or town.
            Settlement candidate = AsFortification(pick);
            if (IsWelcoming(candidate, hero))
            {
                why = (pick != null && pick.IsVillage) ? "landless, castle/town of vanilla's village " + pick.Name
                    : "landless, vanilla's pick";
                return candidate;
            }
            Settlement home = AsFortification(hero.MapFaction?.InitialHomeSettlement);
            if (IsWelcoming(home, hero))
            {
                why = "landless, faction home (vanilla picked " + Name(pick) + ")";
                return home;
            }
            Settlement from = pick ?? hero.MapFaction?.InitialHomeSettlement ?? hero.HomeSettlement;
            why = "landless, nearest non-hostile fortification to " + Name(from);
            return from != null
                ? SettlementHelper.FindNearestFortificationToSettlement(from, MobileParty.NavigationType.Default,
                    s => IsWelcoming(s, hero))
                : null;
        }

        private static string Name(Settlement settlement)
        {
            return settlement != null ? settlement.Name.ToString() : "none";
        }

        /// <summary>One SPAWN line per lord party RBM places: who, where, why there, and what he starts with.</summary>
        private static void LogSpawn(MobileParty party, Hero hero, Settlement settlement, string why, string what)
        {
            if (!SpoilsLog.IsEnabled)
            {
                return;
            }
            string who = party != null ? SpoilsLog.Describe(party.Party) : hero.Name.ToString();
            int men = party != null ? party.MemberRoster.TotalManCount - party.MemberRoster.TotalHeroes : 0;
            SpoilsLog.Log("SPAWN", party?.Party, who + " of " + hero.Clan.Name + " at " + settlement.Name
                + (settlement.IsCastle ? " (castle)" : " (town)") + " -- " + why + "; " + what
                + "; " + men + " men from template");
        }

        /// <summary>His clan's own town or castle nearest to <paramref name="near"/>, threatened or not; null if it has none.</summary>
        private static Settlement NearestOwnFortification(Hero hero, Settlement near)
        {
            Settlement origin = hero.LastKnownClosestSettlement ?? near;
            Settlement best = null;
            float bestDistance = float.MaxValue;
            foreach (Settlement settlement in hero.Clan.Settlements)
            {
                if (!settlement.IsFortification)
                {
                    continue;
                }
                float distance = origin != null
                    ? Campaign.Current.Models.MapDistanceModel.GetDistance(origin, settlement, isFromPort: false,
                        isTargetingPort: false, MobileParty.NavigationType.Default)
                    : 0f;
                if (best == null || distance < bestDistance)
                {
                    best = settlement;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>A village's own castle or town in its place; a fortification as it is; anything else null.</summary>
        private static Settlement AsFortification(Settlement settlement)
        {
            if (settlement == null)
            {
                return null;
            }
            if (settlement.IsVillage)
            {
                return settlement.Village?.Bound;
            }
            return settlement.IsFortification ? settlement : null;
        }

        /// <summary>A town or castle a landless lord can safely appear at: not besieged, not his enemy's.</summary>
        private static bool IsWelcoming(Settlement settlement, Hero hero)
        {
            if (settlement == null || !settlement.IsFortification || settlement.IsUnderSiege
                || settlement.MapFaction == null || hero.MapFaction == null)
            {
                return false;
            }
            return settlement.MapFaction == hero.MapFaction || !settlement.MapFaction.IsAtWarWith(hero.MapFaction);
        }

        /// <summary>
        /// Vanilla's game-start spawn, at the chosen fortification: scattered round its gate, filled from the
        /// clan's template and then to 75-90% of the party limit by the template's weights.
        /// </summary>
        private static MobileParty SpawnStartingArmy(Hero hero, Settlement settlement)
        {
            MobileParty party = MobilePartyHelper.SpawnLordParty(hero, settlement.GatePosition,
                Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default) / 2f);
            if (party == null)
            {
                return null;
            }
            int toAdd = (int)((party.Party.PartySizeLimit - party.MemberRoster.TotalManCount) * MBRandom.RandomFloatRanged(0.75f, 0.9f));
            PartyTemplateObject template = party.LordPartyComponent.Owner.Clan.DefaultPartyTemplate;
            List<(CharacterObject, float)> weights = new List<(CharacterObject, float)>();
            foreach (PartyTemplateStack stack in template.Stacks)
            {
                weights.Add((stack.Character, (stack.MinValue + stack.MaxValue) / 2f));
            }
            for (int i = 0; i < toAdd && weights.Count > 0; i++)
            {
                party.AddElementToMemberRoster(MBRandom.ChooseWeighted(weights), 1);
            }
            return party;
        }

        /// <summary>
        /// Moves the respawned lord's starting men out of <paramref name="settlement"/>'s garrison into his
        /// party: <see cref="KingLevyShare"/> of his party limit for a king (free), <see cref="OwnFiefLevyShare"/>
        /// at his own clan's fief (free), <see cref="OtherFiefLevyShare"/> elsewhere (paid to the fief).
        /// Healthy men only, drawn across the garrison's troop types in proportion to their numbers.
        /// </summary>
        private static void LevyFromGarrison(Hero hero, MobileParty party, Settlement settlement)
        {
            MobileParty garrison = settlement.Town?.GarrisonParty;
            Kingdom kingdom = hero.Clan.Kingdom;
            bool king = kingdom != null && kingdom.Leader == hero;
            bool ownFief = settlement.OwnerClan == hero.Clan;
            bool free = king || ownFief;
            float share = king ? KingLevyShare : (ownFief ? OwnFiefLevyShare : OtherFiefLevyShare);
            int target = MathF.Round(party.Party.PartySizeLimit * share);
            string standing = king ? "king" : (ownFief ? "own fief" : "another's fief");

            if (garrison == null || garrison.MemberRoster == null || target <= 0)
            {
                Log(party, settlement, standing, target, 0, 0, 0, "no garrison");
                return;
            }

            // Healthy men per troop type, and the whole garrison's.
            List<CharacterObject> troops = new List<CharacterObject>();
            List<int> healthy = new List<int>();
            int available = 0;
            TroopRoster roster = garrison.MemberRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                int fit = element.Number - element.WoundedNumber;
                if (element.Character.IsHero || fit <= 0)
                {
                    continue;
                }
                troops.Add(element.Character);
                healthy.Add(fit);
                available += fit;
            }
            if (available <= 0)
            {
                Log(party, settlement, standing, target, 0, 0, 0, "garrison empty");
                return;
            }

            // In proportion to each type's numbers, then the remainder from the largest stacks.
            int want = MathF.Min(target, available);
            int[] take = new int[troops.Count];
            int planned = 0;
            for (int i = 0; i < troops.Count; i++)
            {
                take[i] = (int)((long)want * healthy[i] / available);
                planned += take[i];
            }
            while (planned < want)
            {
                int largest = -1;
                for (int i = 0; i < troops.Count; i++)
                {
                    if (take[i] < healthy[i] && (largest < 0 || healthy[i] - take[i] > healthy[largest] - take[largest]))
                    {
                        largest = i;
                    }
                }
                if (largest < 0)
                {
                    break;
                }
                take[largest]++;
                planned++;
            }

            int taken = 0;
            int paid = 0;
            int carried = 0;
            int unaffordable = 0;
            int goldBefore = hero.Gold;
            for (int i = 0; i < troops.Count; i++)
            {
                int count = take[i];
                if (count <= 0)
                {
                    continue;
                }
                CharacterObject troop = troops[i];
                if (!free)
                {
                    // He pays the fief the going recruit price for each man, and takes only those he can pay for.
                    int price = MathF.Max(1, RecruitSupply.RecruitPrice(troop, hero, settlement).RoundedResultNumber);
                    int affordable = MathF.Min(count, hero.Gold / price);
                    unaffordable += count - affordable;
                    count = affordable;
                    if (count <= 0)
                    {
                        continue;
                    }
                    int cost = count * price;
                    GiveGoldAction.ApplyBetweenCharacters(hero, null, cost, disableNotification: true);
                    paid += SettlementWealth.Credit(settlement, cost, SettlementWealth.Source.GarrisonLevy);
                }
                garrison.MemberRoster.AddToCounts(troop, -count);
                party.MemberRoster.AddToCounts(troop, count);
                // The men bring their share of their troop's purse and rations from the garrison.
                carried += SpoilsPool.TransferSpoils(garrison.Party, party.Party, troop, count);
                taken += count;
            }
            // Why he came away with fewer than his share: a garrison too small, or gold too short.
            string note = null;
            if (available < target)
            {
                note = "garrison had only " + available + " fit men";
            }
            if (unaffordable > 0)
            {
                note = (note != null ? note + "; " : "") + unaffordable + " not affordable (gold " + goldBefore
                    + "d -> " + hero.Gold + "d)";
            }
            Log(party, settlement, standing, target, taken, paid, carried, note);
        }

        private static void Log(MobileParty party, Settlement settlement, string standing, int target, int taken,
            int paid, int carried, string note)
        {
            if (!SpoilsLog.IsEnabled)
            {
                return;
            }
            SpoilsLog.Log("RECRUIT", party.Party, SpoilsLog.Describe(party.Party) + " respawned at " + settlement.Name
                + " (" + standing + "): took " + taken + " of " + target + " men from its garrison"
                + (carried > 0 ? ", bringing " + carried + " spoils" : "")
                + (paid > 0 ? ", paid " + paid + "d to " + settlement.Name : "")
                + (note != null ? " (" + note + ")" : ""));
        }
    }
}
