using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace RBMCampaign
{
    /// <summary>
    /// When a settlement is attacked -- a fortification assault or a village raid -- the volunteers
    /// waiting in it take up arms alongside the defenders instead of standing idle in the recruit pool.
    ///
    /// Every notable's available recruit slots are emptied into the settlement's own defending party:
    /// the garrison for a besieged town or castle, the militia for a raided village (and the militia as
    /// a fallback for the rare fortification with no garrison party). Because the real mission and the
    /// auto-resolve simulation both read the defender parties' member rosters when the fight is joined,
    /// adding the volunteers here reinforces both paths at once -- no agent-level injection is needed.
    ///
    /// The volunteer slots are consumed (mirroring vanilla's garrison auto-recruit), so repeated assaults
    /// across a single siege do not re-add the same men, and survivors simply remain with the garrison the
    /// townsfolk rallied to. Own-settlement notables only -- the people inside the walls, not the bound
    /// villages' volunteers, who defend their own homes when raided.
    /// </summary>
    public class RBMSettlementDefenseBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            if (mapEvent == null || (!mapEvent.IsSiegeAssault && !mapEvent.IsRaid))
            {
                return;
            }

            Settlement settlement = mapEvent.MapEventSettlement;
            if (settlement == null)
            {
                return;
            }

            MobileParty muster = GetDefenderMusterParty(settlement);
            if (muster == null)
            {
                return;
            }

            foreach (Hero notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive || notable.VolunteerTypes == null)
                {
                    continue;
                }

                for (int i = 0; i < notable.VolunteerTypes.Length; i++)
                {
                    CharacterObject volunteer = notable.VolunteerTypes[i];
                    if (volunteer != null)
                    {
                        muster.MemberRoster.AddToCounts(volunteer, 1);
                        notable.VolunteerTypes[i] = null;
                    }
                }
            }
        }

        /// <summary>
        /// The defending party the settlement's volunteers rally to: the garrison for a fortification
        /// (falling back to the militia only when no garrison party exists), the militia for a village.
        /// Both are already stationed on the event's defender side when it starts, so adding to their
        /// rosters counts toward the defense; a party not on the side is never returned.
        /// </summary>
        private static MobileParty GetDefenderMusterParty(Settlement settlement)
        {
            if (settlement.IsVillage)
            {
                return settlement.MilitiaPartyComponent?.MobileParty;
            }

            if (settlement.IsFortification)
            {
                MobileParty garrison = settlement.Town?.GarrisonParty;
                if (garrison != null)
                {
                    return garrison;
                }
                return settlement.MilitiaPartyComponent?.MobileParty;
            }

            return null;
        }
    }
}
