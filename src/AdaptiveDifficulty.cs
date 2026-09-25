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
        }

        internal static void RefreshAll()
        {
            foreach (var subscription in Active.Values) ApplyAll(subscription.Level);
        }

        private static void ApplyAll(Level level)
        {
            ApplyEconomy(level);
            ApplyReputation(level);
            ApplyExpansion(level);
        }

        private static void ApplyEconomy(Level level)
        {
            if (UnderPressurePlugin.AdaptiveEconomySetting == null ||
                !UnderPressurePlugin.AdaptiveEconomySetting.Value) return;
            // 0 at no cash, +100 at one million; debt never creates a bonus.
            var value = Mathf.Clamp(level.FinanceManager.Balance / 10000, 0, 100);
            Set(value, UnderPressurePlugin.StaffSalariesSetting,
                UnderPressurePlugin.PatientIncomeSetting, UnderPressurePlugin.ApplicantWaitSetting,
                UnderPressurePlugin.ElectricityBillSetting);
        }

        private static void ApplyReputation(Level level)
        {
            if (UnderPressurePlugin.AdaptiveReputationSetting == null ||
                !UnderPressurePlugin.AdaptiveReputationSetting.Value) return;
            // Reputation is normalised 0..1 internally: every displayed point adds 1%.
            var value = Mathf.Clamp(Mathf.RoundToInt(level.ReputationTracker.OverallReputation * 100f), 0, 100);
            Set(value, UnderPressurePlugin.HungerThirstSetting, UnderPressurePlugin.HappinessSetting,
                UnderPressurePlugin.HygieneSetting, UnderPressurePlugin.HealthDecaySetting);
        }

        private static void ApplyExpansion(Level level)
        {
            if (UnderPressurePlugin.AdaptiveExpansionSetting == null ||
                !UnderPressurePlugin.AdaptiveExpansionSetting.Value) return;
            // Level 20 is the game's last hospital-level achievement and is a useful hard cap.
            var value = Mathf.Clamp(level.PrestigeTracker.Level * 5, 0, 100);
            Set(value, UnderPressurePlugin.DiagnosisChanceSetting,
                UnderPressurePlugin.TreatmentChanceSetting, UnderPressurePlugin.PatientArrivalSetting,
                UnderPressurePlugin.MachineWearSetting);
        }

        private static void Set(int value, params BepInEx.Configuration.ConfigEntry<int>[] settings)
        {
            foreach (var setting in settings)
                if (setting != null && setting.Value != value)
                    setting.Value = value;
        }
    }
}
