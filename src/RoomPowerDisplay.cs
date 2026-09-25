using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnderPressure
{
    [HarmonyPatch(typeof(InspectorSubItemRoomInfo), "Setup")]
    internal static class RoomPowerSetupPatch
    {
        private static void Postfix(InspectorSubItemRoomInfo __instance, Room __0)
        {
            RoomPowerDisplay.Install(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(Room), "OnRoomItemAdded")]
    internal static class RoomPowerItemAddedPatch
    {
        private static void Postfix(Room __instance)
        {
            RoomPowerDisplay.NotifyChanged(__instance);
            PatientOutcomeDataViews.NotifyWorldChanged();
        }
    }

    [HarmonyPatch(typeof(Room), "OnRoomItemRemoved")]
    internal static class RoomPowerItemRemovedPatch
    {
        private static void Postfix(Room __instance)
        {
            RoomPowerDisplay.NotifyChanged(__instance);
            PatientOutcomeDataViews.NotifyWorldChanged();
        }
    }

    [HarmonyPatch(typeof(Room), "StaffUseRoom")]
    internal static class RoomPowerStaffEnteredPatch
    {
        private static void Postfix(Room __instance)
        {
            RoomPowerDisplay.NotifyChanged(__instance);
            PatientOutcomeDataViews.NotifyWorldChanged();
        }
    }

    [HarmonyPatch(typeof(Room), "StaffLeaveRoom")]
    internal static class RoomPowerStaffLeftPatch
    {
        private static void Postfix(Room __instance)
        {
            RoomPowerDisplay.NotifyChanged(__instance);
            PatientOutcomeDataViews.NotifyWorldChanged();
        }
    }

    [HarmonyPatch(typeof(Room), "OnStaffPickup")]
    internal static class RoomPowerStaffAssignmentPatch
    {
        private static void Postfix(Room __instance)
        {
            RoomPowerDisplay.NotifyChanged(__instance);
            PatientOutcomeDataViews.NotifyWorldChanged();
        }
    }

    internal sealed class RoomPowerDisplay : MonoBehaviour
    {
        private static readonly FieldInfo RoomField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_room");
        private static readonly FieldInfo JobsField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_jobs");
        private static readonly FieldInfo StaffListField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_staffListPanel");
        private static readonly FieldInfo PrestigeBarField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_prestigeBar");
        private static readonly FieldInfo PrestigeTextField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_prestigeLevelText");
        private static readonly FieldInfo AttractivenessBarField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_attractivenessBar");
        private static readonly FieldInfo HygieneBarField = AccessTools.Field(typeof(InspectorSubItemRoomInfo), "_hygieneBar");
        private static readonly FieldInfo StaffNameField = AccessTools.Field(typeof(InspectorStaffInfoRow), "StaffName");
        private static readonly FieldInfo StaffIconField = AccessTools.Field(typeof(InspectorStaffInfoRow), "StaffIcon");
        private static readonly FieldInfo DiagnosisDefaultIncreaseField = AccessTools.Field(typeof(IllnessDefinition), "_diagnosisCertaintyDefaultIncrease");
        private static readonly FieldInfo DiagnosisTypeIncreaseField = AccessTools.Field(typeof(IllnessDefinition.DiagnosisType), "_diagnosisCertaintyIncrease");
        private static readonly FieldInfo DiagnosisUpgradeMultiplierField = AccessTools.Field(typeof(IllnessDefinition.DiagnosisUpgrade), "_diagnosisCertaintyMultiplier");
        private static readonly FieldInfo TreatmentEffectivenessField = AccessTools.Field(typeof(IllnessDefinition.TreatmentType), "_effectiveness");
        private static readonly FieldInfo TreatmentEffectivenessMaxField = AccessTools.Field(typeof(IllnessDefinition.TreatmentType), "_effectivenessMax");
        private static readonly MethodInfo GetDiagnosisTypeMethod = AccessTools.Method(typeof(IllnessDefinition), "GetDiagnosisType");
        private static readonly MethodInfo GetDiagnosisUpgradeMethod = AccessTools.Method(typeof(IllnessDefinition), "GetDiagnosisUpgrade");
        private static readonly List<RoomPowerDisplay> ActiveDisplays = new List<RoomPowerDisplay>();

        private InspectorSubItemRoomInfo _owner;
        private Room _room;
        private ProgressBarMaskable _diagnosisBar;
        private ProgressBarMaskable _treatmentBar;
        private TMP_Text _objectBonusText;
        private Transform _listRoot;
        private GameObject _staffPanelRoot;
        private bool _dirty;
        private bool _diagnosisRelevant;
        private bool _treatmentRelevant;
        private readonly List<RoomModifierDiagnosis> _diagnosisModifiers = new List<RoomModifierDiagnosis>();
        private readonly List<RoomModifierTreatment> _treatmentModifiers = new List<RoomModifierTreatment>();

        internal static void Install(InspectorSubItemRoomInfo owner, Room room)
        {
            var display = owner.GetComponent<RoomPowerDisplay>();
            if (display == null) display = owner.gameObject.AddComponent<RoomPowerDisplay>();
            display.Bind(owner, room);
        }

        internal static void NotifyChanged(Room room)
        {
            for (var index = ActiveDisplays.Count - 1; index >= 0; --index)
            {
                var display = ActiveDisplays[index];
                if (display == null) ActiveDisplays.RemoveAt(index);
                else if (display._room == room) display._dirty = true;
            }
        }

        internal static void NotifySettingChanged()
        {
            foreach (var display in ActiveDisplays)
                if (display != null)
                    display._dirty = true;
        }

        private void Bind(InspectorSubItemRoomInfo owner, Room room)
        {
            _owner = owner;
            _room = room;
            if (!ActiveDisplays.Contains(this)) ActiveDisplays.Add(this);
            ResolveStaffPanel();
            if (_diagnosisBar == null) BuildRoomBars();
            if (_objectBonusText == null) BuildObjectBonusText();
            _dirty = true;
        }

        private void BuildRoomBars()
        {
            var prestige = PrestigeBarField.GetValue(_owner) as ProgressBarMaskable;
            var template = AttractivenessBarField.GetValue(_owner) as ProgressBarMaskable;
            var hygiene = HygieneBarField.GetValue(_owner) as ProgressBarMaskable;
            if (prestige == null || template == null || hygiene == null) return;
            var parent = prestige.transform.parent;
            _diagnosisBar = Instantiate(template, parent);
            _treatmentBar = Instantiate(template, parent);
            ConfigureBar(_diagnosisBar, "Diagnosis Power", template, prestige, "status_diagnosis",
                "room.diagnosis_power");
            ConfigureBar(_treatmentBar, "Treatment Power", hygiene, prestige, "status_pharmacy",
                "room.treatment_power");
        }

        private static void ConfigureBar(ProgressBarMaskable bar, string name, ProgressBarMaskable horizontalSource,
            ProgressBarMaskable verticalSource, string iconName, string tooltipKey)
        {
            bar.name = name;
            var tooltips = bar.GetComponentsInChildren<TooltipSpawner>(true);
            if (tooltips.Length > 0)
            {
                var tooltipSpawner = tooltips[0];
                tooltipSpawner.SetDataProvider(tooltip =>
                    tooltip.Text = ModLocalization.Get(tooltipKey) + ": " +
                                   Mathf.RoundToInt(Mathf.Clamp01(bar.Progress) * 100f) + "%");
                for (var index = 1; index < tooltips.Length; ++index) Object.Destroy(tooltips[index]);
            }
            var horizontal = (RectTransform)horizontalSource.transform;
            var vertical = (RectTransform)verticalSource.transform;
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = horizontal.anchorMin;
            rect.anchorMax = horizontal.anchorMax;
            rect.pivot = horizontal.pivot;
            rect.anchoredPosition = new Vector2(horizontal.anchoredPosition.x, vertical.anchoredPosition.y);
            rect.sizeDelta = horizontal.sizeDelta;
            rect.localScale = horizontal.localScale;
            var layout = bar.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            bar.Clamp = true;
            bar.ColorizeBar = true;
            ReplaceDecorativeIcon(bar, iconName);
        }

        private static void ReplaceDecorativeIcon(ProgressBarMaskable bar, string iconName)
        {
            var sprite = FindSpriteExact(iconName);
            if (sprite == null) return;
            Image best = null;
            var bestScore = float.MinValue;
            foreach (var image in bar.GetComponentsInChildren<Image>(true))
            {
                if (image == bar.BarImage || image.sprite == null) continue;
                var size = image.rectTransform.rect.size;
                if (size.x <= 0f || size.y <= 0f) size = image.rectTransform.sizeDelta;
                var ratioPenalty = Mathf.Abs(size.x - size.y);
                var score = Mathf.Min(size.x, size.y) - ratioPenalty * 2f;
                if (score <= bestScore) continue;
                best = image;
                bestScore = score;
            }
            if (best == null) return;
            best.sprite = sprite;
        }

        private static Sprite FindSpriteExact(string spriteName)
        {
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite.name.Equals(spriteName, StringComparison.OrdinalIgnoreCase))
                    return sprite;
            return null;
        }

        private void ResolveStaffPanel()
        {
            var panel = StaffListField.GetValue(_owner);
            if (panel == null) return;
            var listField = panel.GetType().GetField("ListRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var listObject = listField?.GetValue(panel) as GameObject;
            _listRoot = listObject != null ? listObject.transform : null;
            var panelField = panel.GetType().GetField("PanelRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _staffPanelRoot = panelField?.GetValue(panel) as GameObject;
        }

        private void BuildObjectBonusText()
        {
            if (_listRoot == null) return;
            var template = PrestigeTextField.GetValue(_owner) as TMP_Text;
            if (template == null) return;
            var parent = _staffPanelRoot != null && _staffPanelRoot.transform.parent != null
                ? _staffPanelRoot.transform.parent
                : _listRoot.parent;
            _objectBonusText = Instantiate(template, parent);
            _objectBonusText.name = "Under Pressure Object Bonuses";
            _objectBonusText.alignment = TextAlignmentOptions.Midline;
            _objectBonusText.enableAutoSizing = true;
            _objectBonusText.fontSizeMin = 11f;
            _objectBonusText.fontSizeMax = 17f;
            if (_staffPanelRoot != null)
                _objectBonusText.transform.SetSiblingIndex(_staffPanelRoot.transform.GetSiblingIndex());
            var layout = _objectBonusText.GetComponent<LayoutElement>() ??
                         _objectBonusText.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 24f;
            layout.minHeight = 24f;
        }

        private void LateUpdate()
        {
            if (!_dirty || _room == null) return;
            _dirty = false;
            RefreshAll();
            RefreshStaffRows();
        }

        private void OnDestroy()
        {
            ActiveDisplays.Remove(this);
        }

        private void RefreshAll()
        {
            if (!UnderPressurePlugin.ShouldShowRoomEffectiveness)
            {
                SetBar(_diagnosisBar, false, "D", 0f);
                SetBar(_treatmentBar, false, "T", 0f);
                if (_objectBonusText != null) _objectBonusText.gameObject.SetActive(false);
                return;
            }
            ScanRelevance();
            var diagnosis = CalculateAverageDiagnosis();
            var treatment = CalculateAverageTreatment();
            SetBar(_diagnosisBar, _diagnosisRelevant, "D", diagnosis);
            SetBar(_treatmentBar, _treatmentRelevant, "T", treatment);
            if (_objectBonusText != null)
            {
                _objectBonusText.gameObject.SetActive(_diagnosisRelevant || _treatmentRelevant);
                _objectBonusText.text = string.Format(ModLocalization.Get("room.object_power"),
                    FormatPercent(DiagnosisObjectBonus()), FormatPercent(TreatmentObjectBonus()));
            }
        }

        private void ScanRelevance()
        {
            _diagnosisRelevant = false;
            _treatmentRelevant = false;
            if (_room.Definition != null &&
                (_room.Definition._type == RoomDefinition.Type.GPOffice ||
                 (RoomDefinition.DiagnosisRooms != null &&
                  Array.IndexOf(RoomDefinition.DiagnosisRooms, _room.Definition._type) >= 0)))
                _diagnosisRelevant = true;
            var useType = _room.GetComponent<RoomUseTypeComponent>();
            if (useType != null)
            {
                _diagnosisRelevant = true;
                _treatmentRelevant = true;
            }
            foreach (var item in _room.FloorPlan.Items)
            {
                _diagnosisModifiers.Clear();
                item.GetRoomModifiersOfType(_diagnosisModifiers);
                if (_diagnosisModifiers.Count > 0) _diagnosisRelevant = true;
                _treatmentModifiers.Clear();
                item.GetRoomModifiersOfType(_treatmentModifiers);
                if (_treatmentModifiers.Count > 0) _treatmentRelevant = true;
            }
        }

        private float CalculateAverageDiagnosis()
        {
            var staff = _room.StaffWorkingInRoom;
            if (staff == null || staff.Count == 0) return 0f;
            var staffMultiplier = 0f;
            foreach (var worker in staff) staffMultiplier += worker.GetDiagnosisMultiplier(_room);
            staffMultiplier /= staff.Count;

            var illnesses = _room.Level.GameplayStatsTracker.DiscoveredIllnesses;
            if (illnesses == null || illnesses.Count == 0) return 0f;
            var itemMultiplier = GetBestDiagnosisItemMultiplier();
            var total = 0f;
            var count = 0;
            foreach (var illness in illnesses)
            {
                var diagnosisType = GetDiagnosisTypeMethod.Invoke(illness,
                    new object[] { _room }) as IllnessDefinition.DiagnosisType;
                var increase = diagnosisType != null
                    ? (float)DiagnosisTypeIncreaseField.GetValue(diagnosisType)
                    : (float)DiagnosisDefaultIncreaseField.GetValue(illness);
                if (increase <= 0f) continue;
                var upgrade = GetDiagnosisUpgradeMethod.Invoke(illness,
                    new object[] { _room.Level.ResearchManager }) as IllnessDefinition.DiagnosisUpgrade;
                var upgradeMultiplier = upgrade != null
                    ? (float)DiagnosisUpgradeMultiplierField.GetValue(upgrade)
                    : 1f;
                total += increase * upgradeMultiplier * _room.DiagnosisMultiplier * itemMultiplier * staffMultiplier;
                ++count;
            }
            return count == 0 ? 0f : Mathf.Clamp01(total / count / 100f *
                GameplayModifier.DifficultyFactor(UnderPressurePlugin.DiagnosisChanceSetting.Value));
        }

        private float CalculateAverageTreatment()
        {
            var staff = _room.StaffWorkingInRoom;
            if (staff == null || staff.Count == 0) return 0f;
            var staffSkill = 0f;
            foreach (var worker in staff) staffSkill += worker.GetTreatmentSkillRating(_room);
            staffSkill /= staff.Count;
            var power = Mathf.Clamp01(staffSkill + _room.TreatmentModifier + GetBestTreatmentItemBonus());

            var illnesses = _room.Level.GameplayStatsTracker.DiscoveredIllnesses;
            if (illnesses == null || illnesses.Count == 0) return 0f;
            var total = 0f;
            var count = 0;
            foreach (var illness in illnesses)
            {
                var treatment = illness.GetBestTreatmentType(_room.Definition, _room.Level.ResearchManager);
                if (treatment == null) continue;
                var minimum = (float)TreatmentEffectivenessField.GetValue(treatment);
                var maximum = (float)TreatmentEffectivenessMaxField.GetValue(treatment);
                total += Mathf.Lerp(minimum, maximum, power);
                ++count;
            }
            return count == 0 ? 0f : Mathf.Clamp01(total / count / 100f *
                GameplayModifier.DifficultyFactor(UnderPressurePlugin.TreatmentChanceSetting.Value));
        }

        private float GetBestDiagnosisItemMultiplier()
        {
            var best = 1f;
            foreach (var item in _room.FloorPlan.Items)
            {
                var value = 1f;
                _diagnosisModifiers.Clear();
                item.GetRoomModifiersOfType(_diagnosisModifiers);
                foreach (var modifier in _diagnosisModifiers)
                    if (!modifier.RoomWide)
                        value += modifier.Percentage / 100f;
                best = Mathf.Max(best, value);
            }
            return best;
        }

        private float GetBestTreatmentItemBonus()
        {
            var best = 0f;
            foreach (var item in _room.FloorPlan.Items)
            {
                var value = 0f;
                _treatmentModifiers.Clear();
                item.GetRoomModifiersOfType(_treatmentModifiers);
                foreach (var modifier in _treatmentModifiers)
                    if (!modifier.RoomWide)
                        value += modifier.Percentage / 100f;
                best = Mathf.Max(best, value);
            }
            return best;
        }

        private float DiagnosisObjectBonus() => Mathf.Max(0f, _room.DiagnosisMultiplier - 1f);
        private float TreatmentObjectBonus() => Mathf.Max(0f, _room.TreatmentModifier);

        private static void SetBar(ProgressBarMaskable bar, bool visible, string prefix, float value)
        {
            if (bar == null) return;
            bar.gameObject.SetActive(visible);
            if (!visible) return;
            bar.Progress = Mathf.Clamp01(value);
            bar.LabelText = prefix + " " + Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        private void RefreshStaffRows()
        {
            if (_room == null || _listRoot == null) return;
            var jobs = JobsField.GetValue(_owner) as IList;
            if (jobs == null) return;
            var count = Mathf.Min(jobs.Count, _listRoot.childCount);
            for (var index = 0; index < count; ++index)
            {
                var job = jobs[index] as Job;
                var worker = job?.GetStaff();
                if (worker == null) continue;
                var row = _listRoot.GetChild(index).GetComponent<InspectorStaffInfoRow>();
                var staffName = row != null ? StaffNameField.GetValue(row) as TMP_Text : null;
                if (staffName == null) continue;
                var diagnosis = Mathf.Clamp01(worker.GetDiagnosisMultiplier(_room));
                var treatment = Mathf.Clamp01(worker.GetTreatmentSkillRating(_room));
                var details = string.Format(ModLocalization.Get("room.staff_power"),
                    FormatPercent(diagnosis), FormatPercent(treatment));
                var detailsTransform = staffName.transform.Find("UnderPressurePowerText");
                var detailsText = detailsTransform != null ? detailsTransform.GetComponent<TMP_Text>() : null;
                if (!UnderPressurePlugin.ShouldShowRoomEffectiveness)
                {
                    if (detailsText != null) detailsText.gameObject.SetActive(false);
                    var disabledLayout = row.GetComponent<LayoutElement>();
                    if (disabledLayout != null) disabledLayout.enabled = false;
                    var disabledIcon = StaffIconField.GetValue(row) as Image;
                    if (disabledIcon != null) disabledIcon.rectTransform.localScale = Vector3.one;
                    continue;
                }
                if (detailsText == null)
                {
                    var obsolete = row.transform.Find("UnderPressurePowerText");
                    if (obsolete != null) Object.Destroy(obsolete.gameObject);
                    detailsText = Instantiate(staffName, staffName.transform);
                    detailsText.name = "UnderPressurePowerText";
                    foreach (var component in detailsText.GetComponentsInChildren<Component>(true))
                    {
                        var typeName = component.GetType().FullName;
                        if (typeName != null && typeName.StartsWith("I2.Loc.", StringComparison.Ordinal))
                            Object.Destroy(component);
                    }
                    foreach (var layoutComponent in detailsText.GetComponents<Component>())
                    {
                        if (!(layoutComponent is LayoutElement) && !(layoutComponent is ContentSizeFitter) &&
                            !(layoutComponent is AspectRatioFitter)) continue;
                        var behaviour = layoutComponent as Behaviour;
                        if (behaviour != null) behaviour.enabled = false;
                        Object.Destroy(layoutComponent);
                    }
                    var detailsRect = detailsText.rectTransform;
                    detailsRect.anchorMin = new Vector2(0f, 0f);
                    detailsRect.anchorMax = new Vector2(1f, 0f);
                    detailsRect.pivot = new Vector2(0f, 1f);
                    detailsRect.anchoredPosition = new Vector2(0f, 8f);
                    detailsRect.sizeDelta = new Vector2(0f, 22f);
                    detailsRect.localScale = Vector3.one;
                    detailsText.alignment = TextAlignmentOptions.MidlineLeft;
                    detailsText.enableAutoSizing = true;
                    detailsText.enableWordWrapping = false;
                    detailsText.overflowMode = TextOverflowModes.Overflow;
                    detailsText.fontSizeMin = 12f;
                    detailsText.fontSizeMax = 17f;
                    detailsText.color = new Color(0.47f, 0.84f, 0.95f, 1f);
                    staffName.enableAutoSizing = true;
                    staffName.fontSizeMin = 15f;
                    staffName.fontSizeMax = 21f;
                    var icon = StaffIconField.GetValue(row) as Image;
                    if (icon != null) icon.rectTransform.localScale = new Vector3(1.35f, 1.35f, 1f);
                }
                detailsText.gameObject.SetActive(true);
                detailsText.text = details;
                var layout = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
                layout.enabled = true;
                layout.minHeight = 68f;
                layout.preferredHeight = 68f;
            }
        }

        private static string FormatPercent(float value) => Mathf.RoundToInt(value * 100f) + "%";
    }
}
