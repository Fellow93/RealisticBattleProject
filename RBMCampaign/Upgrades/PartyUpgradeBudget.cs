using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// A per-party ceiling on the GOLD a party may spend on troop upgrades in a day -- the non-spoils
    /// leg only. Spoils-funded upgrades (the leading men a stack's own purse covers for free) are never
    /// capped: this gates exactly the gold billed to the party's hero in <see cref="SpoilsUpgradePatches"/>,
    /// and nothing else.
    ///
    /// Mirrors the native party wage limit that sits beside it in the clan Parties panel. The default is
    /// unlimited; the player sets a finite cap and toggles it on through the slider + checkbox this module
    /// injects next to the wage control. The value is remembered across toggles so unchecking "unlimited"
    /// restores the last cap rather than a default. Enforcement lives in the RBM UpgradeReadyTroops
    /// override, so the cap only bites while the spoils/upgrade economy is on.
    ///
    /// Keyed by <see cref="PartyBase.Id"/>, the same identity <see cref="SpoilsPool"/> uses, so a saved
    /// cap follows its party.
    /// </summary>
    public static class PartyUpgradeBudget
    {
        // Slider bounds, shared with the widgets and enforced here so a hand-edited save cannot smuggle an
        // out-of-range cap past the UI.
        public const int MinCap = 0;
        public const int MaxCap = 100000;

        // Where the slider sits the first time a player unchecks "unlimited" on a party that never carried a
        // finite cap. A middle-ish figure, not the max, so unchecking does not instantly throttle the party.
        public const int DefaultCap = 10000;

        // partyId -> the finite daily gold cap the slider last held. Persisted, and kept even while the party
        // is unlimited so re-enabling the cap restores the value rather than a default. Absent -> DefaultCap.
        private static Dictionary<string, int> _capGold = new Dictionary<string, int>();

        // partyId -> 1 when the finite cap is enforced. Persisted. Absent -> unlimited, which is the default,
        // so a party the player never touched holds no entry in either dictionary and spends as vanilla.
        private static Dictionary<string, int> _capEnabled = new Dictionary<string, int>();

        // Gold already spent on upgrades today, per party. NOT persisted: a reload simply starts the day's
        // tally fresh, which at worst lets a party upgrade a little more the day a save is loaded -- cheaper
        // than threading a second serialized store through the save for a figure that resets every dawn.
        // Rolled over the moment the campaign day changes.
        private static readonly Dictionary<string, int> _spentToday = new Dictionary<string, int>();
        private static int _accumulatorDay = int.MinValue;

        /// <summary>
        /// Drops the previous campaign's caps before this one's save is read. Called from
        /// <see cref="RBMSpoilsCampaignBehavior"/>'s constructor, which runs ahead of the load -- the same
        /// reset ordering <see cref="SpoilsPool.Reset"/> relies on, so a real save repopulates and only a
        /// new campaign starts empty.
        /// </summary>
        public static void Reset()
        {
            _capGold.Clear();
            _capEnabled.Clear();
            _spentToday.Clear();
            _accumulatorDay = int.MinValue;
        }

        public static void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_partyUpgradeCapGold", ref _capGold);
            dataStore.SyncData("RBM_partyUpgradeCapEnabled", ref _capEnabled);
            if (_capGold == null)
            {
                _capGold = new Dictionary<string, int>();
            }
            if (_capEnabled == null)
            {
                _capEnabled = new Dictionary<string, int>();
            }
        }

        /// <summary>Forgets a destroyed party's cap so its id does not linger in the save for the game's life.</summary>
        public static void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party?.Party == null)
            {
                return;
            }
            string id = party.Party.Id;
            _capGold.Remove(id);
            _capEnabled.Remove(id);
            _spentToday.Remove(id);
        }

        // ---- Cap state (read/written by the clan-screen widgets) --------------------------------------

        /// <summary>True when the party has no enforced cap: it may spend gold on upgrades freely.</summary>
        public static bool IsUnlimited(PartyBase party)
        {
            return party == null || !_capEnabled.ContainsKey(party.Id);
        }

        /// <summary>The finite cap the slider should show for this party, whether or not it is enforced.</summary>
        public static int GetFiniteCap(PartyBase party)
        {
            int value;
            if (party != null && _capGold.TryGetValue(party.Id, out value))
            {
                return MathF.Max(MinCap, MathF.Min(MaxCap, value));
            }
            return DefaultCap;
        }

        /// <summary>Stores the slider's finite value. Does not by itself turn the cap on.</summary>
        public static void SetFiniteCap(PartyBase party, int value)
        {
            if (party == null)
            {
                return;
            }
            _capGold[party.Id] = MathF.Max(MinCap, MathF.Min(MaxCap, value));
        }

        /// <summary>Turns the cap on or off. Turning it on with no stored value seeds the default.</summary>
        public static void SetUnlimited(PartyBase party, bool unlimited)
        {
            if (party == null)
            {
                return;
            }
            if (unlimited)
            {
                _capEnabled.Remove(party.Id);
            }
            else
            {
                _capEnabled[party.Id] = 1;
                if (!_capGold.ContainsKey(party.Id))
                {
                    _capGold[party.Id] = DefaultCap;
                }
            }
        }

        // ---- Enforcement (read by the UpgradeReadyTroops override) ------------------------------------

        /// <summary>
        /// The gold a party may still spend on upgrades today: its daily cap less what it has already spent.
        /// <see cref="int.MaxValue"/> stands for "unlimited", so a caller can min() it against a real budget
        /// without a special case.
        /// </summary>
        public static int GetRemainingDailyBudget(PartyBase party)
        {
            if (IsUnlimited(party))
            {
                return int.MaxValue;
            }
            RollDayIfNeeded();
            int spent;
            _spentToday.TryGetValue(party.Id, out spent);
            return MathF.Max(0, GetFiniteCap(party) - spent);
        }

        /// <summary>Records gold spent on upgrades against today's tally for a capped party.</summary>
        public static void RecordDailySpend(PartyBase party, int gold)
        {
            if (party == null || gold <= 0 || IsUnlimited(party))
            {
                return;
            }
            RollDayIfNeeded();
            int spent;
            _spentToday.TryGetValue(party.Id, out spent);
            _spentToday[party.Id] = spent + gold;
        }

        // Clears the day's tallies the first time it is asked after the campaign day rolls over. Keyed off
        // whole campaign days so it survives an in-day save/reload within a session without an event hook.
        private static void RollDayIfNeeded()
        {
            if (Campaign.Current == null)
            {
                return;
            }
            int today = (int)CampaignTime.Now.ToDays;
            if (today != _accumulatorDay)
            {
                _spentToday.Clear();
                _accumulatorDay = today;
            }
        }
    }
}
