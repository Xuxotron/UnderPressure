using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.Audio;

namespace UnderPressure
{
    internal static class TreatmentVolumeAudio
    {
        private static readonly HashSet<string> TreatmentResultEvents = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "SFX_Treatment_Failed_2",
            "SFX_Treatment_Ineffective_2",
            "SFX_Treatment_Successful_2"
        };

        private static readonly HashSet<string> TreatmentResultRequests = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "PatientCured",
            "IneffectiveTreatment",
            "FatalTreatment"
        };

        private static readonly HashSet<AudioEmitter> ActiveEmitters = new HashSet<AudioEmitter>();
        private static App _app;
        private static AudioMixerGroup _masterGroup;
        private static bool _missingMasterGroupReported;

        internal static void SetApp(App app)
        {
            if (ReferenceEquals(_app, app)) return;
            _app = app;
            _masterGroup = null;
            _missingMasterGroupReported = false;
        }

        internal static void Register(AudioEmitter emitter, AudioEvent audioEvent)
        {
            if (emitter == null || audioEvent == null ||
                !TreatmentResultEvents.Contains(audioEvent.EventName)) return;

            ConfigureEmitter(emitter);
        }

        internal static void RegisterRequested(AudioEmitter emitter, string requestedEvent)
        {
            if (emitter == null || string.IsNullOrEmpty(requestedEvent) ||
                !TreatmentResultRequests.Contains(requestedEvent)) return;

            ConfigureEmitter(emitter);
        }

        private static void ConfigureEmitter(AudioEmitter emitter)
        {

            var masterGroup = GetMasterGroup();
            if (masterGroup != null)
            {
                foreach (var source in emitter.GetComponentsInChildren<AudioSource>(true))
                    source.outputAudioMixerGroup = masterGroup;
            }
            else if (!_missingMasterGroupReported)
            {
                _missingMasterGroupReported = true;
                UnderPressurePlugin.Log?.LogWarning(
                    "No se encontró el canal maestro para separar los sonidos de tratamiento de Efectos.");
            }

            ActiveEmitters.Add(emitter);
            ApplyVolume(emitter);
        }

        internal static void RefreshActiveEmitters()
        {
            ActiveEmitters.RemoveWhere(emitter => emitter == null || emitter.Finished);
            foreach (var emitter in ActiveEmitters)
                ApplyVolume(emitter);
        }

        private static void ApplyVolume(AudioEmitter emitter)
        {
            var setting = UnderPressurePlugin.TreatmentVolumeSetting;
            emitter.Volume = !UnderPressurePlugin.IsModEnabled || setting == null
                ? 1f
                : Mathf.Clamp01(setting.Value / 100f);
        }

        private static AudioMixerGroup GetMasterGroup()
        {
            if (_masterGroup != null) return _masterGroup;
            var mixer = _app?.Config?.AppAudioMixerManagerConfig?.AudioMixer;
            if (mixer == null) return null;

            _masterGroup = mixer.FindMatchingGroups("Master")
                .FirstOrDefault(group => group != null &&
                    string.Equals(group.name, "Master", StringComparison.Ordinal));
            return _masterGroup;
        }
    }

    [HarmonyPatch(typeof(AudioEmitter), "SetupAudioEmitter")]
    internal static class TreatmentVolumePatch
    {
        private static void Postfix(AudioEmitter __0, AudioEvent __2)
        {
            TreatmentVolumeAudio.Register(__0, __2);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play), typeof(string), typeof(GameObject))]
    internal static class TreatmentVolumeRequestedEventPatch
    {
        private static void Postfix(string __0, AudioEmitter __result)
        {
            TreatmentVolumeAudio.RegisterRequested(__result, __0);
        }
    }
}
