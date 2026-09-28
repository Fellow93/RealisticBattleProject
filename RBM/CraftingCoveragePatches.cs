using HarmonyLib;
using System;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RBM
{
    // RBM's weapon_descriptions XML is appended (see XmlLoadingPatches.MergeTwoXmlsPatch), so its copy of each
    // WeaponDescription deserializes last and REPLACES vanilla's AvailablePieces, while CraftingTemplate.Pieces only
    // ever accumulates. A piece that a game patch or DLC adds to a template after RBM's list was written therefore
    // stays selectable in smithing but belongs to no description: GenerateCraftedItem gives the item zero weapons,
    // the smithy's RefreshStats throws on Weapons.ElementAt(0), and XML crafted items using it can't attack or block
    // (e.g. mace_handle_25/26, battania_blade_7 before they were added to the XML).
    internal class CraftingCoveragePatches
    {
        private static readonly AccessTools.FieldRef<WeaponDescription, MBList<CraftingPiece>> AvailablePiecesRef =
            AccessTools.FieldRefAccess<WeaponDescription, MBList<CraftingPiece>>("_availablePieces");

        // Game.LoadBasicFiles loads CraftingPieces -> WeaponDescriptions -> CraftingTemplates and every caller
        // (Campaign new/load, CustomGame, NavalCustomGame, EditorGame) loads "Items" right after it. XML crafted items
        // build their weapons inside ItemObject.Deserialize, and player-crafted ones are rebuilt later by
        // CraftingCampaignBehavior, so healing here comes before both.
        [HarmonyPatch(typeof(Game))]
        [HarmonyPatch("LoadBasicFiles")]
        private class LoadBasicFilesPatch
        {
            private static void Postfix()
            {
                // Same gate as the XML that causes it: RBM's weapon descriptions only merge in with combat on.
                if (!RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    return;
                }
                try
                {
                    HealUncoveredPieces();
                }
                catch (Exception ex)
                {
                    Debug.Print($"[RBM] Error healing crafting piece coverage: {ex.Message}");
                }
            }
        }

        private static void HealUncoveredPieces()
        {
            int healed = 0;
            foreach (CraftingTemplate template in CraftingTemplate.All)
            {
                WeaponDescription[] descriptions = template.WeaponDescriptions;
                if (descriptions == null || descriptions.Length == 0 || template.Pieces == null)
                {
                    continue;
                }
                foreach (CraftingPiece piece in template.Pieces)
                {
                    // Crafting.Init never offers a piece whose type the template doesn't build.
                    if (piece == null || !piece.IsValid || template.BuildOrders.All(b => b.PieceType != piece.PieceType))
                    {
                        continue;
                    }
                    if (descriptions.Any(d => d.AvailablePieces != null && d.AvailablePieces.Contains(piece)))
                    {
                        continue;
                    }
                    WeaponDescription target = GetPrimaryDescription(template);
                    MBList<CraftingPiece> available = AvailablePiecesRef(target);
                    if (available == null)
                    {
                        available = new MBList<CraftingPiece>();
                        AvailablePiecesRef(target) = available;
                    }
                    available.Add(piece);
                    healed++;
                    Debug.Print($"[RBM] Crafting coverage: template {template.StringId} piece {piece.StringId} was in no weapon description, added to {target.StringId}");
                }
            }
            if (healed > 0)
            {
                Debug.Print($"[RBM] Crafting coverage: healed {healed} crafting piece(s)");
            }
        }

        // The description covering most of the template's pieces, not simply the first: TwoHandedSword,
        // TwoHandedPolearm and Dagger list a one-handed/alternative usage first, and healing a new two-handed piece
        // into that would hand it a one-handed usage and still leave it unusable with pieces the main usage owns.
        private static WeaponDescription GetPrimaryDescription(CraftingTemplate template)
        {
            WeaponDescription best = template.WeaponDescriptions[0];
            int bestCount = -1;
            foreach (WeaponDescription description in template.WeaponDescriptions)
            {
                int count = description.AvailablePieces == null ? 0 : template.Pieces.Count(p => description.AvailablePieces.Contains(p));
                if (count > bestCount)
                {
                    best = description;
                    bestCount = count;
                }
            }
            return best;
        }
    }
}
