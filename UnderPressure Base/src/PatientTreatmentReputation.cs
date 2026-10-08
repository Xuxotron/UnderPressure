// Propósito: aplica valores fijos de reputación a los resultados de pacientes.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    internal static class PatientTreatmentReputation
    {
        private const float TreatmentSuccess = 1f;
        private const float TreatmentIneffective = -1f;
        private const float TreatmentDeath = -3f;
        private const float PatientRageQuit = -2f;
        private const float PatientWaitTooLong = -1f;

        private static readonly FieldInfo TreatmentSuccessField =
            AccessTools.Field(typeof(IllnessDefinition), "_reputationTreatmentSuccess");
        private static readonly FieldInfo TreatmentIneffectiveField =
            AccessTools.Field(typeof(IllnessDefinition), "_reputationTreatmentIneffective");
        private static readonly FieldInfo TreatmentDeathField =
            AccessTools.Field(typeof(IllnessDefinition), "_reputationTreatmentDeath");
        private static readonly FieldInfo PatientRageQuitField =
            AccessTools.Field(typeof(IllnessDefinition), "_reputationPatientRageQuit");
        private static readonly FieldInfo PatientWaitTooLongField =
            AccessTools.Field(typeof(IllnessDefinition), "_reputationPatientWaitTooLong");
        private static readonly FieldInfo IllnessesField =
            AccessTools.Field(typeof(CharacterManager), "_illnesses");
        private static readonly FieldInfo AmbulanceIllnessesField =
            AccessTools.Field(typeof(CharacterManager), "_illnessesAmbulance");
        private static readonly FieldInfo TimePortalIllnessesField =
            AccessTools.Field(typeof(CharacterManager), "_illnessesTimePortal");

        private static readonly Dictionary<IllnessDefinition, ReputationValues> OriginalValues =
            new Dictionary<IllnessDefinition, ReputationValues>();

        private static bool FieldsAvailable =>
            TreatmentSuccessField != null && TreatmentIneffectiveField != null &&
            TreatmentDeathField != null && PatientRageQuitField != null &&
            PatientWaitTooLongField != null;

        internal static void Refresh()
        {
            if (!FieldsAvailable)
                return;

            var enabled = UnderPressurePlugin.ShouldUsePatientTreatment;
            foreach (var pair in OriginalValues)
                ApplyValues(pair.Key, enabled ? FixedValues : pair.Value);
        }

        internal static void ApplyCurrentState(IllnessDefinition illness)
        {
            if (!FieldsAvailable || illness == null)
                return;

            Apply(illness, UnderPressurePlugin.ShouldUsePatientTreatment);
        }

        internal static void Register(CharacterManager manager)
        {
            if (manager == null)
                return;

            Register(IllnessesField?.GetValue(manager) as IDictionary);
            Register(AmbulanceIllnessesField?.GetValue(manager) as IDictionary);
            Register(TimePortalIllnessesField?.GetValue(manager) as IDictionary);
        }

        internal static void RestoreAll()
        {
            if (!FieldsAvailable)
                return;

            foreach (var pair in OriginalValues)
                if (pair.Key != null)
                    ApplyValues(pair.Key, pair.Value);
            OriginalValues.Clear();
        }

        private static ReputationValues FixedValues =>
            new ReputationValues(TreatmentSuccess, TreatmentIneffective,
                TreatmentDeath, PatientRageQuit, PatientWaitTooLong);

        private static void Register(IDictionary illnesses)
        {
            if (illnesses == null)
                return;

            foreach (DictionaryEntry entry in illnesses)
                ApplyCurrentState(entry.Key as IllnessDefinition);
        }

        private static void Apply(IllnessDefinition illness, bool enabled)
        {
            if (illness == null)
                return;

            if (!OriginalValues.TryGetValue(illness, out var original))
            {
                original = Read(illness);
                OriginalValues.Add(illness, original);
            }

            ApplyValues(illness, enabled ? FixedValues : original);
        }

        private static ReputationValues Read(IllnessDefinition illness) =>
            new ReputationValues(
                (float)TreatmentSuccessField.GetValue(illness),
                (float)TreatmentIneffectiveField.GetValue(illness),
                (float)TreatmentDeathField.GetValue(illness),
                (float)PatientRageQuitField.GetValue(illness),
                (float)PatientWaitTooLongField.GetValue(illness));

        private static void ApplyValues(IllnessDefinition illness, ReputationValues values)
        {
            TreatmentSuccessField.SetValue(illness, values.TreatmentSuccess);
            TreatmentIneffectiveField.SetValue(illness, values.TreatmentIneffective);
            TreatmentDeathField.SetValue(illness, values.TreatmentDeath);
            PatientRageQuitField.SetValue(illness, values.PatientRageQuit);
            PatientWaitTooLongField.SetValue(illness, values.PatientWaitTooLong);
        }

        private readonly struct ReputationValues
        {
            internal readonly float TreatmentSuccess;
            internal readonly float TreatmentIneffective;
            internal readonly float TreatmentDeath;
            internal readonly float PatientRageQuit;
            internal readonly float PatientWaitTooLong;

            internal ReputationValues(float treatmentSuccess, float treatmentIneffective,
                float treatmentDeath, float patientRageQuit, float patientWaitTooLong)
            {
                TreatmentSuccess = treatmentSuccess;
                TreatmentIneffective = treatmentIneffective;
                TreatmentDeath = treatmentDeath;
                PatientRageQuit = patientRageQuit;
                PatientWaitTooLong = patientWaitTooLong;
            }
        }
    }

    [HarmonyPatch]
    internal static class PatientTreatmentIllnessRegistrationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CharacterManager), "AddIllness");
            yield return AccessTools.Method(typeof(CharacterManager), "AddAmbulanceIllness");
            yield return AccessTools.Method(typeof(CharacterManager), "AddIllnessTimePortal");
        }

        private static void Postfix(IllnessDefinition __0) =>
            PatientTreatmentReputation.ApplyCurrentState(__0);
    }

    [HarmonyPatch(typeof(CharacterManager), "OverrideIllnesses")]
    internal static class PatientTreatmentIllnessOverridePatch
    {
        private static void Postfix(CharacterManager __instance) =>
            PatientTreatmentReputation.Register(__instance);
    }

    [HarmonyPatch]
    internal static class PatientTreatmentCharacterManagerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(CharacterManager).GetConstructors(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);

        private static void Postfix(CharacterManager __instance) =>
            PatientTreatmentReputation.Register(__instance);
    }
}
