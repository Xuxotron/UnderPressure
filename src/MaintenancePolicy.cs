using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    internal static class MaintenancePolicySystem
    {
        internal const int DefaultCondition = 50;
        private const string ConfigSection = "Politica de mantenimiento";
        private static readonly Dictionary<string, ConfigEntry<int>> Entries =
            new Dictionary<string, ConfigEntry<int>>(StringComparer.Ordinal);
        private static readonly System.Reflection.FieldInfo EntitiesField =
            AccessTools.Field(typeof(EntityManager), "_entities");
        private static readonly System.Reflection.MethodInfo RepairedMethod =
            AccessTools.Method(typeof(RoomItem), "OnRepairedEvent");
        private static readonly System.Reflection.MethodInfo NeedsMaintenanceMethod =
            AccessTools.Method(typeof(RoomItem), "OnNeedsMaintenanceEvent");
        private static readonly System.Reflection.FieldInfo NativeThresholdField =
            AccessTools.Field(typeof(GameAlgorithmsConfig), "ItemMaintenanceThreshold");

        internal static int GetCondition(Level level)
        {
            var entry = GetEntry(level);
            return Mathf.Clamp(entry?.Value ?? DefaultCondition, 15, 100);
        }

        internal static void SetCondition(Level level, int condition, bool rebindItems)
        {
            condition = Mathf.Clamp(condition, 15, 100);
            var entry = GetEntry(level);
            if (entry != null && entry.Value != condition)
                entry.Value = condition;

            ApplyNativeThreshold(level);
            if (rebindItems)
                RebindCurrentItems(level);
        }

        internal static void Reset(Level level)
        {
            SetCondition(level, DefaultCondition, true);
        }

        internal static void ApplyNativeThreshold(Level level)
        {
            if (!UnderPressurePlugin.IsModEnabled || level == null)
                return;

            // RoomItem.MaintenanceLevel is deterioration: 0 is perfect and 100 is broken.
            // The policy slider is shown as remaining condition, hence the conversion.
            NativeThresholdField?.SetValue(GameAlgorithms.Config, 100f - GetCondition(level));
        }

        private static ConfigEntry<int> GetEntry(Level level)
        {
            var config = UnderPressurePlugin.ModConfig;
            if (config == null)
                return null;

            var key = GetLevelKey(level);
            if (Entries.TryGetValue(key, out var entry))
                return entry;

            entry = config.Bind(ConfigSection, key, DefaultCondition,
                new ConfigDescription(
                    "Estado minimo del objeto para solicitar una reparacion preventiva.",
                    new AcceptableValueRange<int>(15, 100)));
            Entries.Add(key, entry);
            return entry;
        }

        private static string GetLevelKey(Level level)
        {
            var slot = -1;
            try
            {
                if (level?.App?.SaveSystem != null)
                    slot = level.App.SaveSystem.CurrentSaveSlot;
            }
            catch
            {
                // A level can briefly exist before SaveSystem has finished initialising.
            }

            var levelId = string.IsNullOrEmpty(level?.UniqueID) ? "Nivel" : level.UniqueID;
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
                levelId = levelId.Replace(invalid, '_');
            return "Slot" + (slot + 1) + "." + levelId + ".EstadoMinimoReparacion";
        }

        private static void RebindCurrentItems(Level level)
        {
            if (level?.EntityManager == null || EntitiesField?.GetValue(level.EntityManager) is not IList entities)
                return;

            var nativeThreshold = 100f - GetCondition(level);
            for (var index = entities.Count - 1; index >= 0; --index)
            {
                if (entities[index] is not RoomItem item || !ReferenceEquals(item.Level, level))
                    continue;
                RebindItem(item, nativeThreshold);
            }
        }

        private static void RebindItem(RoomItem item, float nativeThreshold)
        {
            var maintenance = item?.MaintenanceLevel;
            if (maintenance == null || RepairedMethod == null || NeedsMaintenanceMethod == null)
                return;

            var repaired = (Action)Delegate.CreateDelegate(typeof(Action), item, RepairedMethod);
            var needsMaintenance = (Action)Delegate.CreateDelegate(typeof(Action), item, NeedsMaintenanceMethod);
            maintenance.RemoveCallback(repaired);
            maintenance.RemoveCallback(needsMaintenance);
            maintenance.LessThan(nativeThreshold, repaired, true);
            maintenance.GreaterThan(nativeThreshold, needsMaintenance, true);
        }
    }

    [HarmonyPatch(typeof(RoomItem), "SetupMaintenanceCallbacks")]
    internal static class RoomItemPreventiveMaintenanceThresholdPatch
    {
        private static void Prefix(RoomItem __instance)
        {
            MaintenancePolicySystem.ApplyNativeThreshold(__instance?.Level);
        }
    }

    [HarmonyPatch(typeof(PolicyTabOverviewPanel), "Setup", typeof(OverviewMenuTab))]
    internal static class MaintenancePolicyPanelPatch
    {
        private static void Postfix(PolicyTabOverviewPanel __instance, OverviewMenuTab __0)
        {
            if (!UnderPressurePlugin.IsModEnabled || __instance == null || __0 == null)
                return;

            var controller = __instance.GetComponent<MaintenancePolicySliderController>();
            if (controller == null)
                controller = __instance.gameObject.AddComponent<MaintenancePolicySliderController>();
            controller.Bind(__instance, __0.TheOverviewMenu?.TheLevel);
        }
    }

    internal sealed class MaintenancePolicySliderController : MonoBehaviour
    {
        private static readonly System.Reflection.FieldInfo DiagnosisSliderField =
            AccessTools.Field(typeof(PolicyTabOverviewPanel), "_sliderDiagnosisThreshold");
        private static readonly System.Reflection.FieldInfo DiagnosisTextField =
            AccessTools.Field(typeof(PolicyTabOverviewPanel), "_textDiagnosisThreshold");
        private static readonly System.Reflection.FieldInfo ResetButtonField =
            AccessTools.Field(typeof(PolicyTabOverviewPanel), "_buttonReset");

        private Level _level;
        private Slider _slider;
        private TMP_Text _label;
        private TMP_Text _value;
        private DynamicButton _resetButton;
        private bool _resetBound;

        internal void Bind(PolicyTabOverviewPanel panel, Level level)
        {
            _level = level;
            if (_slider == null && !CreateNativeRow(panel))
                return;

            RefreshLabel();
            _slider.onValueChanged.RemoveAllListeners();
            _slider.minValue = 15f;
            _slider.maxValue = 100f;
            _slider.wholeNumbers = true;
            _slider.value = MaintenancePolicySystem.GetCondition(_level);
            UpdateValue(_slider.value);
            _slider.onValueChanged.AddListener(OnValueChanged);
            MaintenancePolicySystem.ApplyNativeThreshold(_level);

            if (!_resetBound)
            {
                _resetButton = ResetButtonField?.GetValue(panel) as DynamicButton;
                if (_resetButton != null)
                {
                    _resetButton.onPrimaryDown.AddListener(OnReset);
                    _resetBound = true;
                }
            }
        }

        private bool CreateNativeRow(PolicyTabOverviewPanel panel)
        {
            var sourceSlider = DiagnosisSliderField?.GetValue(panel) as Slider;
            var sourceValue = DiagnosisTextField?.GetValue(panel) as TMP_Text;
            if (sourceSlider == null || sourceValue == null)
                return false;

            var sourceRow = FindSliderRow(panel.transform, sourceSlider.transform, sourceValue);
            if (sourceRow == null || sourceRow.parent == null)
                return false;

            var cloneObject = Instantiate(sourceRow.gameObject, sourceRow.parent, false);
            cloneObject.name = "Preventive Maintenance Threshold";
            var cloneRow = cloneObject.transform as RectTransform;
            cloneRow.SetSiblingIndex(sourceRow.GetSiblingIndex());

            _slider = FindCloneComponent(sourceRow, cloneRow, sourceSlider);
            _value = FindCloneComponent(sourceRow, cloneRow, sourceValue);
            if (_slider == null || _value == null)
            {
                Destroy(cloneObject);
                return false;
            }

            foreach (var text in cloneObject.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!ReferenceEquals(text, _value))
                {
                    _label = text;
                    break;
                }
            }

            var parentLayout = sourceRow.parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (parentLayout == null && cloneRow != null && sourceRow is RectTransform sourceRect)
                cloneRow.anchoredPosition = sourceRect.anchoredPosition +
                                            Vector2.up * (sourceRect.rect.height + 8f);
            return true;
        }

        private static RectTransform FindSliderRow(Transform panel, Transform slider, TMP_Text value)
        {
            for (var current = slider.parent; current != null && current != panel; current = current.parent)
            {
                var texts = current.GetComponentsInChildren<TMP_Text>(true);
                var containsValue = false;
                for (var index = 0; index < texts.Length; ++index)
                    if (ReferenceEquals(texts[index], value))
                        containsValue = true;
                if (containsValue && texts.Length >= 2)
                    return current as RectTransform;
            }
            return null;
        }

        private static T FindCloneComponent<T>(Transform sourceRoot, Transform cloneRoot, T source)
            where T : Component
        {
            var path = RelativePath(sourceRoot, source.transform);
            var target = string.IsNullOrEmpty(path) ? cloneRoot : cloneRoot.Find(path);
            return target?.GetComponent<T>();
        }

        private static string RelativePath(Transform root, Transform child)
        {
            if (ReferenceEquals(root, child))
                return string.Empty;
            var names = new Stack<string>();
            for (var current = child; current != null && current != root; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private void OnValueChanged(float value)
        {
            var condition = Mathf.RoundToInt(value);
            UpdateValue(condition);
            MaintenancePolicySystem.SetCondition(_level, condition, true);
        }

        private void OnReset()
        {
            MaintenancePolicySystem.Reset(_level);
            if (_slider != null)
                _slider.value = MaintenancePolicySystem.DefaultCondition;
        }

        private void RefreshLabel()
        {
            if (_label != null)
                _label.text = ModLocalization.Get("policy.preventive_maintenance");
        }

        private void UpdateValue(float value)
        {
            if (_value != null)
                _value.text = Mathf.RoundToInt(value) + "%";
        }

        private void OnDestroy()
        {
            if (_resetBound && _resetButton != null)
                _resetButton.onPrimaryDown.RemoveListener(OnReset);
        }
    }
}
