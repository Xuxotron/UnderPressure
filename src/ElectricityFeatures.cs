using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;

namespace UnderPressure
{
    internal static class ElectricityFeatures
    {
        internal static bool ConsumesElectricity(IRoomItemDefinition definition)
        {
            if (definition == null) return false;
            if (definition.EnergyCost(0) > 0) return true;
            foreach (var modifier in definition.InteractionAttributeModifiers ??
                     new InteractionAttributeModifier[0])
            {
                if (modifier == null) continue;
                var reference = AccessTools.Field(typeof(InteractionAttributeModifier),
                    "_financeModifier")?.GetValue(modifier);
                if (reference == null) continue;
                var type = reference.GetType();
                FieldInfo instanceField = null;
                while (type != null && instanceField == null)
                {
                    instanceField = type.GetField("Instance", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    type = type.BaseType;
                }
                var finance = instanceField?.GetValue(reference) as FinanceModifier;
                if (finance != null && finance.EnergyCost > 0) return true;
            }
            return false;
        }

        internal static int Scale(int amount)
        {
            if (!UnderPressurePlugin.IsModEnabled) return amount;
            return Mathf.Max(0, Mathf.RoundToInt(amount *
                GameplayModifier.Factor(UnderPressurePlugin.ElectricityBillSetting.Value)));
        }

        internal static void NotifySettingChanged() => ElectricityGraphDisplay.NotifySettingChanged();
    }

    // Only the amount being paid is changed. The game's installed-item total is restored
    // immediately, so adding/removing an item continues to use its original definition.
    [HarmonyPatch(typeof(FinanceManager), "PayEnergyBill")]
    internal static class ElectricityBillPatch
    {
        private static readonly FieldInfo BillField = AccessTools.Field(typeof(FinanceManager), "_energyBill");
        private static readonly FieldInfo PerUseField = AccessTools.Field(typeof(FinanceManager), "_energyBillPerUse");

        private struct OriginalBills
        {
            internal int Bill;
            internal int PerUse;
        }

        private static void Prefix(FinanceManager __instance, out OriginalBills __state)
        {
            __state = new OriginalBills
            {
                Bill = (int)BillField.GetValue(__instance),
                PerUse = (int)PerUseField.GetValue(__instance)
            };
            BillField.SetValue(__instance, ElectricityFeatures.Scale(__state.Bill));
            PerUseField.SetValue(__instance, ElectricityFeatures.Scale(__state.PerUse));
        }

        private static void Postfix(FinanceManager __instance, OriginalBills __state)
        {
            BillField.SetValue(__instance, __state.Bill);
            // PayEnergyBill deliberately clears the per-use accumulator. Restoring it here
            // charged every previous task again on the following month.
        }
    }

    [HarmonyPatch(typeof(FinanceManager), "get_EnergyBills")]
    internal static class ElectricityBillPreviewPatch
    {
        private static void Postfix(ref int __result) => __result = ElectricityFeatures.Scale(__result);
    }

    [HarmonyPatch(typeof(RibbonItemRow), "TooltipDataProvider")]
    internal static class ElectricityItemTooltipPatch
    {
        private static void Postfix(RibbonItemRow __instance, Tooltip __0)
        {
            if (!UnderPressurePlugin.ShouldShowElectricity) return;
            var tooltip = __0 as TooltipItemButton;
            var definition = __instance.RoomItemDefinition;
            if (tooltip == null || definition == null) return;

            var fixedCost = definition.EnergyCost(0);
            var perUseCost = 0;
            foreach (var modifier in definition.InteractionAttributeModifiers ??
                     new InteractionAttributeModifier[0])
            {
                if (modifier == null) continue;
                var reference = AccessTools.Field(typeof(InteractionAttributeModifier),
                    "_financeModifier")?.GetValue(modifier);
                if (reference == null) continue;
                var type = reference.GetType();
                FieldInfo instanceField = null;
                while (type != null && instanceField == null)
                {
                    instanceField = type.GetField("Instance", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    type = type.BaseType;
                }
                var finance = instanceField?.GetValue(reference) as FinanceModifier;
                if (finance != null) perUseCost = Math.Max(perUseCost, finance.EnergyCost);
            }

            if (fixedCost <= 0 && perUseCost <= 0) return;
            var lines = new List<string>();
            if (fixedCost > 0)
                lines.Add(string.Format(ModLocalization.Get("item.electricity_month"),
                    ElectricityFeatures.Scale(fixedCost) + " $"));
            if (perUseCost > 0)
                lines.Add(string.Format(ModLocalization.Get("item.electricity_use"),
                    ElectricityFeatures.Scale(perUseCost) + " $"));
            var current = tooltip.FunctionalDescription.text?.TrimEnd('\r', '\n', ' ', '\t');
            tooltip.FunctionalDescription.text = string.IsNullOrEmpty(current)
                ? string.Join("\n", lines.ToArray())
                : current + "\n" + string.Join("\n", lines.ToArray());
            tooltip.FunctionalDescription.gameObject.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(OverviewMenuGraphPanelBase), "Setup")]
    internal static class ElectricityFinanceGraphSetupPatch
    {
        private static void Postfix(OverviewMenuGraphPanelBase __instance)
        {
            if (!(__instance is FinanceTabGraphPanel)) return;
            var display = __instance.GetComponent<ElectricityGraphDisplay>();
            if (display == null) display = __instance.gameObject.AddComponent<ElectricityGraphDisplay>();
            display.Bind(__instance);
        }
    }

    [HarmonyPatch(typeof(OverviewMenuGraphPanelBase), "SetMode")]
    internal static class ElectricityFinanceGraphModePatch
    {
        private static void Postfix(OverviewMenuGraphPanelBase __instance)
        {
            if (__instance is FinanceTabGraphPanel)
                __instance.GetComponent<ElectricityGraphDisplay>()?.RefreshScale();
        }
    }

    [HarmonyPatch(typeof(HospitalEventLog), "AddEvent")]
    internal static class ElectricityGraphEventPatch
    {
        private static void Postfix(HospitalEvent __0)
        {
            if (__0 is HospitalEventEnergyBillPaid)
                ElectricityGraphDisplay.NotifyBillPaid();
        }
    }

    internal sealed class ElectricityGraphDisplay : MonoBehaviour
    {
        private static readonly FieldInfo DefinitionsField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase), "_statDefinitions");
        private static readonly FieldInfo ModeField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase), "_currentDisplayMode");
        private static readonly FieldInfo DefinitionStatField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase.GraphStatDefinition), "Stat");
        private static readonly FieldInfo DefinitionGraphField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase.GraphStatDefinition), "Graph");
        private static readonly FieldInfo MonthlyField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase.GraphStatDefinition), "CachedMonthlyGraphData");
        private static readonly FieldInfo YearlyField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase.GraphStatDefinition), "CachedYearlyGraphData");
        private static readonly FieldInfo QuarterlyField = AccessTools.Field(
            typeof(OverviewMenuGraphPanelBase.GraphStatDefinition), "CachedQuarterlyGraphData");
        private static readonly List<ElectricityGraphDisplay> Displays = new List<ElectricityGraphDisplay>();

        private OverviewMenuGraphPanelBase _panel;
        private PanelItemGraph _graph;
        private List<LineGraph.DataVector2> _nativeMonthly;
        private List<LineGraph.DataVector2> _nativeYearly;
        private List<LineGraph.DataVector2> _nativeQuarterly;
        private string _nativeTitle;
        private double _nativeMin;
        private double _nativeMax;
        private double _monthlyMax = 1d;
        private double _yearlyMax = 1d;

        internal void Bind(OverviewMenuGraphPanelBase panel)
        {
            _panel = panel;
            _graph = null;
            var definitions = DefinitionsField?.GetValue(panel) as IList;
            if (definitions == null) return;
            foreach (var definition in definitions)
            {
                if ((LevelStatsDatabase.Stat)DefinitionStatField.GetValue(definition) !=
                    LevelStatsDatabase.Stat.TotalPhysicalAssetValue) continue;
                _graph = DefinitionGraphField.GetValue(definition) as PanelItemGraph;
                _nativeMonthly = MonthlyField.GetValue(definition) as List<LineGraph.DataVector2>;
                _nativeQuarterly = QuarterlyField.GetValue(definition) as List<LineGraph.DataVector2>;
                _nativeYearly = YearlyField.GetValue(definition) as List<LineGraph.DataVector2>;
                break;
            }
            if (_graph == null) return;
            _nativeTitle = _graph.AssignedButton != null ? _graph.AssignedButton.GetTitleText() : null;
            _nativeMin = _graph.MinYValue;
            _nativeMax = _graph.MaxYValue;
            if (!Displays.Contains(this)) Displays.Add(this);
            Refresh();
        }

        internal static void NotifySettingChanged()
        {
            for (var index = Displays.Count - 1; index >= 0; --index)
            {
                if (Displays[index] == null) Displays.RemoveAt(index);
                else Displays[index].Refresh();
            }
        }

        internal static void NotifyBillPaid() => NotifySettingChanged();

        private void OnDestroy() => Displays.Remove(this);

        private void Refresh()
        {
            if (_graph == null || _nativeMonthly == null || _nativeYearly == null ||
                _nativeQuarterly == null) return;
            if (!UnderPressurePlugin.ShouldShowElectricity)
            {
                _graph.AssignMonthlyData(_nativeMonthly);
                _graph.AssignQuarterlyData(_nativeQuarterly);
                _graph.AssignYearlyData(_nativeYearly);
                _graph.MinYValue = _nativeMin;
                _graph.MaxYValue = _nativeMax;
                if (_graph.AssignedButton != null)
                    _graph.AssignedButton.SetTitleText(_nativeTitle);
                ShowCurrentMode();
                return;
            }

            var level = _panel.GetLevel();
            if (level == null || level.HospitalEventLog == null) return;
            var events = new List<HospitalEvent>();
            level.HospitalEventLog.GetEvents(ref events, entry => entry is HospitalEventEnergyBillPaid);
            var monthlyBills = new Dictionary<int, double>();
            var yearlyBills = new Dictionary<int, double>();
            foreach (var entry in events)
            {
                var amount = Math.Abs(((HospitalEventEnergyBillPaid)entry).GetFinanceValue());
                var month = GameDateUtils.AsTotalMonths(entry.Date);
                monthlyBills[month] = amount;
                yearlyBills[entry.Date.Year] = yearlyBills.TryGetValue(entry.Date.Year, out var prior)
                    ? prior + amount : amount;
            }
            var monthly = Match(_nativeMonthly, monthlyBills);
            var quarterly = Match(_nativeQuarterly, monthlyBills);
            var yearly = Match(_nativeYearly, yearlyBills);
            _graph.AssignMonthlyData(monthly);
            _graph.AssignQuarterlyData(quarterly);
            _graph.AssignYearlyData(yearly);
            _monthlyMax = 1d;
            _yearlyMax = 1d;
            foreach (var point in monthly) _monthlyMax = Math.Max(_monthlyMax, point.y);
            foreach (var point in yearly) _yearlyMax = Math.Max(_yearlyMax, point.y);
            if (_graph.AssignedButton != null)
                _graph.AssignedButton.SetTitleText(ModLocalization.Get("graph.electricity_bill"));
            ShowCurrentMode();
        }

        private static List<LineGraph.DataVector2> Match(List<LineGraph.DataVector2> source,
            Dictionary<int, double> bills)
        {
            var result = new List<LineGraph.DataVector2>();
            foreach (var point in source)
                if (bills.TryGetValue((int)Math.Floor(point.x), out var amount))
                    result.Add(new LineGraph.DataVector2(point.x, amount));
            return result;
        }

        private void ShowCurrentMode()
        {
            RefreshScale();
            var mode = (GraphDisplayMode)ModeField.GetValue(_panel);
            if (mode == GraphDisplayMode.DmYearly) _graph.ShowYearlyData();
            else if (mode == GraphDisplayMode.DmMonthly) _graph.ShowMonthlyData();
        }

        internal void RefreshScale()
        {
            if (_graph == null || !UnderPressurePlugin.ShouldShowElectricity) return;
            var mode = (GraphDisplayMode)ModeField.GetValue(_panel);
            _graph.MinYValue = 0d;
            _graph.MaxYValue = mode == GraphDisplayMode.DmYearly ? _yearlyMax : _monthlyMax;
        }
    }
}
