using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    public static class PermanentMachineWearSystem
    {
        private const float RepairedPointsPerWearPercent = 100f;
        private const int MaximumWearPercent = 99;
        private const float Epsilon = 0.0001f;
        private static readonly FieldInfo EntitiesField = AccessTools.Field(typeof(EntityManager), "_entities");
        private static readonly Dictionary<int, float> RepairedPoints = new Dictionary<int, float>();
        private static Level _level;

        internal static bool Enabled => UnderPressurePlugin.ShouldUsePermanentMachineWear;

        public static void BeginLoad(Level level)
        {
            _level = level;
            RepairedPoints.Clear();
        }

        public static void LoadRecord(Level level, int itemId, float repairedPoints)
        {
            EnsureLevel(level);
            if (itemId >= 0 && repairedPoints > 0f && !float.IsNaN(repairedPoints) &&
                !float.IsInfinity(repairedPoints))
                RepairedPoints[itemId] = repairedPoints;
        }

        public static void CompleteLoad(Level level)
        {
            EnsureLevel(level);
            if (Enabled)
                ApplyCaps(level);
        }

        public static void AppendSaveRecords(List<string> lines, Level level)
        {
            if (lines == null || level == null)
                return;

            EnsureLevel(level);
            var liveItems = CurrentItems(level);
            var stale = new List<int>();
            foreach (var pair in RepairedPoints)
            {
                if (!liveItems.ContainsKey(pair.Key))
                {
                    stale.Add(pair.Key);
                    continue;
                }
                if (pair.Value > 0f)
                    lines.Add("W," + pair.Key + "," + pair.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            foreach (var itemId in stale)
                RepairedPoints.Remove(itemId);
        }

        internal static void NotifySettingChanged()
        {
            if (Enabled && _level != null)
                ApplyCaps(_level);
        }

        internal static float GetWearPercent(RoomItem item)
        {
            if (!IsMachine(item) || !ReferenceEquals(item.Level, _level) ||
                !RepairedPoints.TryGetValue(item.ID, out var points))
                return 0f;
            return Mathf.Clamp(Mathf.Floor(points / RepairedPointsPerWearPercent), 0f,
                MaximumWearPercent);
        }

        internal static void AddRepairedPoints(RoomItem item, float amount)
        {
            if (!Enabled || !IsMachine(item) || amount <= Epsilon)
                return;

            EnsureLevel(item.Level);
            RepairedPoints.TryGetValue(item.ID, out var current);
            RepairedPoints[item.ID] = current + amount;
            ApplyCap(item);
        }

        internal static void ApplyCap(RoomItem item)
        {
            if (!Enabled || !IsMachine(item))
                return;
            var floor = GetWearPercent(item);
            if (item.MaintenanceLevel.Value() + Epsilon < floor)
                item.MaintenanceLevel.SetValue(floor, true);
        }

        private static void EnsureLevel(Level level)
        {
            if (ReferenceEquals(_level, level))
                return;
            _level = level;
            RepairedPoints.Clear();
        }

        private static void ApplyCaps(Level level)
        {
            foreach (var item in CurrentItems(level).Values)
                ApplyCap(item);
        }

        private static Dictionary<int, RoomItem> CurrentItems(Level level)
        {
            var result = new Dictionary<int, RoomItem>();
            if (level?.EntityManager == null || EntitiesField?.GetValue(level.EntityManager) is not IList entities)
                return result;

            for (var index = 0; index < entities.Count; ++index)
                if (entities[index] is RoomItem item && IsMachine(item) &&
                    ReferenceEquals(item.Level, level) &&
                    !item.HasBeenDestroyed())
                    result[item.ID] = item;
            return result;
        }

        internal static bool IsMachine(RoomItem item) =>
            item?.MaintenanceLevel != null && item.Definition != null &&
            item.Definition.MaintenanceDescription == JobMaintenance.JobDescription.BrokenMachine;
    }

    [HarmonyPatch(typeof(StaffRecordManager), "OnRoomItemMaintenanceComplete")]
    internal static class PermanentWearMaintenanceCompletedPatch
    {
        private static void Postfix(RoomItem __0, JobMaintenance __2)
        {
            var item = __0;
            if (!PermanentMachineWearSystem.IsMachine(item) || __2 == null)
                return;
            PermanentMachineWearSystem.AddRepairedPoints(item,
                Mathf.Max(0f, __2.InitialMaintenanceValue - item.MaintenanceLevel.Value()));
        }
    }

    [HarmonyPatch(typeof(RoomItem), "IsFullyRepaired")]
    internal static class PermanentWearRepairCompletionPatch
    {
        private static void Postfix(RoomItem __instance, ref bool __result)
        {
            if (!PermanentMachineWearSystem.Enabled ||
                !PermanentMachineWearSystem.IsMachine(__instance))
                return;
            __result = __instance.MaintenanceLevel.Value() <=
                       PermanentMachineWearSystem.GetWearPercent(__instance) + 0.0001f;
        }
    }

    [HarmonyPatch(typeof(RoomItemMaintenanceComponent), "Tick")]
    internal static class PermanentWearMaintenanceClampPatch
    {
        private static void Postfix(RoomItemMaintenanceComponent __instance)
        {
            PermanentMachineWearSystem.ApplyCap(__instance?.GetOwner<RoomItem>());
        }
    }

    [HarmonyPatch(typeof(RoomItemUpgradeComponent), "RepairItem")]
    internal static class PermanentWearUpgradeRepairPatch
    {
        private static void Prefix(RoomItem __0, out float __state)
        {
            __state = __0?.MaintenanceLevel?.Value() ?? float.NaN;
        }

        private static void Postfix(RoomItem __0, float __state)
        {
            if (!PermanentMachineWearSystem.Enabled ||
                !PermanentMachineWearSystem.IsMachine(__0) || float.IsNaN(__state))
                return;
            var floor = PermanentMachineWearSystem.GetWearPercent(__0);
            var effectiveAfter = Mathf.Max(__0.MaintenanceLevel.Value(), floor);
            PermanentMachineWearSystem.AddRepairedPoints(__0, Mathf.Max(0f, __state - effectiveAfter));
            PermanentMachineWearSystem.ApplyCap(__0);
        }
    }

    [HarmonyPatch(typeof(SelectMenuRoomItem), "Setup", typeof(RoomItem), typeof(Level))]
    internal static class PermanentWearSelectMenuSetupPatch
    {
        private static readonly FieldInfo MaintenanceBarField =
            AccessTools.Field(typeof(SelectMenuRoomItem), "_maintenanceBar");

        private static void Postfix(SelectMenuRoomItem __instance, RoomItem __0)
        {
            Bind(__instance, MaintenanceBarField?.GetValue(__instance) as ProgressBarMaskable, __0);
        }

        private static void Bind(Component owner, ProgressBarMaskable bar, RoomItem item)
        {
            if (owner == null || bar == null)
                return;
            var overlay = owner.GetComponent<PermanentWearBarOverlay>() ??
                          owner.gameObject.AddComponent<PermanentWearBarOverlay>();
            overlay.Bind(bar, item);
        }
    }

    [HarmonyPatch(typeof(SelectMenuRoomItem), "Update")]
    internal static class PermanentWearSelectMenuUpdatePatch
    {
        private static readonly FieldInfo RoomItemField =
            AccessTools.Field(typeof(SelectMenuRoomItemBase), "_roomItem");
        private static readonly MethodInfo HideRepairButtonMethod =
            AccessTools.Method(typeof(SelectMenuRoomItem), "HideRepairButton");

        private static void Postfix(SelectMenuRoomItem __instance)
        {
            __instance?.GetComponent<PermanentWearBarOverlay>()?.Refresh();
            if (!PermanentMachineWearSystem.Enabled || __instance == null)
                return;
            var item = RoomItemField?.GetValue(__instance) as RoomItem;
            if (!PermanentMachineWearSystem.IsMachine(item))
                return;
            var wear = PermanentMachineWearSystem.GetWearPercent(item);
            if (wear > 0f && item.MaintenanceLevel.Value() <= wear + 0.0001f)
                HideRepairButtonMethod?.Invoke(__instance, null);
        }
    }

    [HarmonyPatch(typeof(HoverMenuRoomItem), "Setup", typeof(RoomItem), typeof(Level))]
    internal static class PermanentWearHoverMenuSetupPatch
    {
        private static readonly FieldInfo MaintenanceBarField =
            AccessTools.Field(typeof(HoverMenuRoomItem), "_maintenanceBar");

        private static void Postfix(HoverMenuRoomItem __instance, RoomItem __0)
        {
            var bar = MaintenanceBarField?.GetValue(__instance) as ProgressBarMaskable;
            if (__instance == null || bar == null)
                return;
            var overlay = __instance.GetComponent<PermanentWearBarOverlay>() ??
                          __instance.gameObject.AddComponent<PermanentWearBarOverlay>();
            overlay.Bind(bar, __0);
        }
    }

    [HarmonyPatch(typeof(HoverMenuRoomItem), "Update")]
    internal static class PermanentWearHoverMenuUpdatePatch
    {
        private static void Postfix(HoverMenuRoomItem __instance)
        {
            __instance?.GetComponent<PermanentWearBarOverlay>()?.Refresh();
        }
    }

    internal sealed class PermanentWearBarOverlay : MonoBehaviour
    {
        private static readonly Color Red = new Color(0.84f, 0.08f, 0.06f, 1f);
        private ProgressBarMaskable _bar;
        private RoomItem _item;
        private Image _fill;
        private RectTransform _rect;

        internal void Bind(ProgressBarMaskable bar, RoomItem item)
        {
            _bar = bar;
            _item = item;
            if (_fill == null)
                CreateFill();
            Refresh();
        }

        private void CreateFill()
        {
            var source = _bar?.BarImage;
            if (source == null || source.transform.parent == null)
                return;
            _fill = Instantiate(source, source.transform.parent, false);
            _fill.name = "Permanent Machine Wear";
            _fill.color = Red;
            _fill.raycastTarget = false;
            _fill.overrideSprite = source.overrideSprite;
            _fill.transform.SetAsLastSibling();
            _rect = _fill.rectTransform;
        }

        internal void Refresh()
        {
            if (_fill == null)
                CreateFill();
            if (_fill == null || _bar == null || _item == null)
                return;

            var enabled = PermanentMachineWearSystem.Enabled &&
                          PermanentMachineWearSystem.IsMachine(_item);
            var wear = enabled
                ? PermanentMachineWearSystem.GetWearPercent(_item)
                : 0f;
            _bar.LabelText = enabled
                ? string.Format(ModLocalization.Get("item.permanent_wear"),
                    Mathf.RoundToInt(wear))
                : string.Empty;
            _fill.gameObject.SetActive(wear > 0f && _bar.gameObject.activeInHierarchy);
            if (wear <= 0f || _rect == null)
                return;

            var parent = _rect.parent as RectTransform;
            if (parent == null)
                return;
            var padding = _bar.BarPadding;
            var width = Mathf.Max(0f, parent.rect.width - (padding?.horizontal ?? 0));
            _rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right,
                padding?.right ?? 0, width * wear / 100f);
            _fill.color = Red;
            _fill.transform.SetAsLastSibling();
        }

    }
}
