// Updated: 2026-11-11 - Recompensas completas; filtros Tutorial/DLCs; checkbox nativo para Hospital publico y filtros.
using System;
using System.Collections.Generic;
using System.Linq;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private const int ObjectiveTabIndex = 2;
        private const float ObjectiveLeftWidth = 238f;
        private const float ObjectiveGap = 10f;
        private static readonly Color ObjectivePanelColor =
            new Color(.105882354f, .407843143f, .6f, 1f);
        private static readonly Color ObjectiveRaisedPanelColor =
            new Color(.16f, .49f, .69f, 1f);

        private static readonly string[] ObjectiveRequirementTypes =
            ScenarioObjectiveRequirements.Types;

        private static readonly string[] ObjectiveResultTypes =
        {
            "money",
            "reputation",
            "research_points",
            "kudosh",
            "unlock_room",
            "unlock_item",
            "unlock_item_upgrade"
        };

        private RectTransform _objectiveContent;
        private RectTransform _objectiveRoot;
        private RectTransform _objectiveListBlock;
        private RectTransform _objectiveConfigurationBlock;
        private RectTransform _objectiveHeaderBlock;
        private RectTransform _objectiveStarPanel;
        private RectTransform _objectiveChallengePanel;
        private TMP_Dropdown _settingsNativeDropdownTemplate;
        private ScrollRect _objectiveChallengeScroll;
        private RectTransform _objectiveChallengeContent;
        private RectTransform _objectiveRequirementContent;
        private ScrollRect _objectiveRequirementScroll;
        private TMP_Dropdown _objectiveRequirementTypeDropdown;
        private readonly RectTransform[] _objectiveResultContents =
            new RectTransform[3];
        private readonly ScrollRect[] _objectiveResultScrolls =
            new ScrollRect[3];
        private readonly TMP_Dropdown[] _objectiveResultTypeDropdowns =
            new TMP_Dropdown[3];
        private readonly Button[] _objectiveResultAddButtons =
            new Button[3];
        private TMP_Text _objectiveSelectionTitle;
        private TMP_InputField _objectiveDurationInput;
        private TMP_InputField _objectiveProbabilityInput;
        private TMP_InputField _objectiveDifficultyInput;
        private TMP_InputField _objectiveCooldownInput;
        private Toggle _objectivePublicHospitalToggle;
        private Toggle _objectiveTutorialRequirementsToggle;
        private Toggle _objectiveDlcRequirementsToggle;
        private bool _objectiveShowTutorialRequirements;
        private bool _objectiveShowDlcRequirements;
        private readonly List<ScenarioMapFormat.ObjectiveData> _objectiveDrafts =
            new List<ScenarioMapFormat.ObjectiveData>();
        private int _selectedObjective;
        private bool _objectiveBuilt;

        internal void BuildObjectiveSettingsContents()
        {
            _objectiveContent = null;
            _objectiveRoot = null;
            _objectiveBuilt = false;
            _objectiveDrafts.Clear();
            _selectedObjective = 0;
            _objectiveTutorialRequirementsToggle = null;
            _objectiveDlcRequirementsToggle = null;
            _objectiveShowTutorialRequirements = false;
            _objectiveShowDlcRequirements = false;
            for (var index = 0; index < 3; ++index)
            {
                _objectiveResultContents[index] = null;
                _objectiveResultScrolls[index] = null;
                _objectiveResultTypeDropdowns[index] = null;
                _objectiveResultAddButtons[index] = null;
            }

            if (_settingsPages == null || _settingsPages.Length <= ObjectiveTabIndex ||
                _settingsPages[ObjectiveTabIndex] == null) return;

            var page = _settingsPages[ObjectiveTabIndex].transform as RectTransform;
            if (page == null) return;
            var nativeScroll = page.GetComponentInChildren<ScrollRect>(true);
            if (nativeScroll != null) nativeScroll.gameObject.SetActive(false);
            _objectiveContent = page;
        }

        internal void RefreshObjectiveSettingsContents()
        {
            if (_objectiveContent == null) return;
            EnsureObjectiveDrafts();
            if (!_objectiveBuilt) BuildObjectiveUi();
            RefreshObjectiveList();
            RefreshObjectiveConfiguration();
        }

        private void EnsureObjectiveDrafts()
        {
            if (_objectiveDrafts.Count != 0) return;
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            _objectiveDrafts.AddRange(
                ScenarioEditorStorage.CloneObjectives(settings.objectives));

            for (var star = 1; star <= 3; ++star)
            {
                var found = _objectiveDrafts.FirstOrDefault(value =>
                    value.isStar && value.starIndex == star);
                if (found != null) continue;
                _objectiveDrafts.Insert(star - 1, CreateObjectiveDraft(true, star));
            }
            _objectiveDrafts.Sort((left, right) =>
            {
                if (left.isStar != right.isStar) return left.isStar ? -1 : 1;
                return left.isStar ? left.starIndex.CompareTo(right.starIndex) : 0;
            });
            SaveObjectiveDrafts();
        }

        private static ScenarioMapFormat.ObjectiveData CreateObjectiveDraft(
            bool star, int starIndex = 0)
        {
            return new ScenarioMapFormat.ObjectiveData
            {
                id = star ? "star_" + starIndex : Guid.NewGuid().ToString("N"),
                name = "",
                isStar = star,
                starIndex = starIndex,
                durationDays = "30",
                probability = "1",
                difficulty = "0",
                cooldownSeconds = "20"
            };
        }

        private void SaveObjectiveDrafts()
        {
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            settings.objectivesConfigured = true;
            settings.objectives = ScenarioEditorStorage.CloneObjectives(
                _objectiveDrafts.ToArray());
            ScenarioEditorStorage.SetScenarioSettings(_level, settings);
        }

        private ScenarioMapFormat.ObjectiveData CurrentObjective =>
            _selectedObjective >= 0 && _selectedObjective < _objectiveDrafts.Count
                ? _objectiveDrafts[_selectedObjective] : null;

        private void BuildObjectiveUi()
        {
            _objectiveBuilt = true;
            Canvas.ForceUpdateCanvases();
            var pageHeight = _objectiveContent.rect.height > 100f
                ? _objectiveContent.rect.height : 650f;
            var rootObject = new GameObject("UP Objectives Root", typeof(RectTransform));
            rootObject.transform.SetParent(_objectiveContent, false);
            _objectiveRoot = rootObject.transform as RectTransform;
            Stretch(_objectiveRoot);
            _objectiveRoot.offsetMin = new Vector2(10f, 10f);
            _objectiveRoot.offsetMax = new Vector2(-10f, -10f);

            _objectiveListBlock = CreateObjectivePanel(
                "UP Objectives List", _objectiveRoot, SettingsOuterPanelColor);
            SetTopLeft(_objectiveListBlock, 0f, 0f, ObjectiveLeftWidth,
                pageHeight - 20f);

            _objectiveConfigurationBlock = CreateObjectivePanel(
                "UP Objective Configuration", _objectiveRoot, SettingsOuterPanelColor);
            _objectiveConfigurationBlock.anchorMin = new Vector2(0f, 0f);
            _objectiveConfigurationBlock.anchorMax = new Vector2(1f, 1f);
            _objectiveConfigurationBlock.offsetMin =
                new Vector2(ObjectiveLeftWidth + ObjectiveGap, 0f);
            _objectiveConfigurationBlock.offsetMax = Vector2.zero;

            BuildObjectiveListChildren(pageHeight - 20f);
            BuildObjectiveConfigurationChildren();
        }

        private void BuildObjectiveListChildren(float height)
        {
            _objectiveStarPanel = CreateObjectivePanel(
                "UP Objective Stars Panel", _objectiveListBlock,
                SettingsOuterPanelColor, true);
            SetTopLeft(_objectiveStarPanel, 6f, 6f,
                ObjectiveLeftWidth - 12f, 174f);

            CreateObjectiveLabel(
                _objectiveStarPanel,
                "Estrellas",
                14f, 0f, ObjectiveLeftWidth - 40f, 34f, 25f,
                TextAlignmentOptions.MidlineLeft);

            _objectiveChallengePanel = CreateObjectivePanel(
                "UP Objective Challenges Panel", _objectiveListBlock,
                ObjectivePanelColor, true);
            _objectiveChallengePanel.anchorMin = Vector2.zero;
            _objectiveChallengePanel.anchorMax = Vector2.one;
            _objectiveChallengePanel.offsetMin = new Vector2(8f, 58f);
            _objectiveChallengePanel.offsetMax = new Vector2(-8f, -189f);

            CreateObjectiveLabel(
                _objectiveChallengePanel,
                EditorLocalization.Get("editor.objectives.challenges"),
                11f, 11f, ObjectiveLeftWidth - 40f, 34f, 25f,
                TextAlignmentOptions.MidlineLeft);

            _objectiveChallengeScroll = CreateObjectiveVerticalScroll(
                _objectiveChallengePanel, "UP Objective Challenge Scroll",
                8f, 37f, 8f, 8f, out _objectiveChallengeContent);
            _objectiveChallengeScroll.scrollSensitivity = 44f;
            InstallNativeVerticalScrollbar(
                _objectiveChallengePanel,
                _objectiveChallengeScroll,
                "UP Objective Challenge Scrollbar",
                37f, 8f, false, 22f);
            _objectiveChallengeScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;

            CreateObjectiveButton(
                _objectiveListBlock,
                "UP Objective Add",
                EditorLocalization.Get("editor.objectives.new_challenge"),
                12f, height - 47f,
                164f, 36f, EmergencyAddColor, AddObjectiveChallenge);

            CreateObjectiveButton(
                _objectiveListBlock,
                "UP Objective Delete",
                "×",
                185f, height - 47f,
                40f, 36f, EmergencyRemoveColor, DeleteObjectiveChallenge);
        }

        private void BuildObjectiveConfigurationChildren()
        {
            _objectiveHeaderBlock = CreateObjectivePanel(
                "UP Objective Header", _objectiveConfigurationBlock,
                ObjectivePanelColor, true);
            SetObjectiveStretchRect(
                _objectiveHeaderBlock,
                0f, 8f, 719f, 434f);

            _objectiveSelectionTitle = CreateObjectiveLabel(
                _objectiveHeaderBlock,
                "",
                14f, 4f, -14f, 38f, 24f,
                TextAlignmentOptions.MidlineLeft,
                "UP Objective Selection Title");
            var objectiveSelectionTitleRect =
                _objectiveSelectionTitle.rectTransform;
            objectiveSelectionTitleRect.anchorMin =
                new Vector2(0f, 1f);
            objectiveSelectionTitleRect.anchorMax =
                new Vector2(1f, 1f);
            objectiveSelectionTitleRect.pivot =
                new Vector2(.5f, 1f);
            objectiveSelectionTitleRect.anchoredPosition =
                new Vector2(0f, -4f);
            objectiveSelectionTitleRect.sizeDelta =
                new Vector2(-28f, 38f);
            _objectiveSelectionTitle.color = Color.white;
            ConfigureObjectiveTmpText(
                _objectiveSelectionTitle,
                24f, 24f, 24f);

            _objectiveDurationInput = CreateObjectiveCompactField(
                _objectiveHeaderBlock,
                "editor.objectives.duration",
                46f,
                true);

            _objectiveProbabilityInput = CreateObjectiveCompactField(
                _objectiveHeaderBlock,
                "editor.objectives.probability",
                117f,
                false);

            _objectiveDifficultyInput = CreateObjectiveCompactField(
                _objectiveHeaderBlock,
                "editor.objectives.difficulty",
                186f,
                true);

            _objectiveCooldownInput = CreateObjectiveCompactField(
                _objectiveHeaderBlock,
                "editor.objectives.cooldown",
                255f,
                false);

            _objectivePublicHospitalToggle =
                CreateObjectivePublicHospitalToggle(
                    _objectiveHeaderBlock,
                    14f,
                    326f);

            var requirements = CreateObjectivePanel(
                "UP Objective Requirements",
                _objectiveConfigurationBlock,
                ObjectiveRaisedPanelColor,
                true);
            SetObjectiveStretchRect(
                requirements,
                364f, 7f, 4f, 436f);

            CreateObjectiveLabel(
                requirements,
                EditorLocalization.Get("editor.objectives.requirements"),
                10f, 5f, 280f, 38f, 24f,
                TextAlignmentOptions.MidlineLeft);

            _objectiveRequirementTypeDropdown =
                CreateObjectiveNativeDropdown(
                    requirements,
                    "UP Objective Requirement Type",
                    13f, 47f, 320f, 36f,
                    AvailableObjectiveRequirementTypes().Select(
                        ObjectiveRequirementLabel));

            CreateObjectiveButton(
                requirements,
                "UP Objective Requirement Add",
                "+",
                343f, 47f, 40f, 36f,
                EmergencyAddColor,
                AddObjectiveRequirement);

            _objectiveTutorialRequirementsToggle =
                CreateObjectiveRequirementFilterToggle(
                    requirements,
                    "UP Objective Tutorial Requirements Toggle",
                    "Tutorial:",
                    394f,
                    47f,
                    116f,
                    _objectiveShowTutorialRequirements,
                    value =>
                    {
                        _objectiveShowTutorialRequirements = value;
                        RefreshObjectiveRequirementTypeDropdown();
                        RefreshObjectiveRequirements();
                    });

            _objectiveDlcRequirementsToggle =
                CreateObjectiveRequirementFilterToggle(
                    requirements,
                    "UP Objective DLC Requirements Toggle",
                    "DLCs:",
                    520f,
                    47f,
                    96f,
                    _objectiveShowDlcRequirements,
                    value =>
                    {
                        _objectiveShowDlcRequirements = value;
                        RefreshObjectiveRequirementTypeDropdown();
                        RefreshObjectiveRequirements();
                    });

            _objectiveRequirementScroll =
                CreateObjectiveVerticalScroll(
                    requirements,
                    "UP Objective Requirements Scroll",
                    12f, 92f, 12f, 12f,
                    out _objectiveRequirementContent);
            _objectiveRequirementScroll.scrollSensitivity = 44f;

            InstallNativeVerticalScrollbar(
                requirements,
                _objectiveRequirementScroll,
                "UP Objective Requirements Scrollbar",
                92f, 12f, false, 12f);

            BuildObjectiveResultPanels(
                _objectiveConfigurationBlock);

            BindObjectiveInput(
                _objectiveDurationInput,
                value => CurrentObjective.durationDays = value);
            BindObjectiveInput(
                _objectiveProbabilityInput,
                value => CurrentObjective.probability = value);
            BindObjectiveInput(
                _objectiveDifficultyInput,
                value => CurrentObjective.difficulty = value);
            BindObjectiveInput(
                _objectiveCooldownInput,
                value => CurrentObjective.cooldownSeconds = value);
        }

        private RectTransform CreateObjectivePanel(string name, Transform parent, Color color,
            bool outlined = false)
        {
            var panel = NewImage(name, parent, _style.ButtonSprite, color, false);
            foreach (var outline in panel.GetComponents<Outline>())
                Destroy(outline);
            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.type = Image.Type.Sliced;
                image.raycastTarget = false;
            }
            if (outlined)
            {
                var outline = panel.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
            }
            return panel.transform as RectTransform;
        }

        private ScrollRect CreateObjectiveVerticalScroll(Transform parent, string name,
            float left, float top, float right, float bottom,
            out RectTransform content)
        {
            var root = NewImage(name, parent, null,
                new Color(0f, 0f, 0f, .001f), true);
            var rootRect = root.transform as RectTransform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(left, bottom);
            rootRect.offsetMax = new Vector2(-right, -top);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.elasticity = 0f;
            scroll.scrollSensitivity = 44f;

            var viewportObject = new GameObject("Viewport",
                typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(root.transform, false);
            var viewport = viewportObject.transform as RectTransform;
            Stretch(viewport);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            content = contentObject.transform as RectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        private TMP_InputField CreateObjectiveCompactField(
            Transform parent,
            string labelKey,
            float top,
            bool integer)
        {
            var rowObject = new GameObject(
                "UP Objective Compact Field " + labelKey,
                typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            var row = rowObject.transform as RectTransform;
            SetTopLeft(row, 14f, top, 174f, 58f);

            const float inputWidth = 58f;
            const float inputX = 116f;

            CreateObjectiveLabel(
                row,
                EditorLocalization.Get(labelKey),
                0f, 0f, 108f, 58f, 18f,
                TextAlignmentOptions.MidlineLeft);

            var input = CreateObjectiveInput(
                row,
                labelKey,
                inputX, 11f,
                inputWidth, 36f,
                "",
                integer);

            SetTopLeft(
                input.transform as RectTransform,
                inputX, 11f,
                inputWidth, 36f);

            if (!integer)
                input.contentType =
                    TMP_InputField.ContentType.DecimalNumber;

            input.characterLimit = 16;
            return input;
        }


        private TMP_Text CreateObjectiveLabel(
            Transform parent,
            string value,
            float x,
            float y,
            float width,
            float height,
            float size,
            TextAlignmentOptions alignment,
            string objectName = "Text")
        {
            var text = CreateEmergencyLabel(
                parent,
                value,
                x, y, width, height,
                size,
                alignment,
                objectName);

            ConfigureObjectiveTmpText(text, size, size, size);
            DisableObjectiveTextOutline(text);
            return text;
        }

        private Button CreateObjectiveButton(
            Transform parent,
            string name,
            string label,
            float x,
            float y,
            float width,
            float height,
            Color color,
            Action action)
        {
            var button = CreateEmergencyButton(
                parent,
                name,
                label,
                x, y, width, height,
                color,
                action);

            var buttonRect = button.transform as RectTransform;

            foreach (var text in
                     button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = Color.white;
                ConfigureObjectiveTmpText(text, 22f, 16f, 16f);
                SetObjectiveButtonTextRect(text.rectTransform, buttonRect);
                DisableObjectiveTextOutline(text);
            }

            foreach (var text in
                     button.GetComponentsInChildren<Text>(true))
            {
                text.fontStyle =
                    text.fontStyle == FontStyle.BoldAndItalic
                        ? FontStyle.Italic
                        : text.fontStyle == FontStyle.Bold
                            ? FontStyle.Normal
                            : text.fontStyle;
                text.resizeTextForBestFit = false;
                text.fontSize = 22;
                text.resizeTextMinSize = 16;
                text.resizeTextMaxSize = 16;
                SetObjectiveButtonTextRect(text.rectTransform, buttonRect);

                foreach (var outline in text.GetComponents<Outline>())
                {
                    if (outline != null)
                        outline.enabled = false;
                }
            }

            return button;
        }

        private TMP_InputField CreateObjectiveInput(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            string initialValue,
            bool integer)
        {
            var input = CreateEmergencyInput(
                parent,
                name,
                x, y, width, height,
                initialValue,
                integer);

            if (input == null)
                return null;

            if (input.textComponent != null)
            {
                var text = input.textComponent;
                var rect = text.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;

                text.color = Color.black;
                ConfigureObjectiveTmpText(text, 20f, 20f, 20f);
                DisableObjectiveTextOutline(text);
            }

            var placeholder = input.placeholder as TMP_Text;
            if (placeholder != null)
            {
                ConfigureObjectiveTmpText(placeholder, 20f, 20f, 20f);
                DisableObjectiveTextOutline(placeholder);
            }

            return input;
        }

        private static void ConfigureObjectiveTmpText(
            TMP_Text text,
            float size,
            float minSize,
            float maxSize)
        {
            if (text == null)
                return;

            text.fontStyle &= ~FontStyles.Bold;
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
        }

        private static void DisableObjectiveTextOutline(TMP_Text text)
        {
            if (text == null)
                return;

            foreach (var outline in text.GetComponents<Outline>())
            {
                if (outline != null)
                    outline.enabled = false;
            }
        }

        private static void SetObjectiveButtonTextRect(
            RectTransform textRect,
            RectTransform buttonRect)
        {
            if (textRect == null || buttonRect == null)
                return;

            var width = Mathf.Max(0f, buttonRect.rect.width - 6f);
            var height = Mathf.Max(0f, buttonRect.rect.height);
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(3f, 0f);
            textRect.sizeDelta = new Vector2(width, height);
        }

        private TMP_Dropdown CreateObjectiveNativeDropdown(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            IEnumerable<string> options)
        {
            var dropdown = CreateDiseaseDropdown(
                parent,
                (options ?? Enumerable.Empty<string>()).ToList(),
                0);
            if (dropdown == null)
                return null;

            dropdown.gameObject.name = name;
            SetTopLeft(
                dropdown.transform as RectTransform,
                x, y, width, height);

            foreach (var text in
                     dropdown.GetComponentsInChildren<TMP_Text>(true))
            {
                ConfigureObjectiveTmpText(
                    text,
                    20f, 20f, 20f);
                DisableObjectiveTextOutline(text);
            }

            if (dropdown.captionText != null)
            {
                var rect = dropdown.captionText.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(-29f, 0f);
                rect.sizeDelta = new Vector2(-78f, 0f);

                dropdown.captionText.color = Color.black;
                ConfigureObjectiveTmpText(
                    dropdown.captionText,
                    20f, 20f, 20f);
            }

            if (dropdown.itemText != null)
            {
                ConfigureObjectiveTmpText(
                    dropdown.itemText,
                    20f, 20f, 20f);
            }

            if (dropdown.template != null)
            {
                foreach (var templateScroll in
                         dropdown.template.GetComponentsInChildren<ScrollRect>(true))
                {
                    if (templateScroll == null)
                        continue;
                    templateScroll.movementType = ScrollRect.MovementType.Clamped;
                    templateScroll.inertia = false;
                    templateScroll.elasticity = 0f;
                    templateScroll.StopMovement();
                }
            }

            dropdown.RefreshShownValue();
            return dropdown;
        }

        private void BuildObjectiveResultPanels(Transform parent)
        {
            var keys = new[]
            {
                "editor.objectives.complete",
                "editor.objectives.fail",
                "editor.objectives.abandon"
            };

            for (var index = 0; index < 3; ++index)
            {
                var captured = index;
                var panel = CreateObjectivePanel(
                    "UP Objective Result Panel " + index,
                    parent, ObjectivePanelColor, true);

                // Los tres bloques son paneles independientes, hermanos de
                // Cabecera/Requisitos. No existe un contenedor "Resultados".
                panel.anchorMin = new Vector2(index / 3f, 0f);
                panel.anchorMax = new Vector2((index + 1f) / 3f, 1f);
                panel.offsetMin = new Vector2(index == 0 ? 0f : 4f, 0f);
                panel.offsetMax = new Vector2(index == 2 ? 0f : -4f, -452f);

                var resultTitle = CreateObjectiveLabel(
                    panel,
                    EditorLocalization.Get(keys[index]),
                    10f, 2f, -10f, 34f, 25f,
                    TextAlignmentOptions.MidlineLeft);
                ConfigureObjectiveTmpText(
                    resultTitle,
                    25f, 20f, 20f);

                _objectiveResultTypeDropdowns[index] =
                    CreateObjectiveNativeDropdown(
                        panel,
                        "UP Objective Result Type " + index,
                        10f, 40f, 200f, 34f,
                        ObjectiveResultTypes.Select(ObjectiveResultLabel));
                if (_objectiveResultTypeDropdowns[index] != null)
                {
                    SetObjectiveStretchRect(
                        _objectiveResultTypeDropdowns[index].transform as RectTransform,
                        10f, 40f, 56f, 34f);
                }

                _objectiveResultAddButtons[index] =
                    CreateObjectiveButton(
                        panel,
                        "UP Objective Result Add " + index,
                        "+",
                        0f, 0f, 38f, 34f,
                        EmergencyAddColor,
                        () => AddObjectiveResult(captured));
                SetTopRight(
                    _objectiveResultAddButtons[index].transform as RectTransform,
                    10f, 40f, 38f, 34f);

                _objectiveResultScrolls[index] =
                    CreateObjectiveVerticalScroll(
                        panel,
                        "UP Objective Results Scroll " + index,
                        8f, 80f, 8f, 8f,
                        out _objectiveResultContents[index]);
                _objectiveResultScrolls[index].scrollSensitivity = 44f;
                InstallNativeVerticalScrollbar(
                    panel,
                    _objectiveResultScrolls[index],
                    "UP Objective Results Scrollbar " + index,
                    80f, 8f, false, 12f);
            }
        }

        private void RefreshObjectiveList()
        {
            if (_objectiveStarPanel == null ||
                _objectiveChallengeContent == null)
                return;

            for (var index = _objectiveStarPanel.childCount - 1;
                 index >= 0;
                 --index)
            {
                var child = _objectiveStarPanel.GetChild(index);
                if (child.name.StartsWith(
                        "UP Objective Star ",
                        StringComparison.Ordinal))
                    Destroy(child.gameObject);
            }

            var stars = _objectiveDrafts
                .Select((value, index) => new { value, index })
                .Where(pair => pair.value.isStar)
                .OrderBy(pair => pair.value.starIndex)
                .ToArray();

            for (var row = 0; row < stars.Length; ++row)
            {
                var captured = stars[row].index;
                var button = CreateObjectiveButton(
                    _objectiveStarPanel,
                    "UP Objective Star Choice " + stars[row].value.starIndex,
                    EditorLocalization.Format(
                        "editor.objectives.star",
                        stars[row].value.starIndex),
                    8f,
                    40f + row * 44f,
                    ObjectiveLeftWidth - 32f,
                    38f,
                    captured == _selectedObjective
                        ? LayerBuildOrange
                        : _style.ButtonColor,
                    () => SelectObjective(captured));

                ApplyObjectiveSelectionOutline(
                    button,
                    captured == _selectedObjective);
            }

            ClearEmergencyChildren(_objectiveChallengeContent);

            var challenges = _objectiveDrafts
                .Select((value, index) => new { value, index })
                .Where(pair => !pair.value.isStar)
                .ToArray();

            Canvas.ForceUpdateCanvases();

            var height = Mathf.Max(
                100f,
                _objectiveChallengeScroll.viewport.rect.height);

            const float rowHeight = 44f;
            _objectiveChallengeContent.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(
                    height,
                    6f + challenges.Length * rowHeight));

            var normalNumber = 0;
            var publicNumber = 0;

            for (var row = 0; row < challenges.Length; ++row)
            {
                var captured = challenges[row].index;
                var publicHospital =
                    ScenarioObjectiveRequirements.IsPublicHospital(
                        challenges[row].value);

                var label = publicHospital
                    ? ScenarioObjectiveRequirements.PublicChallengeLabel(
                        ++publicNumber)
                    : EditorLocalization.Format(
                        "editor.objectives.challenge",
                        ++normalNumber);

                var button = CreateObjectiveButton(
                    _objectiveChallengeContent,
                    "UP Objective Challenge Choice " + row,
                    label,
                    0f, 0f,
                    160f,
                    rowHeight - 4f,
                    captured == _selectedObjective
                        ? LayerBuildOrange
                        : _style.ButtonColor,
                    () => SelectObjective(captured));

                ApplyObjectiveSelectionOutline(
                    button,
                    captured == _selectedObjective);

                var rect = button.transform as RectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition =
                    new Vector2(
                        0f,
                        -(6f + row * rowHeight));
                rect.sizeDelta =
                    new Vector2(
                        -18f,
                        rowHeight - 4f);
            }
        }

        private void RefreshObjectiveConfiguration()
        {
            var draft = CurrentObjective;
            if (draft == null || _objectiveConfigurationBlock == null)
                return;

            var configurationObject =
                _objectiveConfigurationBlock.gameObject;
            var restoreActive = configurationObject.activeSelf;
            if (restoreActive)
                configurationObject.SetActive(false);

            try
            {
                if (_objectiveSelectionTitle != null)
                {
                    if (draft.isStar)
                    {
                        _objectiveSelectionTitle.text =
                            EditorLocalization.Format(
                                "editor.objectives.star",
                                draft.starIndex);
                    }
                    else if (ScenarioObjectiveRequirements.IsPublicHospital(draft))
                    {
                        var publicIndex = _objectiveDrafts
                            .Take(_selectedObjective + 1)
                            .Count(value =>
                                !value.isStar &&
                                ScenarioObjectiveRequirements
                                    .IsPublicHospital(value));
                        _objectiveSelectionTitle.text =
                            ScenarioObjectiveRequirements
                                .PublicChallengeLabel(publicIndex);
                    }
                    else
                    {
                        var challengeIndex = _objectiveDrafts
                            .Take(_selectedObjective + 1)
                            .Count(value =>
                                !value.isStar &&
                                !ScenarioObjectiveRequirements
                                    .IsPublicHospital(value));
                        _objectiveSelectionTitle.text =
                            EditorLocalization.Format(
                                "editor.objectives.challenge",
                                challengeIndex);
                    }
                }

                SetObjectiveInput(
                    _objectiveDurationInput,
                    draft.durationDays,
                    !draft.isStar);
                SetObjectiveInput(
                    _objectiveProbabilityInput,
                    draft.probability,
                    !draft.isStar);
                SetObjectiveInput(
                    _objectiveDifficultyInput,
                    draft.difficulty,
                    !draft.isStar);
                SetObjectiveInput(
                    _objectiveCooldownInput,
                    draft.cooldownSeconds,
                    !draft.isStar);

                if (_objectivePublicHospitalToggle != null)
                {
                    _objectivePublicHospitalToggle.gameObject
                        .SetActive(!draft.isStar);
                    _objectivePublicHospitalToggle
                        .SetIsOnWithoutNotify(
                            ScenarioObjectiveRequirements
                                .IsPublicHospital(draft));
                }

                for (var index = 0; index < 3; ++index)
                {
                    var enabled =
                        !draft.isStar || index == 0;
                    if (_objectiveResultTypeDropdowns[index] != null)
                        _objectiveResultTypeDropdowns[index]
                            .interactable = enabled;
                    if (_objectiveResultAddButtons[index] != null)
                        _objectiveResultAddButtons[index]
                            .interactable = enabled;
                }

                RefreshObjectiveRequirements();
                RefreshObjectiveResults();
            }
            finally
            {
                if (restoreActive)
                {
                    configurationObject.SetActive(true);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(
                        _objectiveConfigurationBlock);
                    Canvas.ForceUpdateCanvases();
                }
            }
        }

        private static void SetObjectiveInput(TMP_InputField input, string value, bool editable)
        {
            if (input == null) return;
            input.SetTextWithoutNotify(value ?? "");
            input.interactable = editable;
            var image = input.targetGraphic as Image;
            if (image != null) image.color = editable ? Color.white : new Color(.65f, .65f, .65f, 1f);
        }

        private void BindObjectiveInput(TMP_InputField input, Action<string> setter)
        {
            if (input == null || setter == null) return;
            input.onEndEdit.AddListener(value =>
            {
                if (CurrentObjective == null || !input.interactable) return;
                setter(value ?? "");
                SaveObjectiveDrafts();
                RefreshObjectiveList();
            });
        }

        private void AddObjectiveChallenge()
        {
            _objectiveDrafts.Add(CreateObjectiveDraft(false));
            _selectedObjective = _objectiveDrafts.Count - 1;
            SaveObjectiveDrafts();
            RefreshObjectiveList();
            RefreshObjectiveConfiguration();
        }

        private void DeleteObjectiveChallenge()
        {
            if (CurrentObjective == null || CurrentObjective.isStar) return;
            _objectiveDrafts.RemoveAt(_selectedObjective);
            _selectedObjective = Mathf.Clamp(_selectedObjective, 0, _objectiveDrafts.Count - 1);
            SaveObjectiveDrafts();
            RefreshObjectiveList();
            RefreshObjectiveConfiguration();
        }

        private void SelectObjective(int index)
        {
            if (index < 0 || index >= _objectiveDrafts.Count) return;
            _selectedObjective = index;
            RefreshObjectiveList();
            RefreshObjectiveConfiguration();
        }


        private void AddObjectiveRequirement()
        {
            var draft = CurrentObjective;
            if (draft == null ||
                _objectiveRequirementTypeDropdown == null)
                return;

            var list = (draft.requirements ??
                new ScenarioMapFormat.ObjectiveRuleData[0]).ToList();

            var availableTypes =
                AvailableObjectiveRequirementTypes();
            if (availableTypes.Length == 0)
                return;

            var type = availableTypes[
                Mathf.Clamp(
                    _objectiveRequirementTypeDropdown.value,
                    0,
                    availableTypes.Length - 1)];

            list.Add(new ScenarioMapFormat.ObjectiveRuleData
            {
                type = type,
                value = ScenarioObjectiveRequirements.DefaultValue(type),
                reference = ""
            });

            draft.requirements = list.ToArray();
            SaveObjectiveDrafts();
            RefreshObjectiveRequirements();
        }

        private void AddObjectiveResult(int resultIndex)
        {
            var draft = CurrentObjective;
            if (draft == null || resultIndex < 0 || resultIndex > 2 ||
                (draft.isStar && resultIndex != 0)) return;

            var dropdown = _objectiveResultTypeDropdowns[resultIndex];
            if (dropdown == null) return;

            var list = ObjectiveResults(draft, resultIndex).ToList();
            var type = ObjectiveResultTypes[Mathf.Clamp(
                dropdown.value, 0, ObjectiveResultTypes.Length - 1)];
            list.Add(new ScenarioMapFormat.ObjectiveRuleData
            {
                type = type,
                value = "0"
            });
            SetObjectiveResults(draft, resultIndex, list.ToArray());
            SaveObjectiveDrafts();
            RefreshObjectiveResults();
        }

        private void RefreshObjectiveRequirements()
        {
            RefreshObjectiveRuleRows(
                _objectiveRequirementContent,
                _objectiveRequirementScroll,
                CurrentObjective?.requirements,
                true,
                -1);
        }

        private void RefreshObjectiveResults()
        {
            for (var index = 0; index < 3; ++index)
            {
                var source = CurrentObjective == null ||
                             (CurrentObjective.isStar && index != 0)
                    ? null
                    : ObjectiveResults(CurrentObjective, index);

                RefreshObjectiveRuleRows(
                    _objectiveResultContents[index],
                    _objectiveResultScrolls[index],
                    source,
                    false,
                    index);
            }
        }

        private void RefreshObjectiveRuleRows(
            RectTransform content,
            ScrollRect scroll,
            ScenarioMapFormat.ObjectiveRuleData[] source,
            bool requirement,
            int resultIndex)
        {
            if (content == null || scroll == null)
                return;

            ClearEmergencyChildren(content);
            var rules = source ??
                new ScenarioMapFormat.ObjectiveRuleData[0];

            Canvas.ForceUpdateCanvases();
            var visible = Mathf.Max(
                120f,
                scroll.viewport.rect.height);

            var showLockedStar =
                !requirement &&
                resultIndex == 0 &&
                CurrentObjective != null &&
                CurrentObjective.isStar;

            var totalHeight = showLockedStar ? 84f : 0f;
            if (requirement)
            {
                for (var index = 0; index < rules.Length; ++index)
                    totalHeight += ObjectiveRequirementRowHeight(rules[index]);
            }
            else
            {
                totalHeight += rules.Length * 84f;
            }

            content.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(visible, totalHeight));

            var top = 0f;
            if (showLockedStar)
            {
                CreateObjectiveLockedStarRow(
                    content,
                    CurrentObjective.starIndex,
                    0f,
                    84f);
                top = 84f;
            }

            for (var index = 0; index < rules.Length; ++index)
            {
                var captured = index;
                var rule = rules[index];
                var rowHeight = requirement
                    ? ObjectiveRequirementRowHeight(rule)
                    : 84f;

                var rowObject = NewImage(
                    "UP Objective Rule " + index,
                    content,
                    _style.ButtonSprite,
                    SettingsOuterPanelColor,
                    false);
                rowObject.SetActive(false);

                var rowOutline =
                    rowObject.AddComponent<Outline>();
                rowOutline.effectColor =
                    new Color(1f, 1f, 1f, .7f);
                rowOutline.effectDistance =
                    new Vector2(1f, -1f);
                rowOutline.useGraphicAlpha = true;

                var row = rowObject.transform as RectTransform;
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(.5f, 1f);
                row.anchoredPosition =
                    new Vector2(0f, -top);
                row.sizeDelta =
                    new Vector2(0f, rowHeight - 4f);

                if (requirement)
                {
                    BuildObjectiveRequirementRow(
                        row,
                        scroll,
                        rule,
                        captured);
                }
                else
                {
                    BuildObjectiveResultRow(
                        row,
                        scroll,
                        rule,
                        captured,
                        resultIndex);
                }

                rowObject.SetActive(true);
                top += rowHeight;
            }

            ClampObjectiveScroll(scroll);
        }

        private static void ClampObjectiveScroll(ScrollRect scroll)
        {
            if (scroll == null)
                return;

            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.elasticity = 0f;
            scroll.StopMovement();
            scroll.velocity = Vector2.zero;

            if (scroll.content == null || scroll.viewport == null)
                return;

            Canvas.ForceUpdateCanvases();

            if (scroll.content.rect.height <=
                scroll.viewport.rect.height + .5f)
            {
                var position = scroll.content.anchoredPosition;
                position.y = 0f;
                scroll.content.anchoredPosition = position;
                scroll.verticalNormalizedPosition = 1f;
                return;
            }

            var normalized = scroll.verticalNormalizedPosition;
            if (float.IsNaN(normalized) || float.IsInfinity(normalized))
                normalized = 1f;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(normalized);
        }

        private void BuildObjectiveRequirementRow(
            RectTransform row,
            ScrollRect scroll,
            ScenarioMapFormat.ObjectiveRuleData rule,
            int captured)
        {
            var available = Mathf.Max(
                520f,
                scroll.viewport.rect.width);
            var typeWidth = available * .42f;
            var references =
                ScenarioObjectiveRequirements.ReferenceKinds(
                    rule.type);
            var hasValue =
                ScenarioObjectiveRequirements.HasValue(
                    rule.type);

            var rowTypes =
                AvailableObjectiveRequirementTypes(rule.type);
            var labels = rowTypes
                .Select(ObjectiveRequirementLabel)
                .ToArray();

            var typeDropdown =
                CreateObjectiveNativeDropdown(
                    row,
                    "Type",
                    6f,
                    4f,
                    typeWidth,
                    34f,
                    labels);

            if (typeDropdown != null)
            {
                typeDropdown.SetValueWithoutNotify(
                    Mathf.Max(
                        0,
                        Array.IndexOf(
                            rowTypes,
                            rule.type)));
                typeDropdown.RefreshShownValue();
                typeDropdown.onValueChanged.AddListener(value =>
                {
                    rule.type = rowTypes[
                        Mathf.Clamp(
                            value,
                            0,
                            rowTypes.Length - 1)];
                    rule.value =
                        ScenarioObjectiveRequirements
                            .DefaultValue(rule.type);
                    rule.reference = "";
                    SaveObjectiveDrafts();
                    RefreshObjectiveRequirements();
                });
            }

            CreateObjectiveButton(
                row,
                "Remove",
                "×",
                available - 42f,
                4f,
                36f,
                34f,
                EmergencyRemoveColor,
                () =>
                {
                    var current =
                        CurrentObjective.requirements.ToList();
                    if (captured < 0 ||
                        captured >= current.Count)
                        return;

                    current.RemoveAt(captured);
                    CurrentObjective.requirements =
                        current.ToArray();
                    SaveObjectiveDrafts();
                    RefreshObjectiveRequirements();
                });

            if (references.Length <= 1)
            {
                var x = 12f + typeWidth;
                var remaining =
                    available - x - 48f;

                if (references.Length == 1)
                {
                    var singleReferenceWidth = hasValue
                        ? remaining * .62f
                        : remaining;
                    CreateObjectiveRequirementReference(
                        row,
                        rule,
                        references[0],
                        0,
                        x,
                        4f,
                        singleReferenceWidth - 6f,
                        34f);
                    x += singleReferenceWidth;
                    remaining = available - x - 48f;
                }

                if (hasValue)
                {
                    CreateObjectiveRequirementValue(
                        row,
                        rule,
                        x,
                        4f,
                        Mathf.Max(54f, remaining - 6f),
                        34f);
                }
                return;
            }

            if (hasValue)
            {
                var valueX = 12f + typeWidth;
                CreateObjectiveRequirementValue(
                    row,
                    rule,
                    valueX,
                    4f,
                    Mathf.Max(
                        54f,
                        available - valueX - 54f),
                    34f);
            }

            const float referenceLeft = 6f;
            const float referenceGap = 6f;
            var referenceAreaWidth =
                available - 12f;
            var referenceWidth =
                (referenceAreaWidth - referenceGap) * .5f;

            for (var index = 0;
                 index < references.Length;
                 ++index)
            {
                var line = index / 2;
                var column = index % 2;
                var x = referenceLeft +
                        column * (referenceWidth + referenceGap);
                var y = 42f + line * 38f;

                CreateObjectiveRequirementReference(
                    row,
                    rule,
                    references[index],
                    index,
                    x,
                    y,
                    referenceWidth,
                    34f);
            }
        }


        private static float ObjectiveRequirementRowHeight(
            ScenarioMapFormat.ObjectiveRuleData rule)
        {
            var references =
                ScenarioObjectiveRequirements.ReferenceKinds(
                    rule?.type);

            if (references.Length <= 1)
                return 46f;

            var extraLines =
                (references.Length + 1) / 2;
            return 46f + extraLines * 38f;
        }

        private void CreateObjectiveRequirementValue(
            RectTransform row,
            ScenarioMapFormat.ObjectiveRuleData rule,
            float x,
            float y,
            float width,
            float height)
        {
            var input = CreateObjectiveInput(
                row,
                "Value",
                x,
                y,
                width,
                height,
                rule.value,
                false);
            if (input == null)
                return;

            input.contentType =
                TMP_InputField.ContentType.DecimalNumber;
            input.onEndEdit.AddListener(value =>
            {
                rule.value = value ?? "";
                SaveObjectiveDrafts();
            });
        }

        private void CreateObjectiveRequirementReference(
            RectTransform row,
            ScenarioMapFormat.ObjectiveRuleData rule,
            ObjectiveRequirementReferenceKind kind,
            int referenceIndex,
            float x,
            float y,
            float width,
            float height)
        {
            var allowEmpty =
                !ScenarioObjectiveRequirements.ReferenceRequired(
                    rule.type,
                    referenceIndex);
            var choices =
                ScenarioObjectiveRequirements.Choices(
                    kind,
                    allowEmpty);

            if (choices.Length == 0)
                return;

            var dropdown = CreateObjectiveNativeDropdown(
                row,
                "Reference " + referenceIndex,
                x,
                y,
                width,
                height,
                choices.Select(value => value.Label));
            if (dropdown == null)
                return;

            var saved =
                ScenarioObjectiveRequirements.ReferencePart(
                    rule.reference,
                    referenceIndex);
            var selected = Array.FindIndex(
                choices,
                value => string.Equals(
                    value.Id,
                    saved,
                    StringComparison.Ordinal));
            if (selected < 0)
                selected = 0;

            if (!allowEmpty &&
                string.IsNullOrEmpty(saved) &&
                !string.IsNullOrEmpty(choices[selected].Id))
            {
                rule.reference =
                    ScenarioObjectiveRequirements.SetReferencePart(
                        rule.reference,
                        referenceIndex,
                        choices[selected].Id);
                SaveObjectiveDrafts();
            }

            dropdown.SetValueWithoutNotify(selected);
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(value =>
            {
                var index = Mathf.Clamp(
                    value,
                    0,
                    choices.Length - 1);
                rule.reference =
                    ScenarioObjectiveRequirements.SetReferencePart(
                        rule.reference,
                        referenceIndex,
                        choices[index].Id);
                SaveObjectiveDrafts();
            });
        }

        private Toggle CreateObjectivePublicHospitalToggle(
            Transform parent,
            float x,
            float y)
        {
            return CreateObjectiveNativeCheckbox(
                parent,
                "UP Objective Public Hospital Toggle",
                ScenarioObjectiveRequirements.PublicHospitalLabel(),
                x,
                y,
                174f,
                false,
                value =>
                {
                    var draft = CurrentObjective;
                    if (draft == null || draft.isStar)
                        return;

                    ScenarioObjectiveRequirements
                        .SetPublicHospital(draft, value);
                    SaveObjectiveDrafts();
                    RefreshObjectiveList();
                    RefreshObjectiveConfiguration();
                });
        }

        private Toggle CreateObjectiveRequirementFilterToggle(
            Transform parent,
            string name,
            string label,
            float x,
            float y,
            float width,
            bool value,
            Action<bool> changed)
        {
            return CreateObjectiveNativeCheckbox(
                parent,
                name,
                label,
                x,
                y,
                width,
                value,
                changed);
        }

        private Toggle CreateObjectiveNativeCheckbox(
            Transform parent,
            string name,
            string label,
            float x,
            float y,
            float width,
            bool value,
            Action<bool> changed)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = root.transform as RectTransform;
            SetTopLeft(rootRect, x, y, width, 36f);

            CreateObjectiveLabel(
                root.transform,
                label,
                0f,
                0f,
                Mathf.Max(40f, width - 38f),
                36f,
                18f,
                TextAlignmentOptions.MidlineLeft);

            var toggle = CreateDiseaseToggle(
                root.transform,
                value,
                changed);
            if (toggle == null)
            {
                Destroy(root);
                return null;
            }

            toggle.gameObject.name = name + " Control";
            SetTopLeft(
                toggle.transform as RectTransform,
                width - 32f,
                3f,
                30f,
                30f);
            return toggle;
        }

        private void BuildObjectiveResultRow(
            RectTransform row,
            ScrollRect scroll,
            ScenarioMapFormat.ObjectiveRuleData rule,
            int captured,
            int resultIndex)
        {
            var available = Mathf.Max(
                160f,
                scroll.viewport.rect.width);
            var firstLineWidth =
                Mathf.Max(80f, available - 54f);

            var labels = ObjectiveResultTypes
                .Select(ObjectiveResultLabel)
                .ToArray();
            var dropdown = CreateObjectiveNativeDropdown(
                row,
                "Type",
                6f,
                4f,
                firstLineWidth,
                34f,
                labels);
            if (dropdown != null &&
                dropdown.captionText != null)
            {
                var resultTypeTextRect =
                    dropdown.captionText.rectTransform;
                resultTypeTextRect.anchorMin = Vector2.zero;
                resultTypeTextRect.anchorMax = Vector2.one;
                resultTypeTextRect.pivot =
                    new Vector2(.5f, .5f);
                resultTypeTextRect.anchoredPosition =
                    new Vector2(-20f, 0f);
                resultTypeTextRect.sizeDelta =
                    new Vector2(-60f, 0f);
            }

            if (dropdown != null)
            {
                dropdown.SetValueWithoutNotify(
                    Mathf.Max(
                        0,
                        Array.IndexOf(
                            ObjectiveResultTypes,
                            rule.type)));
                dropdown.RefreshShownValue();
                dropdown.onValueChanged.AddListener(value =>
                {
                    rule.type = ObjectiveResultTypes[
                        Mathf.Clamp(
                            value,
                            0,
                            ObjectiveResultTypes.Length - 1)];
                    rule.reference = "";
                    SaveObjectiveDrafts();
                    RefreshObjectiveResults();
                });
            }

            CreateObjectiveButton(
                row,
                "Remove",
                "×",
                available - 42f,
                4f,
                36f,
                34f,
                EmergencyRemoveColor,
                () =>
                {
                    var current = ObjectiveResults(
                        CurrentObjective,
                        resultIndex).ToList();
                    if (captured < 0 ||
                        captured >= current.Count)
                        return;
                    current.RemoveAt(captured);
                    SetObjectiveResults(
                        CurrentObjective,
                        resultIndex,
                        current.ToArray());
                    SaveObjectiveDrafts();
                    RefreshObjectiveResults();
                });

            var referenceChoices =
                ObjectiveResultReferenceOptions(rule.type);
            if (referenceChoices != null)
            {
                var reference = referenceChoices.Length == 0
                    ? null
                    : CreateObjectiveNativeDropdown(
                        row,
                        "Reference",
                        6f,
                        42f,
                        Mathf.Max(80f, available - 12f),
                        34f,
                        referenceChoices.Select(value => value.Label));
                if (reference != null)
                {
                    var selected = Array.FindIndex(
                        referenceChoices,
                        value => string.Equals(
                            value.Id,
                            rule.reference,
                            StringComparison.Ordinal));
                    if (selected < 0)
                        selected = 0;

                    if (string.IsNullOrEmpty(rule.reference) &&
                        !string.IsNullOrEmpty(referenceChoices[selected].Id))
                    {
                        rule.reference = referenceChoices[selected].Id;
                        SaveObjectiveDrafts();
                    }

                    reference.SetValueWithoutNotify(selected);
                    reference.RefreshShownValue();
                    reference.onValueChanged.AddListener(value =>
                    {
                        var index = Mathf.Clamp(
                            value,
                            0,
                            referenceChoices.Length - 1);
                        rule.reference = referenceChoices[index].Id;
                        SaveObjectiveDrafts();
                    });
                }
            }
            else
            {
                var input = CreateObjectiveInput(
                    row,
                    "Value",
                    6f,
                    42f,
                    Mathf.Max(80f, available - 12f),
                    34f,
                    rule.value,
                    false);
                input.contentType =
                    TMP_InputField.ContentType.DecimalNumber;
                input.onEndEdit.AddListener(value =>
                {
                    rule.value = value ?? "";
                    SaveObjectiveDrafts();
                });
            }
        }

        private void CreateObjectiveLockedStarRow(Transform parent, int star, float top, float height)
        {
            var row = NewImage("UP Objective Star Reward", parent, _style.ButtonSprite,
                SettingsOuterPanelColor, false).transform as RectTransform;
            var rowOutline = row.gameObject.AddComponent<Outline>();
            rowOutline.effectColor = new Color(1f, 1f, 1f, .7f);
            rowOutline.effectDistance = new Vector2(1f, -1f);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(.5f, 1f);
            row.anchoredPosition = new Vector2(.5f, -(top + 1f));
            row.sizeDelta = new Vector2(-11f, 39f);
            var rewardText = CreateObjectiveLabel(
                row,
                EditorLocalization.Format(
                    "editor.objectives.star_reward",
                    star),
                12f, 0f, -12f, 40f, 20f,
                TextAlignmentOptions.MidlineLeft);

            var rewardTextRect = rewardText.rectTransform;
            rewardTextRect.anchorMin =
                new Vector2(0f, 1f);
            rewardTextRect.anchorMax =
                new Vector2(1f, 1f);
            rewardTextRect.pivot =
                new Vector2(.5f, 1f);
            rewardTextRect.anchoredPosition =
                Vector2.zero;
            rewardTextRect.sizeDelta =
                new Vector2(-24f, 40f);

            ConfigureObjectiveTmpText(
                rewardText,
                20f, 16f, 16f);
        }

        private static ScenarioMapFormat.ObjectiveRuleData[] ObjectiveResults(
            ScenarioMapFormat.ObjectiveData draft, int resultIndex)
        {
            if (draft == null)
                return new ScenarioMapFormat.ObjectiveRuleData[0];
            if (resultIndex == 1)
                return draft.failure ??
                    new ScenarioMapFormat.ObjectiveRuleData[0];
            if (resultIndex == 2)
                return draft.abandon ??
                    new ScenarioMapFormat.ObjectiveRuleData[0];
            return draft.completion ??
                new ScenarioMapFormat.ObjectiveRuleData[0];
        }

        private static void SetObjectiveResults(
            ScenarioMapFormat.ObjectiveData draft,
            int resultIndex,
            ScenarioMapFormat.ObjectiveRuleData[] values)
        {
            if (draft == null) return;
            if (resultIndex == 1)
                draft.failure = values;
            else if (resultIndex == 2)
                draft.abandon = values;
            else
                draft.completion = values;
        }

        private static void ApplyObjectiveSelectionOutline(
            Button button, bool selected)
        {
            if (button == null) return;
            var outline = button.GetComponent<Outline>();
            if (!selected)
            {
                if (outline != null)
                    outline.enabled = false;
                return;
            }

            if (outline == null)
                outline =
                    button.gameObject.AddComponent<Outline>();
            outline.enabled = true;
            outline.effectColor = Color.white;
            outline.effectDistance =
                new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;
        }


        private string[] AvailableObjectiveRequirementTypes(
            string includeCurrentType = null)
        {
            return ObjectiveRequirementTypes
                .Where(type =>
                    string.Equals(
                        type,
                        includeCurrentType,
                        StringComparison.Ordinal) ||
                    (!ScenarioObjectiveRequirements.IsTutorial(type) &&
                     !ScenarioObjectiveRequirements.IsDlc(type)) ||
                    (_objectiveShowTutorialRequirements &&
                     ScenarioObjectiveRequirements.IsTutorial(type)) ||
                    (_objectiveShowDlcRequirements &&
                     ScenarioObjectiveRequirements.IsDlc(type)))
                .ToArray();
        }

        private void RefreshObjectiveRequirementTypeDropdown()
        {
            if (_objectiveRequirementTypeDropdown == null)
                return;

            var types = AvailableObjectiveRequirementTypes();
            _objectiveRequirementTypeDropdown.ClearOptions();
            _objectiveRequirementTypeDropdown.AddOptions(
                types.Select(ObjectiveRequirementLabel).ToList());
            _objectiveRequirementTypeDropdown.SetValueWithoutNotify(0);
            _objectiveRequirementTypeDropdown.RefreshShownValue();
        }

        private static string ObjectiveRequirementLabel(string type) =>
            ScenarioObjectiveRequirements.Label(type);

        private static string ObjectiveResultLabel(string type)
        {
            var language = EditorLocalization.CurrentLanguageCode();
            var spanish = !string.IsNullOrEmpty(language) &&
                          language.StartsWith(
                              "es",
                              StringComparison.OrdinalIgnoreCase);

            switch (type)
            {
                case "kudosh":
                    return "Kudosh";
                case "unlock_item":
                    return spanish
                        ? "Desbloquear objeto"
                        : "Unlock item";
                case "unlock_item_upgrade":
                    return spanish
                        ? "Desbloquear mejora"
                        : "Unlock upgrade";
                default:
                    return EditorLocalization.Get(
                        "editor.objectives.result." + type);
            }
        }

        private static ObjectiveRequirementChoice[] ObjectiveRoomOptions()
        {
            return ScenarioObjectiveRequirements.Choices(
                ObjectiveRequirementReferenceKind.Room,
                false);
        }

        private static ObjectiveRequirementChoice[] ObjectiveResultReferenceOptions(
            string type)
        {
            switch (type)
            {
                case "unlock_room":
                    return ObjectiveRoomOptions();
                case "unlock_item":
                    return ScenarioObjectiveRequirements.Choices(
                        ObjectiveRequirementReferenceKind.Item,
                        false);
                case "unlock_item_upgrade":
                    return ScenarioObjectiveRequirements.Choices(
                        ObjectiveRequirementReferenceKind.ItemUpgrade,
                        false);
                default:
                    return null;
            }
        }

        private static void SetObjectiveStretchRect(RectTransform rect,
            float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2((left - right) * .5f, -top);
            rect.sizeDelta = new Vector2(-(left + right), height);
        }

        private static void SetObjectiveBottomStretchRect(RectTransform rect,
            float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
