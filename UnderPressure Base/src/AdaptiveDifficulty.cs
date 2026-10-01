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
        private static int CurrentEconomyValue;
        private static int CurrentReputationValue;
        private static int CurrentExpansionValue;
        private static bool HasEconomyValue;
        private static bool HasReputationValue;
        private static bool HasExpansionValue;

        internal static void Attach(Level level)
        {
            if (level == null) return;
            CurrentLevel = level;
            if (Active.ContainsKey(level))
            {
                UpdateAll(level);
                return;
            }
            if (level.FinanceManager == null ||
                level.ReputationTracker == null || level.PrestigeTracker == null) return;
            var subscription = new Subscription { Level = level };
            subscription.BalanceChanged = unused => UpdateEconomy(level);
            subscription.ReputationChanged = unused => UpdateReputation(level);
            subscription.LevelChanged = unused => UpdateExpansion(level);
            level.FinanceManager.OnBalanceUpdated += subscription.BalanceChanged;
            level.ReputationTracker.OnReputationChangedEvent += subscription.ReputationChanged;
            level.PrestigeTracker.OnPrestigeChangedEvent += subscription.LevelChanged;
            Active.Add(level, subscription);
            UpdateAll(level);
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
            {
                CurrentLevel = null;
                HasEconomyValue = false;
                HasReputationValue = false;
                HasExpansionValue = false;
            }
        }

        internal static int GetValue(BepInEx.Configuration.ConfigEntry<int> setting,
            Level level = null)
        {
            if (setting == null)
                return 0;

            level ??= CurrentLevel;

            if (UnderPressurePlugin.AdaptiveEconomySetting?.Value == true &&
                IsEconomySetting(setting))
                return level?.FinanceManager != null
                    ? EconomyValue(level)
                    : HasEconomyValue ? CurrentEconomyValue : 0;
            if (UnderPressurePlugin.AdaptiveReputationSetting?.Value == true &&
                IsReputationSetting(setting))
                return level?.ReputationTracker != null
                    ? ReputationValue(level)
                    : HasReputationValue ? CurrentReputationValue : 0;
            if (UnderPressurePlugin.AdaptiveExpansionSetting?.Value == true &&
                IsExpansionSetting(setting))
                return level?.PrestigeTracker != null
                    ? ExpansionValue(level)
                    : HasExpansionValue ? CurrentExpansionValue : 0;

            return setting.Value;
        }

        internal static void RefreshAll()
        {
            foreach (var subscription in Active.Values)
                UpdateAll(subscription.Level);
        }

        private static void UpdateAll(Level level)
        {
            UpdateEconomy(level);
            UpdateReputation(level);
            UpdateExpansion(level);
        }

        private static void UpdateEconomy(Level level)
        {
            if (level?.FinanceManager == null) return;
            CurrentEconomyValue = EconomyValue(level);
            HasEconomyValue = true;
        }

        private static void UpdateReputation(Level level)
        {
            if (level?.ReputationTracker == null) return;
            CurrentReputationValue = ReputationValue(level);
            HasReputationValue = true;
        }

        private static void UpdateExpansion(Level level)
        {
            if (level?.PrestigeTracker == null) return;
            CurrentExpansionValue = ExpansionValue(level);
            HasExpansionValue = true;
        }

        internal static int EconomyValue(Level level) =>
            Mathf.Clamp(level.FinanceManager.Balance / 5000, 0, 100);

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
