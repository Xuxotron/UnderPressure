// Purpose: Displays active difficulty variables, adjustments, and calculated results in the HUD.
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
        private TMP_Text _results;
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
            rect.sizeDelta = new Vector2(370f, 302f);

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

            _labels = CreateColumn("Variables", 10f, 8f, 220f, TextAlignmentOptions.TopLeft);
            _adjustments = CreateColumn("Adjustments", 232f, 8f, 66f, TextAlignmentOptions.TopRight);
            _results = CreateColumn("Results", 304f, 8f, 56f, TextAlignmentOptions.TopRight);
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
            var results = new StringBuilder();
            AddHeading(labels, adjustments, results, ModLocalization.Get("hud.variables.title"),
                ModLocalization.Get("hud.variables.adjustment"), ModLocalization.Get("hud.variables.result"));

            var economySource = AdaptiveDifficulty.EconomyValue(_level);
            AddGroup(labels, adjustments, results, ModLocalization.Get("hud.variables.economy"),
                UnderPressurePlugin.AdaptiveEconomySetting.Value, economySource);
            AddStandard(labels, adjustments, results, "mod.staff_salaries",
                UnderPressurePlugin.StaffSalariesSetting, false);
            AddStandard(labels, adjustments, results, "mod.patient_income",
                UnderPressurePlugin.PatientIncomeSetting, true);
            var applicantWait = AdaptiveDifficulty.GetValue(UnderPressurePlugin.ApplicantWaitSetting, _level);
            Add(labels, adjustments, results, "mod.applicant_wait", applicantWait,
                Mathf.Max(0.25f, GameplayModifier.Factor(applicantWait)));
            AddStandard(labels, adjustments, results, "mod.electricity_bill",
                UnderPressurePlugin.ElectricityBillSetting, false);

            var reputationSource = Mathf.Clamp(Mathf.RoundToInt(
                _level.ReputationTracker.OverallReputation * 100f), 0, 100);
            AddGroup(labels, adjustments, results, ModLocalization.Get("hud.variables.reputation"),
                UnderPressurePlugin.AdaptiveReputationSetting.Value, reputationSource);
            AddStandard(labels, adjustments, results, "mod.hunger_thirst",
                UnderPressurePlugin.HungerThirstSetting, false);
            AddStandard(labels, adjustments, results, "mod.happiness",
                UnderPressurePlugin.HappinessSetting, false);
            AddStandard(labels, adjustments, results, "mod.hygiene",
                UnderPressurePlugin.HygieneSetting, false);
            AddStandard(labels, adjustments, results, "mod.health_decay",
                UnderPressurePlugin.HealthDecaySetting, false);

            var expansionSource = Mathf.Clamp(_level.PrestigeTracker.Level * 5, 0, 100);
            AddGroup(labels, adjustments, results, ModLocalization.Get("hud.variables.expansion"),
                UnderPressurePlugin.AdaptiveExpansionSetting.Value, expansionSource);
            AddStandard(labels, adjustments, results, "mod.diagnosis_chance",
                UnderPressurePlugin.DiagnosisChanceSetting, true);
            AddStandard(labels, adjustments, results, "mod.treatment_chance",
                UnderPressurePlugin.TreatmentChanceSetting, true);
            AddStandard(labels, adjustments, results, "mod.patient_arrival",
                UnderPressurePlugin.PatientArrivalSetting, false);
            AddStandard(labels, adjustments, results, "mod.machine_wear",
                UnderPressurePlugin.MachineWearSetting, false);

            _labels.text = labels.ToString();
            _adjustments.text = adjustments.ToString();
            _results.text = results.ToString();
        }

        private static void AddHeading(StringBuilder labels, StringBuilder adjustments,
            StringBuilder results, string label, string adjustment, string result)
        {
            labels.Append("<b>").Append(label).AppendLine("</b>");
            adjustments.Append("<b>").Append(adjustment).AppendLine("</b>");
            results.Append("<b>").Append(result).AppendLine("</b>");
        }

        private static void AddGroup(StringBuilder labels, StringBuilder adjustments,
            StringBuilder results, string label, bool adaptive, int source)
        {
            labels.Append("<color=#59D5F5><b>").Append(label).AppendLine("</b></color>");
            adjustments.AppendLine(adaptive ? Signed(source) : ModLocalization.Get("hud.variables.manual"));
            results.AppendLine();
        }

        private static void Add(StringBuilder labels, StringBuilder adjustments,
            StringBuilder results, string key, int adjustment, float factor)
        {
            labels.Append("  ").AppendLine(ModLocalization.Get(key));
            adjustments.AppendLine(Signed(adjustment));
            results.Append((factor * 100f).ToString("0.#")).AppendLine("%");
        }

        private void AddStandard(StringBuilder labels, StringBuilder adjustments,
            StringBuilder results, string key, BepInEx.Configuration.ConfigEntry<int> setting,
            bool inverse)
        {
            var value = AdaptiveDifficulty.GetValue(setting, _level);
            Add(labels, adjustments, results, key, value, inverse
                ? GameplayModifier.DifficultyFactor(value)
                : GameplayModifier.Factor(value));
        }

        private static string Signed(int value) => value > 0 ? "+" + value + "%" : value + "%";
    }
}
