using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using TH20;
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
        private RectTransform _settingsDiseaseSurface;
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
            var nativeScroll = page.GetComponentInChildren<ScrollRect>(true);
            if (nativeScroll != null) nativeScroll.gameObject.SetActive(false);

            // La página clonada conserva animadores y máscaras internas de
            // PreferencesScreen. Para esta pestaña solo se reutiliza su barra
            // visual; el área desplazable pertenece enteramente al editor.
            var surface = new GameObject("UP Disease Scroll", typeof(RectTransform),
                typeof(ScrollRect));
            surface.transform.SetParent(page, false);
            _settingsDiseaseSurface = (RectTransform)surface.transform;
            Stretch(_settingsDiseaseSurface);
            _settingsDiseaseSurface.offsetMin = new Vector2(10f, 10f);
            _settingsDiseaseSurface.offsetMax = new Vector2(-10f, -10f);

            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(RectMask2D));
            viewport.transform.SetParent(surface.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _settingsDiseaseContent = (RectTransform)content.transform;
            _settingsDiseaseContent.anchorMin = new Vector2(0f, 1f);
            _settingsDiseaseContent.anchorMax = Vector2.one;
            _settingsDiseaseContent.pivot = new Vector2(.5f, 1f);
            _settingsDiseaseContent.anchoredPosition = Vector2.zero;
            _settingsDiseaseContent.sizeDelta = new Vector2(0f, 430f);

            _settingsDiseaseScroll = surface.GetComponent<ScrollRect>();
            _settingsDiseaseScroll.viewport = viewportRect;
            _settingsDiseaseScroll.content = _settingsDiseaseContent;
            _settingsDiseaseScroll.horizontal = false;
            _settingsDiseaseScroll.vertical = true;
            _settingsDiseaseScroll.scrollSensitivity = 34f;
            InstallNativeVerticalScrollbar(_settingsDiseaseSurface, _settingsDiseaseScroll,
                "UP Disease Native Scrollbar", 0f, 0f);
            _settingsDiseaseSurface.SetAsLastSibling();
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
            if (deprecated != null)
                SetTopLeft((RectTransform)deprecated.transform, 975f, 12f, 30f, 30f);
            AddFieldLabel(panel.transform, "editor.scenario_settings.diseases.unlocked",
                1020f, 8f, 105f);
            var unlocked = CreateDiseaseToggle(panel.transform, value.unlocked,
                state => value.unlocked = state);
            if (unlocked != null)
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
            if (input == null) return;
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

        private InputField CreateDiseaseInput(Transform parent, string value)
        {
            if (!TryGetWorkshopFieldTemplate(false, out _, out var nativeComponent) ||
                !(nativeComponent is InputField nativeInput))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Enfermedades: no se encontro el InputField nativo del Workshop.");
                return null;
            }

            var input = Instantiate(nativeInput, parent, false);
            input.gameObject.name = "UP Disease Native Input";
            input.gameObject.SetActive(true);
            input.transform.localScale = Vector3.one;
            DisableLocalisers(input.transform);
            var layout = input.GetComponent<LayoutElement>();
            if (layout != null) layout.enabled = false;
            input.onEndEdit.RemoveAllListeners();
            input.onValueChanged.RemoveAllListeners();
            input.onValidateInput = null;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 12;
            input.customCaretColor = true;
            input.caretColor = Color.black;
            if (input.textComponent != null) input.textComponent.color = Color.black;
            input.SetTextWithoutNotify(value);
            return input;
        }

        private Toggle CreateDiseaseToggle(Transform parent, bool value, Action<bool> changed)
        {
            Toggle native = null;
            foreach (var row in Resources.FindObjectsOfTypeAll<PlotSelectionItem>())
            {
                if (row == null) continue;
                native = AccessTools.Field(typeof(PlotSelectionItem), "_toggleLayerBase")?
                    .GetValue(row) as Toggle;
                if (native != null &&
                    (native.targetGraphic == null || native.graphic == null)) native = null;
                if (native != null) break;
            }
            if (native == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Enfermedades: no se encontro el Toggle nativo de las parcelas.");
                return null;
            }

            var toggle = Instantiate(native, parent, false);
            toggle.gameObject.name = "UP Disease Native Toggle";
            toggle.gameObject.SetActive(true);
            toggle.transform.localScale = Vector3.one;
            DisableLocalisers(toggle.transform);
            var layout = toggle.GetComponent<LayoutElement>();
            if (layout != null) layout.enabled = false;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.interactable = true;
            if (toggle.graphic != null) toggle.graphic.gameObject.SetActive(true);
            toggle.SetIsOnWithoutNotify(value);
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
            templateRect.sizeDelta = new Vector2(0f,
                Mathf.Clamp(options.Count * 35f + 4f, 39f, 260f));
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
            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(34f, options.Count * 35f));
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
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = Vector2.one;
            itemRect.pivot = new Vector2(.5f, 1f);
            itemRect.anchoredPosition = Vector2.zero;
            itemRect.sizeDelta = new Vector2(0f, 34f);
            var itemLayout = item.AddComponent<LayoutElement>();
            itemLayout.minHeight = itemLayout.preferredHeight = 34f;
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();
            var itemLabel = CreateDiseaseText(item.transform, "", 15f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            itemLabel.rectTransform.offsetMin = new Vector2(10f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-8f, 0f);

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            InstallNativeVerticalScrollbar(templateRect, scroll,
                "UP Disease Dropdown Native Scrollbar", 2f, 2f);
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
            _settingsDiseaseSurface = null;
            _settingsDiseaseContent = null;
            _settingsDiseaseScroll = null;
        }
    }
}
