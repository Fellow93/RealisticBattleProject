using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Where a soldier's equipment comes from and who is paid for him. Two legs, at two different
    /// moments, and deliberately not tied to each other:
    ///
    ///   GEAR, when the man first offers himself. A volunteer appearing in a notable's roster is a
    ///   villager being armed, so his kit comes off the market that supplies the place -- a town's own,
    ///   a village's from the town it trades with. The community arms its own sons: a town's
    ///   citizens front the kit's worth, a village pays its market town for it, and what the shelves lack
    ///   is found off-screen (see <see cref="DrawKitFromMarket"/>). No lord pays here.
    ///
    ///   MONEY, when a party takes him. What the recruiting lord pays goes into that same town's OWN
    ///   TREASURY -- raising soldiers is the fief's business as a body, not its shopkeepers' -- and so it
    ///   pays no market fee, the coin never having crossed a counter. Vanilla charges him and then
    ///   destroys it, paying it to nobody at all. The PRICE replaces vanilla's flat tier ladder with one
    ///   scaled to the recruiter's standing: a lord raising his own fief's men, or a ruler raising his
    ///   realm's, pays nothing; a vassal recruiting inside his own kingdom pays the man's full gear plus a
    ///   five-day enlistment bounty; a mercenary, or a lord recruiting outside his realm, pays that plus a
    ///   tenth as an outsider's tithe; and a landless adventurer -- no realm, no contract -- pays only the
    ///   bounty and the tithe, his rabble bringing their own kit. See <see cref="RecruitPrice"/>.
    ///
    /// Keeping the two apart is the point. Gear leaves when a man is raised, whether or not anyone ever
    /// comes for him; coin arrives when someone does. So a war-torn region arms volunteers its lords are
    /// too poor to collect, and a rich lord sweeping through a stripped countryside pays full price for
    /// men in rags -- both of which are the right answer, and neither of which survives if one number is
    /// computed from the other.
    ///
    /// The whole feature lives in this file plus its call sites in
    /// <see cref="SpoilsPool.OnTroopRecruited"/> / <see cref="SpoilsPool.OnUnitRecruited"/>, each tagged
    /// with the comment "RecruitSupply pay". Switch it off at runtime with
    /// RBMConfig.recruitDrawsFromSettlementStock = 0; remove it outright by deleting this file, its
    /// csproj entry, and those tagged lines.
    /// </summary>
    public static partial class RecruitSupply
    {
        /// <summary>On only when the spoils economy is on and the feature is switched on in config.</summary>
        public static bool IsEnabled
        {
            get { return SpoilsPool.IsEnabled && RBMConfig.RBMConfig.recruitDrawsFromSettlementStock; }
        }

        // ------------------------------------------------------------------ pricing

        /// <summary>
        /// Days of the man's own pay that raising him costs over and above vanilla's price -- the bounty
        /// and the trouble of it. Deliberately priced off his wage rather than off his kit: a kit-priced
        /// recruit costs his lord a fortune up front, which is more than mustering a man should ever be.
        /// </summary>
        private const int EnlistmentWageDays = 5;

        /// <summary>
        /// The tithe an OUTSIDER pays over the odds to raise another lord's subjects -- a tenth on top of
        /// the whole price. A lord recruiting in his own fief, or a ruler anywhere in his realm, pays
        /// nothing at all: the levy is owed him. Everyone else is buying men who owe their service to
        /// someone else, and the settlement takes its cut for parting with them.
        /// </summary>
        private const float OutsiderRecruitSurcharge = 0.10f;

        /// <summary>
        /// Whether <paramref name="recruiter"/> raises this settlement's men for free -- the fief's own
        /// clan, or the ruler of the realm it belongs to. Feudalism: a lord's subjects owe him their
        /// service, and a king's realm owes him its levies, so neither pays to call them up. Everyone
        /// else is an outsider and pays the going rate plus the tithe.
        /// </summary>
        public static bool RecruitsFree(Settlement settlement, Hero recruiter)
        {
            if (settlement == null || recruiter == null)
            {
                return false;
            }
            if (recruiter.Clan != null && recruiter.Clan == settlement.OwnerClan)
            {
                return true;
            }
            IFaction faction = settlement.MapFaction;
            return faction != null && faction.Leader == recruiter;
        }

        /// <summary>
        /// The settlement a recruiter is mustering from, resolved from the buyer, since the cost model is
        /// not told where the recruiting happens. The player's is his party's current settlement; an AI
        /// lord's is the settlement his party sits in. Null when the buyer is not standing in one -- an
        /// AI weighing a muster it has not reached yet -- in which case the price stays a neutral
        /// full-rate quote with neither the levy nor the tithe applied.
        /// </summary>
        private static Settlement RecruiterSettlement(Hero buyer)
        {
            if (buyer == null)
            {
                return null;
            }
            if (buyer == Hero.MainHero)
            {
                return MobileParty.MainParty?.CurrentSettlement ?? buyer.CurrentSettlement;
            }
            return buyer.CurrentSettlement ?? buyer.PartyBelongedTo?.CurrentSettlement;
        }

        /// <summary>
        /// What one man of this troop costs to raise, on top of what vanilla asks: five days of his own
        /// pay. Read through his wage, so RBM's own tier-based pay table drives it and a dearer soldier
        /// is dearer to enlist without anything here needing to know why.
        /// </summary>
        public static int EnlistmentPremium(CharacterObject character)
        {
            if (character == null || character.IsHero)
            {
                return 0;
            }
            int wage = character.TroopWage;
            return (wage > 0) ? wage * EnlistmentWageDays : 0;
        }

        /// <summary>
        /// What a man of this troop wears, by worth. Not a price -- no money is ever charged from this.
        /// It sizes how much gear he takes off the market when he is raised, and picks stock of the right
        /// tier so a helmet is drawn against helmets of about the right quality.
        ///
        /// Mount-less on purpose: vanilla already charges a flat surcharge for a mounted troop's horse
        /// (150 denars, 500 above level 26), so the mount stays abstract -- paid for on vanilla's terms
        /// and not drawn off the market, which is why <see cref="SpoilsPool.GetKitSlots"/> is asked for
        /// the mount-less slot list too.
        /// </summary>
        public static int KitValue(CharacterObject character)
        {
            return (character == null || character.IsHero) ? 0 : SpoilsPool.GetEquipmentValue(character);
        }

        /// <summary>
        /// What one man's kit is worth to a market draw (<see cref="DrawKitFromMarket"/>). He is armed slot
        /// by slot in his representative (first) battle set, so his kit is worth that set -- not the average
        /// over all his sets that <see cref="KitValue"/> gives. With the average, a first set dearer than it
        /// ran the budget dry before its last slots, which were then billed to the citizens as missing
        /// though the shelves had them. Militia arming prices its men through this too, so its
        /// affordability gate, refunds and log agree with what the draw charges.
        /// </summary>
        internal static int DrawnKitValue(CharacterObject character, bool includeMount)
        {
            if (character == null || character.IsHero)
            {
                return 0;
            }
            return DrawnKitValue(character, includeMount, SpoilsPool.GetKitSlots(character, includeMount));
        }

        private static int DrawnKitValue(CharacterObject character, bool includeMount, List<SpoilsPool.SlotPurchase> slots)
        {
            if (slots.Count == 0)
            {
                return includeMount ? SpoilsPool.GetEquipmentValueWithMount(character) : KitValue(character);
            }
            int value = 0;
            foreach (SpoilsPool.SlotPurchase slot in slots)
            {
                value += slot.Value;
            }
            return value;
        }

        /// <summary>
        /// Adds an enlistment premium to what a lord pays for a man -- five days of the soldier's own
        /// wage, on top of vanilla's flat tier ladder. Vanilla asks ten denars for a peasant, which is
        /// less than the man earns in a week and nothing at all against what raising him is worth.
        ///
        /// This is what the supplying town is then paid, so it is credited money that genuinely changed
        /// hands instead of money conjured for it. Both recruit paths price through this one model -- the
        /// player's screen via <c>RecruitVolunteerTroopVM.Cost</c> and the AI via
        /// <c>RecruitmentCampaignBehavior</c> -- so patching it here covers both.
        /// </summary>
        /// <remarks>
        /// Skipped when the caller asked for a price <paramref name="withoutItemCost"/>, which is that
        /// flag's nearest sense here: a bare quote for the man, without what comes attached to him.
        ///
        /// The addition lands on the ExplainedNumber's base, so vanilla's recruitment perks scale it
        /// along with the rest of the price -- a lord with a recruiting perk pays less of it too. That is
        /// why the payment reads the model's own result rather than <see cref="EnlistmentPremium"/>
        /// directly: what reaches the town must be what he actually paid, perks and all.
        /// </remarks>
        [HarmonyPatch(typeof(DefaultPartyWageModel))]
        [HarmonyPatch("GetTroopRecruitmentCost")]
        private class OverrideGetTroopRecruitmentCost
        {
            private static void Postfix(CharacterObject troop, Hero buyerHero, bool withoutItemCost, ref ExplainedNumber __result)
            {
                if (!IsEnabled || withoutItemCost || buyerHero == null)
                {
                    return;
                }
                __result = RecruitPrice(troop, buyerHero, RecruiterSettlement(buyerHero));
            }
        }

        /// <summary>
        /// What a man of this troop costs <paramref name="recruiter"/> to raise at
        /// <paramref name="settlement"/>, priced by the recruiter's standing rather than vanilla's flat
        /// ladder. Replaces the whole quote so the recruit screen and the payment leg read one number.
        /// </summary>
        /// <remarks>
        /// The tiers, cheapest to dearest:
        ///   * OWNER / RULER -- a clan raising its own fief's men, or a ruler raising them anywhere in his
        ///     realm: free. Feudal service is owed, not bought.
        ///   * VASSAL AT HOME -- part of the kingdom that holds the settlement: full gear plus a five-day
        ///     enlistment bounty, no tithe. His own realm's men, at cost.
        ///   * MERCENARY, or a LORD ABROAD (a vassal recruiting in another realm): gear + bounty + a tenth
        ///     as the outsider's tithe.
        ///   * ADVENTURER -- no realm, no contract: bounty + tithe only, no gear. His rabble bring their own.
        ///
        /// The recruiter's contract state is read the way the rest of the module reads it -- Clan.Kingdom
        /// plus IsUnderMercenaryService (see the contract-state note). "Full gear" is the man's whole kit,
        /// mount and all, which is why this uses the with-mount valuation rather than the mount-less
        /// <see cref="KitValue"/> the market-draw leg uses.
        /// </remarks>
        public static ExplainedNumber RecruitPrice(CharacterObject troop, Hero recruiter, Settlement settlement, bool describe = false)
        {
            bool atSettlement = settlement != null && (settlement.IsVillage || settlement.IsTown);
            // Mercenaries, gangsters and caravan guards are hired for coin -- never owed as feudal
            // service -- so they are paid even in one's own fief and stay out of the free path. That
            // also preserves vanilla's "recruitment cost is never zero" invariant (its own model ends
            // on LimitMin(1f)) for the one caller that leans on it: RecruitmentCampaignBehavior's tavern
            // mercenary menu divides the player's gold by the per-man cost, so a zero there is a
            // divide-by-zero crash the moment the player stands in a town he owns.
            //
            // Occupation alone is not enough: troop-replacement mods (Adonnay's Troop Changer and the like)
            // can stock the tavern with their own trees flagged Soldier, which then fell into the free path
            // and crashed that menu anyway. So whatever the town's tavern is actually selling is paid too.
            bool paidTroop = troop != null && (troop.Occupation == Occupation.Mercenary
                || troop.Occupation == Occupation.Gangster || troop.Occupation == Occupation.CaravanGuard
                || IsTavernMercenary(troop, settlement));
            if (atSettlement && !paidTroop && RecruitsFree(settlement, recruiter))
            {
                return new ExplainedNumber(0f, describe);
            }

            Clan clan = (recruiter != null) ? recruiter.Clan : null;
            bool hasRealm = clan != null && clan.Kingdom != null;              // a vassal or a mercenary
            bool mercenary = hasRealm && clan.IsUnderMercenaryService;
            bool vassal = hasRealm && !mercenary;
            IFaction settlementFaction = (settlement != null) ? settlement.MapFaction : null;
            bool vassalAtHome = vassal && settlementFaction != null && clan.Kingdom == settlementFaction;

            // An adventurer pays no gear -- his men come as they are; a lord or mercenary, part of a
            // military supply, pays the man's full equipment, mount included.
            int gear = hasRealm ? SpoilsPool.GetEquipmentValueWithMount(troop) : 0;
            int premium = EnlistmentPremium(troop);

            ExplainedNumber cost = new ExplainedNumber(0f, describe);
            if (gear > 0)
            {
                cost.Add(gear, GearLine);
            }
            if (premium > 0)
            {
                cost.Add(premium, EnlistmentLine);
            }
            // Everyone but a vassal recruiting inside his own realm pays the outsider's tithe on the whole.
            if (!vassalAtHome)
            {
                cost.AddFactor(OutsiderRecruitSurcharge, OutsiderTitheLine);
            }

            // The recruiter's own recruiting perks and feats still tell, discounting the whole price the
            // way they discount vanilla's -- Frugal, RenownedArcher, the Khuzait feat and the rest. Since
            // this replaces vanilla's quote outright, those bonuses have to be reapplied here or they are
            // lost. Also floors the price at one denar, as vanilla does.
            ApplyRecruitmentPerks(ref cost, troop, recruiter);
            return cost;
        }

        /// <summary>
        /// Whether <paramref name="troop"/> is the mercenary this town's tavern is currently hiring out,
        /// read from vanilla's own tavern ledger rather than from the troop's XML occupation.
        /// </summary>
        private static bool IsTavernMercenary(CharacterObject troop, Settlement settlement)
        {
            if (settlement == null || !settlement.IsTown || settlement.Town == null || Campaign.Current == null)
            {
                return false;
            }
            RecruitmentCampaignBehavior recruitment = Campaign.Current.GetCampaignBehavior<RecruitmentCampaignBehavior>();
            return recruitment != null && recruitment.GetMercenaryData(settlement.Town)?.TroopType == troop;
        }

        /// <summary>
        /// The main party's recruit price for this troop, itemised for the UI. The same model path the
        /// recruit screen's Cost reads, but built with descriptions on so the wage (enlistment) and gear
        /// legs can be broken out as a tooltip -- see RecruitCostHint. Its RoundedResultNumber is the
        /// figure shown on the tile; its GetLines() are the named parts that sum to it.
        /// </summary>
        public static ExplainedNumber MainPartyRecruitCost(CharacterObject troop)
        {
            return RecruitPrice(troop, Hero.MainHero, RecruiterSettlement(Hero.MainHero), describe: true);
        }

        /// <summary>
        /// Reapplies vanilla's recruiting perks, feats and one-denar floor to a price this model built
        /// from scratch. A verbatim port of the perk block in
        /// <c>DefaultPartyWageModel.GetTroopRecruitmentCost</c>: the same perks, on the same troop
        /// arms, all multiplicative so they scale this price as they would vanilla's. The base tier
        /// ladder, the horse surcharge and the mercenary-troop doubling are NOT ported -- they are the
        /// vanilla pricing this model deliberately replaces; only the buyer's own bonuses carry over.
        /// </summary>
        private static void ApplyRecruitmentPerks(ref ExplainedNumber result, CharacterObject troop, Hero buyerHero)
        {
            if (buyerHero == null || troop == null)
            {
                return;
            }
            if (troop.Tier >= 2 && buyerHero.GetPerkValue(DefaultPerks.Throwing.HeadHunter))
            {
                result.AddFactor(DefaultPerks.Throwing.HeadHunter.SecondaryBonus);
            }
            if (troop.IsInfantry)
            {
                if (buyerHero.GetPerkValue(DefaultPerks.OneHanded.ChinkInTheArmor))
                {
                    result.AddFactor(DefaultPerks.OneHanded.ChinkInTheArmor.SecondaryBonus);
                }
                if (buyerHero.GetPerkValue(DefaultPerks.TwoHanded.ShowOfStrength))
                {
                    result.AddFactor(DefaultPerks.TwoHanded.ShowOfStrength.SecondaryBonus);
                }
                if (buyerHero.GetPerkValue(DefaultPerks.Polearm.HardyFrontline))
                {
                    result.AddFactor(DefaultPerks.Polearm.HardyFrontline.SecondaryBonus);
                }
            }
            else if (troop.IsRanged)
            {
                if (buyerHero.GetPerkValue(DefaultPerks.Bow.RenownedArcher))
                {
                    result.AddFactor(DefaultPerks.Bow.RenownedArcher.SecondaryBonus);
                }
                if (buyerHero.GetPerkValue(DefaultPerks.Crossbow.Piercer))
                {
                    result.AddFactor(DefaultPerks.Crossbow.Piercer.SecondaryBonus);
                }
            }
            if (troop.IsMounted && buyerHero.Culture != null
                && buyerHero.Culture.HasFeat(DefaultCulturalFeats.KhuzaitRecruitUpgradeFeat))
            {
                result.AddFactor(DefaultCulturalFeats.KhuzaitRecruitUpgradeFeat.EffectBonus);
            }
            if (buyerHero.IsPartyLeader && buyerHero.GetPerkValue(DefaultPerks.Steward.Frugal))
            {
                result.AddFactor(DefaultPerks.Steward.Frugal.SecondaryBonus);
            }
            // The Trade and Charm bonuses key off a mercenary-type troop, as in vanilla.
            if (troop.Occupation == Occupation.Mercenary || troop.Occupation == Occupation.Gangster
                || troop.Occupation == Occupation.CaravanGuard)
            {
                if (buyerHero.GetPerkValue(DefaultPerks.Trade.SwordForBarter))
                {
                    result.AddFactor(DefaultPerks.Trade.SwordForBarter.PrimaryBonus);
                }
                if (buyerHero.GetPerkValue(DefaultPerks.Charm.SlickNegotiator))
                {
                    result.AddFactor(DefaultPerks.Charm.SlickNegotiator.PrimaryBonus);
                }
            }
            result.LimitMin(1f);
        }

        private static readonly TextObject EnlistmentLine = new TextObject("{=RBM_CON_111}Enlistment");
        private static readonly TextObject GearLine = new TextObject("{=RBM_recruit_gear}Equipment");
        private static readonly TextObject OutsiderTitheLine = new TextObject("{=RBM_recruit_tithe}Foreign levy");

        // ------------------------------------------------------------------ the market that supplies a place

        /// <summary>
        /// The market that arms and is paid for a man raised at <paramref name="settlement"/>. A town
        /// serves itself; a village has no armourer worth the name, so it draws on the town it trades
        /// with. Null when nothing can serve him -- anywhere that is neither town nor village, or a
        /// castle-bound village whose trade bound has not been assigned (a faction with no town left to
        /// trade into), in which case he is armed off-screen and no stock or coin moves.
        /// </summary>
        /// <remarks>
        /// Village.TradeBound needs no Bound fallback: its getter already returns the bound settlement
        /// itself when that is a town, and the separately-assigned trade bound only for castle villages.
        /// </remarks>
        public static Settlement GetSupplyMarket(Settlement settlement)
        {
            if (settlement == null)
            {
                return null;
            }
            if (settlement.IsTown)
            {
                return settlement;
            }
            if (settlement.IsVillage && settlement.Village != null)
            {
                return settlement.Village.TradeBound;
            }
            return null;
        }

        // ------------------------------------------------------------------ leg one: gear, at creation

        // The settlement's volunteers as they stood before the day's roll, counted by troop type. Static
        // and reused rather than allocated per settlement: this runs for every settlement every day.
        private static readonly Dictionary<CharacterObject, int> _volunteersBefore = new Dictionary<CharacterObject, int>();
        private static readonly Dictionary<CharacterObject, int> _volunteersAfter = new Dictionary<CharacterObject, int>();

        /// <summary>
        /// Arms the day's new volunteers out of the market that supplies the settlement they offered
        /// themselves in. A man who steps forward has to be equipped from somewhere, and this is that
        /// somewhere: his kit's worth of stock leaves the stalls, and nobody is paid for it.
        /// </summary>
        /// <remarks>
        /// A before/after diff rather than a hook on the assignment, because the native method has no
        /// seam to hook -- it fills empty slots and promotes filled ones in one pass, from two different
        /// branches.
        ///
        /// Counted as a MULTISET over the whole settlement, not slot by slot. The tail of the native
        /// method RE-SORTS each notable's VolunteerTypes array weakest-to-strongest, so a slot-indexed
        /// diff would read the shuffle as a roster full of new men and arm the town's whole militia over
        /// again every day. Troop counts are invariant under that sort; slot positions are not.
        ///
        /// A promoted volunteer reads as one troop leaving and a better one arriving, so he draws his new
        /// kit whole rather than the difference. That over-draws slightly on the ~1%-a-day promotion
        /// roll: a little extra stock, or, where the shelves are short, a little extra coin the citizens
        /// spend buying it in. Small enough to leave.
        /// </remarks>
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior))]
        [HarmonyPatch("UpdateVolunteersOfNotablesInSettlement")]
        private class ArmNewVolunteersFromMarket
        {
            private static void Prefix(Settlement settlement)
            {
                _volunteersBefore.Clear();
                if (!IsEnabled)
                {
                    return;
                }
                CountVolunteers(settlement, _volunteersBefore);
            }

            private static void Postfix(Settlement settlement)
            {
                if (!IsEnabled || _volunteersBefore.Count == 0 && settlement == null)
                {
                    return;
                }
                Settlement market = GetSupplyMarket(settlement);
                if (market == null || market.ItemRoster == null)
                {
                    return;
                }
                _volunteersAfter.Clear();
                CountVolunteers(settlement, _volunteersAfter);

                // One priced view of the market shared by every troop type raised here today, so the stall
                // is priced once for the settlement rather than once per type. Built on first need.
                UpgradeSupply.KitStock kitStock = null;
                foreach (KeyValuePair<CharacterObject, int> after in _volunteersAfter)
                {
                    int before;
                    _volunteersBefore.TryGetValue(after.Key, out before);
                    int raised = after.Value - before;
                    if (raised > 0)
                    {
                        if (kitStock == null)
                        {
                            kitStock = new UpgradeSupply.KitStock();
                        }
                        DrawKitFromMarket(market, settlement, after.Key, raised, includeMount: true,
                            kitStock: kitStock);
                    }
                }
            }
        }

        /// <summary>Tallies a settlement's standing volunteers by troop type.</summary>
        private static void CountVolunteers(Settlement settlement, Dictionary<CharacterObject, int> into)
        {
            if (settlement == null || settlement.Notables == null)
            {
                return;
            }
            foreach (Hero notable in settlement.Notables)
            {
                CharacterObject[] volunteers = (notable != null) ? notable.VolunteerTypes : null;
                if (volunteers == null)
                {
                    continue;
                }
                for (int i = 0; i < volunteers.Length; i++)
                {
                    CharacterObject troop = volunteers[i];
                    if (troop == null)
                    {
                        continue;
                    }
                    int running;
                    into.TryGetValue(troop, out running);
                    into[troop] = running + 1;
                }
            }
        }

        /// <summary>
        /// Takes <paramref name="count"/> men's worth of kit off <paramref name="market"/>: for every
        /// slot the troop's gear fills, one item of that class and tier per man, up to the full worth of
        /// what he wears.
        ///
        /// Each slot is drawn against its own NEED (the slot's worth times <paramref name="valueShare"/>):
        /// the piece priced nearest it, within half to double, at the market's own price. A piece dearer
        /// than the need counts only the need against the kit, and the rest of its price is paid back to
        /// the market's citizens (<see cref="SettlementWealth.Source.ArmsSurplus"/>), so an over-tier piece
        /// neither starves the man's later slots nor takes its extra worth off the map. A slot nothing in
        /// stock can fill is skipped, not the end of the draw.
        ///
        /// A TOWN arming its own sons pays nothing -- the stock simply goes, as the class summary sets
        /// out. A VILLAGE draws its kit from a different settlement's market, so it PAYS that town's
        /// merchants for what it takes: the village purse is debited and the town market credited by the
        /// worth of the gear drawn, money moving village → town exactly as the goods move town → village.
        /// A village can pay only what its purse holds, so what it draws is capped there.
        ///
        /// Soft on stock: it takes what the market has and never holds anything up for want of it, since
        /// a picked-clean market would otherwise stop a countryside arming itself at all. What it cannot
        /// supply is found off-screen, unpaid, and reported as the draw's shortfall.
        /// </summary>
        /// <param name="valueShare">
        /// Fraction of each man's full kit value to actually draw and pay for. One for a recruit, who is
        /// armed properly; a quarter for a village's militia levy, who is not (see
        /// <see cref="MilitiaUpkeep.MilitiaVillageGearShare"/>). Scales every slot's need, so the man is
        /// armed in every slot with gear of that fraction of his troop's worth, and the kit budget with it.
        /// </param>
        /// <param name="includeMount">
        /// Whether a mounted man's horse is drawn as a real riding horse off the market and paid for as
        /// part of his kit. True for the recruit draw, whose price already charges the mount value
        /// (<see cref="RecruitPrice"/> via <see cref="SpoilsPool.GetEquipmentValueWithMount"/>), so sourcing
        /// the horse here completes that leg instead of charging for a mount no market ever supplied. False
        /// for the militia levy, which stays mount-less. Never draws a pack animal or livestock even when
        /// true -- see <see cref="UpgradeSupply.IsCargoAnimal"/>.
        /// </param>
        /// <param name="chargeOwnTown">
        /// Whether a town drawing off its own market has its citizens front the value of the kit that left
        /// (the volunteer leg, recovered as recruit pay). False for a town's militia, armed by its citizens
        /// straight off their own shelves: the gear leaving is the cost of what the shelves had, and no
        /// coin moves for it.
        /// </param>
        /// <param name="kitStock">
        /// The priced view of the market's stall the slot searches run against (see
        /// <see cref="UpgradeSupply.KitStock"/>). A caller drawing several times off the same market in one
        /// go -- every troop type a settlement raised today, every man of a militia batch, every stack of a
        /// spawned lord's party -- passes one view to all of them, so the stall is priced once for the lot
        /// rather than once per call. Null builds a view for this call alone. Either way the picks and
        /// prices are the ones a fresh walk of the market would give.
        /// </param>
        internal static KitDraw DrawKitFromMarket(Settlement market, Settlement raisedAt, CharacterObject character, int count,
            float valueShare = 1f, bool includeMount = false, bool chargeOwnTown = true,
            UpgradeSupply.KitStock kitStock = null)
        {
            if (!IsEnabled || market == null || market.ItemRoster == null
                || character == null || character.IsHero || count <= 0)
            {
                return default(KitDraw);
            }
            List<SpoilsPool.SlotPurchase> slots = SpoilsPool.GetKitSlots(character, includeMount);
            int perManValue = DrawnKitValue(character, includeMount, slots);
            if (perManValue <= 0)
            {
                return default(KitDraw);
            }

            // A settlement that draws its gear off ANOTHER settlement's market pays that town's merchants
            // the full kit it set out to buy, out of its own purse -- a village from its trade-bound town, a
            // castle from its nearest friendly town. A town serving its own recruits does not (market ==
            // raisedAt). The buyer can only spend what its purse holds, so the kit budget is capped at its
            // wealth -- a broke fief arms its men cheaper, not free of charge.
            bool remoteBuyerPays = raisedAt != null && (raisedAt.IsVillage || raisedAt.IsCastle)
                && market != raisedAt && SettlementWealth.HasCitizenPurse(market);
            string armsSource = (raisedAt != null && raisedAt.IsCastle)
                ? SettlementWealth.Source.CastleArms
                : SettlementWealth.Source.VillageArms;

            ItemRoster stock = market.ItemRoster;
            int budget = (int)(perManValue * count * valueShare);
            if (budget <= 0)
            {
                return default(KitDraw);
            }
            // The whole kit's worth, before a broke buyer's purse caps what it can draw.
            int fullBudget = budget;
            if (remoteBuyerPays)
            {
                int purse = SettlementWealth.GetSettlementWealth(raisedAt);
                if (purse < budget)
                {
                    budget = MathF.Max(0, purse);
                }
                // A broke buyer draws nothing; its men are armed off-screen.
                if (budget <= 0)
                {
                    return default(KitDraw);
                }
            }
            // drawn: the worth counted against the kit, each piece at most its slot's need. surplus: the
            // price of the pieces above their need, paid back to the market's citizens below. missing: the
            // need of every slot nothing in stock (or in a broke buyer's budget) could fill, found
            // off-screen. A slot filled with a cheaper in-band piece is armed, so the gap between that
            // piece and its need is not missing.
            int drawn = 0;
            int surplus = 0;
            int missing = 0;
            int taken = 0;
            int wanted = 0;
            // The stall as the slot searches see it, priced once and kept in step with every piece taken,
            // instead of walked and re-priced whole for every slot of every man. Priced at the market's own
            // price, as DrawFromStock counts it. See UpgradeSupply.KitStock.
            if (kitStock == null)
            {
                kitStock = new UpgradeSupply.KitStock();
            }
            kitStock.Bind(stock, market);
            if (slots.Count > 0)
            {
                wanted = slots.Count * count;
                foreach (SpoilsPool.SlotPurchase slot in slots)
                {
                    int need = MathF.Max(1, (int)(slot.Value * valueShare));
                    for (int man = 0; man < count; man++)
                    {
                        // The exact class first, then any gear of the same role, then a value-matched
                        // fallback that stays in category: a picked-over market still arms the man in kind
                        // from what it has. See UpgradeSupply.FindKitOrAnyWarGear. A miss holds for the
                        // later men too (stock and budget only shrink), so the slot is left to the next.
                        int index = kitStock.FindKitOrAnyWarGear(slot.ItemType, need,
                            AffordablePrice(need, budget - drawn));
                        if (index < 0)
                        {
                            missing += need * (count - man);
                            break;
                        }
                        DrawFromStock(market, kitStock, index, need, ref drawn, ref surplus);
                        taken++;
                    }
                }
            }
            else
            {
                // The troop declares no battle equipment to walk, so there is no class to match: fall
                // back to one generic in-band item per man, as the upgrade draw does in the same spot.
                wanted = count;
                int need = MathF.Max(1, (int)(perManValue * valueShare));
                for (int man = 0; man < count; man++)
                {
                    int index = kitStock.FindKitInStock(need, AffordablePrice(need, budget - drawn));
                    if (index < 0)
                    {
                        missing += need * (count - man);
                        break;
                    }
                    DrawFromStock(market, kitStock, index, need, ref drawn, ref surplus);
                    taken++;
                }
            }

            // The part of the pieces' price above their need goes back to the citizens whose shelves they
            // came off, as coin, instead of vanishing with the piece.
            int returned = 0;
            if (surplus > 0 && SettlementWealth.HasCitizenPurse(market))
            {
                returned = SettlementWealth.CreditCitizens(market, surplus, SettlementWealth.Source.ArmsSurplus);
            }

            // The buying fief pays the town's merchants the FULL kit value it set out to spend, whatever the
            // market could actually supply -- the payment is the budget (already capped at the purse), not
            // the lesser sum the draw happened to find. A picked-clean market still costs the fief the full
            // arming: the coin goes to the town that brokered it and any stock it lacked is sourced
            // off-screen. Money fief → town citizens, near-mirroring the goods that went town → fief. A town
            // arming its own sons falls through and pays none here.
            int paid = 0;
            if (remoteBuyerPays && budget > 0)
            {
                paid = SettlementWealth.Debit(raisedAt, budget, armsSource);
                if (paid > 0)
                {
                    SettlementWealth.CreditCitizens(market, paid, armsSource);
                }
            }
            else if (chargeOwnTown && raisedAt == market && drawn > 0 && SettlementWealth.HasCitizenPurse(market))
            {
                // A town arming its own volunteers off its own shelves: its citizens front the value of the
                // kit that left, recovered when a lord musters the man -- RegisterRecruitPay credits the
                // recruit price back to those same citizens. Decoupled like the two legs it sits between:
                // gear now, coin if and when someone comes for him, so a town that arms more men than its
                // lords collect carries the cost of the difference, as it should.
                SettlementWealth.DebitCitizens(market, drawn, SettlementWealth.Source.TownArms);
            }

            // The slots the shelves could not fill are found off-screen, unpaid. Counted slot by slot (see
            // missing above) and reported, not charged.
            int shortfall = missing;

            if (SpoilsLog.IsEnabled && taken > 0)
            {
                SpoilsLog.Log("RECRUIT", (raisedAt != null ? raisedAt.Name.ToString() : "?") + " raised "
                    + count + "x " + SpoilsLog.Describe(character) + "; armed from " + market.Name
                    + " with " + taken + "/" + wanted + " item(s) worth " + drawn + "d of " + fullBudget
                    + "d kit" + (remoteBuyerPays ? ", paid " + paid + "d" : "")
                    + (taken < wanted ? " — market short " + (wanted - taken) : "")
                    + (returned > 0 ? "; " + returned + "d over need paid back to its citizens" : "")
                    + (shortfall > 0 ? "; " + shortfall + "d missing found off-screen" : ""));
            }
            return new KitDraw(fullBudget, drawn, paid, shortfall, returned);
        }

        /// <summary>What one <see cref="DrawKitFromMarket"/> call moved, for callers that report it.</summary>
        public struct KitDraw
        {
            /// <summary>The whole kit's worth for the men raised.</summary>
            public readonly int KitValue;
            /// <summary>The worth of the gear taken off the market's shelves, each piece counted up to its slot's need.</summary>
            public readonly int Drawn;
            /// <summary>Coin a village or castle paid the market town for it.</summary>
            public readonly int Paid;
            /// <summary>The part of the kit the shelves could not supply, found off-screen unpaid.</summary>
            public readonly int Shortfall;
            /// <summary>The price of pieces above their slot's need, paid back to the market's citizens.</summary>
            public readonly int Returned;

            public KitDraw(int kitValue, int drawn, int paid, int shortfall, int returned)
            {
                KitValue = kitValue;
                Drawn = drawn;
                Paid = paid;
                Shortfall = shortfall;
                Returned = returned;
            }
        }

        /// <summary>
        /// Puts <paramref name="count"/> disbanded men's kit back on <paramref name="market"/>'s shelves -- the
        /// inverse of a town arming its militia free off its own market (<see cref="DrawKitFromMarket"/> with
        /// chargeOwnTown false). Each man hands back the cheap end of his troop's own kit, up to
        /// <paramref name="valueShare"/> of its value, mirroring the draw. No money moves. Returns the worth
        /// of the gear put back.
        /// </summary>
        public static int ReturnKitToMarket(Settlement market, CharacterObject character, int count, float valueShare = 1f)
        {
            if (market == null || market.ItemRoster == null || character == null || character.IsHero || count <= 0)
            {
                return 0;
            }
            List<EquipmentElement> kit = SpoilsPool.GetKitElements(character);
            if (kit.Count == 0)
            {
                return 0;
            }
            kit.Sort((a, b) => a.ItemValue.CompareTo(b.ItemValue));
            int perManBudget = (int)(KitValue(character) * valueShare);
            int returned = 0;
            for (int man = 0; man < count; man++)
            {
                int budget = perManBudget;
                foreach (EquipmentElement element in kit)
                {
                    if (element.ItemValue > budget)
                    {
                        break;
                    }
                    market.ItemRoster.AddToCounts(element, 1);
                    budget -= element.ItemValue;
                    returned += element.ItemValue;
                }
            }
            return returned;
        }

        /// <summary>
        /// The dearest piece a slot needing <paramref name="need"/> can take with <paramref name="remaining"/>
        /// of the kit budget left. A piece dearer than the need counts only the need, so with a whole need
        /// left any piece up to the band's top (double the need) fits; with less, only a piece the rest
        /// covers in full.
        /// </summary>
        private static int AffordablePrice(int need, int remaining)
        {
            return remaining >= need ? need * 2 : MathF.Max(0, remaining);
        }

        /// <summary>
        /// Takes one piece off the stall for a slot needing <paramref name="need"/>: up to the need is
        /// counted into <paramref name="drawn"/>, anything above it into <paramref name="surplus"/>. The
        /// caller has already checked the budget covers it (<see cref="AffordablePrice"/>).
        /// </summary>
        /// <remarks>
        /// Valued through <see cref="TroopMarketFeedback.UnitPrice"/> rather than off the item's base
        /// value, so a town stripped of mail values what it has left the way it would sell it -- the same
        /// price the search ranked it by. No money moves here: the figures meter how much gear the man has
        /// taken and drive the demand signal; the caller settles the coin.
        /// </remarks>
        private static void DrawFromStock(Settlement market, UpgradeSupply.KitStock stock, int index, int need,
            ref int drawn, ref int surplus)
        {
            // Priced (at the market's price, as the search ranked it) and removed from the stall in one step.
            int value;
            ItemObject item = stock.TakeOne(index, out value);
            if (item == null)
            {
                return;
            }
            // A price signal, not a payment: the town restocks what its recruits keep walking off with.
            if (market.Town != null && item.ItemCategory != null)
            {
                RBMTownFoodSupply.RegisterPurchaseDemand(market.Town.MarketData, item.ItemCategory, value);
            }
            int counted = MathF.Min(value, need);
            drawn += counted;
            surplus += value - counted;
        }

        // ------------------------------------------------------------------ leg two: money, at recruitment

        /// <summary>
        /// Set while the player is turning prisoners into soldiers, which reaches us down the same event
        /// as a proper muster but is not one.
        /// </summary>
        private static bool _inPrisonerRecruitment;

        /// <summary>Whether the recruit event now firing is a prisoner joining the ranks, not a muster.</summary>
        public static bool InPrisonerRecruitment => _inPrisonerRecruitment;

        /// <summary>
        /// Keeps prisoner recruitment from paying a town for men it never raised. A prisoner talked round
        /// to the ranks costs conformity, not coin -- <c>RecruitPrisonersCampaignBehavior</c> moves no
        /// gold whatsoever -- yet the player's side of it announces itself through
        /// <c>OnUnitRecruited</c>, the same event a paid muster uses. Crediting a settlement there would
        /// hand it the price of a recruit nobody paid, which is precisely the minting this design exists
        /// to avoid.
        /// </summary>
        /// <remarks>
        /// The AI's side of prisoner recruitment needs no guard: it reports through
        /// <c>OnTroopRecruited</c> with a null settlement, which is already turned away.
        ///
        /// A scoped flag rather than an inspection of the troop, because nothing about the character says
        /// how it was obtained -- the same CharacterObject arrives from the recruit screen. Cleared in a
        /// Finalizer so a throw inside the native method cannot leave it stuck on and silently switch the
        /// payment off for the rest of the session.
        /// </remarks>
        [HarmonyPatch(typeof(RecruitPrisonersCampaignBehavior))]
        [HarmonyPatch("OnMainPartyPrisonerRecruited")]
        private class SuppressPrisonerRecruitPay
        {
            private static void Prefix()
            {
                _inPrisonerRecruitment = true;
            }

            private static void Finalizer()
            {
                _inPrisonerRecruitment = false;
            }
        }

        /// <summary>
        /// Hands the recruit price over to the settlement the men were raised from -- a town its own
        /// treasury, a village its own purse. Nothing is drawn here and nothing is charged here: the gear
        /// went when they were raised, and vanilla billed <paramref name="payer"/> a moment ago and then
        /// destroyed the money, paying it to nobody. This redirects the payment already made, so charge
        /// and credit are the same number by construction and the ledger can neither mint nor burn.
        /// </summary>
        /// <remarks>
        /// Paid to the VILLAGE, not the town it trades with, deliberately. A village bought its recruits'
        /// kit off that town at muster (the gear leg, <see cref="DrawKitFromMarket"/>); reimbursing the
        /// village here -- rather than the town a second time -- is what lets the village turn a profit on
        /// the men it raises and arms, instead of the town being paid twice over for one set of gear. A
        /// town serving its own recruits is both raiser and market, so its money lands in its treasury as
        /// before.
        /// </remarks>
        public static void PayRecruitPrice(Settlement recruitedAt, PartyBase buyer, Hero payer, CharacterObject character, int count)
        {
            if (!IsEnabled || character == null || character.IsHero || count <= 0)
            {
                return;
            }
            // No buyer, no payment. Covers the paths that reach the recruit events with no hero behind
            // them, and prisoners talked round to the ranks, who cost their captor nothing at all.
            if (payer == null || _inPrisonerRecruitment)
            {
                return;
            }
            if (recruitedAt == null || !(recruitedAt.IsVillage || recruitedAt.IsTown))
            {
                return;
            }
            int price = RecruitPricePaid(character, payer) * count;
            if (price <= 0)
            {
                return;
            }
            TroopMarketFeedback.RegisterRecruitPay(recruitedAt, price);

            if (SpoilsLog.IsEnabled)
            {
                SpoilsLog.Log("RECRUIT", buyer, SpoilsLog.Describe(buyer) + " recruited " + count + "x "
                    + SpoilsLog.Describe(character) + "; paid " + price + "d to " + recruitedAt.Name);
            }
        }

        /// <summary>
        /// What one man of this troop cost <paramref name="payer"/> to recruit -- the same model call,
        /// with the same buyer, that both recruit paths bill through, so the figure here is the figure
        /// charged rather than a reconstruction of it.
        /// </summary>
        private static int RecruitPricePaid(CharacterObject character, Hero payer)
        {
            PartyWageModel model = Campaign.Current != null ? Campaign.Current.Models.PartyWageModel : null;
            if (model == null)
            {
                return 0;
            }
            return model.GetTroopRecruitmentCost(character, payer).RoundedResultNumber;
        }
    }
}
