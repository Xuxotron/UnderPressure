using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    internal static class GameplayModifier
    {
        internal static float Factor(int percentage) =>
            UnderPressurePlugin.IsModEnabled ? Mathf.Max(0f, 1f + percentage / 100f) : 1f;

        internal static float DifficultyFactor(int percentage)
        {
            if (!UnderPressurePlugin.IsModEnabled) return 1f;
            return percentage >= 0
                ? 1f / (1f + percentage / 100f)
                : 1f - percentage / 100f;
        }
    }

    [HarmonyPatch(typeof(CharacterManager), "CalculateSpawnTime")]
    internal static class PatientArrivalPatch
    {
        private static readonly FieldInfo PrestigeTrackerField =
            AccessTools.Field(typeof(CharacterManager), "_prestigeTracker");
        private static readonly FieldInfo ReputationTrackerField =
            AccessTools.Field(typeof(CharacterManager), "_reputationTracker");
        private static readonly FieldInfo ConfigField =
            AccessTools.Field(typeof(CharacterManager), "_config");
        private static readonly FieldInfo ReputationMinimumField =
            AccessTools.Field(typeof(CharacterManager.Config), "_reputationArrivalRateMultiplierMin");
        private static readonly FieldInfo ReputationMaximumField =
            AccessTools.Field(typeof(CharacterManager.Config), "_reputationArrivalRateMultiplierMax");

        private static void Postfix(CharacterManager __instance, ref float __result)
        {
            if (UnderPressurePlugin.ShouldSeparateReputationAndPrestige)
            {
                // Elimina únicamente el multiplicador nativo de prestigio.
                var prestige = PrestigeTrackerField?.GetValue(__instance) as PrestigeTracker;
                var prestigeFactor = prestige?.Data?.PatientArrivalRate ?? 1f;
                if (prestigeFactor > 0f)
                    __result *= prestigeFactor;

                // Duplica la bonificación de reputación sin reducir nunca la frecuencia base.
                var reputation = ReputationTrackerField?.GetValue(__instance) as ReputationTracker;
                var config = ConfigField?.GetValue(__instance) as CharacterManager.Config;
                var minimum = ReputationMinimumField != null && config != null
                    ? (float)ReputationMinimumField.GetValue(config) : 1f;
                var maximum = ReputationMaximumField != null && config != null
                    ? (float)ReputationMaximumField.GetValue(config) : 2f;
                var normalFactor = Mathf.Lerp(minimum, maximum,
                    reputation?.OverallReputation ?? 0f);
                var reinforcedFactor = minimum + 2f * (normalFactor - minimum);
                if (normalFactor > 0f && reinforcedFactor > 0f)
                    __result *= normalFactor / reinforcedFactor;
            }

            // Bajo Presión modifica al final la frecuencia ya calculada por el juego.
            var factor = GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                UnderPressurePlugin.PatientArrivalSetting));
            __result = factor <= 0f ? float.MaxValue : __result / factor;
        }
    }

    [HarmonyPatch(typeof(IllnessDefinition), "GetAttributeMultiplier")]
    internal static class PatientHealthDeteriorationPatch
    {
        private static void Postfix(CharacterAttributes.Type __0, ref float __result)
        {
            if (string.Equals(__0.ToString(), "Health", StringComparison.OrdinalIgnoreCase))
                __result *= GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                    UnderPressurePlugin.HealthDecaySetting));
        }
    }

    internal static class DiagnosisEffectiveness
    {
        internal static void Apply(ref DiagnosisCalculationBreakdown result)
        {
            result.Certainty = Mathf.Clamp(result.Certainty *
                GameplayModifier.DifficultyFactor(AdaptiveDifficulty.GetValue(
                    UnderPressurePlugin.DiagnosisChanceSetting)), 0f, 100f);
        }
    }

    [HarmonyPatch(typeof(GameAlgorithms), "GetDiagnosisCertainty",
        new[] { typeof(Patient), typeof(Room), typeof(ResearchManager) })]
    internal static class DiagnosisEffectivenessWithoutStaffPatch
    {
        private static void Postfix(ref DiagnosisCalculationBreakdown __result) =>
            DiagnosisEffectiveness.Apply(ref __result);
    }

    [HarmonyPatch(typeof(GameAlgorithms), "GetDiagnosisCertainty",
        new[] { typeof(Patient), typeof(Room), typeof(Staff), typeof(ResearchManager) })]
    internal static class DiagnosisEffectivenessWithStaffPatch
    {
        private static void Postfix(ref DiagnosisCalculationBreakdown __result) =>
            DiagnosisEffectiveness.Apply(ref __result);
    }

    [HarmonyPatch(typeof(GameAlgorithms), "CalculateEstimatedTreatmentOutcome")]
    internal static class TreatmentChancePatch
    {
        private static void Postfix(ref TreatmentCalculationBreakdown __result)
        {
            var factor = GameplayModifier.DifficultyFactor(AdaptiveDifficulty.GetValue(
                UnderPressurePlugin.TreatmentChanceSetting));
            __result.MinChanceOfSuccess = Mathf.Clamp(__result.MinChanceOfSuccess * factor, 0f, 100f);
            __result.ChanceOfSuccess = Mathf.Clamp(__result.ChanceOfSuccess * factor, 0f, 100f);
        }
    }

    [HarmonyPatch(typeof(ObjectAttributeModifier), "Apply")]
    internal static class MachineWearPatch
    {
        private static void Prefix(IAttributesInterface __0, out float __state)
        {
            var item = __0 as RoomItem;
            __state = item != null ? item.MaintenanceLevel.Value() : float.NaN;
        }

        private static void Postfix(IAttributesInterface __0, float __state)
        {
            var item = __0 as RoomItem;
            if (item == null || float.IsNaN(__state)) return;
            var delta = item.MaintenanceLevel.Value() - __state;
            var percentage = UnderPressurePlugin.IsModEnabled
                ? AdaptiveDifficulty.GetValue(UnderPressurePlugin.MachineWearSetting)
                : 0;
            if (delta > 0f && percentage != 0)
                item.MaintenanceLevel.Modify(delta * percentage / 100f, 1f);
        }
    }

    [HarmonyPatch(typeof(Staff), "GetSalary")]
    internal static class EmployeeSalaryPatch
    {
        internal static float SalaryFactor => Mathf.Max(0.01f,
            GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                UnderPressurePlugin.StaffSalariesSetting)));

        private static void Postfix(ref int __result)
        {
            __result = Mathf.Max(0, Mathf.RoundToInt(__result * SalaryFactor));
        }
    }

    [HarmonyPatch(typeof(GameAlgorithms), "CalculateDesiredSalary", new[]
    {
        typeof(StaffDefinition), typeof(int), typeof(float), typeof(List<QualificationSlot>),
        typeof(CharacterTraits), typeof(float)
    })]
    internal static class EmployeeDesiredSalaryPatch
    {
        private static void Postfix(ref int __result)
        {
            if (StaffBaseSalaryStoragePatch.IsStoringBaseSalary) return;
            __result = Mathf.Max(0, Mathf.RoundToInt(__result * EmployeeSalaryPatch.SalaryFactor));
        }
    }

    // El constructor debe guardar el sueldo base; todas las lecturas públicas se escalan después.
    [HarmonyPatch]
    internal static class StaffBaseSalaryStoragePatch
    {
        [ThreadStatic]
        private static int _storageDepth;

        internal static bool IsStoringBaseSalary => _storageDepth > 0;

        private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(Staff), new[]
        {
            typeof(JobApplicant), typeof(Level), typeof(VisualManager), typeof(int),
            typeof(Vector3), typeof(bool)
        });

        private static void Prefix() => ++_storageDepth;

        private static Exception Finalizer(Exception __exception)
        {
            if (_storageDepth > 0) --_storageDepth;
            return __exception;
        }
    }

    // Los controles pasan importes visibles; el almacenamiento interno permanece sin escalar.
    [HarmonyPatch]
    internal static class DisplayedSalaryInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InspectorSubItemStaffInfo), "OnPayRiseConfirm");
            yield return AccessTools.Method(typeof(StaffMenuPayReviewRow), "GivePayRise");
            yield return AccessTools.Method(typeof(StaffMenuPayReviewRow), "IncreasePay");
            yield return AccessTools.Method(typeof(StaffMenuPayReviewRow), "SatisfyPayRequest");
            yield return AccessTools.Method(typeof(StaffMenuPayReviewRow), "Revert");
            yield return AccessTools.Method(typeof(Staff), "Promote");
        }

        private static void SetDisplayedSalary(Staff staff, int amount, bool silent)
        {
            staff.SetSalary(Mathf.RoundToInt(amount / EmployeeSalaryPatch.SalaryFactor), silent);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Staff), "SetSalary", new[] { typeof(int), typeof(bool) });
            var replacement = AccessTools.Method(typeof(DisplayedSalaryInputPatch), nameof(SetDisplayedSalary));
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(InspectorSubItemStaffInfo), "Update")]
    internal static class AdaptiveSalaryInspectorRefreshPatch
    {
        private static readonly FieldInfo SliderField =
            AccessTools.Field(typeof(InspectorSubItemStaffInfo), "_payRiseSlider");
        private static readonly FieldInfo StaffField =
            AccessTools.Field(typeof(InspectorSubItemStaffInfo), "_staff");
        private static readonly MethodInfo ResetSliderMethod =
            AccessTools.Method(typeof(InspectorSubItemStaffInfo), "ResetPayRiseSlider");

        private static void Prefix(InspectorSubItemStaffInfo __instance)
        {
            var slider = SliderField?.GetValue(__instance) as UnityEngine.UI.Slider;
            var staff = StaffField?.GetValue(__instance) as Staff;
            if (slider == null || staff == null || ResetSliderMethod == null ||
                slider.normalizedValue > 0.0001f) return;
            if (!Mathf.Approximately(slider.minValue, staff.GetSalary()))
                ResetSliderMethod.Invoke(__instance, null);
        }
    }

    [HarmonyPatch(typeof(StaffMenuPayReviewRow), "Refresh")]
    internal static class AdaptiveSalaryReviewRefreshPatch
    {
        private static readonly FieldInfo StaffField =
            AccessTools.Field(typeof(StaffMenuRowBase), "<Staff>k__BackingField");
        private static readonly FieldInfo MaximumSalaryField =
            AccessTools.Field(typeof(StaffMenuPayReviewRow), "_maxSalary");

        private static void Prefix(StaffMenuPayReviewRow __instance)
        {
            var staff = StaffField?.GetValue(__instance) as Staff;
            if (staff == null || MaximumSalaryField == null) return;
            var maximum = Mathf.RoundToInt(staff.GetDesiredSalary() *
                (1f + GameAlgorithms.Config.MaxDesiredSalary));
            MaximumSalaryField.SetValue(__instance, maximum);
        }
    }

    [HarmonyPatch(typeof(FinanceManager), "get_LocalMarketRateModifier")]
    internal static class PatientIncomePatch
    {
        internal static float IncomeFactor => GameplayModifier.DifficultyFactor(
            AdaptiveDifficulty.GetValue(UnderPressurePlugin.PatientIncomeSetting));

        private static void Postfix(ref float __result) => __result *= IncomeFactor;
    }

    [HarmonyPatch(typeof(FinanceManager), "GetDiagnosisCharge")]
    internal static class DisableDiagnosisChargesPatch
    {
        private static bool Prefix(ref int __result)
        {
            if (!UnderPressurePlugin.ShouldDisableDiagnosisCharges) return true;
            __result = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(FinanceManager), "GetDiagnosisBaseCharge")]
    internal static class DisableDiagnosisBaseChargePatch
    {
        private static bool Prefix(ref int __result)
        {
            if (!UnderPressurePlugin.ShouldDisableDiagnosisCharges) return true;
            __result = 0;
            return false;
        }
    }

    // Reconstruct the pre-mod comparison prices for the game's native pay decision.
    // Actual charges remain reduced, but an unchanged markup does not make patients happier.
    [HarmonyPatch]
    internal static class PatientPaymentTolerancePatch
    {
        private static readonly MethodInfo NativePayDecision =
            AccessTools.Method(typeof(FinanceManager), "IsCharacterHappyToPay");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FinanceManager), "OnPatientReceivedDiagnosis");
            yield return AccessTools.Method(typeof(FinanceManager), "OnPatientReceivedTreatment");
        }

        private static bool IsHappyToPay(FinanceManager finance, Character patient,
            int actualPrice, int basePrice)
        {
            var factor = PatientIncomePatch.IncomeFactor;
            return (bool)NativePayDecision.Invoke(finance, new object[]
            {
                patient, Mathf.RoundToInt(actualPrice / factor),
                Mathf.RoundToInt(basePrice / factor)
            });
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(FinanceManager), "IsCharacterHappyToPay");
            var replacement = AccessTools.Method(typeof(PatientPaymentTolerancePatch), nameof(IsHappyToPay));
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(JobApplicantPool.Config), "GetTimeUntilNextApplicant")]
    internal static class ApplicantWaitPatch
    {
        private static readonly FieldInfo BaseApplicantTimeField =
            AccessTools.Field(typeof(JobApplicantPool.Config), "TimeUntilNextApplicant");

        private static bool Prefix(JobApplicantPool.Config __instance, ref float __result)
        {
            if (!UnderPressurePlugin.ShouldSeparateReputationAndPrestige || BaseApplicantTimeField == null)
                return true;
            __result = (float)BaseApplicantTimeField.GetValue(__instance);
            return false;
        }

        private static void Postfix(ref float __result)
        {
            __result *= Mathf.Max(0.25f,
                GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                    UnderPressurePlugin.ApplicantWaitSetting)));
        }
    }

    [HarmonyPatch(typeof(Character), "GetAttributeModifierOverTime")]
    internal static class PassiveNeedsPatch
    {
        private static void Postfix(string __0, ref float __result)
        {
            if (string.Equals(__0, "Hunger", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(__0, "Thirst", StringComparison.OrdinalIgnoreCase))
                __result *= GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                    UnderPressurePlugin.HungerThirstSetting));
            else if (string.Equals(__0, "Happiness", StringComparison.OrdinalIgnoreCase))
                __result *= GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                    UnderPressurePlugin.HappinessSetting));
        }
    }

    [HarmonyPatch(typeof(CharacterHappinessComponent), "CalculateHappinessModifier")]
    internal static class HappinessRatePatch
    {
        private static void Postfix(ref float __result)
        {
            __result *= GameplayModifier.Factor(AdaptiveDifficulty.GetValue(
                UnderPressurePlugin.HappinessSetting));
        }
    }

    [HarmonyPatch(typeof(Character), "UpdateAttributes")]
    internal static class HygieneDeteriorationPatch
    {
        private static void Prefix(Character __instance, out float __state)
        {
            var attribute = __instance.GetCharacterAttributes()
                ?.GetAttribute(CharacterAttributes.Type.Hygiene);
            __state = attribute != null ? attribute.Value() : float.NaN;
        }

        private static void Postfix(Character __instance, float __state)
        {
            if (float.IsNaN(__state)) return;
            var attribute = __instance.GetCharacterAttributes()
                ?.GetAttribute(CharacterAttributes.Type.Hygiene);
            if (attribute == null) return;
            var delta = attribute.Value() - __state;
            var percentage = UnderPressurePlugin.IsModEnabled
                ? AdaptiveDifficulty.GetValue(UnderPressurePlugin.HygieneSetting)
                : 0;
            if (delta < 0f && percentage != 0)
                attribute.Modify(delta * percentage / 100f, 1f);
        }
    }
}
