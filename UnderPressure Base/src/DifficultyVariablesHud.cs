// Propósito: muestra en el HUD las variables adaptables activas y sus ajustes.
using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using TH20;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure
{
    [HarmonyPatch(typeof(TimeAndStatsMenu), "Setup")]
    internal static class DifficultyVariablesHudPatch
    {
        private static readonly FieldInfo BalanceTextField =
            AccessTools.Field(typeof(TimeAndStatsMenu), "_balanceText");

        private static void Postfix(TimeAndStatsMenu __instance, Level __0)
        {
            // Este HUD recibe el hospital activo ya construido; úsalo como fuente adaptable fiable.
            AdaptiveDifficulty.Attach(__0);
            var balanceText = BalanceTextField?.GetValue(__instance) as TMP_Text;
            if (balanceText == null || __0 == null) return;
            var display = balanceText.GetComponent<DifficultyVariablesHud>() ??
                          balanceText.gameObject.AddComponent<DifficultyVariablesHud>();
            display.Initialise(__0, balanceText);
        }
    }

    internal sealed class DifficultyVariablesHud : MonoBehaviour, IPointerClickHandler
    {
        private Level _level;
        private TMP_Text _balanceText;
        private GameObject _panel;
        private TMP_Text _labels;
        private TMP_Text _adjustments;
        private float _refreshTimer;

        internal void Initialise(Level level, TMP_Text balanceText)
        {
            _level = level;
            _balanceText = balanceText;
            _balanceText.raycastTarget = true;
            if (_panel == null) BuildPanel();
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left || _panel == null)
                return;
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf) Refresh();
        }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f) Refresh();
        }

        private void BuildPanel()
        {
            _panel = new GameObject("UnderPressure Difficulty Variables", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            var rect = (RectTransform)_panel.transform;
            rect.SetParent(_balanceText.rectTransform, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(0f, 8f);
            rect.sizeDelta = new Vector2(290f, 302f);

            var background = _panel.GetComponent<Image>();
            var nativeBackground = _balanceText.GetComponentInParent<Image>();
            if (nativeBackground != null)
            {
                background.sprite = nativeBackground.sprite;
                background.type = nativeBackground.type;
            }
            background.color = new Color(0.10f, 0.14f, 0.14f, 0.96f);
            background.raycastTarget = false;
            var outline = _panel.GetComponent<Outline>();
            outline.effectColor = new Color(0.88f, 0.92f, 0.90f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            _labels = CreateColumn("Variables", 10f, 8f, 204f, TextAlignmentOptions.TopLeft);
            _adjustments = CreateColumn("Adjustments", 216f, 8f, 64f, TextAlignmentOptions.TopRight);
            _panel.SetActive(false);
        }

        private TMP_Text CreateColumn(string name, float x, float y, float width,
            TextAlignmentOptions alignment)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            var rect = (RectTransform)child.transform;
            rect.SetParent(_panel.transform, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, 286f);
            var text = child.GetComponent<TextMeshProUGUI>();
            text.font = _balanceText.font;
            text.fontSharedMaterial = _balanceText.fontSharedMaterial;
            text.fontSize = 16f;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.richText = true;
            text.lineSpacing = 0f;
            return text;
        }

        private void Refresh()
        {
            _refreshTimer = 0.25f;
            if (_level == null || _labels == null) return;

            var labels = new StringBuilder();
            var adjustments = new StringBuilder();
            AddHeading(labels, adjustments, ModLocalization.Get("hud.variables.title"),
                ModLocalization.Get("hud.variables.adjustment"));

            AddGroup(labels, adjustments, ModLocalization.Get("hud.variables.economy"));
            AddStandard(labels, adjustments, "mod.staff_salaries",
                UnderPressurePlugin.StaffSalariesSetting);
            AddStandard(labels, adjustments, "mod.patient_income",
                UnderPressurePlugin.PatientIncomeSetting);
            var applicantWait = AdaptiveDifficulty.GetValue(UnderPressurePlugin.ApplicantWaitSetting, _level);
            Add(labels, adjustments, "mod.applicant_wait", applicantWait);
            AddStandard(labels, adjustments, "mod.electricity_bill",
                UnderPressurePlugin.ElectricityBillSetting);

            AddGroup(labels, adjustments, ModLocalization.Get("hud.variables.reputation"));
            AddStandard(labels, adjustments, "mod.hunger_thirst",
                UnderPressurePlugin.HungerThirstSetting);
            AddStandard(labels, adjustments, "mod.boredom",
                UnderPressurePlugin.BoredomSetting);
            AddStandard(labels, adjustments, "mod.happiness",
                UnderPressurePlugin.HappinessSetting);
            AddStandard(labels, adjustments, "mod.hygiene",
                UnderPressurePlugin.HygieneSetting);

            AddGroup(labels, adjustments, ModLocalization.Get("hud.variables.expansion"));
            AddStandard(labels, adjustments, "mod.diagnosis_chance",
                UnderPressurePlugin.DiagnosisChanceSetting);
            AddStandard(labels, adjustments, "mod.treatment_chance",
                UnderPressurePlugin.TreatmentChanceSetting);
            AddStandard(labels, adjustments, "mod.health_decay",
                UnderPressurePlugin.HealthDecaySetting);
            AddStandard(labels, adjustments, "mod.machine_wear",
                UnderPressurePlugin.MachineWearSetting);

            _labels.text = labels.ToString();
            _adjustments.text = adjustments.ToString();
        }

        private static void AddHeading(StringBuilder labels, StringBuilder adjustments,
            string label, string adjustment)
        {
            labels.Append("<b>").Append(label).AppendLine("</b>");
            adjustments.Append("<b>").Append(adjustment).AppendLine("</b>");
        }

        private static void AddGroup(StringBuilder labels, StringBuilder adjustments, string label)
        {
            labels.Append("<color=#59D5F5><b>").Append(label).AppendLine("</b></color>");
            adjustments.AppendLine();
        }

        private static void Add(StringBuilder labels, StringBuilder adjustments,
            string key, int adjustment)
        {
            labels.Append("  ").AppendLine(ModLocalization.Get(key));
            adjustments.AppendLine(Signed(adjustment));
        }

        private void AddStandard(StringBuilder labels, StringBuilder adjustments, string key,
            BepInEx.Configuration.ConfigEntry<int> setting)
        {
            var value = AdaptiveDifficulty.GetValue(setting, _level);
            Add(labels, adjustments, key, value);
        }

        private static string Signed(int value) => value > 0 ? "+" + value + "%" : value + "%";
    }
}
