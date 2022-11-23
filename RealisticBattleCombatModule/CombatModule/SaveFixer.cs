//using HarmonyLib;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;
//using TaleWorlds.CampaignSystem;
//using TaleWorlds.CampaignSystem.CampaignBehaviors;
//using TaleWorlds.CampaignSystem.GameComponents;
//using TaleWorlds.CampaignSystem.Party;
//using TaleWorlds.CampaignSystem.Roster;
//using TaleWorlds.CampaignSystem.ViewModelCollection;
//using TaleWorlds.Core.ViewModelCollection.Information;
//using TaleWorlds.MountAndBlade;

//namespace RBMAI
//{
//    class SaveFixer
//    {
//        [HarmonyPatch(typeof(DefaultPartyWageModel))]
//        class DefaultPartyWageModelPatch
//        {
//            [HarmonyPrefix]
//            [HarmonyPatch("GetTotalWage")]
//            public static bool GetTotalWagePrefix(ref MobileParty mobileParty, ref bool includeDescriptions, ref ExplainedNumber __result)
//            {
//                List<TroopRosterElement> toRemainList = new List<TroopRosterElement>();

//                FieldInfo dataField = typeof(TroopRoster).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
//                dataField.DeclaringType.GetField("data");
//                TroopRosterElement[] data = (TroopRosterElement[])dataField.GetValue(mobileParty.MemberRoster);

//                FieldInfo countField = typeof(TroopRoster).GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance);
//                countField.DeclaringType.GetField("_count");

//                for (int i=0; i< data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character?.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                        //data[i] = new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop);
//                    }
                    
//                }
//                if (toRemainList.Count == 0)
//                {
//                    toRemainList.Add(new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop));
//                }
//                //dataField.SetValue(mobileParty.MemberRoster, data, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                dataField.SetValue(mobileParty.MemberRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                mobileParty.MemberRoster.UpdateVersion();
//                countField.SetValue(mobileParty.MemberRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);

//                data = (TroopRosterElement[])dataField.GetValue(mobileParty.PrisonRoster);
//                toRemainList.Clear();
//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                    }

//                }

//                dataField.SetValue(mobileParty.PrisonRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                mobileParty.PrisonRoster.UpdateVersion();
//                countField.SetValue(mobileParty.PrisonRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                //mobileParty.MemberRoster.RemoveZeroCounts();
//                if (mobileParty.MemberRoster.Count == 0)
//                {
//                    mobileParty.AddElementToMemberRoster(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop, 0);
//                    //mobileParty.RemoveParty();
//                    __result = new ExplainedNumber(1, includeDescriptions);
//                    return false;
//                }
//                return true;
//            }
//        }

//        [HarmonyPatch(typeof(DefaultPartyMoraleModel))]
//        class DefaultPartyMoraleModelPatch
//        {
//            [HarmonyPrefix]
//            [HarmonyPatch("GetEffectivePartyMorale")]
//            public static bool GetEffectivePartyMoralePrefix(ref MobileParty mobileParty, ref bool includeDescription, ref ExplainedNumber __result)
//            {
//                List<TroopRosterElement> toRemainList = new List<TroopRosterElement>();

//                FieldInfo dataField = typeof(TroopRoster).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
//                dataField.DeclaringType.GetField("data");
//                TroopRosterElement[] data = (TroopRosterElement[])dataField.GetValue(mobileParty.MemberRoster);

//                FieldInfo countField = typeof(TroopRoster).GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance);
//                countField.DeclaringType.GetField("_count");

//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character?.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                        //data[i] = new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop);
//                    }

//                }
//                if (toRemainList.Count == 0)
//                {
//                    toRemainList.Add(new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop));
//                }
//                //dataField.SetValue(mobileParty.MemberRoster, data, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                dataField.SetValue(mobileParty.MemberRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                mobileParty.MemberRoster.UpdateVersion();
//                countField.SetValue(mobileParty.MemberRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);

//                data = (TroopRosterElement[])dataField.GetValue(mobileParty.PrisonRoster);
//                toRemainList.Clear();
//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                    }

//                }
//                dataField.SetValue(mobileParty.PrisonRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                mobileParty.PrisonRoster.UpdateVersion();
//                countField.SetValue(mobileParty.PrisonRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                if(mobileParty.MemberRoster.Count == 0)
//                {
//                    //mobileParty.RemoveParty();
//                    __result = new ExplainedNumber(50f, includeDescription);
//                    return false;
//                }
//                //mobileParty.MemberRoster.RemoveZeroCounts();
//                return true;
//            }
//        }

//        [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel))]
//        class DefaultMobilePartyFoodConsumptionModelPatch
//        {
//            [HarmonyPrefix]
//            [HarmonyPatch("CalculatePerkEffects")]
//            public static bool CalculatePerkEffectsPrefix(ref MobileParty party , ref ExplainedNumber result)
//            {
//                List<TroopRosterElement> toRemainList = new List<TroopRosterElement>();

//                FieldInfo dataField = typeof(TroopRoster).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
//                dataField.DeclaringType.GetField("data");
//                TroopRosterElement[] data = (TroopRosterElement[])dataField.GetValue(party.MemberRoster);

//                FieldInfo countField = typeof(TroopRoster).GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance);
//                countField.DeclaringType.GetField("_count");

//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character?.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                        //data[i] = new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop);
//                    }

//                }
//                if (toRemainList.Count == 0)
//                {
//                    toRemainList.Add(new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop));
//                }
//                //dataField.SetValue(mobileParty.MemberRoster, data, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                dataField.SetValue(party.MemberRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                party.MemberRoster.UpdateVersion();
//                countField.SetValue(party.MemberRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);

//                data = (TroopRosterElement[])dataField.GetValue(party.PrisonRoster);
//                toRemainList.Clear();
//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                    }

//                }
//                dataField.SetValue(party.PrisonRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                party.PrisonRoster.UpdateVersion();
//                countField.SetValue(party.PrisonRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                if (party.MemberRoster.Count == 0)
//                {
//                    //mobileParty.RemoveParty();
//                    result = new ExplainedNumber(1f, false);
//                    return false;
//                }
//                //mobileParty.MemberRoster.RemoveZeroCounts();
//                return true;
//            }
//        }

//        [HarmonyPatch(typeof(TroopRoster))]
//        class TroopRosterPatch
//        {
//            [HarmonyPostfix]
//            [HarmonyPatch("CreateDummyTroopRoster")]
//            public static void CreateDummyTroopRosterPrefix(ref TroopRoster __result)
//            {
//                __result = __result;
//            }
//        }

//        [HarmonyPatch(typeof(PropertyBasedTooltipVMExtensions))]
//        class PropertyBasedTooltipVMExtensionsPatch
//        {
//            [HarmonyPrefix]
//            [HarmonyPatch("UpdateTooltip", new Type[] { typeof(PropertyBasedTooltipVM), typeof(MobileParty), typeof(bool), typeof(bool) })]
//            public static bool UpdateTooltipPrefix(ref MobileParty mobileParty, bool openedFromMenuLayout, bool checkForMapVisibility)
//            {
//                List<TroopRosterElement> toRemainList = new List<TroopRosterElement>();

//                FieldInfo dataField = typeof(TroopRoster).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
//                dataField.DeclaringType.GetField("data");
//                TroopRosterElement[] data = (TroopRosterElement[])dataField.GetValue(mobileParty.MemberRoster);

//                FieldInfo countField = typeof(TroopRoster).GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance);
//                countField.DeclaringType.GetField("_count");

//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character?.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                        //data[i] = new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop);
//                    }

//                }
//                if (toRemainList.Count == 0)
//                {
//                    toRemainList.Add(new TroopRosterElement(Campaign.Current.Factions.FirstOrDefault().Culture.BasicTroop));
//                }
//                //dataField.SetValue(mobileParty.MemberRoster, data, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                dataField.SetValue(mobileParty.MemberRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                mobileParty.MemberRoster.UpdateVersion();
//                countField.SetValue(mobileParty.MemberRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);

//                data = (TroopRosterElement[])dataField.GetValue(mobileParty.PrisonRoster);
//                toRemainList.Clear();
//                for (int i = 0; i < data.Length; i++)
//                {
//                    if (data[i].Character != null && data[i].Character.Culture != null)
//                    {
//                        toRemainList.Add(data[i]);
//                    }

//                }
//                dataField.SetValue(mobileParty.PrisonRoster, toRemainList.ToArray(), BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                mobileParty.PrisonRoster.UpdateVersion();
//                countField.SetValue(mobileParty.PrisonRoster, toRemainList.Count, BindingFlags.NonPublic | BindingFlags.SetField, null, null);
//                //mobileParty.MemberRoster.RemoveZeroCounts();
//                return true;
//            }
//        }

//        [HarmonyPatch(typeof(RebellionsCampaignBehavior))]
//        class RebellionsCampaignBehaviorPatch
//        {
//            [HarmonyPrefix]
//            [HarmonyPatch("InitializeIconIdAndFrequencies")]
//            public static bool InitializeIconIdAndFrequenciesPrefix(ref Dictionary<CultureObject, Dictionary<int, int>> ____cultureIconIdAndFrequencies)
//            {

//                foreach (CultureObject key in ____cultureIconIdAndFrequencies.Keys.ToList())
//                {
//                    if(key.BasicTroop == null)
//                    {
//                        ____cultureIconIdAndFrequencies.Remove(key);
//                    }
//                }
//                return true;
//            }
//        }
//    }
//}
