using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace RBM
{
    /// <summary>
    /// Load-time repair for troop stacks whose CharacterObject the save loader never initialized.
    ///
    /// A save stores each CharacterObject in full and rebinds it to its XML definition on load only if it
    /// was registered when saved and no object with the same StringId got registered first
    /// (MBObjectManager.RegisterMBObjectWithoutInitialization skips duplicates silently). A copy that misses
    /// that rebind keeps the ctor-less load state: Name null, UpgradeTargets null. The next daily training
    /// tick then NREs in PartyBase.OnXpChanged, which reads UpgradeTargets.Length. Some other mod's party
    /// ("Food Supply") was seen carrying one; the save stays poisoned for every later load.
    ///
    /// Not an RBMCampaign feature (it guards vanilla code against foreign corruption), so it lives in the
    /// main module and runs whenever a campaign is loaded, whatever the campaign toggle.
    /// </summary>
    public class SaveRosterRepairBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private static void OnGameLoaded(CampaignGameStarter starter)
        {
            int repaired = 0;
            foreach (MobileParty party in Campaign.Current.MobileParties)
            {
                repaired += RepairParty(party?.Party);
            }
            // Settlement parties hold town/castle prison rosters; garrisons are MobileParties, covered above.
            foreach (Settlement settlement in Settlement.All)
            {
                repaired += RepairParty(settlement?.Party);
            }
            if (repaired > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "RBM: repaired " + repaired + " corrupted troop stack(s) in this save.", Colors.Yellow));
            }
        }

        private static int RepairParty(PartyBase party)
        {
            if (party == null)
            {
                return 0;
            }
            return RepairRoster(party, party.MemberRoster) + RepairRoster(party, party.PrisonRoster);
        }

        private static int RepairRoster(PartyBase party, TroopRoster roster)
        {
            if (roster == null)
            {
                return 0;
            }
            int repaired = 0;
            // Backwards: removing an element shifts the ones after it down.
            for (int i = roster.Count - 1; i >= 0; i--)
            {
                TroopRosterElement element = roster.GetElementCopyAtIndex(i);
                CharacterObject broken = element.Character;
                if (broken == null || broken.IsHero || broken.UpgradeTargets != null)
                {
                    continue;
                }

                // Remove first with xpChange 0, so nothing calls OnXpChanged on the broken object.
                roster.AddToCountsAtIndex(i, -element.Number, -element.WoundedNumber, 0);

                CharacterObject registered = MBObjectManager.Instance.GetObject<CharacterObject>(broken.StringId);
                bool remapped = registered != null && registered != broken && registered.UpgradeTargets != null;
                if (remapped)
                {
                    roster.AddToCounts(registered, element.Number, false, element.WoundedNumber, element.Xp);
                }
                Debug.Print("[RBM SaveRosterRepair] " + party.Name + ": stack '" + broken.StringId + "' x" + element.Number
                    + (remapped ? " rebound to the registered troop" : " removed (no valid troop with that id)"));
                repaired++;
            }
            return repaired;
        }
    }
}
