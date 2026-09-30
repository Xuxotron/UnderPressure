using System;
using System.Reflection;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnderPressure
{
    [HarmonyPatch(typeof(PreferencesScreen), "Setup", typeof(App),
        typeof(ControlBindingsLocalisationParamsManager), typeof(MonoBehaviour))]
    internal static class PreferencesScreenSetupPatch
    {
        private static void Postfix(PreferencesScreen __instance)
        {
            try
            {
                ModSettingsTab.Install(__instance);
            }
            catch (Exception exception)
            {
                UnderPressurePlugin.Log.LogError("No se pudo crear la pestaña MOD: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(PreferencesScreen), "SetTabActive")]
    internal static class PreferencesScreenSetTabPatch
    {
        private static void Postfix(PreferencesScreen __instance)
        {
            __instance.GetComponent<ModSettingsTab>()?.DeactivateModTab();
        }
    }

    [HarmonyPatch(typeof(PreferencesScreen), "OnLocalize")]
    internal static class PreferencesScreenLocalizePatch
    {
        private static void Postfix(PreferencesScreen __instance)
        {
            __instance.GetComponent<ModSettingsTab>()?.RefreshLocalizedText();
        }
    }

    internal sealed class ModSettingsTab : MonoBehaviour
    {
        private static readonly Type ScreenType = typeof(PreferencesScreen);
        private static readonly MethodInfo SetTabActiveMethod = AccessTools.Method(ScreenType, "SetTabActive");
        private static readonly FieldInfo TooltipColliderField =
            AccessTools.Field(typeof(TooltipSpawner), "_collider");

        private PreferencesScreen _screen;
        private Transform _tab;
        private Transform _contents;
        private DynamicButton _tabButton;
        private ButtonAnimator _tabAnimator;
        private Toggle _skipIntroToggle;
        private TMP_Text _skipIntroLabel;
        private Toggle _autoLoadLastSaveToggle;
        private TMP_Text _autoLoadLastSaveLabel;
        private Toggle _disableTutorialToggle;
        private TMP_Text _disableTutorialLabel;
        private Toggle _roomEffectivenessToggle;
        private TMP_Text _roomEffectivenessLabel;
        private Toggle _imperfectStaffToggle;
        private TMP_Text _imperfectStaffLabel;
        private Toggle _electricityToggle;
        private TMP_Text _electricityLabel;
        private Toggle _separateReputationPrestigeToggle;
        private TMP_Text _separateReputationPrestigeLabel;
        private Toggle _permanentMachineWearToggle;
        private TMP_Text _permanentMachineWearLabel;
        private Toggle _visualLightingToggle;
        private TMP_Text _visualLightingLabel;
        private Toggle _unlockLampsToggle;
        private TMP_Text _unlockLampsLabel;
        private Toggle _disableDiagnosisChargesToggle;
        private TMP_Text _disableDiagnosisChargesLabel;
        private Toggle _disableAwardsToggle;
        private TMP_Text _disableAwardsLabel;
        private Toggle _masterToggle;
        private TMP_Text _tabLabel;
        private TMP_Text _nativeSliderLabel;
        private TMP_Text _nativeSliderValue;
        private GameObject _tooltipPrefab;
        private float _tooltipHoverTime;
        private static Sprite _nativeFillSprite;
        private static Color32[] _nativeFillPixels;
        private static readonly Dictionary<int, Sprite> TintedFillSprites = new Dictionary<int, Sprite>();
        private readonly List<SliderBinding> _sliders = new List<SliderBinding>();
        private readonly List<AdaptiveGroup> _adaptiveGroups = new List<AdaptiveGroup>();
        private bool _applyingGlobalDifficulty;
        private bool _showingModTab;

        internal static void Install(PreferencesScreen screen)
        {
            if (screen == null || screen.GetComponent<ModSettingsTab>() != null)
                return;

            var controller = screen.gameObject.AddComponent<ModSettingsTab>();
            controller.Build(screen);
        }

        private void Build(PreferencesScreen screen)
        {
            _screen = screen;
            ReduceNativeTitleFont(screen);
            var videoContents = GetField<Transform>(screen, "_videoTabContents");
            var languageButton = GetField<DynamicButton>(screen, "_languageTabButton");
            var creditsButton = GetField<Button>(screen, "_creditsButton");

            if (videoContents == null || languageButton == null)
                return;

            var headerParent = creditsButton != null ? creditsButton.transform.parent : screen.transform;
            _tabButton = Instantiate(languageButton, headerParent);
            _tabButton.name = "UnderPressureTabButton";
            _tab = _tabButton.transform;
            _tab.SetAsLastSibling();
            _tabAnimator = _tabButton.GetComponentInChildren<ButtonAnimator>(true);
            _tabLabel = _tabButton.GetComponentInChildren<TMP_Text>(true);

            var headerRect = (RectTransform)_tabButton.transform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(0f, 1f);
            headerRect.pivot = new Vector2(0f, 1f);
            headerRect.anchoredPosition = new Vector2(265f, -20f);
            headerRect.sizeDelta = new Vector2(190f, 42f);
            headerRect.localScale = Vector3.one;

            RemoveLocalisers(_tabButton.transform);
            _tabButton.onPrimaryDown.RemoveAllListeners();
            _tabButton.onPrimaryDown.AddListener(ShowModTab);
            SetTabCaption();

            _contents = Instantiate(videoContents, videoContents.parent);
            _contents.name = "UnderPressureTabContents";
            BuildContents();
            BuildMasterToggle(headerParent);
            _contents.gameObject.SetActive(false);
        }

        private void BuildMasterToggle(Transform headerParent)
        {
            if (_skipIntroToggle == null) return;
            _masterToggle = Instantiate(_skipIntroToggle, headerParent);
            _masterToggle.name = "UnderPressureMasterToggle";
            RemoveLocalisers(_masterToggle.transform);
            RemoveTooltips(_masterToggle.transform);
            var rect = (RectTransform)_masterToggle.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            var tabRect = _tab as RectTransform;
            var toggleX = tabRect != null ? tabRect.anchoredPosition.x + tabRect.rect.width + 8f : 428f;
            rect.anchoredPosition = new Vector2(toggleX, -22f);
            rect.sizeDelta = new Vector2(38f, 38f);
            rect.localScale = Vector3.one;
            _masterToggle.onValueChanged.RemoveAllListeners();
            _masterToggle.isOn = UnderPressurePlugin.IsModEnabled;
            _masterToggle.onValueChanged.AddListener(SetMasterEnabled);
            _masterToggle.transform.SetAsLastSibling();
            AddTooltip(_masterToggle.gameObject, "tooltip.mod_enabled");
        }

        private void BuildContents()
        {
            var content = _contents.Find("Scroll View/Viewport/Content");
            if (content == null)
                return;
            DisableAutomaticLayout(content);

            var sourceSection = content.Find("Window Settings");
            if (sourceSection == null)
                return;

            CaptureNativeTooltipStyle();

            var sourceSlider = content.GetComponentInChildren<Slider>(true);
            CaptureNativeSliderStyle(sourceSlider, content);
            Transform nativeVideoRow = null;
            foreach (var candidate in _screen.GetComponentsInChildren<Slider>(true))
                if (candidate.name == "MaximumFPSSlider")
                {
                    nativeVideoRow = candidate.transform.parent;
                    break;
                }
            var section = Instantiate(sourceSection, content);
            section.name = "Startup Settings";
            DisableAutomaticLayout(section);
            for (var index = content.childCount - 1; index >= 0; --index)
            {
                var child = content.GetChild(index);
                if (child != section)
                    Object.Destroy(child.gameObject);
            }

            for (var index = section.childCount - 1; index >= 0; --index)
            {
                var child = section.GetChild(index);
                if (child.name != "Background" && child.name != "Fullscreen Toggle")
                    Object.Destroy(child.gameObject);
            }

            var rightSection = Instantiate(section, content);
            rightSection.name = "Startup Settings Right";
            DisableAutomaticLayout(rightSection);
            for (var index = rightSection.childCount - 1; index >= 0; --index)
            {
                var child = rightSection.GetChild(index);
                if (child.name != "Background")
                    Object.Destroy(child.gameObject);
            }

            var settingRow = section.Find("Fullscreen Toggle");
            if (settingRow == null)
                return;
            settingRow.SetParent(rightSection, false);
            settingRow.name = "Skip Intro Screens";
            RemoveLocalisers(settingRow);
            SetStretchRow((RectTransform)settingRow, -12f, 45f, 12f);

            _skipIntroToggle = settingRow.GetComponentInChildren<Toggle>(true);
            _skipIntroLabel = settingRow.GetComponentInChildren<TMP_Text>(true);
            if (_skipIntroToggle == null || _skipIntroLabel == null)
                return;

            _skipIntroToggle.onValueChanged.RemoveAllListeners();
            _skipIntroToggle.isOn = UnderPressurePlugin.ShouldSkipIntroScreens;
            _skipIntroToggle.onValueChanged.AddListener(SetSkipIntroScreens);
            var tutorialRow = Instantiate(settingRow, rightSection);
            tutorialRow.name = "Disable Tutorial";
            RemoveLocalisers(tutorialRow);
            SetStretchRow((RectTransform)tutorialRow, -57f, 45f, 12f);
            _disableTutorialToggle = tutorialRow.GetComponentInChildren<Toggle>(true);
            _disableTutorialLabel = tutorialRow.GetComponentInChildren<TMP_Text>(true);
            if (_disableTutorialToggle != null)
            {
                _disableTutorialToggle.onValueChanged.RemoveAllListeners();
                _disableTutorialToggle.isOn = UnderPressurePlugin.ShouldDisableTutorial;
                _disableTutorialToggle.onValueChanged.AddListener(SetDisableTutorial);
            }
            var separateReputationPrestigeRow = Instantiate(settingRow, rightSection);
            separateReputationPrestigeRow.name = "Separate Reputation And Prestige";
            RemoveLocalisers(separateReputationPrestigeRow);
            SetStretchRow((RectTransform)separateReputationPrestigeRow, -102f, 45f, 12f);
            _separateReputationPrestigeToggle = separateReputationPrestigeRow.GetComponentInChildren<Toggle>(true);
            _separateReputationPrestigeLabel = separateReputationPrestigeRow.GetComponentInChildren<TMP_Text>(true);
            if (_separateReputationPrestigeToggle != null)
            {
                _separateReputationPrestigeToggle.onValueChanged.RemoveAllListeners();
                _separateReputationPrestigeToggle.isOn = UnderPressurePlugin.ShouldSeparateReputationAndPrestige;
                _separateReputationPrestigeToggle.onValueChanged.AddListener(SetSeparateReputationAndPrestige);
            }
            var autoLoadLastSaveRow = Instantiate(settingRow, rightSection);
            autoLoadLastSaveRow.name = "Auto Load Last Level";
            RemoveLocalisers(autoLoadLastSaveRow);
            SetStretchRow((RectTransform)autoLoadLastSaveRow, -147f, 45f, 12f);
            _autoLoadLastSaveToggle = autoLoadLastSaveRow.GetComponentInChildren<Toggle>(true);
            _autoLoadLastSaveLabel = autoLoadLastSaveRow.GetComponentInChildren<TMP_Text>(true);
            if (_autoLoadLastSaveToggle != null)
            {
                _autoLoadLastSaveToggle.onValueChanged.RemoveAllListeners();
                _autoLoadLastSaveToggle.isOn = UnderPressurePlugin.ShouldAutoLoadLastSave;
                _autoLoadLastSaveToggle.onValueChanged.AddListener(SetAutoLoadLastSave);
            }
            var effectivenessRow = Instantiate(settingRow, section);
            effectivenessRow.name = "Show Room Effectiveness";
            RemoveLocalisers(effectivenessRow);
            SetStretchRow((RectTransform)effectivenessRow, -12f, 45f, 12f);
            _roomEffectivenessToggle = effectivenessRow.GetComponentInChildren<Toggle>(true);
            _roomEffectivenessLabel = effectivenessRow.GetComponentInChildren<TMP_Text>(true);
            if (_roomEffectivenessToggle != null)
            {
                _roomEffectivenessToggle.onValueChanged.RemoveAllListeners();
                _roomEffectivenessToggle.isOn = UnderPressurePlugin.ShouldShowRoomEffectiveness;
                _roomEffectivenessToggle.onValueChanged.AddListener(SetShowRoomEffectiveness);
            }
            var imperfectRow = Instantiate(settingRow, section);
            imperfectRow.name = "Imperfect Staff";
            RemoveLocalisers(imperfectRow);
            SetStretchRow((RectTransform)imperfectRow, -57f, 45f, 12f);
            _imperfectStaffToggle = imperfectRow.GetComponentInChildren<Toggle>(true);
            _imperfectStaffLabel = imperfectRow.GetComponentInChildren<TMP_Text>(true);
            if (_imperfectStaffToggle != null)
            {
                _imperfectStaffToggle.onValueChanged.RemoveAllListeners();
                _imperfectStaffToggle.isOn = UnderPressurePlugin.ShouldUseImperfectStaff;
                _imperfectStaffToggle.onValueChanged.AddListener(SetImperfectStaff);
            }
            var electricityRow = Instantiate(settingRow, section);
            electricityRow.name = "Show Electricity";
            RemoveLocalisers(electricityRow);
            SetStretchRow((RectTransform)electricityRow, -102f, 45f, 12f);
            _electricityToggle = electricityRow.GetComponentInChildren<Toggle>(true);
            _electricityLabel = electricityRow.GetComponentInChildren<TMP_Text>(true);
            if (_electricityToggle != null)
            {
                _electricityToggle.onValueChanged.RemoveAllListeners();
                _electricityToggle.isOn = UnderPressurePlugin.ShouldShowElectricity;
                _electricityToggle.onValueChanged.AddListener(SetShowElectricity);
            }
            var permanentWearRow = Instantiate(settingRow, section);
            permanentWearRow.name = "Permanent Machine Wear";
            RemoveLocalisers(permanentWearRow);
            SetStretchRow((RectTransform)permanentWearRow, -147f, 45f, 12f);
            _permanentMachineWearToggle = permanentWearRow.GetComponentInChildren<Toggle>(true);
            _permanentMachineWearLabel = permanentWearRow.GetComponentInChildren<TMP_Text>(true);
            if (_permanentMachineWearToggle != null)
            {
                _permanentMachineWearToggle.onValueChanged.RemoveAllListeners();
                _permanentMachineWearToggle.isOn = UnderPressurePlugin.ShouldUsePermanentMachineWear;
                _permanentMachineWearToggle.onValueChanged.AddListener(SetPermanentMachineWear);
            }
            var visualLightingRow = Instantiate(settingRow, rightSection);
            visualLightingRow.name = "Hospital Lighting";
            RemoveLocalisers(visualLightingRow);
            SetStretchRow((RectTransform)visualLightingRow, -192f, 45f, 12f);
            _visualLightingToggle = visualLightingRow.GetComponentInChildren<Toggle>(true);
            _visualLightingLabel = visualLightingRow.GetComponentInChildren<TMP_Text>(true);
            if (_visualLightingToggle != null)
            {
                _visualLightingToggle.onValueChanged.RemoveAllListeners();
                _visualLightingToggle.isOn = UnderPressurePlugin.ShouldUseVisualLighting;
                _visualLightingToggle.onValueChanged.AddListener(SetVisualLighting);
            }
            var unlockLampsRow = Instantiate(settingRow, section);
            unlockLampsRow.name = "Unlock Lamps";
            RemoveLocalisers(unlockLampsRow);
            SetStretchRow((RectTransform)unlockLampsRow, -192f, 45f, 12f);
            _unlockLampsToggle = unlockLampsRow.GetComponentInChildren<Toggle>(true);
            _unlockLampsLabel = unlockLampsRow.GetComponentInChildren<TMP_Text>(true);
            if (_unlockLampsToggle != null)
            {
                _unlockLampsToggle.onValueChanged.RemoveAllListeners();
                _unlockLampsToggle.isOn = UnderPressurePlugin.ShouldUnlockLamps;
                _unlockLampsToggle.onValueChanged.AddListener(SetUnlockLamps);
            }
            var disableDiagnosisChargesRow = Instantiate(settingRow, section);
            disableDiagnosisChargesRow.name = "Disable Diagnosis Charges";
            RemoveLocalisers(disableDiagnosisChargesRow);
            SetStretchRow((RectTransform)disableDiagnosisChargesRow, -237f, 45f, 12f);
            _disableDiagnosisChargesToggle = disableDiagnosisChargesRow.GetComponentInChildren<Toggle>(true);
            _disableDiagnosisChargesLabel = disableDiagnosisChargesRow.GetComponentInChildren<TMP_Text>(true);
            if (_disableDiagnosisChargesToggle != null)
            {
                _disableDiagnosisChargesToggle.onValueChanged.RemoveAllListeners();
                _disableDiagnosisChargesToggle.isOn = UnderPressurePlugin.ShouldDisableDiagnosisCharges;
                _disableDiagnosisChargesToggle.onValueChanged.AddListener(SetDisableDiagnosisCharges);
            }
            var disableAwardsRow = Instantiate(settingRow, rightSection);
            disableAwardsRow.name = "Disable Awards";
            RemoveLocalisers(disableAwardsRow);
            SetStretchRow((RectTransform)disableAwardsRow, -237f, 45f, 12f);
            _disableAwardsToggle = disableAwardsRow.GetComponentInChildren<Toggle>(true);
            _disableAwardsLabel = disableAwardsRow.GetComponentInChildren<TMP_Text>(true);
            if (_disableAwardsToggle != null)
            {
                _disableAwardsToggle.onValueChanged.RemoveAllListeners();
                _disableAwardsToggle.isOn = UnderPressurePlugin.ShouldDisableAwards;
                _disableAwardsToggle.onValueChanged.AddListener(SetDisableAwards);
            }
            ConfigureRowTooltip(settingRow, _skipIntroLabel, "tooltip.skip_intro");
            ConfigureRowTooltip(tutorialRow, _disableTutorialLabel, "tooltip.disable_tutorial");
            ConfigureRowTooltip(separateReputationPrestigeRow, _separateReputationPrestigeLabel,
                "tooltip.separate_reputation_prestige");
            ConfigureRowTooltip(autoLoadLastSaveRow, _autoLoadLastSaveLabel,
                "tooltip.auto_load_last_save");
            ConfigureRowTooltip(effectivenessRow, _roomEffectivenessLabel,
                "tooltip.show_room_effectiveness");
            ConfigureRowTooltip(imperfectRow, _imperfectStaffLabel, "tooltip.imperfect_staff");
            ConfigureRowTooltip(electricityRow, _electricityLabel, "tooltip.show_electricity");
            ConfigureRowTooltip(permanentWearRow, _permanentMachineWearLabel,
                "tooltip.permanent_machine_wear");
            ConfigureRowTooltip(visualLightingRow, _visualLightingLabel,
                "tooltip.visual_lighting");
            ConfigureRowTooltip(unlockLampsRow, _unlockLampsLabel,
                "tooltip.unlock_lamps");
            ConfigureRowTooltip(disableDiagnosisChargesRow, _disableDiagnosisChargesLabel,
                "tooltip.disable_diagnosis_charges");
            ConfigureRowTooltip(disableAwardsRow, _disableAwardsLabel,
                "tooltip.disable_awards");
            var uiRect = (RectTransform)section;
            SetHalfSectionRect(uiRect, -8f, 301f, true);
            SetHalfSectionRect((RectTransform)rightSection, -8f, 301f, false);
            StretchBackground(section);
            StretchBackground(rightSection);
            BuildGameplaySection(content, nativeVideoRow, section);
            RefreshLocalizedText();
        }

        private void BuildGameplaySection(Transform content, Transform nativeVideoRow, Transform uiSection)
        {
            if (nativeVideoRow == null)
                return;

            var global = CreateEmptySection(uiSection, content, "Global Difficulty", -325f, 82f);
            CreateSliderRow(global, nativeVideoRow, "mod.global_difficulty",
                UnderPressurePlugin.GlobalDifficultySetting, -10f, ApplyGlobalDifficulty);

            var economy = CreateDifficultyGroup(uiSection, content, nativeVideoRow,
                "mod.section.economy", UnderPressurePlugin.AdaptiveEconomySetting, -423f,
                new[]
                {
                    new SliderSpec("mod.staff_salaries", UnderPressurePlugin.StaffSalariesSetting),
                    new SliderSpec("mod.patient_income", UnderPressurePlugin.PatientIncomeSetting),
                    new SliderSpec("mod.applicant_wait", UnderPressurePlugin.ApplicantWaitSetting),
                    new SliderSpec("mod.electricity_bill", UnderPressurePlugin.ElectricityBillSetting)
                });
            var reputation = CreateDifficultyGroup(uiSection, content, nativeVideoRow,
                "mod.section.reputation", UnderPressurePlugin.AdaptiveReputationSetting, -723f,
                new[]
                {
                    new SliderSpec("mod.hunger_thirst", UnderPressurePlugin.HungerThirstSetting),
                    new SliderSpec("mod.happiness", UnderPressurePlugin.HappinessSetting),
                    new SliderSpec("mod.hygiene", UnderPressurePlugin.HygieneSetting),
                    new SliderSpec("mod.health_decay", UnderPressurePlugin.HealthDecaySetting)
                });
            var expansion = CreateDifficultyGroup(uiSection, content, nativeVideoRow,
                "mod.section.expansion", UnderPressurePlugin.AdaptiveExpansionSetting, -1023f,
                new[]
                {
                    new SliderSpec("mod.diagnosis_chance", UnderPressurePlugin.DiagnosisChanceSetting),
                    new SliderSpec("mod.treatment_chance", UnderPressurePlugin.TreatmentChanceSetting),
                    new SliderSpec("mod.patient_arrival", UnderPressurePlugin.PatientArrivalSetting),
                    new SliderSpec("mod.machine_wear", UnderPressurePlugin.MachineWearSetting)
                });

            var contentRect = content as RectTransform;
            if (contentRect != null)
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, 1315f);
        }

        private Transform CreateEmptySection(Transform template, Transform content, string name,
            float y, float height)
        {
            var section = Instantiate(template, content);
            section.name = name;
            DisableAutomaticLayout(section);
            for (var index = section.childCount - 1; index >= 0; --index)
                if (section.GetChild(index).name != "Background")
                    Object.Destroy(section.GetChild(index).gameObject);
            SetSectionRect((RectTransform)section, y, height);
            StretchBackground(section);
            return section;
        }

        private AdaptiveGroup CreateDifficultyGroup(Transform template, Transform content,
            Transform nativeVideoRow, string titleKey, ConfigEntry<bool> setting, float y,
            SliderSpec[] specs)
        {
            var section = CreateEmptySection(template, content, titleKey, y, 284f);
            var title = Instantiate(_skipIntroLabel, section);
            title.name = titleKey + " Title";
            RemoveLocalisers(title.transform);
            RemoveTooltips(title.transform);
            SetRect(title.rectTransform, new Vector2(24f, -6f), new Vector2(590f, 46f));
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.enableAutoSizing = false;
            title.fontSize = 29f;

            var adaptiveLabel = Instantiate(_skipIntroLabel, section);
            adaptiveLabel.name = titleKey + " Adaptive Label";
            RemoveLocalisers(adaptiveLabel.transform);
            RemoveTooltips(adaptiveLabel.transform);
            SetRect(adaptiveLabel.rectTransform, new Vector2(812f, -7f), new Vector2(180f, 44f));
            adaptiveLabel.alignment = TextAlignmentOptions.MidlineRight;
            AddTooltip(adaptiveLabel.gameObject, titleKey.Replace("mod.section.", "tooltip.adaptive."));

            var toggle = Instantiate(_skipIntroToggle, section);
            toggle.name = titleKey + " Adaptive Toggle";
            RemoveLocalisers(toggle.transform);
            RemoveTooltips(toggle.transform);
            SetRect((RectTransform)toggle.transform, new Vector2(1000f, -7f), new Vector2(42f, 42f));
            toggle.onValueChanged.RemoveAllListeners();

            var group = new AdaptiveGroup(titleKey, title, adaptiveLabel, toggle, setting);
            for (var index = 0; index < specs.Length; ++index)
                group.Sliders.Add(CreateSliderRow(section, nativeVideoRow, specs[index].Key,
                    specs[index].Setting, -48f - index * 53f));
            toggle.isOn = setting.Value;
            toggle.onValueChanged.AddListener(value =>
            {
                setting.Value = value;
                AdaptiveDifficulty.RefreshAll();
                SetAdaptiveState(group, value);
            });
            _adaptiveGroups.Add(group);
            SetAdaptiveState(group, setting.Value);
            return group;
        }

        private static void SetAdaptiveState(AdaptiveGroup group, bool adaptive)
        {
            foreach (var binding in group.Sliders)
            {
                if (binding == null) continue;
                binding.Slider.interactable = !adaptive;
                if (binding.RowCanvas != null) binding.RowCanvas.alpha = adaptive ? 0.48f : 1f;
                SetSliderDisplay(binding, adaptive
                    ? AdaptiveDifficulty.GetValue(binding.Setting)
                    : binding.Setting.Value);
            }
        }

        private static void SetSliderDisplay(SliderBinding binding, int value)
        {
            if (binding.Slider.value != value)
                binding.Slider.SetValueWithoutNotify(value);
            if (binding.ValueLabel != null)
                binding.ValueLabel.text = FormatPercentage(value);
            SetNativeFillColour(binding.Slider.fillRect != null
                ? binding.Slider.fillRect.GetComponent<Image>() : null, value);
        }

        private SliderBinding CreateSliderRow(Transform section, Transform nativeVideoRow, string localizationKey,
            ConfigEntry<int> setting, float y, Action<int> onChanged = null)
        {
            var row = Instantiate(nativeVideoRow, section);
            row.name = localizationKey + " Native Row";
            SetRect((RectTransform)row, new Vector2(12f, y), new Vector2(1060f, 55f));
            RemoveLocalisers(row);
            RemoveTooltips(row);
            var label = row.Find("MaximumFPSLabel")?.GetComponent<TMP_Text>();
            var valueLabel = row.Find("MaximumFPSValueLabel")?.GetComponent<TMP_Text>();
            var slider = row.GetComponentInChildren<Slider>(true);
            if (label == null || valueLabel == null || slider == null) return null;
            label.name = localizationKey + " Label";
            label.alignment = TextAlignmentOptions.MidlineLeft;
            AddTooltip(label.gameObject, localizationKey.Replace("mod.", "tooltip."));
            valueLabel.name = localizationKey + " Value";
            valueLabel.alignment = TextAlignmentOptions.Center;

            slider.name = localizationKey + " Slider";
            var sliderRect = slider.transform as RectTransform;
            if (sliderRect != null)
                sliderRect.anchoredPosition += new Vector2(-5f, 0f);
            slider.onValueChanged.RemoveAllListeners();
            slider.minValue = -100f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.value = setting.Value;
            var fillImage = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            SetNativeFillColour(fillImage, setting.Value);
            slider.onValueChanged.AddListener(value =>
            {
                setting.Value = Mathf.RoundToInt(value);
                valueLabel.text = FormatPercentage(setting.Value);
                SetNativeFillColour(fillImage, setting.Value);
                onChanged?.Invoke(setting.Value);
            });
            var canvas = row.GetComponent<CanvasGroup>() ?? row.gameObject.AddComponent<CanvasGroup>();
            var binding = new SliderBinding(localizationKey, label, valueLabel, setting, slider, canvas);
            _sliders.Add(binding);
            return binding;
        }

        private void ApplyGlobalDifficulty(int value)
        {
            if (_applyingGlobalDifficulty) return;
            _applyingGlobalDifficulty = true;
            foreach (var binding in _sliders)
            {
                if (binding.Setting == UnderPressurePlugin.GlobalDifficultySetting) continue;
                binding.Slider.value = value;
            }
            _applyingGlobalDifficulty = false;
        }

        private static void SetNativeFillColour(Image image, int value)
        {
            if (image == null || image.sprite == null) return;
            if (_nativeFillSprite == null) _nativeFillSprite = image.sprite;
            if (_nativeFillPixels == null)
            {
                var source = _nativeFillSprite.texture;
                var temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                    readable.Apply();
                    _nativeFillPixels = readable.GetPixels32();
                    Object.Destroy(readable);
                }
                catch (Exception exception)
                {
                    UnderPressurePlugin.Log.LogWarning("No se pudo recolorear el sprite nativo: " + exception.Message);
                    return;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }

            if (!TintedFillSprites.TryGetValue(value, out var tinted))
            {
                var pixels = (Color32[])_nativeFillPixels.Clone();
                var position = Mathf.InverseLerp(-100f, 100f, value);
                var goal = position < 0.5f
                    ? Color.Lerp(new Color(0.35f, 1f, 0.53f), new Color(1f, 0.90f, 0.27f), position * 2f)
                    : Color.Lerp(new Color(1f, 0.90f, 0.27f), new Color(1f, 0.40f, 0.34f),
                        (position - 0.5f) * 2f);
                Color.RGBToHSV(goal, out var hue, out var saturation, out _);
                for (var index = 0; index < pixels.Length; ++index)
                {
                    var pixel = pixels[index];
                    if (pixel.a == 0 || pixel.b - pixel.r < 35 || pixel.g - pixel.r < 25) continue;
                    Color.RGBToHSV(pixel, out _, out var oldSaturation, out var brightness);
                    var recoloured = Color.HSVToRGB(hue,
                        Mathf.Clamp01(saturation * oldSaturation / 0.42f), brightness);
                    pixels[index] = new Color32((byte)(recoloured.r * 255f),
                        (byte)(recoloured.g * 255f), (byte)(recoloured.b * 255f), pixel.a);
                }
                var texture = new Texture2D(_nativeFillSprite.texture.width,
                    _nativeFillSprite.texture.height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                tinted = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(_nativeFillSprite.pivot.x / _nativeFillSprite.rect.width,
                        _nativeFillSprite.pivot.y / _nativeFillSprite.rect.height),
                    _nativeFillSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                    _nativeFillSprite.border);
                TintedFillSprites.Add(value, tinted);
            }
            image.sprite = tinted;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private void CaptureNativeSliderStyle(Slider slider, Transform content)
        {
            if (slider == null) return;
            var cursor = slider.transform.parent;
            while (cursor != null && cursor != content)
            {
                var texts = cursor.GetComponentsInChildren<TMP_Text>(true);
                if (texts.Length >= 2)
                {
                    Array.Sort(texts, (left, right) => left.rectTransform.position.x.CompareTo(
                        right.rectTransform.position.x));
                    _nativeSliderLabel = texts[0];
                    _nativeSliderValue = texts[1];
                    return;
                }
                cursor = cursor.parent;
            }
        }

        private static void SetSectionRect(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(-16f, height);
            rect.localScale = Vector3.one;
        }

        private static void SetHalfSectionRect(RectTransform rect, float y, float height, bool left)
        {
            rect.anchorMin = new Vector2(left ? 0f : 0.5f, 1f);
            rect.anchorMax = new Vector2(left ? 0.5f : 1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(left ? 2f : -2f, y);
            rect.sizeDelta = new Vector2(-12f, height);
            rect.localScale = Vector3.one;
        }

        private static void SetStretchRow(RectTransform rect, float y, float height, float horizontalMargin)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(-horizontalMargin * 2f, height);
            rect.localScale = Vector3.one;
        }

        private static void DisableAutomaticLayout(Transform root)
        {
            foreach (var layout in root.GetComponents<LayoutGroup>())
                layout.enabled = false;
            foreach (var fitter in root.GetComponents<ContentSizeFitter>())
                fitter.enabled = false;
            foreach (var behaviour in root.GetComponents<Behaviour>())
            {
                var name = behaviour.GetType().Name;
                if (name.IndexOf("Layout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0)
                    behaviour.enabled = false;
            }
        }

        private static void StretchBackground(Transform section)
        {
            var background = section.Find("Background") as RectTransform;
            if (background == null) return;
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = Vector2.zero;
            background.offsetMax = Vector2.zero;
        }

        private static Transform FindSectionRow(Transform child, Transform content)
        {
            if (child == null) return null;
            var section = child;
            while (section.parent != null && section.parent != content) section = section.parent;
            var row = child;
            while (row.parent != null && row.parent != section) row = row.parent;
            return row == section ? null : row;
        }

        private static string FormatPercentage(int value) => value > 0 ? "+" + value + "%" : value + "%";

        private static void SetSkipIntroScreens(bool value)
        {
            UnderPressurePlugin.SkipIntroScreensSetting.Value = value;
        }

        private static void SetDisableTutorial(bool value)
        {
            UnderPressurePlugin.DisableTutorialSetting.Value = value;
        }

        private static void SetAutoLoadLastSave(bool value)
        {
            UnderPressurePlugin.AutoLoadLastSaveSetting.Value = value;
        }

        private static void SetShowRoomEffectiveness(bool value)
        {
            UnderPressurePlugin.ShowRoomEffectivenessSetting.Value = value;
            RoomPowerDisplay.NotifySettingChanged();
        }

        private static void SetImperfectStaff(bool value)
        {
            UnderPressurePlugin.ImperfectStaffSetting.Value = value;
        }

        private static void SetShowElectricity(bool value)
        {
            UnderPressurePlugin.ShowElectricitySetting.Value = value;
            ElectricityFeatures.NotifySettingChanged();
        }

        private static void SetSeparateReputationAndPrestige(bool value)
        {
            UnderPressurePlugin.SeparateReputationPrestigeSetting.Value = value;
        }

        private static void SetPermanentMachineWear(bool value)
        {
            UnderPressurePlugin.PermanentMachineWearSetting.Value = value;
            PermanentMachineWearSystem.NotifySettingChanged();
        }

        private static void SetVisualLighting(bool value)
        {
            UnderPressurePlugin.VisualLightingSetting.Value = value;
            HospitalLightingPrototype.NotifySettingChanged();
        }

        private static void SetUnlockLamps(bool value)
        {
            UnderPressurePlugin.UnlockLampsSetting.Value = value;
        }

        private static void SetDisableDiagnosisCharges(bool value)
        {
            UnderPressurePlugin.DisableDiagnosisChargesSetting.Value = value;
        }

        private static void SetDisableAwards(bool value)
        {
            UnderPressurePlugin.DisableAwardsSetting.Value = value;
        }

        private static void SetMasterEnabled(bool value)
        {
            UnderPressurePlugin.EnabledSetting.Value = value;
            RoomPowerDisplay.NotifySettingChanged();
            ElectricityFeatures.NotifySettingChanged();
            HospitalLightingPrototype.NotifySettingChanged();
        }

        private void ShowModTab()
        {
            if (_contents == null)
                return;

            var enumType = SetTabActiveMethod.GetParameters()[0].ParameterType;
            SetTabActiveMethod.Invoke(_screen, new[] { Enum.ToObject(enumType, 5) });
            _showingModTab = true;
            _contents.gameObject.SetActive(true);
            if (_tabAnimator != null)
                _tabAnimator.CurrentState = (ButtonAnimator.State)1;
            _tab.SetAsLastSibling();
            RefreshLocalizedText();
        }

        internal void DeactivateModTab()
        {
            if (!_showingModTab)
                return;

            _showingModTab = false;
            if (_contents != null)
                _contents.gameObject.SetActive(false);
            if (_tabAnimator != null)
                _tabAnimator.CurrentState = (ButtonAnimator.State)0;
        }

        internal void RefreshLocalizedText()
        {
            if (_masterToggle != null && _masterToggle.isOn != UnderPressurePlugin.IsModEnabled)
                _masterToggle.isOn = UnderPressurePlugin.IsModEnabled;
            if (_skipIntroLabel != null)
                _skipIntroLabel.text = ModLocalization.Get("mod.skip_intro");
            if (_autoLoadLastSaveLabel != null)
                _autoLoadLastSaveLabel.text = ModLocalization.Get("mod.auto_load_last_save");
            if (_disableTutorialLabel != null)
                _disableTutorialLabel.text = ModLocalization.Get("mod.disable_tutorial");
            if (_roomEffectivenessLabel != null)
                _roomEffectivenessLabel.text = ModLocalization.Get("mod.show_room_effectiveness");
            if (_imperfectStaffLabel != null)
                _imperfectStaffLabel.text = ModLocalization.Get("mod.imperfect_staff");
            if (_electricityLabel != null)
                _electricityLabel.text = ModLocalization.Get("mod.show_electricity");
            if (_separateReputationPrestigeLabel != null)
                _separateReputationPrestigeLabel.text = ModLocalization.Get("mod.separate_reputation_prestige");
            if (_permanentMachineWearLabel != null)
                _permanentMachineWearLabel.text = ModLocalization.Get("mod.permanent_machine_wear");
            if (_visualLightingLabel != null)
                _visualLightingLabel.text = ModLocalization.Get("mod.visual_lighting");
            if (_unlockLampsLabel != null)
                _unlockLampsLabel.text = ModLocalization.Get("mod.unlock_lamps");
            if (_disableDiagnosisChargesLabel != null)
                _disableDiagnosisChargesLabel.text = ModLocalization.Get("mod.disable_diagnosis_charges");
            if (_disableAwardsLabel != null)
                _disableAwardsLabel.text = ModLocalization.Get("mod.disable_awards");
            foreach (var binding in _sliders)
            {
                binding.Label.text = ModLocalization.Get(binding.Key);
                if (binding.ValueLabel != null) binding.ValueLabel.text = FormatPercentage(binding.Setting.Value);
            }
            foreach (var group in _adaptiveGroups)
            {
                group.Title.text = ModLocalization.Get(group.TitleKey);
                group.AdaptiveLabel.text = ModLocalization.Get("mod.adaptive");
                if (group.Toggle.isOn != group.Setting.Value) group.Toggle.isOn = group.Setting.Value;
                SetAdaptiveState(group, group.Setting.Value);
            }
            SetTabCaption();
        }

        private void LateUpdate()
        {
            SetTabCaption();
            foreach (var group in _adaptiveGroups)
                if (group.Setting.Value)
                    SetAdaptiveState(group, true);
        }

        private void SetTabCaption()
        {
            var caption = ModLocalization.Get("mod.tab");
            if (_tabButton == null || _tabLabel == null || _tabLabel.text == caption)
                return;

            _tabButton.SetTMPText(caption);
            _tabLabel.enableAutoSizing = true;
            _tabLabel.fontSizeMin = 13f;
        }

        private static T GetField<T>(PreferencesScreen screen, string name) where T : class
        {
            return AccessTools.Field(ScreenType, name)?.GetValue(screen) as T;
        }

        private static void ReduceNativeTitleFont(PreferencesScreen screen)
        {
            TMP_Text title = null;
            foreach (var text in screen.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || text.GetComponentInParent<Button>() != null) continue;
                if (title == null || text.fontSize > title.fontSize)
                    title = text;
            }
            if (title == null) return;

            title.fontSize *= 0.9f;
            if (title.enableAutoSizing)
                title.fontSizeMax *= 0.9f;
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; --i)
                Object.Destroy(parent.GetChild(i).gameObject);
        }

        private static void RemoveLocalisers(Transform root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                var typeName = component.GetType().FullName;
                if (typeName != null && typeName.StartsWith("I2.Loc.", StringComparison.Ordinal))
                    Object.Destroy(component);
            }
        }

        private static void RemoveTooltips(Transform root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                var name = component.GetType().Name;
                if (name.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ToolTip", StringComparison.OrdinalIgnoreCase) >= 0)
                    Object.Destroy(component);
            }
        }

        private void CaptureNativeTooltipStyle()
        {
            foreach (var tooltip in _screen.GetComponentsInChildren<TooltipSpawner>(true))
            {
                if (tooltip == null || tooltip.Prefab == null) continue;
                _tooltipPrefab = tooltip.Prefab;
                _tooltipHoverTime = tooltip.HoverTime;
                return;
            }
        }

        private void ConfigureRowTooltip(Transform row, TMP_Text label, string key)
        {
            if (row == null || label == null) return;
            RemoveTooltips(row);
            AddTooltip(label.gameObject, key);
        }

        private void AddTooltip(GameObject target, string key)
        {
            if (target == null || _tooltipPrefab == null || TooltipColliderField == null) return;

            RemoveTooltips(target.transform);
            var rect = target.transform as RectTransform;
            if (rect == null) return;

            var collider = target.GetComponent<BoxCollider>() ?? target.AddComponent<BoxCollider>();
            var width = Mathf.Max(1f, rect.rect.width);
            var height = Mathf.Max(1f, rect.rect.height);
            collider.size = new Vector3(width, height, 1f);
            collider.center = new Vector3((0.5f - rect.pivot.x) * width,
                (0.5f - rect.pivot.y) * height, 0f);

            var tooltip = target.AddComponent<TooltipSpawner>();
            TooltipColliderField.SetValue(tooltip, collider);
            tooltip.Prefab = _tooltipPrefab;
            tooltip.HoverTime = _tooltipHoverTime;
            tooltip.AnchorToMouse = true;
            tooltip.SetDataProvider(value => value.Text = ModLocalization.Get(key));
        }

        private sealed class SliderBinding
        {
            internal readonly string Key;
            internal readonly TMP_Text Label;
            internal readonly TMP_Text ValueLabel;
            internal readonly ConfigEntry<int> Setting;
            internal readonly Slider Slider;
            internal readonly CanvasGroup RowCanvas;
            internal SliderBinding(string key, TMP_Text label, TMP_Text valueLabel, ConfigEntry<int> setting,
                Slider slider, CanvasGroup rowCanvas)
            {
                Key = key; Label = label; ValueLabel = valueLabel; Setting = setting;
                Slider = slider; RowCanvas = rowCanvas;
            }
        }

        private sealed class SliderSpec
        {
            internal readonly string Key;
            internal readonly ConfigEntry<int> Setting;
            internal SliderSpec(string key, ConfigEntry<int> setting) { Key = key; Setting = setting; }
        }

        private sealed class AdaptiveGroup
        {
            internal readonly string TitleKey;
            internal readonly TMP_Text Title;
            internal readonly TMP_Text AdaptiveLabel;
            internal readonly Toggle Toggle;
            internal readonly ConfigEntry<bool> Setting;
            internal readonly List<SliderBinding> Sliders = new List<SliderBinding>();
            internal AdaptiveGroup(string titleKey, TMP_Text title, TMP_Text adaptiveLabel,
                Toggle toggle, ConfigEntry<bool> setting)
            {
                TitleKey = titleKey; Title = title; AdaptiveLabel = adaptiveLabel;
                Toggle = toggle; Setting = setting;
            }
        }

    }
}
