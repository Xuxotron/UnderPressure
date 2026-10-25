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
        private ScrollRect _settingsDiseaseNativeScrollTemplate;
        private Scrollbar _settingsDiseaseNativeScrollbarTemplate;
        private Toggle _settingsDiseaseNativeToggleTemplate;
        private bool _settingsDiseasesLoaded;

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
            var inheritedScroll = page.GetComponentInChildren<ScrollRect>(true);
            if (inheritedScroll != null) inheritedScroll.gameObject.SetActive(false);

            var source = _settingsDiseaseNativeScrollTemplate;
            var nativeScrollObject = source != null
                ? Instantiate(source.gameObject, page, false)
                : null;
            var nativeScroll = nativeScrollObject != null
                ? nativeScrollObject.GetComponent<ScrollRect>()
                : null;
            if (nativeScroll == null || nativeScroll.viewport == null ||
                nativeScroll.content == null)
            {
                if (nativeScrollObject != null) Destroy(nativeScrollObject);
                UnderPressureEditorPlugin.Log?.LogError(
                    "Enfermedades: no se pudo clonar el ScrollRect nativo completo de Ajustes.");
                return;
            }

            nativeScrollObject.name = "UP Disease Native Scroll";
            DisableLocalisers(nativeScrollObject.transform);
            nativeScroll.gameObject.SetActive(true);
            _settingsDiseaseSurface = nativeScroll.transform as RectTransform;
            _settingsDiseaseContent = nativeScroll.content;
            foreach (var layout in _settingsDiseaseContent.GetComponents<LayoutGroup>())
                layout.enabled = false;
            var fitter = _settingsDiseaseContent.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            for (var index = _settingsDiseaseContent.childCount - 1; index >= 0; --index)
            {
                var child = _settingsDiseaseContent.GetChild(index);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            _settingsDiseaseContent.anchorMin = new Vector2(0f, 1f);
            _settingsDiseaseContent.anchorMax = Vector2.one;
            _settingsDiseaseContent.pivot = new Vector2(.5f, 1f);
            _settingsDiseaseContent.anchoredPosition = Vector2.zero;
            _settingsDiseaseContent.sizeDelta = new Vector2(0f, 430f);

            _settingsDiseaseScroll = nativeScroll;
            _settingsDiseaseScroll.horizontal = false;
            _settingsDiseaseScroll.vertical = true;
            _settingsDiseaseScroll.inertia = true;
            _settingsDiseaseScroll.movementType = ScrollRect.MovementType.Clamped;
            _settingsDiseaseScroll.scrollSensitivity = 120f;
            var viewportGraphic = _settingsDiseaseScroll.viewport.GetComponent<Graphic>();
            if (viewportGraphic != null) viewportGraphic.raycastTarget = true;
            ConfigureDiseaseNativeScrollbar(_settingsDiseaseScroll.verticalScrollbar,
                _settingsDiseaseScroll);
            _settingsDiseaseSurface.SetAsLastSibling();
        }

        private void RefreshDiseaseSettingsContents()
        {
            if (_settingsDiseaseContent == null || _level == null) return;
            if (_settingsDiseasesLoaded) return;
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
            _settingsDiseasesLoaded = true;
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
                var section = BuildDiseaseRoomSection(room, top);
                _settingsDiseaseSections.Add(section);
                top += ((RectTransform)section.transform).sizeDelta.y + 10f;
            }
            _settingsDiseaseContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(430f, top));
            LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsDiseaseContent);
        }

        private GameObject BuildDiseaseRoomSection(ScenarioDiseaseCatalog.Room room, float top)
        {
            var values = _settingsIllnesses.Where(value => value.room == room.Id).ToList();
            var expanded = _settingsDiseaseExpanded[room.Id];
            var height = 66f + (expanded ? values.Count * 122f + 8f : 0f);
            var section = NewImage("UP Disease Room " + room.Id, _settingsDiseaseContent,
                _style.ButtonSprite, new Color32(5, 132, 204, 255), true);
            var sectionRect = (RectTransform)section.transform;
            SetRect(sectionRect, new Vector2(.01f, 1f), new Vector2(.99f, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, height));
            BuildDiseaseRoomHeader(section.transform, room, expanded);
            if (expanded)
                for (var index = 0; index < values.Count; ++index)
                    BuildDiseaseRow(section.transform, room, values[index], 68f + index * 122f);
            return section;
        }

        private void RebuildDiseaseRoom(ScenarioDiseaseCatalog.Room room)
        {
            if (_settingsDiseaseCatalog == null || _settingsDiseaseContent == null) return;
            var index = _settingsDiseaseCatalog.Rooms.IndexOf(room);
            if (index < 0 || index >= _settingsDiseaseSections.Count) return;
            var previous = _settingsDiseaseSections[index];
            var previousRect = previous != null ? previous.transform as RectTransform : null;
            if (previousRect == null) return;
            var top = -previousRect.anchoredPosition.y;
            var previousHeight = previousRect.sizeDelta.y;
            previous.SetActive(false);
            Destroy(previous);

            var replacement = BuildDiseaseRoomSection(room, top);
            _settingsDiseaseSections[index] = replacement;
            replacement.transform.SetSiblingIndex(index);
            var replacementHeight = ((RectTransform)replacement.transform).sizeDelta.y;
            var difference = replacementHeight - previousHeight;
            if (Mathf.Abs(difference) > .01f)
                for (var next = index + 1; next < _settingsDiseaseSections.Count; ++next)
                {
                    var nextRect = _settingsDiseaseSections[next]?.transform as RectTransform;
                    if (nextRect != null)
                        nextRect.anchoredPosition += new Vector2(0f, -difference);
                }
            _settingsDiseaseContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(430f, _settingsDiseaseContent.rect.height + difference));
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

            if (room.Dlc?.Icon != null)
            {
                var dlcIcon = NewImage("Icono DLC", header.transform, room.Dlc.Icon,
                    Color.white, false);
                SetTopLeft((RectTransform)dlcIcon.transform, 1110f, 8f, 44f, 44f);
                dlcIcon.GetComponent<Image>().preserveAspect = true;
            }

            var collapse = CreateDiseaseButton(header.transform, expanded ? "▲" : "▼",
                _style.ButtonColor, () =>
                {
                    _settingsDiseaseExpanded[room.Id] = !expanded;
                    RebuildDiseaseRoom(room);
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
            var name = CreateDiseaseText(panel.transform,
                selected?.Name ?? value.definition, 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            name.fontStyle = FontStyles.Bold;
            SetTopLeft(name.rectTransform, 130f, 8f, 250f, 38f);

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
                855f, 8f, 92f);
            var deprecated = CreateDiseaseToggle(panel.transform, value.deprecated,
                state => value.deprecated = state);
            if (deprecated != null)
                SetTopLeft((RectTransform)deprecated.transform, 947f, 12f, 30f, 30f);
            AddFieldLabel(panel.transform, "editor.scenario_settings.diseases.unlocked",
                995f, 8f, 145f);
            var unlocked = CreateDiseaseToggle(panel.transform, value.unlocked,
                state => value.unlocked = state);
            if (unlocked != null)
                SetTopLeft((RectTransform)unlocked.transform, 1140f, 12f, 30f, 30f);

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
            var selectedNames = new HashSet<string>(_settingsIllnesses
                .Select(value => _settingsDiseaseCatalog.ById.TryGetValue(
                    value.definition ?? "", out var disease) ? disease.Name : "")
                .Where(name => !string.IsNullOrEmpty(name)),
                StringComparer.CurrentCultureIgnoreCase);
            return room.Diseases
                .Where(disease => disease.Id == includeId ||
                    (!selected.Contains(disease.Id) && !selectedNames.Contains(disease.Name)))
                .GroupBy(disease => disease.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.FirstOrDefault(disease => disease.Id == includeId) ??
                                 group.First())
                .ToList();
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
            var input = CreateDiseaseInput(parent,
                current.ToString(CultureInfo.InvariantCulture));
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

        private TMP_InputField CreateDiseaseInput(Transform parent, string value)
        {
            var root = NewImage("Entrada", parent, _style.ButtonSprite,
                new Color32(218, 222, 226, 255), true);
            var input = root.AddComponent<TMP_InputField>();
            var viewport = new GameObject("Text Area", typeof(RectTransform),
                typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(8f, 1f);
            viewportRect.offsetMax = new Vector2(-8f, -1f);
            var text = CreateDiseaseText(viewport.transform, value, 16f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            text.overflowMode = TextOverflowModes.Overflow;
            input.targetGraphic = root.GetComponent<Image>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.interactable = true;
            input.readOnly = false;
            input.caretColor = Color.black;
            input.caretWidth = 2;
            input.caretBlinkRate = .85f;
            var selection = _style.ButtonColor;
            selection.a = .35f;
            input.selectionColor = selection;
            input.SetTextWithoutNotify(value);
            return input;
        }

        private Scrollbar InstallDiseaseNativeScrollbar(RectTransform host, ScrollRect scroll,
            string name, float top, float bottom)
        {
            var source = _settingsDiseaseNativeScrollbarTemplate;
            if (host == null || scroll == null || source == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Enfermedades: no se encontro el Scrollbar vertical nativo de Ajustes.");
                return null;
            }

            var scrollbar = Instantiate(source, host, false);
            scrollbar.gameObject.name = name;
            scrollbar.gameObject.SetActive(true);
            scrollbar.transform.localScale = Vector3.one;
            DisableLocalisers(scrollbar.transform);
            var layout = scrollbar.GetComponent<LayoutElement>();
            if (layout != null) layout.enabled = false;
            scrollbar.onValueChanged.RemoveAllListeners();
            scrollbar.interactable = true;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            ConfigureDiseaseNativeScrollbar(scrollbar, scroll);

            var sourceRect = source.transform as RectTransform;
            var width = Mathf.Clamp(sourceRect != null ? sourceRect.rect.width : 26f,
                22f, 34f);
            var rect = scrollbar.transform as RectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, .5f);
            rect.offsetMin = new Vector2(-width - 2f, bottom);
            rect.offsetMax = new Vector2(-2f, -top);
            scrollbar.transform.SetAsLastSibling();

            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 0f;
            if (scroll.viewport != null)
            {
                var maximum = scroll.viewport.offsetMax;
                maximum.x = -width - 4f;
                scroll.viewport.offsetMax = maximum;
            }
            return scrollbar;
        }

        private static void ConfigureDiseaseNativeScrollbar(Scrollbar scrollbar,
            ScrollRect scroll)
        {
            if (scrollbar == null || scroll == null) return;
            scrollbar.gameObject.SetActive(true);
            scrollbar.interactable = true;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handleGraphic = scrollbar.handleRect == null
                ? null : scrollbar.handleRect.GetComponent<Graphic>();
            if (handleGraphic != null)
            {
                handleGraphic.enabled = true;
                handleGraphic.color = Color.white;
                scrollbar.targetGraphic = handleGraphic;
            }
            var colors = scrollbar.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.82f, .82f, .82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.72f, .72f, .72f, 1f);
            colors.colorMultiplier = 1f;
            scrollbar.colors = colors;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 0f;
        }

        private Toggle CreateDiseaseToggle(Transform parent, bool value, Action<bool> changed)
        {
            var native = _settingsDiseaseNativeToggleTemplate;
            if (native == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Enfermedades: no se encontro el Toggle nativo de Ajustes.");
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
            if (options.Count > 7)
                InstallDiseaseNativeScrollbar(templateRect, scroll,
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
            _settingsDiseaseNativeScrollTemplate = null;
            _settingsDiseaseNativeScrollbarTemplate = null;
            _settingsDiseaseNativeToggleTemplate = null;
            _settingsDiseasesLoaded = false;
        }
    }
}
