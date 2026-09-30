using System;
using System.Collections;
using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    // Hogsport's tutorial is an ExternalBehavior, not a standalone C# script.
    internal static class FirstLevelTutorial
    {
        private static readonly HashSet<int> PreparedTrees = new HashSet<int>();

        internal static bool ShouldSkip(Task task)
        {
            if (!UnderPressurePlugin.ShouldDisableTutorial || task == null) return false;
            var tree = task.Owner as LevelScriptBehaviorTree;
            return tree != null && tree.Level != null && tree.Level.Config != null &&
                   tree.Level.Config.UniqueId == "901";
        }

        internal static bool ShouldSkip(Behavior behavior)
        {
            if (!UnderPressurePlugin.ShouldDisableTutorial) return false;
            var tree = behavior as LevelScriptBehaviorTree;
            return tree != null && tree.Level != null && tree.Level.Config != null &&
                   tree.Level.Config.UniqueId == "901";
        }

        internal static bool ShouldSkip(TH20.Level level) =>
            UnderPressurePlugin.ShouldDisableTutorial && level != null &&
            level.Config != null && level.Config.UniqueId == "901";

        internal static bool ShouldSkipButtonPopup(TH20.TutorialButtonClickedMessage button)
        {
            var level = AccessTools.Field(typeof(TH20.TutorialButtonClickedMessage), "_level")
                ?.GetValue(button) as TH20.Level;
            return ShouldSkip(level);
        }

        internal static bool ShouldSkipObjective(Task task, string name)
        {
            if (!ShouldSkip(task)) return false;
            return name != "ObjectiveLevel1" && name != "ObjectiveLevel2" &&
                   name != "ObjectiveLevel3";
        }

        internal static bool ShouldSkipTutorialWait(TH20.BTA.WaitNamed task) =>
            ShouldSkip(task) && task.TimerName != null &&
            task.TimerName.StartsWith("TutorialWait", StringComparison.Ordinal);

        internal static IEnumerator UnlockAfterStart(LevelScriptBehaviorTree tree)
        {
            // Let the build menu subscribe to BuildEvents before sending the unlocks.
            yield return null;
            if (!ShouldSkip(tree)) yield break;

            var rooms = new Dictionary<RoomDefinition, bool>();
            var items = new Dictionary<RoomItemDefinition, bool>();
            foreach (var action in tree.FindTasks<TH20.BTA.AddRoomDefinition>())
                CollectDefinitions(action, "_rooms", rooms);

            foreach (var action in tree.FindTasks<TH20.BTA.AddRoomItemDefinition>())
                CollectDefinitions(action, "_items", items);

            // Star objectives can also award build items; unlock only those rewards,
            // not money or stars, so the normal objectives remain meaningful.
            foreach (var action in tree.FindTasks<TH20.BTA.Level.CreateObjective>())
                CollectRewardDefinitions(action.Objective, rooms, items);
            foreach (var action in tree.FindTasks<TH20.BTA.Level.CreateHiddenObjective>())
                CollectRewardDefinitions(action.Objective, rooms, items);

            foreach (var room in rooms)
                tree.Level.BuildEvents.OnAddRoomDefinition?.Invoke(room.Key, room.Value, false, false);
            foreach (var item in items)
                tree.Level.BuildEvents.OnAddRoomItemDefinition?.Invoke(item.Key, item.Value, false, false);

            UnderPressurePlugin.Log.LogInfo(
                $"Hogsport: {rooms.Count} salas y {items.Count} objetos desbloqueados al empezar.");
            foreach (var room in rooms.Keys)
                UnderPressurePlugin.Log.LogInfo("Sala inicial: " + room.GetSanitizedName());
            foreach (var item in items.Keys)
                UnderPressurePlugin.Log.LogInfo("Objeto inicial: " + item.GetSanitizedName());
        }

        private static void CollectDefinitions<T>(object action, string fieldName, Dictionary<T, bool> result)
            where T : class
        {
            var wrappers = AccessTools.Field(action.GetType(), fieldName)?.GetValue(action) as Array;
            if (wrappers == null) return;
            var unlock = AccessTools.Field(action.GetType(), "_unlockItem")?.GetValue(action) as bool? ?? false;
            foreach (var wrapper in wrappers)
            {
                var definition = GetInstance(wrapper) as T;
                if (definition != null)
                    result[definition] = unlock || (result.TryGetValue(definition, out var prior) && prior);
            }
        }

        private static object GetInstance(object wrapper) => wrapper == null ? null :
            AccessTools.Field(wrapper.GetType(), "Instance")?.GetValue(wrapper);

        private static void CollectRewardDefinitions(object wrapper,
            Dictionary<RoomDefinition, bool> rooms, Dictionary<RoomItemDefinition, bool> items)
        {
            var objective = GetInstance(wrapper) as ObjectiveDefinition;
            if (objective?.CompletionRewards == null) return;
            foreach (var reward in objective.CompletionRewards)
            {
                if (reward is RewardRoom rewardRoom)
                {
                    var room = GetInstance(AccessTools.Field(typeof(RewardRoom), "_definition")
                        ?.GetValue(rewardRoom)) as RoomDefinition;
                    if (room != null) rooms[room] = true;
                }
                else if (reward is RewardRoomItem rewardItem)
                {
                    var item = GetInstance(AccessTools.Field(typeof(RewardRoomItem), "_definition")
                        ?.GetValue(rewardItem)) as RoomItemDefinition;
                    if (item != null) items[item] = true;
                }
            }
        }

        internal static void Prepare(Behavior behavior)
        {
            if (!ShouldSkip(behavior)) return;
            var tree = (LevelScriptBehaviorTree)behavior;
            if (PreparedTrees.Add(tree.GetInstanceID()))
                tree.StartCoroutine(UnlockAfterStart(tree));
        }
    }

    [HarmonyPatch(typeof(Behavior), "OnBehaviorStarted")]
    internal static class FirstLevelStartPatch
    {
        private static void Postfix(Behavior __instance) => FirstLevelTutorial.Prepare(__instance);
    }

    [HarmonyPatch(typeof(TH20.BTA.ShowMessage), "OnStart")]
    internal static class FirstLevelMessagePatch
    {
        private static bool Prefix(TH20.BTA.ShowMessage __instance)
        {
            if (!FirstLevelTutorial.ShouldSkip(__instance)) return true;
            AccessTools.Field(typeof(TH20.BTA.ShowMessage), "_messageDismissed")
                .SetValue(__instance, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(TH20.BTA.ShowAdvisorMessage), "OnUpdate")]
    internal static class FirstLevelAdvisorPatch
    {
        private static bool Prefix(TH20.BTA.ShowAdvisorMessage __instance, ref TaskStatus __result)
        {
            if (!FirstLevelTutorial.ShouldSkip(__instance)) return true;
            __result = TaskStatus.Success;
            return false;
        }
    }

    // Complete the introductory objective tasks without creating HUD entries.
    // The three star objectives retain the game's original creation/completion path.
    [HarmonyPatch(typeof(TH20.BTA.Level.CreateObjective), "OnStart")]
    internal static class FirstLevelObjectiveStartPatch
    {
        private static bool Prefix(TH20.BTA.Level.CreateObjective __instance) =>
            !FirstLevelTutorial.ShouldSkipObjective(__instance, __instance.Name);
    }

    [HarmonyPatch(typeof(TH20.BTA.Level.CreateObjective), "OnUpdate")]
    internal static class FirstLevelObjectiveUpdatePatch
    {
        private static bool Prefix(TH20.BTA.Level.CreateObjective __instance, ref TaskStatus __result)
        {
            if (!FirstLevelTutorial.ShouldSkipObjective(__instance, __instance.Name)) return true;
            __result = TaskStatus.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(TH20.BTA.Level.CreateHiddenObjective), "OnStart")]
    internal static class FirstLevelHiddenObjectiveStartPatch
    {
        private static bool Prefix(TH20.BTA.Level.CreateHiddenObjective __instance) =>
            !FirstLevelTutorial.ShouldSkip(__instance);
    }

    [HarmonyPatch(typeof(TH20.BTA.Level.CreateHiddenObjective), "OnUpdate")]
    internal static class FirstLevelHiddenObjectiveUpdatePatch
    {
        private static bool Prefix(TH20.BTA.Level.CreateHiddenObjective __instance, ref TaskStatus __result)
        {
            if (!FirstLevelTutorial.ShouldSkip(__instance)) return true;
            __result = TaskStatus.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(TH20.BTA.WaitNamed), "OnStart")]
    internal static class FirstLevelWaitStartPatch
    {
        private static bool Prefix(TH20.BTA.WaitNamed __instance) =>
            !FirstLevelTutorial.ShouldSkipTutorialWait(__instance);
    }

    [HarmonyPatch(typeof(TH20.BTA.WaitNamed), "OnUpdate")]
    internal static class FirstLevelWaitUpdatePatch
    {
        private static bool Prefix(TH20.BTA.WaitNamed __instance, ref TaskStatus __result)
        {
            if (!FirstLevelTutorial.ShouldSkipTutorialWait(__instance)) return true;
            __result = TaskStatus.Success;
            return false;
        }
    }

    [HarmonyPatch(typeof(TH20.BTA.Level.CircleActiveSubgoal), "OnUpdate")]
    internal static class FirstLevelCirclePatch
    {
        private static bool Prefix(TH20.BTA.Level.CircleActiveSubgoal __instance,
            ref TaskStatus __result)
        {
            if (!FirstLevelTutorial.ShouldSkip(__instance)) return true;
            __result = TaskStatus.Success;
            return false;
        }
    }

    // Menu-help popups are independent of the level's behavior tree. Every
    // first-click help button (staff, patients, illnesses, finances, etc.) uses
    // this component, so suppress its dispatcher rather than individual panels.
    [HarmonyPatch(typeof(TH20.TutorialButtonClickedMessage), "TryShowMessage")]
    internal static class FirstLevelMenuHelpPatch
    {
        private static bool Prefix(TH20.TutorialButtonClickedMessage __instance) =>
            !FirstLevelTutorial.ShouldSkipButtonPopup(__instance);
    }

    [HarmonyPatch(typeof(TH20.TutorialButtonClickedMessage), "ShowMessage")]
    internal static class FirstLevelMenuHelpDelayedPatch
    {
        private static bool Prefix(TH20.TutorialButtonClickedMessage __instance) =>
            !FirstLevelTutorial.ShouldSkipButtonPopup(__instance);
    }
}
