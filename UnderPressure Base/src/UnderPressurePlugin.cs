// Updated: 2026-10-12
// Purpose: Bootstraps the core Under Pressure plugin, settings, patches, localization, and shared assets.
using System.IO;
using System.Reflection;
using UnderPressure.LoaderRuntime;
using UnderPressure.LoaderRuntime.Configuration;
using UnderPressure.LoaderRuntime.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TH20;
using UnityEngine;
using UnityEngine.Audio;

namespace UnderPressure
{
    [ModPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class UnderPressurePlugin : BaseModComponent
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure";
        public const string PluginName = "Bajo Presión";
        public const string PluginVersion = "0.1.1";

        private Harmony _harmony;

        internal static ConfigEntry<bool> EnabledSetting { get; private set; }
        internal static ConfigEntry<bool> SkipIntroScreensSetting { get; private set; }
        internal static ConfigEntry<bool> AutoLoadLastSaveSetting { get; private set; }
        internal static ConfigEntry<bool> DisableTutorialSetting { get; private set; }
        internal static ConfigEntry<bool> ShowRoomEffectivenessSetting { get; private set; }
        internal static ConfigEntry<bool> ImperfectStaffSetting { get; private set; }
        internal static ConfigEntry<bool> ShowElectricitySetting { get; private set; }
        internal static ConfigEntry<bool> SeparateReputationPrestigeSetting { get; private set; }
        internal static ConfigEntry<bool> PermanentMachineWearSetting { get; private set; }
        internal static ConfigEntry<bool> VisualLightingSetting { get; private set; }
        internal static ConfigEntry<bool> UnlockLampsSetting { get; private set; }
        internal static ConfigEntry<bool> DisableDiagnosisChargesSetting { get; private set; }
        internal static ConfigEntry<bool> DisableAwardsSetting { get; private set; }
        internal static ConfigEntry<bool> ResalePenaltiesSetting { get; private set; }
        internal static ConfigEntry<bool> DismissalSeveranceSetting { get; private set; }
        internal static ConfigEntry<bool> PatientTreatmentSetting { get; private set; }
        internal static ConfigEntry<bool> AdaptiveEconomySetting { get; private set; }
        internal static ConfigEntry<bool> AdaptiveReputationSetting { get; private set; }
        internal static ConfigEntry<bool> AdaptiveExpansionSetting { get; private set; }
        internal static ConfigEntry<int> GlobalDifficultySetting { get; private set; }
        internal static ConfigEntry<int> PatientArrivalSetting { get; private set; }
        internal static ConfigEntry<int> HealthDecaySetting { get; private set; }
        internal static ConfigEntry<int> DiagnosisChanceSetting { get; private set; }
        internal static ConfigEntry<int> TreatmentChanceSetting { get; private set; }
        internal static ConfigEntry<int> MachineWearSetting { get; private set; }
        internal static ConfigEntry<int> StaffSalariesSetting { get; private set; }
        internal static ConfigEntry<int> HungerThirstSetting { get; private set; }
        internal static ConfigEntry<int> BoredomSetting { get; private set; }
        internal static ConfigEntry<int> HappinessSetting { get; private set; }
        internal static ConfigEntry<int> HygieneSetting { get; private set; }
        internal static ConfigEntry<int> PatientIncomeSetting { get; private set; }
        internal static ConfigEntry<int> ApplicantWaitSetting { get; private set; }
        internal static ConfigEntry<int> ElectricityBillSetting { get; private set; }
        internal static ConfigEntry<int> TreatmentVolumeSetting { get; private set; }
        internal static ConfigFile ModConfig { get; private set; }
        internal static bool IsModEnabled => EnabledSetting == null || EnabledSetting.Value;
        internal static bool ShouldSkipIntroScreens =>
            IsModEnabled && (SkipIntroScreensSetting == null || SkipIntroScreensSetting.Value);
        internal static bool ShouldAutoLoadLastSave =>
            IsModEnabled && AutoLoadLastSaveSetting != null && AutoLoadLastSaveSetting.Value;
        internal static bool ShouldDisableTutorial =>
            IsModEnabled && DisableTutorialSetting != null && DisableTutorialSetting.Value;
        internal static bool ShouldShowRoomEffectiveness =>
            IsModEnabled && (ShowRoomEffectivenessSetting == null || ShowRoomEffectivenessSetting.Value);
        internal static bool ShouldUseImperfectStaff =>
            IsModEnabled && ImperfectStaffSetting != null && ImperfectStaffSetting.Value;
        public static bool ElectricityGameplayEnabled =>
            IsModEnabled && ShowElectricitySetting != null && ShowElectricitySetting.Value;
        internal static bool ShouldShowElectricity => ElectricityGameplayEnabled;
        internal static bool ShouldSeparateReputationAndPrestige =>
            IsModEnabled && SeparateReputationPrestigeSetting != null && SeparateReputationPrestigeSetting.Value;
        internal static bool ShouldUsePermanentMachineWear =>
            IsModEnabled && PermanentMachineWearSetting != null && PermanentMachineWearSetting.Value;
        internal static bool ShouldUseVisualLighting =>
            IsModEnabled && VisualLightingSetting != null && VisualLightingSetting.Value;
        internal static bool ShouldUnlockLamps =>
            IsModEnabled && UnlockLampsSetting != null && UnlockLampsSetting.Value;
        internal static bool ShouldDisableDiagnosisCharges =>
            IsModEnabled && DisableDiagnosisChargesSetting != null && DisableDiagnosisChargesSetting.Value;
        internal static bool ShouldDisableAwards =>
            IsModEnabled && DisableAwardsSetting != null && DisableAwardsSetting.Value;
        internal static bool ShouldUseResalePenalties =>
            IsModEnabled && ResalePenaltiesSetting != null && ResalePenaltiesSetting.Value;
        internal static bool ShouldUseDismissalSeverance =>
            IsModEnabled && DismissalSeveranceSetting != null && DismissalSeveranceSetting.Value;
        internal static bool ShouldUsePatientTreatment =>
            IsModEnabled && PatientTreatmentSetting != null && PatientTreatmentSetting.Value;
        internal static ModLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            ModConfig = Config;
            EnabledSetting = Config.Bind(
                "General",
                "Enabled",
                true,
                "Activa o desactiva las funciones de Under Pressure.");
            SkipIntroScreensSetting = Config.Bind(
                "Inicio",
                "SkipIntroScreens",
                true,
                "Omite el vídeo, el logo y el aviso legal iniciales para mostrar directamente la carga.");
            AutoLoadLastSaveSetting = Config.Bind(
                "Inicio",
                "AutoLoadLastSave",
                true,
                "Busca el hospital guardado más reciente entre todas las campañas y lo carga sin mostrar la pantalla inicial, el mapa de campaña ni el selector de hospital.");
            DisableTutorialSetting = Config.Bind(
                "Inicio",
                "DisableTutorial",
                false,
                "Omite StartTutorialMode en el primer nivel. Puede alterar la secuencia del escenario.");
            ShowRoomEffectivenessSetting = Config.Bind(
                "Interfaz",
                "ShowRoomEffectiveness",
                true,
                "Muestra la efectividad de diagnóstico y tratamiento en las salas.");
            ImperfectStaffSetting = Config.Bind(
                "Personal",
                "ImperfectStaff",
                false,
                "Los nuevos candidatos tienen dos rasgos positivos y uno negativo.");
            ShowElectricitySetting = Config.Bind(
                "Interfaz", "ShowElectricity", false,
                ModLocalization.Get("tooltip.show_electricity"));
            SeparateReputationPrestigeSetting = Config.Bind(
                "Jugabilidad", "SeparateReputationAndPrestige", true,
                "La reputación aumenta por sí sola la llegada de pacientes con bonificación doble; el prestigio solo aumenta las plazas de candidatos.");
            PermanentMachineWearSetting = Config.Bind(
                "Jugabilidad", "PermanentMachineWear", true,
                "Cada 100 puntos porcentuales reparados reducen en un 1% el estado máximo reparable de esa máquina.");
            VisualLightingSetting = Config.Bind(
                "Visual", "HospitalLighting", false,
                "Oscurece los interiores y añade iluminación visual procedente de ventanas y lámparas compatibles.");
            UnlockLampsSetting = Config.Bind(
                "Jugabilidad", "UnlockLamps", false,
                "Desbloquea las lámparas de Consulta, Psiquiatría, Sala de personal y Marketing y permite colocarlas en cualquier sala.");
            DisableDiagnosisChargesSetting = Config.Bind(
                "Jugabilidad", "DisableDiagnosisCharges", true,
                ModLocalization.Get("tooltip.disable_diagnosis_charges"));
            DisableAwardsSetting = Config.Bind(
                "Jugabilidad", "DisableAwards", true,
                ModLocalization.Get("tooltip.disable_awards"));
            ResalePenaltiesSetting = Config.Bind(
                "Jugabilidad", "ResalePenalties", true,
                ModLocalization.Get("tooltip.resale_penalties"));
            DismissalSeveranceSetting = Config.Bind(
                "Personal", "DismissalSeverance", true,
                ModLocalization.Get("tooltip.dismissal_severance"));
            PatientTreatmentSetting = Config.Bind(
                "Jugabilidad", "PatientTreatment", true,
                ModLocalization.Get("tooltip.patient_treatment"));
            PatientTreatmentSetting.SettingChanged += (sender, args) =>
                PatientTreatmentReputation.Refresh();
            AdaptiveEconomySetting = Config.Bind("Dificultad adaptable", "Economy", false,
                "Reserva la dificultad económica para su ajuste automático.");
            AdaptiveReputationSetting = Config.Bind("Dificultad adaptable", "Reputation", false,
                "Reserva la dificultad de reputación para su ajuste automático.");
            AdaptiveExpansionSetting = Config.Bind("Dificultad adaptable", "Expansion", false,
                "Reserva la dificultad de expansión para su ajuste automático.");
            GlobalDifficultySetting = BindPercentage("GlobalDifficulty", "Control conjunto de todos los modificadores de dificultad.");
            PatientArrivalSetting = BindPercentage("PatientVisitFrequency", "Frecuencia de visitas de pacientes.");
            PatientArrivalSetting.SettingChanged += (sender, args) =>
                PatientArrivalPatch.RefreshActiveTimer();
            HealthDecaySetting = BindPercentage("PatientHealthDeterioration", "Velocidad de deterioro de la salud de los pacientes.");
            DiagnosisChanceSetting = BindPercentage("DiagnosisEffectiveness", "Dificultad global del diagnóstico. Los valores positivos reducen su efectividad.");
            TreatmentChanceSetting = BindPercentage("TreatmentSuccessChance", "Riesgo global de fallo del tratamiento. Los valores positivos reducen su probabilidad de éxito.");
            MachineWearSetting = BindPercentage("MachineWear", "Velocidad de desgaste de las máquinas.");
            StaffSalariesSetting = BindPercentage("EmployeeSalaries", "Salarios pagados a los empleados.");
            HungerThirstSetting = BindPercentage("HungerAndThirst", "Velocidad de aumento del hambre y la sed.");
            BoredomSetting = BindPercentage("Boredom", "Velocidad de aumento del aburrimiento.");
            HappinessSetting = BindPercentage("Happiness", "Velocidad de cambio de la felicidad.");
            HygieneSetting = BindPercentage("Hygiene", "Velocidad de deterioro de la higiene.");
            PatientIncomeSetting = BindPercentage("PatientIncomeDifficulty", "Reduce conjuntamente los pagos y la tolerancia del paciente al sobreprecio.");
            ApplicantWaitSetting = BindPercentage("ApplicantWait", "Tiempo entre candidatos a empleados.");
            ElectricityBillSetting = BindPercentage("ElectricityBill", "Importe de la factura mensual de electricidad.");
            TreatmentVolumeSetting = Config.Bind(
                "Audio",
                "TreatmentVolume",
                100,
                new ConfigDescription("Volumen de los sonidos de resultado del tratamiento.",
                    new AcceptableValueRange<int>(0, 100)));
            TreatmentVolumeSetting.SettingChanged += (sender, args) =>
                TreatmentVolumeAudio.RefreshActiveEmitters();

            UnderPressureAssetBundle.Initialise(Logger,
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            PatientTreatmentReputation.Refresh();
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void Update()
        {
            UnderPressureAssetBundle.UpdateHotkey();
        }

        private void OnGUI()
        {
            UnderPressureAssetBundle.DrawReloadOverlay();
        }

        private ConfigEntry<int> BindPercentage(string key, string description)
        {
            return Config.Bind("Dificultad", key, 0, new ConfigDescription(description,
                new AcceptableValueRange<int>(-100, 100)));
        }

        private void OnDestroy()
        {
            PatientTreatmentReputation.RestoreAll();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
    // Purpose: Tracks treatment audio emitters and applies the configured treatment-volume scale.
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
    // Purpose: Centralizes switches that disable unfinished features without removing their code.
    /// <summary>
    /// Interruptores provisionales para excluir funciones incompletas de las versiones publicadas.
    /// Cambiar un valor a false reactiva su implementación sin recuperar código eliminado.
    /// </summary>
    public static class DisabledFeatures
    {
        public static readonly bool EnergyCampaignLaunch = true;
        public static readonly bool EnergyRoomDesk = true;
    }
}
