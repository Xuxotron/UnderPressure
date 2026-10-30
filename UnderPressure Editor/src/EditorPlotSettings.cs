using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private const float PlotListHeaderHeight = 52f;
        private const float PlotListButtonHeight = 54f;
        private const float PlotListSpacing = 6f;
        private const float PlotListWidth = 300f;
        private const float PlotColumnGap = 18f;

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
            var page = _settingsPages[3].transform;
            var nativeScroll = page.GetComponentInChildren<ScrollRect>(true);
            if (nativeScroll != null) nativeScroll.gameObject.SetActive(false);

            _settingsPlotRoot = NewImage("UP Plot Settings", page, null,
                Color.clear, false).transform as RectTransform;
            Stretch(_settingsPlotRoot);
            _settingsPlotRoot.offsetMin = new Vector2(10f, 10f);
            _settingsPlotRoot.offsetMax = new Vector2(-10f, -10f);

            var listPanel = NewImage("Lista de parcelas", _settingsPlotRoot,
                _style.ButtonSprite, _style.PanelColor, false);
            var listRect = listPanel.transform as RectTransform;
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(0f, 1f);
            listRect.pivot = new Vector2(0f, .5f);
            listRect.anchoredPosition = Vector2.zero;
            listRect.sizeDelta = new Vector2(PlotListWidth, 0f);
            CreatePlotHeader(listPanel.transform,
                EditorLocalization.Get("editor.scenario_settings.plots.list"),
                TextAlignmentOptions.Center, _style.ButtonColor);

            var scrollObject = new GameObject("Desplazamiento de parcelas",
                typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(listPanel.transform, false);
            var scrollRect = scrollObject.transform as RectTransform;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(10f, 10f);
            scrollRect.offsetMax = new Vector2(-10f, -PlotListHeaderHeight - 8f);

            var viewportObject = new GameObject("Viewport", typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRect, false);
            var viewport = viewportObject.transform as RectTransform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;

            var listObject = new GameObject("Content", typeof(RectTransform));
            listObject.transform.SetParent(viewport, false);
            _settingsPlotList = listObject.transform as RectTransform;
            _settingsPlotList.anchorMin = new Vector2(0f, 1f);
            _settingsPlotList.anchorMax = Vector2.one;
            _settingsPlotList.pivot = new Vector2(.5f, 1f);
            _settingsPlotList.anchoredPosition = Vector2.zero;
            _settingsPlotList.sizeDelta = new Vector2(0f, 430f);

            _settingsPlotScroll = scrollObject.GetComponent<ScrollRect>();
            _settingsPlotScroll.viewport = viewport;
            _settingsPlotScroll.content = _settingsPlotList;
            _settingsPlotScroll.horizontal = false;
            _settingsPlotScroll.vertical = true;
            _settingsPlotScroll.inertia = true;
            _settingsPlotScroll.movementType = ScrollRect.MovementType.Clamped;
            _settingsPlotScroll.scrollSensitivity = 120f;
            InstallDiseaseNativeScrollbar(scrollRect, _settingsPlotScroll,
                "UP Plot Native Scrollbar", 0f, 0f);
            scrollRect.SetAsLastSibling();

            var detailsPanel = NewImage("Editor de parcela", _settingsPlotRoot,
                _style.ButtonSprite, _style.PanelColor, false);
            _settingsPlotDetails = detailsPanel.transform as RectTransform;
            _settingsPlotDetails.anchorMin = Vector2.zero;
            _settingsPlotDetails.anchorMax = Vector2.one;
            _settingsPlotDetails.offsetMin = new Vector2(
                PlotListWidth + PlotColumnGap, 0f);
            _settingsPlotDetails.offsetMax = Vector2.zero;
            RefreshPlotSettingsContents();
        }

        private void RefreshPlotSettingsContents()
        {
            if (_settingsPlotList == null || _settingsPlotDetails == null ||
                _level?.WorldState?.HospitalPlots == null) return;
            var spatialPlots = _level.WorldState.HospitalPlots;
            var plots = ScenarioEditorStorage.CurrentPlotSettings(_level);
            _settingsPlotSelectedIndex = Mathf.Clamp(_settingsPlotSelectedIndex, 0,
                Mathf.Max(0, plots.Count - 1));

            ClearPlotChildren(_settingsPlotList);
            var contentHeight = plots.Count * PlotListButtonHeight +
                Mathf.Max(0, plots.Count - 1) * PlotListSpacing;
            _settingsPlotList.sizeDelta = new Vector2(0f, contentHeight);
            for (var index = 0; index < plots.Count; ++index)
            {
                var captured = index;
                var selected = index == _settingsPlotSelectedIndex;
                var button = CreateDiseaseButton(_settingsPlotList,
                    EditorLocalization.Format("editor.scenario_settings.plots.plot", index + 1),
                    selected ? _style.ButtonColor : _style.SelectedColor,
                    () => SelectPlotSettings(captured));
                var rect = button.transform as RectTransform;
                SetRect(rect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(.5f, 1f),
                    new Vector2(0f, -index * (PlotListButtonHeight + PlotListSpacing)),
                    new Vector2(0f, PlotListButtonHeight));
            }
            _settingsPlotList.anchoredPosition = Vector2.zero;
            _settingsPlotScroll.StopMovement();
            _settingsPlotScroll.verticalNormalizedPosition = 1f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsPlotList);

            ClearPlotChildren(_settingsPlotDetails);
            if (plots.Count == 0) return;
            var plot = plots[_settingsPlotSelectedIndex];
            if (plot == null) return;
            LoadPlotRequirements(plot);

            CreatePlotHeader(_settingsPlotDetails,
                EditorLocalization.Format("editor.scenario_settings.plots.plot",
                    _settingsPlotSelectedIndex + 1), TextAlignmentOptions.MidlineLeft,
                _style.SelectedColor);

            CreatePlotIntegerField("editor.scenario_settings.plots.cost",
                .025f, .335f, 86f, plot.cost,
                value => plot.cost = value);
            AddPlotToggle("editor.scenario_settings.plots.initially_open",
                .35f, .66f, 86f, plot.initiallyOpen,
                value => plot.initiallyOpen = value);
            AddPlotToggle("editor.scenario_settings.plots.hide_until_available",
                .675f, .985f, 86f, plot.hideUntilAvailable,
                value => plot.hideUntilAvailable = value);

            AddPlotLabel("editor.scenario_settings.plots.initial_state",
                .025f, .17f, 158f);
            var state = CreateDiseaseDropdown(_settingsPlotDetails,
                new[]
                {
                    EditorLocalization.Get("editor.scenario_settings.plots.unbuilt"),
                    EditorLocalization.Get("editor.scenario_settings.plots.built")
                }, plot.built ? 1 : 0);
            SetPlotControlRect(state.transform as RectTransform, .175f, .335f, 156f, 44f);
            state.onValueChanged.AddListener(value => plot.built = value == 1);

            AddPlotToggle("editor.scenario_settings.plots.instant_build",
                .35f, .66f, 158f, plot.instaBuild,
                value => plot.instaBuild = value);
            CreatePlotFloatField("editor.scenario_settings.plots.build_time",
                .675f, .985f, 158f, plot.timeToBuild,
                value => plot.timeToBuild = value);

            var plotNames = BuildPlotNames(spatialPlots);
            AddPlotLabel("editor.scenario_settings.plots.merge_into",
                .025f, .17f, 230f);
            var mergeIndex = plot.mergeInto;
            var merge = CreateDiseaseDropdown(_settingsPlotDetails, plotNames, mergeIndex + 1);
            SetPlotControlRect(merge.transform as RectTransform, .175f, .335f, 228f, 44f);
            merge.onValueChanged.AddListener(value => SetPlotMerge(value - 1));

            AddPlotLabel("editor.scenario_settings.plots.requires",
                .35f, .495f, 230f);
            var requirement = CreateDiseaseDropdown(_settingsPlotDetails, plotNames, 0);
            SetPlotControlRect(requirement.transform as RectTransform,
                .50f, .66f, 228f, 44f);
            var add = CreateDiseaseButton(_settingsPlotDetails, "+",
                new Color32(0, 190, 75, 255), () =>
                {
                    var selected = requirement.value - 1;
                    if (selected < 0 || selected == _settingsPlotSelectedIndex ||
                        _settingsPlotRequirements.Contains(selected)) return;
                    _settingsPlotRequirements.Add(selected);
                    ApplyPlotRequirements(plot, plots.Count);
                    RefreshPlotSettingsContents();
                });
            SetPlotControlRect(add.transform as RectTransform, .675f, .72f, 228f, 44f);
            BuildPlotRequirementChips(plot, plots.Count);

            Canvas.ForceUpdateCanvases();
        }

        private void CreatePlotHeader(Transform parent, string value,
            TextAlignmentOptions alignment, Color color)
        {
            var header = NewImage("Cabecera", parent, _style.ButtonSprite,
                color, false);
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
                EditorLocalization.Get(key) + ":", 18f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetPlotControlRect(label.rectTransform, minX, maxX, top, 44f);
        }

        private void AddPlotToggle(string key, float minX, float maxX, float top,
            bool value, Action<bool> changed)
        {
            const float toggleWidth = .045f;
            AddPlotLabel(key, minX, maxX - toggleWidth - .012f, top);
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
                130f, 120,
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
                80f, 3,
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
            float top, string value, bool integer, float inputWidth,
            int characterLimit, Func<string, bool> accept)
        {
            var input = CreateScenarioSettingsInput(_settingsPlotDetails, labelKey,
                top, 48f, false, integer, minX, maxX, inputWidth);
            if (input == null) return;
            ConfigurePlotNativeField(input, labelKey, characterLimit);
            input.SetTextWithoutNotify(value);
            input.onEndEdit.AddListener(text =>
            {
                if (accept(text)) RefreshPlotSettingsContents();
                else input.SetTextWithoutNotify(value);
            });
        }

        private static void ConfigurePlotNativeField(TMP_InputField input,
            string labelKey, int characterLimit)
        {
            input.characterLimit = characterLimit;
            var fieldRoot = input.transform.parent;
            var row = fieldRoot;
            while (row != null &&
                   !row.name.StartsWith("UP Workshop Settings Field ",
                       StringComparison.Ordinal))
                row = row.parent;
            if (row == null) return;

            SetWorkshopFieldLabel(row, fieldRoot,
                EditorLocalization.Get(labelKey) + ":");
            foreach (var label in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.transform.IsChildOf(fieldRoot)) continue;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
            }
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
            var plots = ScenarioEditorStorage.CurrentPlotSettings(_level);
            if (_settingsPlotSelectedIndex < 0 ||
                _settingsPlotSelectedIndex >= plots.Count) return;
            var plot = plots[_settingsPlotSelectedIndex];
            if (targetIndex < 0 || targetIndex >= plots.Count ||
                targetIndex == _settingsPlotSelectedIndex)
            {
                plot.mergeInto = -1;
                RefreshPlotSettingsContents();
                return;
            }

            plot.mergeInto = targetIndex;
            if (!_settingsPlotRequirements.Contains(targetIndex))
                _settingsPlotRequirements.Add(targetIndex);
            ApplyPlotRequirements(plot, plots.Count);
            RefreshPlotSettingsContents();
        }

        private void BuildPlotRequirementChips(ScenarioMapFormat.Plot plot,
            int plotCount)
        {
            var mergeIndex = plot.mergeInto;
            for (var position = 0; position < _settingsPlotRequirements.Count; ++position)
            {
                var captured = _settingsPlotRequirements[position];
                var button = CreateDiseaseButton(_settingsPlotDetails,
                    (captured + 1).ToString(CultureInfo.InvariantCulture),
                    Color.white, () =>
                    {
                        _settingsPlotRequirements.Remove(captured);
                        if (mergeIndex == captured) plot.mergeInto = -1;
                        ApplyPlotRequirements(plot, plotCount);
                        RefreshPlotSettingsContents();
                    });
                var left = .735f + position * .052f;
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

        private void LoadPlotRequirements(ScenarioMapFormat.Plot plot)
        {
            _settingsPlotRequirements.Clear();
            if (plot.prerequisites == null) return;
            foreach (var prerequisite in plot.prerequisites)
            {
                if (prerequisite?.anyBought == null) continue;
                foreach (var index in prerequisite.anyBought)
                    if (index >= 0 && !_settingsPlotRequirements.Contains(index))
                        _settingsPlotRequirements.Add(index);
            }
        }

        private void ApplyPlotRequirements(ScenarioMapFormat.Plot plot, int plotCount)
        {
            plot.prerequisites = _settingsPlotRequirements.Distinct()
                .Where(index => index >= 0 && index < plotCount)
                .Select(index => new ScenarioMapFormat.PlotPrerequisite
                {
                    anyBought = new[] { index }
                }).ToArray();
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
