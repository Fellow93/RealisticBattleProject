using HarmonyLib;
using Helpers;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBMCampaign
{
    /// <summary>
    /// EVERY SOLDIER HAS HIT POINTS NOW, not just the lords.
    ///
    /// Vanilla gives a line trooper no health at all. His life is one coin-flip per blow:
    ///
    ///     else if (MBRandom.RandomInt(_selectedSimulationTroop.MaxHitPoints()) &lt; damage)
    ///
    /// Eight damage against a hundred hit points is not eight points off a bar -- it is an eight per cent chance the
    /// man is simply gone, and a ninety-two per cent chance the blow never happened. Nothing accumulates. A veteran
    /// in plate who has been hacked at for twenty rounds is as fresh as the moment he arrived, and a recruit's lucky
    /// swing can kill a champion outright. Only a HERO gets a real pool (AddHeroDamage), and he gets it four lines
    /// higher up in the same method.
    ///
    /// The game does know who each man is: MapEventSide keeps a UniqueTroopDescriptor for the soldier it has
    /// selected. So a pool is possible, and this keeps one.
    ///
    /// THE TRICK, and the reason this is forty lines instead of a reimplementation: the roll is not replaced, it is
    /// BENT. RandomInt(maxHitPoints) returns 0..maxHitPoints-1, so rewriting `damage` in a prefix makes the outcome
    /// certain in either direction --
    ///
    ///     still standing  -&gt; damage = 0             -&gt; RandomInt(max) &lt; 0    is never true, and he lives
    ///     worn through    -&gt; damage = maxHitPoints  -&gt; RandomInt(max) &lt; max  is always true, and he falls
    ///
    /// -- and everything downstream then runs exactly as it always did: the surgeon's survival roll deciding
    /// whether he is dead or merely wounded, the BattleObserver, the casualty books, the player's kill event.
    /// None of it is reimplemented, so none of it can drift.
    ///
    /// The XP survives too, and that is luck rather than design: MapEvent awards it from its OWN copy of the damage
    /// (`ApplySimulatedHitRewardToSelectedTroop(..., num, flag)`), and a ref prefix only rewrites the callee's. Had
    /// the reward been taken from this method's parameter, zeroing it would have silently stopped every soldier in
    /// Calradia from learning anything in an auto-resolved battle.
    ///
    /// WHAT IT CHANGES IN PLAY: the MEAN is untouched -- the expected number of blows to kill a man is maxHP/damage
    /// either way. What collapses is the VARIANCE. Men now die in the order they are worn down; no recruit fluke-kills
    /// a champion, and twenty grazes finally add up to a corpse instead of twenty separate near-misses. Battles get
    /// less swingy and the better army wins more reliably -- and every part of the equipment model bites harder,
    /// because armour that halves a blow now genuinely doubles a man's life instead of halving a lottery ticket.
    /// </summary>
    [HarmonyPatch(typeof(MapEventSide), "ApplySimulationDamageToSelectedTroop")]
    internal static class SimulationTroopHitPoints
    {
        private static readonly AccessTools.FieldRef<MapEventSide, CharacterObject> SelectedTroop =
            AccessTools.FieldRefAccess<MapEventSide, CharacterObject>("_selectedSimulationTroop");

        private static readonly AccessTools.FieldRef<MapEventSide, UniqueTroopDescriptor> SelectedDescriptor =
            AccessTools.FieldRefAccess<MapEventSide, UniqueTroopDescriptor>("_selectedSimulationTroopDescriptor");

        /// <summary>
        /// What each man has taken so far, kept per battle so it can be dropped with the battle. Several fights run
        /// at once across the map, and a campaign that never forgot its wounded would carry every man it ever
        /// scratched.
        /// </summary>
        private static readonly Dictionary<MapEvent, Dictionary<UniqueTroopDescriptor, float>> _wounds =
            new Dictionary<MapEvent, Dictionary<UniqueTroopDescriptor, float>>();

        /// <summary>What the man who was just struck has left. Read by the log, which no longer has to guess.</summary>
        internal static float LastHitPointsLeft = -1f;

        /// <summary>
        /// What a man can take, WITH HIS COMMANDER COUNTED IN.
        ///
        /// A map battle gives every soldier in Calradia a flat hundred hit points, and that is the whole of it.
        /// DefaultCharacterStatsModel.MaxHitpoints starts at 100 and then adds perks through
        /// AddPerkBonusForCharacter(perk, CHARACTER, ...) -- which asks the SOLDIER whether he has the perk, and a
        /// soldier has no perks. So the entire list is dead for everyone but a hero. Your lord can have spent forty
        /// years learning how to keep men alive and it does not add a single point to one of them.
        ///
        /// It is not that the perks do not exist. They do, and they are substantial -- they simply only ever fire
        /// in a MISSION, in SandboxAgentStatCalculateModel, where a soldier is an Agent and his party is known:
        ///
        ///     TwoHanded.ThickHides          +5   to all troops
        ///     Polearm.HardyFrontline        +5   to all troops       (its PRIMARY bonus)
        ///     Crossbow.PickedShots          +5   to ranged troops
        ///     Athletics.WellBuilt           +5   to troops on foot
        ///     Polearm.HardKnock             +3   to troops on foot
        ///     OneHanded.UnwaveringDefense   +10  to INFANTRY on foot
        ///     Medicine.MinisterOfHealth     scales with the leader's MEDICINE SKILL above the epic threshold
        ///
        /// So a well-led infantry line can be carrying +28 and a doctor-lord's men a great deal more -- and none of
        /// it has ever reached an auto-resolved battle. Fight the battle yourself and your perks matter; press the
        /// button and they evaporate. That is the bug.
        ///
        /// What follows is a TRANSCRIPTION of that mission block, not an approximation of it: the same perks, the
        /// same primary/secondary slots, the same conditions (at-sea, mounted, ranged, infantry), and the same
        /// PerkHelper.AddPerkBonusForParty call -- which asks MobileParty.HasPerk, and so consults the party's
        /// leader AND its role-holders. Nothing is invented. `agent.HasMount` becomes troop.IsMounted, which is the
        /// same question asked of a man who has no Agent.
        /// </summary>
        internal static int MaxHitPoints(CharacterObject troop, PartyBase party)
        {
            if (troop == null)
            {
                return 100;
            }

            // The base, and every perk the man himself owns. For a hero that is already the whole story -- his own
            // Personal bonuses are in there, and native adds nothing else to him.
            ExplainedNumber bonuses = new ExplainedNumber(troop.MaxHitPoints());
            if (troop.IsHero)
            {
                return MathF.Round(bonuses.ResultNumber);
            }

            MobileParty mobileParty = (party != null) ? party.MobileParty : null;
            if (mobileParty == null || mobileParty.LeaderHero == null)
            {
                return MathF.Round(bonuses.ResultNumber);
            }

            // --- SandboxAgentStatCalculateModel, transcribed. ---

            if (!mobileParty.IsCurrentlyAtSea)
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.TwoHanded.ThickHides, mobileParty, false, ref bonuses);
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.HardyFrontline, mobileParty, true, ref bonuses);
            }

            if (troop.IsRanged)
            {
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Crossbow.PickedShots, mobileParty, false, ref bonuses);
            }

            // A man on a horse gets none of these: they are for the men standing in the line.
            if (!troop.IsMounted)
            {
                if (!mobileParty.IsCurrentlyAtSea)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.Athletics.WellBuilt, mobileParty, false, ref bonuses);
                }

                PerkHelper.AddPerkBonusForParty(DefaultPerks.Polearm.HardKnock, mobileParty, false, ref bonuses);

                if (!mobileParty.IsCurrentlyAtSea && troop.IsInfantry)
                {
                    PerkHelper.AddPerkBonusForParty(DefaultPerks.OneHanded.UnwaveringDefense, mobileParty, false, ref bonuses);
                }
            }

            // And the lord's own doctoring. This one is not a flat bonus at all -- it is his MEDICINE SKILL, every
            // point of it above the threshold at which epic perks begin to pay.
            CharacterObject leader = mobileParty.LeaderHero.CharacterObject;
            if (leader != null && leader.GetPerkValue(DefaultPerks.Medicine.MinisterOfHealth))
            {
                int epicThreshold = Campaign.Current.Models.CharacterDevelopmentModel.MaxSkillRequiredForEpicPerkBonus;
                int skill = leader.GetSkillValue(DefaultSkills.Medicine);
                int bonus = (int)(MathF.Max(skill - epicThreshold, 0)
                    * DefaultPerks.Medicine.MinisterOfHealth.PrimaryBonus);
                if (bonus > 0)
                {
                    bonuses.Add(bonus);
                }
            }

            int hitPoints = MathF.Round(bonuses.ResultNumber);
            return (hitPoints > 1) ? hitPoints : 1;
        }

        private static void Prefix(MapEventSide __instance, ref int damage)
        {
            LastHitPointsLeft = -1f;

            if (__instance == null || damage <= 0)
            {
                return;
            }

            CharacterObject troop = SelectedTroop(__instance);
            if (troop == null || troop.IsHero)
            {
                // A hero already has a pool of his own, four lines below this in the original. Leave him to it.
                return;
            }

            UniqueTroopDescriptor selected = SelectedDescriptor(__instance);

            // His officers count. Note this only ever RAISES the pool above native's hundred, which is what keeps
            // the trick below sound: vanilla will roll RandomInt(nativeMax) and we hand it a damage of nativeMax or
            // more, so "always true" stays always true.
            int maxHitPoints = MaxHitPoints(troop, __instance.GetAllocatedTroopParty(selected));
            if (maxHitPoints <= 0)
            {
                return;
            }

            MapEvent battle = __instance.MapEvent;
            if (battle == null)
            {
                return;
            }

            Dictionary<UniqueTroopDescriptor, float> wounds;
            if (!_wounds.TryGetValue(battle, out wounds))
            {
                wounds = new Dictionary<UniqueTroopDescriptor, float>();
                _wounds[battle] = wounds;
            }

            UniqueTroopDescriptor id = selected;

            float taken;
            wounds.TryGetValue(id, out taken);
            taken += damage;

            if (taken < maxHitPoints)
            {
                // Still on his feet, and carrying it. The blow is real and it is remembered; it simply has not
                // finished him. Zeroing the damage makes vanilla's roll certain to spare him.
                wounds[id] = taken;
                LastHitPointsLeft = maxHitPoints - taken;
                damage = 0;
                return;
            }

            // Worn through. Vanilla decides from here whether he is dead or only wounded, and the surgeon has his
            // say -- exactly as before.
            wounds.Remove(id);
            LastHitPointsLeft = 0f;
            damage = maxHitPoints;
        }

        /// <summary>The battle is over; its wounded are the campaign's problem now, not ours.</summary>
        internal static void Forget(MapEvent battle)
        {
            if (battle != null)
            {
                _wounds.Remove(battle);
            }
        }
    }
}
