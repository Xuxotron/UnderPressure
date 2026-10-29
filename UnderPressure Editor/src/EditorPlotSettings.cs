using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FullInspector;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private RectTransform _settingsPlotRoot;
        private RectTransform _settingsPlotList;
        private RectTransform _settingsPlotDetails;
        private ScrollRect _settingsPlotScroll;
        private int _settingsPlotSelectedIndex;
        private readonly List<int> _settingsPlotRequirements = new List<int>();

        private void BuildPlotSettingsContents()
        {
            if (_settingsPages == null || _settingsPages.Length <= 3 ||
                _settingsPages[3] == null) return;
            var content = _settingsPages[3].transform.Find("Scroll View/Viewport/Content")
                as RectTransform;
            if (content == null) return;

            _settingsPlotRoot = NewImage("UP Plot Settings", content, null,
                Color.clear, false).transform as RectTransform;
            Stretch(_settingsPlotRoot);

            var listPanel = NewImage("Lista de parcelas", _settingsPlotRoot,
                _style.PanelSprite, _style.PanelColor, false);
            var listRect = listPanel.transform as RectTransform;
            SetRect(listRect, new Vector2(.01f, .02f), new Vector2(.24f, .98f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);

            var listTitle = CreateDiseaseText(listPanel.transform,
                EditorLocalization.Get("editor.scenario_settings.plots.list"), 22f,
                TextAlignmentOptions.Center, Color.white);
            SetRect(listTitle.rectTransform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(.5f, 1f), new Vector2(0f, -8f), new Vector2(0f, 48f));

            var viewportObject = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(listPanel.transform, false);
            var viewport = viewportObject.transform as RectTransform;
            SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(.5f, .5f),
                new Vector2(8f, 8f), new Vector2(-16f, -62f));
            viewportObject.GetComponent<Image>().color = Color.clear;

            var listObject = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listObject.transform.SetParent(viewport, false);
            _settingsPlotList = listObject.transform as RectTransform;
            _settingsPlotList.anchorMin = new Vector2(0f, 1f);
            _settingsPlotList.anchorMax = Vector2.one;
            _settingsPlotList.pivot = new Vector2(.5f, 1f);
            _settingsPlotList.anchoredPosition = Vector2.zero;
            _settingsPlotList.sizeDelta = Vector2.zero;
            var group = listObject.GetComponent<VerticalLayoutGroup>();
            group.spacing = 6f;
            group.childControlHeight = false;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            var fitter = listObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _settingsPlotScroll = listPanel.AddComponent<ScrollRect>();
            _settingsPlotScroll.viewport = viewport;
            _settingsPlotScroll.content = _settingsPlotList;
            _settingsPlotScroll.horizontal = false;
            _settingsPlotScroll.vertical = true;
            _settingsPlotScroll.movementType = ScrollRect.MovementType.Clamped;
            _settingsPlotScroll.scrollSensitivity = 45f;
            InstallDiseaseNativeScrollbar(listRect, _settingsPlotScroll,
                "UP Plot Native Scrollbar", 58f, 8f);

            var detailsPanel = NewImage("Editor de parcela", _settingsPlotRoot,
                _style.PanelSprite, _style.PanelColor, false);
            _settingsPlotDetails = detailsPanel.transform as RectTransform;
            SetRect(_settingsPlotDetails, new Vector2(.25f, .02f),
                new Vector2(.99f, .98f), new Vector2(.5f, .5f),
                Vector2.zero, Vector2.zero);
            RefreshPlotSettingsContents();
        }

        private void RefreshPlotSettingsContents()
        {
            if (_settingsPlotList == null || _settingsPlotDetails == null ||
                _level?.WorldState?.HospitalPlots == null) return;
            var plots = _level.WorldState.HospitalPlots;
            _settingsPlotSelectedIndex = Mathf.Clamp(_settingsPlotSelectedIndex, 0,
                Mathf.Max(0, plots.Count - 1));

            ClearPlotChildren(_settingsPlotList);
            for (var index = 0; index < plots.Count; ++index)
            {
                var captured = index;
                var selected = index == _settingsPlotSelectedIndex;
                var button = CreateDiseaseButton(_settingsPlotList,
                    EditorLocalization.Format("editor.scenario_settings.plots.plot", index + 1),
                    selected ? _style.SelectedColor : _style.ButtonColor,
                    () => SelectPlotSettings(captured));
                var rect = button.transform as RectTransform;
                var layout = button.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 48f;
                layout.minHeight = 48f;
                rect.sizeDelta = new Vector2(0f, 48f);
            }

            ClearPlotChildren(_settingsPlotDetails);
            if (plots.Count == 0) return;
            var definition = plots[_settingsPlotSelectedIndex]?.Definition;
            if (definition == null) return;
            LoadPlotRequirements(definition, plots);

            var title = CreateDiseaseText(_settingsPlotDetails,
                EditorLocalization.Format("editor.scenario_settings.plots.plot",
                    _settingsPlotSelectedIndex + 1), 26f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            SetRect(title.rectTransform, new Vector2(.02f, 1f), new Vector2(.98f, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -8f), new Vector2(0f, 48f));

            var cost = CreateScenarioSettingsInput(_settingsPlotDetails,
                "editor.scenario_settings.plots.cost", 70f, 48f, false, true,
                .02f, .32f, 130f);
            if (cost != null)
            {
                cost.SetTextWithoutNotify(definition.Cost.ToString(CultureInfo.InvariantCulture));
                cost.onEndEdit.AddListener(value =>
                {
                    int parsed;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out parsed)) definition.Cost = parsed;
                    cost.SetTextWithoutNotify(definition.Cost.ToString(CultureInfo.InvariantCulture));
                });
            }

            AddPlotToggle("editor.scenario_settings.plots.initially_open", .36f, 70f,
                definition.InitiallyOpen, value => definition.InitiallyOpen = value);
            AddPlotToggle("editor.scenario_settings.plots.hide_until_available", .68f, 70f,
                definition.HideUntilAvailable, value => definition.HideUntilAvailable = value);

            AddPlotLabel("editor.scenario_settings.plots.initial_state", .02f, 142f, .17f);
            var state = CreateDiseaseDropdown(_settingsPlotDetails,
                new[]
                {
                    EditorLocalization.Get("editor.scenario_settings.plots.unbuilt"),
                    EditorLocalization.Get("editor.scenario_settings.plots.built")
                }, definition.Built ? 1 : 0);
            SetRect(state.transform as RectTransform, new Vector2(.19f, 1f),
                new Vector2(.34f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -140f), new Vector2(0f, 42f));
            state.onValueChanged.AddListener(value => definition.Built = value == 1);

            AddPlotToggle("editor.scenario_settings.plots.instant_build", .36f, 142f,
                definition.InstaBuild, value => definition.InstaBuild = value);

            var buildTime = CreateScenarioSettingsInput(_settingsPlotDetails,
                "editor.scenario_settings.plots.build_time", 140f, 48f, false, false,
                .68f, .98f, 130f);
            if (buildTime != null)
            {
                buildTime.SetTextWithoutNotify(definition.TimeToBuild.ToString("0.##",
                    CultureInfo.InvariantCulture));
                buildTime.onEndEdit.AddListener(value =>
                {
                    float parsed;
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out parsed)) definition.TimeToBuild = Mathf.Max(0f, parsed);
                    buildTime.SetTextWithoutNotify(definition.TimeToBuild.ToString("0.##",
                        CultureInfo.InvariantCulture));
                });
            }

            var plotNames = new List<string>
            {
                EditorLocalization.Get("editor.scenario_settings.plots.none")
            };
            for (var index = 0; index < plots.Count; ++index)
                plotNames.Add(EditorLocalization.Format(
                    "editor.scenario_settings.plots.plot", index + 1));

            AddPlotLabel("editor.scenario_settings.plots.merge_into", .02f, 214f, .17f);
            var mergeIndex = FindPlotDefinitionIndex(definition.MergeInto?.Instance, plots);
            var merge = CreateDiseaseDropdown(_settingsPlotDetails, plotNames, mergeIndex + 1);
            SetRect(merge.transform as RectTransform, new Vector2(.19f, 1f),
                new Vector2(.42f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -212f), new Vector2(0f, 42f));
            merge.onValueChanged.AddListener(value => SetPlotMerge(value - 1));

            AddPlotLabel("editor.scenario_settings.plots.requires", .47f, 214f, .12f);
            var requirementChoices = new List<string>(plotNames);
            var requirement = CreateDiseaseDropdown(_settingsPlotDetails,
                requirementChoices, 0);
            SetRect(requirement.transform as RectTransform, new Vector2(.59f, 1f),
                new Vector2(.78f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -212f), new Vector2(0f, 42f));
            var add = CreateDiseaseButton(_settingsPlotDetails, "+",
                new Color32(0, 190, 75, 255), () =>
                {
                    var selected = requirement.value - 1;
                    if (selected >= 0 && selected != _settingsPlotSelectedIndex &&
                        !_settingsPlotRequirements.Contains(selected))
                    {
                        _settingsPlotRequirements.Add(selected);
                        ApplyPlotRequirements(definition, plots);
                        RefreshPlotSettingsContents();
                    }
                });
            SetRect(add.transform as RectTransform, new Vector2(.79f, 1f),
                new Vector2(.84f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -212f), new Vector2(0f, 42f));
            BuildPlotRequirementChips(definition, plots);
        }

        private void SelectPlotSettings(int index)
        {
            _settingsPlotSelectedIndex = index;
            RefreshPlotSettingsContents();
        }

        private void AddPlotLabel(string key, float x, float top, float width)
        {
            var label = CreateDiseaseText(_settingsPlotDetails,
                EditorLocalization.Get(key), 17f, TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetRect(label.rectTransform, new Vector2(x, 1f),
                new Vector2(x + width, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -top), new Vector2(0f, 42f));
        }

        private void AddPlotToggle(string key, float x, float top, bool value,
            Action<bool> changed)
        {
            AddPlotLabel(key, x, top, .23f);
            var toggle = CreateDiseaseToggle(_settingsPlotDetails, value, changed);
            if (toggle == null) return;
            SetRect(toggle.transform as RectTransform, new Vector2(x + .24f, 1f),
                new Vector2(x + .285f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -top), new Vector2(0f, 42f));
        }

        private void SetPlotMerge(int targetIndex)
        {
            var plots = _level.WorldState.HospitalPlots;
            var definition = plots[_settingsPlotSelectedIndex]?.Definition;
            if (definition == null) return;
            if (targetIndex < 0 || targetIndex >= plots.Count ||
                targetIndex == _settingsPlotSelectedIndex)
            {
                definition.MergeInto = null;
                return;
            }
            definition.MergeInto = CreatePlotWrapper(plots[targetIndex].Definition);
            if (!_settingsPlotRequirements.Contains(targetIndex))
                _settingsPlotRequirements.Add(targetIndex);
            ApplyPlotRequirements(definition, plots);
            HighlightPlotMergeTarget(targetIndex);
            RefreshPlotSettingsContents();
        }

        private void HighlightPlotMergeTarget(int index)
        {
            var plots = _level.WorldState.HospitalPlots;
            if (index < 0 || index >= plots.Count) return;
            _level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plots[index]);
            var nativeMenu = _level?.HUD?.FindMenu<EditHospitalMenu>(true);
            var frame = nativeMenu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                "_editFrame")?.GetValue(nativeMenu) as Graphic;
            if (frame != null) frame.color = new Color32(235, 52, 188, 255);
        }

        private void BuildPlotRequirementChips(HospitalPlotDefinition definition,
            IList<HospitalPlot> plots)
        {
            for (var position = 0; position < _settingsPlotRequirements.Count; ++position)
            {
                var captured = _settingsPlotRequirements[position];
                var button = CreateDiseaseButton(_settingsPlotDetails,
                    (captured + 1).ToString(CultureInfo.InvariantCulture),
                    captured == FindPlotDefinitionIndex(definition.MergeInto?.Instance, plots)
                        ? new Color32(235, 52, 188, 255) : _style.ButtonColor,
                    () =>
                    {
                        _settingsPlotRequirements.Remove(captured);
                        if (FindPlotDefinitionIndex(definition.MergeInto?.Instance, plots) == captured)
                            definition.MergeInto = null;
                        ApplyPlotRequirements(definition, plots);
                        RefreshPlotSettingsContents();
                    });
                var left = .85f + position * .055f;
                SetRect(button.transform as RectTransform, new Vector2(left, 1f),
                    new Vector2(left + .045f, 1f), new Vector2(.5f, 1f),
                    new Vector2(0f, -212f), new Vector2(0f, 42f));
            }
        }

        private void LoadPlotRequirements(HospitalPlotDefinition definition,
            IList<HospitalPlot> plots)
        {
            _settingsPlotRequirements.Clear();
            if (definition.Prerequisites == null) return;
            foreach (var prerequisite in definition.Prerequisites)
            {
                var bought = prerequisite as HospitalPlotPrerequisitePlotBought;
                if (bought == null) continue;
                var values = AccessTools.Field(typeof(HospitalPlotPrerequisitePlotBought),
                    "_plot")?.GetValue(bought) as SharedInstance<HospitalPlotDefinition>[];
                if (values == null) continue;
                foreach (var value in values)
                {
                    var index = FindPlotDefinitionIndex(value?.Instance, plots);
                    if (index >= 0 && !_settingsPlotRequirements.Contains(index))
                        _settingsPlotRequirements.Add(index);
                }
            }
        }

        private void ApplyPlotRequirements(HospitalPlotDefinition definition,
            IList<HospitalPlot> plots)
        {
            if (definition.Prerequisites == null)
                definition.Prerequisites = new List<HospitalPlotPrerequisite>();
            definition.Prerequisites.Clear();
            foreach (var index in _settingsPlotRequirements.Distinct())
            {
                if (index < 0 || index >= plots.Count) continue;
                var prerequisite = new HospitalPlotPrerequisitePlotBought();
                AccessTools.Field(typeof(HospitalPlotPrerequisitePlotBought), "_plot")
                    ?.SetValue(prerequisite, new SharedInstance<HospitalPlotDefinition>[]
                    {
                        CreatePlotWrapper(plots[index].Definition)
                    });
                definition.Prerequisites.Add(prerequisite);
            }
        }

        private static UnderPressurePlotInstance CreatePlotWrapper(
            HospitalPlotDefinition definition)
        {
            var wrapper = ScriptableObject.CreateInstance<UnderPressurePlotInstance>();
            wrapper.Instance = definition;
            return wrapper;
        }

        private static int FindPlotDefinitionIndex(HospitalPlotDefinition definition,
            IList<HospitalPlot> plots)
        {
            if (definition == null || plots == null) return -1;
            for (var index = 0; index < plots.Count; ++index)
                if (ReferenceEquals(plots[index]?.Definition, definition)) return index;
            return -1;
        }

        private static void ClearPlotChildren(Transform root)
        {
            if (root == null) return;
            for (var index = root.childCount - 1; index >= 0; --index)
                Destroy(root.GetChild(index).gameObject);
        }

        private void ReleasePlotSettingsContents()
        {
            _settingsPlotRequirements.Clear();
            _settingsPlotRoot = null;
            _settingsPlotList = null;
            _settingsPlotDetails = null;
            _settingsPlotScroll = null;
            _settingsPlotSelectedIndex = 0;
        }
    }
}
