// Updated: 2026-11-11 - Recompensas Kudosh/objeto/mejora y clasificacion de requisitos Tutorial/DLCs.
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
            return ScenarioObjectiveRequirements.Capture(definitions);
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
                else if (reward is RewardSilver silver)
                    data = Rule("kudosh", silver.Amount);
                else if (reward is RewardRoom room)
                {
                    data = Rule("unlock_room", "0");
                    data.reference = room.Definition?.Instance?._type.ToString() ?? "";
                }
                else if (reward is RewardRoomItem item)
                {
                    data = Rule("unlock_item", "0");
                    data.reference = item.Definition?.name ?? "";
                }
                else if (reward is RewardRoomItemUpgrade upgrade)
                {
                    data = Rule("unlock_item_upgrade", "0");
                    data.reference = upgrade.Definition?.name ?? "";
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
                        Name = string.IsNullOrEmpty(
                                   ScenarioObjectiveRequirements
                                       .RuntimeScheduleName(data))
                            ? "UP Objectives"
                            : ScenarioObjectiveRequirements
                                .RuntimeScheduleName(data),
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

        private static bool IsEditable(SubGoalDefinition value)
        {
            return ScenarioObjectiveRequirements.IsEditable(value);
        }

        private static bool IsEditable(IReward value) =>
            value is RewardMoney ||
            value is RewardReputation ||
            value is RewardResearchPoints ||
            value is RewardSilver ||
            value is RewardRoom ||
            value is RewardRoomItem ||
            value is RewardRoomItemUpgrade;

        private static List<IReward> MergeRewards(
            IEnumerable<ScenarioMapFormat.ObjectiveRuleData> rules,
            IEnumerable<IReward> template)
        {
            var result = BuildRewards(rules);
            result.AddRange((template ?? new IReward[0])
                .Where(value => !IsEditable(value)));
            return result;
        }

        private static List<SubGoalDefinition> BuildRequirements(
            IEnumerable<ScenarioMapFormat.ObjectiveRuleData> rules)
        {
            return ScenarioObjectiveRequirements.Build(rules);
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
                    case "research_points":
                        reward = Set(
                            new RewardResearchPoints(),
                            "_points",
                            Float(rule.value));
                        break;
                    case "kudosh":
                        reward = Set(
                            new RewardSilver(),
                            "_amount",
                            Int(rule.value));
                        break;
                    case "unlock_room":
                        var room = ResolveRoom(rule.reference);
                        if (room != null)
                            reward = Set(
                                new RewardRoom(),
                                "_definition",
                                room);
                        break;
                    case "unlock_item":
                        var item = Resolve<RoomItemDefinition>(
                            rule.reference);
                        if (item != null)
                            reward = Set(
                                new RewardRoomItem(),
                                "_definition",
                                item);
                        break;
                    case "unlock_item_upgrade":
                        var upgrade = Resolve<RoomItemUpgradeDefinition>(
                            rule.reference);
                        if (upgrade != null)
                            reward = Set(
                                new RewardRoomItemUpgrade(),
                                "_definition",
                                upgrade);
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

        private static int ResolveSchedule(
            IList<ChallengeScheduleDefinition> schedules,
            ScenarioMapFormat.ObjectiveData data,
            int fallback)
        {
            if (data.scheduleIndex >= 0 &&
                data.scheduleIndex < schedules.Count)
                return data.scheduleIndex;

            var scheduleName =
                ScenarioObjectiveRequirements.RuntimeScheduleName(data);
            if (!string.IsNullOrEmpty(scheduleName))
            {
                for (var index = 0; index < schedules.Count; index++)
                {
                    if (string.Equals(
                            schedules[index]?.Name,
                            scheduleName,
                            StringComparison.Ordinal))
                        return index;
                }
            }

            return fallback >= 0 && fallback < schedules.Count
                ? fallback
                : -1;
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

        private static SharedInstance<T> Resolve<T>(string id)
            where T : class
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return Resources.FindObjectsOfTypeAll<SharedInstance<T>>()
                .FirstOrDefault(value =>
                    value?.Instance != null &&
                    string.Equals(
                        value.name,
                        id,
                        StringComparison.Ordinal));
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


    internal enum ObjectiveRequirementReferenceKind
    {
        None,
        Room,
        Item,
        ItemUpgrade,
        Illness,
        Staff,
        Qualification,
        ResearchProject,
        MaintenanceJob,
        CameraMovement
    }

    internal sealed class ObjectiveRequirementChoice
    {
        internal string Id;
        internal string Label;
    }

    internal static class ScenarioObjectiveRequirements
    {
        internal const string PublicHospitalSchedulePrefix = "UP Public Hospital|";

        internal static readonly string[] Types =
        {
            "earn_money",
            "reach_balance",
            "reputation",
            "cure_patients",
            "cure_rate",
            "diagnose_patients",
            "hospital_value",
            "attractiveness",
            "hygiene",
            "staff_morale",
            "thermal_comfort",
            "days_without_death",
            "patient_deaths",
            "prestige",
            "room_prestige",
            "room_count",
            "build_room",
            "build_reception",
            "build_item",
            "upgrade_item",
            "train_staff",
            "promote_staff",
            "hire_staff",
            "fire_staff",
            "research_points",
            "research_project",
            "launch_marketing",
            "marketing_complete_general",
            "marketing_complete_illness",
            "marketing_complete_recruitment",
            "marketing_months",
            "buy_plots",
            "own_plots",
            "own_robo_plots",
            "shoot_monobeasts",
            "vip_satisfied",
            "vaccination",
            "reveal_aliens",
            "generate_electricity",
            "machine_catch_fire",
            "rotate_item",
            "maintenance_jobs",
            "budget",
            "anachronistic_cure_count",
            "anachronistic_cure_rate",
            "time_tunnel_count",
            "time_tunnel_cure",
            "time_portal_cure_count",
            "reach_horde_wave",
            "reach_horde_objectives",
            "camera_movement"
        };

        internal static bool IsTutorial(string type)
        {
            switch (type)
            {
                case "build_reception":
                case "rotate_item":
                case "camera_movement":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsDlc(string type)
        {
            switch (type)
            {
                case "own_robo_plots":
                case "reveal_aliens":
                case "generate_electricity":
                case "anachronistic_cure_count":
                case "anachronistic_cure_rate":
                case "time_tunnel_count":
                case "time_tunnel_cure":
                case "time_portal_cure_count":
                case "reach_horde_wave":
                case "reach_horde_objectives":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsPublicHospital(
            ScenarioMapFormat.ObjectiveData data)
        {
            return data != null &&
                   !data.isStar &&
                   !string.IsNullOrEmpty(data.scheduleName) &&
                   data.scheduleName.StartsWith(
                       PublicHospitalSchedulePrefix,
                       StringComparison.Ordinal);
        }

        internal static string RuntimeScheduleName(
            ScenarioMapFormat.ObjectiveData data)
        {
            if (data == null)
                return "";

            var name = data.scheduleName ?? "";
            if (!name.StartsWith(
                    PublicHospitalSchedulePrefix,
                    StringComparison.Ordinal))
                return name;

            var encoded =
                name.Substring(PublicHospitalSchedulePrefix.Length);
            try
            {
                return Uri.UnescapeDataString(encoded);
            }
            catch
            {
                return encoded;
            }
        }

        internal static void SetPublicHospital(
            ScenarioMapFormat.ObjectiveData data,
            bool enabled)
        {
            if (data == null || data.isStar)
                return;

            if (enabled)
            {
                if (IsPublicHospital(data))
                    return;

                data.scheduleName =
                    PublicHospitalSchedulePrefix +
                    Uri.EscapeDataString(
                        data.scheduleName ?? "");
            }
            else if (IsPublicHospital(data))
            {
                data.scheduleName =
                    RuntimeScheduleName(data);
            }
        }

        internal static string PublicHospitalLabel()
        {
            return Spanish() ? "Hospital público:" : "Public hospital:";
        }

        internal static string PublicChallengeLabel(int index)
        {
            return Spanish()
                ? "Hospital público " + index
                : "Public hospital " + index;
        }

        internal static string Label(string type)
        {
            var spanish = Spanish();
            switch (type)
            {
                case "earn_money": return spanish ? "Ganar dinero" : "Earn money";
                case "reach_balance": return spanish ? "Alcanzar saldo" : "Reach balance";
                case "reputation": return spanish ? "Reputación" : "Reputation";
                case "cure_patients": return spanish ? "Curar pacientes" : "Cure patients";
                case "cure_rate": return spanish ? "Tasa de curación" : "Cure rate";
                case "diagnose_patients": return spanish ? "Diagnosticar pacientes" : "Diagnose patients";
                case "hospital_value": return spanish ? "Valor del hospital" : "Hospital value";
                case "attractiveness": return spanish ? "Atractivo" : "Attractiveness";
                case "hygiene": return spanish ? "Higiene" : "Hygiene";
                case "staff_morale": return spanish ? "Moral del personal" : "Staff morale";
                case "thermal_comfort": return spanish ? "Confort térmico" : "Thermal comfort";
                case "days_without_death": return spanish ? "Días sin muertes" : "Days without deaths";
                case "patient_deaths": return spanish ? "Muertes de pacientes" : "Patient deaths";
                case "prestige": return spanish ? "Prestigio del hospital" : "Hospital prestige";
                case "room_prestige": return spanish ? "Prestigio de sala" : "Room prestige";
                case "room_count": return spanish ? "Número de salas" : "Room count";
                case "build_room": return spanish ? "Construir sala" : "Build room";
                case "build_reception": return spanish ? "Construir recepción" : "Build reception";
                case "build_item": return spanish ? "Construir objeto" : "Build item";
                case "upgrade_item": return spanish ? "Mejorar máquinas" : "Upgrade machines";
                case "train_staff": return spanish ? "Formar personal" : "Train staff";
                case "promote_staff": return spanish ? "Ascender personal" : "Promote staff";
                case "hire_staff": return spanish ? "Contratar personal" : "Hire staff";
                case "fire_staff": return spanish ? "Despedir personal" : "Fire staff";
                case "research_points": return spanish ? "Generar investigación" : "Generate research";
                case "research_project": return spanish ? "Completar investigación" : "Complete research project";
                case "launch_marketing": return spanish ? "Lanzar campañas" : "Launch marketing campaigns";
                case "marketing_complete_general": return spanish ? "Completar campaña general" : "Complete general campaign";
                case "marketing_complete_illness": return spanish ? "Completar campaña de enfermedad" : "Complete illness campaign";
                case "marketing_complete_recruitment": return spanish ? "Completar campaña de contratación" : "Complete recruitment campaign";
                case "marketing_months": return spanish ? "Meses de marketing" : "Marketing months";
                case "buy_plots": return spanish ? "Comprar parcelas" : "Buy plots";
                case "own_plots": return spanish ? "Poseer parcelas" : "Own plots";
                case "own_robo_plots": return spanish ? "Poseer parcelas robóticas" : "Own robo plots";
                case "shoot_monobeasts": return spanish ? "Disparar a monobestias" : "Shoot monobeasts";
                case "vip_satisfied": return spanish ? "Satisfacer VIP" : "Satisfy VIPs";
                case "vaccination": return spanish ? "Vacunar pacientes" : "Vaccinate patients";
                case "reveal_aliens": return spanish ? "Revelar alienígenas" : "Reveal aliens";
                case "generate_electricity": return spanish ? "Generar electricidad" : "Generate electricity";
                case "machine_catch_fire": return spanish ? "Incendios de máquinas" : "Machines catch fire";
                case "rotate_item": return spanish ? "Rotar objetos" : "Rotate items";
                case "maintenance_jobs": return spanish ? "Tareas de mantenimiento" : "Maintenance jobs";
                case "budget": return spanish ? "Puntuación de presupuesto" : "Budget score";
                case "anachronistic_cure_count": return spanish ? "Curaciones anacrónicas" : "Anachronistic cures";
                case "anachronistic_cure_rate": return spanish ? "Tasa de curación anacrónica" : "Anachronistic cure rate";
                case "time_tunnel_count": return spanish ? "Usos del túnel temporal" : "Time tunnel uses";
                case "time_tunnel_cure": return spanish ? "Curaciones del túnel temporal" : "Time tunnel cures";
                case "time_portal_cure_count": return spanish ? "Curaciones del portal temporal" : "Time portal cures";
                case "reach_horde_wave": return spanish ? "Alcanzar oleada de hordas" : "Reach horde wave";
                case "reach_horde_objectives": return spanish ? "Alcanzar oleada de objetivos" : "Reach objective wave";
                case "camera_movement": return spanish ? "Movimiento de cámara" : "Camera movement";
                default: return type ?? "";
            }
        }

        internal static bool HasValue(string type)
        {
            return !string.Equals(type, "build_reception", StringComparison.Ordinal) &&
                   !string.Equals(type, "research_project", StringComparison.Ordinal);
        }

        internal static string DefaultValue(string type)
        {
            switch (type)
            {
                case "build_room":
                case "build_item":
                case "upgrade_item":
                case "train_staff":
                case "promote_staff":
                case "hire_staff":
                case "fire_staff":
                case "launch_marketing":
                case "marketing_complete_general":
                case "marketing_complete_illness":
                case "marketing_complete_recruitment":
                case "marketing_months":
                case "buy_plots":
                case "own_plots":
                case "own_robo_plots":
                case "shoot_monobeasts":
                case "vip_satisfied":
                case "vaccination":
                case "reveal_aliens":
                case "machine_catch_fire":
                case "maintenance_jobs":
                case "reach_horde_wave":
                case "reach_horde_objectives":
                    return "1";
                case "thermal_comfort":
                case "hygiene":
                case "cure_rate":
                    return "80";
                case "rotate_item":
                    return "3";
                case "camera_movement":
                    return "100";
                default:
                    return "0";
            }
        }

        internal static ObjectiveRequirementReferenceKind[] ReferenceKinds(string type)
        {
            switch (type)
            {
                case "cure_patients":
                case "diagnose_patients":
                    return new[]
                    {
                        ObjectiveRequirementReferenceKind.Illness,
                        ObjectiveRequirementReferenceKind.Room,
                        ObjectiveRequirementReferenceKind.Staff
                    };
                case "cure_rate":
                    return new[]
                    {
                        ObjectiveRequirementReferenceKind.Illness,
                        ObjectiveRequirementReferenceKind.Room
                    };
                case "room_prestige":
                case "build_room":
                    return new[] { ObjectiveRequirementReferenceKind.Room };
                case "build_item":
                case "machine_catch_fire":
                    return new[] { ObjectiveRequirementReferenceKind.Item };
                case "train_staff":
                    return new[]
                    {
                        ObjectiveRequirementReferenceKind.Staff,
                        ObjectiveRequirementReferenceKind.Qualification
                    };
                case "promote_staff":
                case "hire_staff":
                    return new[] { ObjectiveRequirementReferenceKind.Staff };
                case "research_project":
                    return new[] { ObjectiveRequirementReferenceKind.ResearchProject };
                case "maintenance_jobs":
                    return new[] { ObjectiveRequirementReferenceKind.MaintenanceJob };
                case "camera_movement":
                    return new[] { ObjectiveRequirementReferenceKind.CameraMovement };
                default:
                    return new ObjectiveRequirementReferenceKind[0];
            }
        }

        internal static bool ReferenceRequired(string type, int index)
        {
            return index == 0 &&
                   (string.Equals(type, "build_room", StringComparison.Ordinal) ||
                    string.Equals(type, "build_item", StringComparison.Ordinal));
        }

        internal static string ReferencePart(string reference, int index)
        {
            if (index < 0 || string.IsNullOrEmpty(reference))
                return "";
            var parts = reference.Split('|');
            if (index >= parts.Length)
                return "";
            try
            {
                return Uri.UnescapeDataString(parts[index] ?? "");
            }
            catch
            {
                return parts[index] ?? "";
            }
        }

        internal static string SetReferencePart(string reference, int index, string value)
        {
            if (index < 0)
                return reference ?? "";
            var parts = string.IsNullOrEmpty(reference)
                ? new List<string>()
                : reference.Split('|').ToList();
            while (parts.Count <= index)
                parts.Add("");
            parts[index] = Uri.EscapeDataString(value ?? "");
            while (parts.Count > 0 &&
                   string.IsNullOrEmpty(parts[parts.Count - 1]))
                parts.RemoveAt(parts.Count - 1);
            return string.Join("|", parts.ToArray());
        }

        internal static ObjectiveRequirementChoice[] Choices(
            ObjectiveRequirementReferenceKind kind,
            bool allowEmpty)
        {
            var result = new List<ObjectiveRequirementChoice>();
            if (allowEmpty)
            {
                result.Add(new ObjectiveRequirementChoice
                {
                    Id = "",
                    Label = EmptyReferenceLabel(kind)
                });
            }

            switch (kind)
            {
                case ObjectiveRequirementReferenceKind.Room:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<RoomDefinition>>()
                            .Where(value => value?.Instance != null)
                            .GroupBy(value => value.Instance._type)
                            .Select(group => group.First())
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.Instance._type.ToString(),
                                Label = value.Instance.GetLocalisedName()
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.Item:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<RoomItemDefinition>>()
                            .Where(value => value?.Instance != null &&
                                            !string.IsNullOrEmpty(value.name))
                            .GroupBy(value => value.name)
                            .Select(group => group.First())
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.name,
                                Label = value.Instance.GetLocalisedName()
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.ItemUpgrade:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<RoomItemUpgradeDefinition>>()
                            .Where(value => value?.Instance != null &&
                                            !string.IsNullOrEmpty(value.name))
                            .GroupBy(value => value.name)
                            .Select(group => group.First())
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.name,
                                Label = value.Instance.LocalisedName.Translation
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.Illness:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<IllnessDefinition>>()
                            .Where(value => value?.Instance != null &&
                                            !string.IsNullOrEmpty(value.name))
                            .GroupBy(value => value.name)
                            .Select(group => group.First())
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.name,
                                Label = value.Instance.Name.Translation
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.Staff:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<StaffDefinition>>()
                            .Where(value => value?.Instance != null)
                            .GroupBy(value => value.Instance._type)
                            .Select(group => group.First())
                            .Where(value => value.Instance._type != StaffDefinition.Type.None)
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.Instance._type.ToString(),
                                Label = GameStringUtils.GetStaffTypeTextLoc(
                                    value.Instance._type)
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.Qualification:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<QualificationDefinition>>()
                            .Where(value => value?.Instance != null &&
                                            !string.IsNullOrEmpty(value.name))
                            .GroupBy(value => value.name)
                            .Select(group => group.First())
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.name,
                                Label = value.Instance.NameLocalised.Translation
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.ResearchProject:
                    result.AddRange(
                        Resources.FindObjectsOfTypeAll<SharedInstance<ResearchProjectDefinition>>()
                            .Where(value => value?.Instance != null &&
                                            !string.IsNullOrEmpty(value.name))
                            .GroupBy(value => value.name)
                            .Select(group => group.First())
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.name,
                                Label = value.Instance.NameLocalised.Translation
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.MaintenanceJob:
                    result.AddRange(
                        Enum.GetValues(typeof(JobMaintenance.JobDescription))
                            .Cast<JobMaintenance.JobDescription>()
                            .Where(value =>
                                value != JobMaintenance.JobDescription.None &&
                                value != JobMaintenance.JobDescription.Max)
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.ToString(),
                                Label = MaintenanceJobLabel(value)
                            }));
                    break;

                case ObjectiveRequirementReferenceKind.CameraMovement:
                    result.AddRange(
                        Enum.GetValues(typeof(SubGoalDefinitionCameraMovement.Type))
                            .Cast<SubGoalDefinitionCameraMovement.Type>()
                            .Select(value => new ObjectiveRequirementChoice
                            {
                                Id = value.ToString(),
                                Label = CameraMovementLabel(value)
                            }));
                    break;
            }

            foreach (var choice in result)
            {
                if (choice != null &&
                    string.IsNullOrEmpty(choice.Label))
                    choice.Label = choice.Id ?? "";
            }

            return result
                .Where(value => value != null)
                .OrderBy(value => string.IsNullOrEmpty(value.Id) ? 0 : 1)
                .ThenBy(value => value.Label ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        internal static ScenarioMapFormat.ObjectiveRuleData[] Capture(
            IEnumerable<SubGoalDefinition> definitions)
        {
            var result = new List<ScenarioMapFormat.ObjectiveRuleData>();
            foreach (var value in definitions ?? new SubGoalDefinition[0])
            {
                var data = CaptureOne(value);
                if (data != null)
                    result.Add(data);
            }
            return result.ToArray();
        }

        internal static bool IsEditable(SubGoalDefinition value)
        {
            return CaptureOne(value) != null;
        }

        internal static List<SubGoalDefinition> Build(
            IEnumerable<ScenarioMapFormat.ObjectiveRuleData> rules)
        {
            var result = new List<SubGoalDefinition>();
            foreach (var rule in rules ?? new ScenarioMapFormat.ObjectiveRuleData[0])
            {
                var value = BuildOne(rule);
                if (value != null)
                    result.Add(value);
            }
            return result;
        }

        private static ScenarioMapFormat.ObjectiveRuleData CaptureOne(
            SubGoalDefinition value)
        {
            if (value == null ||
                value is SubGoalDefinitionBuildItemScore ||
                value is SubGoalDefinitionStaffHappiness ||
                value is SubGoalDefinitionEpidemic ||
                value is SubGoalDefinitionFlavourText ||
                value is SubGoalDefinitionOpenRoom ||
                value is SubGoalDefinitionCharacterModifier ||
                value is SubGoalDefinitionCharacterUseItem ||
                value is SubGoalDefinitionMachineExplode ||
                value is SubGoalDefinitionD7CurePatients ||
                value is SubGoalDefinitionD7CureRate ||
                value is SubGoalDefinitionD7DaysSinceLastDeath ||
                value is SubGoalDefinitionD7DaysSinceLastDeathAtScene ||
                value is SubGoalDefinitionD7LeagueTablePosition ||
                value is SubGoalDefinitionD7LeagueTablePositionMonths ||
                value is SubGoalDefinitionD7PatientsCollected ||
                value is SubGoalDefinitionD7PatientsCollectedShare)
                return null;

            ScenarioMapFormat.ObjectiveRuleData data = null;

            if (value is SubGoalDefinitionEarnMoney earn)
                data = Rule("earn_money", earn.TargetAmount);
            else if (value is SubGoalDefinitionReachBalance balance)
                data = Rule("reach_balance", balance.Target);
            else if (value is SubGoalDefinitionReputation reputation)
                data = Rule("reputation", reputation.Target);
            else if (value is SubGoalDefinitionCurePatients cure)
            {
                data = Rule("cure_patients", cure.CureCount);
                data.reference = Pack(
                    Id(cure.Illness),
                    RoomId(cure.Room),
                    StaffId(cure.Staff));
            }
            else if (value is SubGoalDefinitionCureRate cureRate)
            {
                data = Rule("cure_rate", cureRate.TargetCureRate);
                data.reference = Pack(
                    Id(cureRate.Illness),
                    RoomId(cureRate.Room));
            }
            else if (value is SubGoalDiagnosePatientsDefinition diagnose)
            {
                data = Rule("diagnose_patients", diagnose.DiagnoseCount);
                data.reference = Pack(
                    Id(diagnose.Illness),
                    RoomId(diagnose.Room),
                    StaffId(diagnose.Staff));
            }
            else if (value is SubGoalDefinitionHospitalValue hospitalValue)
                data = Rule("hospital_value", hospitalValue.TargetValue);
            else if (value is SubGoalDefinitionHospitalAttractiveness attractiveness)
                data = Rule("attractiveness", attractiveness.TargetAttractiveness);
            else if (value is SubGoalDefinitionHygieneRating hygiene)
                data = Rule("hygiene", hygiene.Rating);
            else if (value is SubGoalDefinitionStaffMorale morale)
                data = Rule("staff_morale", morale.TargetPercentage);
            else if (value is SubGoalDefinitionThermalComfort thermal)
                data = Rule("thermal_comfort", thermal.Target);
            else if (value is SubGoalDefinitionDaysSinceLastDeath days)
                data = Rule("days_without_death", days.Days);
            else if (value is SubGoalDefinitionPatientDeaths deaths)
                data = Rule("patient_deaths", deaths.Deaths);
            else if (value is SubGoalDefinitionPrestige prestige)
                data = Rule("prestige", prestige.PrestigeTarget);
            else if (value is SubGoalDefinitionRoomPrestige roomPrestige)
            {
                data = Rule("room_prestige", roomPrestige.TargetLevel);
                data.reference = Pack(RoomId(roomPrestige.RoomDefinition));
            }
            else if (value is SubGoalDefinitionRoomCount roomCount)
                data = Rule("room_count", roomCount.TargetAmount);
            else if (value is SubGoalDefinitionBuildRoom room)
            {
                data = Rule("build_room", room.RequiredCount);
                data.reference = Pack(RoomId(room.RoomDefinition));
            }
            else if (value is SubGoalDefinitionBuildReception)
                data = Rule("build_reception", "0");
            else if (value is SubGoalDefinitionBuildItem item &&
                     item.Item != null &&
                     (item.ItemList == null || item.ItemList.Count == 0))
            {
                data = Rule("build_item", item.ItemCount);
                data.reference = Pack(Id(item.Item));
            }
            else if (value is SubGoalUpgradeItemDefinition upgrade)
                data = Rule("upgrade_item", upgrade.NumOfUpgrades);
            else if (value is SubGoalDefinitionTrainStaff train)
            {
                data = Rule("train_staff", train.TargetTrainingCount);
                data.reference = Pack(
                    StaffId(train.StaffType),
                    Id(train.QualificationType));
            }
            else if (value is SubGoalDefinitionPromoteStaff promote)
            {
                data = Rule("promote_staff", promote.TargetNumPromotions);
                data.reference = Pack(StaffId(promote.StaffType));
            }
            else if (value is SubGoalDefinitionHireStaff hire)
            {
                data = Rule("hire_staff", hire.StaffCount);
                data.reference = Pack(StaffId(hire.StaffDefinition));
            }
            else if (value is SubGoalDefinitionFireStaff fire)
                data = Rule("fire_staff", fire.NumStaffToFire);
            else if (value is SubGoalResearchPointsDefinition researchPoints)
                data = Rule("research_points", researchPoints.Points);
            else if (value is SubGoalResearchProjectDefinition researchProject)
            {
                data = Rule("research_project", "0");
                data.reference = Pack(
                    WrapperName<ResearchProjectDefinition>(
                        researchProject,
                        "_researchProject"));
            }
            else if (value is SubGoalLaunchMarketingCampaignDefinition launchMarketing)
                data = Rule("launch_marketing", launchMarketing.NumCampaigns);
            else if (value is SubGoalDefinitionMarketingCampaignCompleteGeneral generalMarketing &&
                     string.IsNullOrEmpty(
                         WrapperName<GeneralMarketingCampaignDefinition>(
                             generalMarketing,
                             "_campaign")))
                data = Rule("marketing_complete_general", generalMarketing.NumCampaigns);
            else if (value is SubGoalDefinitionMarketingCampaignCompleteIllness illnessMarketing &&
                     string.IsNullOrEmpty(
                         WrapperName<IllnessMarketingCampaignDefinition>(
                             illnessMarketing,
                             "_campaign")))
                data = Rule("marketing_complete_illness", illnessMarketing.NumCampaigns);
            else if (value is SubGoalDefinitionMarketingCampaignCompleteRecruitment recruitmentMarketing &&
                     string.IsNullOrEmpty(
                         WrapperName<RecruitmentMarketingCampaignDefinition>(
                             recruitmentMarketing,
                             "_campaign")))
                data = Rule("marketing_complete_recruitment", recruitmentMarketing.NumCampaigns);
            else if (value is SubGoalDefinitionMarketingCampaignMonthsSpentOn marketingMonths)
                data = Rule("marketing_months", marketingMonths.Months);
            else if (value is SubGoalDefinitionBuyPlots buyPlots)
                data = Rule("buy_plots", buyPlots.PlotCount);
            else if (value is SubGoalDefinitionOwnPlots ownPlots)
                data = Rule("own_plots", ownPlots.PlotCount);
            else if (value is SubGoalDefinitionOwnRoboPlots roboPlots)
                data = Rule("own_robo_plots", roboPlots.PlotCount);
            else if (value is SubGoalDefinitionShootMonoBeasts monoBeasts)
                data = Rule("shoot_monobeasts", monoBeasts.NumToShoot);
            else if (value is SubGoalDefinitionVIPSatisfied vip)
                data = Rule("vip_satisfied", vip.VIPSatisfiedTarget);
            else if (value is SubGoalDefinitionVaccination vaccination)
                data = Rule("vaccination", vaccination.Target);
            else if (value is SubGoalDefinitionRevealAliens aliens)
                data = Rule("reveal_aliens", aliens.NumAliens);
            else if (value is SubGoalDefinitionGenerateElectricity electricity)
                data = Rule("generate_electricity", electricity.TargetAmount);
            else if (value is SubGoalDefinitionMachineCatchFire fireMachine)
            {
                data = Rule("machine_catch_fire", fireMachine.NumToCatchFire);
                data.reference = Pack(Id(fireMachine.RoomItemDefinition));
            }
            else if (value is SubGoalDefinitionRotateItem rotate)
                data = Rule("rotate_item", rotate.RotationsRequired);
            else if (value is SubGoalMaintenanceJobDefinition maintenance)
            {
                data = Rule("maintenance_jobs", maintenance.NumOfJobs);
                data.reference = Pack(maintenance.JobType.ToString());
            }
            else if (value is SubGoalDefinitionBudget budget)
                data = Rule("budget", budget.TargetBudgetScore);
            else if (value is SubGoalDefinitionAnachronisticCureCount anachronisticCount)
                data = Rule("anachronistic_cure_count", anachronisticCount.Target);
            else if (value is SubGoalDefinitionAnachronisticCureRate anachronisticRate)
                data = Rule("anachronistic_cure_rate", anachronisticRate.Target);
            else if (value is SubGoalDefinitionAnachronisticTimeTunnelCount tunnelCount)
                data = Rule("time_tunnel_count", tunnelCount.Target);
            else if (value is SubGoalDefinitionAnachronisticTimeTunnelCure tunnelCure)
                data = Rule("time_tunnel_cure", tunnelCure.Target);
            else if (value is SubGoalDefinitionPatientTimePortalCureCount portalCure)
                data = Rule("time_portal_cure_count", portalCure.Target);
            else if (value is SubGoalDefinitionReachHordeWave hordeWave)
                data = Rule("reach_horde_wave", hordeWave.Wave);
            else if (value is SubGoalDefinitionReachWaveObjectivesHordeWave hordeObjectives)
                data = Rule("reach_horde_objectives", hordeObjectives.WaveToReach);
            else if (value is SubGoalDefinitionCameraMovement camera)
            {
                data = Rule("camera_movement", camera.Threshold);
                data.reference = Pack(camera.MovementType.ToString());
            }

            return data;
        }

        private static SubGoalDefinition BuildOne(
            ScenarioMapFormat.ObjectiveRuleData rule)
        {
            if (rule == null)
                return null;

            var reference0 = ReferencePart(rule.reference, 0);
            var reference1 = ReferencePart(rule.reference, 1);
            var reference2 = ReferencePart(rule.reference, 2);

            switch (rule.type)
            {
                case "earn_money":
                    return new SubGoalDefinitionEarnMoney
                        { TargetAmount = Int(rule.value) };
                case "reach_balance":
                    return new SubGoalDefinitionReachBalance
                        { Target = Int(rule.value) };
                case "reputation":
                    return new SubGoalDefinitionReputation
                        { Target = Int(rule.value) };
                case "cure_patients":
                    return new SubGoalDefinitionCurePatients
                    {
                        CureCount = Int(rule.value),
                        Illness = Resolve<IllnessDefinition>(reference0),
                        Room = ResolveRoom(reference1),
                        Staff = ResolveStaff(reference2)
                    };
                case "cure_rate":
                    return new SubGoalDefinitionCureRate
                    {
                        TargetCureRate = Float(rule.value),
                        Illness = Resolve<IllnessDefinition>(reference0),
                        Room = ResolveRoom(reference1)
                    };
                case "diagnose_patients":
                    return new SubGoalDiagnosePatientsDefinition
                    {
                        DiagnoseCount = Int(rule.value),
                        Illness = Resolve<IllnessDefinition>(reference0),
                        Room = ResolveRoom(reference1),
                        Staff = ResolveStaff(reference2)
                    };
                case "hospital_value":
                    return new SubGoalDefinitionHospitalValue
                        { TargetValue = Int(rule.value) };
                case "attractiveness":
                    return new SubGoalDefinitionHospitalAttractiveness
                        { TargetAttractiveness = Int(rule.value) };
                case "hygiene":
                    return new SubGoalDefinitionHygieneRating
                        { Rating = Float(rule.value) };
                case "staff_morale":
                    return new SubGoalDefinitionStaffMorale
                        { TargetPercentage = Int(rule.value) };
                case "thermal_comfort":
                    return new SubGoalDefinitionThermalComfort
                        { Target = Int(rule.value) };
                case "days_without_death":
                    return new SubGoalDefinitionDaysSinceLastDeath
                        { Days = Int(rule.value) };
                case "patient_deaths":
                    return new SubGoalDefinitionPatientDeaths
                        { Deaths = Int(rule.value) };
                case "prestige":
                    return new SubGoalDefinitionPrestige
                        { PrestigeTarget = Int(rule.value) };
                case "room_prestige":
                    return new SubGoalDefinitionRoomPrestige
                    {
                        RoomDefinition = ResolveRoom(reference0),
                        TargetLevel = Math.Max(1, Int(rule.value, 1))
                    };
                case "room_count":
                    return new SubGoalDefinitionRoomCount
                        { TargetAmount = Int(rule.value) };
                case "build_room":
                    var room = ResolveRoom(reference0);
                    return room == null ? null : new SubGoalDefinitionBuildRoom
                    {
                        RoomDefinition = room,
                        RequiredCount = Math.Max(1, Int(rule.value, 1)),
                        IncludeExisting = true
                    };
                case "build_reception":
                    return new SubGoalDefinitionBuildReception();
                case "build_item":
                    var item = Resolve<RoomItemDefinition>(reference0);
                    return item == null ? null : new SubGoalDefinitionBuildItem
                    {
                        Item = item,
                        ItemCount = Math.Max(1, Int(rule.value, 1)),
                        IncludeExisting = true
                    };
                case "upgrade_item":
                    return new SubGoalUpgradeItemDefinition
                        { NumOfUpgrades = Math.Max(1, Int(rule.value, 1)) };
                case "train_staff":
                    return new SubGoalDefinitionTrainStaff
                    {
                        StaffType = ResolveStaff(reference0),
                        QualificationType = Resolve<QualificationDefinition>(reference1),
                        TargetTrainingCount = Math.Max(1, Int(rule.value, 1))
                    };
                case "promote_staff":
                    return new SubGoalDefinitionPromoteStaff
                    {
                        StaffType = ResolveStaff(reference0),
                        TargetNumPromotions = Math.Max(1, Int(rule.value, 1))
                    };
                case "hire_staff":
                    return new SubGoalDefinitionHireStaff
                    {
                        StaffDefinition = ResolveStaff(reference0),
                        StaffCount = Math.Max(1, Int(rule.value, 1)),
                        IncludeExisting = true
                    };
                case "fire_staff":
                    return new SubGoalDefinitionFireStaff
                        { NumStaffToFire = Math.Max(1, Int(rule.value, 1)) };
                case "research_points":
                    return new SubGoalResearchPointsDefinition
                        { Points = Float(rule.value) };
                case "research_project":
                    var research = new SubGoalResearchProjectDefinition();
                    SetPrivate(
                        research,
                        "_researchProject",
                        Resolve<ResearchProjectDefinition>(reference0));
                    return research;
                case "launch_marketing":
                    return new SubGoalLaunchMarketingCampaignDefinition
                        { NumCampaigns = Math.Max(1, Int(rule.value, 1)) };
                case "marketing_complete_general":
                    return new SubGoalDefinitionMarketingCampaignCompleteGeneral
                    {
                        NumCampaigns = Math.Max(1, Int(rule.value, 1)),
                        OnlyCompleteOnMatchingCampaignType = true
                    };
                case "marketing_complete_illness":
                    return new SubGoalDefinitionMarketingCampaignCompleteIllness
                    {
                        NumCampaigns = Math.Max(1, Int(rule.value, 1)),
                        OnlyCompleteOnMatchingCampaignType = true
                    };
                case "marketing_complete_recruitment":
                    return new SubGoalDefinitionMarketingCampaignCompleteRecruitment
                    {
                        NumCampaigns = Math.Max(1, Int(rule.value, 1)),
                        OnlyCompleteOnMatchingCampaignType = true
                    };
                case "marketing_months":
                    return new SubGoalDefinitionMarketingCampaignMonthsSpentOn
                        { Months = Math.Max(1, Int(rule.value, 1)) };
                case "buy_plots":
                    return new SubGoalDefinitionBuyPlots
                        { PlotCount = Math.Max(1, Int(rule.value, 1)) };
                case "own_plots":
                    return new SubGoalDefinitionOwnPlots
                    {
                        PlotCount = Math.Max(1, Int(rule.value, 1)),
                        IncludeExisting = true
                    };
                case "own_robo_plots":
                    return new SubGoalDefinitionOwnRoboPlots
                    {
                        PlotCount = Math.Max(1, Int(rule.value, 1)),
                        IncludeExisting = true
                    };
                case "shoot_monobeasts":
                    return new SubGoalDefinitionShootMonoBeasts
                        { NumToShoot = Math.Max(1, Int(rule.value, 1)) };
                case "vip_satisfied":
                    return new SubGoalDefinitionVIPSatisfied
                        { VIPSatisfiedTarget = Math.Max(1, Int(rule.value, 1)) };
                case "vaccination":
                    return new SubGoalDefinitionVaccination
                        { Target = Math.Max(1, Int(rule.value, 1)) };
                case "reveal_aliens":
                    return new SubGoalDefinitionRevealAliens
                        { NumAliens = Math.Max(1, Int(rule.value, 1)) };
                case "generate_electricity":
                    return new SubGoalDefinitionGenerateElectricity
                        { TargetAmount = Int(rule.value) };
                case "machine_catch_fire":
                    return new SubGoalDefinitionMachineCatchFire
                    {
                        NumToCatchFire = Math.Max(1, Int(rule.value, 1)),
                        RoomItemDefinition = Resolve<RoomItemDefinition>(reference0)
                    };
                case "rotate_item":
                    return new SubGoalDefinitionRotateItem
                        { RotationsRequired = Math.Max(1, Int(rule.value, 3)) };
                case "maintenance_jobs":
                    return new SubGoalMaintenanceJobDefinition
                    {
                        NumOfJobs = Math.Max(1, Int(rule.value, 1)),
                        JobType = ParseEnum(
                            reference0,
                            JobMaintenance.JobDescription.BrokenMachine)
                    };
                case "budget":
                    return new SubGoalDefinitionBudget
                        { TargetBudgetScore = Int(rule.value) };
                case "anachronistic_cure_count":
                    return new SubGoalDefinitionAnachronisticCureCount
                        { Target = Int(rule.value) };
                case "anachronistic_cure_rate":
                    return new SubGoalDefinitionAnachronisticCureRate
                        { Target = Int(rule.value) };
                case "time_tunnel_count":
                    return new SubGoalDefinitionAnachronisticTimeTunnelCount
                        { Target = Int(rule.value) };
                case "time_tunnel_cure":
                    return new SubGoalDefinitionAnachronisticTimeTunnelCure
                        { Target = Int(rule.value) };
                case "time_portal_cure_count":
                    return new SubGoalDefinitionPatientTimePortalCureCount
                        { Target = Int(rule.value) };
                case "reach_horde_wave":
                    return new SubGoalDefinitionReachHordeWave
                        { Wave = Math.Max(1, Int(rule.value, 1)) };
                case "reach_horde_objectives":
                    return new SubGoalDefinitionReachWaveObjectivesHordeWave
                        { WaveToReach = Math.Max(1, Int(rule.value, 1)) };
                case "camera_movement":
                    return new SubGoalDefinitionCameraMovement
                    {
                        MovementType = ParseEnum(
                            reference0,
                            SubGoalDefinitionCameraMovement.Type.Pan),
                        Threshold = Float(rule.value)
                    };
                default:
                    return null;
            }
        }

        private static string EmptyReferenceLabel(
            ObjectiveRequirementReferenceKind kind)
        {
            var spanish = Spanish();
            switch (kind)
            {
                case ObjectiveRequirementReferenceKind.Room:
                    return spanish ? "Cualquier sala..." : "Any room...";
                case ObjectiveRequirementReferenceKind.Item:
                    return spanish ? "Cualquier objeto..." : "Any item...";
                case ObjectiveRequirementReferenceKind.ItemUpgrade:
                    return spanish ? "Cualquier mejora..." : "Any upgrade...";
                case ObjectiveRequirementReferenceKind.Illness:
                    return spanish ? "Cualquier enfermedad..." : "Any illness...";
                case ObjectiveRequirementReferenceKind.Staff:
                    return spanish ? "Cualquier personal..." : "Any staff...";
                case ObjectiveRequirementReferenceKind.Qualification:
                    return spanish ? "Cualquier cualificación..." : "Any qualification...";
                case ObjectiveRequirementReferenceKind.ResearchProject:
                    return spanish ? "Cualquier proyecto..." : "Any project...";
                case ObjectiveRequirementReferenceKind.MaintenanceJob:
                    return spanish ? "Tipo de mantenimiento..." : "Maintenance type...";
                case ObjectiveRequirementReferenceKind.CameraMovement:
                    return spanish ? "Movimiento..." : "Movement...";
                default:
                    return spanish ? "Cualquiera..." : "Any...";
            }
        }

        private static string MaintenanceJobLabel(
            JobMaintenance.JobDescription value)
        {
            if (!Spanish())
                return value.ToString();

            switch (value)
            {
                case JobMaintenance.JobDescription.BrokenMachine: return "Reparar máquina";
                case JobMaintenance.JobDescription.BlockedToilet: return "Desatascar baño";
                case JobMaintenance.JobDescription.OutOfStock: return "Reponer objeto";
                case JobMaintenance.JobDescription.WiltedPlant: return "Regar planta";
                case JobMaintenance.JobDescription.Litter: return "Recoger basura";
                case JobMaintenance.JobDescription.MedicalWaste: return "Residuos médicos";
                case JobMaintenance.JobDescription.Ghost: return "Capturar fantasma";
                case JobMaintenance.JobDescription.Vehicular: return "Mantenimiento de vehículo";
                default: return value.ToString();
            }
        }

        private static string CameraMovementLabel(
            SubGoalDefinitionCameraMovement.Type value)
        {
            if (!Spanish())
                return value.ToString();

            switch (value)
            {
                case SubGoalDefinitionCameraMovement.Type.Pan: return "Desplazar cámara";
                case SubGoalDefinitionCameraMovement.Type.Rotate: return "Rotar cámara";
                case SubGoalDefinitionCameraMovement.Type.Zoom: return "Zoom";
                case SubGoalDefinitionCameraMovement.Type.Pitch: return "Inclinar cámara";
                default: return value.ToString();
            }
        }

        private static bool Spanish()
        {
            var language = EditorLocalization.CurrentLanguageCode();
            return !string.IsNullOrEmpty(language) &&
                   language.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        }

        private static ScenarioMapFormat.ObjectiveRuleData Rule(
            string type,
            object value)
        {
            return new ScenarioMapFormat.ObjectiveRuleData
            {
                type = type ?? "",
                value = Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture) ?? ""
            };
        }

        private static string Pack(params string[] values)
        {
            var result = "";
            for (var index = 0; index < (values?.Length ?? 0); index++)
                result = SetReferencePart(result, index, values[index]);
            return result;
        }

        private static string Id<T>(SharedInstance<T> value)
            where T : class
        {
            return value == null ? "" : value.name ?? "";
        }

        private static string RoomId(
            SharedInstance<RoomDefinition> value)
        {
            return value?.Instance == null
                ? ""
                : value.Instance._type.ToString();
        }

        private static string StaffId(
            SharedInstance<StaffDefinition> value)
        {
            return value?.Instance == null
                ? ""
                : value.Instance._type.ToString();
        }

        private static string WrapperName<T>(
            object owner,
            string field)
            where T : class
        {
            var wrapper = AccessTools.Field(owner.GetType(), field)
                ?.GetValue(owner) as SharedInstance<T>;
            return wrapper == null ? "" : wrapper.name ?? "";
        }

        private static SharedInstance<T> Resolve<T>(string id)
            where T : class
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return Resources.FindObjectsOfTypeAll<SharedInstance<T>>()
                .FirstOrDefault(value =>
                    value?.Instance != null &&
                    string.Equals(
                        value.name,
                        id,
                        StringComparison.Ordinal));
        }

        private static SharedInstance<RoomDefinition> ResolveRoom(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return Resources.FindObjectsOfTypeAll<SharedInstance<RoomDefinition>>()
                .FirstOrDefault(value =>
                    value?.Instance != null &&
                    (string.Equals(
                         value.name,
                         id,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         value.Instance._type.ToString(),
                         id,
                         StringComparison.Ordinal)));
        }

        private static SharedInstance<StaffDefinition> ResolveStaff(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return Resources.FindObjectsOfTypeAll<SharedInstance<StaffDefinition>>()
                .FirstOrDefault(value =>
                    value?.Instance != null &&
                    (string.Equals(
                         value.name,
                         id,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         value.Instance._type.ToString(),
                         id,
                         StringComparison.Ordinal)));
        }

        private static T ParseEnum<T>(string value, T fallback)
            where T : struct
        {
            T parsed;
            return Enum.TryParse(value, true, out parsed)
                ? parsed
                : fallback;
        }

        private static int Int(string value, int fallback = 0)
        {
            int parsed;
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : fallback;
        }

        private static float Float(string value)
        {
            float parsed;
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : 0f;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            AccessTools.Field(target.GetType(), field)?.SetValue(target, value);
        }
    }
}
