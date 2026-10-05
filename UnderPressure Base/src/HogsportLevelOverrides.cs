// Purpose: Applies editable runtime overrides to Hogsport's level configuration.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    // Valores editables que se aplican realmente al crear Hogsport.
    [HarmonyPatch]
    internal static class HogsportLevelOverrides
    {
        internal static int InitialMoney = 200000;
        internal static int MaximumPatients = 250;
        internal static float PatientSpawnRate = 48f;
        internal static float ReputationArrivalMinimum = 0.8f;
        internal static float ReputationArrivalMaximum = 1.2f;
        internal static float ArrivalRandomFactor = 10f;
        internal static float PatientSelectionRandomJitter = 0.35f;

        // Coche, autobus y ambulancia.
        internal static readonly int[] ArrivalWeights = { 100, 25, 25 };

        // Grout, Clamp, Bogwarts, Misery Guts, Bed Face y Light Headed.
        internal static readonly int[] IllnessMinimumWeights = { 100, 100, 100, 100, 400, 200 };
        internal static readonly int[] IllnessMaximumWeights = { 200, 200, 200, 200, 800, 400 };
        internal static readonly int[] IllnessMinimumStars = { 0, 0, 0, 1, 0, 0 };
        internal static readonly int[] IllnessMinimumPatients = { 0, 5, 10, 0, 10, 0 };
        internal static readonly bool[] IllnessInitiallyUnlocked = { true, true, false, false, false, false };

        internal static int EmergencyCooldownMinimumDays = 90;
        internal static int EmergencyCooldownMaximumDays = 120;
        internal static bool EmergencyEventsEnabledOnStart = false;
        internal static int VipCooldownMinimumDays = 180;
        internal static int VipCooldownMaximumDays = 365;
        internal static bool VipEventsEnabledOnStart = false;
        internal static readonly int[] EmergencyPatientCounts = { 7, 5, 5 };
        internal static readonly int[] EmergencyTimeLimits = { 90, 90, 90 };

        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(Level).GetConstructors(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);

        private static void Prefix(object[] __args)
        {
            if (__args == null) return;
            foreach (var argument in __args)
            {
                if (argument == null || (string)Read(argument, "UniqueId") != "901") continue;
                Apply(argument);
                return;
            }
        }

        private static void Apply(object levelConfig)
        {
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

            var challenges = LocalConfig(levelConfig, "ChallengeManagerConfig");
            var schedules = Read(challenges, "Schedules") as IList;
            ApplySchedule(schedules, 0, EmergencyCooldownMinimumDays,
                EmergencyCooldownMaximumDays, EmergencyEventsEnabledOnStart,
                EmergencyPatientCounts, EmergencyTimeLimits);
            ApplySchedule(schedules, 1, VipCooldownMinimumDays,
                VipCooldownMaximumDays, VipEventsEnabledOnStart, null, null);

            // Hogsport no contiene ningun calendario de catastrofes. No se
            // inventa uno: solo se editan sus emergencias y su visita VIP.
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
