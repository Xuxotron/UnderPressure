// Updated: 2026-11-17 - Selector de idioma compartido en General y ajustes exactos del ultimo override.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TH20;
using TH20.ExtContent;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private Button _settingsToggle;
        private GameObject _settingsOverlay;
        private DynamicButton[] _settingsTabButtons;
        private ButtonAnimator[] _settingsTabAnimators;
        private Transform[] _settingsTabRoots;
        private Transform[] _settingsTabFolderBackgrounds;
        private GameObject[] _settingsPages;
        private Transform _settingsCloseButton;
        private bool _settingsClosePending;
        private TMP_InputField _settingsDisplayName;
        private TMP_InputField _settingsAuthor;
        private TMP_InputField _settingsDescriptionPerStar;
        private TMP_InputField _settingsInitialBalance;
        private TMP_InputField _settingsFailWarning;
        private TMP_InputField _settingsFailGameOver;
        private TMP_InputField _settingsInitialTemperature;
        private TMP_InputField _settingsInitialAttractiveness;
        private TMP_InputField _settingsInitialHygiene;
        private static GameObject _cachedSingleLineWorkshopPanel;
        private static Component _cachedSingleLineWorkshopInput;
        private static GameObject _cachedMultilineWorkshopPanel;
        private static Component _cachedMultilineWorkshopInput;
        private RectTransform _generalFlagSelector;
        private RectTransform _generalFlagList;
        private Image _generalSelectedFlag;
        private bool _generalFlagExpanded;
        private int _activeSettingsTab;
        private readonly bool[] _settingsTabContentsCurrent = new bool[6];
        internal static bool CloseSettingsPanelIfOpen()
        {
            if (_active == null || _active._settingsOverlay == null ||
                !_active._settingsOverlay.activeSelf) return false;
            _active.CloseSettingsPanel();
            return true;
        }

        // AJUSTES DEL ESCENARIO: una sola fuente de color compartida.
        // Las seis paginas usan exactamente estos mismos valores; no hay un
        // color independiente por pestana/panel.
        private static readonly Color SettingsOuterPanelColor =
            new Color(.06366315f, .373614669f, .5894737f, 1f);
        private static readonly Color SettingsSharedPageColor =
            new Color(.105882354f, .407843143f, .6f, 1f);
        private static readonly Color SettingsNormalBorderColor =
            new Color(.14f, .62f, .89f, 1f);
        private static readonly Color SettingsLightBorderColor =
            new Color(.407843143f, .75686276f, 1f, 1f);
        private static readonly Color SettingsGeneralTabColor =
            new Color(.215686277f, .7019608f, 1f, 1f);
        private static readonly Color SettingsGeneralTabStepColor =
            new Color(.216000021f, .7029052f, 1f, 1f);

        private void BuildSettingsToolbarButton(Button template)
        {
            _settingsToggle = CloneButton(template, _root, "UP Settings Toolbar Button",
                EditorLocalization.Get("editor.toolbar.settings"), ToggleSettingsPanel);
        }

        private void ToggleSettingsPanel()
        {
            if (_settingsOverlay != null && _settingsOverlay.activeSelf)
            {
                CloseSettingsPanel();
                return;
            }
            ScenarioEditorStorageMenu.CloseOpenDialog();
            if (_settingsOverlay == null) BuildSettingsPanel();
            if (_settingsOverlay == null) return;
            RefreshScenarioSettingsFields();
            SetSettingsTab(0);
            _settingsOverlay.SetActive(true);
            _settingsOverlay.transform.SetAsLastSibling();
            SetSelected(_settingsToggle, true);
        }

        private void CloseSettingsPanel()
        {
            // Enfermedades es una pestaña del mismo editor: al cerrar por Escape,
            // por el botón superior o por la X debe conservar exactamente el mismo
            // borrador que se guardará después dentro del paquete del mapa.
            SaveCurrentLocalizedScenarioInputs();
            if (_level != null && _settingsDiseasesLoaded)
            {
                var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
                StoreDiseaseSettings(settings);
                ScenarioEditorStorage.SetScenarioSettings(_level, settings);
            }
            _settingsClosePending = false;
            ScenarioPlotMapEditor.Close();
            ScenarioCharacterViewer.CloseEmbedded();
            if (_settingsOverlay != null) _settingsOverlay.SetActive(false);
            SetSelected(_settingsToggle, false);
        }

        // AJUSTES DEL ESCENARIO: se clona PreferencesScreen y se conservan su
        // marco, carpetas horizontales y contenido desplazable. El titulo y los
        // botones nativos inferiores se sustituyen por la X nativa superior.
        private void BuildSettingsPanel()
        {
            if (_root == null) return;
            var native = Resources.FindObjectsOfTypeAll<PreferencesScreen>()
                .FirstOrDefault(candidate => candidate != null &&
                    !candidate.transform.IsChildOf(_root) &&
                    GetPreferencesField<Transform>(candidate, "_videoTab") != null &&
                    GetPreferencesField<Transform>(candidate, "_videoTabContents") != null);
            if (native == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ajustes del escenario: no se encontro PreferencesScreen nativo.");
                return;
            }

            _settingsDiseaseNativeToggleTemplate =
                GetPreferencesField<Toggle>(native, "_fullScreenToggle");
            _settingsNativeDropdownTemplate =
                GetPreferencesField<TMP_Dropdown>(native, "_qualitySettingDropdown") ??
                GetPreferencesField<TMP_Dropdown>(native, "_resolutionDropdown");
            _settingsDiseaseNativeScrollbarTemplate = native
                .GetComponentsInChildren<Scrollbar>(true)
                .Where(candidate => candidate != null &&
                    (candidate.direction == Scrollbar.Direction.BottomToTop ||
                     candidate.direction == Scrollbar.Direction.TopToBottom))
                .OrderByDescending(candidate =>
                    Mathf.Abs((candidate.transform as RectTransform)?.rect.height ?? 0f))
                .FirstOrDefault();

            _settingsOverlay = NewImage("UP Scenario Settings Overlay", _root, null,
                new Color32(0, 0, 0, 150), true);
            Stretch((RectTransform)_settingsOverlay.transform);
            _settingsOverlay.SetActive(false);

            var clone = Instantiate(native.gameObject, _settingsOverlay.transform, false);
            clone.name = "UP Native Scenario Settings";
            var cloneRect = clone.transform as RectTransform;
            if (cloneRect != null)
            {
                Stretch(cloneRect);
                cloneRect.localScale = Vector3.one;
            }
            foreach (var nestedCanvas in clone.GetComponentsInChildren<Canvas>(true))
                nestedCanvas.overrideSorting = false;
            var clonedScreen = clone.GetComponent<PreferencesScreen>();
            if (clonedScreen == null)
            {
                Destroy(_settingsOverlay);
                _settingsOverlay = null;
                return;
            }
            clonedScreen.enabled = false;
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var typeName = behaviour.GetType().FullName;
                if (typeName == "UnderPressure.ModSettingsTab") behaviour.enabled = false;
                if (typeName != null && typeName.StartsWith("I2.Loc.", StringComparison.Ordinal))
                    behaviour.enabled = false;
            }

            HideSettingsObject(clone.transform.Find("UnderPressureTabButton"));
            HideSettingsObject(clone.transform.Find("UnderPressureMasterToggle"));
            HideSettingsObjectsByName(clone.transform, "UnderPressureTabButton");
            HideSettingsObjectsByName(clone.transform, "UnderPressureMasterToggle");
            var credits = GetPreferencesField<Button>(clonedScreen, "_creditsButton");
            HideNativeVersionText(clone.transform);

            var titleText = clone.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text != null && text.GetComponentInParent<Selectable>() == null)
                .OrderByDescending(text => text.fontSize)
                .FirstOrDefault();
            if (titleText != null)
            {
                DisableLocalisers(titleText.transform);
                titleText.gameObject.SetActive(false);
            }

            var tabKeys = new[]
            {
                "editor.scenario_settings.tab.general",
                "editor.scenario_settings.tab.diseases",
                "editor.scenario_settings.tab.objectives",
                "editor.scenario_settings.tab.plots",
                "editor.scenario_settings.tab.characters",
                "editor.scenario_settings.tab.characters"
            };
            var tabNames = new[]
            {
                "_videoTab", "_audioTab", "_controlsTab", "_gameTab", "_languageTab"
            };
            var buttonNames = new[]
            {
                "_videoTabButton", "_audioTabButton", "_controlsTabButton",
                "_gameTabButton", "_languageTabButton"
            };
            var animatorNames = new[]
            {
                "_videoTabButtonAnimator", "_audioTabButtonAnimator",
                "_controlsTabButtonAnimator", "_gameTabButtonAnimator",
                "_languageTabButtonAnimator"
            };
            var folderNames = new[]
            {
                "_videoTabFolderBackground", "_audioTabFolderBackground",
                "_controlsTabFolderBackground", "_gameTabFolderBackground"
            };
            var contentsNames = new[]
            {
                "_videoTabContents", "_audioTabContents",
                "_controlsTabContents", "_gameTabContents", "_languageTabContents"
            };
            _settingsTabButtons = new DynamicButton[tabKeys.Length];
            _settingsTabAnimators = new ButtonAnimator[tabKeys.Length];
            _settingsTabRoots = new Transform[tabKeys.Length];
            _settingsTabFolderBackgrounds = new Transform[tabKeys.Length];
            _settingsPages = new GameObject[tabKeys.Length];
            Array.Clear(_settingsTabContentsCurrent, 0, _settingsTabContentsCurrent.Length);
            var nativeVideoContents = GetPreferencesField<Transform>(clonedScreen,
                "_videoTabContents");
            if (nativeVideoContents == null)
            {
                Destroy(_settingsOverlay);
                _settingsOverlay = null;
                return;
            }
            var nativeVideoTab = GetPreferencesField<Transform>(clonedScreen, "_videoTab");
            var settingsPanel = nativeVideoTab != null
                ? nativeVideoTab.parent as RectTransform : null;
            if (settingsPanel != null)
            {
                settingsPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1400f);
                settingsPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 940f);
            }
            for (var index = 0; index < tabKeys.Length; ++index)
            {
                if (index < contentsNames.Length && index > 0)
                    HideSettingsObject(GetPreferencesField<Transform>(clonedScreen,
                        contentsNames[index]));
                var page = index == 0
                    ? nativeVideoContents
                    : Instantiate(nativeVideoContents, nativeVideoContents.parent, false);
                page.name = "UP Scenario Settings Contents " + index;
                _settingsPages[index] = page.gameObject;
            }
            var languageRoot = GetPreferencesField<Transform>(clonedScreen, "_languageTab");
            var languageButton = GetPreferencesField<DynamicButton>(clonedScreen,
                "_languageTabButton");
            var languageAnimator = GetPreferencesField<ButtonAnimator>(clonedScreen,
                "_languageTabButtonAnimator");
            var languageFolder = GetPreferencesField<Transform>(clonedScreen,
                "_languageTabFolderBackground");
            var characterHolder = languageButton != null && languageButton.transform.parent != null
                ? Instantiate(languageButton.transform.parent.gameObject,
                    languageButton.transform.parent.parent, false)
                : null;
            if (characterHolder != null) characterHolder.name = "UP Characters Tab Holder";
            var characterButton = characterHolder != null
                ? characterHolder.GetComponentInChildren<DynamicButton>(true) : null;
            var characterAnimator = characterHolder != null
                ? characterHolder.GetComponentInChildren<ButtonAnimator>(true) : null;
            var emergencyFolder = GetPreferencesField<Transform>(clonedScreen,
                "_gameTabFolderBackground");
            emergencyFolder = emergencyFolder != null
                ? Instantiate(emergencyFolder.gameObject, emergencyFolder.parent, false).transform : null;
            if (emergencyFolder != null) emergencyFolder.name = "UP Emergencies Tab";

            for (var index = 0; index < tabKeys.Length; ++index)
            {
                var tabIndex = index;
                var title = index == 4
                    ? (EditorLocalization.CurrentLanguageCode() == "es"
                        ? "Emergencias"
                        : "Emergencies")
                    : EditorLocalization.Get(tabKeys[index]);
                var nativeIndex = Mathf.Min(index, tabNames.Length - 1);
                var root = index < tabNames.Length
                    ? GetPreferencesField<Transform>(clonedScreen, tabNames[nativeIndex])
                    : languageRoot;
                var button = index < buttonNames.Length
                    ? GetPreferencesField<DynamicButton>(clonedScreen, buttonNames[nativeIndex])
                    : characterButton;
                var animator = index < animatorNames.Length
                    ? GetPreferencesField<ButtonAnimator>(clonedScreen, animatorNames[nativeIndex])
                    : characterAnimator;
                Transform folder;
                if (index < folderNames.Length)
                    folder = GetPreferencesField<Transform>(clonedScreen, folderNames[index]);
                else
                    folder = index == 4 ? emergencyFolder : languageFolder;
                var contents = _settingsPages[index]?.transform;
                if (root == null || button == null || contents == null) continue;
                DisableLocalisers(root);
                button.onPrimaryDown.RemoveAllListeners();
                button.onSecondaryDown.RemoveAllListeners();
                button.onPrimaryDownFailed.RemoveAllListeners();
                button.onPrimaryDown.AddListener(() => SetSettingsTab(tabIndex));
                button.SetTMPText(title);
                _settingsTabButtons[index] = button;
                _settingsTabAnimators[index] = animator;
                _settingsTabRoots[index] = root;
                _settingsTabFolderBackgrounds[index] = folder;
                ClearNativeSettingsContents(contents, index == 0 ? null : title);
            }
            BuildGeneralSettingsContents();
            BuildDiseaseSettingsContents();
            BuildObjectiveSettingsContents();
            BuildPlotSettingsContents();
            BuildEmergencySettingsContents();
            BuildCharacterSettingsContents();
            // Parcelas construye su contenido completo durante BuildPlotSettingsContents.
            _settingsTabContentsCurrent[3] = true;

            var close = GetPreferencesField<Button>(clonedScreen, "_closeButton");
            HideSettingsObject(close);
            HideSettingsObject(credits);
            ExpandNativeSettingsContents(clonedScreen);
            RedistributeNativeSettingsTabs(clonedScreen);
            _settingsCloseButton = CreateNativeSettingsCloseButton(
                settingsPanel != null ? settingsPanel : clone.transform);
            ApplyNativeSettingsPalette(clone.transform);
            ApplySettingsContentFrameColor();
            ApplyLatestSavedSettingsUi();
            ReapplySettingsDesignerStructure();
            SetSettingsTab(0);
            clone.SetActive(true);
            _settingsOverlay.SetActive(false);
        }

        private void SetSettingsTab(int selectedIndex)
        {
            if (_settingsTabButtons == null || _settingsPages == null) return;
            if (selectedIndex != 0) SetGeneralFlagExpanded(false);
            if (selectedIndex != ObjectiveTabIndex) SetObjectiveFlagExpanded(false);
            if (selectedIndex != 3) ScenarioPlotMapEditor.Close();
            if (selectedIndex != 5) ScenarioCharacterViewer.CloseEmbedded();
            _activeSettingsTab = Mathf.Clamp(selectedIndex, 0,
                Mathf.Min(_settingsTabButtons.Length, _settingsPages.Length) - 1);
            if (_activeSettingsTab == 1)
            {
                RefreshDiseaseSettingsContents();
                _settingsTabContentsCurrent[1] = true;
            }
            if (_activeSettingsTab == 2 && !_settingsTabContentsCurrent[2])
            {
                RefreshObjectiveSettingsContents();
                _settingsTabContentsCurrent[2] = true;
            }
            if (_activeSettingsTab == 3 && !_settingsTabContentsCurrent[3])
            {
                RefreshPlotSettingsContents();
                _settingsTabContentsCurrent[3] = true;
            }
            if (_activeSettingsTab == 4 && !_settingsTabContentsCurrent[4])
            {
                RefreshEmergencySettingsContents();
                _settingsTabContentsCurrent[4] = true;
            }

            for (var index = 0; index < _settingsTabButtons.Length; ++index)
            {
                var selected = index == _activeSettingsTab;
                var button = _settingsTabButtons[index];
                var animator = _settingsTabAnimators[index];
                var folder = _settingsTabFolderBackgrounds[index];

                if (animator != null)
                {
                    animator.CurrentState = selected
                        ? (ButtonAnimator.State)1 : (ButtonAnimator.State)0;
                    animator.enabled = false;
                }

                if (folder != null)
                    folder.gameObject.SetActive(selected);

                if (index == 1)
                {
                    if (_settingsPages[index] != null)
                        _settingsPages[index].SetActive(selected);

                    if (_settingsDiseaseSurface != null)
                        _settingsDiseaseSurface.gameObject.SetActive(selected);
                }
                else if (_settingsPages[index] != null)
                {
                    _settingsPages[index].SetActive(selected);
                }
            }

            _settingsTabRoots[_activeSettingsTab]?.SetAsLastSibling();

            if (_activeSettingsTab == 1)
                _settingsDiseaseSurface?.SetAsLastSibling();
            else
                _settingsPages[_activeSettingsTab]?.transform.SetAsLastSibling();

            if (_activeSettingsTab == 5)
                ScenarioCharacterViewer.OpenEmbedded(
                    _settingsPages[5].transform as RectTransform,
                    _style.ButtonSprite, _style.PanelColor,
                    _style.ButtonColor, _style.SelectedColor);

            _settingsCloseButton?.SetAsLastSibling();
        }

        private void BuildCharacterSettingsContents()
        {
            if (_settingsPages == null || _settingsPages.Length <= 5 ||
                _settingsPages[5] == null) return;
            var nativeScroll = _settingsPages[5].GetComponentInChildren<ScrollRect>(true);
            if (nativeScroll != null) nativeScroll.gameObject.SetActive(false);
        }

        // Estado visual guardado por RuntimeUiDesigner V6.
        // Conserva el panel y los cortes nativos de las seis pestanas: solo se
        // tintan sus Images; nunca se sustituyen sus sprites por rectangulos.
        private void ApplyLatestSavedSettingsUi()
        {
            var nativeSettings = _settingsOverlay != null
                ? _settingsOverlay.transform.Find("UP Native Scenario Settings")
                : null;

            if (nativeSettings != null)
            {
                var backing = nativeSettings.Find("Backing");
                var backingImage = backing != null
                    ? backing.GetComponent<Image>()
                    : null;
                if (backingImage != null)
                    backingImage.color = new Color(0f, 0f, 0f, .723f);

                var panel = nativeSettings.Find("Panel");
                var panelImage = panel != null
                    ? panel.GetComponent<Image>()
                    : null;
                if (panelImage != null)
                    panelImage.color = SettingsOuterPanelColor;

                var videoTab =
                    nativeSettings.Find("Panel/VideoTab") as RectTransform;

                if (videoTab != null)
                {
                    videoTab.anchorMin = Vector2.zero;
                    videoTab.anchorMax = Vector2.one;
                    videoTab.pivot = new Vector2(.5f, .5f);
                    videoTab.anchoredPosition = new Vector2(-4f, -34f);
                    videoTab.sizeDelta = new Vector2(-58f, -142f);
                }
            }

            if (_settingsTabButtons != null &&
                _settingsTabButtons.Length > 0 &&
                _settingsTabButtons[0] != null)
            {
                var generalText =
                    _settingsTabButtons[0]
                        .GetComponentsInChildren<TMP_Text>(true)
                        .FirstOrDefault();

                if (generalText != null)
                {
                    generalText.fontSize = 30.35f;
                    generalText.enableAutoSizing = true;
                    generalText.fontSizeMin = 8f;
                    generalText.fontSizeMax = 72f;
                }
            }

            // Se conserva tambien el ajuste anterior de Enfermedades.
            if (_settingsTabButtons != null &&
                _settingsTabButtons.Length > 1 &&
                _settingsTabButtons[1] != null)
            {
                var diseasesText =
                    _settingsTabButtons[1]
                        .GetComponentsInChildren<TMP_Text>(true)
                        .FirstOrDefault();

                if (diseasesText != null)
                {
                    diseasesText.fontSize = 26.35f;
                    diseasesText.enableAutoSizing = true;
                    diseasesText.fontSizeMin = 8f;
                    diseasesText.fontSizeMax = 72f;
                }
            }

            ApplyVideoTabFolderColorsToAllSettingsTabs();
        }

        private void ApplySharedSettingsPageStyle(
            Color sharedColor)
        {
            if (_settingsPages == null)
                return;

            for (var index = 0;
                 index < _settingsPages.Length;
                 ++index)
            {
                var page = _settingsPages[index];
                if (page == null)
                    continue;

                var frame = page.GetComponent<Image>();
                if (frame != null)
                    frame.color = sharedColor;

                var viewport =
                    page.transform.Find("Scroll View/Viewport");
                var viewportImage =
                    viewport != null
                        ? viewport.GetComponent<Image>()
                        : null;

                if (viewportImage != null)
                    viewportImage.color = sharedColor;
            }
        }

        private bool IsSettingsPanelSurface(
            Image image)
        {
            if (image == null)
                return false;

            var transform = image.transform;

            if (IsSettingsPageRoot(transform))
                return true;

            if (transform.name == "Cabecera" ||
                transform.name == "Header" ||
                transform.name.IndexOf(
                    "Scrollbar",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (image.GetComponent<Selectable>() != null ||
                image.GetComponentInParent<Selectable>() != null ||
                image.GetComponentInParent<InputField>() != null ||
                image.GetComponentInParent<TMP_InputField>() != null ||
                IsWorkshopSettingsField(transform) ||
                IsDiseaseNativeScrollbar(transform))
                return false;

            var sprite =
                image.overrideSprite != null
                    ? image.overrideSprite
                    : image.sprite;

            if (_style == null ||
                _style.ButtonSprite == null ||
                sprite != _style.ButtonSprite)
                return false;

            // Cualquier superficie no seleccionable que use el sprite
            // compartido de panel pertenece a la misma familia visual,
            // independientemente de su alto (filas, bloques y paneles grandes).
            return true;
        }

        private void ApplyVideoTabFolderColorsToAllSettingsTabs()
        {
            if (_settingsTabFolderBackgrounds == null)
                return;

            foreach (var folder in _settingsTabFolderBackgrounds)
            {
                if (folder == null)
                    continue;

                foreach (var image in
                         folder.GetComponentsInChildren<Image>(true))
                {
                    if (image == null ||
                        !IsSettingsTabSliceName(image.gameObject.name))
                        continue;

                    image.color =
                        image.gameObject.name == "TabRightStep"
                            ? SettingsGeneralTabStepColor
                            : SettingsGeneralTabColor;
                }
            }
        }

        private static bool IsSettingsTabSliceName(
            string objectName)
        {
            return objectName == "TabFlushLeft" ||
                   objectName == "TabLeft" ||
                   objectName == "TabLeftStep" ||
                   objectName == "TabMiddle" ||
                   objectName == "TabRight" ||
                   objectName == "TabRightStep" ||
                   objectName == "TabFlushRight";
        }

        private static T GetPreferencesField<T>(PreferencesScreen screen, string name)
            where T : class
        {
            return AccessTools.Field(typeof(PreferencesScreen), name)?.GetValue(screen) as T;
        }

        private static void HideSettingsObject(Component value)
        {
            if (value != null) value.gameObject.SetActive(false);
        }

        private static void HideSettingsObjectsByName(Transform root, string name)
        {
            if (root == null) return;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child != null && child.name == name) child.gameObject.SetActive(false);
        }

        private static void DisableLocalisers(Transform root)
        {
            if (root == null) return;
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName != null &&
                    component.GetType().FullName.StartsWith("I2.Loc.", StringComparison.Ordinal))
                    component.enabled = false;
        }

        private static void HideNativeVersionText(Transform root)
        {
            if (root == null) return;
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().FullName !=
                    "TH20.SetTextToVersionNumber") continue;
                component.gameObject.SetActive(false);
            }
        }

        private static void ClearNativeSettingsContents(Transform contents, string title)
        {
            if (contents == null) return;
            DisableLocalisers(contents);
            var content = contents.Find("Scroll View/Viewport/Content");
            if (content == null) return;
            foreach (var layout in content.GetComponents<LayoutGroup>()) layout.enabled = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            for (var index = content.childCount - 1; index >= 0; --index)
            {
                var child = content.GetChild(index);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            var contentRect = content as RectTransform;
            if (contentRect != null)
                contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 430f);
            if (!string.IsNullOrEmpty(title))
                CreateSettingsLabel(content, title, 26f, TextAlignmentOptions.TopLeft,
                    new Vector2(22f, -18f));
        }

        private void ExpandNativeSettingsContents(PreferencesScreen screen)
        {
            if (screen == null) return;
            var firstButton = GetPreferencesField<DynamicButton>(screen, "_videoTabButton");
            var tabRow = firstButton != null && firstButton.transform.parent != null
                ? firstButton.transform.parent.parent as RectTransform : null;
            if (tabRow != null)
                tabRow.anchoredPosition += new Vector2(0f, 63f);

            var folder = GetPreferencesField<Transform>(screen,
                "_videoTabFolderBackground")?.parent as RectTransform;
            if (folder != null)
                folder.sizeDelta += new Vector2(0f, 126f);

            if (_settingsTabRoots != null)
            {
                foreach (var root in _settingsTabRoots.Distinct())
                {
                    var rect = root as RectTransform;
                    if (rect != null) rect.sizeDelta += new Vector2(0f, 126f);
                }
            }
        }

        private void RedistributeNativeSettingsTabs(PreferencesScreen screen)
        {
            if (screen == null || _settingsTabFolderBackgrounds == null ||
_settingsTabFolderBackgrounds.Length < 3) return;
            var firstFolder = GetPreferencesField<Transform>(screen,
                "_videoTabFolderBackground");
            var commonFolder = firstFolder != null ? firstFolder.parent as RectTransform : null;
            if (commonFolder == null) return;

            // Las piezas extremas nativas dejan 130 px hasta el centro de la
            // primera y la ultima pestaña. Las cuatro intermedias se reparten
            // uniformemente entre ambos centros.
            var usableWidth = commonFolder.rect.width > 0f
                ? commonFolder.rect.width : 1180f;
            var firstCenter = 130f;
            var pitch = (usableWidth - firstCenter * 2f) /
                (_settingsTabFolderBackgrounds.Length - 1f);
            for (var index = 1; index < _settingsTabFolderBackgrounds.Length - 1;
                ++index)
            {
                var folder = _settingsTabFolderBackgrounds[index];
                if (folder == null || folder.childCount == 0) continue;
                var left = folder.GetChild(0).GetComponent<LayoutElement>();
                if (left == null) continue;
                var center = firstCenter + pitch * index;
                var width = center - 142f;
                left.minWidth = width;
                left.preferredWidth = width;
                left.flexibleWidth = -1f;
            }
            var tabRow = GetPreferencesField<DynamicButton>(screen,
                "_videoTabButton")?.transform.parent?.parent as RectTransform;
            if (tabRow != null) LayoutRebuilder.ForceRebuildLayoutImmediate(tabRow);
            LayoutRebuilder.ForceRebuildLayoutImmediate(commonFolder);
        }

        private Transform CreateNativeSettingsCloseButton(Transform parent)
        {
            if (parent == null) return null;
            var closeField = AccessTools.Field(typeof(MenuBase), "_closeMenuButton");
            var nativeCloseButtons = Resources.FindObjectsOfTypeAll<MenuBase>()
                .Select(menu => closeField?.GetValue(menu) as DynamicButton)
                .Where(button => button != null)
                .Distinct().ToArray();
            var template = nativeCloseButtons.FirstOrDefault(button =>
                button.GetComponentsInChildren<Image>(true).Any(image =>
                    IsNativeCloseSprite(image, "T_UI_Button_Close_Red")));
            if (template == null)
                template = nativeCloseButtons.FirstOrDefault(button =>
                    button.GetComponentsInChildren<Image>(true).Any(image =>
                        IsNativeCloseSprite(image, "T_UI_Button_Close")));
            if (template == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ajustes del escenario: no se encontro el DynamicButton X de MenuBase.");
                return null;
            }

            // Se clona exactamente el DynamicButton que MenuBase usa como cierre,
            // incluidos su sombra, animador, dimensiones y escala nativos.
            var closeObject = Instantiate(template.gameObject, parent, false);
            closeObject.name = "UP Native Settings Close";
            closeObject.SetActive(true);
            var rect = closeObject.transform as RectTransform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            DisableLocalisers(closeObject.transform);
            var dynamicButton = closeObject.GetComponent<DynamicButton>();
            dynamicButton.onPrimaryDown.RemoveAllListeners();
            dynamicButton.onSecondaryDown.RemoveAllListeners();
            dynamicButton.onPrimaryDownFailed.RemoveAllListeners();
            dynamicButton.onPrimaryDown.AddListener(RequestAcceptScenarioSettings);
            return closeObject.transform;
        }

        private void RequestAcceptScenarioSettings()
        {
            if (_settingsClosePending) return;
            _settingsClosePending = true;
            StartCoroutine(AcceptScenarioSettingsAfterPointerRelease());
        }

        private System.Collections.IEnumerator AcceptScenarioSettingsAfterPointerRelease()
        {
            // MenuBase mantiene el menu activo durante su cierre. Aqui se espera
            // igualmente a que termine la pulsacion para que no atraviese al mapa.
            while (Input.GetMouseButton(0)) yield return null;
            yield return null;
            _settingsClosePending = false;
            AcceptScenarioSettings();
        }

        private static bool IsNativeCloseSprite(Image image, string spriteName)
        {
            if (image == null || string.IsNullOrEmpty(spriteName)) return false;
            return string.Equals(image.overrideSprite?.name, spriteName,
                       StringComparison.Ordinal) ||
                   string.Equals(image.sprite?.name, spriteName, StringComparison.Ordinal);
        }

        private void ApplyNativeSettingsPalette(Transform root)
        {
            if (root == null) return;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.GetComponentInParent<InputField>() != null ||
                    image.GetComponentInParent<TMP_InputField>() != null ||
                    IsWorkshopSettingsField(image.transform) ||
                    IsDiseaseNativeScrollbar(image.transform)) continue;
                var interior = IsSettingsInterior(image.transform);
                if (interior)
                {
                    if (IsSettingsPanelSurface(image))
                        image.color = SettingsSharedPageColor;
                    continue;
                }
                if (image.GetComponent<Selectable>() != null ||
                    image.GetComponentInParent<Selectable>() != null) continue;
                var color = image.color;
                var maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                var minimum = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
                if (color.a < .85f || maximum - minimum > .08f || maximum > .92f) continue;
                var tint = _style.HeaderColor;
                tint.a = color.a;
                var rect = image.rectTransform.rect;
                if (!IsSettingsStructuralImage(image.transform) &&
                    rect.width >= 400f && rect.height >= 150f)
                {
                    image.overrideSprite = null;
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                }
                image.color = tint;
            }
            foreach (var outline in root.GetComponentsInChildren<Outline>(true))
            {
                if (outline == null || !IsSettingsInterior(outline.transform) ||
                    IsSettingsStructuralImage(outline.transform) ||
                    outline.GetComponentInParent<InputField>() != null ||
                    outline.GetComponentInParent<TMP_InputField>() != null ||
                    IsWorkshopSettingsField(outline.transform)) continue;
                outline.effectColor = SettingsNormalBorderColor;
            }
        }

        private static bool IsWorkshopSettingsField(Transform value)
        {
            for (var current = value; current != null; current = current.parent)
                if (current.name.StartsWith("UP Workshop Settings Field ",
                        StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsDiseaseNativeScrollbar(Transform value)
        {
            for (var current = value; current != null; current = current.parent)
                if (current.name.StartsWith("UP Disease Native Scrollbar",
                        StringComparison.Ordinal)) return true;
            return false;
        }

        private bool IsSettingsStructuralImage(
            Transform value)
        {
            if (value == null)
                return false;

            if (_settingsTabFolderBackgrounds != null &&
                _settingsTabFolderBackgrounds.Any(folder =>
                    folder != null &&
                    (value == folder ||
                     value.IsChildOf(folder))))
                return true;

            if (value.name == "Panel" &&
                value.parent != null &&
                value.parent.name == "UP Native Scenario Settings")
                return true;

            if (value.name == "Image" &&
                value.parent != null &&
                value.parent.name == "Panel")
                return true;

            return false;
        }

        private bool IsSettingsInterior(Transform value)
        {
            if (value == null) return false;
            if (_settingsPages != null && _settingsPages.Any(page =>
                    page != null && (value == page.transform || value.IsChildOf(page.transform))))
                return true;
            return _settingsTabFolderBackgrounds != null &&
                _settingsTabFolderBackgrounds.Any(folder => folder != null &&
                    (value == folder || value.IsChildOf(folder)));
        }

        private bool IsSettingsPageRoot(Transform value)
        {
            return value != null && _settingsPages != null &&
                _settingsPages.Any(page => page != null && value == page.transform);
        }

        private void ApplySettingsContentFrameColor()
        {
            ApplySharedSettingsPageStyle(
                SettingsSharedPageColor);
        }

        // Solo se llama cuando se crea o reconstruye UI de Ajustes.
        // RuntimeUiDesigner resuelve las rutas guardadas directamente; no
        // recorre la escena ni se ejecuta por cambiar una pestana ya creada.
        private void ReapplySettingsDesignerStructure()
        {
            if (_settingsOverlay == null)
                return;

            UnderPressure.RuntimeUiDesignerPlugin
                .ReapplyPersistentStructureNow(
                    _settingsOverlay.transform);
        }

        private void BuildGeneralSettingsContents()
        {
            if (_settingsPages == null || _settingsPages.Length == 0 ||
                _settingsPages[0] == null) return;
            var content = _settingsPages[0].transform.Find(
                "Scroll View/Viewport/Content");
            if (content == null) return;

            _settingsDisplayName = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.display_name", 16f, 48f, false,
                false, .02f, .50f);
            _settingsAuthor = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.author", 16f, 48f, false,
                false, .52f, .98f);
            SetScenarioSettingsInputLabel(_settingsAuthor, "Autor");
            _settingsDescriptionPerStar = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.description_per_star", 70f, 126f, true,
                false, .02f, .98f);

            ApplyGeneralFlagOverrideGeometry();
            BuildGeneralFlagSelector(content);

            _settingsInitialBalance = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.initial_balance", 202f, 48f, false, true,
                .02f, .32f, 130f);
            _settingsFailWarning = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.fail_warning", 202f, 48f, false, true,
                .35f, .65f, 130f);
            ApplyScenarioSettingsLabelV7(_settingsFailWarning, 23.35f, 12f, 32f);
            _settingsFailGameOver = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.fail_game_over", 202f, 48f, false, true,
                .68f, .98f, 130f);
            _settingsInitialTemperature = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.initial_temperature", 258f, 48f, false,
                false, .02f, .32f, 130f);
            _settingsInitialAttractiveness = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.initial_attractiveness", 258f, 48f, false,
                false, .35f, .65f, 130f);
            _settingsInitialHygiene = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.initial_hygiene", 258f, 48f, false,
                false, .68f, .98f, 130f);
            SetScenarioDecimalContentType(_settingsInitialTemperature);
            SetScenarioDecimalContentType(_settingsInitialAttractiveness);
            SetScenarioDecimalContentType(_settingsInitialHygiene);
            BuildGeneralStyleSettings(content);
            BuildGeneralRoomAvailabilitySettings(content);
            RefreshScenarioSettingsFields();
            RefreshSharedFlagSelection();
        }

        private void ApplyGeneralFlagOverrideGeometry()
        {
            var nameRow = FindScenarioSettingsRow(_settingsDisplayName);
            if (nameRow != null)
            {
                nameRow.anchorMin = new Vector2(.02f, 1f);
                nameRow.anchorMax = new Vector2(.5f, 1f);
                nameRow.pivot = new Vector2(.5f, 1f);
                nameRow.anchoredPosition = new Vector2(48f, -16f);
                nameRow.sizeDelta = new Vector2(-70f, 48f);
            }

            var descriptionRow = FindScenarioSettingsRow(
                _settingsDescriptionPerStar);
            if (descriptionRow != null)
            {
                descriptionRow.anchorMin = new Vector2(.02f, 1f);
                descriptionRow.anchorMax = new Vector2(.98f, 1f);
                descriptionRow.pivot = new Vector2(.5f, 1f);
                descriptionRow.anchoredPosition = new Vector2(0f, -70f);
                descriptionRow.sizeDelta = new Vector2(0f, 126f);

                var inputContainer = descriptionRow.Find(
                    "LayoutGroup/DescPanel/InputFieldContainer") as RectTransform;
                if (inputContainer != null)
                {
                    inputContainer.anchorMin = Vector2.zero;
                    inputContainer.anchorMax = Vector2.one;
                    inputContainer.pivot = new Vector2(.5f, .5f);
                    inputContainer.anchoredPosition =
                        new Vector2(-42.612915f, .487548828f);
                    inputContainer.sizeDelta =
                        new Vector2(43.8739967f, -20.7f);
                }
            }
        }

        private static RectTransform FindScenarioSettingsRow(
            TMP_InputField input)
        {
            if (input == null) return null;
            var current = input.transform;
            while (current != null &&
                   !current.name.StartsWith(
                       "UP Workshop Settings Field ",
                       StringComparison.Ordinal))
                current = current.parent;
            return current as RectTransform;
        }

        private void BuildGeneralFlagSelector(Transform content)
        {
            if (content == null) return;
            var scrollView = content.parent != null
                ? content.parent.parent : null;
            if (scrollView == null) return;

            var container = new GameObject(
                "UP Objective Star Flag Selector Copy",
                typeof(RectTransform));
            container.transform.SetParent(scrollView, false);
            _generalFlagSelector = container.transform as RectTransform;
            SetObjectiveExactRect(
                _generalFlagSelector,
                Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-1229f, -4f),
                new Vector2(75f, 70f));
            _generalFlagSelector.SetAsLastSibling();

            var hit = new GameObject(
                "UP Objective Flag Hit Area",
                typeof(RectTransform), typeof(Image), typeof(Button));
            hit.transform.SetParent(_generalFlagSelector, false);
            var hitRect = hit.transform as RectTransform;
            SetTopLeft(hitRect, -16f, 0f, 100f, 70f);
            var hitImage = hit.GetComponent<Image>();
            hitImage.color = new Color(0f, 0f, 0f, 0f);
            hitImage.raycastTarget = true;
            var hitButton = hit.GetComponent<Button>();
            hitButton.transition = Selectable.Transition.None;
            hitButton.onClick.AddListener(() =>
            {
                SetGeneralFlagExpanded(!_generalFlagExpanded);
            });

            var open = new GameObject(
                "UP Objective Flag Open", typeof(RectTransform));
            open.transform.SetParent(_generalFlagSelector, false);
            SetObjectiveExactRect(
                open.transform as RectTransform,
                Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f),
                new Vector2(-37.5f, 0f),
                new Vector2(-75f, 0f));

            var arrow = CreateObjectiveLabel(
                open.transform, "▼",
                -13f, 17f, 28f, 36f, 18f,
                TextAlignmentOptions.Center,
                "UP Objective Flag Arrow");
            SetObjectiveExactRect(
                arrow.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(-13f, -17f),
                new Vector2(28f, 36f));
            arrow.raycastTarget = false;

            _generalSelectedFlag = CreateObjectiveFlagImage(
                open.transform,
                "UP Objective Selected Flag",
                11f, 19f, 61f, 36f);
            SetObjectiveExactRect(
                _generalSelectedFlag.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(11f, -19f),
                new Vector2(61f, 36f));

            const float rowHeight = 40f;
            const float buttonHeight = 40f;
            const float dropdownWidth = 315f;
            const float dropdownHeight = 484f;
            var total = ObjectiveFlagFileNames.Length;

            var listObject = NewImage(
                "UP Objective Flags Dropdown",
                _generalFlagSelector,
                _style.ButtonSprite, ObjectivePanelColor, true);
            _generalFlagList = listObject.transform as RectTransform;
            SetTopLeft(
                _generalFlagList, 0f, 72f,
                dropdownWidth, dropdownHeight);
            _generalFlagList.SetAsLastSibling();

            RectTransform rows;
            var scroll = CreateObjectiveVerticalScroll(
                _generalFlagList,
                "UP Objective Flag Choices Scroll",
                2f, 2f, 2f, 2f, out rows);
            if (scroll.viewport != null)
            {
                SetObjectiveExactRect(
                    scroll.viewport,
                    Vector2.zero, Vector2.one,
                    new Vector2(.5f, .5f),
                    new Vector2(60.5f, 0f),
                    new Vector2(121f, 0f));
            }
            scroll.vertical = false;
            rows.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, 480f);

            for (var index = 0; index < total; ++index)
            {
                var captured = index;
                var entry = CreateObjectiveButton(
                    rows,
                    "UP Objective Flag Choice " + index,
                    "", 0f, index * rowHeight,
                    309f, rowHeight - 2f,
                    ObjectivePanelColor,
                    () => SelectObjectiveFlag(captured));
                var entryRect = entry.transform as RectTransform;
                entryRect.anchorMin = new Vector2(0f, 1f);
                entryRect.anchorMax = new Vector2(1f, 1f);
                entryRect.pivot = new Vector2(.5f, 1f);
                entryRect.anchoredPosition =
                    new Vector2(0f, -index * rowHeight);
                entryRect.sizeDelta =
                    new Vector2(-2f, rowHeight - 2f);

                var flag = CreateObjectiveFlagImage(
                    entry.transform,
                    "UP Objective Flag Icon " + index,
                    7f, 1f, 61f, 36f);
                flag.sprite = LoadObjectiveFlag(
                    ObjectiveFlagFileNames[index]);

                var label = CreateObjectiveLabel(
                    entry.transform,
                    ObjectiveNativeFlagName(index),
                    76f, 0f, 225f, rowHeight - 2f,
                    18f, TextAlignmentOptions.MidlineLeft,
                    "UP General Flag Language " + index);
                label.raycastTarget = false;
                _generalFlagLanguageLabels[index] = label;
            }

            _generalTranslateLanguagesButton = CreateObjectiveButton(
                rows,
                "UP Objective Translate Languages",
                EditorLocalization.Get("editor.scenario_settings.translate_languages"),
                0f, total * rowHeight,
                309f, buttonHeight - 2f,
                _style.ButtonColor, ShowTranslateLanguagesConfirmation);
            var translateRect = _generalTranslateLanguagesButton.transform as RectTransform;
            translateRect.anchorMin = new Vector2(0f, 1f);
            translateRect.anchorMax = new Vector2(1f, 1f);
            translateRect.pivot = new Vector2(.5f, 1f);
            translateRect.anchoredPosition =
                new Vector2(0f, -total * rowHeight);
            translateRect.sizeDelta =
                new Vector2(-2f, buttonHeight - 2f);

            _generalFlagList.gameObject.SetActive(false);
            RefreshSharedFlagSelection();
        }

        private void SetGeneralFlagExpanded(bool expanded)
        {
            _generalFlagExpanded = expanded;
            if (_generalFlagList != null)
                _generalFlagList.gameObject.SetActive(expanded);
            if (!expanded || _generalFlagSelector == null) return;
            RefreshSharedFlagLanguageLabels();
            _generalFlagSelector.SetAsLastSibling();
            if (_generalFlagList != null)
                _generalFlagList.SetAsLastSibling();
        }

        private void BuildGeneralStyleSettings(Transform content)
        {
            var contentRect = content as RectTransform;
            if (contentRect != null)
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x,
                    Mathf.Max(contentRect.sizeDelta.y, 802f));
            var catalog = ScenarioHospitalStyles.Build(_level,
                EditorLocalization.Get("editor.map.style.original"),
                EditorLocalization.Get("editor.map.style.none"));
            CreateGeneralStyleRow(content, "editor.map.style.perimeter", 334f,
                catalog.Perimeters.Select(choice => choice.Label).ToList(),
                () =>
                {
                    var current = ScenarioEditorStorage.CurrentStyles(_level);
                    return catalog.Perimeters.FindIndex(choice =>
                        choice.Mode == current.plotPerimeter &&
                        (choice.Mode != ScenarioMapFormat.PerimeterCustom ||
                         choice.PrefabNames.SequenceEqual(current.perimeterPrefabs)));
                }, index =>
                {
                    var choice = catalog.Perimeters[index];
                    var current = ScenarioEditorStorage.CurrentStyles(_level);
                    current.plotPerimeter = choice.Mode;
                    current.perimeterPrefabs = choice.PrefabNames ?? new string[0];
                    ScenarioEditorStorage.SetStyles(_level, current);
                });
            CreateGeneralRoomStyleRow(content, "editor.map.style.built", 390f,
                catalog.Built, map => map.builtRoomStyle,
                (map, value) => map.builtRoomStyle = value);
            CreateGeneralRoomStyleRow(content, "editor.map.style.building", 446f,
                catalog.Building, map => map.buildingRoomStyle,
                (map, value) => map.buildingRoomStyle = value);
            CreateGeneralRoomStyleRow(content, "editor.map.style.unbuilt", 502f,
                catalog.Unbuilt, map => map.unbuiltRoomStyle,
                (map, value) => map.unbuiltRoomStyle = value);
        }

        private void CreateGeneralRoomStyleRow(Transform content, string labelKey, float top,
            IList<ScenarioHospitalStyles.RoomChoice> choices,
            Func<ScenarioMapFormat.Map, string> get,
            Action<ScenarioMapFormat.Map, string> set)
        {
            CreateGeneralStyleRow(content, labelKey, top,
                choices.Select(choice => choice.Label).ToList(),
                () => choices.ToList().FindIndex(choice =>
                    string.Equals(choice.Id,
                        get(ScenarioEditorStorage.CurrentStyles(_level)),
                        StringComparison.Ordinal)),
                index =>
                {
                    var current = ScenarioEditorStorage.CurrentStyles(_level);
                    set(current, choices[index].Id);
                    ScenarioEditorStorage.SetStyles(_level, current);
                });
        }

        private void CreateGeneralStyleRow(Transform content, string labelKey, float top,
            IList<string> labels, Func<int> currentIndex, Action<int> select)
        {
            if (labels == null || labels.Count == 0) return;
            var label = CreateDiseaseText(content,
                EditorLocalization.Get(labelKey) + ":", 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            (label.gameObject.GetComponent<LayoutElement>() ??
                label.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
            SetRect(label.rectTransform, new Vector2(.02f, 1f), new Vector2(.34f, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, 44f));
            var index = Mathf.Clamp(currentIndex(), 0, labels.Count - 1);
            Button button = null;
            button = CreateDiseaseButton(content, labels[index], _style.ButtonColor, () =>
            {
                index = (index + 1) % labels.Count;
                select(index);
                var text = button.GetComponentInChildren<TMP_Text>(true);
                if (text != null) text.text = labels[index];
            });
            (button.gameObject.GetComponent<LayoutElement>() ??
                button.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
            AddPlotButtonOutline(button);
            SetRect(button.transform as RectTransform,
                new Vector2(.36f, 1f), new Vector2(.98f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -top), new Vector2(0f, 44f));
        }

        private TMP_InputField CreateScenarioSettingsInput(Transform parent, string labelKey,
            float top, float height, bool multiline, bool integer = false,
            float anchorMinX = .02f, float anchorMaxX = .98f,
            float inputWidth = -1f)
        {
            GameObject panelTemplate;
            Component nativeInputTemplate;
            if (!TryGetWorkshopFieldTemplate(multiline, out panelTemplate,
                    out nativeInputTemplate))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ajustes del escenario: no se encontro el panel de escritura nativo del Workshop.");
                return null;
            }

            var row = Instantiate(panelTemplate, parent, false);
            row.name = "UP Workshop Settings Field " + labelKey;
            row.SetActive(true);
            var rowRect = row.transform as RectTransform;
            SetRect(rowRect, new Vector2(anchorMinX, 1f), new Vector2(anchorMaxX, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, height));
            var layout = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            DisableLocalisers(row.transform);

            var nativeInput = row.GetComponentsInChildren(nativeInputTemplate.GetType(), true)
                .OfType<Component>().FirstOrDefault();
            if (nativeInput == null)
            {
                Destroy(row);
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ajustes del escenario: el panel Workshop no contiene su campo de escritura.");
                return null;
            }
            var nativeBehaviour = nativeInput as Behaviour;
            if (nativeBehaviour != null) nativeBehaviour.enabled = false;
            var fieldObject = nativeInput.gameObject;
            fieldObject.SetActive(true);

            SetWorkshopFieldLabel(row.transform, fieldObject.transform,
                EditorLocalization.Get(labelKey));
            if (inputWidth > 0f)
                ArrangeNativeWorkshopColumns(row.transform, fieldObject.transform,
                    inputWidth);
            foreach (var oldText in fieldObject.GetComponentsInChildren<Text>(true))
                oldText.gameObject.SetActive(false);

            var editorObject = new GameObject("UP Editor Input", typeof(RectTransform),
                typeof(Image), typeof(TMP_InputField));
            editorObject.transform.SetParent(fieldObject.transform, false);
            var editorRect = (RectTransform)editorObject.transform;
            Stretch(editorRect);
            var hitArea = editorObject.GetComponent<Image>();
            hitArea.color = new Color(1f, 1f, 1f, 0f);
            hitArea.raycastTarget = true;

            var viewportObject = new GameObject("Text Area", typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(editorObject.transform, false);
            var viewportRect = (RectTransform)viewportObject.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(10f, 1f);
            viewportRect.offsetMax = new Vector2(-10f, -1f);

            var textObject = new GameObject("Text", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(viewportObject.transform, false);
            var textRect = (RectTransform)textObject.transform;
            Stretch(textRect);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = Color.black;
            text.alignment = multiline
                ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = multiline;
            text.extraPadding = true;

            var input = editorObject.GetComponent<TMP_InputField>();
            input.targetGraphic = hitArea;
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.contentType = integer
                ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
            input.characterLimit = multiline ? 1000 : 120;
            input.caretColor = _style.HeaderColor;
            var selection = _style.ButtonColor;
            selection.a = .35f;
            input.selectionColor = selection;
            return input;
        }

        private static void ArrangeNativeWorkshopColumns(Transform row, Transform input,
            float inputWidth)
        {
            // Jerarquia verificada del prefab nativo GameItemUIScreen:
            // LayoutGroup/TitleLabelTextHolder y LayoutGroup/TitlePanel/
            // InputFieldContainer/InputField. Se cambian solo sus dos columnas.
            var layoutRoot = row.Find("LayoutGroup");
            var labelHolder = row.Find("LayoutGroup/TitleLabelTextHolder") as RectTransform;
            var inputPanel = row.Find("LayoutGroup/TitlePanel") as RectTransform;
            if (layoutRoot == null || labelHolder == null || inputPanel == null ||
                input == null || !input.IsChildOf(inputPanel))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ajustes del escenario: la jerarquia nativa del campo Workshop no coincide.");
                return;
            }

            // StaticLayoutGroup fija la proporcion 20/80 del prefab. Una vez clonado,
            // se desactiva para conservar las columnas explicitas solicitadas.
            foreach (var behaviour in layoutRoot.GetComponents<Behaviour>())
                if (behaviour != null && behaviour.GetType().Name == "StaticLayoutGroup")
                    behaviour.enabled = false;

            var inputContainer = inputPanel.Find("InputFieldContainer") as RectTransform;
            var nativeHorizontalInset = inputContainer == null
                ? 0f : Mathf.Max(0f, -inputContainer.sizeDelta.x);
            var nativePanelWidth = inputWidth + nativeHorizontalInset;

            labelHolder.anchorMin = Vector2.zero;
            labelHolder.anchorMax = Vector2.one;
            labelHolder.offsetMin = Vector2.zero;
            labelHolder.offsetMax = new Vector2(-nativePanelWidth - 8f, 0f);

            inputPanel.anchorMin = new Vector2(1f, 0f);
            inputPanel.anchorMax = Vector2.one;
            inputPanel.pivot = new Vector2(1f, .5f);
            inputPanel.offsetMin = new Vector2(-nativePanelWidth, 0f);
            inputPanel.offsetMax = Vector2.zero;

            var label = labelHolder.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        private static bool TryGetWorkshopFieldTemplate(bool multiline,
            out GameObject panel, out Component input)
        {
            panel = multiline
                ? _cachedMultilineWorkshopPanel : _cachedSingleLineWorkshopPanel;
            input = multiline
                ? _cachedMultilineWorkshopInput : _cachedSingleLineWorkshopInput;
            if (panel != null && input != null) return true;

            panel = null;
            input = null;
            var manager = ExtContentUtils.ExtContentManager;
            var prefab = manager?.ExtContentUIManager?.Config?.GameItemUIScreenPrefab;
            var screen = prefab?.GetComponent<ExtContentGameItemUIScreen>();
            if (screen == null) return false;
            var panelName = multiline
                ? "_gameObjectItemDescriptionPanel" : "_gameObjectItemTitlePanel";
            var fieldName = multiline ? "_inputDescription" : "_inputTitle";
            panel = AccessTools.Field(typeof(ExtContentGameItemUIScreen), panelName)
                ?.GetValue(screen) as GameObject;
            input = AccessTools.Field(typeof(ExtContentGameItemUIScreen), fieldName)
                ?.GetValue(screen) as Component;
            if (multiline)
            {
                _cachedMultilineWorkshopPanel = panel;
                _cachedMultilineWorkshopInput = input;
            }
            else
            {
                _cachedSingleLineWorkshopPanel = panel;
                _cachedSingleLineWorkshopInput = input;
            }
            return panel != null && input != null;
        }

        private static void SetWorkshopFieldLabel(Transform row, Transform inputRoot,
            string value)
        {
            var tmpLabel = row.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text != null &&
                    !text.transform.IsChildOf(inputRoot));
            if (tmpLabel != null)
            {
                tmpLabel.text = value;
                tmpLabel.color = Color.white;
                tmpLabel.alignment = TextAlignmentOptions.MidlineRight;
                return;
            }
            var regularLabel = row.GetComponentsInChildren<Text>(true)
                .FirstOrDefault(text => text != null &&
                    !text.transform.IsChildOf(inputRoot));
            if (regularLabel != null)
            {
                regularLabel.text = value;
                regularLabel.color = Color.white;
                regularLabel.alignment = TextAnchor.MiddleRight;
            }
        }

        private static void SetScenarioSettingsInputLabel(TMP_InputField input, string value)
        {
            if (input == null) return;
            var fieldRoot = input.transform.parent;
            var row = fieldRoot;
            while (row != null &&
                   !row.name.StartsWith("UP Workshop Settings Field ",
                       StringComparison.Ordinal))
                row = row.parent;
            if (row != null) SetWorkshopFieldLabel(row, fieldRoot, value);
        }

        private static void SetScenarioDecimalContentType(TMP_InputField input)
        {
            if (input != null) input.contentType = TMP_InputField.ContentType.DecimalNumber;
        }

        private static void ApplyScenarioSettingsLabelV7(
            TMP_InputField input, float size, float minimum, float maximum)
        {
            if (input == null) return;
            var fieldRoot = input.transform.parent;
            var row = fieldRoot;
            while (row != null &&
                   !row.name.StartsWith("UP Workshop Settings Field ",
                       StringComparison.Ordinal))
                row = row.parent;
            if (row == null) return;

            var label = row.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text != null &&
                    !text.transform.IsChildOf(fieldRoot));
            if (label == null) return;

            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = minimum;
            label.fontSizeMax = maximum;
        }

        private void RefreshScenarioSettingsFields()
        {
            if (_level == null || _settingsDisplayName == null) return;
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            _settingsDisplayName.SetTextWithoutNotify(settings.DisplayName ?? "");
            _settingsAuthor?.SetTextWithoutNotify(
                ScenarioWorkshopMetaStore.Current(_level).author ?? "");
            _settingsDescriptionPerStar.SetTextWithoutNotify(
                ScenarioEditorStorage.GetMapText(_level,
                    ScenarioMapLocalization.DescriptionKey));
            _settingsInitialBalance.SetTextWithoutNotify(
                settings.InitialBalance.ToString(CultureInfo.InvariantCulture));
            _settingsFailWarning.SetTextWithoutNotify(
                settings.FailStateBalanceWarning.ToString(CultureInfo.InvariantCulture));
            _settingsFailGameOver.SetTextWithoutNotify(
                settings.FailStateBalanceGameOver.ToString(CultureInfo.InvariantCulture));
            _settingsInitialTemperature?.SetTextWithoutNotify(
                settings.initialTemperature.ToString(CultureInfo.InvariantCulture));
            _settingsInitialAttractiveness?.SetTextWithoutNotify(
                settings.initialAttractiveness.ToString(CultureInfo.InvariantCulture));
            _settingsInitialHygiene?.SetTextWithoutNotify(
                settings.initialHygiene.ToString(CultureInfo.InvariantCulture));
        }

        private void AcceptScenarioSettings()
        {
            if (_level == null) return;
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            settings.DisplayName = _settingsDisplayName?.text ?? "";
            ScenarioWorkshopMetaStore.SetAuthor(_level, _settingsAuthor?.text ?? "");
            var description = (_settingsDescriptionPerStar?.text ?? "")
                .Replace("\r\n", "\n").Replace('\r', '\n');
            ScenarioEditorStorage.SetMapText(_level,
                ScenarioMapLocalization.DescriptionKey, description);
            settings.DescriptionPerStar = ScenarioEditorStorage.CurrentStarDescriptions(
                _level, ScenarioEditorStorage.CurrentMapTranslationLanguage(_level));
            if (!TryReadScenarioInteger(_settingsInitialBalance, out settings.InitialBalance) ||
                !TryReadScenarioInteger(_settingsFailWarning,
                    out settings.FailStateBalanceWarning) ||
                !TryReadScenarioInteger(_settingsFailGameOver,
                    out settings.FailStateBalanceGameOver) ||
                !TryReadScenarioFloat(_settingsInitialTemperature,
                    out settings.initialTemperature) ||
                !TryReadScenarioFloat(_settingsInitialAttractiveness,
                    out settings.initialAttractiveness) ||
                !TryReadScenarioFloat(_settingsInitialHygiene,
                    out settings.initialHygiene)) return;
            settings.initialHygiene = Mathf.Clamp01(settings.initialHygiene);
            settings.environmentSettingsConfigured = true;
            StoreDiseaseSettings(settings);
            ScenarioEditorStorage.SetScenarioSettings(_level, settings);
            CloseSettingsPanel();
        }

        private static bool TryReadScenarioInteger(TMP_InputField input, out int value)
        {
            value = 0;
            if (input != null && int.TryParse(input.text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value)) return true;
            input?.Select();
            return false;
        }

        private static bool TryReadScenarioFloat(TMP_InputField input, out float value)
        {
            value = 0f;
            if (input != null && float.TryParse(input.text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) &&
                !float.IsNaN(value) && !float.IsInfinity(value)) return true;
            input?.Select();
            return false;
        }

        private static void CreateSettingsLabel(Transform parent, string value, float size,
            TextAlignmentOptions alignment, Vector2? inset = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            var offset = inset ?? new Vector2(12f, 0f);
            rect.offsetMin = new Vector2(offset.x, 0f);
            rect.offsetMax = new Vector2(-offset.x, offset.y);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private void ReleaseSettingsPanelContents()
        {
            ReleaseTranslationMessageBox();
            _settingsOverlay = null;
            _settingsCloseButton = null;
            _settingsTabButtons = null;
            _settingsTabAnimators = null;
            _settingsTabRoots = null;
            _settingsTabFolderBackgrounds = null;
            _settingsPages = null;
            Array.Clear(_settingsTabContentsCurrent, 0, _settingsTabContentsCurrent.Length);
            _generalFlagSelector = null;
            _generalFlagList = null;
            _generalSelectedFlag = null;
            _generalTranslateLanguagesButton = null;
            _generalFlagExpanded = false;
            _generalRoomStateLevel = null;
            _generalRoomStates.Clear();
            _roomAvailabilityToggles.Clear();
            ReleaseDiseaseSettingsContents();
            ReleasePlotSettingsContents();
        }

        private Level _generalRoomStateLevel;
        private readonly Dictionary<string, bool> _generalRoomStates =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Toggle>> _roomAvailabilityToggles =
            new Dictionary<string, List<Toggle>>(StringComparer.Ordinal);
        private const string RoomUnlockedLabelKey =
            "editor.scenario_settings.diseases.unlocked";

        private sealed class GeneralRoomEntry
        {
            internal string Id;
            internal string Name;
            internal Sprite Icon;
            internal RoomDefinition.Type Type;
        }

        private void BuildGeneralRoomAvailabilitySettings(Transform content)
        {
            if (content == null || _level?.App?.Metagame?.RoomDatabase?.Instance == null)
                return;

            var sources = _level.App.Metagame.RoomDatabase.Instance.Rooms;
            if (sources == null) return;

            RefreshGeneralRoomAvailabilityState();

            // Unica fuente de salas e iconos: la base de datos nativa del juego.
            var rooms = sources.Where(source => source?.Instance != null)
                .GroupBy(source => source.Instance._type)
                .Select(group => group.First().Instance)
                .Where(room => !string.IsNullOrWhiteSpace(room.GetLocalisedName()))
                .Select(room => new GeneralRoomEntry
                {
                    Id = room._type.ToString(),
                    Name = room.GetLocalisedName(),
                    Icon = room._icon,
                    Type = room._type
                })
                .OrderBy(room => room.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var diagnosed = RoomDefinition.DiagnosisRooms ??
                new RoomDefinition.Type[0];
            var diagnosisTypes = new HashSet<RoomDefinition.Type>(diagnosed)
            {
                RoomDefinition.Type.GPOffice
            };

            // Las salas de tratamiento permanecen en Enfermedades; no se
            // duplican en los dos paneles de la pestaña General.
            if (_settingsDiseaseCatalog == null)
                _settingsDiseaseCatalog = ScenarioDiseaseCatalog.Build(_level);
            var treatmentTypes = new HashSet<RoomDefinition.Type>(
                _settingsDiseaseCatalog.Rooms
                    .Where(room => room.Definition != null)
                    .Select(room => room.Definition._type));

            var diagnosis = rooms.Where(room => diagnosisTypes.Contains(room.Type)).ToArray();
            var general = rooms.Where(room => !diagnosisTypes.Contains(room.Type) &&
                                              !treatmentTypes.Contains(room.Type)).ToArray();

            // Los paneles quedan en la zona inferior, sin modificar el
            // ScrollRect ni la geometria de los campos superiores de General.
            const float panelTop = 412f;
            const float panelHeight = 390f;

            BuildGeneralRoomPanel(content, "UP General Common Rooms",
                EditorLocalization.Get("editor.scenario_settings.tab.general"),
                general, .02f, .49f, panelTop, panelHeight);
            BuildGeneralRoomPanel(content, "UP General Diagnosis Rooms",
                EditorLocalization.Get("room.diagnosis_power"),
                diagnosis, .51f, .98f, panelTop, panelHeight);
        }

        private void BuildGeneralRoomPanel(Transform parent, string name,
            string title, GeneralRoomEntry[] rooms, float minX, float maxX,
            float top, float height)
        {
            var panel = NewImage(name, parent, _style.ButtonSprite,
                SettingsSharedPageColor, false);
            // El Content nativo utiliza un LayoutGroup. Estos paneles tienen
            // geometria manual y no deben comprimirse ni apilarse arriba.
            var panelLayout = panel.GetComponent<LayoutElement>() ??
                panel.AddComponent<LayoutElement>();
            panelLayout.ignoreLayout = true;
            var panelRect = panel.transform as RectTransform;
            SetRect(panelRect, new Vector2(minX, 1f), new Vector2(maxX, 1f),
                new Vector2(.5f, 1f), new Vector2(0f, -top),
                new Vector2(0f, height));

            var titleText = CreateDiseaseText(panel.transform, title, 24f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            SetTopLeft(titleText.rectTransform, 15f, 8f, 420f, 42f);

            // Cada panel tiene su propio ScrollRect y su propia barra nativa.
            // La pestana General no recibe ningun scrollbar global.
            var scrollObject = NewImage("Lista de salas", panel.transform, null,
                new Color(0f, 0f, 0f, .001f), true);
            var scrollRect = scrollObject.transform as RectTransform;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(10f, 10f);
            scrollRect.offsetMax = new Vector2(-10f, -54f);

            var scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.elasticity = 0f;
            scroll.scrollSensitivity = 50f;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRect, false);
            var viewport = viewportObject.transform as RectTransform;
            Stretch(viewport);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewport, false);
            var rowsRect = contentObject.transform as RectTransform;
            rowsRect.anchorMin = new Vector2(0f, 1f);
            rowsRect.anchorMax = Vector2.one;
            rowsRect.pivot = new Vector2(.5f, 1f);
            rowsRect.anchoredPosition = Vector2.zero;
            // Altura exacta hasta el borde inferior de la ultima fila.
            rowsRect.sizeDelta = new Vector2(0f, rooms.Length == 0
                ? 0f : (rooms.Length - 1) * 53f + 49f);

            scroll.viewport = viewport;
            scroll.content = rowsRect;
            for (var index = 0; index < rooms.Length; ++index)
                CreateGeneralRoomRow(rowsRect, rooms[index], index);

            var scrollbar = InstallNativeVerticalScrollbar(scrollRect, scroll,
                "UP General Rooms Native Scrollbar", 0f, 0f, false, 22f);
            if (scrollbar != null)
                scroll.verticalScrollbarVisibility =
                    ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalNormalizedPosition = 1f;
        }

        private void CreateGeneralRoomRow(RectTransform parent, GeneralRoomEntry room,
            int index)
        {
            var row = NewImage("UP General Room " + room.Id, parent,
                _style.ButtonSprite, SettingsSharedPageColor, false);
            var rect = row.transform as RectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -index * 53f);
            rect.sizeDelta = new Vector2(-10f, 49f);

            if (room.Icon != null)
            {
                var icon = NewImage("Icono", row.transform, room.Icon, Color.white, false);
                var iconRect = icon.transform as RectTransform;
                // Igual que la cabecera de sala de Enfermedades.
                SetTopLeft(iconRect, 3f, 2.5f, 44f, 44f);
                var image = icon.GetComponent<Image>();
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }

            // Igual que el nombre de sala de Enfermedades.
            var roomName = CreateDiseaseText(row.transform, room.Name, 22f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            var nameRect = roomName.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(56f, 0f);
            nameRect.offsetMax = new Vector2(-209f, 0f);

            var unlocked = CreateDiseaseText(row.transform,
                EditorLocalization.Get(RoomUnlockedLabelKey), 16f,
                TextAlignmentOptions.MidlineRight, Color.white);
            var unlockedRect = unlocked.rectTransform;
            unlockedRect.anchorMin = unlockedRect.anchorMax = new Vector2(1f, .5f);
            unlockedRect.pivot = new Vector2(1f, .5f);
            unlockedRect.anchoredPosition = new Vector2(-48f, 0f);
            unlockedRect.sizeDelta = new Vector2(143f, 36f);

            var toggle = CreateDiseaseToggle(row.transform,
                IsGeneralRoomAvailable(room.Id),
                enabled => SaveGeneralRoomAvailability(room.Id, enabled));
            if (toggle == null) return;
            toggle.gameObject.name = "UP General Room Unlocked " + room.Id;
            RegisterRoomAvailabilityToggle(room.Id, toggle);
            SetTopRight(toggle.transform as RectTransform, 9f, 9f, 30f, 30f);
            var background = toggle.transform.Find("Background") as RectTransform;
            if (background == null) return;
            background.anchorMin = background.anchorMax = new Vector2(.5f, .5f);
            background.pivot = new Vector2(.5f, .5f);
            background.anchoredPosition = Vector2.zero;
            background.sizeDelta = new Vector2(38f, 38f);
            var check = background.Find("Checkmark") as RectTransform;
            if (check == null && toggle.graphic != null)
                check = toggle.graphic.rectTransform;
            if (check != null)
            {
                check.anchorMin = Vector2.zero;
                check.anchorMax = Vector2.one;
                check.anchoredPosition = Vector2.zero;
                check.sizeDelta = new Vector2(-10f, -10f);
            }
        }

        private void RefreshGeneralRoomAvailabilityState()
        {
            if (_level == null) return;
            _generalRoomStateLevel = _level;
            _generalRoomStates.Clear();
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            foreach (var id in settings.availableRooms ?? new string[0])
                if (!string.IsNullOrEmpty(id))
                    _generalRoomStates[id] = true;
        }

        private bool IsGeneralRoomAvailable(string id)
        {
            if (_level == null || string.IsNullOrEmpty(id)) return false;
            if (!ReferenceEquals(_generalRoomStateLevel, _level))
                RefreshGeneralRoomAvailabilityState();
            return _generalRoomStates.TryGetValue(id, out var available) && available;
        }

        private void RegisterRoomAvailabilityToggle(string id, Toggle toggle)
        {
            if (string.IsNullOrEmpty(id) || toggle == null) return;
            if (!_roomAvailabilityToggles.TryGetValue(id, out var toggles))
            {
                toggles = new List<Toggle>();
                _roomAvailabilityToggles.Add(id, toggles);
            }
            for (var index = toggles.Count - 1; index >= 0; --index)
                if (toggles[index] == null) toggles.RemoveAt(index);
            if (!toggles.Contains(toggle)) toggles.Add(toggle);
        }

        private void SaveGeneralRoomAvailability(string id, bool unlocked)
        {
            if (_level == null || string.IsNullOrEmpty(id)) return;
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            var available = (settings.availableRooms ?? new string[0])
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (unlocked)
            {
                if (!available.Contains(id)) available.Add(id);
            }
            else
                available.RemoveAll(value => string.Equals(value, id,
                    StringComparison.Ordinal));
            settings.availableRoomsConfigured = true;
            settings.availableRooms = available.ToArray();
            ScenarioEditorStorage.SetScenarioSettings(_level, settings);
            _generalRoomStateLevel = _level;
            _generalRoomStates.Clear();
            foreach (var value in settings.availableRooms)
                _generalRoomStates[value] = true;

            if (!_roomAvailabilityToggles.TryGetValue(id, out var toggles)) return;
            for (var index = toggles.Count - 1; index >= 0; --index)
            {
                var toggle = toggles[index];
                if (toggle == null)
                {
                    toggles.RemoveAt(index);
                    continue;
                }
                toggle.SetIsOnWithoutNotify(unlocked);
            }
        }
    }
}
