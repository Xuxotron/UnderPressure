using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace UnderPressure
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class UnderPressurePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure";
        public const string PluginName = "Bajo Presión";
        public const string PluginVersion = "0.1.0";

        private Harmony _harmony;

        internal static ConfigEntry<bool> EnabledSetting { get; private set; }
        internal static ConfigEntry<bool> SkipIntroScreensSetting { get; private set; }
        internal static ConfigEntry<bool> DisableTutorialSetting { get; private set; }
        internal static ConfigEntry<bool> ShowRoomEffectivenessSetting { get; private set; }
        internal static ConfigEntry<bool> ImperfectStaffSetting { get; private set; }
        internal static ConfigEntry<bool> ShowElectricitySetting { get; private set; }
        internal static ConfigEntry<bool> SharedRoomTemplatesSetting { get; private set; }
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
        internal static ConfigEntry<int> HappinessSetting { get; private set; }
        internal static ConfigEntry<int> HygieneSetting { get; private set; }
        internal static ConfigEntry<int> PatientIncomeSetting { get; private set; }
        internal static ConfigEntry<int> ApplicantWaitSetting { get; private set; }
        internal static ConfigEntry<int> ElectricityBillSetting { get; private set; }
        internal static bool IsModEnabled => EnabledSetting == null || EnabledSetting.Value;
        internal static bool ShouldSkipIntroScreens =>
            IsModEnabled && (SkipIntroScreensSetting == null || SkipIntroScreensSetting.Value);
        internal static bool ShouldDisableTutorial =>
            IsModEnabled && DisableTutorialSetting != null && DisableTutorialSetting.Value;
        internal static bool ShouldShowRoomEffectiveness =>
            IsModEnabled && (ShowRoomEffectivenessSetting == null || ShowRoomEffectivenessSetting.Value);
        internal static bool ShouldUseImperfectStaff =>
            IsModEnabled && ImperfectStaffSetting != null && ImperfectStaffSetting.Value;
        internal static bool ShouldShowElectricity =>
            IsModEnabled && ShowElectricitySetting != null && ShowElectricitySetting.Value;
        internal static bool ShouldShareRoomTemplates =>
            IsModEnabled && (SharedRoomTemplatesSetting == null || SharedRoomTemplatesSetting.Value);
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
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
                "Sustituye Activos materiales por la factura eléctrica en la gráfica y muestra el gasto en objetos.");
            SharedRoomTemplatesSetting = Config.Bind(
                "Interfaz", "SharedRoomTemplates", true,
                "Muestra las plantillas globales de salas en cualquier slot, aunque sus elementos aún no estén desbloqueados en esa partida.");
            AdaptiveEconomySetting = Config.Bind("Dificultad adaptable", "Economy", false,
                "Reserva la dificultad económica para su ajuste automático.");
            AdaptiveReputationSetting = Config.Bind("Dificultad adaptable", "Reputation", false,
                "Reserva la dificultad de reputación para su ajuste automático.");
            AdaptiveExpansionSetting = Config.Bind("Dificultad adaptable", "Expansion", false,
                "Reserva la dificultad de expansión para su ajuste automático.");
            GlobalDifficultySetting = BindPercentage("GlobalDifficulty", "Control conjunto de todos los modificadores de dificultad.");
            PatientArrivalSetting = BindPercentage("PatientVisitFrequency", "Frecuencia de visitas de pacientes.");
            HealthDecaySetting = BindPercentage("PatientHealthDeterioration", "Velocidad de deterioro de la salud de los pacientes.");
            DiagnosisChanceSetting = BindPercentage("DiagnosisEffectiveness", "Dificultad global del diagnóstico. Los valores positivos reducen su efectividad.");
            TreatmentChanceSetting = BindPercentage("TreatmentSuccessChance", "Riesgo global de fallo del tratamiento. Los valores positivos reducen su probabilidad de éxito.");
            MachineWearSetting = BindPercentage("MachineWear", "Velocidad de desgaste de las máquinas.");
            StaffSalariesSetting = BindPercentage("EmployeeSalaries", "Salarios pagados a los empleados.");
            HungerThirstSetting = BindPercentage("HungerAndThirst", "Velocidad de aumento del hambre y la sed.");
            HappinessSetting = BindPercentage("Happiness", "Velocidad de cambio de la felicidad.");
            HygieneSetting = BindPercentage("Hygiene", "Velocidad de deterioro de la higiene.");
            PatientIncomeSetting = BindPercentage("PatientIncomeDifficulty", "Reduce conjuntamente los pagos y la tolerancia del paciente al sobreprecio.");
            ApplicantWaitSetting = BindPercentage("ApplicantWait", "Tiempo entre candidatos a empleados.");
            ElectricityBillSetting = BindPercentage("ElectricityBill", "Importe de la factura mensual de electricidad.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private ConfigEntry<int> BindPercentage(string key, string description)
        {
            return Config.Bind("Dificultad", key, 0, new ConfigDescription(description,
                new AcceptableValueRange<int>(-100, 100)));
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
