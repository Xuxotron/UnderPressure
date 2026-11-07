using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BehaviorDesigner.Runtime;
using FullInspector;
using FullInspector.Generated.SharedInstance;
using HarmonyLib;
using TH20;
using TH20.BTA.Level;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal sealed class UnderPressureLevelScriptConfigInstance :
        SharedInstance<LevelScriptManager.Config> { }

    internal static class ScenarioObjectiveCatalog
    {
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static bool IsEmptyPlaceholder(ScenarioMapFormat.ObjectiveData value)
        {
            if (value == null || value.isStar ||
                !string.IsNullOrEmpty(value.name) ||
                !string.IsNullOrEmpty(value.template)) return false;
            return (value.requirements == null || value.requirements.Length == 0) &&
                   (value.completion == null || value.completion.Length == 0) &&
                   (value.failure == null || value.failure.Length == 0) &&
                   (value.abandon == null || value.abandon.Length == 0);
        }

        internal static ScenarioMapFormat.ObjectiveData[] Capture(LevelConfig level)
        {
            var result = new List<ScenarioMapFormat.ObjectiveData>();
            CaptureStars(level, result);
            CaptureChallenges(level, result);
            return result.OrderBy(value => value.isStar ? 0 : 1)
                .ThenBy(value => value.isStar ? value.starIndex : value.scheduleIndex)
                .ToArray();
        }

        private static void CaptureStars(LevelConfig level,
            ICollection<ScenarioMapFormat.ObjectiveData> result)
        {
            var script = level?.GetLevelScriptConfig()?.LevelObjectiveScript;
            if (script == null) return;
            foreach (var task in script.FindTasks<CreateObjective>() ??
                                 new List<CreateObjective>())
            {
                var definition = task?.Objective?.Instance;
                var star = FindStar(definition?.CompletionRewards);
                if (definition == null || star == null) continue;
                var index = (int)star.Star + 1;
                result.Add(CaptureDefinition(definition, new ScenarioMapFormat.ObjectiveData
                {
                    id = "star_" + index,
                    name = definition.NameLocalised.ToString(),
                    template = task.Objective.name ?? "",
                    isStar = true,
                    starIndex = index,
                    taskName = task.Name ?? "",
                    waitForCompletion = task.WaitForCompletion,
                    isDiscovered = task.IsDiscovered,
                    isReplayable = task.IsReplayable,
                    startImmediately = task.StartImmediately
                }));
            }
        }

        private static void CaptureChallenges(LevelConfig level,
            ICollection<ScenarioMapFormat.ObjectiveData> result)
        {
            var schedules = level?.GetChallengeManagerConfig()?.Schedules;
            if (schedules == null) return;
            for (var scheduleIndex = 0; scheduleIndex < schedules.Count; scheduleIndex++)
            {
                var schedule = schedules[scheduleIndex];
                foreach (var item in schedule?.Challenges ??
                                     new List<ChallengeScheduleDefinition.Item>())
                {
                    var native = item.Config?.Instance as ChallengeObjectiveConfig;
                    if (native == null) continue;
                    var data = CaptureDefinition(native,
                        new ScenarioMapFormat.ObjectiveData
                        {
                            id = Guid.NewGuid().ToString("N"),
                            name = native.NameLocalised.ToString(),
                            template = item.Config.name ?? "",
                            isStar = false,
                            scheduleIndex = scheduleIndex,
                            scheduleName = schedule.Name ?? "",
                            scheduleEnabled = schedule.IsEnabledOnStart,
                            scheduleStartsWithCooldown = schedule.StartWithCooldown,
                            minimumCooldownDays = schedule.MinCooldownInDays,
                            maximumCooldownDays = schedule.MaxCooldownInDays,
                            weight = item.Weight,
                            canAbandon = native.CanAbandon,
                            probability = item.Weight.ToString(CultureInfo.InvariantCulture),
                            difficulty = native.RewardSuccessScore.ToString(
                                CultureInfo.InvariantCulture),
                            cooldownSeconds = schedule.MinCooldownInDays.ToString(
                                CultureInfo.InvariantCulture)
                        });
                    data.completion = CaptureRewards(native.Reward?
                        .FindRewardForScore(1)?.Rewards);
                    data.failure = CaptureRewards(native.Reward?
                        .FindRewardForScore(0)?.Rewards);
                    data.abandon = CaptureRewards(native.Reward?
                        .FindRewardForScore(-1)?.Rewards);
                    result.Add(data);
                }
            }
        }

        private static ScenarioMapFormat.ObjectiveData CaptureDefinition(
            ObjectiveDefinition definition, ScenarioMapFormat.ObjectiveData data)
        {
            data.scoreType = (int)definition.ScoreType;
            data.requiredHighScore = definition.RequiredHighScore;
            data.evaluateOnTimeElapse = definition.EvaluateOnTimeElapse;
            data.durationDays = definition.TimeLength.ToString(CultureInfo.InvariantCulture);
            data.requirements = CaptureRequirements(definition.SubGoalDefinitions);
            if (data.isStar)
            {
                data.completion = CaptureRewards((definition.CompletionRewards ??
                    new IReward[0]).Where(value => !(value is RewardStar)));
                data.failure = CaptureRewards(definition.FailRewards);
                data.abandon = CaptureRewards(definition.AbandonRewards);
            }
            return data;
        }

        private static ScenarioMapFormat.ObjectiveRuleData[] CaptureRequirements(
            IEnumerable<SubGoalDefinition> definitions)
        {
            var result = new List<ScenarioMapFormat.ObjectiveRuleData>();
            foreach (var value in definitions ?? new SubGoalDefinition[0])
            {
                ScenarioMapFormat.ObjectiveRuleData data = null;
                if (value is SubGoalDefinitionEarnMoney money)
                    data = Rule("earn_money", money.TargetAmount);
                else if (value is SubGoalDefinitionReputation reputation)
                    data = Rule("reputation", reputation.Target);
                else if (value is SubGoalDefinitionCurePatients cure)
                    data = Rule("cure_patients", cure.CureCount);
                else if (value is SubGoalDefinitionHospitalValue hospitalValue)
                    data = Rule("hospital_value", hospitalValue.TargetValue);
                else if (value is SubGoalDefinitionHospitalAttractiveness attractiveness)
                    data = Rule("attractiveness", attractiveness.TargetAttractiveness);
                else if (value is SubGoalDefinitionStaffMorale morale)
                    data = Rule("staff_morale", morale.TargetPercentage);
                else if (value is SubGoalDefinitionDaysSinceLastDeath days)
                    data = Rule("days_without_death", days.Days);
                else if (value is SubGoalDefinitionBuildRoom room)
                {
                    data = Rule("build_room", room.RequiredCount);
                    data.reference = room.RoomDefinition?.Instance?._type.ToString() ?? "";
                }
                if (data != null) result.Add(data);
            }
            return result.ToArray();
        }

        private static ScenarioMapFormat.ObjectiveRuleData[] CaptureRewards(
            IEnumerable<IReward> rewards)
        {
            var result = new List<ScenarioMapFormat.ObjectiveRuleData>();
            foreach (var reward in rewards ?? new IReward[0])
            {
                ScenarioMapFormat.ObjectiveRuleData data = null;
                if (reward is RewardMoney money) data = Rule("money", money.Amount);
                else if (reward is RewardReputation reputation)
                    data = Rule("reputation", reputation.Amount);
                else if (reward is RewardResearchPoints research)
                    data = Rule("research_points", research.Points);
                else if (reward is RewardRoom room)
                {
                    data = Rule("unlock_room", "0");
                    data.reference = room.Definition?.Instance?._type.ToString() ?? "";
                }
                if (data != null) result.Add(data);
            }
            return result.ToArray();
        }

        private static ScenarioMapFormat.ObjectiveRuleData Rule(string type, object value) =>
            new ScenarioMapFormat.ObjectiveRuleData
            {
                type = type,
                value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };

        private static RewardStar FindStar(IEnumerable<IReward> rewards) =>
            (rewards ?? new IReward[0]).OfType<RewardStar>().FirstOrDefault();

        internal static UnderPressureLevelScriptConfigInstance ApplyStars(
            LevelConfig level, ScenarioMapFormat.ObjectiveData[] saved,
            IList<UnityEngine.Object> temporaryAssets)
        {
            var original = level?.GetLevelScriptConfig();
            var originalScript = original?.LevelObjectiveScript;
            if (original == null || originalScript == null) return null;
            var clone = Clone(original);
            var script = UnityEngine.Object.Instantiate(originalScript);
            script.name = originalScript.name + " (UP)";
            script.hideFlags = HideFlags.DontSave;
            temporaryAssets?.Add(script);
            var tasks = script.FindTasks<CreateObjective>() ?? new List<CreateObjective>();
            foreach (var data in (saved ?? new ScenarioMapFormat.ObjectiveData[0])
                         .Where(value => value != null && value.isStar))
            {
                var task = ResolveStarTask(tasks, data);
                if (task?.Objective?.Instance == null)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Carrera: no existe la tarea nativa para la estrella " + data.starIndex + ".");
                    continue;
                }
                var native = BuildDefinition(task.Objective.Instance, data, true);
                var wrapper = ScriptableObject.CreateInstance<
                    SharedInstance_TH20TH20_ObjectiveDefinition>();
                wrapper.name = string.IsNullOrEmpty(data.template)
                    ? task.Objective.name + " (UP)" : data.template + " (UP)";
                wrapper.hideFlags = HideFlags.DontSave;
                wrapper.Instance = native;
                temporaryAssets?.Add(wrapper);
                task.Objective = wrapper;
                task.Name = string.IsNullOrEmpty(data.taskName) ? task.Name : data.taskName;
                task.WaitForCompletion = data.waitForCompletion;
                task.IsDiscovered = data.isDiscovered;
                task.IsReplayable = data.isReplayable;
                task.StartImmediately = data.startImmediately;
            }
            clone.LevelObjectiveScript = script;
            var result = ScriptableObject.CreateInstance<
                UnderPressureLevelScriptConfigInstance>();
            result.name = "UP Scenario Level Script Config";
            result.hideFlags = HideFlags.DontSave;
            result.Instance = clone;
            temporaryAssets?.Add(result);
            return result;
        }

        internal static ChallengeManager.Config ApplyChallenges(LevelConfig level,
            ChallengeManager.Config original, ScenarioMapFormat.ObjectiveData[] saved,
            IList<UnityEngine.Object> temporaryAssets)
        {
            if (original == null) return null;
            var clone = Clone(original);
            var originalSchedules = original.Schedules ??
                                    new List<ChallengeScheduleDefinition>();
            var schedules = originalSchedules.Select(CloneSchedule).ToList();
            var fallbackSchedule = -1;
            ChallengeObjectiveConfig fallback = null;
            for (var index = 0; index < schedules.Count; index++)
            {
                var items = schedules[index].Challenges ??
                            new List<ChallengeScheduleDefinition.Item>();
                for (var itemIndex = items.Count - 1; itemIndex >= 0; itemIndex--)
                {
                    var objective = items[itemIndex].Config?.Instance as ChallengeObjectiveConfig;
                    if (objective == null) continue;
                    if (fallback == null)
                    {
                        fallback = objective;
                        fallbackSchedule = index;
                    }
                    items.RemoveAt(itemIndex);
                }
            }
            foreach (var data in (saved ?? new ScenarioMapFormat.ObjectiveData[0])
                         .Where(value => value != null && !value.isStar))
            {
                var template = FindChallengeTemplate(originalSchedules, data.template) ?? fallback;
                if (template == null)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Carrera: no existe una plantilla nativa para el reto guardado.");
                    continue;
                }
                var native = (ChallengeObjectiveConfig)BuildDefinition(template, data, false);
                native.CanAbandon = data.canAbandon;
                native.RewardSuccessScore = Int(data.difficulty, 1);
                native.Reward = BuildChallengeRewards(data, template.Reward);
                var wrapper = ScriptableObject.CreateInstance<UnderPressureChallengeConfigInstance>();
                wrapper.name = string.IsNullOrEmpty(data.template)
                    ? "UP Objective Challenge" : data.template + " (UP)";
                wrapper.hideFlags = HideFlags.DontSave;
                wrapper.Instance = native;
                temporaryAssets?.Add(wrapper);
                var target = ResolveSchedule(schedules, data, fallbackSchedule);
                if (target < 0)
                {
                    schedules.Add(new ChallengeScheduleDefinition
                    {
                        Name = string.IsNullOrEmpty(data.scheduleName)
                            ? "UP Objectives" : data.scheduleName,
                        Challenges = new List<ChallengeScheduleDefinition.Item>()
                    });
                    target = schedules.Count - 1;
                }
                var schedule = schedules[target];
                schedule.IsEnabledOnStart = data.scheduleEnabled;
                schedule.StartWithCooldown = data.scheduleStartsWithCooldown;
                schedule.MinCooldownInDays = Math.Max(0,
                    Int(data.cooldownSeconds, data.minimumCooldownDays));
                schedule.MaxCooldownInDays = Math.Max(schedule.MinCooldownInDays,
                    data.maximumCooldownDays);
                schedule.Challenges.Add(new ChallengeScheduleDefinition.Item
                {
                    Config = wrapper,
                    Weight = Math.Max(1, Int(data.probability, data.weight))
                });
            }
            clone.Schedules = schedules;
            return clone;
        }

        internal static UnderPressureChallengeManagerConfigInstance WrapChallenges(
            ChallengeManager.Config config, IList<UnityEngine.Object> temporaryAssets)
        {
            if (config == null) return null;
            var wrapper = ScriptableObject.CreateInstance<
                UnderPressureChallengeManagerConfigInstance>();
            wrapper.name = "UP Scenario Challenge Manager Config";
            wrapper.hideFlags = HideFlags.DontSave;
            wrapper.Instance = config;
            temporaryAssets?.Add(wrapper);
            return wrapper;
        }

        private static CreateObjective ResolveStarTask(IEnumerable<CreateObjective> tasks,
            ScenarioMapFormat.ObjectiveData data)
        {
            if (!string.IsNullOrEmpty(data.taskName))
            {
                var named = tasks.FirstOrDefault(value => string.Equals(value?.Name,
                    data.taskName, StringComparison.Ordinal));
                if (named != null) return named;
            }
            return tasks.FirstOrDefault(value =>
            {
                var star = FindStar(value?.Objective?.Instance?.CompletionRewards);
                return star != null && (int)star.Star + 1 == data.starIndex;
            });
        }

        private static ObjectiveDefinition BuildDefinition(ObjectiveDefinition template,
            ScenarioMapFormat.ObjectiveData data, bool preserveStar)
        {
            var result = Clone(template);
            result.ScoreType = (Objective.ObjectiveScoring)Mathf.Clamp(data.scoreType, 0, 3);
            result.RequiredHighScore = data.requiredHighScore;
            result.TimeLength = Int(data.durationDays, template.TimeLength);
            result.EvaluateOnTimeElapse = data.evaluateOnTimeElapse;
            result.SubGoalDefinitions = BuildRequirements(data.requirements);
            result.SubGoalDefinitions.AddRange((template.SubGoalDefinitions ??
                new List<SubGoalDefinition>()).Where(value => !IsEditable(value)));
            result.FailRewards = MergeRewards(data.failure, template.FailRewards).ToArray();
            result.AbandonRewards = MergeRewards(data.abandon,
                template.AbandonRewards).ToArray();
            var completion = BuildRewards(data.completion);
            completion.AddRange((template.CompletionRewards ?? new IReward[0])
                .Where(value => !IsEditable(value) || preserveStar && value is RewardStar));
            result.CompletionRewards = completion.ToArray();
            return result;
        }

        private static bool IsEditable(SubGoalDefinition value) =>
            value is SubGoalDefinitionEarnMoney ||
            value is SubGoalDefinitionReputation ||
            value is SubGoalDefinitionCurePatients ||
            value is SubGoalDefinitionHospitalValue ||
            value is SubGoalDefinitionHospitalAttractiveness ||
            value is SubGoalDefinitionStaffMorale ||
            value is SubGoalDefinitionDaysSinceLastDeath ||
            value is SubGoalDefinitionBuildRoom;

        private static bool IsEditable(IReward value) =>
            value is RewardMoney || value is RewardReputation ||
            value is RewardResearchPoints || value is RewardRoom;

        private static List<IReward> MergeRewards(
            IEnumerable<ScenarioMapFormat.ObjectiveRuleData> rules,
            IEnumerable<IReward> template)
        {
            var result = BuildRewards(rules);
            result.AddRange((template ?? new IReward[0]).Where(value => !IsEditable(value)));
            return result;
        }

        private static List<SubGoalDefinition> BuildRequirements(
            IEnumerable<ScenarioMapFormat.ObjectiveRuleData> rules)
        {
            var result = new List<SubGoalDefinition>();
            foreach (var rule in rules ?? new ScenarioMapFormat.ObjectiveRuleData[0])
            {
                if (rule == null) continue;
                SubGoalDefinition value = null;
                switch (rule.type)
                {
                    case "earn_money": value = new SubGoalDefinitionEarnMoney
                        { TargetAmount = Int(rule.value) }; break;
                    case "reputation": value = new SubGoalDefinitionReputation
                        { Target = Int(rule.value) }; break;
                    case "cure_patients": value = new SubGoalDefinitionCurePatients
                        { CureCount = Int(rule.value) }; break;
                    case "hospital_value": value = new SubGoalDefinitionHospitalValue
                        { TargetValue = Int(rule.value) }; break;
                    case "attractiveness": value = new SubGoalDefinitionHospitalAttractiveness
                        { TargetAttractiveness = Int(rule.value) }; break;
                    case "staff_morale": value = new SubGoalDefinitionStaffMorale
                        { TargetPercentage = Int(rule.value) }; break;
                    case "days_without_death": value = new SubGoalDefinitionDaysSinceLastDeath
                        { Days = Int(rule.value) }; break;
                    case "build_room":
                        var room = ResolveRoom(rule.reference);
                        if (room != null) value = new SubGoalDefinitionBuildRoom
                        {
                            RoomDefinition = room,
                            RequiredCount = Math.Max(1, Int(rule.value, 1)),
                            IncludeExisting = true
                        };
                        break;
                }
                if (value != null) result.Add(value);
            }
            return result;
        }

        private static List<IReward> BuildRewards(
            IEnumerable<ScenarioMapFormat.ObjectiveRuleData> rules)
        {
            var result = new List<IReward>();
            foreach (var rule in rules ?? new ScenarioMapFormat.ObjectiveRuleData[0])
            {
                if (rule == null) continue;
                IReward reward = null;
                switch (rule.type)
                {
                    case "money": reward = Set(new RewardMoney(), "_amount", Int(rule.value)); break;
                    case "reputation": reward = Set(new RewardReputation(), "_amount", Int(rule.value)); break;
                    case "research_points": reward = Set(new RewardResearchPoints(),
                        "_points", Float(rule.value)); break;
                    case "unlock_room":
                        var room = ResolveRoom(rule.reference);
                        if (room != null) reward = Set(new RewardRoom(), "_definition", room);
                        break;
                }
                if (reward != null) result.Add(reward);
            }
            return result;
        }

        private static ChallengeReward BuildChallengeRewards(
            ScenarioMapFormat.ObjectiveData data, ChallengeReward template)
        {
            var options = new[]
            {
                BuildRewardOption(template?.FindRewardForScore(1), 1, data.completion),
                BuildRewardOption(template?.FindRewardForScore(0), 0, data.failure),
                BuildRewardOption(template?.FindRewardForScore(-1), -1, data.abandon)
            };
            var result = template == null ? new ChallengeReward() : Clone(template);
            SetPrivate(result, "_rewardOptions", options);
            return result;
        }

        private static ChallengeRewardOption BuildRewardOption(
            ChallengeRewardOption template, int score,
            ScenarioMapFormat.ObjectiveRuleData[] rules)
        {
            var result = template == null ? new ChallengeRewardOption() : Clone(template);
            object comparator = new ChallengeRewardComparator();
            SetPrivate(comparator, "_comparatorType", ChallengeRewardComparator.Type.Equal);
            SetPrivate(comparator, "_amount", score);
            SetPrivate(result, "_comparator", comparator);
            SetPrivate(result, "_rewards", MergeRewards(rules,
                template?.Rewards).ToArray());
            return result;
        }

        private static ChallengeObjectiveConfig FindChallengeTemplate(
            IEnumerable<ChallengeScheduleDefinition> schedules, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return schedules.SelectMany(value => value?.Challenges ??
                       new List<ChallengeScheduleDefinition.Item>())
                .Where(value => value.Config?.Instance is ChallengeObjectiveConfig)
                .Select(value => value.Config)
                .FirstOrDefault(value => string.Equals(value.name, id,
                    StringComparison.Ordinal))?.Instance as ChallengeObjectiveConfig;
        }

        private static int ResolveSchedule(IList<ChallengeScheduleDefinition> schedules,
            ScenarioMapFormat.ObjectiveData data, int fallback)
        {
            if (data.scheduleIndex >= 0 && data.scheduleIndex < schedules.Count)
                return data.scheduleIndex;
            if (!string.IsNullOrEmpty(data.scheduleName))
                for (var index = 0; index < schedules.Count; index++)
                    if (string.Equals(schedules[index]?.Name, data.scheduleName,
                            StringComparison.Ordinal)) return index;
            return fallback >= 0 && fallback < schedules.Count ? fallback : -1;
        }

        private static ChallengeScheduleDefinition CloneSchedule(
            ChallengeScheduleDefinition source)
        {
            if (source == null) return new ChallengeScheduleDefinition
                { Challenges = new List<ChallengeScheduleDefinition.Item>() };
            var result = Clone(source);
            result.Prerequisites = source.Prerequisites == null ? null :
                new List<IChallengePrerequisite>(source.Prerequisites);
            result.Challenges = source.Challenges == null
                ? new List<ChallengeScheduleDefinition.Item>()
                : new List<ChallengeScheduleDefinition.Item>(source.Challenges);
            return result;
        }

        private static SharedInstance<RoomDefinition> ResolveRoom(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Resources.FindObjectsOfTypeAll<SharedInstance<RoomDefinition>>()
                .FirstOrDefault(value => value?.Instance != null &&
                    (string.Equals(value.name, id, StringComparison.Ordinal) ||
                     string.Equals(value.Instance._type.ToString(), id,
                         StringComparison.Ordinal)));
        }

        private static int Int(string value, int fallback = 0) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed) ? parsed : fallback;

        private static float Float(string value) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var parsed) ? parsed : 0f;

        private static T Clone<T>(T source) where T : class =>
            source == null ? null : (T)CloneMethod.Invoke(source, null);

        private static T Set<T>(T target, string field, object value)
        {
            SetPrivate(target, field, value);
            return target;
        }

        private static void SetPrivate(object target, string field, object value) =>
            AccessTools.Field(target.GetType(), field)?.SetValue(target, value);
    }
}
