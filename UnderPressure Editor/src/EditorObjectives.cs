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

        private static readonly string[] ObjectiveRequirementTypes =
        {
            "earn_money", "reputation", "cure_patients", "hospital_value",
            "attractiveness", "staff_morale", "days_without_death", "build_room"
        };

        private static readonly string[] ObjectiveResultTypes =
        {
            "money", "reputation", "research_points", "unlock_room"
        };

        private RectTransform _objectiveContent;
        private RectTransform _objectiveRoot;
        private RectTransform _objectiveListBlock;
        private RectTransform _objectiveConfigurationBlock;
        private ScrollRect _objectiveChallengeScroll;
        private RectTransform _objectiveChallengeContent;
        private RectTransform _objectiveRequirementContent;
        private ScrollRect _objectiveRequirementScroll;
        private RectTransform _objectiveResultContent;
        private ScrollRect _objectiveResultScroll;
        private TMP_Dropdown _objectiveRequirementTypeDropdown;
        private TMP_Dropdown _objectiveResultTypeDropdown;
        private TMP_InputField _objectiveNameInput;
        private TMP_InputField _objectiveDurationInput;
        private TMP_InputField _objectiveProbabilityInput;
        private TMP_InputField _objectiveDifficultyInput;
        private TMP_InputField _objectiveCooldownInput;
        private Button[] _objectiveResultTabButtons;
        private readonly List<ScenarioMapFormat.ObjectiveData> _objectiveDrafts =
            new List<ScenarioMapFormat.ObjectiveData>();
        private int _selectedObjective;
        private int _selectedObjectiveResult;
        private bool _objectiveBuilt;

        internal void BuildObjectiveSettingsContents()
        {
            _objectiveContent = null;
            _objectiveRoot = null;
            _objectiveBuilt = false;
            _objectiveDrafts.Clear();
            _selectedObjective = 0;
            _selectedObjectiveResult = 0;

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

            _objectiveListBlock = CreateEmergencyBlock(
                "UP Objectives List", _objectiveRoot);
            SetTopLeft(_objectiveListBlock, 0f, 0f, ObjectiveLeftWidth,
                pageHeight - 20f);

            _objectiveConfigurationBlock = CreateEmergencyBlock(
                "UP Objective Configuration", _objectiveRoot);
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
            CreateEmergencyLabel(_objectiveListBlock,
                EditorLocalization.Get("editor.objectives.stars"),
                12f, 8f, ObjectiveLeftWidth - 24f, 28f, 20f,
                TextAlignmentOptions.MidlineLeft);

            CreateEmergencyLabel(_objectiveListBlock,
                EditorLocalization.Get("editor.objectives.challenges"),
                12f, 190f, ObjectiveLeftWidth - 24f, 32f, 21f,
                TextAlignmentOptions.MidlineLeft);

            _objectiveChallengeScroll = CreateEmergencyVerticalScroll(
                _objectiveListBlock, "UP Objective Challenge Scroll",
                12f, 228f, 12f, 60f, out _objectiveChallengeContent);
            InstallNativeVerticalScrollbar(_objectiveListBlock,
                _objectiveChallengeScroll, "UP Objective Challenge Scrollbar",
                228f, 60f, false, 12f);

            CreateEmergencyButton(_objectiveListBlock, "UP Objective Add",
                EditorLocalization.Get("editor.objectives.new_challenge"),
                12f, height - 47f,
                174f, 36f, EmergencyAddColor, AddObjectiveChallenge);
            CreateEmergencyButton(_objectiveListBlock, "UP Objective Delete", "×",
                191f, height - 47f,
                34f, 36f, EmergencyRemoveColor, DeleteObjectiveChallenge);
        }

        private void BuildObjectiveConfigurationChildren()
        {
            CreateEmergencyLabel(_objectiveConfigurationBlock,
                EditorLocalization.Get("editor.objectives.configuration"),
                14f, 8f, 240f, 30f, 21f,
                TextAlignmentOptions.MidlineLeft);

            _objectiveNameInput = CreateObjectiveField(
                "editor.objectives.name", 14f, 44f, 0f, false, false);
            _objectiveDurationInput = CreateObjectiveField(
                "editor.objectives.duration", 14f, 88f, .25f, true, true);
            _objectiveProbabilityInput = CreateObjectiveField(
                "editor.objectives.probability", .25f, 88f, .5f, false, true);
            _objectiveDifficultyInput = CreateObjectiveField(
                "editor.objectives.difficulty", .5f, 88f, .75f, true, true);
            _objectiveCooldownInput = CreateObjectiveField(
                "editor.objectives.cooldown", .75f, 88f, 1f, false, true);

            var requirements = CreateEmergencyBlock(
                "UP Objective Requirements", _objectiveConfigurationBlock);
            SetObjectiveStretchRect(requirements, 14f, 140f, 14f, 330f);
            CreateEmergencyLabel(requirements,
                EditorLocalization.Get("editor.objectives.requirements"),
                12f, 7f, 190f, 30f, 20f,
                TextAlignmentOptions.MidlineLeft);
            _objectiveRequirementTypeDropdown = CreateEmergencyDropdown(
                requirements, "UP Objective Requirement Type",
                205f, 6f, 310f, 34f,
                ObjectiveRequirementTypes.Select(ObjectiveRequirementLabel));
            CreateEmergencyButton(requirements, "UP Objective Requirement Add", "+",
                525f, 6f, 36f, 34f, EmergencyAddColor, AddObjectiveRequirement);
            _objectiveRequirementScroll = CreateEmergencyVerticalScroll(
                requirements, "UP Objective Requirements Scroll",
                12f, 48f, 12f, 12f, out _objectiveRequirementContent);
            InstallNativeVerticalScrollbar(requirements,
                _objectiveRequirementScroll, "UP Objective Requirements Scrollbar",
                48f, 12f, false, 12f);

            var results = CreateEmergencyBlock(
                "UP Objective Results", _objectiveConfigurationBlock);
            SetObjectiveBottomStretchRect(results, 14f, 340f, 14f, 14f);
            BuildObjectiveResultHeader(results);
            _objectiveResultTypeDropdown = CreateEmergencyDropdown(
                results, "UP Objective Result Type",
                12f, 50f, 310f, 34f,
                ObjectiveResultTypes.Select(ObjectiveResultLabel));
            CreateEmergencyButton(results, "UP Objective Result Add", "+",
                332f, 50f, 36f, 34f, EmergencyAddColor, AddObjectiveResult);
            _objectiveResultScroll = CreateEmergencyVerticalScroll(
                results, "UP Objective Results Scroll",
                12f, 92f, 12f, 12f, out _objectiveResultContent);
            InstallNativeVerticalScrollbar(results,
                _objectiveResultScroll, "UP Objective Results Scrollbar",
                92f, 12f, false, 12f);

            BindObjectiveInput(_objectiveNameInput, value => CurrentObjective.name = value);
            BindObjectiveInput(_objectiveDurationInput, value => CurrentObjective.durationDays = value);
            BindObjectiveInput(_objectiveProbabilityInput, value => CurrentObjective.probability = value);
            BindObjectiveInput(_objectiveDifficultyInput, value => CurrentObjective.difficulty = value);
            BindObjectiveInput(_objectiveCooldownInput, value => CurrentObjective.cooldownSeconds = value);
        }

        private TMP_InputField CreateObjectiveField(string labelKey, float xOrAnchor,
            float top, float rightAnchor, bool integer, bool challengeOnly)
        {
            var parent = _objectiveConfigurationBlock;
            var width = rightAnchor <= 0f ? -14f : 0f;
            float left;
            float right;
            if (rightAnchor <= 0f)
            {
                left = 14f;
                right = 14f;
            }
            else
            {
                var rectWidth = Mathf.Max(800f, parent.rect.width);
                left = xOrAnchor * rectWidth + 8f;
                right = (1f - rightAnchor) * rectWidth + 8f;
            }

            var row = new GameObject("UP Objective Field", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = row.transform as RectTransform;
            rowRect.anchorMin = new Vector2(rightAnchor > 0f ? xOrAnchor : 0f, 1f);
            rowRect.anchorMax = new Vector2(rightAnchor > 0f ? rightAnchor : 1f, 1f);
            rowRect.pivot = new Vector2(.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -top);
            rowRect.sizeDelta = new Vector2(-16f, 38f);

            CreateEmergencyLabel(row.transform, EditorLocalization.Get(labelKey),
                0f, 0f, 106f, 38f, 15f, TextAlignmentOptions.MidlineLeft);
            var input = CreateEmergencyInput(row.transform, labelKey,
                108f, 1f, -4f, 36f, "", integer);
            var inputRect = input.transform as RectTransform;
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(.5f, 1f);
            inputRect.anchoredPosition = new Vector2(52f, -1f);
            inputRect.sizeDelta = new Vector2(-112f, 36f);
            if (!integer) input.contentType = TMP_InputField.ContentType.DecimalNumber;
            if (!challengeOnly)
                input.contentType = TMP_InputField.ContentType.Standard;
            return input;
        }

        private void BuildObjectiveResultHeader(Transform parent)
        {
            _objectiveResultTabButtons = new Button[3];
            var keys = new[]
            {
                "editor.objectives.complete", "editor.objectives.fail",
                "editor.objectives.abandon"
            };
            for (var index = 0; index < keys.Length; ++index)
            {
                var captured = index;
                _objectiveResultTabButtons[index] = CreateEmergencyButton(
                    parent, "UP Objective Result Tab " + index,
                    EditorLocalization.Get(keys[index]),
                    12f + index * 126f, 7f, 118f, 34f,
                    _style.ButtonColor, () => SelectObjectiveResult(captured));
            }
        }

        private void RefreshObjectiveList()
        {
            if (_objectiveListBlock == null || _objectiveChallengeContent == null) return;
            for (var index = _objectiveListBlock.childCount - 1; index >= 0; --index)
            {
                var child = _objectiveListBlock.GetChild(index);
                if (child.name.StartsWith("UP Objective Star ", StringComparison.Ordinal))
                    Destroy(child.gameObject);
            }

            var stars = _objectiveDrafts.Select((value, index) => new { value, index })
                .Where(pair => pair.value.isStar).OrderBy(pair => pair.value.starIndex).ToArray();
            for (var row = 0; row < stars.Length; ++row)
            {
                var captured = stars[row].index;
                CreateEmergencyButton(_objectiveListBlock,
                    "UP Objective Star " + stars[row].value.starIndex,
                    EditorLocalization.Format("editor.objectives.star", stars[row].value.starIndex),
                    12f, 42f + row * 46f, ObjectiveLeftWidth - 24f, 40f,
                    captured == _selectedObjective ? LayerBuildOrange : _style.ButtonColor,
                    () => SelectObjective(captured));
            }

            ClearEmergencyChildren(_objectiveChallengeContent);
            var challenges = _objectiveDrafts.Select((value, index) => new { value, index })
                .Where(pair => !pair.value.isStar).ToArray();
            Canvas.ForceUpdateCanvases();
            var height = Mathf.Max(100f, _objectiveChallengeScroll.viewport.rect.height);
            const float rowHeight = 44f;
            _objectiveChallengeContent.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, Mathf.Max(height, challenges.Length * rowHeight));
            for (var row = 0; row < challenges.Length; ++row)
            {
                var captured = challenges[row].index;
                var label = string.IsNullOrWhiteSpace(challenges[row].value.name)
                    ? EditorLocalization.Format("editor.objectives.challenge", row + 1)
                    : challenges[row].value.name;
                var button = CreateEmergencyButton(_objectiveChallengeContent,
                    "UP Objective Challenge " + row, label,
                    0f, row * rowHeight, -2f, rowHeight - 4f,
                    captured == _selectedObjective ? LayerBuildOrange : _style.ButtonColor,
                    () => SelectObjective(captured));
                var rect = button.transform as RectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -row * rowHeight);
                rect.sizeDelta = new Vector2(-2f, rowHeight - 4f);
            }
        }

        private void RefreshObjectiveConfiguration()
        {
            var draft = CurrentObjective;
            if (draft == null || _objectiveConfigurationBlock == null) return;
            SetObjectiveInput(_objectiveNameInput,
                draft.isStar
                    ? EditorLocalization.Format("editor.objectives.star", draft.starIndex)
                    : draft.name, !draft.isStar);
            SetObjectiveInput(_objectiveDurationInput, draft.durationDays, !draft.isStar);
            SetObjectiveInput(_objectiveProbabilityInput, draft.probability, !draft.isStar);
            SetObjectiveInput(_objectiveDifficultyInput, draft.difficulty, !draft.isStar);
            SetObjectiveInput(_objectiveCooldownInput, draft.cooldownSeconds, !draft.isStar);

            if (draft.isStar && _selectedObjectiveResult != 0) _selectedObjectiveResult = 0;
            for (var index = 0; index < _objectiveResultTabButtons.Length; ++index)
            {
                var button = _objectiveResultTabButtons[index];
                button.interactable = !draft.isStar || index == 0;
                ApplyButtonStyle(button, index == _selectedObjectiveResult
                    ? LayerBuildOrange : _style.ButtonColor);
            }
            RefreshObjectiveRequirements();
            RefreshObjectiveResults();
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
            _selectedObjectiveResult = 0;
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
            _selectedObjectiveResult = 0;
            RefreshObjectiveList();
            RefreshObjectiveConfiguration();
        }

        private void SelectObjectiveResult(int index)
        {
            if (CurrentObjective == null || index < 0 || index > 2 ||
                (CurrentObjective.isStar && index != 0)) return;
            _selectedObjectiveResult = index;
            RefreshObjectiveConfiguration();
        }

        private void AddObjectiveRequirement()
        {
            var draft = CurrentObjective;
            if (draft == null) return;
            var list = (draft.requirements ?? new ScenarioMapFormat.ObjectiveRuleData[0]).ToList();
            var type = ObjectiveRequirementTypes[Mathf.Clamp(
                _objectiveRequirementTypeDropdown.value, 0, ObjectiveRequirementTypes.Length - 1)];
            list.Add(new ScenarioMapFormat.ObjectiveRuleData
            {
                type = type,
                value = type == "build_room" ? "1" : "0"
            });
            draft.requirements = list.ToArray();
            SaveObjectiveDrafts();
            RefreshObjectiveRequirements();
        }

        private void AddObjectiveResult()
        {
            var draft = CurrentObjective;
            if (draft == null) return;
            var list = CurrentObjectiveResults(draft).ToList();
            var type = ObjectiveResultTypes[Mathf.Clamp(
                _objectiveResultTypeDropdown.value, 0, ObjectiveResultTypes.Length - 1)];
            list.Add(new ScenarioMapFormat.ObjectiveRuleData { type = type, value = "0" });
            SetCurrentObjectiveResults(draft, list.ToArray());
            SaveObjectiveDrafts();
            RefreshObjectiveResults();
        }

        private void RefreshObjectiveRequirements()
        {
            RefreshObjectiveRuleRows(_objectiveRequirementContent,
                _objectiveRequirementScroll, CurrentObjective?.requirements, true);
        }

        private void RefreshObjectiveResults()
        {
            RefreshObjectiveRuleRows(_objectiveResultContent,
                _objectiveResultScroll,
                CurrentObjective == null ? null : CurrentObjectiveResults(CurrentObjective), false);
        }

        private void RefreshObjectiveRuleRows(RectTransform content, ScrollRect scroll,
            ScenarioMapFormat.ObjectiveRuleData[] source, bool requirement)
        {
            if (content == null || scroll == null) return;
            ClearEmergencyChildren(content);
            var rules = source ?? new ScenarioMapFormat.ObjectiveRuleData[0];
            Canvas.ForceUpdateCanvases();
            var visible = Mathf.Max(120f, scroll.viewport.rect.height);
            const float rowHeight = 46f;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(visible, rules.Length * rowHeight + (!requirement && CurrentObjective.isStar ? rowHeight : 0f)));

            var offset = 0;
            if (!requirement && CurrentObjective.isStar)
            {
                CreateObjectiveLockedStarRow(content, CurrentObjective.starIndex, 0f, rowHeight);
                offset = 1;
            }

            for (var index = 0; index < rules.Length; ++index)
            {
                var captured = index;
                var rule = rules[index];
                var rowObject = NewImage("UP Objective Rule " + index, content,
                    _style.ButtonSprite, SettingsSharedPageColor, false);
                var row = rowObject.transform as RectTransform;
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -(index + offset) * rowHeight);
                row.sizeDelta = new Vector2(0f, rowHeight - 4f);

                var available = Mathf.Max(520f, scroll.viewport.rect.width);
                var typeWidth = available * .42f;
                var refWidth = (requirement && rule.type == "build_room") ||
                               (!requirement && rule.type == "unlock_room")
                    ? available * .27f : 0f;
                var valueWidth = available - typeWidth - refWidth - 58f;
                var canonical = requirement ? ObjectiveRequirementTypes : ObjectiveResultTypes;
                var labels = canonical.Select(requirement
                    ? (Func<string, string>)ObjectiveRequirementLabel
                    : ObjectiveResultLabel).ToArray();
                var dropdown = CreateEmergencyDropdown(row, "Type", 6f, 4f,
                    typeWidth, 34f, labels);
                dropdown.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(canonical, rule.type)));
                dropdown.onValueChanged.AddListener(value =>
                {
                    rule.type = canonical[Mathf.Clamp(value, 0, canonical.Length - 1)];
                    rule.reference = "";
                    SaveObjectiveDrafts();
                    if (requirement) RefreshObjectiveRequirements(); else RefreshObjectiveResults();
                });

                var x = 12f + typeWidth;
                if (refWidth > 0f)
                {
                    var rooms = ObjectiveRoomOptions();
                    var reference = CreateEmergencyDropdown(row, "Reference", x, 4f,
                        refWidth - 6f, 34f, rooms);
                    reference.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(rooms, rule.reference)));
                    reference.onValueChanged.AddListener(value =>
                    {
                        rule.reference = rooms[Mathf.Clamp(value, 0, rooms.Length - 1)];
                        SaveObjectiveDrafts();
                    });
                    x += refWidth;
                }

                if (rule.type != "unlock_room")
                {
                    var input = CreateEmergencyInput(row, "Value", x, 4f,
                        valueWidth, 34f, rule.value, false);
                    input.contentType = TMP_InputField.ContentType.DecimalNumber;
                    input.onEndEdit.AddListener(value =>
                    {
                        rule.value = value ?? "";
                        SaveObjectiveDrafts();
                    });
                }

                CreateEmergencyButton(row, "Remove", "×", available - 42f, 4f,
                    36f, 34f, EmergencyRemoveColor, () =>
                    {
                        var current = requirement
                            ? CurrentObjective.requirements.ToList()
                            : CurrentObjectiveResults(CurrentObjective).ToList();
                        if (captured < 0 || captured >= current.Count) return;
                        current.RemoveAt(captured);
                        if (requirement) CurrentObjective.requirements = current.ToArray();
                        else SetCurrentObjectiveResults(CurrentObjective, current.ToArray());
                        SaveObjectiveDrafts();
                        if (requirement) RefreshObjectiveRequirements(); else RefreshObjectiveResults();
                    });
            }
        }

        private void CreateObjectiveLockedStarRow(Transform parent, int star, float top, float height)
        {
            var row = NewImage("UP Objective Star Reward", parent, _style.ButtonSprite,
                SettingsSharedPageColor, false).transform as RectTransform;
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -top);
            row.sizeDelta = new Vector2(0f, height - 4f);
            CreateEmergencyLabel(row,
                EditorLocalization.Format("editor.objectives.star_reward", star),
                12f, 0f, -12f, height - 4f, 16f,
                TextAlignmentOptions.MidlineLeft);
        }

        private ScenarioMapFormat.ObjectiveRuleData[] CurrentObjectiveResults(
            ScenarioMapFormat.ObjectiveData draft)
        {
            if (_selectedObjectiveResult == 1) return draft.failure ?? new ScenarioMapFormat.ObjectiveRuleData[0];
            if (_selectedObjectiveResult == 2) return draft.abandon ?? new ScenarioMapFormat.ObjectiveRuleData[0];
            return draft.completion ?? new ScenarioMapFormat.ObjectiveRuleData[0];
        }

        private void SetCurrentObjectiveResults(ScenarioMapFormat.ObjectiveData draft,
            ScenarioMapFormat.ObjectiveRuleData[] values)
        {
            if (_selectedObjectiveResult == 1) draft.failure = values;
            else if (_selectedObjectiveResult == 2) draft.abandon = values;
            else draft.completion = values;
        }

        private static string ObjectiveRequirementLabel(string type) =>
            EditorLocalization.Get("editor.objectives.requirement." + type);

        private static string ObjectiveResultLabel(string type) =>
            EditorLocalization.Get("editor.objectives.result." + type);

        private static string[] ObjectiveRoomOptions()
        {
            var values = Enum.GetNames(typeof(RoomDefinition.Type));
            var result = new string[values.Length + 1];
            result[0] = EditorLocalization.Get("editor.objectives.select_room");
            Array.Copy(values, 0, result, 1, values.Length);
            return result;
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
