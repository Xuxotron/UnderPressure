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
        private const float PlotListHeaderHeight = 52f;

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
            SetRect(listRect, new Vector2(.01f, .02f), new Vector2(.235f, .98f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            CreatePlotHeader(listPanel.transform,
                EditorLocalization.Get("editor.scenario_settings.plots.list"),
                TextAlignmentOptions.Center);

            var viewportObject = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(listPanel.transform, false);
            var viewport = viewportObject.transform as RectTransform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10f, 10f);
            viewport.offsetMax = new Vector2(-22f, -PlotListHeaderHeight - 8f);
            viewportObject.GetComponent<Image>().color = Color.clear;

            var listObject = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listObject.transform.SetParent(viewport, false);
            _settingsPlotList = listObject.transform as RectTransform;
            _settingsPlotList.anchorMin = new Vector2(0f, 1f);
            _settingsPlotList.anchorMax = new Vector2(1f, 1f);
            _settingsPlotList.pivot = new Vector2(.5f, 1f);
            _settingsPlotList.anchoredPosition = Vector2.zero;
            _settingsPlotList.sizeDelta = Vector2.zero;
            var group = listObject.GetComponent<VerticalLayoutGroup>();
            group.spacing = 7f;
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
                "UP Plot Native Scrollbar", PlotListHeaderHeight + 6f, 8f);

            var detailsPanel = NewImage("Editor de parcela", _settingsPlotRoot,
                _style.PanelSprite, _style.PanelColor, false);
            _settingsPlotDetails = detailsPanel.transform as RectTransform;
            SetRect(_settingsPlotDetails, new Vector2(.245f, .02f),
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
                layout.preferredHeight = 50f;
                layout.minHeight = 50f;
                rect.sizeDelta = new Vector2(0f, 50f);
            }

            ClearPlotChildren(_settingsPlotDetails);
            if (plots.Count == 0) return;
            var definition = plots[_settingsPlotSelectedIndex]?.Definition;
            if (definition == null) return;
            LoadPlotRequirements(definition, plots);

            CreatePlotHeader(_settingsPlotDetails,
                EditorLocalization.Format("editor.scenario_settings.plots.plot",
                    _settingsPlotSelectedIndex + 1), TextAlignmentOptions.MidlineLeft);

            CreatePlotIntegerField("editor.scenario_settings.plots.cost",
                .035f, .335f, 86f, definition.Cost,
                value => definition.Cost = value);
            AddPlotToggle("editor.scenario_settings.plots.initially_open",
                .365f, .655f, 86f, definition.InitiallyOpen,
                value => definition.InitiallyOpen = value);
            AddPlotToggle("editor.scenario_settings.plots.hide_until_available",
                .685f, .975f, 86f, definition.HideUntilAvailable,
                value => definition.HideUntilAvailable = value);

            AddPlotLabel("editor.scenario_settings.plots.initial_state",
                .035f, .185f, 158f);
            var state = CreateDiseaseDropdown(_settingsPlotDetails,
                new[]
                {
                    EditorLocalization.Get("editor.scenario_settings.plots.unbuilt"),
                    EditorLocalization.Get("editor.scenario_settings.plots.built")
                }, definition.Built ? 1 : 0);
            SetPlotControlRect(state.transform as RectTransform, .185f, .335f, 156f, 44f);
            state.onValueChanged.AddListener(value => definition.Built = value == 1);

            AddPlotToggle("editor.scenario_settings.plots.instant_build",
                .365f, .655f, 158f, definition.InstaBuild,
                value => definition.InstaBuild = value);
            CreatePlotFloatField("editor.scenario_settings.plots.build_time",
                .685f, .975f, 158f, definition.TimeToBuild,
                value => definition.TimeToBuild = value);

            var plotNames = BuildPlotNames(plots);
            AddPlotLabel("editor.scenario_settings.plots.merge_into",
                .035f, .185f, 230f);
            var mergeIndex = FindPlotDefinitionIndex(definition.MergeInto?.Instance, plots);
            var merge = CreateDiseaseDropdown(_settingsPlotDetails, plotNames, mergeIndex + 1);
            SetPlotControlRect(merge.transform as RectTransform, .185f, .335f, 228f, 44f);
            merge.onValueChanged.AddListener(value => SetPlotMerge(value - 1));

            AddPlotLabel("editor.scenario_settings.plots.requires",
                .365f, .475f, 230f);
            var requirement = CreateDiseaseDropdown(_settingsPlotDetails, plotNames, 0);
            SetPlotControlRect(requirement.transform as RectTransform,
                .475f, .655f, 228f, 44f);
            var add = CreateDiseaseButton(_settingsPlotDetails, "+",
                new Color32(0, 190, 75, 255), () =>
                {
                    var selected = requirement.value - 1;
                    if (selected < 0 || selected == _settingsPlotSelectedIndex ||
                        _settingsPlotRequirements.Contains(selected)) return;
                    _settingsPlotRequirements.Add(selected);
                    ApplyPlotRequirements(definition, plots);
                    RefreshPlotSettingsContents();
                });
            SetPlotControlRect(add.transform as RectTransform, .665f, .71f, 228f, 44f);
            BuildPlotRequirementChips(definition, plots);

            Canvas.ForceUpdateCanvases();
        }

        private void CreatePlotHeader(Transform parent, string value,
            TextAlignmentOptions alignment)
        {
            var header = NewImage("Cabecera", parent, _style.ButtonSprite,
                _style.SelectedColor, false);
            var rect = header.transform as RectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            rect.sizeDelta = new Vector2(0f, PlotListHeaderHeight - 8f);
            var title = CreateDiseaseText(header.transform, value, 22f,
                alignment, Color.white);
            Stretch(title.rectTransform);
            title.rectTransform.offsetMin = new Vector2(14f, 0f);
            title.rectTransform.offsetMax = new Vector2(-14f, 0f);
        }

        private List<string> BuildPlotNames(IList<HospitalPlot> plots)
        {
            var names = new List<string>
            {
                EditorLocalization.Get("editor.scenario_settings.plots.none")
            };
            for (var index = 0; index < plots.Count; ++index)
                names.Add(EditorLocalization.Format(
                    "editor.scenario_settings.plots.plot", index + 1));
            return names;
        }

        private void SelectPlotSettings(int index)
        {
            _settingsPlotSelectedIndex = index;
            RefreshPlotSettingsContents();
        }

        private void AddPlotLabel(string key, float minX, float maxX, float top)
        {
            var label = CreateDiseaseText(_settingsPlotDetails,
                EditorLocalization.Get(key), 18f, TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetPlotControlRect(label.rectTransform, minX, maxX, top, 44f);
        }

        private void AddPlotToggle(string key, float minX, float maxX, float top,
            bool value, Action<bool> changed)
        {
            var toggleWidth = .045f;
            AddPlotLabel(key, minX, maxX - toggleWidth - .01f, top);
            var toggle = CreateDiseaseToggle(_settingsPlotDetails, value, changed);
            if (toggle == null) return;
            SetPlotControlRect(toggle.transform as RectTransform,
                maxX - toggleWidth, maxX, top, 44f);
        }

        private void CreatePlotIntegerField(string labelKey, float minX, float maxX,
            float top, int value, Action<int> changed)
        {
            CreatePlotNumberField(labelKey, minX, maxX, top,
                value.ToString(CultureInfo.InvariantCulture), true,
                text =>
                {
                    int parsed;
                    if (!int.TryParse(text, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsed)) return false;
                    changed(parsed);
                    return true;
                });
        }

        private void CreatePlotFloatField(string labelKey, float minX, float maxX,
            float top, float value, Action<float> changed)
        {
            CreatePlotNumberField(labelKey, minX, maxX, top,
                value.ToString("0.##", CultureInfo.InvariantCulture), false,
                text =>
                {
                    float parsed;
                    if (!float.TryParse(text, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out parsed)) return false;
                    changed(Mathf.Max(0f, parsed));
                    return true;
                });
        }

        private void CreatePlotNumberField(string labelKey, float minX, float maxX,
            float top, string value, bool integer, Func<string, bool> accept)
        {
            var input = CreateScenarioSettingsInput(_settingsPlotDetails, labelKey,
                top, 44f, false, integer, minX, maxX, 130f);
            if (input == null) return;
            input.SetTextWithoutNotify(value);
            input.onEndEdit.AddListener(text =>
            {
                if (accept(text)) RefreshPlotSettingsContents();
                else input.SetTextWithoutNotify(value);
            });
        }

        private static void SetPlotControlRect(RectTransform rect, float minX,
            float maxX, float top, float height)
        {
            SetRect(rect, new Vector2(minX, 1f), new Vector2(maxX, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top),
                new Vector2(0f, height));
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
                RefreshPlotSettingsContents();
                return;
            }

            definition.MergeInto = CreatePlotWrapper(plots[targetIndex].Definition);
            if (!_settingsPlotRequirements.Contains(targetIndex))
                _settingsPlotRequirements.Add(targetIndex);
            ApplyPlotRequirements(definition, plots);
            RefreshPlotSettingsContents();
        }

        private void BuildPlotRequirementChips(HospitalPlotDefinition definition,
            IList<HospitalPlot> plots)
        {
            var mergeIndex = FindPlotDefinitionIndex(definition.MergeInto?.Instance, plots);
            for (var position = 0; position < _settingsPlotRequirements.Count; ++position)
            {
                var captured = _settingsPlotRequirements[position];
                var button = CreateDiseaseButton(_settingsPlotDetails,
                    (captured + 1).ToString(CultureInfo.InvariantCulture),
                    Color.white, () =>
                    {
                        _settingsPlotRequirements.Remove(captured);
                        if (mergeIndex == captured) definition.MergeInto = null;
                        ApplyPlotRequirements(definition, plots);
                        RefreshPlotSettingsContents();
                    });
                var left = .725f + position * .052f;
                SetPlotControlRect(button.transform as RectTransform,
                    left, left + .042f, 228f, 44f);
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.color = Color.black;
                if (captured != mergeIndex) continue;
                var outline = button.gameObject.GetComponent<Outline>() ??
                    button.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color32(235, 52, 188, 255);
                outline.effectDistance = new Vector2(3f, -3f);
                outline.useGraphicAlpha = false;
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
                foreach (var item in values)
                {
                    var index = FindPlotDefinitionIndex(item?.Instance, plots);
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
