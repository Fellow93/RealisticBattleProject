using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace RBMCampaign
{
    /// <summary>
    /// Tells the player what RBM Campaign changes for his role (adventurer, mercenary, vassal, ruler) the
    /// first time he is on the map in that role. Polled on the map tick rather than hooked to the many
    /// events that change a role, so it never opens during character creation or over a conversation.
    /// </summary>
    public class RBMPlayerRoleNoticeBehavior : CampaignBehaviorBase
    {
        // Set here, not in OnSessionLaunched: SyncData runs before it on load.
        private int _lastRole = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("RBM_playerRoleNotice", ref _lastRole);
        }

        private void OnTick(float dt)
        {
            Clan clan = Clan.PlayerClan;
            if (clan == null || !(Game.Current?.GameStateManager?.ActiveState is MapState)
                || Campaign.Current.ConversationManager.IsConversationInProgress)
            {
                return;
            }
            Kingdom kingdom = clan.Kingdom;
            int role = kingdom == null ? 0 : clan.IsUnderMercenaryService ? 1 : kingdom.RulingClan == clan ? 3 : 2;
            if (role == _lastRole)
            {
                return;
            }
            _lastRole = role;
            Show(role);
        }

        private static void Show(int role)
        {
            bool spoils = SpoilsPool.IsEnabled;
            bool maintenance = spoils && RBMConfig.RBMConfig.troopMaintenanceFraction > 0f;
            bool menPayUpkeep = maintenance && RBMConfig.RBMConfig.independentMaintenancePurseFraction >= 1f;
            bool recruit = RecruitSupply.IsEnabled;
            List<TextObject> lines = new List<TextObject>();
            TextObject title;
            switch (role)
            {
                case 0:
                    title = new TextObject("{=RBM_CAMP_ROLE_000}RBM Campaign: you are an adventurer");
                    if (recruit)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_001}Recruits cost a bounty of five days' wages plus 10%. You do not pay for their gear."));
                    }
                    if (menPayUpkeep)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_002}Your men pay for the upkeep of their gear from their own spoils. You cover only what they cannot."));
                    }
                    lines.Add(new TextObject("{=RBM_CAMP_ROLE_003}No one pays you a stipend. You live off loot, trade and quests."));
                    break;
                case 1:
                    title = new TextObject("{=RBM_CAMP_ROLE_010}RBM Campaign: you are a mercenary");
                    lines.Add(new TextObject("{=RBM_CAMP_ROLE_011}Your employer pays you a daily stipend that grows with your influence and renown. Your field troops draw double wages, and your employer repays all of it while his treasury allows."));
                    if (spoils)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_012}Your men bank twice their wage into spoils."));
                    }
                    if (spoils && RBMConfig.RBMConfig.troopLeaderSpoilsCutFraction > 0f)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_013}Your own cut of the loot is 1.5 times larger."));
                    }
                    if (menPayUpkeep)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_014}Your men still pay for the upkeep of their gear from their own spoils. You cover only what they cannot."));
                    }
                    if (recruit)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_015}Recruits cost their full gear, a bounty of five days' wages and 10% on top."));
                    }
                    break;
                default:
                    bool ruler = role == 3;
                    title = ruler
                        ? new TextObject("{=RBM_CAMP_ROLE_030}RBM Campaign: you are a ruler")
                        : new TextObject("{=RBM_CAMP_ROLE_020}RBM Campaign: you are a vassal");
                    if (recruit)
                    {
                        lines.Add(ruler
                            ? new TextObject("{=RBM_CAMP_ROLE_031}Recruits are free in every fief of your realm.")
                            : new TextObject("{=RBM_CAMP_ROLE_021}Recruits are free in your own fiefs. Elsewhere in your realm you pay for their gear and a bounty of five days' wages; outside it, 10% more."));
                    }
                    if (maintenance)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_022}You pay the whole upkeep of your men's gear from your own gold. Their spoils no longer cover it."));
                    }
                    if (MercenaryCompanies.IsEnabled)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_023}When you gather an army you can hire mercenary companies from the army screen."));
                    }
                    if (ruler)
                    {
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_032}You receive a fifth of the coin minted from silver in your realm's towns."));
                        lines.Add(new TextObject("{=RBM_CAMP_ROLE_033}You pay the stipends and the doubled troop wages of the mercenary clans your kingdom employs."));
                    }
                    break;
            }
            if (lines.Count == 0)
            {
                return;
            }
            InformationManager.ShowInquiry(new InquiryData(title.ToString(),
                string.Join("\n\n", lines.ConvertAll(l => l.ToString())), true, false, "OK", null, null, null), true, true);
        }
    }
}
