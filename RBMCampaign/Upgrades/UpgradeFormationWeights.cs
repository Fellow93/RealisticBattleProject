using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RBMCampaign
{
    /// <summary>
    /// Decides WHICH troop type the AI upgrades into at a fork (a troop with 2+ UpgradeTargets).
    ///
    /// Vanilla's DefaultPartyTroopUpgradeModel.GetUpgradeChanceForTroopUpgrade hands back 9999 for exactly
    /// one target and 1 for the rest, so the "weighted-random" draw the selector runs is effectively a fixed
    /// pick -- either the lord's birth-RNG PreferredUpgradeFormation or, failing that, a hash of the party/troop
    /// ids. Every lord therefore funnels a given recruit line down one frozen branch for the whole campaign.
    ///
    /// This replaces that number with a genuine per-category weight, so the same selector becomes a real
    /// weighted RNG. Weights come from the TROOP's culture (a Khuzait line leans horse-archer whoever leads it),
    /// are reshaped for garrisons (archers up, cavalry down -- defenders, not a stable), nudged by the leader's
    /// personality traits, and given a light lean toward his PreferredUpgradeFormation. The result is culturally
    /// coherent variety: thirty Khuzait lords field thirty different-but-recognisably-Khuzait rosters.
    ///
    /// Only AI auto-upgrade selection calls this model method; the player's manual party-screen upgrades do not,
    /// so the player is untouched. It composes with the spoils/cost system, which only READS this result.
    /// </summary>
    public static class UpgradeFormationWeights
    {
        // The seven weight buckets. Shock, crossbow and sling are their own categories, not modifiers over
        // melee/ranged foot -- a culture that does not list one gets 1 for it (sling is the exception: a flat
        // low value, below). Shock/crossbow/sling still count as their base arm (infantry / ranged foot) for
        // the garrison modifier and the personality-trait nudges.
        private const int MeleeFoot = 0;
        private const int Shock = 1;
        private const int RangedFoot = 2;
        private const int Crossbow = 3;
        private const int Sling = 4;
        private const int Cavalry = 5;
        private const int HorseArcher = 6;
        private const int CategoryCount = 7;

        // A branch is never made impossible: even a 0.75-vs-4 fork keeps a small chance of the underdog.
        private const float MinWeight = 0.05f;

        // Slingers are a rare skirmisher wherever they appear (only the Empire imperial_infantryman fork, in the
        // base cultures). A flat low weight, the same for every culture, before the garrison x2 and traits.
        private const float SlingWeight = 0.25f;

        // The lord's PreferredUpgradeFormation is kept but demoted from vanilla's 9999 lock to a light additive
        // lean toward the matching arm -- personality, not destiny.
        private const float PreferenceBonus = 0.25f;

        // culture StringId -> the seven category weights, in the index order above. A culture not in the table
        // (bandits, darshi, minor factions) resolves to all-1 (sling still 0.25), i.e. a uniform draw.
        //                                                    mf    shock  rf    xbow  sling        cav   ha
        private static readonly Dictionary<string, float[]> _cultureWeights = new Dictionary<string, float[]>
        {
            { "khuzait",  new[] { 1f,   1f,   2f,   1f,   SlingWeight, 3f,   4f } },
            { "vlandia",  new[] { 2f,   1f,   1f,   1f,   SlingWeight, 3f,   1f } },
            { "empire",   new[] { 1.5f, 1f,   1f,   0.75f, SlingWeight, 3f,  2f } },
            { "battania", new[] { 1f,   1.5f, 2f,   1f,   SlingWeight, 0.75f, 1f } },
            { "sturgia",  new[] { 2f,   1.5f, 1f,   1f,   SlingWeight, 3f,   3f } },
            { "nord",     new[] { 2f,   1.5f, 1f,   1f,   SlingWeight, 1f,   1f } },
            { "aserai",   new[] { 1f,   1.5f, 1.5f, 1f,   SlingWeight, 2f,   2f } },
        };

        /// <summary>
        /// The relative weight for upgrading <paramref name="troop"/> into <paramref name="target"/> in
        /// <paramref name="party"/>. Fed straight into the selector's totalChance-weighted draw, so only the
        /// ratios between a fork's targets matter, not the absolute scale.
        /// </summary>
        public static float ComputeWeight(PartyBase party, CharacterObject troop, CharacterObject target)
        {
            int category = Classify(target);
            string culture = troop?.Culture?.StringId;
            float weight = CultureWeight(culture, category);

            bool garrison = party?.MobileParty != null && party.MobileParty.IsGarrison;
            if (garrison)
            {
                weight *= GarrisonModifier(category);
            }

            // A garrison has no leader, so neither trait nor preference nudges apply -- it upgrades on culture
            // and its defensive modifier alone. Bandit/caravan parties with no hero fall through here too.
            Hero lord = party?.LeaderHero;
            if (lord != null)
            {
                weight += TraitDelta(lord, category);
                weight += PreferenceBonusFor(lord, category);
            }

            if (weight < MinWeight)
            {
                weight = MinWeight;
            }

            if (SpoilsLog.IsEnabled && troop != null && target != null)
            {
                // Keyed per troop->target pair: the model is queried on every party-screen refresh and every AI
                // pass, but the weight is pure over the pair (+ the lord's fixed traits), so once is plenty.
                SpoilsLog.LogOnce("upgw-" + troop.StringId + "-" + target.StringId, "UPGW", party,
                    SpoilsLog.Describe(troop) + " -> " + SpoilsLog.Describe(target)
                    + " | " + (culture ?? "?") + "/" + CategoryName(category) + (garrison ? "/garrison" : "")
                    + " weight " + weight.ToString("0.00") + " in " + SpoilsLog.Describe(party));
            }

            return weight;
        }

        // --- classification -------------------------------------------------------------------------------

        /// <summary>
        /// Which of the seven categories <paramref name="target"/> belongs to. Built on RBM's single arm
        /// classifier (SimulationEquipmentPower.ArmOf) so selection and the rest of the campaign never disagree
        /// about what a man is, then split shock/crossbow/sling off their base arm by weapon.
        /// </summary>
        private static int Classify(CharacterObject target)
        {
            int arm = SimulationEquipmentPower.ArmOf(target);

            // A foot troop whose ONLY upgrades are mounted is a pre-mount cavalry recruit (e.g. vlandian_spearman
            // -> vlandian_light_cavalry): it fights on foot now but the branch exists to make horsemen, so weight
            // it as the arm it becomes. Troops that are already mounted (khuzait_tribal_warrior, battanian_scout
            // carry a horse) resolve straight to Cavalry/HorseArcher above and never reach here.
            if (arm == SimulationEquipmentPower.InfantryType || arm == SimulationEquipmentPower.ArcherType)
            {
                int mounted = PreMountArm(target);
                if (mounted >= 0)
                {
                    arm = mounted;
                }
            }

            if (arm == SimulationEquipmentPower.CavalryType)
            {
                return Cavalry;
            }
            if (arm == SimulationEquipmentPower.HorseArcherType)
            {
                return HorseArcher;
            }

            ScanWeapons(target, out bool hasCrossbow, out bool hasSling, out bool hasTwoHandedMelee);
            if (arm == SimulationEquipmentPower.ArcherType)
            {
                if (hasCrossbow)
                {
                    return Crossbow;
                }
                if (hasSling)
                {
                    return Sling;
                }
                return RangedFoot;
            }

            // Infantry: a two-handed melee weapon (greatsword/great-axe/maul, or a menavlion/voulge/pike --
            // all TwoHandedPolearm) marks the shock foot; spear-and-shield and javelin skirmishers stay melee foot.
            return hasTwoHandedMelee ? Shock : MeleeFoot;
        }

        /// <summary>
        /// If every one of <paramref name="target"/>'s own upgrade targets is mounted, the arm they become
        /// (Cavalry or HorseArcher); otherwise -1. Depth one -- the known pre-mount recruits become cavalry in a
        /// single step -- and deliberately strict (ALL children mounted), so a genuine foot troop that merely has
        /// a cavalry option deeper in a fork (e.g. vlandian_billman) is left as the foot troop it is.
        /// </summary>
        private static int PreMountArm(CharacterObject target)
        {
            CharacterObject[] ups = target?.UpgradeTargets;
            if (ups == null || ups.Length == 0)
            {
                return -1;
            }
            int arm = -1;
            foreach (CharacterObject up in ups)
            {
                if (up == null || !up.IsMounted)
                {
                    return -1;
                }
                if (arm < 0)
                {
                    arm = SimulationEquipmentPower.ArmOf(up);
                }
            }
            return arm;
        }

        private static void ScanWeapons(CharacterObject troop, out bool hasCrossbow, out bool hasSling, out bool hasTwoHandedMelee)
        {
            hasCrossbow = false;
            hasSling = false;
            hasTwoHandedMelee = false;
            if (troop == null)
            {
                return;
            }
            foreach (Equipment set in Equipments(troop))
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    ItemObject item = set[i].Item;
                    if (item == null)
                    {
                        continue;
                    }
                    if (item.ItemType == ItemObject.ItemTypeEnum.Sling)
                    {
                        hasSling = true;
                    }
                    WeaponComponentData weapon = item.PrimaryWeapon;
                    if (weapon == null)
                    {
                        continue;
                    }
                    switch (weapon.WeaponClass)
                    {
                        case WeaponClass.Crossbow:
                            hasCrossbow = true;
                            break;
                        case WeaponClass.TwoHandedSword:
                        case WeaponClass.TwoHandedAxe:
                        case WeaponClass.TwoHandedMace:
                        case WeaponClass.TwoHandedPolearm:
                            hasTwoHandedMelee = true;
                            break;
                    }
                }
            }
        }

        // Mirrors SimulationEquipmentPower.EnumerateBattleEquipments (which is private): every battle roster the
        // troop can spawn with, so a weapon that only appears in a secondary roster is still seen.
        private static IEnumerable<Equipment> Equipments(CharacterObject troop)
        {
            bool any = false;
            if (troop.BattleEquipments != null)
            {
                foreach (Equipment equipment in troop.BattleEquipments)
                {
                    if (equipment != null)
                    {
                        any = true;
                        yield return equipment;
                    }
                }
            }
            if (!any)
            {
                Equipment fallback = troop.FirstBattleEquipment ?? troop.Equipment;
                if (fallback != null)
                {
                    yield return fallback;
                }
            }
        }

        // --- weight tables --------------------------------------------------------------------------------

        private static float CultureWeight(string culture, int category)
        {
            if (culture != null && _cultureWeights.TryGetValue(culture, out float[] weights))
            {
                return weights[category];
            }
            // Unlisted culture: uniform, save the flat sling floor.
            return (category == Sling) ? SlingWeight : 1f;
        }

        // Garrisons want shooters on the wall, not horsemen they cannot use: archers/crossbows/slings doubled,
        // cavalry halved, everything else unchanged. Keyed off the base arm, so shock counts as melee foot (x1)
        // and crossbow/sling as ranged foot (x2).
        private static float GarrisonModifier(int category)
        {
            switch (category)
            {
                case RangedFoot:
                case Crossbow:
                case Sling:
                    return 2f;
                case Cavalry:
                    return 0.5f;
                default:
                    return 1f; // MeleeFoot, Shock, HorseArcher
            }
        }

        // --- personality ----------------------------------------------------------------------------------

        /// <summary>The additive nudge the leader's personality gives this category (0 if none apply).</summary>
        private static float TraitDelta(Hero lord, int category)
        {
            float delta = 0f;
            bool rangedFoot = category == RangedFoot || category == Crossbow || category == Sling;
            bool meleeInfantry = category == MeleeFoot || category == Shock;

            // Calculating -> ranged foot (a shrewd commander invests in shooters).
            if (rangedFoot)
            {
                delta += TraitStep(lord, DefaultTraits.Calculating, 0.5f, 1.0f, -0.33f, -0.5f);
            }
            // Generosity -> cavalry & horse-archer (he can afford the horses).
            if (category == Cavalry || category == HorseArcher)
            {
                delta += TraitStep(lord, DefaultTraits.Generosity, 0.25f, 0.5f, -0.2f, -0.33f);
            }
            // Honor -> melee cavalry & melee infantry (he closes and fights fair).
            if (category == Cavalry || meleeInfantry)
            {
                delta += TraitStep(lord, DefaultTraits.Honor, 0.25f, 0.5f, -0.2f, -0.33f);
            }
            // Mercy -> melee foot.
            if (meleeInfantry)
            {
                delta += TraitStep(lord, DefaultTraits.Mercy, 0.5f, 1.0f, -0.33f, -0.5f);
            }
            // Valor is asymmetric: the brave lean into shock cavalry, the timid into horse-archer kiting.
            int valor = lord.GetTraitLevel(DefaultTraits.Valor);
            if (category == Cavalry && valor > 0)
            {
                delta += (valor >= 2) ? 1.0f : 0.5f;
            }
            else if (category == HorseArcher && valor < 0)
            {
                delta += (valor <= -2) ? 1.0f : 0.5f;
            }

            return delta;
        }

        // The +1 / +2 / -1 / -2 delta table for one trait, read off the lord's -2..+2 level.
        private static float TraitStep(Hero lord, TraitObject trait, float plus1, float plus2, float minus1, float minus2)
        {
            int level = lord.GetTraitLevel(trait);
            if (level >= 2)
            {
                return plus2;
            }
            if (level == 1)
            {
                return plus1;
            }
            if (level == -1)
            {
                return minus1;
            }
            if (level <= -2)
            {
                return minus2;
            }
            return 0f;
        }

        private static float PreferenceBonusFor(Hero lord, int category)
        {
            FormationClass pref = lord.PreferredUpgradeFormation;
            if (pref == FormationClass.NumberOfAllFormations)
            {
                return 0f;
            }
            int preferredArm = PreferenceToArm(pref);
            return (preferredArm >= 0 && preferredArm == CategoryArm(category)) ? PreferenceBonus : 0f;
        }

        // The base arm (infantry / ranged foot / cavalry / horse-archer) a category belongs to, for matching
        // against a lord's preference and for the garrison modifier's intent.
        private static int CategoryArm(int category)
        {
            switch (category)
            {
                case Cavalry:
                    return SimulationEquipmentPower.CavalryType;
                case HorseArcher:
                    return SimulationEquipmentPower.HorseArcherType;
                case RangedFoot:
                case Crossbow:
                case Sling:
                    return SimulationEquipmentPower.ArcherType;
                default:
                    return SimulationEquipmentPower.InfantryType; // MeleeFoot, Shock
            }
        }

        private static int PreferenceToArm(FormationClass formation)
        {
            switch (formation)
            {
                case FormationClass.Infantry:
                    return SimulationEquipmentPower.InfantryType;
                case FormationClass.Ranged:
                    return SimulationEquipmentPower.ArcherType;
                case FormationClass.Cavalry:
                    return SimulationEquipmentPower.CavalryType;
                case FormationClass.HorseArcher:
                    return SimulationEquipmentPower.HorseArcherType;
                default:
                    return -1;
            }
        }

        private static string CategoryName(int category)
        {
            switch (category)
            {
                case MeleeFoot: return "melee-foot";
                case Shock: return "shock";
                case RangedFoot: return "ranged-foot";
                case Crossbow: return "crossbow";
                case Sling: return "sling";
                case Cavalry: return "cavalry";
                case HorseArcher: return "horse-archer";
                default: return "?";
            }
        }

        // --- patch ----------------------------------------------------------------------------------------

        /// <summary>
        /// Replaces the vanilla chance (1 / 9999) with the culture+personality weight. Returning false skips
        /// the original entirely; the selector (both RBM's SpoilsUpgradePatches.SelectPossibleUpgrade and
        /// vanilla's) does a totalChance-weighted random draw over these, so proportional weights make it a
        /// true weighted RNG. Patching a second method on DefaultPartyTroopUpgradeModel is safe: RBM already
        /// patches GetGoldCostForUpgrade on it at module load with no cctor trouble.
        /// </summary>
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel))]
        [HarmonyPatch("GetUpgradeChanceForTroopUpgrade")]
        private class OverrideGetUpgradeChanceForTroopUpgrade
        {
            private static bool Prefix(PartyBase party, CharacterObject troop, int upgradeTargetIndex, ref float __result)
            {
                CharacterObject[] targets = troop?.UpgradeTargets;
                int count = (targets != null) ? targets.Length : 0;
                // Single-target upgrades carry no choice, so the weight is irrelevant -- match vanilla's 1f and
                // skip the work. Also guards a bad index.
                if (count <= 1 || upgradeTargetIndex < 0 || upgradeTargetIndex >= count)
                {
                    __result = 1f;
                    return false;
                }
                __result = ComputeWeight(party, troop, targets[upgradeTargetIndex]);
                return false;
            }
        }
    }
}
