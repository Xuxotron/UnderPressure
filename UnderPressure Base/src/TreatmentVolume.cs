using System;
using System.Collections.Generic;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play), typeof(string), typeof(GameObject))]
    internal static class TreatmentVolumePatch
    {
        private static readonly HashSet<string> TreatmentResultEvents = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "SFX_Treatment_Failed_2",
            "SFX_Treatment_Ineffective_2",
            "SFX_Treatment_Successful_2"
        };

        private static void Postfix(ref AudioEmitter __result)
        {
            var eventName = __result?.AudioEvent?.EventName;
            if (string.IsNullOrEmpty(eventName) || !TreatmentResultEvents.Contains(eventName)) return;

            var setting = UnderPressurePlugin.TreatmentVolumeSetting;
            var factor = !UnderPressurePlugin.IsModEnabled || setting == null
                ? 1f
                : Mathf.Clamp01(setting.Value / 100f);
            __result.Volume *= factor;
        }
    }
}
