// Updated: 2026-11-04 - Enfermedades queda contenida dentro de su propia pestana; sin superficie hermana.
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
        private Scrollbar _settingsDiseaseNativeScrollbarTemplate;
        private Toggle _settingsDiseaseNativeToggleTemplate;
        private bool _settingsDiseasesLoaded;
        private string _settingsDiseaseLanguage;
        private Sprite _settingsDiseaseStarFullSprite;
        private Sprite _settingsDiseaseStarEmptySprite;
        private bool _settingsDiseaseStarSpritesSearched;

        // Panel superior de pacientes. De momento mantiene un borrador local
        // cargado desde CharacterManager.Config para poder terminar su diseño
        // antes de fijar el formato definitivo del mapa.
        private GameObject _settingsPatientPanel;
        private bool _settingsPatientValuesLoaded;
        private int _settingsMaxPatients = 250;
        private float _settingsPatientSpawnRate = 48f;
        private bool _settingsNeverSpawnPatients;
        private bool _settingsUsePatientSelector = true;
        private float _settingsPatientSelectionRandomJitter = .35f;

        private sealed class DiseaseChoice
        {
            internal string Id;
            internal string Name;
            internal Sprite DlcIcon;
        }

        private void BuildDiseaseSettingsContents()
        {
            if (_settingsPages == null || _settingsPages.Length < 2 ||
                _settingsPages[1] == null) return;

            var page = _settingsPages[1].transform as RectTransform;
            if (page == null) return;

            var nativeScroll = page.GetComponentInChildren<ScrollRect>(true);
            if (nativeScroll != null)
                nativeScroll.gameObject.SetActive(false);

            // Raiz transparente propia de Enfermedades.
            // Todo lo creado por este CS queda dentro de su pestana.
            var surface = new GameObject(
                "UP Disease Scroll",
                typeof(RectTransform),
                typeof(ScrollRect));
            surface.transform.SetParent(page, false);

            _settingsDiseaseSurface =
                (RectTransform)surface.transform;

            Stretch(_settingsDiseaseSurface);
            _settingsDiseaseSurface.offsetMin =
                new Vector2(10f, 10f);
            _settingsDiseaseSurface.offsetMax =
                new Vector2(-10f, -10f);
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
            _settingsDiseaseScroll.inertia = true;
            _settingsDiseaseScroll.movementType = ScrollRect.MovementType.Clamped;
            _settingsDiseaseScroll.scrollSensitivity = 120f;
            InstallDiseaseNativeScrollbar(_settingsDiseaseSurface, _settingsDiseaseScroll,
                "UP Disease Native Scrollbar", 0f, 0f);
            _settingsDiseaseSurface.SetAsLastSibling();
        }

        private void RefreshDiseaseSettingsContents()
        {
            if (_settingsDiseaseContent == null || _level == null) return;
            var language = EditorLocalization.CurrentLanguageCode();
            if (_settingsDiseasesLoaded)
            {
                if (!string.Equals(language, _settingsDiseaseLanguage,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _settingsDiseaseLanguage = language;
                    ScenarioDiseaseCatalog.SortLocalized(_settingsDiseaseCatalog);
                    RebuildDiseaseSettingsContents();
                }
                return;
            }
            LoadPatientSettingsFromLevel();
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
            _settingsDiseaseLanguage = language;
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
            if (_settingsPatientPanel != null)
            {
                _settingsPatientPanel.SetActive(false);
                Destroy(_settingsPatientPanel);
                _settingsPatientPanel = null;
            }

            var top = 12f;
            _settingsPatientPanel = BuildPatientSettingsPanel(top);
            if (_settingsPatientPanel != null)
                top += ((RectTransform)_settingsPatientPanel.transform).sizeDelta.y + 10f;

            foreach (var room in _settingsDiseaseCatalog.Rooms)
            {
                if (!_settingsDiseaseExpanded.ContainsKey(room.Id))
                    _settingsDiseaseExpanded[room.Id] = true;
                var section = BuildDiseaseRoomSection(room, top);
                _settingsDiseaseSections.Add(section);
                top += ((RectTransform)section.transform).sizeDelta.y;
            }
            _settingsDiseaseContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(430f, top));
            LayoutRebuilder.ForceRebuildLayoutImmediate(_settingsDiseaseContent);
            ReapplySettingsDesignerStructure();
            RestoreDiseaseSettingsParent();
        }

        // El layout persistido puede contener una relacion antigua que movia
        // esta raiz al panel comun. Enfermedades siempre pertenece a su pagina.
        private void RestoreDiseaseSettingsParent()
        {
            if (_settingsDiseaseSurface == null ||
                _settingsPages == null ||
                _settingsPages.Length < 2 ||
                _settingsPages[1] == null)
                return;

            var page = _settingsPages[1].transform;

            if (_settingsDiseaseSurface.parent != page)
                _settingsDiseaseSurface.SetParent(page, false);

            Stretch(_settingsDiseaseSurface);
            _settingsDiseaseSurface.offsetMin =
                new Vector2(10f, 10f);
            _settingsDiseaseSurface.offsetMax =
                new Vector2(-10f, -10f);
            _settingsDiseaseSurface.SetAsLastSibling();
        }

        private void LoadPatientSettingsFromLevel()
        {
            if (_settingsPatientValuesLoaded) return;
            _settingsPatientValuesLoaded = true;

            var config = _level?.Config?.GetCharacterManagerConfig();
            if (config == null) return;

            _settingsMaxPatients = ReadPatientConfigInt(
                config, "_maxPatients", _settingsMaxPatients);
            _settingsPatientSpawnRate = ReadPatientConfigFloat(
                config, "_patientSpawnRate", _settingsPatientSpawnRate);
            _settingsNeverSpawnPatients = ReadPatientConfigBool(
                config, "NeverSpawnPatients", _settingsNeverSpawnPatients);
            _settingsUsePatientSelector = ReadPatientConfigBool(
                config, "_usePatientSelector", _settingsUsePatientSelector);
            _settingsPatientSelectionRandomJitter = ReadPatientConfigFloat(
                config, "_patientSelectionRandomJitter",
                _settingsPatientSelectionRandomJitter);
        }

        private static int ReadPatientConfigInt(
            CharacterManager.Config config, string fieldName, int fallback)
        {
            var field = AccessTools.Field(typeof(CharacterManager.Config), fieldName);
            if (field == null || config == null) return fallback;
            var value = field.GetValue(config);
            return value is int result ? result : fallback;
        }

        private static float ReadPatientConfigFloat(
            CharacterManager.Config config, string fieldName, float fallback)
        {
            var field = AccessTools.Field(typeof(CharacterManager.Config), fieldName);
            if (field == null || config == null) return fallback;
            var value = field.GetValue(config);
            if (value is float result) return result;
            if (value is double doubleValue) return (float)doubleValue;
            return fallback;
        }

        private static bool ReadPatientConfigBool(
            CharacterManager.Config config, string fieldName, bool fallback)
        {
            var field = AccessTools.Field(typeof(CharacterManager.Config), fieldName);
            if (field == null || config == null) return fallback;
            var value = field.GetValue(config);
            return value is bool result ? result : fallback;
        }

        private GameObject BuildPatientSettingsPanel(float top)
        {
            var panel = NewImage(
                "UP Patient Settings",
                _settingsDiseaseContent,
                _style.ButtonSprite,
                SettingsSharedPageColor,
                true);
            var panelRect = (RectTransform)panel.transform;
            SetRect(panelRect,
                new Vector2(.01f, 1f), new Vector2(.99f, 1f),
                new Vector2(.5f, 1f),
                new Vector2(0f, -top),
                new Vector2(0f, 108f));

            var header = NewImage(
                "Cabecera",
                panel.transform,
                _style.ButtonSprite,
                _style.HeaderColor,
                false);
            SetRect((RectTransform)header.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(.5f, 1f),
                Vector2.zero,
                new Vector2(-8f, 42f));

            var title = CreateDiseaseText(
                header.transform,
                "Pacientes",
                22f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            title.fontStyle = FontStyles.Bold;
            SetTopLeft(title.rectTransform, 16f, 0f, 240f, 42f);

            CreatePatientIntegerField(
                panel.transform,
                "Máx. pacientes",
                _settingsMaxPatients,
                16f, 135f,
                154f, 65f,
                value => _settingsMaxPatients = Mathf.Max(1, value),
                "UP Patient Max Patients");

            CreatePatientFloatField(
                panel.transform,
                "Ritmo de llegada",
                _settingsPatientSpawnRate,
                286f, 140f,
                431f, 60f,
                value => _settingsPatientSpawnRate = Mathf.Max(0f, value),
                "UP Patient Spawn Rate");

            CreatePatientFloatField(
                panel.transform,
                "Aleatoriedad",
                _settingsPatientSelectionRandomJitter,
                556f, 121f,
                668f, 61f,
                value => _settingsPatientSelectionRandomJitter =
                    Mathf.Clamp01(value),
                "UP Patient Selection Jitter");

            var neverLabel = CreateDiseaseText(
                panel.transform,
                "No generar pacientes",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(neverLabel.rectTransform, 781f, 57f, 180f, 38f);

            var never = CreateDiseaseToggle(
                panel.transform,
                _settingsNeverSpawnPatients,
                value => _settingsNeverSpawnPatients = value);
            if (never != null)
            {
                never.gameObject.name = "UP Patient Never Spawn Toggle";
                SetTopLeft((RectTransform)never.transform, 1022f, 61f, 30f, 30f);
                ApplyPatientToggleDesignerLayout(never, new Vector2(-60f, 0f));
            }

            var selectorLabel = CreateDiseaseText(
                panel.transform,
                "Selector ponderado",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(selectorLabel.rectTransform, 1036f, 57f, 160f, 38f);

            var selector = CreateDiseaseToggle(
                panel.transform,
                _settingsUsePatientSelector,
                value => _settingsUsePatientSelector = value);
            if (selector != null)
            {
                selector.gameObject.name = "UP Patient Selector Toggle";
                SetTopLeft((RectTransform)selector.transform, 1225f, 61f, 30f, 30f);
                ApplyPatientToggleDesignerLayout(selector, new Vector2(-16f, 0f));
            }

            return panel;
        }

        private static void ApplyPatientToggleDesignerLayout(
            Toggle toggle, Vector2 backgroundPosition)
        {
            if (toggle == null) return;
            var background = toggle.transform.Find("Background") as RectTransform;
            if (background == null) return;

            background.anchorMin = new Vector2(.5f, .5f);
            background.anchorMax = new Vector2(.5f, .5f);
            background.pivot = new Vector2(.5f, .5f);
            background.anchoredPosition = backgroundPosition;
            background.sizeDelta = new Vector2(40f, 40f);

            var checkmark = background.Find("Checkmark") as RectTransform;
            if (checkmark == null && toggle.graphic != null)
                checkmark = toggle.graphic.rectTransform;
            if (checkmark == null) return;

            checkmark.anchorMin = Vector2.zero;
            checkmark.anchorMax = Vector2.one;
            checkmark.pivot = new Vector2(.5f, .5f);
            checkmark.anchoredPosition = Vector2.zero;
            checkmark.sizeDelta = new Vector2(-10f, -10f);
        }

        private void CreatePatientIntegerField(
            Transform parent, string labelText, int current,
            float labelX, float labelWidth,
            float inputX, float inputWidth,
            Action<int> setter, string objectName)
        {
            var label = CreateDiseaseText(
                parent, labelText, 17f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            SetTopLeft(label.rectTransform, labelX, 57f, labelWidth, 38f);

            var input = CreateDiseaseInput(
                parent, current.ToString(CultureInfo.InvariantCulture));
            if (input == null) return;
            input.gameObject.name = objectName;
            SetTopLeft((RectTransform)input.transform, inputX, 58f, inputWidth, 36f);
            input.onEndEdit.AddListener(text =>
            {
                if (int.TryParse(text, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsed))
                {
                    parsed = Mathf.Max(1, parsed);
                    setter(parsed);
                    input.SetTextWithoutNotify(
                        parsed.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    input.SetTextWithoutNotify(
                        current.ToString(CultureInfo.InvariantCulture));
                }
            });
        }

        private void CreatePatientFloatField(
            Transform parent, string labelText, float current,
            float labelX, float labelWidth,
            float inputX, float inputWidth,
            Action<float> setter, string objectName)
        {
            var label = CreateDiseaseText(
                parent, labelText, 17f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            SetTopLeft(label.rectTransform, labelX, 57f, labelWidth, 38f);

            var input = CreatePatientDecimalInput(
                parent, current.ToString("0.###", CultureInfo.InvariantCulture));
            if (input == null) return;
            input.gameObject.name = objectName;
            SetTopLeft((RectTransform)input.transform, inputX, 58f, inputWidth, 36f);
            input.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var parsed))
                {
                    setter(parsed);
                }
                else
                {
                    input.SetTextWithoutNotify(
                        current.ToString("0.###", CultureInfo.InvariantCulture));
                }
            });
        }

        private TMP_InputField CreatePatientDecimalInput(
            Transform parent, string value)
        {
            var root = NewImage(
                "Entrada decimal",
                parent,
                _style.ButtonSprite,
                new Color32(218, 222, 226, 255),
                true);
            var input = root.AddComponent<TMP_InputField>();

            var viewport = new GameObject(
                "Text Area",
                typeof(RectTransform),
                typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(8f, 1f);
            viewportRect.offsetMax = new Vector2(-8f, -1f);

            var text = CreateDiseaseText(
                viewport.transform,
                value,
                16f,
                TextAlignmentOptions.MidlineLeft,
                Color.black);
            text.overflowMode = TextOverflowModes.Overflow;

            input.targetGraphic = root.GetComponent<Image>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
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

        private GameObject BuildDiseaseRoomSection(ScenarioDiseaseCatalog.Room room, float top)
        {
            var values = _settingsIllnesses.Where(value => value.room == room.Id).ToList();
            var expanded = _settingsDiseaseExpanded[room.Id];
            var height = 76f + (expanded ? values.Count * 64f : 0f);
            var section = NewImage("UP Disease Room " + room.Id, _settingsDiseaseContent,
                _style.ButtonSprite, SettingsSharedPageColor, true);
            var sectionRect = (RectTransform)section.transform;
            SetRect(sectionRect, new Vector2(.01f, 1f), new Vector2(.99f, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, height));
            BuildDiseaseRoomHeader(section.transform, room, expanded);
            if (expanded)
                for (var index = 0; index < values.Count; ++index)
                    BuildDiseaseRow(section.transform, room, values[index], 68f + index * 64f);
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
            replacement.transform.SetSiblingIndex(index +
                (_settingsPatientPanel != null ? 1 : 0));
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
            ReapplySettingsDesignerStructure();
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
                { Id = disease.Id, Name = disease.Name, DlcIcon = disease.Dlc?.Icon }));
            var selector = CreateDiseaseDropdown(header.transform,
                choices.Select(choice => choice.Name).ToList(), 0,
                choices.Select(choice => choice.DlcIcon).ToList());
            SetTopLeft((RectTransform)selector.transform, 350f, 8f, 274f, 44f);

            var add = CreateDiseaseButton(header.transform,
                EditorLocalization.Get("editor.scenario_settings.diseases.add"),
                new Color32(15, 177, 77, 255), () =>
                {
                    if (selector.value <= 0 || selector.value >= choices.Count) return;
                    AddDisease(room, choices[selector.value].Id);
                });
            SetTopLeft((RectTransform)add.transform, 639f, 8f, 116f, 44f);

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
                SettingsSharedPageColor, false);
            var rect = (RectTransform)panel.transform;
            SetRect(rect, new Vector2(.01f, 1f), new Vector2(.99f, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, 56f));

            _settingsDiseaseCatalog.ById.TryGetValue(value.definition ?? "", out var selected);

            var remove = CreateDiseaseButton(panel.transform,
                "X",
                new Color32(232, 38, 46, 255), () =>
                {
                    _settingsIllnesses.Remove(value);
                    RebuildDiseaseSettingsContents();
                });
            SetTopRight((RectTransform)remove.transform, 1186f, 9f, 41f, 38f);

            var available = AvailableForRoom(room, value.definition);
            var choices = available.Select(disease => new DiseaseChoice
                { Id = disease.Id, Name = disease.Name, DlcIcon = disease.Dlc?.Icon }).ToList();
            var currentIndex = Mathf.Max(0,
                choices.FindIndex(choice => choice.Id == value.definition));
            var definition = CreateDiseaseDropdown(panel.transform,
                choices.Select(choice => choice.Name).ToList(), currentIndex,
                choices.Select(choice => choice.DlcIcon).ToList());
            SetTopLeft((RectTransform)definition.transform, 66f, 8f, 237f, 38f);
            ApplyDiseaseDropdownDesignerLayout(definition);
            definition.onValueChanged.AddListener(index =>
            {
                if (index < 0 || index >= choices.Count) return;
                value.definition = choices[index].Id;
                RebuildDiseaseSettingsContents();
            });

            var unlockedLabel = CreateDiseaseText(
                panel.transform,
                "Desbloqueada:",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(unlockedLabel.rectTransform, 326f, 11f, 125f, 38f);

            var unlocked = CreateDiseaseToggle(panel.transform, value.unlocked,
                state => value.unlocked = state);
            if (unlocked != null)
            {
                SetTopLeft((RectTransform)unlocked.transform, 1140f, 12f, 30f, 30f);
                ApplyDiseaseUnlockedToggleDesignerLayout(unlocked);
            }

            var frequencyLabel = CreateDiseaseText(
                panel.transform,
                "Frecuencia:",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(frequencyLabel.rectTransform, 542f, 10f, 111f, 38f);

            CreateDiseaseIntegerInput(
                panel.transform,
                value.minWeight,
                638f, 9f, 46f,
                changed => value.minWeight = changed);

            var frequencySeparator = CreateDiseaseText(
                panel.transform,
                "   >",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(frequencySeparator.rectTransform, 684f, 8f, 36f, 38f);

            CreateDiseaseIntegerInput(
                panel.transform,
                value.maxWeight,
                712f, 9f, 48f,
                changed => value.maxWeight = changed);

            var patientsLabel = CreateDiseaseText(
                panel.transform,
                "Pacientes previos:",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(patientsLabel.rectTransform, 817f, 10f, 156f, 38f);

            CreateDiseaseIntegerInput(
                panel.transform,
                value.minPatientsSpawned,
                965f, 8f, 40f,
                changed => value.minPatientsSpawned = changed);

            CreateDiseaseStarRatingDesigner(panel.transform, value);

            if (selected?.Dlc?.Icon != null)
            {
                var dlcIcon = NewImage("Icono DLC enfermedad", panel.transform,
                    selected.Dlc.Icon, Color.white, false);
                SetTopRight((RectTransform)dlcIcon.transform, 10f, 8f, 38f, 38f);
                dlcIcon.GetComponent<Image>().preserveAspect = true;
            }
        }

        private static void ApplyDiseaseDropdownDesignerLayout(TMP_Dropdown dropdown)
        {
            if (dropdown?.captionText == null) return;
            var caption = dropdown.captionText.rectTransform;
            caption.anchorMin = Vector2.zero;
            caption.anchorMax = Vector2.one;
            caption.pivot = new Vector2(.5f, .5f);
            caption.anchoredPosition = new Vector2(-29f, 0f);
            caption.sizeDelta = new Vector2(-78f, 0f);
        }

        private static void ApplyDiseaseUnlockedToggleDesignerLayout(Toggle toggle)
        {
            if (toggle == null) return;

            var background = toggle.transform.Find("Background") as RectTransform;
            if (background == null) return;

            background.anchorMin = new Vector2(.5f, .5f);background.anchorMax = new Vector2(.5f, .5f);
            background.pivot = new Vector2(.5f, .5f);
            background.anchoredPosition = new Vector2(-679f, -1f);
            background.sizeDelta = new Vector2(40f, 40f);

            var checkmark = background.Find("Checkmark") as RectTransform;
            if (checkmark == null && toggle.graphic != null)
                checkmark = toggle.graphic.rectTransform;
            if (checkmark == null) return;

            checkmark.anchorMin = Vector2.zero;
            checkmark.anchorMax = Vector2.one;
            checkmark.pivot = new Vector2(.5f, .5f);
            checkmark.anchoredPosition = new Vector2(1f, 0f);
            checkmark.sizeDelta = new Vector2(-10f, -10f);
        }

        private TMP_InputField CreateDiseaseIntegerInput(
            Transform parent, int current,
            float inputX, float inputY, float inputWidth,
            Action<int> setter)
        {
            var input = CreateDiseaseInput(parent,
                current.ToString(CultureInfo.InvariantCulture));
            if (input == null) return null;

            SetTopLeft((RectTransform)input.transform,
                inputX, inputY, inputWidth, 36f);
            input.onEndEdit.AddListener(text =>
            {
                if (int.TryParse(text, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsed))
                {
                    setter(parsed);
                }
                else
                {
                    input.SetTextWithoutNotify(
                        current.ToString(CultureInfo.InvariantCulture));
                }
            });
            return input;
        }

        private void CreateDiseaseStarRatingDesigner(
            Transform parent, ScenarioMapFormat.WeightedIllnessData value)
        {
            var starsLabel = CreateDiseaseText(
                parent,
                "Estrellas:",
                17f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            SetTopLeft(starsLabel.rectTransform, 1034f, 10f, 87f, 38f);

            value.minStarRating = Mathf.Clamp(value.minStarRating, 0, 3);
            if (!ResolveNativeDiseaseStarSprites()) return;

            var starX = new[] { 1117f, 1155f, 1193f };
            var starY = new[] { 10f, 11f, 10f };
            var images = new Image[3];

            for (var index = 0; index < images.Length; ++index)
            {
                var rating = index + 1;
                var star = NewImage("Estrella minima " + rating, parent,
                    rating <= value.minStarRating
                        ? _settingsDiseaseStarFullSprite
                        : _settingsDiseaseStarEmptySprite,
                    Color.white, true);
                var image = star.GetComponent<Image>();
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                images[index] = image;

                SetTopLeft((RectTransform)star.transform,
                    starX[index], starY[index], 34f, 34f);

                var button = star.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    var next = value.minStarRating == rating ? 0 : rating;
                    value.minStarRating = next;
                    RefreshDiseaseStarRating(images, next);
                });
                StandardiseClickSound(button);
            }
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

        private void RefreshDiseaseStarRating(IList<Image> images, int rating)
        {
            if (images == null) return;
            for (var index = 0; index < images.Count; ++index)
                if (images[index] != null)
                    images[index].overrideSprite = index < rating
                        ? _settingsDiseaseStarFullSprite : _settingsDiseaseStarEmptySprite;
        }

        private bool ResolveNativeDiseaseStarSprites()
        {
            if (_settingsDiseaseStarFullSprite != null &&
                _settingsDiseaseStarEmptySprite != null) return true;
            if (_settingsDiseaseStarSpritesSearched) return false;
            _settingsDiseaseStarSpritesSearched = true;

            foreach (var source in Resources.FindObjectsOfTypeAll<HospitalStarIcons>())
            {
                if (source == null) continue;
                var full = AccessTools.Field(typeof(HospitalStarIcons), "_starSprite")
                    ?.GetValue(source) as Sprite;
                var empty = AccessTools.Field(typeof(HospitalStarIcons), "_starHolderSprite")
                    ?.GetValue(source) as Sprite;
                if (full == null || empty == null) continue;
                _settingsDiseaseStarFullSprite = full;
                _settingsDiseaseStarEmptySprite = empty;
                return true;
            }

            foreach (var source in Resources.FindObjectsOfTypeAll<StarIcons>())
            {
                if (source == null) continue;
                var full = AccessTools.Field(typeof(StarIcons), "_starFullSprite")
                    ?.GetValue(source) as Sprite;
                var empty = AccessTools.Field(typeof(StarIcons), "_starEmptySprite")
                    ?.GetValue(source) as Sprite;
                if (full == null || empty == null) continue;
                _settingsDiseaseStarFullSprite = full;
                _settingsDiseaseStarEmptySprite = empty;
                return true;
            }

            UnderPressureEditorPlugin.Log?.LogError(
                "Enfermedades: no se encontraron los sprites nativos de estrellas.");
            return false;
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

            // El componente y sus sprites siguen siendo los nativos. Solo se
            // fija el tinte claro solicitado para que el asa no desaparezca
            // visualmente cuando el control no tiene el foco.
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

            var sourceRect = source.transform as RectTransform;
            var width = Mathf.Clamp(sourceRect != null ? sourceRect.rect.width : 30f,
                30f, 40f);
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
            int selected, IList<Sprite> icons = null)
        {
            var root = NewImage("Selector", parent, _style.ButtonSprite,
                new Color32(218, 222, 226, 255), true);
            var dropdown = root.AddComponent<TMP_Dropdown>();
            var caption = CreateDiseaseText(root.transform, "", 16f,
                TextAlignmentOptions.MidlineLeft, Color.black);
            caption.rectTransform.offsetMin = new Vector2(10f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-68f, 0f);
            var captionIconObject = NewImage("Icono DLC seleccionado", root.transform,
                null, Color.white, false);
            var captionIcon = captionIconObject.GetComponent<Image>();
            captionIcon.preserveAspect = true;
            var captionIconRect = (RectTransform)captionIconObject.transform;
            captionIconRect.anchorMin = new Vector2(1f, .5f);
            captionIconRect.anchorMax = new Vector2(1f, .5f);
            captionIconRect.pivot = new Vector2(1f, .5f);
            captionIconRect.sizeDelta = new Vector2(26f, 26f);
            captionIconRect.anchoredPosition = new Vector2(-34f, 0f);
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
            scroll.scrollSensitivity = 800f;

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
            itemLabel.rectTransform.offsetMax = new Vector2(-42f, 0f);
            var itemIconObject = NewImage("Icono DLC opcion", item.transform,
                null, Color.white, false);
            var itemIcon = itemIconObject.GetComponent<Image>();
            itemIcon.preserveAspect = true;
            var itemIconRect = (RectTransform)itemIconObject.transform;
            itemIconRect.anchorMin = new Vector2(1f, .5f);
            itemIconRect.anchorMax = new Vector2(1f, .5f);
            itemIconRect.pivot = new Vector2(1f, .5f);
            itemIconRect.sizeDelta = new Vector2(26f, 26f);
            itemIconRect.anchoredPosition = new Vector2(-8f, 0f);

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            if (options.Count > 7)
                InstallDiseaseNativeScrollbar(templateRect, scroll,
                    "UP Disease Dropdown Native Scrollbar", 2f, 2f);
            dropdown.targetGraphic = root.GetComponent<Image>();
            dropdown.template = templateRect;
            dropdown.captionText = caption;
            dropdown.captionImage = captionIcon;
            dropdown.itemText = itemLabel;
            dropdown.itemImage = itemIcon;
            dropdown.options = options.Select((value, index) =>
                new TMP_Dropdown.OptionData(value,
                    icons != null && index < icons.Count ? icons[index] : null)).ToList();
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
            _settingsPatientPanel = null;
            _settingsPatientValuesLoaded = false;
            _settingsMaxPatients = 250;
            _settingsPatientSpawnRate = 48f;
            _settingsNeverSpawnPatients = false;
            _settingsUsePatientSelector = true;
            _settingsPatientSelectionRandomJitter = .35f;
            _settingsDiseaseCatalog = null;
            _settingsDiseaseSurface = null;
            _settingsDiseaseContent = null;
            _settingsDiseaseScroll = null;
            _settingsDiseaseNativeScrollbarTemplate = null;
            _settingsDiseaseNativeToggleTemplate = null;
            _settingsDiseaseLanguage = null;
            _settingsDiseaseStarFullSprite = null;
            _settingsDiseaseStarEmptySprite = null;
            _settingsDiseaseStarSpritesSearched = false;
            _settingsDiseasesLoaded = false;
        }
    }
}
