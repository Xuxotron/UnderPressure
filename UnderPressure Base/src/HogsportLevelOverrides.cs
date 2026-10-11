// Purpose: Documents and applies editable runtime overrides to Hogsport's level configuration.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Configuración editable de Hogsport (nivel interno 901).
    /// Cada inicializador muestra el valor original verificado del juego.
    /// Cambiarlo modifica solamente la copia usada al construir Hogsport; no
    /// altera los assets ni la configuración de los demás hospitales.
    ///
    /// Fuente: TPH_Data/sharedassets3.assets. Assets principales:
    /// R1_TutorialHospital (156482), CharacterManager (156472), Finance
    /// (156473) y ChallengeManager (156471).
    /// </summary>
    [HarmonyPatch]
    internal static class HogsportLevelOverrides
    {
        private const string HogsportLevelId = "901";

        // =========================== ECONOMÍA ================================

        /// <summary>
        /// FinanceManagerConfig.InitialBalance. Dinero de una partida nueva.
        /// Original: 200.000.
        /// </summary>
        internal static int InitialMoney = 200000;

        // ===================== PACIENTES Y LLEGADAS ==========================

        /// <summary>
        /// CharacterManagerConfig._maxPatients. Máximo de pacientes que puede
        /// mantener el gestor; no determina por sí solo su frecuencia.
        /// Original: 250.
        /// </summary>
        internal static int MaximumPatients = 250;

        /// <summary>
        /// CharacterManagerConfig._patientSpawnRate. Valor base usado para
        /// programar llegadas; después intervienen reputación, nivel y azar.
        /// Original: 48.
        /// </summary>
        internal static float PatientSpawnRate = 48f;

        /// <summary>
        /// CharacterManagerConfig._reputationArrivalRateMultiplierMin.
        /// Extremo inferior del multiplicador de llegadas por reputación.
        /// Original: 0,8.
        /// </summary>
        internal static float ReputationArrivalMinimum = 0.8f;

        /// <summary>
        /// CharacterManagerConfig._reputationArrivalRateMultiplierMax.
        /// Extremo superior del multiplicador de llegadas por reputación.
        /// Original: 1,2.
        /// </summary>
        internal static float ReputationArrivalMaximum = 1.2f;

        /// <summary>
        /// CharacterManagerConfig._arrivalRandomFactor. Variación aleatoria del
        /// planificador de llegadas; el asset no declara su unidad.
        /// Original: 10.
        /// </summary>
        internal static float ArrivalRandomFactor = 10f;

        /// <summary>
        /// CharacterManagerConfig._patientSelectionRandomJitter. Ruido al
        /// escoger el siguiente tipo de paciente.
        /// Original: 0,35.
        /// </summary>
        internal static float PatientSelectionRandomJitter = 0.35f;

        /// <summary>
        /// CharacterManagerConfig.RandomArrivalMethods[*].Weight. Orden:
        /// coche, autobús y ambulancia. Originales: 100, 25 y 25; antes de
        /// considerar capacidad equivalen a 66,67 %, 16,67 % y 16,67 %.
        /// </summary>
        internal static readonly int[] ArrivalWeights = { 100, 25, 25 };

        // =========================== ENFERMEDADES ============================

        // Orden exacto del asset para todos los arrays siguientes:
        // 0 Grout, 1 Clamp, 2 Bogwarts, 3 Misery Guts, 4 Bed Face,
        // 5 Light Headed. Una misma posición siempre representa la misma
        // enfermedad; no se buscan enfermedades por nombre durante la carga.

        /// <summary>
        /// WeightedIllness.MinWeight. Peso mínimo de selección.
        /// Originales: 100, 100, 100, 100, 400 y 200.
        /// </summary>
        internal static readonly int[] IllnessMinimumWeights = { 100, 100, 100, 100, 400, 200 };

        /// <summary>
        /// WeightedIllness.MaxWeight. Peso máximo de selección.
        /// Originales: 200, 200, 200, 200, 800 y 400.
        /// </summary>
        internal static readonly int[] IllnessMaximumWeights = { 200, 200, 200, 200, 800, 400 };

        /// <summary>
        /// WeightedIllness.MinStarRating. Estrellas mínimas para ser elegible.
        /// Originales: 0, 0, 0, 1, 0 y 0.
        /// </summary>
        internal static readonly int[] IllnessMinimumStars = { 0, 0, 0, 1, 0, 0 };

        /// <summary>
        /// WeightedIllness.MinPatientsSpawned. Pacientes generados antes de
        /// poder escoger cada enfermedad. Originales: 0, 5, 10, 0, 10 y 0.
        /// </summary>
        internal static readonly int[] IllnessMinimumPatients = { 0, 5, 10, 0, 10, 0 };

        /// <summary>
        /// WeightedIllness.Unlocked. Desbloqueo inicial; los false pueden ser
        /// habilitados después por el LevelScript.
        /// Originales: true, true, false, false, false y false.
        /// </summary>
        internal static readonly bool[] IllnessInitiallyUnlocked =
            { true, true, false, false, false, false };

        // =========================== EMERGENCIAS =============================

        /// <summary>
        /// Schedules["Challenges"].MinCooldownInDays. Espera mínima entre
        /// intentos de generar emergencias. Original: 90 días.
        /// </summary>
        internal static int EmergencyCooldownMinimumDays = 90;

        /// <summary>
        /// Schedules["Challenges"].MaxCooldownInDays. Espera máxima entre
        /// intentos de generar emergencias. Original: 120 días.
        /// </summary>
        internal static int EmergencyCooldownMaximumDays = 120;

        /// <summary>
        /// Schedules["Challenges"].IsEnabledOnStart. El original es false;
        /// posteriormente el LevelScript activa el calendario.
        /// </summary>
        internal static bool EmergencyEventsEnabledOnStart = false;

        /// <summary>
        /// ChallengeConfig.PatientCount. Orden: Light Headed, Bogwarts y
        /// Bed Face. Originales: 7, 5 y 5 pacientes.
        /// </summary>
        internal static readonly int[] EmergencyPatientCounts = { 7, 5, 5 };

        /// <summary>
        /// ChallengeConfig.TimeLength, en el mismo orden anterior.
        /// Originales: 90, 90 y 90 días de juego.
        /// </summary>
        internal static readonly int[] EmergencyTimeLimits = { 90, 90, 90 };

        // =========================== VISITAS VIP =============================

        /// <summary>
        /// Schedules["VIPs"].MinCooldownInDays. Espera mínima entre intentos
        /// de programar una visita VIP. Original: 180 días.
        /// </summary>
        internal static int VipCooldownMinimumDays = 180;

        /// <summary>
        /// Schedules["VIPs"].MaxCooldownInDays. Espera máxima entre intentos
        /// de programar una visita VIP. Original: 365 días.
        /// </summary>
        internal static int VipCooldownMaximumDays = 365;

        /// <summary>
        /// Schedules["VIPs"].IsEnabledOnStart. El original es false;
        /// posteriormente el LevelScript activa el calendario.
        /// </summary>
        internal static bool VipEventsEnabledOnStart = false;

        // ====================== APLICACIÓN DE VALORES ========================

        // Se interceptan todos los constructores de Level porque la configuración
        // llega como uno de sus argumentos. UniqueId limita el parche a Hogsport.
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(Level).GetConstructors(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);

        private static void Prefix(object[] __args)
        {
            if (__args == null) return;
            foreach (var argument in __args)
            {
                if (argument == null || (string)Read(argument, "UniqueId") != HogsportLevelId) continue;
                Apply(argument);
                return;
            }
        }

        private static void Apply(object levelConfig)
        {
            // Copiar antes de escribir evita modificar la configuración heredada
            // y compartida por otros hospitales.
            var finance = LocalConfig(levelConfig, "FinanceManagerConfig");
            Write(finance, "InitialBalance", InitialMoney);

            var characters = LocalConfig(levelConfig, "CharacterManagerConfig");
            Write(characters, "_maxPatients", MaximumPatients);
            Write(characters, "_patientSpawnRate", PatientSpawnRate);
            Write(characters, "_reputationArrivalRateMultiplierMin", ReputationArrivalMinimum);
            Write(characters, "_reputationArrivalRateMultiplierMax", ReputationArrivalMaximum);
            Write(characters, "_arrivalRandomFactor", ArrivalRandomFactor);
            Write(characters, "_patientSelectionRandomJitter", PatientSelectionRandomJitter);
            ApplyIndexed(Read(characters, "RandomArrivalMethods") as IList,
                ArrivalWeights, "Weight");
            ApplyIllnesses(Read(characters, "_weightedIllnesses") as IList);

            // Índice 0: emergencias. Índice 1: visitas VIP. Hogsport no tiene
            // calendario de catástrofes, por lo que no se inventa ninguno.
            var challenges = LocalConfig(levelConfig, "ChallengeManagerConfig");
            var schedules = Read(challenges, "Schedules") as IList;
            ApplySchedule(schedules, 0, EmergencyCooldownMinimumDays,
                EmergencyCooldownMaximumDays, EmergencyEventsEnabledOnStart,
                EmergencyPatientCounts, EmergencyTimeLimits);
            ApplySchedule(schedules, 1, VipCooldownMinimumDays,
                VipCooldownMaximumDays, VipEventsEnabledOnStart, null, null);
        }

        private static void ApplyIllnesses(IList illnesses)
        {
            if (illnesses == null) return;
            var count = Mathf.Min(illnesses.Count, IllnessMinimumWeights.Length);
            for (var index = 0; index < count; ++index)
            {
                var illness = illnesses[index];
                Write(illness, "MinWeight", IllnessMinimumWeights[index]);
                Write(illness, "MaxWeight", IllnessMaximumWeights[index]);
                Write(illness, "MinStarRating", IllnessMinimumStars[index]);
                Write(illness, "MinPatientsSpawned", IllnessMinimumPatients[index]);
                Write(illness, "Unlocked", IllnessInitiallyUnlocked[index]);
            }
        }

        private static void ApplySchedule(IList schedules, int index, int minimum,
            int maximum, bool enabledOnStart, int[] patientCounts, int[] timeLimits)
        {
            if (schedules == null || index >= schedules.Count) return;
            var schedule = schedules[index];
            Write(schedule, "IsEnabledOnStart", enabledOnStart);
            Write(schedule, "MinCooldownInDays", minimum);
            Write(schedule, "MaxCooldownInDays", maximum);
            if (patientCounts == null) return;
            var entries = Read(schedule, "Challenges") as IList;
            if (entries == null) return;
            var count = Mathf.Min(entries.Count, patientCounts.Length);
            for (var item = 0; item < count; ++item)
            {
                // Las entradas comparten originalmente Config con el asset. La
                // copia impide que estos dos valores contaminen otros niveles.
                var entry = entries[item];
                var config = Clone(Read(entry, "Config"));
                if (config == null) continue;
                Write(entry, "Config", config);
                Write(config, "PatientCount", patientCounts[item]);
                Write(config, "TimeLength", timeLimits[item]);
            }
        }

        private static void ApplyIndexed(IList list, IList values, string member)
        {
            if (list == null || values == null) return;
            var count = Mathf.Min(list.Count, values.Count);
            for (var index = 0; index < count; ++index)
                Write(list[index], member, values[index]);
        }

        // Busca una configuración local o heredada, la clona y la asigna al
        // nivel antes de modificarla. No se escribe sobre el asset compartido.
        private static object LocalConfig(object levelConfig, string member)
        {
            var source = levelConfig;
            object value = null;
            while (source != null && value == null)
            {
                value = Read(source, member);
                source = Read(source, "BaseConfig");
            }
            if (value == null) return null;
            var clone = Clone(value) ?? value;
            Write(levelConfig, member, clone);
            return clone;
        }

        private static object Clone(object value)
        {
            var unityObject = value as UnityEngine.Object;
            return unityObject != null ? UnityEngine.Object.Instantiate(unityObject) : value;
        }

        private static object Read(object target, string name)
        {
            if (target == null) return null;
            var field = AccessTools.Field(target.GetType(), name);
            if (field != null) return field.GetValue(target);
            var property = AccessTools.Property(target.GetType(), name);
            return property != null && property.CanRead ? property.GetValue(target, null) : null;
        }

        private static void Write(object target, string name, object value)
        {
            if (target == null) return;
            var field = AccessTools.Field(target.GetType(), name);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            var property = AccessTools.Property(target.GetType(), name);
            if (property != null && property.CanWrite) property.SetValue(target, value, null);
        }
    }
}
