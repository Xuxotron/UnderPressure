using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal sealed class UnderPressureChallengeManagerConfigInstance :
        SharedInstance<ChallengeManager.Config> { }

    internal sealed class UnderPressureChallengeConfigInstance :
        SharedInstance<ChallengeConfig> { }

    internal static class ScenarioEmergencyCatalog
    {
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static ScenarioMapFormat.EmergencyData[] Capture(LevelConfig level)
        {
            var result = new List<ScenarioMapFormat.EmergencyData>();
            var schedules = level?.GetChallengeManagerConfig()?.Schedules;
            if (schedules == null) return result.ToArray();
            for (var scheduleIndex = 0; scheduleIndex < schedules.Count; scheduleIndex++)
            {
                var schedule = schedules[scheduleIndex];
                if (schedule?.Challenges == null) continue;
                foreach (var item in schedule.Challenges)
                {
                    var native = item.Config?.Instance as ChallengeSpecialPatientConfig;
                    if (native == null) continue;
                    result.Add(Capture(native, item.Config, item.Weight,
                        schedule, scheduleIndex));
                }
            }
            return result.ToArray();
        }

        private static ScenarioMapFormat.EmergencyData Capture(
            ChallengeSpecialPatientConfig native,
            SharedInstance<ChallengeConfig> wrapper,
            int weight,
            ChallengeScheduleDefinition schedule,
            int scheduleIndex)
        {
            var data = new ScenarioMapFormat.EmergencyData
            {
                template = WrapperId(wrapper),
                scheduleIndex = scheduleIndex,
                scheduleName = schedule.Name ?? "",
                scheduleEnabled = schedule.IsEnabledOnStart,
                scheduleStartsWithCooldown = schedule.StartWithCooldown,
                minimumCooldownDays = schedule.MinCooldownInDays,
                maximumCooldownDays = schedule.MaxCooldownInDays,
                weight = weight,
                failureAction = (int)native.ActionOnFail,
                scoreType = (int)native.ScoreType,
                arrivalMethod = WrapperId(native.ArrivalMethod),
                arrivalMethodType = native.ArrivalMethod?.Instance?.GetType().FullName ?? "",
                issueWarning = native.IssueChallengeNotice,
                rejectable = native.PlayerCanRejectChallengeNotice,
                patients = native.PatientCount,
                diagnosis = native.DiagnosisComplete,
                success = native.RewardSuccessScore,
                arrivalRate = native.PatientSpawnRate,
                durationDays = native.TimeLength,
                daysUntilStart = native.DaysUntilChallengeStart,
                illnesses = (native.IllnessDefinition ??
                    new SharedInstance<IllnessDefinition>[0])
                    .Where(value => value?.Instance != null)
                    .Select(ScenarioDiseaseCatalog.DefinitionId).ToArray(),
                prerequisites = CapturePrerequisites(native.Prerequisites),
                rewards = CaptureRewards(native)
            };
            return data;
        }

        private static ScenarioMapFormat.EmergencyRuleData[] CapturePrerequisites(
            IEnumerable<IChallengePrerequisite> values)
        {
            var result = new List<ScenarioMapFormat.EmergencyRuleData>();
            if (values == null) return result.ToArray();
            foreach (var value in values)
            {
                ScenarioMapFormat.EmergencyRuleData data = null;
                if (value is ChallengePrerequisiteHasRoomUnlocked)
                {
                    var room = GetPrivate<SharedInstance<RoomDefinition>>(value, "_room");
                    data = Rule(0, room?.Instance?._type.ToString() ?? WrapperId(room));
                }
                else if (value is ChallengePrerequisiteReputationComparitor)
                    data = ComparedRule(1, GetPrivate<int>(value,
                        "_reputationComparitor"), GetPrivate<bool>(value, "_lessThan"));
                else if (value is ChallengePrerequisiteDaysPassed)
                    data = Rule(2, GetPrivate<int>(value, "_numDays").ToString());
                else if (value is ChallengePrerequisite_MinimumCurrentStarRating)
                    data = Rule(3, GetPrivate<int>(value, "_minimum").ToString());
                else if (value is ChallengePrerequisiteHasRoom)
                {
                    data = Rule(4, GetPrivate<RoomDefinition.Type>(value, "_room").ToString());
                    data.reference = GetPrivate<int>(value, "_numRooms").ToString();
                }
                else if (value is ChallengePrerequisitePrestigeComparitor)
                    data = ComparedRule(5, GetPrivate<int>(value,
                        "_prestigeLevel"), GetPrivate<bool>(value, "_lessThan"));
                else if (value is ChallengePrerequisiteBalanceComparitor)
                    data = ComparedRule(6, GetPrivate<int>(value,
                        "_dollars"), GetPrivate<bool>(value, "_lessThan"));
                else if (value is ChallengePrerequisiteStaffMoraleComparitor)
                    data = ComparedRule(7, GetPrivate<int>(value,
                        "_staffMoraleComparitor"), GetPrivate<bool>(value, "_lessThan"));
                else if (value is ChallengePrerequisiteHospitalHygieneComparitor)
                    data = ComparedRule(8, GetPrivate<int>(value,
                        "_hygiene"), GetPrivate<bool>(value, "_lessThan"));
                else if (value is ChallengePrerequisiteStaffComparitor)
                {
                    data = ComparedRule(9, GetPrivate<int>(value,
                        "_numStaff"), GetPrivate<bool>(value, "_lessThan"));
                    data.reference = GetPrivate<StaffDefinition.Type>(value, "_staff").ToString();
                }
                else if (value is ChallengePrerequisite_MaximumCurrentStarRating)
                    data = Rule(10, GetPrivate<int>(value, "_maximum").ToString());
                else if (value is ChallengePrerequisiteHasObjectiveCompleted)
                    data = Rule(11, GetPrivate<string>(value,
                        "_objectiveUniqueReference") ?? "");
                if (data != null) result.Add(data);
            }
            return result.ToArray();
        }

        private static ScenarioMapFormat.EmergencyRuleData[] CaptureRewards(
            ChallengeSpecialPatientConfig native)
        {
            var result = new List<ScenarioMapFormat.EmergencyRuleData>();
            var option = native.Reward?.FindRewardForScore(native.RewardSuccessScore);
            foreach (var reward in option?.Rewards ?? new IReward[0])
            {
                ScenarioMapFormat.EmergencyRuleData data = null;
                if (reward is RewardMoney money) data = Rule(0, money.Amount.ToString());
                else if (reward is RewardReputation reputation)
                    data = Rule(1, reputation.Amount.ToString());
                else if (reward is RewardResearchPoints research)
                    data = Rule(2, research.Points.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                else if (reward is RewardSilver silver)
                    data = Rule(3, silver.Amount.ToString());
                else if (reward is RewardRoom room)
                    data = Rule(4, room.Definition?.Instance?._type.ToString() ??
                        WrapperId(room.Definition));
                else if (reward is RewardRoomItem item)
                    data = Rule(5, WrapperId(item.Definition));
                else if (reward is RewardRoomItemUpgrade upgrade)
                    data = Rule(6, WrapperId(upgrade.Definition));
                if (data != null) result.Add(data);
            }
            return result.ToArray();
        }

        private static ScenarioMapFormat.EmergencyRuleData Rule(int type, string value) =>
            new ScenarioMapFormat.EmergencyRuleData
            {
                type = type,
                value = value ?? ""
            };

        private static ScenarioMapFormat.EmergencyRuleData ComparedRule(
            int type, int value, bool lessThan)
        {
            var result = Rule(type, value.ToString());
            result.lessThan = lessThan;
            return result;
        }

        internal static ScenarioMapFormat.EmergencyData Clone(
            ScenarioMapFormat.EmergencyData source)
        {
            if (source == null) return null;
            return new ScenarioMapFormat.EmergencyData
            {
                template = source.template ?? "",
                scheduleIndex = source.scheduleIndex,
                scheduleName = source.scheduleName ?? "",
                scheduleEnabled = source.scheduleEnabled,
                scheduleStartsWithCooldown = source.scheduleStartsWithCooldown,
                minimumCooldownDays = source.minimumCooldownDays,
                maximumCooldownDays = source.maximumCooldownDays,
                weight = source.weight,
                failureAction = source.failureAction,
                scoreType = source.scoreType,
                arrivalMethod = source.arrivalMethod ?? "",
                arrivalMethodType = source.arrivalMethodType ?? "",
                issueWarning = source.issueWarning,
                rejectable = source.rejectable,
                patients = source.patients,
                diagnosis = source.diagnosis,
                success = source.success,
                arrivalRate = source.arrivalRate,
                durationDays = source.durationDays,
                daysUntilStart = source.daysUntilStart,
                illnesses = source.illnesses == null
                    ? new string[0] : (string[])source.illnesses.Clone(),
                prerequisites = CloneRules(source.prerequisites),
                rewards = CloneRules(source.rewards)
            };
        }

        private static ScenarioMapFormat.EmergencyRuleData[] CloneRules(
            ScenarioMapFormat.EmergencyRuleData[] source)
        {
            if (source == null) return new ScenarioMapFormat.EmergencyRuleData[0];
            return source.Where(value => value != null).Select(value =>
                new ScenarioMapFormat.EmergencyRuleData
                {
                    type = value.type,
                    value = value.value ?? "",
                    reference = value.reference ?? "",
                    lessThan = value.lessThan
                }).ToArray();
        }

        internal static UnderPressureChallengeManagerConfigInstance Apply(
            LevelConfig level,
            ScenarioMapFormat.EmergencyData[] saved,
            IList<UnityEngine.Object> temporaryAssets)
        {
            return Apply(level, level?.GetChallengeManagerConfig(), saved,
                temporaryAssets);
        }

        internal static UnderPressureChallengeManagerConfigInstance Apply(
            LevelConfig level,
            ChallengeManager.Config original,
            ScenarioMapFormat.EmergencyData[] saved,
            IList<UnityEngine.Object> temporaryAssets)
        {
            if (original == null) return null;
            var clone = Clone(original);
            var originalSchedules = original.Schedules ?? new List<ChallengeScheduleDefinition>();
            var schedules = originalSchedules.Select(CloneSchedule).ToList();
            var firstNativeSchedule = -1;
            ChallengeSpecialPatientConfig firstTemplate = null;
            ChallengeRewardOption firstRewardOption = null;

            for (var index = 0; index < originalSchedules.Count; index++)
            {
                var originalSchedule = originalSchedules[index];
                var clonedItems = schedules[index].Challenges ??
                                  new List<ChallengeScheduleDefinition.Item>();
                for (var itemIndex = clonedItems.Count - 1; itemIndex >= 0; itemIndex--)
                {
                    var special = clonedItems[itemIndex].Config?.Instance as
                        ChallengeSpecialPatientConfig;
                    if (special == null) continue;
                    if (firstNativeSchedule < 0) firstNativeSchedule = index;
                    if (firstTemplate == null)
                    {
                        firstTemplate = special;
                        firstRewardOption = special.Reward?.FindRewardForScore(
                            special.RewardSuccessScore);
                    }
                    clonedItems.RemoveAt(itemIndex);
                }
            }

            foreach (var data in saved ?? new ScenarioMapFormat.EmergencyData[0])
            {
                if (data == null) continue;
                var templateWrapper = FindTemplate(originalSchedules, data.template);
                var template = templateWrapper?.Instance as ChallengeSpecialPatientConfig ??
                               firstTemplate;
                if (template == null)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Carrera: no existe una plantilla nativa para la emergencia guardada.");
                    continue;
                }
                var illnesses = (data.illnesses ?? new string[0])
                    .Select(id => ScenarioDiseaseCatalog.Resolve(level, id))
                    .Where(value => value?.Instance != null).ToArray();
                if (illnesses.Length == 0)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Carrera: emergencia omitida porque no contiene enfermedades válidas.");
                    continue;
                }

                var native = Clone(template);
                native.PatientCount = Math.Max(1, data.patients);
                native.PatientSpawnRate = Math.Max(0f, data.arrivalRate);
                native.DiagnosisComplete = Mathf.Clamp(data.diagnosis, 0f, 100f);
                native.IllnessDefinition = illnesses;
                native.ArrivalMethod = ResolveArrival(data, template.ArrivalMethod);
                native.ActionOnFail = (ChallengeSpecialPatient.ActionOnFail)Mathf.Clamp(
                    data.failureAction, 0, 2);
                native.ScoreType = (Objective.ObjectiveScoring)Mathf.Clamp(
                    data.scoreType, 0, 3);
                native.IssueChallengeNotice = data.issueWarning;
                native.PlayerCanRejectChallengeNotice = data.rejectable;
                native.RewardSuccessScore = Mathf.Clamp(data.success, 0, 100);
                native.TimeLength = Math.Max(1, data.durationDays);
                native.DaysUntilChallengeStart = Math.Max(0, data.daysUntilStart);
                native.Prerequisites = BuildPrerequisites(data.prerequisites);
                native.Reward = BuildRewards(data.rewards, native.RewardSuccessScore,
                    template.Reward, template.Reward?.FindRewardForScore(
                        template.RewardSuccessScore) ?? firstRewardOption);

                var wrapper = ScriptableObject.CreateInstance<
                    UnderPressureChallengeConfigInstance>();
                wrapper.name = string.IsNullOrEmpty(data.template)
                    ? "UP Emergency" : data.template + " (UP)";
                wrapper.hideFlags = HideFlags.DontSave;
                wrapper.Instance = native;
                temporaryAssets?.Add(wrapper);

                var target = ResolveScheduleIndex(schedules, data, firstNativeSchedule);
                if (target < 0)
                {
                    schedules.Add(new ChallengeScheduleDefinition
                    {
                        Name = string.IsNullOrEmpty(data.scheduleName)
                            ? "UP Emergencies" : data.scheduleName,
                        IsEnabledOnStart = data.scheduleEnabled,
                        StartWithCooldown = data.scheduleStartsWithCooldown,
                        MinCooldownInDays = data.minimumCooldownDays,
                        MaxCooldownInDays = data.maximumCooldownDays,
                        Challenges = new List<ChallengeScheduleDefinition.Item>()
                    });
                    target = schedules.Count - 1;
                }
                var schedule = schedules[target];
                schedule.IsEnabledOnStart = data.scheduleEnabled;
                schedule.StartWithCooldown = data.scheduleStartsWithCooldown;
                schedule.MinCooldownInDays = Math.Max(0, data.minimumCooldownDays);
                schedule.MaxCooldownInDays = Math.Max(schedule.MinCooldownInDays,
                    data.maximumCooldownDays);
                if (schedule.Challenges == null)
                    schedule.Challenges = new List<ChallengeScheduleDefinition.Item>();
                schedule.Challenges.Add(new ChallengeScheduleDefinition.Item
                {
                    Config = wrapper,
                    Weight = Math.Max(1, data.weight)
                });
            }

            clone.Schedules = schedules;
            var manager = ScriptableObject.CreateInstance<
                UnderPressureChallengeManagerConfigInstance>();
            manager.name = "UP Scenario Challenge Manager Config";
            manager.hideFlags = HideFlags.DontSave;
            manager.Instance = clone;
            temporaryAssets?.Add(manager);
            return manager;
        }

        private static int ResolveScheduleIndex(
            IList<ChallengeScheduleDefinition> schedules,
            ScenarioMapFormat.EmergencyData data,
            int fallback)
        {
            if (data.scheduleIndex >= 0 && data.scheduleIndex < schedules.Count)
                return data.scheduleIndex;
            if (!string.IsNullOrEmpty(data.scheduleName))
                for (var index = 0; index < schedules.Count; index++)
                    if (string.Equals(schedules[index]?.Name, data.scheduleName,
                            StringComparison.Ordinal)) return index;
            return fallback >= 0 && fallback < schedules.Count ? fallback : -1;
        }

        private static SharedInstance<ChallengeConfig> FindTemplate(
            IEnumerable<ChallengeScheduleDefinition> schedules, string id)
        {
            if (string.IsNullOrEmpty(id) || schedules == null) return null;
            foreach (var schedule in schedules)
                foreach (var item in schedule?.Challenges ??
                         new List<ChallengeScheduleDefinition.Item>())
                    if (item.Config?.Instance is ChallengeSpecialPatientConfig &&
                        string.Equals(WrapperId(item.Config), id,
                            StringComparison.Ordinal)) return item.Config;
            return null;
        }

        private static ChallengeScheduleDefinition CloneSchedule(
            ChallengeScheduleDefinition source)
        {
            if (source == null) return new ChallengeScheduleDefinition();
            var clone = Clone(source);
            clone.Prerequisites = source.Prerequisites == null
                ? null : new List<IChallengePrerequisite>(source.Prerequisites);
            clone.Challenges = source.Challenges == null
                ? new List<ChallengeScheduleDefinition.Item>()
                : new List<ChallengeScheduleDefinition.Item>(source.Challenges);
            return clone;
        }

        private static SharedInstance<ArrivalMethodDefinition> ResolveArrival(
            ScenarioMapFormat.EmergencyData data,
            SharedInstance<ArrivalMethodDefinition> fallback)
        {
            foreach (var value in Resources.FindObjectsOfTypeAll<
                         SharedInstance<ArrivalMethodDefinition>>())
            {
                if (value?.Instance == null) continue;
                if (!string.IsNullOrEmpty(data.arrivalMethod) &&
                    string.Equals(WrapperId(value), data.arrivalMethod,
                        StringComparison.Ordinal)) return value;
                if (!string.IsNullOrEmpty(data.arrivalMethodType) &&
                    string.Equals(value.Instance.GetType().FullName,
                        data.arrivalMethodType, StringComparison.Ordinal)) return value;
            }
            return fallback;
        }

        private static List<IChallengePrerequisite> BuildPrerequisites(
            ScenarioMapFormat.EmergencyRuleData[] rules)
        {
            var result = new List<IChallengePrerequisite>();
            foreach (var rule in rules ?? new ScenarioMapFormat.EmergencyRuleData[0])
            {
                if (rule == null) continue;
                IChallengePrerequisite native = null;
                switch (rule.type)
                {
                    case 0:
                        var unlocked = new ChallengePrerequisiteHasRoomUnlocked();
                        SetPrivate(unlocked, "_room", ResolveRoom(rule.value));
                        native = unlocked;
                        break;
                    case 1:
                        native = Compared<ChallengePrerequisiteReputationComparitor>(
                            "_reputationComparitor", Int(rule.value), rule.lessThan);
                        break;
                    case 2:
                        native = Set(new ChallengePrerequisiteDaysPassed(),
                            "_numDays", Int(rule.value));
                        break;
                    case 3:
                        native = Set(new ChallengePrerequisite_MinimumCurrentStarRating(),
                            "_minimum", Int(rule.value));
                        break;
                    case 4:
                        var room = new ChallengePrerequisiteHasRoom();
                        if (Enum.TryParse(rule.value, out RoomDefinition.Type roomType))
                            SetPrivate(room, "_room", roomType);
                        SetPrivate(room, "_numRooms", Math.Max(1, Int(rule.reference, 1)));
                        native = room;
                        break;
                    case 5:
                        native = Compared<ChallengePrerequisitePrestigeComparitor>(
                            "_prestigeLevel", Int(rule.value), rule.lessThan);
                        break;
                    case 6:
                        native = Compared<ChallengePrerequisiteBalanceComparitor>(
                            "_dollars", Int(rule.value), rule.lessThan);
                        break;
                    case 7:
                        native = Compared<ChallengePrerequisiteStaffMoraleComparitor>(
                            "_staffMoraleComparitor", Int(rule.value), rule.lessThan);
                        break;
                    case 8:
                        native = Compared<ChallengePrerequisiteHospitalHygieneComparitor>(
                            "_hygiene", Int(rule.value), rule.lessThan);
                        break;
                    case 9:
                        var staff = Compared<ChallengePrerequisiteStaffComparitor>(
                            "_numStaff", Int(rule.value), rule.lessThan);
                        if (Enum.TryParse(rule.reference, out StaffDefinition.Type staffType))
                            SetPrivate(staff, "_staff", staffType);
                        native = staff;
                        break;
                    case 10:
                        native = Set(new ChallengePrerequisite_MaximumCurrentStarRating(),
                            "_maximum", Int(rule.value));
                        break;
                    case 11:
                        native = Set(new ChallengePrerequisiteHasObjectiveCompleted(),
                            "_objectiveUniqueReference", rule.value ?? "");
                        break;
                }
                if (native != null) result.Add(native);
            }
            return result;
        }

        private static ChallengeReward BuildRewards(
            ScenarioMapFormat.EmergencyRuleData[] rules,
            int success,
            ChallengeReward template,
            ChallengeRewardOption fallbackOption)
        {
            var rewards = new List<IReward>();
            foreach (var rule in rules ?? new ScenarioMapFormat.EmergencyRuleData[0])
            {
                if (rule == null) continue;
                IReward reward = null;
                switch (rule.type)
                {
                    case 0:
                        reward = Set(new RewardMoney(), "_amount", Int(rule.value));
                        break;
                    case 1:
                        reward = Set(new RewardReputation(), "_amount", Int(rule.value));
                        break;
                    case 2:
                        reward = Set(new RewardResearchPoints(), "_points", Float(rule.value));
                        break;
                    case 3:
                        reward = RewardSilver.Create(Int(rule.value));
                        break;
                    case 4:
                        var room = ResolveRoom(rule.value);
                        if (room != null) reward = Set(new RewardRoom(), "_definition", room);
                        break;
                    case 5:
                        var item = ResolveAsset<RoomItemDefinition>(rule.value);
                        if (item != null) reward = Set(new RewardRoomItem(), "_definition", item);
                        break;
                    case 6:
                        var upgrade = ResolveAsset<RoomItemUpgradeDefinition>(rule.value);
                        if (upgrade != null)
                            reward = Set(new RewardRoomItemUpgrade(), "_definition", upgrade);
                        break;
                }
                if (reward != null) rewards.Add(reward);
            }

            var option = fallbackOption == null
                ? new ChallengeRewardOption() : Clone(fallbackOption);
            object comparator = new ChallengeRewardComparator();
            SetPrivate(comparator, "_comparatorType",
                ChallengeRewardComparator.Type.GreaterThanOrEqual);
            SetPrivate(comparator, "_amount", success);
            SetPrivate(option, "_comparator", comparator);
            SetPrivate(option, "_rewards", rewards.ToArray());
            var result = template == null ? new ChallengeReward() : Clone(template);
            SetPrivate(result, "_rewardOptions", new[] { option });
            return result;
        }

        private static T Compared<T>(string valueField, int value, bool lessThan)
            where T : IChallengePrerequisite, new()
        {
            var result = new T();
            SetPrivate(result, valueField, value);
            SetPrivate(result, "_lessThan", lessThan);
            return result;
        }

        private static T Set<T>(T target, string field, object value)
        {
            SetPrivate(target, field, value);
            return target;
        }

        private static SharedInstance<RoomDefinition> ResolveRoom(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var value in Resources.FindObjectsOfTypeAll<
                         SharedInstance<RoomDefinition>>())
                if (value?.Instance != null &&
                    (string.Equals(WrapperId(value), id, StringComparison.Ordinal) ||
                     string.Equals(value.Instance._type.ToString(), id,
                         StringComparison.Ordinal))) return value;
            return null;
        }

        private static SharedInstance<T> ResolveAsset<T>(string id) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var value in Resources.FindObjectsOfTypeAll<SharedInstance<T>>())
                if (value?.Instance != null && string.Equals(WrapperId(value), id,
                        StringComparison.Ordinal)) return value;
            return null;
        }

        private static string WrapperId<T>(SharedInstance<T> value) where T : class =>
            value == null ? "" : value.name ?? "";

        private static int Int(string value, int fallback = 0) =>
            int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed : fallback;

        private static float Float(string value) =>
            float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed : 0f;

        private static T Clone<T>(T source) where T : class =>
            source == null ? null : (T)CloneMethod.Invoke(source, null);

        private static T GetPrivate<T>(object target, string field)
        {
            var info = AccessTools.Field(target.GetType(), field);
            return info == null ? default(T) : (T)info.GetValue(target);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            AccessTools.Field(target.GetType(), field)?.SetValue(target, value);
        }
    }
}
