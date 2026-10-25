using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private ScenarioDiseaseCatalog.Snapshot _settingsDiseaseCatalog;
        private readonly List<ScenarioMapFormat.WeightedIllnessData> _settingsIllnesses =
            new List<ScenarioMapFormat.WeightedIllnessData>();
        private readonly Dictionary<string, bool> _settingsDiseaseExpanded =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly List<GameObject> _settingsDiseaseSections = new List<GameObject>();
        private RectTransform _settingsDiseaseContent;
        private ScrollRect _settingsDiseaseScroll;

        private sealed class DiseaseChoice
        {
            internal string Id;
            internal string Name;
        }

        private void BuildDiseaseSettingsContents()
        {
            if (_settingsPages == null || _settingsPages.Length < 2 ||
                _settingsPages[1] == null) return;
            var page = _settingsPages[1].transform;
            var content = page.Find("Scroll View/Viewport/Content") as RectTransform;
            if (content == null) return;
            _settingsDiseaseContent = content;
            _settingsDiseaseScroll = page.GetComponentInChildren<ScrollRect>(true);
            if (_settingsDiseaseScroll != null)
            {
                _settingsDiseaseScroll.horizontal = false;
                _settingsDiseaseScroll.vertical = true;
                _settingsDiseaseScroll.scrollSensitivity = 34f;
                if (_settingsDiseaseScroll.verticalScrollbar != null)
                {
                    _settingsDiseaseScroll.verticalScrollbar.gameObject.SetActive(true);
                    _settingsDiseaseScroll.verticalScrollbarVisibility =
                        ScrollRect.ScrollbarVisibility.Permanent;
                }
            }
        }

        private void RefreshDiseaseSettingsContents()
        {
            if (_settingsDiseaseContent == null || _level == null) return;
            _settingsDiseaseCatalog = ScenarioDiseaseCatalog.Build(_level);
            _settingsIllnesses.Clear();
            var stored = ScenarioEditorStorage.CurrentScenarioSettings(_level).illnesses ??
                         new ScenarioMapFormat.WeightedIllnessData[0];
            foreach (var value in stored)
            {
                var copy = ScenarioDiseaseCatalog.Clone(value);
                if (copy != null) _settingsIllnesses.Add(copy);
            }
            NormalizeDiseaseRooms();
            RebuildDiseaseSettingsContents();
            if (_settingsDiseaseScroll != null)
                _settingsDiseaseScroll.verticalNormalizedPosition = 1f;
        }

        private void NormalizeDiseaseRooms()
        {
            if (_settingsDiseaseCatalog == null) return;
            foreach (var value in _settingsIllnesses)
            {
                if (!_settingsDiseaseCatalog.ById.TryGetValue(value.definition ?? "",
                        out var disease)) continue;
                var selected = _settingsDiseaseCatalog.Rooms.FirstOrDefault(room =>
                    room.Id == value.room && disease.Rooms.Contains(room.Definition));
                if (selected == null)
                    selected = _settingsDiseaseCatalog.Rooms.FirstOrDefault(room =>
                        disease.Rooms.Contains(room.Definition));
                value.room = selected?.Id ?? "";
            }
        }

        private void RebuildDiseaseSettingsContents()
        {
            if (_settingsDiseaseContent == null || _settingsDiseaseCatalog == null) return;
            foreach (var section in _settingsDiseaseSections)
                if (section != null) Destroy(section);
            _settingsDiseaseSections.Clear();

            var top = 12f;
            foreach (var room in _settingsDiseaseCatalog.Rooms)
            {
                if (!_settingsDiseaseExpanded.ContainsKey(room.Id))
                    _settingsDiseaseExpanded[room.Id] = true;
                var values = _settingsIllnesses.Where(value => value.room == room.Id).ToList();
                var expanded = _settingsDiseaseExpanded[room.Id];
                var height = 66f + (expanded ? values.Count * 122f + 8f : 0f);
                var section = NewImage("UP Disease Room " + room.Id, _settingsDiseaseContent,
                    _style.ButtonSprite, new Color32(5, 132, 204, 255), false);
                _settingsDiseaseSections.Add(section);
                var sectionRect = (RectTransform)section.transform;
                SetRect(sectionRect, new Vector2(.01f, 1f), new Vector2(.99f, 1f),
                    new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, height));
                BuildDiseaseRoomHeader(section.transform, room, expanded);
                if (expanded)
                    for (var index = 0; index < values.Count; ++index)
                        BuildDiseaseRow(section.transform, room, values[index], 68f + index * 122f);
                top += height + 10f;
            }
            _settingsDiseaseContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(430f, top));
            LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsDiseaseContent);
        }

        private void BuildDiseaseRoomHeader(Transform parent, ScenarioDiseaseCatalog.Room room,
            bool expanded)
        {
            var header = NewImage("Cabecera", parent, _style.ButtonSprite,
                _style.HeaderColor, false);
            var headerRect = (RectTransform)header.transform;
            SetRect(headerRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(.5f, 1f), Vector2.zero, new Vector2(-8f, 60f));

            if (room.Icon != null)
            {
                var icon = NewImage("Icono", header.transform, room.Icon, Color.white, false);
                SetTopLeft((RectTransform)icon.transform, 14f, 8f, 44f, 44f);
                icon.GetComponent<Image>().preserveAspect = true;
            }
            var roomName = CreateDiseaseText(header.transform, room.Name, 22f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            SetTopLeft(roomName.rectTransform, 70f, 5f, 280f, 50f);

            var available = AvailableForRoom(room, null);
            var choices = new List<DiseaseChoice>
            {
                new DiseaseChoice { Id = "", Name = EditorLocalization.Get(
                    "editor.scenario_settings.diseases.select") }
            };
            choices.AddRange(available.Select(disease => new DiseaseChoice
                { Id = disease.Id, Name = disease.Name }));
            var selector = CreateDiseaseDropdown(header.transform, choices.Select(choice =>
                choice.Name).ToList(), 0);
            SetTopLeft((RectTransform)selector.transform, 350f, 8f, 590f, 44f);

            var add = CreateDiseaseButton(header.transform,
                EditorLocalization.Get("editor.scenario_settings.diseases.add"),
                new Color32(15, 177, 77, 255), () =>
                {
                    if (selector.value <= 0 || selector.value >= choices.Count) return;
                    AddDisease(room, choices[selector.value].Id);
                });
            SetTopLeft((RectTransform)add.transform, 955f, 8f, 145f, 44f);

            var collapse = CreateDiseaseButton(header.transform, expanded ? "▲" : "▼",
                _style.ButtonColor, () =>
                {
                    _settingsDiseaseExpanded[room.Id] = !expanded;
                    RebuildDiseaseSettingsContents();
                });
            SetTopRight((RectTransform)collapse.transform, 10f, 8f, 58f, 44f);
        }

        private void BuildDiseaseRow(Transform parent, ScenarioDiseaseCatalog.Room room,
            ScenarioMapFormat.WeightedIllnessData value, float top)
        {
            var panel = NewImage("UP Disease " + value.definition, parent, _style.ButtonSprite,
                new Color32(22, 154, 221, 255), false);
            var rect = (RectTransform)panel.transform;
            SetRect(rect, new Vector2(.01f, 1f), new Vector2(.99f, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, 114f));

            _settingsDiseaseCatalog.ById.TryGetValue(value.definition ?? "", out var selected);
            AddFieldLabel(panel.transform, "editor.scenario_settings.diseases.illness", 16f, 8f, 115f);
            var nameField = CreateReadOnlyDiseaseField(panel.transform,
                selected?.Name ?? value.definition);
            SetTopLeft(nameField, 130f, 8f, 250f, 38f);

            AddFieldLabel(panel.transform, "editor.scenario_settings.diseases.definition",
                395f, 8f, 120f);
            var available = AvailableForRoom(room, value.definition);
            var choices = available.Select(disease => new DiseaseChoice
                { Id = disease.Id, Name = disease.Id }).ToList();
            var currentIndex = Mathf.Max(0, choices.FindIndex(choice => choice.Id == value.definition));
            var definition = CreateDiseaseDropdown(panel.transform,
                choices.Select(choice => choice.Name).ToList(), currentIndex);
            SetTopLeft((RectTransform)definition.transform, 515f, 8f, 325f, 38f);
            definition.onValueChanged.AddListener(index =>
            {
                if (index < 0 || index >= choices.Count) return;
                value.definition = choices[index].Id;
                RebuildDiseaseSettingsContents();
            });

            AddFieldLabel(panel.transform, "editor.scenario_settings.diseases.deprecated",
                855f, 8f, 120f);
            var deprecated = CreateDiseaseToggle(panel.transform, value.deprecated,
                state => value.deprecated = state);
            SetTopLeft((RectTransform)deprecated.transform, 975f, 12f, 30f, 30f);
            AddFieldLabel(panel.transform, "editor.scenario_settings.diseases.unlocked",
                1020f, 8f, 105f);
            var unlocked = CreateDiseaseToggle(panel.transform, value.unlocked,
                state => value.unlocked = state);
            SetTopLeft((RectTransform)unlocked.transform, 1125f, 12f, 30f, 30f);

            CreateDiseaseInteger(panel.transform,
                "editor.scenario_settings.diseases.min_weight", value.minWeight,
                16f, changed => value.minWeight = changed);
            CreateDiseaseInteger(panel.transform,
                "editor.scenario_settings.diseases.max_weight", value.maxWeight,
                275f, changed => value.maxWeight = changed);
            CreateDiseaseInteger(panel.transform,
                "editor.scenario_settings.diseases.min_stars", value.minStarRating,
                535f, changed => value.minStarRating = changed);
            CreateDiseaseInteger(panel.transform,
                "editor.scenario_settings.diseases.min_spawned", value.minPatientsSpawned,
                795f, changed => value.minPatientsSpawned = changed);

            var remove = CreateDiseaseButton(panel.transform,
                EditorLocalization.Get("editor.scenario_settings.diseases.remove"),
                new Color32(232, 38, 46, 255), () =>
                {
                    _settingsIllnesses.Remove(value);
                    RebuildDiseaseSettingsContents();
                });
            SetTopRight((RectTransform)remove.transform, 10f, 66f, 125f, 38f);
        }

        private List<ScenarioDiseaseCatalog.Disease> AvailableForRoom(
            ScenarioDiseaseCatalog.Room room, string includeId)
        {
            var selected = new HashSet<string>(_settingsIllnesses
                .Select(value => value.definition ?? ""), StringComparer.Ordinal);
            return room.Diseases.Where(disease => disease.Id == includeId ||
                !selected.Contains(disease.Id)).ToList();
        }

        private void AddDisease(ScenarioDiseaseCatalog.Room room, string id)
        {
            if (string.IsNullOrEmpty(id) || _settingsIllnesses.Any(value =>
                    value.definition == id) || !_settingsDiseaseCatalog.ById.TryGetValue(id,
                    out var disease)) return;
            var value = ScenarioDiseaseCatalog.FromCatalog(disease);
            value.room = room.Id;
            _settingsIllnesses.Add(value);
            _settingsDiseaseExpanded[room.Id] = true;
            RebuildDiseaseSettingsContents();
        }

        private void CreateDiseaseInteger(Transform parent, string key, int current,
            float x, Action<int> setter)
        {
            AddFieldLabel(parent, key, x, 64f, 135f);
            var input = CreateDiseaseInput(parent, current.ToString(CultureInfo.InvariantCulture));
            SetTopLeft((RectTransform)input.transform, x + 140f, 64f, 100f, 36f);
            input.onEndEdit.AddListener(text =>
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var parsed)) setter(parsed);
                else input.SetTextWithoutNotify(current.ToString(CultureInfo.InvariantCulture));
            });
        }

        private void AddFieldLabel(Transform parent, string key, float x, float y, float width)
        {
            var label = CreateDiseaseText(parent, EditorLocalization.Get(key), 17f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            SetTopLeft(label.rectTransform, x, y, width, 38f);
        }

        private RectTransform CreateReadOnlyDiseaseField(Transform parent, string value)
        {
            var field = NewImage("Valor", parent, _style.ButtonSprite,
                new Color32(218, 222, 226, 255), false);
            var label = CreateDiseaseText(field.transform, value ?? "", 16f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.rectTransform.offsetMax = new Vector2(-8f, 0f);
            return (RectTransform)field.transform;
        }

        private TMP_InputField CreateDiseaseInput(Transform parent, string value)
        {
            var root = NewImage("Entrada", parent, _style.ButtonSprite,
                new Color32(218, 222, 226, 255), true);
            var input = root.AddComponent<TMP_InputField>();
            var viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(8f, 1f);
            viewportRect.offsetMax = new Vector2(-8f, -1f);
            var label = CreateDiseaseText(viewport.transform, value, 16f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            input.targetGraphic = root.GetComponent<Image>();
            input.textViewport = viewportRect;
            input.textComponent = label;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(value);
            return input;
        }

        private Toggle CreateDiseaseToggle(Transform parent, bool value, Action<bool> changed)
        {
            var root = NewImage("Casilla", parent, _style.ButtonSprite,
                new Color32(216, 226, 235, 255), true);
            var toggle = root.AddComponent<Toggle>();
            var mark = NewImage("Marca", root.transform, _style.ButtonSprite,
                _style.ButtonColor, false);
            var markRect = (RectTransform)mark.transform;
            Stretch(markRect);
            markRect.offsetMin = new Vector2(5f, 5f);
            markRect.offsetMax = new Vector2(-5f, -5f);
            toggle.targetGraphic = root.GetComponent<Image>();
            toggle.graphic = mark.GetComponent<Image>();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(state => changed(state));
            return toggle;
        }

        private Button CreateDiseaseButton(Transform parent, string value, Color color,
            Action clicked)
        {
            var root = NewImage("Boton " + value, parent, _style.ButtonSprite, color, true);
            var button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.colors = _style.ButtonColors;
            button.onClick.AddListener(() => clicked());
            CreateDiseaseText(root.transform, value, 18f,
                TextAlignmentOptions.Center, Color.white).fontStyle = FontStyles.Bold;
            StandardiseClickSound(button);
            return button;
        }

        private TMP_Dropdown CreateDiseaseDropdown(Transform parent, IList<string> options,
            int selected)
        {
            var root = NewImage("Selector", parent, _style.ButtonSprite,
                new Color32(218, 222, 226, 255), true);
            var dropdown = root.AddComponent<TMP_Dropdown>();
            var caption = CreateDiseaseText(root.transform, "", 16f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            caption.rectTransform.offsetMin = new Vector2(10f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-34f, 0f);
            var arrow = CreateDiseaseText(root.transform, "▼", 17f,
                TextAlignmentOptions.Center, Color.black);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
            arrow.rectTransform.anchorMax = Vector2.one;
            arrow.rectTransform.pivot = new Vector2(1f, .5f);
            arrow.rectTransform.sizeDelta = new Vector2(32f, 0f);
            arrow.rectTransform.anchoredPosition = Vector2.zero;

            var template = NewImage("Template", root.transform, _style.ButtonSprite,
                new Color32(225, 230, 235, 255), true);
            var templateRect = (RectTransform)template.transform;
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -2f);
            templateRect.sizeDelta = new Vector2(0f, 260f);
            var scroll = template.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image),
                typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMax = new Vector2(-18f, 0f);
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            var content = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            content.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var item = NewImage("Item", content.transform, _style.ButtonSprite,
                new Color32(218, 222, 226, 255), true);
            var itemRect = (RectTransform)item.transform;
            itemRect.sizeDelta = new Vector2(0f, 34f);
            var itemLayout = item.AddComponent<LayoutElement>();
            itemLayout.minHeight = itemLayout.preferredHeight = 34f;
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();
            var itemLabel = CreateDiseaseText(item.transform, "", 15f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            itemLabel.rectTransform.offsetMin = new Vector2(10f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-8f, 0f);

            var scrollbarRoot = NewImage("Scrollbar", template.transform, _style.ButtonSprite,
                new Color32(13, 96, 145, 255), true);
            var scrollbarRect = (RectTransform)scrollbarRoot.transform;
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = new Vector2(1f, .5f);
            scrollbarRect.anchoredPosition = Vector2.zero;
            scrollbarRect.sizeDelta = new Vector2(18f, 0f);
            var scrollbar = scrollbarRoot.AddComponent<Scrollbar>();
            var sliding = new GameObject("Sliding Area", typeof(RectTransform));
            sliding.transform.SetParent(scrollbarRoot.transform, false);
            Stretch((RectTransform)sliding.transform);
            var handle = NewImage("Handle", sliding.transform, _style.ButtonSprite,
                _style.ButtonColor, true);
            Stretch((RectTransform)handle.transform);
            scrollbar.handleRect = (RectTransform)handle.transform;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.verticalScrollbar = scrollbar;
            dropdown.targetGraphic = root.GetComponent<Image>();
            dropdown.template = templateRect;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            dropdown.options = options.Select(value => new TMP_Dropdown.OptionData(value)).ToList();
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0,
                Mathf.Max(0, dropdown.options.Count - 1)));
            dropdown.RefreshShownValue();
            template.SetActive(false);
            return dropdown;
        }

        private static TextMeshProUGUI CreateDiseaseText(Transform parent, string value,
            float size, TextAlignmentOptions alignment, Color color)
        {
            var root = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            Stretch(rect);
            var text = root.GetComponent<TextMeshProUGUI>();
            text.text = value ?? "";
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private void StoreDiseaseSettings(ScenarioMapFormat.Map settings)
        {
            if (settings == null) return;
            settings.illnessesConfigured = true;
            settings.illnesses = _settingsIllnesses.Select(ScenarioDiseaseCatalog.Clone)
                .Where(value => value != null).ToArray();
        }

        private void ReleaseDiseaseSettingsContents()
        {
            _settingsDiseaseSections.Clear();
            _settingsIllnesses.Clear();
            _settingsDiseaseExpanded.Clear();
            _settingsDiseaseCatalog = null;
            _settingsDiseaseContent = null;
            _settingsDiseaseScroll = null;
        }
    }
}
