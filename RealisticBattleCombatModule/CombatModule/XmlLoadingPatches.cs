using HarmonyLib;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace RealisticBattleCombatModule.CombatModule
{
    class XmlLoadingPatches
    {
        [HarmonyPatch(typeof(MBObjectManager))]
        [HarmonyPatch("MergeTwoXmls")]
        class MergeTwoXmlsPatch
        {

            [HarmonyPatch(typeof(FightTournamentGame))]
            [HarmonyPatch("CachePossibleEliteRewardItems")]
            class CachePossibleEliteRewardItemsPatch
            {
                static bool Prefix(ref FightTournamentGame __instance, ref int ____lastRecordedLordCountForTournamentPrize, ref List<ItemObject> ____possibleEliteRewardItemObjectsCache, ref List<ItemObject> ____possibleRegularRewardItemObjectsCache)
                {
                    if (____possibleEliteRewardItemObjectsCache == null)
                    {
                        ____possibleEliteRewardItemObjectsCache = new List<ItemObject>();
                    }

                    string[] array = new string[31]
                    {
        "winds_fury_sword_t3", "bone_crusher_mace_t3", "tyrhung_sword_t3", "pernach_mace_t3", "early_retirement_2hsword_t3", "black_heart_2haxe_t3", "knights_fall_mace_t3", "the_scalpel_sword_t3", "judgement_mace_t3", "dawnbreaker_sword_t3",
        "ambassador_sword_t3", "heavy_nasalhelm_over_imperial_mail", "sturgian_helmet_closed", "full_helm_over_laced_coif", "desert_mail_coif", "heavy_nasalhelm_over_imperial_mail", "plumed_nomad_helmet", "ridged_northernhelm", "noble_horse_southern", "noble_horse_imperial",
        "noble_horse_western", "noble_horse_eastern", "noble_horse_battania", "noble_horse_northern", "special_camel", "western_crowned_helmet", "northern_warlord_helmet", "battania_warlord_pauldrons", "aserai_armor_02_b", "white_coat_over_mail",
        "spiked_helmet_with_facemask"
                    };

                    foreach (string objectName in array)
                    {
                        ItemObject io = Game.Current.ObjectManager.GetObject<ItemObject>(objectName);
                        if (io != null)
                        {
                            ____possibleEliteRewardItemObjectsCache.Add(io);
                        }
                    }
                    ____possibleEliteRewardItemObjectsCache.Sort((ItemObject x, ItemObject y) => x.Value.CompareTo(y.Value));

                    return false;
                }
            }

            static bool Prefix(XmlDocument xmlDocument1, XmlDocument xmlDocument2, ref XmlDocument __result)
            {
                XDocument originalXml = MBObjectManager.ToXDocument(xmlDocument1);
                XDocument mergedXml = MBObjectManager.ToXDocument(xmlDocument2);

                List<XElement> nodesToRemoveArray = new List<XElement>();

                if (RBMCMConfig.dict["Global.TroopOverhaulActive"] == 0 && xmlDocument2.BaseURI.Contains("unit_overhaul"))
                {
                    __result = MBObjectManager.ToXmlDocument(originalXml);
                    return false;
                }

                bool isShoulderShiledsEnabled = false;
                if (RBMCMConfig.dict["Global.PassiveShoulderShields"] == 0)
                {
                    isShoulderShiledsEnabled = false;
                }
                else if (RBMCMConfig.dict["Global.PassiveShoulderShields"] == 1)
                {
                    isShoulderShiledsEnabled = true;
                }

                bool isBetterArrowVisualsEnabled = false;
                if (RBMCMConfig.dict["Global.BetterArrowVisuals"] == 0)
                {
                    isBetterArrowVisualsEnabled = false;
                }
                else if (RBMCMConfig.dict["Global.BetterArrowVisuals"] == 1)
                {
                    isBetterArrowVisualsEnabled = true;
                }

                foreach (XElement origNode in originalXml.Root.Elements())
                {
                    if (origNode.Name == "CraftedItem" && xmlDocument2.BaseURI.Contains("RealisticBattle"))
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

                    if (origNode.Name == "Item" && xmlDocument2.BaseURI.Contains("RealisticBattle"))
                    {
                        foreach (XElement mergedNode in mergedXml.Root.Elements())
                        {
                            if (mergedNode.Name == "Item")
                            {
                                if (origNode.Attribute("id").Value.Equals(mergedNode.Attribute("id").Value))
                                {
                                    nodesToRemoveArray.Add(origNode);
                                }

                                if (isBetterArrowVisualsEnabled && (mergedNode.Attribute("Type").Value.Equals("Arrows") || mergedNode.Attribute("Type").Value.Equals("Bolts")))
                                {
                                    mergedNode.Attribute("flying_mesh").Value = mergedNode.Attribute("mesh").Value;
                                }
                            }
                        }
                    }

                    if (origNode.Name == "NPCCharacter" && xmlDocument2.BaseURI.Contains("RealisticBattle"))
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
                                                                        if (equipmentNode.Attribute("id") != null && equipmentNode.Attribute("id").Value.Contains("shield_shoulder") && !isShoulderShiledsEnabled)
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

    }
}
