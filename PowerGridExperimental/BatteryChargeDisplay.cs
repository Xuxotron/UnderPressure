using System.Reflection;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.PowerGrid
{
    internal sealed class BatteryChargeDisplay : MonoBehaviour
    {
        private static readonly LocalisedString MaximumCapacityText = EnergyLocalization.Create(
            "energy.battery.maximum_capacity", "Capacidad máxima", "Maximum capacity");

        private RoomItem _item;
        private ProgressBarMaskable _bar;
        private GameObject _section;
        private TMP_Text _capacityLabel;

        internal void Bind(RoomItem item, ProgressBarMaskable bar, GameObject section, TMP_Text labelTemplate)
        {
            _item = item;
            _bar = bar;
            _section = section;
            if (EnergyRoomItems.IsBattery(item) && _capacityLabel == null)
                _capacityLabel = CreateCapacityLabel(labelTemplate, section);
            RefreshDisplay();
        }

        internal void RefreshDisplay()
        {
            if (!PowerGridPrototype.TryGetBatteryCharge(_item, out var progress, out var roundedMaximum))
            {
                if (_capacityLabel != null) _capacityLabel.gameObject.SetActive(false);
                return;
            }

            if (_capacityLabel != null)
            {
                _capacityLabel.text = MaximumCapacityText.Translation + ": +" + roundedMaximum;
                _capacityLabel.gameObject.SetActive(true);
            }
            if (_section != null) _section.SetActive(true);
            if (_bar == null) return;
            _bar.Clamp = true;
            _bar.ColorizeBar = true;
            _bar.Progress = progress;
        }

        private static TMP_Text CreateCapacityLabel(TMP_Text template, GameObject section)
        {
            if (template == null || section == null || section.transform.parent == null) return null;
            var labelObject = new GameObject("Battery Maximum Capacity", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            labelObject.transform.SetParent(section.transform.parent, false);
            labelObject.transform.SetSiblingIndex(section.transform.GetSiblingIndex());

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
            label.color = template.color;
            label.alignment = template.alignment;
            label.fontStyle = template.fontStyle;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = Mathf.Min(template.fontSize, 18f);
            label.enableWordWrapping = false;
            label.raycastTarget = false;

            var layout = labelObject.GetComponent<LayoutElement>();
            layout.minHeight = 22f;
            layout.preferredHeight = 22f;
            layout.flexibleWidth = 1f;
            return label;
        }
    }

    internal static class BatteryChargeMenuFields
    {
        internal static BatteryChargeDisplay Bind(Component menu, RoomItem item, FieldInfo barField,
            FieldInfo sectionField, FieldInfo nameField)
        {
            var display = menu.GetComponent<BatteryChargeDisplay>() ??
                          menu.gameObject.AddComponent<BatteryChargeDisplay>();
            var bar = barField?.GetValue(menu) as ProgressBarMaskable;
            var section = sectionField?.GetValue(menu) as GameObject;
            var label = nameField?.GetValue(menu) as TMP_Text;
            display.Bind(item, bar, section, label);
            return display;
        }
    }

    [HarmonyPatch(typeof(SelectMenuRoomItem), "Setup", typeof(RoomItem), typeof(Level))]
    internal static class BatteryChargeSelectSetupPatch
    {
        private static readonly FieldInfo BarField = AccessTools.Field(typeof(SelectMenuRoomItem), "_maintenanceBar");
        private static readonly FieldInfo SectionField = AccessTools.Field(typeof(SelectMenuRoomItem), "_maintenanceSection");
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(SelectMenuRoomItem), "_itemName");

        private static void Postfix(SelectMenuRoomItem __instance, RoomItem __0)
        {
            BatteryChargeMenuFields.Bind(__instance, __0, BarField, SectionField, NameField);
        }
    }

    [HarmonyPatch(typeof(SelectMenuRoomItem), "Update")]
    internal static class BatteryChargeSelectUpdatePatch
    {
        private static void Postfix(SelectMenuRoomItem __instance)
        {
            __instance.GetComponent<BatteryChargeDisplay>()?.RefreshDisplay();
        }
    }

    [HarmonyPatch(typeof(HoverMenuRoomItem), "Setup", typeof(RoomItem), typeof(Level))]
    internal static class BatteryChargeHoverSetupPatch
    {
        private static readonly FieldInfo BarField = AccessTools.Field(typeof(HoverMenuRoomItem), "_maintenanceBar");
        private static readonly FieldInfo SectionField = AccessTools.Field(typeof(HoverMenuRoomItem), "_maintenanceSection");
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(HoverMenuRoomItem), "_name");

        private static void Postfix(HoverMenuRoomItem __instance, RoomItem __0)
        {
            BatteryChargeMenuFields.Bind(__instance, __0, BarField, SectionField, NameField);
        }
    }

    [HarmonyPatch(typeof(HoverMenuRoomItem), "Update")]
    internal static class BatteryChargeHoverUpdatePatch
    {
        private static void Postfix(HoverMenuRoomItem __instance)
        {
            __instance.GetComponent<BatteryChargeDisplay>()?.RefreshDisplay();
        }
    }
}
