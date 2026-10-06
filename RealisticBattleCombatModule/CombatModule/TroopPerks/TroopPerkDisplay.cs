using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.TwoDimension;

namespace RBMCombat
{
    /// <summary>
    /// Shows a regular troop's perks (RBMConfig.TroopPerks) to the player: a "Perks" block in the troop tooltip
    /// (recruit screen, encyclopedia troop tree and list, RBM's party-screen troop hover) and one perk icon per perk
    /// beside the tier and type icons on the troop's encyclopedia page. Lives in RBMCombat with the battle effect,
    /// so it shows exactly when the perks apply: rbmCombatEnabled (this module is only patched when it is on) and
    /// troopPerksEnabled. Text only in the tooltip: perk icons live in a sprite category that is not loaded on most
    /// screens the tooltip appears on.
    /// </summary>
    public static class TroopPerkDisplay
    {
        private const string PerkSpritePrefix = "SPPerks\\";
        private const string PerkSpriteFallback = "SPPerks\\locked_fallback";

        /// <summary>
        /// The perk's effect as it applies to a troop: only its personal-role half (a troop is never a captain,
        /// leader or governor), falling back to the whole description for a perk with no personal effect.
        /// </summary>
        private static string GetPersonalDescription(PerkObject perk)
        {
            bool primary = perk.PrimaryRole == PartyRole.Personal && !TextObject.IsNullOrEmpty(perk.PrimaryDescription);
            bool secondary = perk.SecondaryRole == PartyRole.Personal && !TextObject.IsNullOrEmpty(perk.SecondaryDescription);
            if (primary && secondary)
            {
                return perk.PrimaryDescription.ToString() + "\n" + perk.SecondaryDescription.ToString();
            }
            if (primary)
            {
                return perk.PrimaryDescription.ToString();
            }
            if (secondary)
            {
                return perk.SecondaryDescription.ToString();
            }
            return perk.Description?.ToString() ?? "";
        }

        private static List<PerkObject> GetShownPerks(CharacterObject character)
        {
            if (character == null || character.IsHero || !RBMConfig.TroopPerks.IsActive || !RBMConfig.TroopPerks.HasAny)
            {
                return null;
            }
            List<PerkObject> perks = RBMConfig.TroopPerks.GetOrdered(character);
            return (perks != null && perks.Count > 0) ? perks : null;
        }

        /// <summary>
        /// Appends the "Perks" block after vanilla's "Skills" list. RBMCampaign also postfixes this method:
        /// MaintenanceTroopTooltipLine inserts by index above the skills (unaffected by order), and RecruitCostHint
        /// APPENDS its recruit-cost section. High priority runs this postfix first, so the perk block always sits
        /// between the skills and the recruit cost.
        /// </summary>
        [HarmonyPatch(typeof(TooltipRefresherCollection), nameof(TooltipRefresherCollection.RefreshCharacterTooltip))]
        private class CharacterTooltipPerksPatch
        {
            [HarmonyPriority(Priority.High)]
            private static void Postfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
            {
                try
                {
                    if (propertyBasedTooltipVM == null || args == null || args.Length == 0)
                    {
                        return;
                    }
                    List<PerkObject> perks = GetShownPerks(args[0] as CharacterObject);
                    if (perks == null)
                    {
                        return;
                    }
                    // Shaped like vanilla's skills block above it: blank row, header, rundown separator, rows.
                    propertyBasedTooltipVM.AddProperty("", "");
                    propertyBasedTooltipVM.AddProperty("", new TextObject("{=RBM_TROOP_PERKS_001}Perks").ToString());
                    propertyBasedTooltipVM.AddProperty("", "", 0, TooltipProperty.TooltipPropertyFlags.RundownSeperator);
                    bool extended = propertyBasedTooltipVM.IsExtended;
                    foreach (PerkObject perk in perks)
                    {
                        string skill = perk.Skill?.Name?.ToString() ?? "";
                        propertyBasedTooltipVM.AddProperty(skill, perk.Name.ToString());
                        if (extended)
                        {
                            string description = GetPersonalDescription(perk);
                            if (!string.IsNullOrEmpty(description))
                            {
                                propertyBasedTooltipVM.AddProperty("", description, 0, TooltipProperty.TooltipPropertyFlags.MultiLine);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Print("[RBM] Troop perks: tooltip failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Applies EncyclopediaUnitPagePerksPatch, but only once a game is live (a no-op before). Not attribute-patched
        /// on purpose: RefreshValues reads the static field CampaignUIHelper.SkillObjectComparerInstance, and compiling
        /// the patched copy at module load runs CampaignUIHelper's static initializer there and then -- which calls
        /// GameTexts.FindText, null before a game exists, so it throws, the runtime caches the failure, and every later
        /// use of CampaignUIHelper (first RBMCampaignPatcher's RunClassConstructor) rethrows it. Same reason as
        /// RBMCampaignPatcher's held-back types. ApplyHarmonyPatches re-runs on OnGameStart, which is when this lands.
        /// </summary>
        public static void ApplyDeferred(Harmony harmony)
        {
            if (Game.Current == null)
            {
                return;
            }
            harmony.Patch(AccessTools.Method(typeof(EncyclopediaUnitPageVM), nameof(EncyclopediaUnitPageVM.RefreshValues)),
                postfix: new HarmonyMethod(typeof(EncyclopediaUnitPagePerksPatch), "Postfix"));
        }

        /// <summary>
        /// Adds one perk icon per perk to the unit page's PropertiesList, the icon row under the troop's name that
        /// already holds the tier and type icons; hovering one shows the perk's name and effect. RefreshValues
        /// rebuilds that list from scratch (it Clear()s it first), so appending here never duplicates.
        /// Applied by hand through ApplyDeferred, never by PatchAll -- see there.
        /// </summary>
        private class EncyclopediaUnitPagePerksPatch
        {
            private static void Postfix(EncyclopediaUnitPageVM __instance, CharacterObject ____character)
            {
                try
                {
                    List<PerkObject> perks = GetShownPerks(____character);
                    if (perks == null || __instance.PropertiesList == null)
                    {
                        return;
                    }
                    foreach (PerkObject perk in perks)
                    {
                        string sprite = GetLoadedPerkSprite(perk);
                        string hint = perk.Name.ToString();
                        string description = GetPersonalDescription(perk);
                        if (!string.IsNullOrEmpty(description))
                        {
                            hint += "\n" + description;
                        }
                        __instance.PropertiesList.Add(new StringItemWithHintVM(sprite, new TextObject("{=!}" + hint)));
                    }
                }
                catch (Exception e)
                {
                    Debug.Print("[RBM] Troop perks: encyclopedia page failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// The perk's icon (SPPerks\StringId, or the generic locked icon if it has none), with its sprite category
        /// loaded. Perk icons live in ui_characterdeveloper (War Sails perks in ui_naval_character_developer),
        /// which only the character developer screen loads, and an unloaded sprite simply draws nothing. The
        /// category is loaded here and never unloaded: SpriteCategory has no reference count, so unloading it would
        /// blank the character developer screen if it were open, and the character developer screen unloads it
        /// itself when it closes.
        /// </summary>
        private static string GetLoadedPerkSprite(PerkObject perk)
        {
            SpriteData spriteData = UIResourceManager.SpriteData;
            if (spriteData == null)
            {
                return PerkSpriteFallback;
            }
            string name = PerkSpritePrefix + perk.StringId;
            Sprite sprite = spriteData.GetSprite(name);
            if (sprite == null)
            {
                name = PerkSpriteFallback;
                sprite = spriteData.GetSprite(name);
            }
            SpriteCategory category = (sprite as SpriteGeneric)?.SpritePart?.Category;
            if (category != null && !category.IsLoaded && UIResourceManager.ResourceContext != null && UIResourceManager.ResourceDepot != null)
            {
                category.Load(UIResourceManager.ResourceContext, UIResourceManager.ResourceDepot);
            }
            return name;
        }
    }
}
