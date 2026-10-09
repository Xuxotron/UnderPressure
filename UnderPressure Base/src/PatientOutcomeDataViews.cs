// Purpose: Adds diagnosis, treatment, and electricity data views with live world colouring.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnderPressure
{
    [HarmonyPatch(typeof(DataViewButtons), "Setup")]
    internal static class PatientOutcomeDataViewSetupPatch
    {
        private static void Postfix(DataViewButtons __instance, DataViewManager __0)
        {
            PatientOutcomeDataViews.Install(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(Patient), "ReceiveDiagnosis")]
    internal static class PatientOutcomeDiagnosisChangedPatch
    {
        private static void Postfix(Patient __instance) => PatientOutcomeDataViews.NotifyPatientChanged(__instance);
    }

    [HarmonyPatch(typeof(Patient), "set_ReasonWaitingForRoom")]
    internal static class PatientOutcomeWaitingReasonChangedPatch
    {
        private static void Postfix(Patient __instance) => PatientOutcomeDataViews.NotifyPatientChanged(__instance);
    }

    [HarmonyPatch(typeof(Character), "set_GoingToRoom")]
    internal static class PatientOutcomeGoingToRoomChangedPatch
    {
        private static void Postfix(Character __instance)
        {
            var patient = __instance as Patient;
            if (patient != null) PatientOutcomeDataViews.NotifyPatientChanged(patient);
        }
    }

    [HarmonyPatch(typeof(Character), "set_QueuingAtRoom")]
    internal static class PatientOutcomeQueueChangedPatch
    {
        private static void Postfix(Character __instance)
        {
            var patient = __instance as Patient;
            if (patient != null) PatientOutcomeDataViews.NotifyPatientChanged(patient);
        }
    }

    [HarmonyPatch(typeof(RoomItem), "Upgrade")]
    internal static class PatientOutcomeItemUpgradePatch
    {
        private static void Postfix() => PatientOutcomeDataViews.NotifyWorldChanged();
    }

    internal sealed class PatientOutcomeDataViews : MonoBehaviour
    {
        private enum CustomMode { None, Diagnosis, Treatment, Electricity }

        private static readonly List<PatientOutcomeDataViews> ActiveControllers = new List<PatientOutcomeDataViews>();
        private static readonly FieldInfo ButtonsField = AccessTools.Field(typeof(DataViewButtons), "_attractivenessButton");
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(DataViewManager), "_level");
        private static readonly FieldInfo ManagerModeField = AccessTools.Field(typeof(DataViewManager), "_mode");
        private static readonly FieldInfo ManagerModeSetByPlayerField = AccessTools.Field(typeof(DataViewManager), "_modeSetByPlayer");
        private static readonly FieldInfo NextDataViewField = AccessTools.Field(typeof(DataViewManager), "_nextDataViewMode");
        private const int DiagnosisManagerMode = 600;
        private const int TreatmentManagerMode = 601;
        private const int ElectricityManagerMode = 602;
        private DataViewManager _manager;
        private Level _level;
        private DynamicButton _diagnosisButton;
        private DynamicButton _treatmentButton;
        private DynamicButton _electricityButton;
        private CustomMode _mode;
        private NativeDataViewAdapter _nativeAdapter;

        internal static void NotifyPatientChanged(Patient patient)
        {
            foreach (var controller in ActiveControllers)
                if (controller != null && controller._mode != CustomMode.None)
                    controller.RefreshPatient(patient);
        }

        internal static void NotifyWorldChanged()
        {
            foreach (var controller in ActiveControllers)
                if (controller != null && controller._mode != CustomMode.None)
                    controller.RefreshColors();
        }

        internal static void Install(DataViewButtons owner, DataViewManager manager)
        {
            var controller = owner.GetComponent<PatientOutcomeDataViews>();
            if (controller == null) controller = owner.gameObject.AddComponent<PatientOutcomeDataViews>();
            controller.Initialise(owner, manager);
        }

        private void Initialise(DataViewButtons owner, DataViewManager manager)
        {
            if (_manager != null) return;
            _manager = manager;
            _level = LevelField.GetValue(manager) as Level;
            if (!ActiveControllers.Contains(this)) ActiveControllers.Add(this);
            var buttons = ButtonsField.GetValue(owner) as Array;
            if (buttons == null || buttons.Length == 0) return;

            DynamicButton template = null;
            foreach (var entry in buttons)
            {
                var entryType = entry.GetType();
                var mode = AccessTools.Field(entryType, "Mode").GetValue(entry);
                var button = AccessTools.Field(entryType, "Button").GetValue(entry) as DynamicButton;
                if (Convert.ToInt32(mode) == 500) template = button;
            }
            if (template == null)
            {
                var last = buttons.GetValue(buttons.Length - 1);
                template = AccessTools.Field(last.GetType(), "Button").GetValue(last) as DynamicButton;
            }
            if (template == null) return;

            _diagnosisButton = CreateButton(template, "Under Pressure Diagnosis View",
                new[] { "T_UI_Icon_Job_Assignment_General_Diagnosis" },
                "view.diagnosis", CustomMode.Diagnosis, true);
            _treatmentButton = CreateButton(template, "Under Pressure Treatment View",
                new[] { "pharmacy_job_assignment_icon" },
                "view.treatment", CustomMode.Treatment, true);
            _electricityButton = CreateButton(template, "Under Pressure Electricity View",
                new[] { "energy_icon" }, "view.electricity", CustomMode.Electricity, true);

            ExpandButtonGrid(template.transform.parent as RectTransform);
            _nativeAdapter = new NativeDataViewAdapter(this);
        }

        private DynamicButton CreateButton(DynamicButton template, string name, string[] spriteNames,
            string tooltipKey, CustomMode mode, bool interactable)
        {
            var clone = Instantiate(template, template.transform.parent);
            clone.name = name;
            clone.onPrimaryDown.RemoveAllListeners();
            clone.interactable = interactable;
            if (interactable) clone.onPrimaryDown.AddListener(() => Toggle(mode));

            var tooltip = clone.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null)
            {
                tooltip.enabled = interactable;
                if (interactable) tooltip.SetDataProvider(value => value.Text = ModLocalization.Get(tooltipKey));
            }
            if (spriteNames != null) ReplaceIcon(clone, spriteNames);
            else
            {
                foreach (var image in clone.GetComponentsInChildren<Image>(true))
                    if (image.transform != clone.transform)
                        image.enabled = false;
            }
            return clone;
        }

        private static void ReplaceIcon(DynamicButton button, string[] names)
        {
            Sprite sprite = null;
            foreach (var name in names)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (candidate.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        sprite = candidate;
                        break;
                    }
                if (sprite != null) break;
            }
            if (sprite == null) return;

            var targetGraphic = button.targetGraphic;
            Image best = null;
            var bestArea = float.MaxValue;
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                if (image == targetGraphic || image.sprite == null) continue;
                var size = image.rectTransform.rect.size;
                var area = Mathf.Abs(size.x * size.y);
                if (area <= 1f || area >= bestArea) continue;
                bestArea = area;
                best = image;
            }
            if (best != null) best.sprite = sprite;
        }

        private static void ExpandButtonGrid(RectTransform grid)
        {
            if (grid == null) return;
            var layout = grid.GetComponent<GridLayoutGroup>();
            if (layout == null) return;
            layout.startAxis = GridLayoutGroup.Axis.Vertical;
            layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            layout.constraintCount = 3;
            var delta = new Vector2(layout.cellSize.x + layout.spacing.x, 0f);
            var parent = grid.parent as RectTransform;
            if (parent != null) parent.anchoredPosition -= delta;
            // Keep the original left edge untouched and grow only towards the right.
            grid.offsetMax += delta;
            var layoutElement = grid.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                if (delta.x > 0f) layoutElement.preferredWidth += delta.x;
                if (delta.y > 0f) layoutElement.preferredHeight += delta.y;
            }
            if (parent != null)
            {
                // VisualisationsTab is animated by its width. Keep its original 250-unit
                // rectangle intact; extend only the visible background and its contents.
                var background = parent.Find("Background") as RectTransform;
                if (background != null)
                {
                    background.offsetMax += delta;
                    // The native tab uses this polygon, not the visible Image, as its hit area.
                    var hitArea = background.GetComponent<PolygonCollider2D>();
                    if (hitArea != null)
                    {
                        for (var path = 0; path < hitArea.pathCount; ++path)
                        {
                            var points = hitArea.GetPath(path);
                            for (var point = 0; point < points.Length; ++point)
                                if (points[point].x > 0f)
                                    points[point].x += delta.x;
                            hitArea.SetPath(path, points);
                        }
                    }
                }
                var info = parent.Find("DataView Tab Button") as RectTransform;
                if (info != null) info.anchoredPosition += delta + new Vector2(3f, 0f);
            }
        }

        private void Toggle(CustomMode mode)
        {
            if (_mode == mode)
            {
                _manager.DisableOverlay(true);
                return;
            }
            _manager.DisableOverlay(true);
            _mode = mode;
            var managerMode = (DataViewManager.Mode)(mode == CustomMode.Diagnosis
                ? DiagnosisManagerMode : mode == CustomMode.Treatment
                    ? TreatmentManagerMode : ElectricityManagerMode);
            ManagerModeField.SetValue(_manager, managerMode);
            ManagerModeSetByPlayerField.SetValue(_manager, true);
            NextDataViewField.SetValue(_manager, _nativeAdapter);
            SetButtonStates();
            _manager.OnEnterMode?.Invoke(managerMode);
        }

        private void RefreshColors()
        {
            foreach (var patient in _level.CharacterManager.Patients)
                RefreshPatient(patient);
        }

        private void RefreshPatient(Patient patient)
        {
            if (patient == null || _mode == CustomMode.None || _mode == CustomMode.Electricity) return;
            var treatment = patient.CurrentMode == Patient.Mode.Normal && patient.IsGoingForTreatment();
            var relevant = patient.CurrentMode == Patient.Mode.Normal &&
                           (_mode == CustomMode.Treatment ? treatment : !treatment);
            if (!relevant)
            {
                patient.Visual.SetValueMaterial(Color.white);
                return;
            }
            var value = _mode == CustomMode.Diagnosis
                ? Mathf.Clamp01(patient.DiagnosisCertainty / 100f)
                : GetTreatmentChance(patient);
            patient.Visual.SetValueMaterial(value < 0f
                ? Color.white
                : ValueColor(value));
        }

        private static float GetTreatmentChance(Patient patient)
        {
            Room room = null;
            if (patient.IsInTreatmentRoom()) room = patient.RoomUsing;
            else if (patient.QueuingAtRoom != null) room = patient.QueuingAtRoom;
            else if (patient.GoingToRoom != null) room = patient.GoingToRoom;
            if (room == null) return -1f;
            var staff = GameAlgorithms.FindStaffLikelyToSeePatient(room);
            var item = patient.Interaction != null ? patient.Interaction.ParentRoomItem : null;
            return Mathf.Clamp01(GameAlgorithms.CalculateEstimatedTreatmentOutcome(patient, staff, room, item)
                .ChanceOfSuccess / 100f);
        }

        private static Color ValueColor(float value)
        {
            value = Mathf.Clamp01(value);
            return value < 0.5f
                ? Color.Lerp(new Color(0.9f, 0.08f, 0.03f), Color.yellow, value * 2f)
                : Color.Lerp(Color.yellow, new Color(0.05f, 0.9f, 0.12f), (value - 0.5f) * 2f);
        }

        private void EnableCustomView()
        {
            _level.VisualManager.RoomLightingManager.EnableDesaturatedHospital();
            if (_mode == CustomMode.Electricity)
            {
                SetButtonStates();
                return;
            }
            _level.CharacterEvents.OnPatientSpawned += OnPatientSpawned;
            _level.CharacterEvents.OnStaffSpawned += OnStaffSpawned;
            foreach (var patient in _level.CharacterManager.Patients)
                patient.Visual.ValueModeEnabled = true;
            foreach (var staff in _level.CharacterManager.StaffMembers)
            {
                staff.Visual.ValueModeEnabled = true;
                staff.Visual.SetValueMaterial(Color.white);
            }
            SetButtonStates();
            RefreshColors();
        }

        private void OnPatientSpawned(Patient patient)
        {
            patient.Visual.ValueModeEnabled = true;
            RefreshPatient(patient);
        }

        private static void OnStaffSpawned(Staff staff)
        {
            staff.Visual.ValueModeEnabled = true;
            staff.Visual.SetValueMaterial(Color.white);
        }

        private void DisableCustomView()
        {
            if (_mode == CustomMode.None) return;
            if (_mode == CustomMode.Electricity)
            {
                foreach (var room in _level.WorldState.AllRooms)
                    foreach (var item in room.FloorPlan.Items)
                        if (item != null && item.Visual != null)
                            item.Visual.DisableValueMaterial();
            }
            else
            {
                _level.CharacterEvents.OnPatientSpawned -= OnPatientSpawned;
                _level.CharacterEvents.OnStaffSpawned -= OnStaffSpawned;
                foreach (var patient in _level.CharacterManager.Patients)
                    patient.Visual.ValueModeEnabled = false;
                foreach (var staff in _level.CharacterManager.StaffMembers)
                    staff.Visual.ValueModeEnabled = false;
            }
            _mode = CustomMode.None;
            SetButtonStates();
        }

        private void SetButtonStates()
        {
            SetButtonState(_diagnosisButton, _mode == CustomMode.Diagnosis);
            SetButtonState(_treatmentButton, _mode == CustomMode.Treatment);
            SetButtonState(_electricityButton, _mode == CustomMode.Electricity);
        }

        private static void SetButtonState(DynamicButton button, bool selected)
        {
            if (button == null) return;
            var animator = button.GetComponent<ButtonAnimator>();
            if (animator != null) animator.CurrentState = selected ? ButtonAnimator.State.Selected : ButtonAnimator.State.Selectable;
        }

        private void OnDestroy()
        {
            ActiveControllers.Remove(this);
            if (_mode != CustomMode.None) DisableCustomView();
        }

        private sealed class NativeDataViewAdapter : IDataViewMode
        {
            private readonly PatientOutcomeDataViews _owner;

            internal NativeDataViewAdapter(PatientOutcomeDataViews owner) => _owner = owner;

            public void Enable(DataViewManager.Mode mode) => _owner.EnableCustomView();

            public void Disable() => _owner.DisableCustomView();

            // Deliberadamente vacío: los colores se recalculan solo al cambiar pacientes,
            // personal u objetos, nunca una o varias veces por fotograma.
            public void Update() { }
        }
    }
}
