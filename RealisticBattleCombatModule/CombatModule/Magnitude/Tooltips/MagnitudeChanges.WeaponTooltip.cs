using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public static partial class MagnitudeChanges
    {
        // One row of the hover damage table: damage inflicted at the given armor value, plus the penetrated/blunt split.
        private delegate float DamageTableRow(float armor, out float penetratedDamage, out float bluntForce);

        private static string DamageTableHeader()
        {
            return new TextObject("{=RBM_COM_028}A-Armor").ToString() + "\n" + new TextObject("{=RBM_COM_029}D-Damage Inflicted").ToString() + "\n" + new TextObject("{=RBM_COM_030}P-Penetrated Damage").ToString() + "\n" + new TextObject("{=RBM_COM_031}B-Blunt Force Trauma").ToString() + "\n";
        }

        // Builds the "A: armor D: damage P: penetrated B: blunt" hint text for armor 0..100 in steps of 10.
        private static string GenerateDamageTable(DamageTableRow computeRow)
        {
            string colA = new TextObject("{=RBM_COM_032}A").ToString();
            string colD = new TextObject("{=RBM_COM_033}D").ToString();
            string colP = new TextObject("{=RBM_COM_034}P").ToString();
            string colB = new TextObject("{=RBM_COM_035}B").ToString();

            StringBuilder sb = new StringBuilder(DamageTableHeader());
            for (float i = 0; i <= 100; i += 10)
            {
                int realDamage = MBMath.ClampInt(MathF.Floor(computeRow(i, out float penetratedDamage, out float bluntForce)), 0, 2000);
                sb.Append(colA).Append(": ").Append(String.Format("{0,-5}", i)).Append(' ')
                  .Append(colD).Append(": ").Append(String.Format("{0,-5}", realDamage)).Append(' ')
                  .Append(colP).Append(": ").Append(String.Format("{0,-5}", MathF.Floor(penetratedDamage))).Append(' ')
                  .Append(colB).Append(": ").Append(MathF.Floor(bluntForce)).Append('\n');
            }
            return sb.ToString();
        }

        // Draw weight, ideal ammo weight and launch speed a bow or crossbow shows in its tooltip. Returns false for any other weapon.
        private static bool GetLauncherTooltipStats(EquipmentElement weapon, int weaponUsageIndex, out int drawWeight, out float ammoWeightIdeal, out int calculatedMissileSpeed)
        {
            drawWeight = 0;
            ammoWeightIdeal = 0f;
            calculatedMissileSpeed = 0;
            WeaponComponentData wcd = weapon.IsEmpty ? null : weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex);
            if (wcd == null || (wcd.WeaponClass != WeaponClass.Bow && wcd.WeaponClass != WeaponClass.Crossbow))
            {
                return false;
            }

            int msModifier = 0;
            if (weapon.ItemModifier != null)
            {
                msModifier = weapon.ItemModifier.HitPoints;
            }
            drawWeight = weapon.GetModifiedMissileSpeedForUsage(weaponUsageIndex) + msModifier;
            if (wcd.WeaponClass == WeaponClass.Bow)
            {
                float ammoWeightIdealModifier = wcd.ItemUsage.Equals("bow") ? 1600f : 1400f;
                ammoWeightIdeal = drawWeight / ammoWeightIdealModifier;
            }
            else
            {
                float ammoWeightIdealModifier = 1750f;
                ammoWeightIdeal = MathF.Clamp(drawWeight / ammoWeightIdealModifier, 0f, 0.150f);
            }
            calculatedMissileSpeed = Utilities.calculateMissileSpeed(ammoWeightIdeal, wcd.ItemUsage, drawWeight);
            return true;
        }

        public static void GetRBMMeleeWeaponStats(in EquipmentElement targetWeapon, int targetWeaponUsageIndex, EquipmentElement comparedWeapon, int comparedWeaponUsageIndex,
            out int relevantSkill, out float swingSpeed, out float swingSpeedCompred, out float thrustSpeed, out float thrustSpeedCompred, out float sweetSpotOut, out float sweetSpotComparedOut,
            out string swingCombinedStringOut, out string swingCombinedStringComparedOut, out string thrustCombinedStringOut, out string thrustCombinedStringComparedOut,
            out float swingDamageFactor, out float swingDamageFactorCompared, out float thrustDamageFactor, out float thrustDamageFactorCompared)
        {
            relevantSkill = 0;
            swingSpeed = 0f;
            swingSpeedCompred = 0f;
            thrustSpeed = 0f;
            thrustSpeedCompred = 0f;
            swingDamageFactor = 0f;
            swingDamageFactorCompared = 0f;
            thrustDamageFactor = 0f;
            thrustDamageFactorCompared = 0f;
            sweetSpotOut = 0f;
            sweetSpotComparedOut = 0f;
            swingCombinedStringOut = "";
            swingCombinedStringComparedOut = "";
            thrustCombinedStringOut = "";
            thrustCombinedStringComparedOut = "";
            if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsMeleeWeapon)
            {
                if (currentSelectedChar != null)
                {
                    SkillObject skill = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).RelevantSkill;
                    int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                    float effectiveSkillDR = Utilities.GetEffectiveSkillWithDR(effectiveSkill);
                    float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill);

                    Utilities.CalculateVisualSpeeds(targetWeapon, targetWeaponUsageIndex, effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal);
                    Utilities.CalculateVisualSpeeds(comparedWeapon, comparedWeaponUsageIndex, effectiveSkillDR, out int swingSpeedRealCompred, out int thrustSpeedRealCompared, out int handlingRealCompared);

                    float swingSpeedRealF = swingSpeedReal / Utilities.swingSpeedTransfer;
                    float thrustSpeedRealF = thrustSpeedReal / Utilities.thrustSpeedTransfer;
                    float swingSpeedRealComparedF = swingSpeedRealCompred / Utilities.swingSpeedTransfer;
                    float thrustSpeedRealComparedF = thrustSpeedRealCompared / Utilities.thrustSpeedTransfer;

                    relevantSkill = effectiveSkill;

                    swingSpeed = swingSpeedRealF;
                    swingSpeedCompred = swingSpeedRealComparedF;
                    thrustSpeed = thrustSpeedRealF;
                    thrustSpeedCompred = thrustSpeedRealComparedF;

                    if (targetWeapon.GetModifiedSwingDamageForUsage(targetWeaponUsageIndex) > 0f)
                    {
                        float sweetSpotMagnitude = CalculateSweetSpotSwingMagnitude(targetWeapon, targetWeaponUsageIndex, effectiveSkill, out float sweetSpot);
                        float sweetSpotMagnitudeCompared = CalculateSweetSpotSwingMagnitude(comparedWeapon, comparedWeaponUsageIndex, effectiveSkill, out float sweetSpotCompared);

                        WeaponComponentData targetWcd = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex);
                        float skillBasedDamage = Utilities.GetSkillBasedDamage(sweetSpotMagnitude, false, targetWcd.WeaponClass.ToString(),
                            targetWcd.SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, targetWeapon.Item.Weight);

                        swingDamageFactor = (float)Math.Sqrt(Utilities.getSwingDamageFactor(targetWcd, targetWeapon.ItemModifier));
                        swingDamageFactorCompared = -1f;

                        sweetSpotOut = sweetSpot;
                        sweetSpotComparedOut = sweetSpotCompared;

                        float targetFactor = swingDamageFactor;
                        swingCombinedStringOut = GenerateDamageTable((float armor, out float pen, out float blunt) =>
                            Utilities.RBMComputeDamage(targetWcd.WeaponClass.ToString(), targetWcd.SwingDamageType, skillBasedDamage, armor, 1f, out pen, out blunt, targetFactor, null, false));

                        if (!comparedWeapon.IsEmpty)
                        {
                            swingCombinedStringComparedOut = DamageTableHeader();
                        }
                        if (sweetSpotMagnitudeCompared > 0f)
                        {
                            WeaponComponentData comparedWcd = comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex);
                            float skillBasedDamageCompared = Utilities.GetSkillBasedDamage(sweetSpotMagnitudeCompared, false, comparedWcd.WeaponClass.ToString(),
                                comparedWcd.SwingDamageType, effectiveSkillDR, skillModifier, StrikeType.Swing, comparedWeapon.Item.Weight);
                            swingDamageFactorCompared = (float)Math.Sqrt(Utilities.getSwingDamageFactor(comparedWcd, comparedWeapon.ItemModifier));

                            float comparedFactor = swingDamageFactorCompared;
                            swingCombinedStringComparedOut = GenerateDamageTable((float armor, out float pen, out float blunt) =>
                                Utilities.RBMComputeDamage(comparedWcd.WeaponClass.ToString(), comparedWcd.SwingDamageType, skillBasedDamageCompared, armor, 1f, out pen, out blunt, comparedFactor, null, false));
                        }
                    }

                    if (targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) > 0f)
                    {
                        float thrustMagnitude = CalculateThrustMagnitude(targetWeapon, targetWeaponUsageIndex, effectiveSkill);
                        float thrustMagnitudeCompared = CalculateThrustMagnitude(comparedWeapon, comparedWeaponUsageIndex, effectiveSkill);

                        WeaponComponentData targetWcd = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex);
                        float skillBasedDamage = Utilities.GetSkillBasedDamage(thrustMagnitude, false, targetWcd.WeaponClass.ToString(),
                            targetWcd.ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, targetWeapon.Item.Weight);

                        thrustDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWcd, targetWeapon.ItemModifier));
                        thrustDamageFactorCompared = -1f;

                        float targetFactor = thrustDamageFactor;
                        thrustCombinedStringOut = GenerateDamageTable((float armor, out float pen, out float blunt) =>
                            Utilities.RBMComputeDamage(targetWcd.WeaponClass.ToString(), targetWcd.ThrustDamageType, skillBasedDamage, armor, 1f, out pen, out blunt, targetFactor, null, false));

                        if (!comparedWeapon.IsEmpty)
                        {
                            thrustCombinedStringComparedOut = DamageTableHeader();
                        }
                        if (thrustMagnitudeCompared > 0f)
                        {
                            WeaponComponentData comparedWcd = comparedWeapon.Item.GetWeaponWithUsageIndex(comparedWeaponUsageIndex);
                            float skillBasedDamageCompared = Utilities.GetSkillBasedDamage(thrustMagnitudeCompared, false, comparedWcd.WeaponClass.ToString(),
                                comparedWcd.ThrustDamageType, effectiveSkillDR, skillModifier, StrikeType.Thrust, comparedWeapon.Item.Weight);
                            thrustDamageFactorCompared = (float)Math.Sqrt(Utilities.getThrustDamageFactor(comparedWcd, comparedWeapon.ItemModifier));

                            float comparedFactor = thrustDamageFactorCompared;
                            thrustCombinedStringComparedOut = GenerateDamageTable((float armor, out float pen, out float blunt) =>
                                Utilities.RBMComputeDamage(comparedWcd.WeaponClass.ToString(), comparedWcd.ThrustDamageType, skillBasedDamageCompared, armor, 1f, out pen, out blunt, comparedFactor, null, false));
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ItemMenuVM))]
        [HarmonyPatch("SetWeaponComponentTooltip")]
        private class SetWeaponComponentTooltipPatch
        {
            private static readonly MethodInfo methodAddFloatProperty = typeof(ItemMenuVM).GetMethod("AddFloatProperty", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(TextObject), typeof(float), typeof(float?), typeof(bool) }, null);
            private static readonly MethodInfo methodAddIntProperty = typeof(ItemMenuVM).GetMethod("AddIntProperty", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly MethodInfo methodCreateProperty = typeof(ItemMenuVM).GetMethod("CreateProperty", BindingFlags.NonPublic | BindingFlags.Instance);

            // While comparing, the compared column is a separate list with blank labels, so its values only line up with
            // their stat if both lists get exactly the same rows (vanilla pads with empty rows for the same reason).
            // Every RBM row goes through these helpers so no row is added to one list only.
            private static void AddRow(ItemMenuVM vm, MBBindingList<ItemMenuTooltipPropertyVM> list, string definition, string value, int textHeight = 0, HintViewModel hint = null)
            {
                methodCreateProperty.Invoke(vm, new object[] { list, definition, value, textHeight, hint });
            }

            private static void AddHeader(ItemMenuVM vm, string text)
            {
                AddRow(vm, vm.TargetItemProperties, text, "", 1);
                if (vm.IsComparing)
                {
                    AddRow(vm, vm.ComparedItemProperties, text, "", 1);
                }
            }

            // Hover-for-table subheader. The compared column gets its own table when there is one, else an empty spacer row.
            private static void AddDamageSubHeader(ItemMenuVM vm, string text, string table, string comparedTable = null)
            {
                AddRow(vm, vm.TargetItemProperties, "", text, 1, new HintViewModel(new TextObject(table)));
                if (vm.IsComparing)
                {
                    if (!string.IsNullOrEmpty(comparedTable))
                    {
                        AddRow(vm, vm.ComparedItemProperties, "", text, 1, new HintViewModel(new TextObject(comparedTable)));
                    }
                    else
                    {
                        AddRow(vm, vm.ComparedItemProperties, "", "", 1);
                    }
                }
            }

            private static void AddInt(ItemMenuVM vm, TextObject label, int value, int? comparedValue)
            {
                methodAddIntProperty.Invoke(vm, new object[] { label, value, comparedValue });
                if (vm.IsComparing && !comparedValue.HasValue)
                {
                    AddRow(vm, vm.ComparedItemProperties, "", "");
                }
            }

            private static void AddFloat(ItemMenuVM vm, TextObject label, float value, float? comparedValue)
            {
                methodAddFloatProperty.Invoke(vm, new object[] { label, value, comparedValue, false });
                if (vm.IsComparing && !comparedValue.HasValue)
                {
                    AddRow(vm, vm.ComparedItemProperties, "", "");
                }
            }

            // comparedWeapon/comparedWeaponUsageIndex are the values vanilla resolved inside SetWeaponComponentTooltip
            // (it reassigns the parameters via GetComparedWeapon, and the postfix reads the same argument slots).
            private static void Postfix(ref ItemMenuVM __instance, in EquipmentElement targetWeapon, int targetWeaponUsageIndex, EquipmentElement comparedWeapon, int comparedWeaponUsageIndex)
            {

                if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsShield)
                {
                    AddInt(__instance, new TextObject("{=RBM_COM_022}Shield Armor: "), targetWeapon.GetModifiedBodyArmor(), comparedWeapon.IsEmpty ? (int?)null : comparedWeapon.GetModifiedBodyArmor());
                }
                if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsRangedWeapon)
                {
                    if (currentSelectedChar != null)
                    {
                        SkillObject skill = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).RelevantSkill;
                        int effectiveSkill = currentSelectedChar.GetSkillValue(skill);
                        WeaponComponentData targetWcd = targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex);
                        if (targetWcd.WeaponClass == WeaponClass.Bow || targetWcd.WeaponClass == WeaponClass.Crossbow)
                        {
                            GetLauncherTooltipStats(targetWeapon, targetWeaponUsageIndex, out int drawWeight, out float ammoWeightIdeal, out int calculatedMissileSpeed);

                            // Compare column shows the compared launcher's own stats; null (no compare value) when there isn't one.
                            int? comparedAmmoGrams = null;
                            int? comparedMissileSpeed = null;
                            int? comparedDrawWeight = null;
                            if (GetLauncherTooltipStats(comparedWeapon, comparedWeaponUsageIndex, out int cDrawWeight, out float cAmmoWeightIdeal, out int cMissileSpeed))
                            {
                                comparedAmmoGrams = MathF.Round(cAmmoWeightIdeal * 1000f);
                                comparedMissileSpeed = cMissileSpeed;
                                comparedDrawWeight = cDrawWeight;
                            }

                            AddHeader(__instance, new TextObject("{=RBM_COM_036}RBM Stats").ToString());

                            AddInt(__instance, new TextObject("{=RBM_COM_009}Ideal Ammo Weight Range/Damage, grams: "), MathF.Round(ammoWeightIdeal * 1000f), comparedAmmoGrams);
                            AddInt(__instance, new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calculatedMissileSpeed, comparedMissileSpeed);
                            AddInt(__instance, new TextObject("{=RBM_COM_011}Draw weight with modifier: "), drawWeight, comparedDrawWeight);

                            WeaponClass ammoClass = targetWcd.WeaponClass == WeaponClass.Bow ? WeaponClass.Arrow : WeaponClass.Bolt;

                            //pierce
                            float pierceMagnitude = CalculateMissileMagnitude(ammoClass, ammoWeightIdeal, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) + 100f, 1f, DamageTypes.Pierce);
                            AddDamageSubHeader(__instance, new TextObject("{=RBM_COM_012}Missile Damage Pierce").ToString(), GenerateDamageTable((float armor, out float pen, out float blunt) =>
                                Utilities.RBMComputeDamage(ammoClass.ToString(), DamageTypes.Pierce, pierceMagnitude, armor, 1f, out pen, out blunt, 1f, null, false)));

                            //cut
                            float cutMagnitude = CalculateMissileMagnitude(ammoClass, ammoWeightIdeal, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) + 115f, 1f, DamageTypes.Cut);
                            AddDamageSubHeader(__instance, new TextObject("{=RBM_COM_013}Missile Damage Cut").ToString(), GenerateDamageTable((float armor, out float pen, out float blunt) =>
                                Utilities.RBMComputeDamage(ammoClass.ToString(), DamageTypes.Cut, cutMagnitude, armor, 1f, out pen, out blunt, 1f, null, false)));
                        }
                        if (targetWcd.WeaponClass == WeaponClass.Javelin ||
                            targetWcd.WeaponClass == WeaponClass.ThrowingAxe ||
                            targetWcd.WeaponClass == WeaponClass.ThrowingKnife ||
                            targetWcd.WeaponClass == WeaponClass.Dagger)
                        {
                            int calculatedMissileSpeed = Utilities.assignThrowableMissileSpeedForMenu(targetWeapon.Weight, (int)Utilities.throwableCorrectionSpeed, effectiveSkill);
                            int? comparedMissileSpeed = null;
                            if (!comparedWeapon.IsEmpty)
                            {
                                comparedMissileSpeed = Utilities.assignThrowableMissileSpeedForMenu(comparedWeapon.Weight, (int)Utilities.throwableCorrectionSpeed, effectiveSkill);
                            }

                            AddHeader(__instance, new TextObject("{=RBM_COM_036}RBM Stats").ToString());
                            AddInt(__instance, new TextObject("{=RBM_COM_014}Relevant Skill: "), effectiveSkill, effectiveSkill);
                            AddInt(__instance, new TextObject("{=RBM_COM_010}Initial Missile Speed, m/s: "), calculatedMissileSpeed, comparedMissileSpeed);

                            // Javelins: pierce with the modifier-aware thrust factor. Throwing axes: swing type/factor. Knives/daggers: raw thrust factor.
                            DamageTypes magnitudeDamageType = targetWcd.WeaponClass == WeaponClass.ThrowingAxe ? targetWcd.SwingDamageType : targetWcd.ThrustDamageType;
                            DamageTypes tableDamageType;
                            float weaponDamageFactor;
                            if (targetWcd.WeaponClass == WeaponClass.Javelin)
                            {
                                tableDamageType = DamageTypes.Pierce;
                                weaponDamageFactor = (float)Math.Sqrt(Utilities.getThrustDamageFactor(targetWcd, targetWeapon.ItemModifier));
                            }
                            else if (targetWcd.WeaponClass == WeaponClass.ThrowingAxe)
                            {
                                tableDamageType = targetWcd.SwingDamageType;
                                weaponDamageFactor = (float)Math.Sqrt(targetWcd.SwingDamageFactor);
                            }
                            else
                            {
                                tableDamageType = targetWcd.ThrustDamageType;
                                weaponDamageFactor = (float)Math.Sqrt(targetWcd.ThrustDamageFactor);
                            }

                            float missileMagnitude = CalculateMissileMagnitude(targetWcd.WeaponClass, targetWeapon.Weight, calculatedMissileSpeed, targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex), 1f, magnitudeDamageType);
                            AddDamageSubHeader(__instance, new TextObject("{=RBM_COM_015}Missile Damage").ToString(), GenerateDamageTable((float armor, out float pen, out float blunt) =>
                                Utilities.RBMComputeDamage(targetWcd.WeaponClass.ToString(), tableDamageType, missileMagnitude, armor, 1f, out pen, out blunt, weaponDamageFactor, null, false)));
                        }
                    }
                }
                if (!targetWeapon.IsEmpty && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex) != null && targetWeapon.Item.GetWeaponWithUsageIndex(targetWeaponUsageIndex).IsMeleeWeapon)
                {
                    GetRBMMeleeWeaponStats(targetWeapon, targetWeaponUsageIndex, comparedWeapon, comparedWeaponUsageIndex, out int relevantSkill, out float swingSpeed, out float swingSpeedCompred, out float thrustSpeed, out float thrustSpeedCompred, out float sweetSpotOut, out float sweetSpotComparedOut,
                    out string swingCombinedStringOut, out string swingCombinedStringComparedOut, out string thrustCombinedStringOut, out string thrustCombinedStringComparedOut,
                    out float swingDamageFactor, out float swingDamageFactorCompared, out float thrustDamageFactor, out float thrustDamageFactorCompared);

                    if (currentSelectedChar != null)
                    {
                        bool hasCompared = !comparedWeapon.IsEmpty;

                        AddHeader(__instance, new TextObject("{=RBM_COM_036}RBM Stats").ToString());

                        AddInt(__instance, new TextObject("{=RBM_COM_014}Relevant Skill: "), relevantSkill, hasCompared ? relevantSkill : (int?)null);

                        // A compared factor of -1 means the compared weapon has no such attack: leave its cell blank instead of showing "-100".
                        AddInt(__instance, new TextObject("{=RBM_COM_016}Swing Damage Factor:"), MathF.Round(swingDamageFactor * 100f), swingDamageFactorCompared > 0f ? MathF.Round(swingDamageFactorCompared * 100f) : (int?)null);
                        AddInt(__instance, new TextObject("{=RBM_COM_017}Thrust Damage Factor:"), MathF.Round(thrustDamageFactor * 100f), thrustDamageFactorCompared > 0f ? MathF.Round(thrustDamageFactorCompared * 100f) : (int?)null);

                        AddFloat(__instance, new TextObject("{=RBM_COM_020}Swing Speed, m/s: "), swingSpeed, hasCompared ? swingSpeedCompred : (float?)null);
                        AddFloat(__instance, new TextObject("{=RBM_COM_021}Thrust Speed, m/s: "), thrustSpeed, hasCompared ? thrustSpeedCompred : (float?)null);

                        if (targetWeapon.GetModifiedSwingDamageForUsage(targetWeaponUsageIndex) > 0f)
                        {
                            AddInt(__instance, new TextObject("{=RBM_COM_018}Swing Sweet Spot, %: "), MathF.Floor(sweetSpotOut * 100f), sweetSpotComparedOut > 0f ? MathF.Floor(sweetSpotComparedOut * 100f) : (int?)null);

                            AddDamageSubHeader(__instance, new TextObject("{=QeToaiLt}Swing Damage").ToString() + " (" + new TextObject("{=RBM_COM_037}Hover").ToString() + ")",
                                swingCombinedStringOut, hasCompared ? swingCombinedStringComparedOut : null);
                        }

                        if (targetWeapon.GetModifiedThrustDamageForUsage(targetWeaponUsageIndex) > 0f)
                        {
                            AddDamageSubHeader(__instance, new TextObject("{=dO95yR9b}Thrust Damage").ToString() + " (" + new TextObject("{=RBM_COM_037}Hover").ToString() + ")",
                                thrustCombinedStringOut, hasCompared ? thrustCombinedStringComparedOut : null);
                        }

                        if (RBMConfig.RBMConfig.developerMode)
                        {
                            if (targetWeapon.Item.WeaponDesign != null && targetWeapon.Item.WeaponDesign.UsedPieces != null && targetWeapon.Item.WeaponDesign.UsedPieces.Count() > 0)
                            {
                                AddHeader(__instance, new TextObject("{=RBM_COM_019}RBM Developer Stats").ToString());

                                foreach (WeaponDesignElement wde in targetWeapon.Item.WeaponDesign.UsedPieces)
                                {
                                    AddRow(__instance, __instance.TargetItemProperties, "", wde.CraftingPiece.StringId + " " + wde.CraftingPiece.Name, 1);
                                    if (__instance.IsComparing)
                                    {
                                        AddRow(__instance, __instance.ComparedItemProperties, "", "", 1);
                                    }
                                    AddFloat(__instance, new TextObject("{=YvwQL9aa}Weight: "), wde.CraftingPiece.Weight, null);
                                    AddFloat(__instance, new TextObject("{=XUtiwiYP}Length: "), wde.CraftingPiece.Length, null);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
