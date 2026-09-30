using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    [HarmonyPatch(typeof(HospitalHUDManager), "ShowYearlyReviewMenu")]
    internal static class DisableAwardsEventPatch
    {
        private static readonly FieldInfo LevelField =
            AccessTools.Field(typeof(HospitalHUDManager), "_level");

        private static bool Prefix(HospitalHUDManager __instance)
        {
            if (!UnderPressurePlugin.ShouldDisableAwards) return true;

            // Conserva el cierre anual de contadores sin abrir la ceremonia ni conceder premios.
            var level = LevelField?.GetValue(__instance) as Level;
            level?.HospitalAwardsManager?.OnEndAwardsCeremony();
            return false;
        }
    }

    [HarmonyPatch(typeof(HospitalAwardsManager), "CalculatePendingAwards")]
    internal static class DisablePendingAwardsPatch
    {
        private static bool Prefix(HospitalAwardsManager __instance)
        {
            if (!UnderPressurePlugin.ShouldDisableAwards) return true;
            __instance.PendingAwards?.Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(HospitalAwardsManager), "ProcessAwards")]
    internal static class DisableAwardProcessingPatch
    {
        private static bool Prefix(ref List<HospitalAwardsManager.SimpleAwardInfo> __0)
        {
            if (!UnderPressurePlugin.ShouldDisableAwards) return true;
            __0?.Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(HospitalAwardsManager), "GiveReward")]
    internal static class DisableAwardRewardsPatch
    {
        private static bool Prefix() => !UnderPressurePlugin.ShouldDisableAwards;
    }

    [HarmonyPatch(typeof(HospitalAwardsManager), "SetAwardWon")]
    internal static class DisableAwardRecordsPatch
    {
        private static bool Prefix() => !UnderPressurePlugin.ShouldDisableAwards;
    }
}
