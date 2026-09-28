using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace RBMCombat
{
    internal partial class CampaignChanges
    {
        /// <summary>
        /// "Escort Merchant Caravan" quest crash (InvalidOperationException: Sequence contains no matching element).
        ///
        /// Vanilla picks the quest caravan's leader with
        /// <code>
        ///     CharacterObject.All.First(c => c.Occupation == CaravanGuard &amp;&amp; c.IsInfantry &amp;&amp; c.Level == 26
        ///                                   &amp;&amp; c.Culture == mobileParty.Party.Owner.Culture);
        /// </code>
        /// i.e. it identifies the culture's Caravan Master by its vanilla shape. RBM's troop overhaul
        /// (RBMCombat_unit_overhaul.xml) makes the land cultures' caravan_master_* default_group="Ranged", so
        /// IsInfantry is false, nothing matches and First throws. Every other vanilla caravan path uses
        /// Culture.CaravanMaster directly, so this swaps First for a lookup that keeps vanilla's match when
        /// there is one and otherwise falls back to that.
        /// </summary>
        [HarmonyPatch(typeof(EscortMerchantCaravanIssueBehavior.EscortMerchantCaravanIssueQuest), "InitializeCaravanOnCreation")]
        private class EscortCaravanMasterLookup
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                MethodInfo lookup = AccessTools.Method(typeof(EscortCaravanMasterLookup), nameof(FindCaravanMaster));

                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode != OpCodes.Call || !(codes[i].operand is MethodInfo method)
                        || method.DeclaringType != typeof(Enumerable) || method.Name != nameof(Enumerable.First)
                        || method.GetParameters().Length != 2 || method.GetGenericArguments()[0] != typeof(CharacterObject))
                    {
                        continue;
                    }
                    // Stack holds (source, predicate); push mobileParty (arg 1) and call the lookup instead.
                    // Labels move onto the ldarg so a branch into the call still pushes the extra argument.
                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(codes[i]));
                    codes[i + 1] = new CodeInstruction(OpCodes.Call, lookup);
                    break;
                }

                return codes;
            }

            private static CharacterObject FindCaravanMaster(IEnumerable<CharacterObject> source, Func<CharacterObject, bool> predicate, MobileParty mobileParty)
            {
                return source.FirstOrDefault(predicate)
                    ?? mobileParty?.Party?.Owner?.Culture?.CaravanMaster
                    ?? source.First(c => c.Occupation == Occupation.CaravanGuard);
            }
        }
    }
}
