using HarmonyLib;
using NavalDLC;
using NavalDLC.CampaignBehaviors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.ModuleManager;
using TaleWorlds.ObjectSystem;

namespace RBM
{
    internal class XmlLoadingPatches
    {
        [HarmonyPatch(typeof(MBObjectManager))]
        [HarmonyPatch("CreateMergedXmlFile")]
        private class CreateMergedXmlFilePatch
        {
            private static bool Prefix(List<Tuple<string, string>> toBeMerged, List<string> xsltList, bool skipValidation)
            {
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
                foreach (XComment comment in comments)
                {
                    if (comment.Value.Contains("RBM_XML_TAG"))
                    {
                        isRbmXml = true;
                    }
                    if (comment.Value.Contains("RBM_COMBAT_OVERHAUL_XML_TAG"))
                    {
                        isRbmUnitOverhaulXml = true;
                    }
                    if (comment.Value.Contains("RBM_COMBAT_XML_TAG"))
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
                if ((!RBMConfig.RBMConfig.rbmCombatEnabled || !RBMConfig.RBMConfig.troopOverhaulActive) && isRbmUnitOverhaulXml)
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }
                
                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    if (isRbmXml) { 
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
                                            mergedNode.Attribute("flying_mesh").Value = mergedNode.Attribute("mesh").Value;
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
                                                                                if (equipmentNode.Attribute("id") != null && equipmentNode.Attribute("id").Value.Contains("shield_shoulder") && !RBMConfig.RBMConfig.passiveShoulderShields)
                                                                                {
                                                                                    equipmentNode.Attribute("id").Value = equipmentNode.Attribute("id").Value.Replace("shield_shoulder", "shield");
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