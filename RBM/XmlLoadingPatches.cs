using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CraftingSystem;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace RBM
{
    internal class XmlLoadingPatches
    {
        [HarmonyPatch(typeof(Module))]
        [HarmonyPatch("CreateProcessedItemHolstersXMLForNative")]
        private class CreateProcessedItemHolstersXMLForNativePatch
        {
            private static void Postfix(ref string __result)
            {
                try
                {
                    // Find all active modules that have a RBMCombat_WS_item_holsters.xml
                    var activeModules = ModuleHelper.GetModules()
                        .Where(m => ModuleHelper.IsModuleActive(m.Id));

                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(__result);
                    XmlNode holstersRoot = doc.SelectSingleNode("//item_holsters");
                    if (holstersRoot == null) return;

                    bool modified = false;
                    foreach (var module in activeModules)
                    {
                        string filePath = Path.Combine(
                            ModuleHelper.GetModuleFullPath(module.Id),
                            "ModuleData",
                            "RBMCombat_item_holsters.xml");

                        if (!File.Exists(filePath)) continue;

                        XmlDocument holsterDoc = new XmlDocument();
                        holsterDoc.Load(filePath);
                        XmlNodeList newHolsters = holsterDoc.SelectNodes("//item_holster");
                        if (newHolsters == null) continue;

                        foreach (XmlNode holster in newHolsters)
                        {
                            string id = holster.Attributes?["id"]?.Value;
                            if (string.IsNullOrEmpty(id)) continue;
                            // Skip if already present
                            if (doc.SelectSingleNode($"//item_holster[@id='{id}']") != null) continue;

                            XmlNode imported = doc.ImportNode(holster, true);
                            holstersRoot.AppendChild(imported);
                            modified = true;
                        }
                    }

                    if (modified)
                        __result = doc.OuterXml;
                }
                catch (Exception ex)
                {
                    TaleWorlds.Library.Debug.Print($"[RBM] Error injecting item holsters: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(CraftingOrder))]
        [HarmonyPatch("InitializeCraftingOrderOnLoad")]
        public class CraftingPatch
        {
            private static Exception Finalizer(CraftingOrder __instance)
            {
                if (__instance.PreCraftedWeaponDesignItem == null)
                {
                    __instance.PreCraftedWeaponDesignItem = DefaultItems.Trash;
                }
                return null;
            }
        }

        // A crafted weapon saved with RBMCombat's crafting pieces can't be rebuilt once those pieces are gone (combat
        // module disabled): vanilla GenerateCraftedItem returns null and CraftingCampaignBehavior.InitializeCraftedItemData
        // unregisters the item. Postfixes from other mods (BetterSmithingContinued's InitAsPlayerCraftedItem) dereference
        // that null and NRE the load. The finalizer wraps those postfixes too, so hand the null back to vanilla.
        [HarmonyPatch(typeof(TaleWorlds.Core.Crafting))]
        [HarmonyPatch("InitializePreCraftedWeaponOnLoad")]
        public class PreCraftedWeaponOnLoadPatch
        {
            private static Exception Finalizer(Exception __exception, TaleWorlds.Core.ItemObject __result)
            {
                if (__exception is NullReferenceException && __result == null)
                {
                    return null;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(MBObjectManager))]
        [HarmonyPatch("CreateMergedXmlFile")]
        private class CreateMergedXmlFilePatch
        {
            private static bool Prefix(List<Tuple<string, string>> toBeMerged, List<string> xsltList, bool skipValidation)
            {
                // XSLT transforms are applied straight by ApplyXslt and never pass through MergeTwoXmls, so the
                // RBM_COMBAT_XML_TAG gate below cannot see them. RBM_WS's RBMCombat_WS_*.xslt re-add the Nord pieces
                // that RBM's own weapon descriptions drop; with Combat off those descriptions never load, NavalDLC's
                // copy already added the pieces, and the second copy double-counts in Crafting.GenerateCraftedItem --
                // which is what made every Nord spear throwable. "" is native's own "no transform" entry.
                if (!RBMConfig.RBMConfig.rbmCombatEnabled && xsltList != null)
                {
                    for (int i = 0; i < xsltList.Count; i++)
                    {
                        string xslt = xsltList[i];
                        if (!string.IsNullOrEmpty(xslt) && Path.GetFileName(xslt).StartsWith("RBMCombat_", StringComparison.OrdinalIgnoreCase))
                        {
                            xsltList[i] = "";
                        }
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MBObjectManager))]
        [HarmonyPatch("MergeTwoXmls")]
        private class MergeTwoXmlsPatch
        {
            private static bool Prefix(ref XmlDocument xmlDocument1, ref XmlDocument xmlDocument2, string xsdPath, bool keepDuplicates, ref XmlDocument __result)
            {
                XDocument originalXml = MBObjectManager.ToXDocument(xmlDocument1);
                XDocument mergedXml = MBObjectManager.ToXDocument(xmlDocument2);
                var comments = mergedXml.DescendantNodes().OfType<XComment>();

                var isRbmXml = false;
                var isRbmCombatXml = false;
                var isRbmUnitOverhaulXml = false;
                var isRbmCampaignXml = false;
                // Requires BOTH combat and campaign (e.g. RBMEconomyCombat_ranged.xml).
                var isRbmEconomyCombatXml = false;
                // Requires combat WITHOUT campaign (e.g. RBMCombat_ranged.xml, superseded by the economy variant).
                var isRbmCombatOnlyXml = false;
                foreach (XComment comment in comments)
                {
                    if (comment.Value.Contains("RBM_XML_TAG"))
                    {
                        isRbmXml = true;
                    }
                    if (comment.Value.Contains("RBM_ECONOMY_COMBAT_XML_TAG"))
                    {
                        isRbmEconomyCombatXml = true;
                    }
                    if (comment.Value.Contains("RBM_COMBAT_ONLY_XML_TAG"))
                    {
                        isRbmCombatOnlyXml = true;
                    }
                    if (comment.Value.Contains("RBM_COMBAT_OVERHAUL_XML_TAG"))
                    {
                        isRbmUnitOverhaulXml = true;
                    }
                    if (comment.Value.Contains("RBM_CAMPAIGN_XML_TAG"))
                    {
                        isRbmCampaignXml = true;
                    }
                    else if (comment.Value.Contains("RBM_COMBAT_XML_TAG"))
                    {
                        isRbmCombatXml = true;
                    }
                }

                var isWSctive = ModuleHelper.IsModuleActive("NavalDLC");

                List<XElement> nodesToRemoveArray = new List<XElement>();
                if (!RBMConfig.RBMConfig.rbmCombatEnabled && isRbmCombatXml)
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }
                if (!RBMConfig.RBMConfig.rbmCampaignEnabled && isRbmCampaignXml)
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }
                if (isRbmEconomyCombatXml && !(RBMConfig.RBMConfig.rbmCombatEnabled && RBMConfig.RBMConfig.rbmCampaignEnabled))
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }
                if (isRbmCombatOnlyXml && RBMConfig.RBMConfig.rbmCampaignEnabled)
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }
                if ((!RBMConfig.RBMConfig.rbmCombatEnabled || !RBMConfig.RBMConfig.troopOverhaulActive) && isRbmUnitOverhaulXml)
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }
                // Core parameters merge per @id (CoreParameters.xsd: AlwaysPreferMerge + unique id). The append below
                // would add a second <managed_core_parameters> block that ManagedParameters never reads (it takes the
                // first one), so hand these to the vanilla keyed merge.
                if (isRbmXml && mergedXml.Root?.Element("managed_core_parameters") != null)
                {
                    return true;
                }

                if (RBMConfig.RBMConfig.rbmCombatEnabled || (RBMConfig.RBMConfig.rbmCampaignEnabled && isRbmCampaignXml))
                {
                    if (isRbmXml)
                    {
                        foreach (XElement origNode in originalXml.Root.Elements())
                        {
                            if (origNode.Name == "ItemModifier" && isRbmXml)
                            {
                                foreach (XElement mergedNode in mergedXml.Root.Elements())
                                {
                                    if (mergedNode.Name == "ItemModifier")
                                    {
                                        if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value) && origNode.Attribute("name").Value.Equals(mergedNode.Attribute("name").Value))
                                        {
                                            nodesToRemoveArray.Add(origNode);
                                        }
                                    }
                                }
                            }

                            if (origNode.Name == "CraftedItem" && isRbmXml)
                            {
                                foreach (XElement mergedNode in mergedXml.Root.Elements())
                                {
                                    if (mergedNode.Name == "CraftedItem")
                                    {
                                        if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value))
                                        {
                                            nodesToRemoveArray.Add(origNode);
                                        }
                                    }
                                }
                            }

                            if (origNode.Name == "Item" && isRbmXml)
                            {
                                foreach (XElement mergedNode in mergedXml.Root.Elements())
                                {
                                    if (mergedNode.Name == "Item")
                                    {
                                        if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value))
                                        {
                                            nodesToRemoveArray.Add(origNode);
                                        }

                                        if (RBMConfig.RBMConfig.betterArrowVisuals && (mergedNode.Attribute("Type").Value.Equals("Arrows") || mergedNode.Attribute("Type").Value.Equals("Bolts")))
                                        {
                                            if (mergedNode.Attribute("flying_mesh") != null && mergedNode.Attribute("mesh") != null)
                                            {
                                                mergedNode.Attribute("flying_mesh").Value = mergedNode.Attribute("mesh").Value;
                                            }
                                        }
                                    }
                                }
                            }

                            if (origNode.Name == "NPCCharacter" && isRbmXml)
                            {
                                foreach (XElement nodeEquip in origNode.Elements())
                                {
                                    if (nodeEquip.Name == "Equipments")
                                    {
                                        foreach (XElement nodeEquipRoster in nodeEquip.Elements())
                                        {
                                            if (nodeEquipRoster.Name == "EquipmentRoster")
                                            {
                                                foreach (XElement mergedNode in mergedXml.Root.Elements())
                                                {
                                                    if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value))
                                                    {
                                                        foreach (XElement mergedNodeEquip in mergedNode.Elements())
                                                        {
                                                            if (mergedNodeEquip.Name == "Equipments")
                                                            {
                                                                foreach (XElement mergedNodeRoster in mergedNodeEquip.Elements())
                                                                {
                                                                    if (mergedNodeRoster.Name == "EquipmentRoster")
                                                                    {
                                                                        if (!nodesToRemoveArray.Contains(origNode))
                                                                        {
                                                                            nodesToRemoveArray.Add(origNode);
                                                                        }
                                                                        foreach (XElement equipmentNode in mergedNodeRoster.Elements())
                                                                        {
                                                                            if (equipmentNode.Name == "equipment")
                                                                            {
                                                                                // Shoulder variants are "<shield id>_shoulder"; most base ids end in "shield", but not all
                                                                                // (battania_shield_targe_a_shoulder), so strip the suffix rather than matching "shield_shoulder".
                                                                                // Requiring "shield" keeps shoulder armour (nord_fur_shoulder, ...) untouched.
                                                                                // Other strap styles sit between the base id and the suffix ("<shield id>_kalkan_shoulder",
                                                                                // "<shield id>_cataphract_shoulder") because "<shield id>_shoulder" is already taken; drop the tag too.
                                                                                string equipmentId = equipmentNode.Attribute("id")?.Value;
                                                                                if (equipmentId != null && equipmentId.Contains("shield") && equipmentId.EndsWith("_shoulder") && !RBMConfig.RBMConfig.passiveShoulderShields)
                                                                                {
                                                                                    string baseId = equipmentId.Substring(0, equipmentId.Length - "_shoulder".Length);
                                                                                    foreach (string strapStyle in new[] { "_kalkan", "_cataphract" })
                                                                                    {
                                                                                        if (baseId.EndsWith(strapStyle))
                                                                                        {
                                                                                            baseId = baseId.Substring(0, baseId.Length - strapStyle.Length);
                                                                                            break;
                                                                                        }
                                                                                    }
                                                                                    equipmentNode.Attribute("id").Value = baseId;
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (nodesToRemoveArray.Count > 0)
                        {
                            foreach (XElement node in nodesToRemoveArray)
                            {
                                node.Remove();
                            }
                        }

                        originalXml.Root.Add(mergedXml.Root.Elements());
                        __result = MBObjectManager.ToXmlDocument(originalXml);
                        return false;
                    }
                }
                return true;
            }
        }
    }
}