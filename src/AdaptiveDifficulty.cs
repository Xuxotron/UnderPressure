using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    [HarmonyPatch]
    internal static class AdaptiveDifficultyLevelCreatedPatch
    {
        private static MethodBase TargetMethod()
        {
            var constructors = typeof(Level).GetConstructors(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            return constructors.Length == 1 ? constructors[0] : null;
        }

        private static void Postfix(Level __instance) => AdaptiveDifficulty.Attach(__instance);
    }

    [HarmonyPatch(typeof(Level), "Destroy")]
    internal static class AdaptiveDifficultyLevelDestroyedPatch
    {
        private static void Prefix(Level __instance) => AdaptiveDifficulty.Detach(__instance);
    }

    [HarmonyPatch(typeof(Level), "InitialiseGameEvents")]
    internal static class AdaptiveDifficultyLevelInitialisedPatch
    {
        private static void Postfix(Level __instance) => AdaptiveDifficulty.Attach(__instance);
    }

    internal static class AdaptiveDifficulty
    {
        private static readonly HashSet<Level> Active = new HashSet<Level>();
        private static Level CurrentLevel;

        internal static void Attach(Level level)
        {
            if (level == null || Active.Contains(level) || level.FinanceManager == null ||
                level.ReputationTracker == null || level.PrestigeTracker == null) return;
            Active.Add(level);
            CurrentLevel = level;
        }

        internal static void Detach(Level level)
        {
            if (level == null || !Active.Remove(level)) return;
            if (ReferenceEquals(CurrentLevel, level))
                CurrentLevel = null;
        }

        internal static int GetValue(BepInEx.Configuration.ConfigEntry<int> setting,
            Level level = null)
        {
            if (setting == null)
                return 0;

            level ??= CurrentLevel;
            if (level == null)
                return setting.Value;

            if (UnderPressurePlugin.AdaptiveEconomySetting?.Value == true &&
                IsEconomySetting(setting) && level.FinanceManager != null)
                return EconomyValue(level);
            if (UnderPressurePlugin.AdaptiveReputationSetting?.Value == true &&
                IsReputationSetting(setting) && level.ReputationTracker != null)
                return ReputationValue(level);
            if (UnderPressurePlugin.AdaptiveExpansionSetting?.Value == true &&
                IsExpansionSetting(setting) && level.PrestigeTracker != null)
                return ExpansionValue(level);

            return setting.Value;
        }

        private static int EconomyValue(Level level) =>
            Mathf.Clamp(level.FinanceManager.Balance / 10000, 0, 100);

        private static int ReputationValue(Level level) => Mathf.Clamp(Mathf.RoundToInt(
            level.ReputationTracker.OverallReputation * 100f), 0, 100);

        private static int ExpansionValue(Level level) =>
            Mathf.Clamp(level.PrestigeTracker.Level * 5, 0, 100);

        private static bool IsEconomySetting(BepInEx.Configuration.ConfigEntry<int> setting) =>
            ReferenceEquals(setting, UnderPressurePlugin.StaffSalariesSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.PatientIncomeSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.ApplicantWaitSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.ElectricityBillSetting);

        private static bool IsReputationSetting(BepInEx.Configuration.ConfigEntry<int> setting) =>
            ReferenceEquals(setting, UnderPressurePlugin.HungerThirstSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.HappinessSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.HygieneSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.HealthDecaySetting);

        private static bool IsExpansionSetting(BepInEx.Configuration.ConfigEntry<int> setting) =>
            ReferenceEquals(setting, UnderPressurePlugin.DiagnosisChanceSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.TreatmentChanceSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.PatientArrivalSetting) ||
            ReferenceEquals(setting, UnderPressurePlugin.MachineWearSetting);
    }
}
