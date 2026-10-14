using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A mercenary company's contract with an army leader. While hired the company is a vanilla mercenary of
    /// the employer's kingdom (joined with no mercenary award and a stay-until far in the future, so vanilla
    /// neither pays it nor lets it drift off) and its party is a member of the employer's army. The contract
    /// lasts exactly as long as the party serves in an army of that kingdom: when the army disperses, the party
    /// is removed from it, or the employer stops paying, the company leaves the kingdom again -- except while its
    /// party is lost and its leader is a captive of the employer's enemy, when it stays, unpaid, until he is free
    /// (<see cref="HoldForCaptiveLeader"/>).
    ///
    /// Ending is reconciled, not event-driven: <see cref="Reconcile"/> runs hourly over every company and
    /// takes any company that is in a kingdom without a serving contract out of it -- but never while its
    /// party is in a battle or siege (a kingdom change there breaks the encounter's sides), so it waits for
    /// that to end. Reconciling, rather than acting inside <c>ArmyDispersed</c> / <c>OnPartyLeftArmy</c>, keeps
    /// the kingdom change out of the army's own dispersal loop and also catches every other way a company can
    /// end up in or out of a kingdom (vanilla kicking a mercenary, a kingdom destroyed, a contract made by
    /// something other than RBM). Those two events only note WHY the army ended (<see cref="NoteArmyEnded"/>),
    /// for the log.
    ///
    /// The money: the hire price and every day's pay are paid by the party that leads the army at that moment
    /// (its leader's gold, which under RBM is the party purse), topped up from that party's clan leader above a
    /// reserve; the player pays from his own gold. All of it goes to the company leader (conserved, no coin
    /// minted), and the company keeps it to pay its men and hire more.
    /// </summary>
    public static class MercenaryContracts
    {
        // AI army leaders hire only companies at least this full (vanilla's player call-to-army floor).
        private const float MinimumSizeRatio = 0.4f;
        // Gold an AI army leader's party and clan keep back, together, after paying a hire price. Low on
        // purpose ("scale to employer wealth"): a company costs 10 days of its full wage up front, and a lord
        // has to be able to spend most of what he and his clan hold to afford one.
        private const int AiGoldReserve = 5000;
        // Companies an AI army hires at most.
        private const int MaxPerAiArmy = 2;
        // How far an AI army leader reaches for a company, as a multiple of vanilla's range for calling lords into an
        // army (8x the neighbouring-town distance). Only companies get the longer reach; lords keep vanilla's.
        internal const float HireRangeMultiplier = 1.5f;

        // Persisted: company clan StringId -> "employerHeroId|kingdomId|dailyPay|startDay".
        private static Dictionary<string, string> _contracts = new Dictionary<string, string>();

        // Transient, for the log only: why a company's army ended (clan id -> note), which companies have
        // already logged a DEFER for the battle they are waiting out, and which a HOLD for their captive leader.
        private static readonly Dictionary<string, string> _armyEndNotes = new Dictionary<string, string>();
        private static readonly HashSet<string> _deferLogged = new HashSet<string>();
        private static readonly HashSet<string> _holdLogged = new HashSet<string>();

        private struct Contract
        {
            public string EmployerId;
            public string KingdomId;
            public int DailyPay;
            public int StartDay;
        }

        internal static void Reset()
        {
            _contracts = new Dictionary<string, string>();
            _armyEndNotes.Clear();
            _deferLogged.Clear();
            _holdLogged.Clear();
        }

        /// <summary>Drops a company's contract and notes without leaving any kingdom (the disband cleanup does that).</summary>
        internal static void Forget(Clan clan)
        {
            if (clan == null)
            {
                return;
            }
            _contracts.Remove(clan.StringId);
            ClearNotes(clan);
        }

        /// <summary>
        /// The contracts the player's party is paying today: a hired company whose army the main party leads,
        /// with the day's pay. Read by both passes of the player's clan finance (MercenaryCompanyPayLines), so
        /// the projection and the apply pass list the same companies.
        /// </summary>
        internal static List<(Clan clan, int pay)> PlayerPaidContracts()
        {
            List<(Clan, int)> list = new List<(Clan, int)>();
            if (Campaign.Current == null)
            {
                return list;
            }
            foreach (string clanId in _contracts.Keys)
            {
                Clan clan = Campaign.Current.CampaignObjectManager.Find<Clan>(clanId);
                Contract contract;
                if (clan == null || clan.IsEliminated || clan.Leader == null || !TryGet(clan, out contract) || contract.DailyPay <= 0)
                {
                    continue;
                }
                if (IsPaidByPlayer(MercenaryCompanies.ActiveParty(clan)))
                {
                    list.Add((clan, contract.DailyPay));
                }
            }
            return list;
        }

        /// <summary>Whether this company party's pay is the player's today (his main party leads its army).</summary>
        private static bool IsPaidByPlayer(MobileParty party)
        {
            return party?.Army?.LeaderParty != null && party.Army.LeaderParty.IsMainParty;
        }

        /// <summary>
        /// The player's finance pass found he cannot pay a company: the contract ends (logged); the company
        /// leaves the kingdom on the next hourly reconcile, outside the finance computation.
        /// </summary>
        internal static void EndUnpaidByPlayer(Clan clan, int pay, int playerGold)
        {
            Contract contract;
            if (clan == null || !TryGet(clan, out contract))
            {
                return;
            }
            string why = "army leader party and clan could not pay " + pay + "d (player has " + playerGold + "d)";
            _contracts.Remove(clan.StringId);
            _armyEndNotes[clan.StringId] = why;
            MercenaryLog.Log("END", clan, why + " (served " + (Today - contract.StartDay) + " days)");
        }

        /// <summary>Logs a day's pay the player's finance pass charged (see MercenaryCompanyPayLines).</summary>
        internal static void LogPlayerPay(Clan clan, int pay)
        {
            Contract contract;
            if (TryGet(clan, out contract))
            {
                MercenaryLog.Log("PAY", clan, pay + "d from the player's daily finances (clan gold change line), company gold "
                    + clan.Gold + "d, day " + (Today - contract.StartDay));
            }
        }

        /// <summary>Contracts still stored, for the disband cleanup's last step.</summary>
        internal static int Count
        {
            get { return _contracts.Count; }
        }

        internal static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_mercContracts", ref _contracts);
            if (_contracts == null)
            {
                _contracts = new Dictionary<string, string>();
            }
        }

        private static int Today
        {
            get { return (int)CampaignTime.Now.ToDays; }
        }

        private static bool TryGet(Clan clan, out Contract contract)
        {
            contract = default(Contract);
            return clan != null && TryGet(clan.StringId, out contract);
        }

        private static bool TryGet(string clanId, out Contract contract)
        {
            contract = default(Contract);
            string value;
            if (clanId == null || !_contracts.TryGetValue(clanId, out value) || value == null)
            {
                return false;
            }
            string[] parts = value.Split('|');
            if (parts.Length < 4)
            {
                return false;
            }
            contract.EmployerId = parts[0];
            contract.KingdomId = parts[1];
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out contract.DailyPay);
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out contract.StartDay);
            return true;
        }

        public static bool IsHired(Clan clan)
        {
            return clan != null && _contracts.ContainsKey(clan.StringId);
        }

        /// <summary>
        /// Records why a hired company's army ended, from <c>ArmyDispersed</c> / <c>OnPartyLeftArmy</c>, so the
        /// contract's END line can say it. Changes nothing else; the first note for a company wins.
        /// </summary>
        internal static void NoteArmyEnded(MobileParty party, string note)
        {
            Clan clan = party?.ActualClan;
            if (clan == null || !IsHired(clan) || !MercenaryCompanies.IsCompany(clan) || _armyEndNotes.ContainsKey(clan.StringId))
            {
                return;
            }
            _armyEndNotes[clan.StringId] = note;
        }

        public static bool CanBeHired(MobileParty party, Kingdom kingdom)
        {
            return WhyNotHireable(party, kingdom) == null;
        }

        /// <summary>
        /// Null when <paramref name="party"/> can be taken on by <paramref name="kingdom"/> right now (a company's
        /// own free party, led by a free leader, under no contract, not in a battle or siege, not at war with the
        /// kingdom); otherwise the reason it cannot, for the log.
        /// </summary>
        public static string WhyNotHireable(MobileParty party, Kingdom kingdom)
        {
            if (!MercenaryCompanies.IsCompanyParty(party) || !party.IsActive)
            {
                return "no active company party";
            }
            if (kingdom == null || kingdom.IsEliminated)
            {
                return "no kingdom";
            }
            Clan clan = party.ActualClan;
            Hero leader = party.LeaderHero;
            if (leader == null || !leader.IsAlive || leader.IsPrisoner || clan.Leader == null)
            {
                return "no free leader";
            }
            if (clan.Kingdom != null || IsHired(clan))
            {
                return "already serving " + (clan.Kingdom != null ? clan.Kingdom.Name.ToString() : "a contract");
            }
            if (party.Army != null)
            {
                return "in an army";
            }
            if (party.MapEvent != null || party.SiegeEvent != null || party.BesiegerCamp != null
                || (party.CurrentSettlement != null && party.CurrentSettlement.IsUnderSiege))
            {
                return "in a battle or siege";
            }
            if (FactionManager.IsAtWarAgainstFaction(clan, kingdom))
            {
                return "at war with " + kingdom.Name;
            }
            return null;
        }

        // ---- Who pays ----------------------------------------------------------------------------------
        // The army's leader PARTY pays (under RBM a lord party's purse is its leader hero's gold), topped up
        // from its clan -- the clan leader's gold above ClanLeaderReserve, the same floor RBM's clan purse
        // (ClanPartyPurse.LenderReserve) keeps when the clan lends to a member's party. When the party leader
        // IS the clan leader the party purse already is the clan's gold and there is no second purse. The
        // player pays from his own gold only (his clan's gold is his gold).

        /// <summary>
        /// The clan leader's gold an AI employer clan never pays out to a company, for the hire price and daily
        /// pay alike. Lower than ClanPartyPurse.LenderReserve (20,000) by the user's choice: an AI lord may spend
        /// his clan down to this to keep a company. (The player's clan is never drawn on: his gold is his own.)
        /// </summary>
        private const int ClanLeaderReserve = 5000;

        private static Hero PartyPurse(MobileParty payerParty)
        {
            if (payerParty == null)
            {
                return null;
            }
            return payerParty.IsMainParty ? Hero.MainHero : payerParty.LeaderHero;
        }

        /// <summary>The clan leader whose gold tops the party purse up, or null when there is no separate clan purse.</summary>
        private static Hero ClanPurse(Hero partyPurse)
        {
            Hero clanLeader = partyPurse?.Clan?.Leader;
            if (clanLeader == null || clanLeader == partyPurse || !clanLeader.IsAlive || partyPurse.Clan == Clan.PlayerClan)
            {
                return null;
            }
            return clanLeader;
        }

        private static int ClanSpare(Hero clanPurse)
        {
            return clanPurse == null ? 0 : Math.Max(0, clanPurse.Gold - ClanLeaderReserve);
        }

        /// <summary>What the paying party and its clan can put up together.</summary>
        public static int PayerFunds(MobileParty payerParty)
        {
            Hero partyPurse = PartyPurse(payerParty);
            return partyPurse == null ? 0 : Math.Max(0, partyPurse.Gold) + ClanSpare(ClanPurse(partyPurse));
        }

        /// <summary>
        /// Pays <paramref name="amount"/> to <paramref name="recipient"/>: the party purse first, then the clan's
        /// spare gold. Pays nothing and returns false when the two together fall short.
        /// </summary>
        private static bool TryPay(MobileParty payerParty, Hero recipient, int amount, out int fromParty, out int fromClan)
        {
            fromParty = 0;
            fromClan = 0;
            Hero partyPurse = PartyPurse(payerParty);
            if (partyPurse == null || amount < 0 || PayerFunds(payerParty) < amount)
            {
                return false;
            }
            Hero clanPurse = ClanPurse(partyPurse);
            fromParty = Math.Min(amount, Math.Max(0, partyPurse.Gold));
            fromClan = amount - fromParty;
            bool quiet = partyPurse != Hero.MainHero;
            if (fromParty > 0)
            {
                GiveGoldAction.ApplyBetweenCharacters(partyPurse, recipient, fromParty, disableNotification: quiet);
            }
            if (fromClan > 0 && clanPurse != null)
            {
                GiveGoldAction.ApplyBetweenCharacters(clanPurse, recipient, fromClan, disableNotification: true);
            }
            return true;
        }

        private static string PayerName(MobileParty payerParty)
        {
            Hero purse = PartyPurse(payerParty);
            return purse != null ? purse.Name + "'s party" : "?";
        }

        /// <summary>
        /// Hires the company: the hire price from <paramref name="payerParty"/> (the army leader's party, topped up
        /// from its clan) to the company leader, then the company joins <paramref name="kingdom"/> as its
        /// mercenary, then (when given) the party joins <paramref name="army"/>. The order matters: joining a
        /// kingdom halts the clan's parties, so the army is assigned after it. The player's army screen passes
        /// the main party and no army, and lets vanilla assign it.
        /// </summary>
        public static bool Hire(MobileParty party, MobileParty payerParty, Kingdom kingdom, Army army)
        {
            Hero employer = PartyPurse(payerParty);
            if (employer == null || !CanBeHired(party, kingdom))
            {
                return false;
            }
            int price = MercenaryCompanies.HirePrice(party);
            int pay = MercenaryCompanies.DailyPay(party);
            Clan clan = party.ActualClan;
            int fromParty, fromClan;
            if (clan.Leader == null || !TryPay(payerParty, clan.Leader, price, out fromParty, out fromClan))
            {
                return false;
            }
            // The player's one-off fee, for the RBM Ledger's Clan finances tab (ignored for anyone else).
            ClanEventGoldLedger.Record(employer, EventGoldKind.MercenaryCompanyHire, fromParty + fromClan);
            ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, kingdom, CampaignTime.YearsFromNow(100f), 0, showNotification: false);
            _contracts[clan.StringId] = employer.StringId + "|" + kingdom.StringId + "|"
                + pay.ToString(CultureInfo.InvariantCulture) + "|" + Today.ToString(CultureInfo.InvariantCulture);
            _armyEndNotes.Remove(clan.StringId);
            _deferLogged.Remove(clan.StringId);
            if (army != null)
            {
                party.Army = army;
            }
            bool byPlayer = employer == Hero.MainHero;
            Hero armyLeader = army?.LeaderParty?.LeaderHero ?? (byPlayer ? Hero.MainHero : null);
            MercenaryLog.Log("CONTRACT", party, (byPlayer ? "PLAYER" : "AI") + " hire by " + PayerName(payerParty)
                + " of " + kingdom.Name + ", army of " + (armyLeader != null ? armyLeader.Name.ToString() : "?")
                + " | hire " + price + "d (party " + fromParty + "d + clan " + fromClan + "d), daily " + pay
                + "d (wage " + party.TotalWage + "d)"
                + " | payer funds left " + PayerFunds(payerParty) + "d, company gold " + clan.Gold + "d"
                + " | " + party.MemberRoster.TotalManCount + " men");
            return true;
        }

        /// <summary>Ends the company's contract; it leaves the kingdom now, or as soon as its party is out of battle.</summary>
        public static void End(Clan clan, string why)
        {
            if (clan == null)
            {
                return;
            }
            Contract contract;
            if (TryGet(clan, out contract))
            {
                _contracts.Remove(clan.StringId);
                MercenaryLog.Log("END", clan, why + " (served " + (Today - contract.StartDay) + " days)");
            }
            // So the LEAVE line names the real reason rather than "without a contract".
            _armyEndNotes[clan.StringId] = why;
            Reconcile(clan);
        }

        /// <summary>
        /// The hourly pass over contracts whose company is no longer among the living ones (its leader died and
        /// vanilla destroyed the clan, which also took it out of the kingdom), so each gets its END line. The
        /// pass over living companies never sees them.
        /// </summary>
        internal static void ReconcileGone()
        {
            if (Campaign.Current == null || _contracts.Count == 0)
            {
                return;
            }
            foreach (string clanId in _contracts.Keys.ToList())
            {
                Clan clan = Campaign.Current.CampaignObjectManager.Find<Clan>(clanId);
                if (clan == null)
                {
                    DropMissing(clanId);
                }
                else if (clan.IsEliminated)
                {
                    Reconcile(clan);
                }
            }
        }

        /// <summary>A contract whose clan cannot be found any more: dropped, with its END line.</summary>
        private static void DropMissing(string clanId)
        {
            Contract contract;
            if (TryGet(clanId, out contract))
            {
                MercenaryLog.Log("END", clanId, "company no longer found (served " + (Today - contract.StartDay) + " days)");
            }
            _contracts.Remove(clanId);
            _armyEndNotes.Remove(clanId);
            _deferLogged.Remove(clanId);
            _holdLogged.Remove(clanId);
        }

        /// <summary>
        /// Keeps the company's kingdom membership and its contract in step: a company in a kingdom stays only
        /// while its party serves in an army of the kingdom it was hired by; otherwise it leaves (deferred while
        /// in a battle or siege). One exception: a company whose party was lost and whose leader is held captive
        /// by an enemy of its employer keeps its kingdom and contract (unpaid -- there is no party to pay) until
        /// he is free again, because leaving would make peace with his captor and so set him free for nothing.
        /// A company out of any kingdom holds no contract.
        /// </summary>
        public static void Reconcile(Clan clan)
        {
            if (clan == null)
            {
                return;
            }
            Contract contract;
            bool hasContract = TryGet(clan, out contract);
            if (clan.IsEliminated || !MercenaryCompanies.IsCompany(clan))
            {
                if (hasContract)
                {
                    _contracts.Remove(clan.StringId);
                    Kingdom employer = Campaign.Current?.CampaignObjectManager.Find<Kingdom>(contract.KingdomId);
                    MercenaryLog.Log("END", clan, "company destroyed"
                        + (clan.IsEliminated && (clan.Leader == null || !clan.Leader.IsAlive) ? " (leader dead)" : "")
                        + "; the game already took it out of " + (employer != null ? employer.Name.ToString() : contract.KingdomId)
                        + " (served " + (Today - contract.StartDay) + " days)");
                }
                ClearNotes(clan);
                return;
            }
            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null)
            {
                if (hasContract)
                {
                    _contracts.Remove(clan.StringId);
                    MercenaryLog.Log("END", clan, "removed from its kingdom by the game (kicked out or kingdom destroyed)"
                        + " (served " + (Today - contract.StartDay) + " days)");
                }
                ClearNotes(clan);
                return;
            }
            MobileParty party = MercenaryCompanies.ActiveParty(clan);
            string why = null;
            string note;
            if (!hasContract)
            {
                why = _armyEndNotes.TryGetValue(clan.StringId, out note) ? note : "in " + kingdom.Name + " without an RBM contract";
            }
            else if (contract.KingdomId != kingdom.StringId)
            {
                why = "moved to another kingdom";
            }
            else if (party == null)
            {
                if (HoldForCaptiveLeader(clan, kingdom))
                {
                    return;
                }
                why = _holdLogged.Contains(clan.StringId) ? "party lost, leader " + clan.Leader?.Name + " no longer held by an enemy"
                    : "party lost";
            }
            else if (party.Army == null)
            {
                why =_armyEndNotes.TryGetValue(clan.StringId, out note) ? note : "not in an army";
            }
            else if (party.Army.Kingdom != kingdom)
            {
                why = "in an army of another kingdom";
            }
            if (why == null)
            {
                return;
            }
            if (hasContract)
            {
                _contracts.Remove(clan.StringId);
                MercenaryLog.Log("END", clan, why + " (served " + (Today - contract.StartDay) + " days)");
            }
            if (party != null && (party.MapEvent != null || party.SiegeEvent != null || party.BesiegerCamp != null))
            {
                if (_deferLogged.Add(clan.StringId))
                {
                    MercenaryLog.Log("DEFER", party, "stays in " + kingdom.Name + " until its "
                        + (party.MapEvent != null ? "battle" : "siege") + " is over");
                }
                return;
            }
            ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, showNotification: false);
            MercenaryLog.Log("LEAVE", clan, "left " + kingdom.Name + " (" + why + ")"
                + (party != null ? ", party at " + Where(party) + ", " + party.MemberRoster.TotalManCount + " men" : ", no party")
                + ", gold " + clan.Gold + "d");
            ClearNotes(clan);
        }

        /// <summary>
        /// True while the hired company's party is lost and its leader is a prisoner of a faction at war with
        /// <paramref name="kingdom"/> (his captor: the party or settlement that holds him). The contract and the
        /// kingdom membership are then kept: ChangeKingdomAction's leave would make peace between the company
        /// and every enemy of the kingdom, and PrisonerReleaseCampaignBehavior frees a hero whose faction is not
        /// at war with his captor. Nothing is paid meanwhile (PayDaily and the player's pay lines pay only through
        /// a party in an army). Once he escapes, is ransomed or released by a peace, the normal "party lost" end
        /// applies; if he dies, vanilla destroys the clan and <see cref="ReconcileGone"/> writes the END.
        /// </summary>
        private static bool HoldForCaptiveLeader(Clan clan, Kingdom kingdom)
        {
            Hero leader = clan.Leader;
            IFaction captor = leader != null && leader.IsAlive && leader.IsPrisoner ? leader.PartyBelongedToAsPrisoner?.MapFaction : null;
            if (captor == null || !FactionManager.IsAtWarAgainstFaction(captor, kingdom))
            {
                return false;
            }
            if (_holdLogged.Add(clan.StringId))
            {
                MercenaryLog.Log("HOLD", clan, "party lost, leader " + leader.Name + " held by " + leader.PartyBelongedToAsPrisoner.Name
                    + " (" + captor.Name + ", at war with " + kingdom.Name + "): stays in " + kingdom.Name
                    + " under contract, unpaid, until he is free");
            }
            return true;
        }

        private static void ClearNotes(Clan clan)
        {
            _armyEndNotes.Remove(clan.StringId);
            _deferLogged.Remove(clan.StringId);
            _holdLogged.Remove(clan.StringId);
        }

        private static string Where(MobileParty party)
        {
            if (party.CurrentSettlement != null)
            {
                return party.CurrentSettlement.Name.ToString();
            }
            return "(" + party.Position.ToVec2().x.ToString("0") + "," + party.Position.ToVec2().y.ToString("0") + ")";
        }

        /// <summary>
        /// The day's pay, from whichever party leads the company's army TODAY (re-read every day, so a change of
        /// army leadership moves the bill with it), topped up from that party's clan. A party and clan that
        /// together cannot pay end the contract. A company out of its army is not paid; the hourly reconcile ends
        /// that contract.
        /// </summary>
        internal static void PayDaily()
        {
            foreach (string clanId in _contracts.Keys.ToList())
            {
                Clan clan = Campaign.Current.CampaignObjectManager.Find<Clan>(clanId);
                Contract contract;
                if (clan == null)
                {
                    DropMissing(clanId);
                    continue;
                }
                if (clan.IsEliminated)
                {
                    // Normally caught by the hourly ReconcileGone first; this writes the same END line.
                    Reconcile(clan);
                    continue;
                }
                if (!TryGet(clan, out contract))
                {
                    _contracts.Remove(clanId);
                    continue;
                }
                MobileParty party = MercenaryCompanies.ActiveParty(clan);
                MobileParty payerParty = party?.Army?.LeaderParty;
                if (payerParty == null || payerParty == party || clan.Leader == null || contract.DailyPay <= 0)
                {
                    continue;
                }
                if (payerParty.IsMainParty)
                {
                    // The player pays through his clan's daily finances instead (MercenaryCompanyPayLines), so
                    // the gold tooltip, the Finances tab and the Ledger all show it as a line.
                    continue;
                }
                int fromParty, fromClan;
                if (!TryPay(payerParty, clan.Leader, contract.DailyPay, out fromParty, out fromClan))
                {
                    End(clan, "army leader party and clan could not pay " + contract.DailyPay + "d ("
                        + PayerName(payerParty) + " has " + PartyPurse(payerParty)?.Gold + "d, clan spare "
                        + ClanSpare(ClanPurse(PartyPurse(payerParty))) + "d)");
                    continue;
                }
                MercenaryLog.Log("PAY", clan, contract.DailyPay + "d from " + PayerName(payerParty) + ": party " + fromParty
                    + "d + clan " + fromClan + "d (payer funds left " + PayerFunds(payerParty) + "d), company gold "
                    + clan.Gold + "d, day " + (Today - contract.StartDay));
            }
        }

        /// <summary>
        /// An AI army leader takes on the nearest idle companies he can afford, keeping
        /// <see cref="AiGoldReserve"/>, up to <see cref="MaxPerAiArmy"/>. Vanilla's army-member AI then marches
        /// them to him. The player's own armies are hired from the army management screen instead. Logs one
        /// AI-HIRE line per army: what it hired and why every other company was passed over.
        /// </summary>
        internal static void TryAiHire(Army army)
        {
            MobileParty leaderParty = army?.LeaderParty;
            Hero leader = leaderParty?.LeaderHero;
            Kingdom kingdom = army?.Kingdom;
            if (leaderParty != null && leaderParty.IsMainParty)
            {
                return; // the player's own army hires from the army management screen
            }
            string skip = army == null ? "no army"
                : leaderParty == null ? "army has no leader party"
                : leader == null ? "leader party has no leader hero"
                : kingdom == null ? "army has no kingdom (dispersed before the hiring hour)"
                : leader.Clan == Clan.PlayerClan ? "led by the player's clan"
                : !kingdom.Armies.Contains(army) ? "army no longer in " + kingdom.Name + "'s armies (dispersed before the hiring hour)"
                : !leaderParty.IsActive ? "leader party no longer active"
                : null;
            if (skip != null)
            {
                MercenaryLog.Log("AI-HIRE", "", "army of " + (leader != null ? leader.Name.ToString() : "?")
                    + (kingdom != null ? " (" + kingdom.Name + ")" : "") + " skipped: " + skip);
                return;
            }
            float maxDistance = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy
                * HireRangeMultiplier;
            List<string> rejected = new List<string>();
            List<(MobileParty party, float distance)> candidates = new List<(MobileParty, float)>();
            List<Clan> companies = MercenaryCompanies.LiveCompanies();
            foreach (Clan clan in companies)
            {
                MobileParty party = MercenaryCompanies.ActiveParty(clan);
                string why = party == null ? "no party" : WhyNotHireable(party, kingdom);
                if (why == null && party.PartySizeRatio < MinimumSizeRatio)
                {
                    why = "only " + (party.PartySizeRatio * 100f).ToString("0") + "% full";
                }
                float distance = 0f;
                if (why == null)
                {
                    distance = DistanceHelper.GetDistanceBetweenMobilePartyToMobileParty(party, leaderParty,
                        party.NavigationCapability, out float _);
                    if (!(distance < maxDistance))
                    {
                        why = "too far (" + distance.ToString("0") + " > " + maxDistance.ToString("0") + ")";
                    }
                }
                if (why != null)
                {
                    rejected.Add(MercenaryLog.Who(clan) + ": " + why);
                    continue;
                }
                candidates.Add((party, distance));
            }
            int hired = 0;
            List<string> hires = new List<string>();
            foreach ((MobileParty party, float distance) in candidates.OrderBy(c => c.distance))
            {
                int price = MercenaryCompanies.HirePrice(party);
                if (hired >= MaxPerAiArmy)
                {
                    rejected.Add(MercenaryLog.Who(party) + ": army already has " + MaxPerAiArmy);
                    continue;
                }
                int funds = PayerFunds(leaderParty);
                if (funds - price < AiGoldReserve)
                {
                    rejected.Add(MercenaryLog.Who(party) + ": costs " + price + "d, party+clan have " + funds
                        + "d and keep " + AiGoldReserve + "d");
                    continue;
                }
                if (Hire(party, leaderParty, kingdom, army))
                {
                    hired++;
                    hires.Add(MercenaryLog.Who(party) + " for " + price + "d at " + distance.ToString("0"));
                }
                else
                {
                    rejected.Add(MercenaryLog.Who(party) + ": hire failed");
                }
            }
            StringBuilder line = new StringBuilder();
            line.Append("army of ").Append(leader.Name).Append(" (").Append(kingdom.Name).Append(", ")
                .Append(army.ArmyType).Append(", party+clan funds ").Append(PayerFunds(leaderParty)).Append("d, ").Append(companies.Count).Append(" living companies): hired ").Append(hired);
            if (hires.Count > 0)
            {
                line.Append(" [").Append(string.Join("; ", hires)).Append("]");
            }
            if (rejected.Count > 0)
            {
                line.Append(" | passed over: ").Append(string.Join("; ", rejected));
            }
            MercenaryLog.Log("AI-HIRE", "", line.ToString());
        }
    }
}
