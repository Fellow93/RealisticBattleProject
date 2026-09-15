using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace RBMCampaign
{
    /// <summary>
    /// What becomes of a stack's purse when its men leave the party alive rather than by dying or
    /// upgrading: a march into a garrison, a handover to a companion's party, a dismissal. The purse is
    /// keyed to a party, so without this its coin is stranded under the old party's name, confiscated by
    /// whatever men of the same troop stayed behind or, in a party that never dies, orphaned for good.
    /// </summary>
    public static partial class SpoilsPool
    {
        /// <summary>
        /// Moves the leaving men's share of a stack's purse from one party to another when they are
        /// transferred across, the mirror between parties of what <see cref="CarrySpoilsOnUpgrade"/> does
        /// between troop names within one. Call once the roster has already moved the men: the size the
        /// share is measured against is what the source holds now plus the <paramref name="count"/> that
        /// left. A whole stack marching off carries its whole purse. Returns what was carried, for logging.
        /// </summary>
        public static int TransferSpoils(PartyBase from, PartyBase to, CharacterObject character, int count)
        {
            if (from == null || to == null || from == to || character == null || character.IsHero || count <= 0)
            {
                return 0;
            }
            int stackSizeBefore = GetStackSize(from, character) + count;
            int carried = GetCarriedSpoils(GetSpoils(from, character), count, stackSizeBefore);
            if (carried > 0)
            {
                AddSpoils(from, character, -carried);
                AddSpoils(to, character, carried);
            }
            // The men carry the rations they marched off with, wherever they marched to.
            TroopUpkeep.TransferFedState(from, to, character);
            return carried;
        }

        /// <summary>
        /// Drops purse entries for stacks that have left <paramref name="party"/> by some path that did not
        /// route through an upgrade or a party-screen transfer -- a dismissal, a donation, an AI handover.
        /// Cleanup elsewhere is opportunistic (an upgrade of the same stack, or the party being destroyed),
        /// and the main party is never destroyed, so without a sweep its orphaned purses would never be
        /// collected. Spoils left by a stack that is simply gone die with it, as in
        /// <see cref="ClearSpoilsIfStackGone"/>.
        /// </summary>
        public static void PruneOrphans(PartyBase party)
        {
            if (party == null || party.MemberRoster == null)
            {
                TroopUpkeep.PruneOrphans(party);
                return;
            }
            // Index-driven: only this party's own keys can be orphaned, so walk them, not the whole pool.
            if (_partyKeys.TryGetValue(party.Id, out HashSet<string> keys) && keys.Count > 0)
            {
                string prefix = party.Id + "#";
                List<string> orphans = null;
                foreach (string key in keys)
                {
                    // The key is party.Id + "#" + character.StringId, so the tail past the separator is the id.
                    string charId = key.Substring(prefix.Length);
                    CharacterObject character = MBObjectManager.Instance.GetObject<CharacterObject>(charId);
                    if (character == null || party.MemberRoster.FindIndexOfTroop(character) < 0)
                    {
                        (orphans ?? (orphans = new List<string>())).Add(key);
                    }
                }
                if (orphans != null)
                {
                    // Remove after the walk above -- IndexRemove mutates the same set we were iterating.
                    foreach (string key in orphans)
                    {
                        _spoils.Remove(key);
                        IndexRemove(key);
                    }
                    SpoilsLog.Log("POOL", party, "pruned " + orphans.Count + " orphaned spoils entr"
                        + (orphans.Count == 1 ? "y" : "ies") + " from " + SpoilsLog.Describe(party));
                }
            }
            TroopUpkeep.PruneOrphans(party);
        }
    }
}
