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
        private sealed class Subscription
        {
            internal Level Level;
            internal Action<int> BalanceChanged;
            internal Action<float> ReputationChanged;
            internal Action<PrestigeTracker> LevelChanged;
        }

        private static readonly Dictionary<Level, Subscription> Active =
            new Dictionary<Level, Subscription>();
        private static Level CurrentLevel;

        internal static void Attach(Level level)
        {
            if (level == null || Active.ContainsKey(level) || level.FinanceManager == null ||
                level.ReputationTracker == null || level.PrestigeTracker == null) return;
            var subscription = new Subscription { Level = level };
            subscription.BalanceChanged = unused => ApplyEconomy(level);
            subscription.ReputationChanged = unused => ApplyReputation(level);
            subscription.LevelChanged = unused => ApplyExpansion(level);
            level.FinanceManager.OnBalanceUpdated += subscription.BalanceChanged;
            level.ReputationTracker.OnReputationChangedEvent += subscription.ReputationChanged;
            level.PrestigeTracker.OnPrestigeChangedEvent += subscription.LevelChanged;
            Active.Add(level, subscription);
            CurrentLevel = level;
            ApplyAll(level);
        }

        internal static void Detach(Level level)
        {
            if (level == null || !Active.TryGetValue(level, out var subscription)) return;
            if (level.FinanceManager != null)
                level.FinanceManager.OnBalanceUpdated -= subscription.BalanceChanged;
            if (level.ReputationTracker != null)
                level.ReputationTracker.OnReputationChangedEvent -= subscription.ReputationChanged;
            if (level.PrestigeTracker != null)
                level.PrestigeTracker.OnPrestigeChangedEvent -= subscription.LevelChanged;
            Active.Remove(level);
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

        internal static void RefreshAll()
        {
            foreach (var subscription in Active.Values)
                ApplyAll(subscription.Level);
        }

        private static void ApplyAll(Level level)
        {
            ApplyEconomy(level);
            ApplyReputation(level);
            ApplyExpansion(level);
        }

        private static void ApplyEconomy(Level level)
        {
            if (UnderPressurePlugin.AdaptiveEconomySetting?.Value != true) return;
            Set(EconomyValue(level), UnderPressurePlugin.StaffSalariesSetting,
                UnderPressurePlugin.PatientIncomeSetting, UnderPressurePlugin.ApplicantWaitSetting,
                UnderPressurePlugin.ElectricityBillSetting);
        }

        private static void ApplyReputation(Level level)
        {
            if (UnderPressurePlugin.AdaptiveReputationSetting?.Value != true) return;
            Set(ReputationValue(level), UnderPressurePlugin.HungerThirstSetting,
                UnderPressurePlugin.HappinessSetting, UnderPressurePlugin.HygieneSetting,
                UnderPressurePlugin.HealthDecaySetting);
        }

        private static void ApplyExpansion(Level level)
        {
            if (UnderPressurePlugin.AdaptiveExpansionSetting?.Value != true) return;
            Set(ExpansionValue(level), UnderPressurePlugin.DiagnosisChanceSetting,
                UnderPressurePlugin.TreatmentChanceSetting, UnderPressurePlugin.PatientArrivalSetting,
                UnderPressurePlugin.MachineWearSetting);
        }

        private static void Set(int value,
            params BepInEx.Configuration.ConfigEntry<int>[] settings)
        {
            foreach (var setting in settings)
                if (setting != null && setting.Value != value)
                    setting.Value = value;
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
