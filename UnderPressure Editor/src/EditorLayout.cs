// Updated: 2026-10-21 - Objetos independiente: no se cierra con Escape ni al abrir otros menus.
using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TH20;
using TH20.ExtContent;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    // ===============================================================
    // GUIA RAPIDA DE LA INTERFAZ (editar aqui, sin bucles de repintado)
    // ===============================================================
    // COLORES COMUNES: WorkshopStyle.PanelColor (paneles),
    // WorkshopStyle.ButtonColor (botones azules),
    // WorkshopStyle.SelectedColor (seleccionados),
    // ScenarioEditorPlotRowSkin.LayerColors (checks gris/naranja/verde).
    // MARCO Y SONIDO: ApplyButtonStyle / StandardiseClickSound.
    // ANCHO DE PANELES: PanelWidth. ALTO DE BARRAS: BarHeight.
    // BOTONES SUPERIORES (Baldosas, Camara libre, Personajes,
    // Niebla, Estilos, Ajustes): PlaceTopButtons() (ancho y separacion).
    // BOTONES DE ESQUINA (Objetos, Historial, Terreno):
    // PlaceCornerButton() (tamano); BuildChrome() (creacion).
    // PANEL OBJETOS PROPIO: ApplyObjectsPanel() y
    // ScenarioEditorObjectDock.DetachFromNativeMenus() (retira el catalogo
    // del ciclo de menus del HUD y mueve sus controles al panel propio).
    // BOTONES DE PARCELAS (Aislar, Anadir, Mover, Eliminar):
    // FitPlotPanel() + StyleCompactButtonText() (texto).
    // FILAS DE PARCELA Y SUS CHECKS: CompactPlotRow() (medidas),
    // ScenarioEditorPlotRowSkin.LayerColors (colores).
    // CAPAS Base/Build/Unbuild: ScenarioEditorStorageMenu.AddLayerButtons().
    // BOTONES DEL PINCEL (Pincel, Elevar, Bajar, Aplanar,
    // Wireframe): StyleTerrainButtons() (estilo),
    // EditorTerrain.cs/ApplyDockSize() (posicion y tamano).
    // TEXTURAS DE TERRENO: ArrangeTerrainTextureTiles() (2 columnas,
    // tamano cuadrado, espaciado); no son las miniaturas de Objetos.
    // HISTORIAL: StyleHistoryButtons(); tamano de panel en ApplyLayout().
    // MENU DE PAUSA: StylePauseMenu() (mismo estilo azul).
    // OBJETOS no depende de Escape ni de la herramienta activa.
    // ===============================================================
    // Un unico coordinador calcula toda la geometria. Los paneles inferiores
    // nunca deciden su altura: reciben exactamente el espacio que dejan los
    // paneles superiores y las barras de 50 px.
    internal sealed partial class ScenarioEditorDockLayout : MonoBehaviour
    {
        internal const float PanelWidth = 220f;
        internal const float BarHeight = 50f;
        internal const float CompactTripleButtonWidth = 64f;
        internal const float CompactTripleButtonHeight = 28f;
        internal const float CompactTripleButtonGap = 5f;
        // La linea visible conserva 6 px. El separador de Historial dispone,
        // ademas, de otros 6 px invisibles de agarre solamente hacia arriba.
        private const float DividerHeight = 6f;
        private const float MinimumUpperHeight = 150f;
        private const float MinimumLowerHeight = 150f;

        private static ScenarioEditorDockLayout _active;
        private EditHospitalMenu _menu;
        private Level _level;
        private ScenarioEditorStorageMenu _storage;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _root;
        private RectTransform _plotPanel;
        private RectTransform _plotViewport;
        private LandscapeObjectsMenu _objectsMenu;
        // PANEL OBJETOS: el catalogo conserva su cargador original, pero queda
        // fuera del registro de menus del HUD. Su interfaz vive en el panel
        // propio UP Objects Panel y no participa en cierres de menus.
        private RectTransform _objectsDockHost;
        private LandscapeObjectsMenu _objectsHostOwner;
        private bool _objectsOpen;
        private bool _objectsExpanded;
        private float _objectsPanelHeight = -1f;
        private Button _objectsToggle;
        private Button _historyToggle;
        private Button _terrainToggle;
        private Button _settingsToggle;
        private GameObject _settingsOverlay;
        private DynamicButton[] _settingsTabButtons;
        private ButtonAnimator[] _settingsTabAnimators;
        private Transform[] _settingsTabRoots;
        private Transform[] _settingsTabFolderBackgrounds;
        private GameObject[] _settingsPages;
        private Button _settingsCloseButton;
        private TMP_InputField _settingsDisplayName;
        private TMP_InputField _settingsDescriptionPerStar;
        private TMP_InputField _settingsInitialBalance;
        private TMP_InputField _settingsFailWarning;
        private TMP_InputField _settingsFailGameOver;
        private int _activeSettingsTab;
        private RectTransform _leftDivider;
        private RectTransform _rightDivider;
        private GameObject _topBackground;
        private GameObject _leftTitle;
        private float _leftUpperHeight;
        private float _rightUpperHeight;
        private bool _leftHeightChosen;
        private bool _rightHeightChosen;
        private bool _historyOpen;
        private bool _terrainOpen;
        private float _lastCanvasWidth = -1f;
        private float _lastCanvasHeight = -1f;
        private bool _resetPlotScroll;
        private WorkshopStyle _style;
        // Cada boton se configura una sola vez, no en Update ni LateUpdate.
        private readonly HashSet<Button> _styledButtons = new HashSet<Button>();
        private readonly Dictionary<RectTransform, OriginalRect> _originalRects =
            new Dictionary<RectTransform, OriginalRect>();
        private readonly Dictionary<LayoutElement, bool> _originalLayouts =
            new Dictionary<LayoutElement, bool>();
        private readonly List<Canvas> _createdCanvases = new List<Canvas>();
        private readonly List<GraphicRaycaster> _createdRaycasters = new List<GraphicRaycaster>();
        private readonly List<LayoutElement> _createdLayouts = new List<LayoutElement>();
        private bool _closing;

        private struct OriginalRect
        {
            internal Transform Parent;
            internal int Sibling;
            internal Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
            internal Vector3 Scale;
        }

        private void RememberRect(RectTransform rect)
        {
            if (rect == null || _originalRects.ContainsKey(rect)) return;
            _originalRects.Add(rect, new OriginalRect
            {
                Parent = rect.parent,
                Sibling = rect.GetSiblingIndex(),
                AnchorMin = rect.anchorMin,
                AnchorMax = rect.anchorMax,
                Pivot = rect.pivot,
                Position = rect.anchoredPosition,
                Size = rect.sizeDelta,
                Scale = rect.localScale
            });
        }

        internal static bool PointerOverEditorUi() =>
            ScenarioEditorFlow.IsRunning &&
            ScenarioEditorUiHierarchy.IsPointerOverUi(Input.mousePosition);

        internal static bool CloseSettingsPanelIfOpen()
        {
            if (_active == null || _active._settingsOverlay == null ||
                !_active._settingsOverlay.activeSelf) return false;
            _active.CloseSettingsPanel();
            return true;
        }

        // Referencia cromatica de Build.
        internal static Color LayerBuildOrange => new Color(1f, .59f, .08f, 1f);

        internal static void StyleToolbarButton(Button button, bool selected)
        {
            if (_active == null || _active._style == null || button == null) return;
            _active.ApplyButtonStyle(button,
                selected ? _active._style.SelectedColor : _active._style.ButtonColor);
        }

        // Intercepta el tinte gris de EditorMaps.cs sin introducir otro Update.
        // EditorMaps sigue comprobando atajos y herramientas, pero no modifica el
        // sprite ni escribe colores mientras el estado no cambie.
        internal static bool OverrideToolTint(Button button, bool selected)
        {
            if (_active == null || _active._style == null || button == null) return false;
            _active.ApplyButtonStyle(button,
                selected ? _active._style.SelectedColor : _active._style.ButtonColor);
            return true;
        }

        // MENU DE PAUSA: Continuar, Guardar/Abrir/Vaciar, Ajustes y Salir.
        // Colores y bordes comunes en ApplyButtonStyle()/WorkshopStyle.
        internal static void StylePauseMenu(PauseMenu menu)
        {
            if (_active == null || _active._style == null || menu == null ||
                !ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning) return;
            var fields = new[]
            {
                "_resumeButton", "_quickSaveButton", "_quickLoadButton",
                "_restartLevelButton", "_preferencesButton", "_quitToMapButton",
                "_quitToMenuButton", "_quitToDesktopButton"
            };
            foreach (var name in fields)
            {
                var button = AccessTools.Field(typeof(PauseMenu), name)?.GetValue(menu) as Button;
                if (button == null) continue;
                _active.ApplyButtonStyle(button);
                var borderRoot = button.image != null ? button.image.gameObject : button.gameObject;
                var border = borderRoot.GetComponent<Outline>() ??
                    borderRoot.AddComponent<Outline>();
                border.enabled = true;
                border.effectColor = new Color(1f, 1f, 1f, .98f);
                border.effectDistance = new Vector2(2f, -2f);
                border.useGraphicAlpha = true;
                var rect = button.transform as RectTransform;
                if (rect != null &&
                    button.GetComponentInParent<VerticalLayoutGroup>() == null &&
                    button.GetComponentInParent<DynamicLayoutGroup>() == null &&
                    button.GetComponent<ScenarioEditorPauseSpacing>() == null)
                {
                    button.gameObject.AddComponent<ScenarioEditorPauseSpacing>();
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                        Mathf.Max(1f, rect.rect.height - 4f));
                }
            }

            // El prefab agrupa los botones en una disposición vertical. Se conserva
            // su geometría nativa y sólo se abre una separación mínima entre filas.
            foreach (var layout in fields
                .Select(name => AccessTools.Field(typeof(PauseMenu), name)?.GetValue(menu) as Button)
                .Where(button => button != null)
                .Select(button => button.GetComponentInParent<VerticalLayoutGroup>())
                .Where(layout => layout != null)
                .Distinct())
                layout.spacing = Mathf.Max(4f, layout.spacing);
            foreach (var layout in fields
                .Select(name => AccessTools.Field(typeof(PauseMenu), name)?.GetValue(menu) as Button)
                .Where(button => button != null)
                .Select(button => button.GetComponentInParent<DynamicLayoutGroup>())
                .Where(layout => layout != null)
                .Distinct())
                layout.minimumSpacing = Mathf.Max(4f, layout.minimumSpacing);
        }

        internal static void StyleUtilityToggle(Button button, bool selected)
        {
            StyleToolbarButton(button, selected);
        }

        internal static void StyleHistoryButtons(ScenarioEditorHistory history)
        {
            if (_active == null || history == null) return;
            // El historial tenia un segundo fondo negro semitransparente
            // en su viewport: igualar ambos fondos al panel azul.
            var panel = AccessTools.Field(typeof(ScenarioEditorHistory), "_historyPanel")?
                .GetValue(history) as RectTransform;
            if (panel != null)
            {
                var background = panel.GetComponent<Image>();
                if (background != null)
                {
                    background.overrideSprite = null;
                    background.sprite = null;
                    background.type = Image.Type.Simple;
                    background.color = _active._style.PanelColor;
                }
                var viewport = panel.Find("Desplazamiento/Recorte");
                var viewportImage = viewport?.GetComponent<Image>();
                if (viewportImage != null)
                    viewportImage.color = Color.clear;
            }
            foreach (var button in history.GetComponentsInChildren<Button>(true))
            {
                if (button == null || !button.gameObject.name.StartsWith("Paso ")) continue;
                // El historial es texto, no una lista de botones coloreados.
                // La imagen permanece activa y transparente porque Button la
                // necesita como objetivo de raycast para recibir el clic.
                if (button.image != null)
                {
                    button.image.enabled = true;
                    button.image.raycastTarget = true;
                    if (button.interactable)
                    {
                        button.image.color = new Color(0f, 0f, 0f, .001f);
                    }
                    else
                    {
                        var selected = _active._style.PanelColor;
                        selected.r *= .82f;
                        selected.g *= .82f;
                        selected.b *= .82f;
                        selected.a = 1f;
                        button.image.color = selected;
                    }
                }
                // RefreshHistoryUI distingue las acciones deshechas en gris.
                // No sobrescribir aquí ese estado visual.
            }
        }

        // TERRENO: Pincel, Elevar, Bajar, Aplanar, Wireframe.
        // Los colores azul/seleccionado salen de WorkshopStyle;
        // sus dimensiones originales se establecen en EditorTerrain.cs.
        internal static void StyleTerrainButtons(ScenarioEditorTerrainPainterBehaviour painter)
        {
            if (_active == null || painter == null) return;
            var kind = typeof(ScenarioEditorTerrainPainterBehaviour);
            var brush = AccessTools.Field(kind, "_brushButton")?.GetValue(painter) as Button;
            var raise = AccessTools.Field(kind, "_raiseButton")?.GetValue(painter) as Button;
            var lower = AccessTools.Field(kind, "_lowerButton")?.GetValue(painter) as Button;
            var flatten = AccessTools.Field(kind, "_flattenButton")?.GetValue(painter) as Button;
            var mode = ScenarioEditorTerrainPainter.CurrentSculptMode;
            var wire = AccessTools.Field(kind, "_wireButton")?.GetValue(painter) as Button;
            StyleCompactButtonGroup(
                new[] { raise, lower, flatten, brush, wire },
                new[]
                {
                    mode == ScenarioEditorTerrainPainter.SculptMode.Raise,
                    mode == ScenarioEditorTerrainPainter.SculptMode.Lower,
                    mode == ScenarioEditorTerrainPainter.SculptMode.Flatten,
                    ScenarioEditorTerrainPainter.BrushEnabled &&
                        mode == ScenarioEditorTerrainPainter.SculptMode.Off,
                    ScenarioEditorTerrainPainter.WireframeEnabled
                });
        }

        // Todos los botones compactos de un grupo comparten exactamente el
        // mismo sprite, color de estado, tipografia y centrado.
        private static void StyleCompactButtonGroup(Button[] buttons, bool[] selected)
        {
            if (_active == null || buttons == null || selected == null) return;
            var count = Mathf.Min(buttons.Length, selected.Length);
            for (var index = 0; index < count; ++index)
            {
                var button = buttons[index];
                if (button == null) continue;
                _active.ApplyButtonStyle(button, selected[index]
                    ? _active._style.SelectedColor : _active._style.ButtonColor);
                StyleCompactButtonText(button);
            }
        }

        // TEXTURAS DE TERRENO: dos columnas de miniaturas CUADRADAS.
        // Cambiar Padding / Gap para espaciado y PanelWidth (arriba) para ancho.
        // NO se anade GridLayoutGroup porque ya existe VerticalLayoutGroup
        // en EditorTerrain.cs; ambos sobre el mismo GameObject pueden chocar.
        // Este metodo solo se llama al crear el panel o cambiar sus medidas.
        internal static void ArrangeTerrainTextureTiles(ScenarioEditorTerrainPainterBehaviour painter)
        {
            if (_active == null || painter == null) return;
            var content = AccessTools.Field(typeof(ScenarioEditorTerrainPainterBehaviour),
                "_content")?.GetValue(painter) as RectTransform;
            if (content == null) return;
            var scroll = content.GetComponentInParent<ScrollRect>();
            if (scroll == null) return;
            InstallNativeVerticalScrollbar(painter.transform as RectTransform, scroll,
                "UP Terrain Native Vertical Scrollbar", 264f, 8f, true);

            // El contenido lo distribuimos manualmente: sin bucles por frame.
            var vertical = content.GetComponent<VerticalLayoutGroup>();
            if (vertical != null) vertical.enabled = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            const float padding = 6f;
            const float spacing = 6f;
            var scrollRect = scroll.transform as RectTransform;
            var viewportRect = scroll.viewport;
            var width = viewportRect != null && viewportRect.rect.width > 40f
                ? viewportRect.rect.width
                : scrollRect != null && scrollRect.rect.width > 40f
                    ? scrollRect.rect.width - 12f : PanelWidth - 28f;
            var cell = Mathf.Max(24f, (width - padding * 2f - spacing) / 2f);

            var index = 0;
            for (var childIndex = 0; childIndex < content.childCount; ++childIndex)
            {
                var tile = content.GetChild(childIndex) as RectTransform;
                if (tile == null || !tile.gameObject.name.StartsWith("Texture")) continue;
                tile.anchorMin = tile.anchorMax = new Vector2(0f, 1f);
                tile.pivot = new Vector2(0f, 1f);
                tile.anchoredPosition = new Vector2(padding + (index % 2) * (cell + spacing),
                    -padding - (index / 2) * (cell + spacing));
                tile.sizeDelta = new Vector2(cell, cell);
                ++index;
            }
            // Ajusta la altura del ScrollRect a todas las filas existentes.
            var rows = (index + 1) / 2;
            var contentHeight = padding * 2f + rows * cell + Mathf.Max(0, rows - 1) * spacing;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(contentHeight, 1f));
        }

        internal static void Attach(EditHospitalMenu menu, Level level,
            ScenarioEditorStorageMenu storage, Button template, RectTransform plotViewport)
        {
            if (menu == null || level == null || storage == null || template == null) return;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var component = canvas.GetComponent<ScenarioEditorDockLayout>() ??
                canvas.gameObject.AddComponent<ScenarioEditorDockLayout>();
            component.Initialise(menu, level, storage, template, plotViewport);
        }

        internal static void RegisterObjectsMenu(LandscapeObjectsMenu menu)
        {
            if (_active != null && menu != null) _active.ApplyObjectsPanel(menu, true);
        }

        // OBJETOS PERSISTENTE: el panel nuevo no depende de que el juego
        // tenga activo el modo CursorEditHospitalItem ni de otros menus.
        internal static bool ObjectsPanelIsOpen() =>
            _active != null && !_active._closing &&
            _active._objectsOpen &&
            _active._objectsDockHost != null &&
            _active._objectsDockHost.gameObject.activeInHierarchy;

        // Objetos es un panel independiente: solo su propio boton cambia
        // su visibilidad. Los cambios de herramienta y Escape no lo tocan.
        internal static bool ToggleObjectsPanel()
        {
            if (_active == null || _active._closing ||
                _active._objectsDockHost == null) return false;
            _active._objectsOpen = !_active._objectsOpen;
            _active._objectsDockHost.gameObject.SetActive(_active._objectsOpen);
            _active.SetSelected(_active._objectsToggle, _active._objectsOpen);
            return true;
        }

        internal static void ToggleObjectsExpanded()
        {
            if (_active == null || _active._closing || _active._objectsMenu == null) return;
            _active._objectsExpanded = !_active._objectsExpanded;
            _active.ApplyObjectsPanel(_active._objectsMenu, true);
        }

        internal static void RegisterPlotRow(PlotSelectionItem row)
        {
            var active = _active;
            if (active == null || active._closing || active._menu == null ||
                active._plotViewport == null || row == null) return;
            active.ApplyPlotRow(row);
            active.RefreshPlotPanelForCount();
        }

        internal static void NotifyPlotListChanged()
        {
            var active = _active;
            if (active == null || active._closing || active._menu == null ||
                active._plotViewport == null) return;
            active.RefreshPlotPanelForCount();
        }

        private void Initialise(EditHospitalMenu menu, Level level,
            ScenarioEditorStorageMenu storage, Button template, RectTransform plotViewport)
        {
            // El Canvas raiz sobrevive al editor y reutiliza este componente.
            // Cada sesion debe rearmar expresamente su ciclo de vida.
            _closing = false;
            _active = this;
            _menu = menu;
            _level = level;
            _storage = storage;
            _canvas = menu.GetComponentInParent<Canvas>();
            _canvasRect = _canvas.transform as RectTransform;
            _plotViewport = plotViewport;
            _plotPanel = FindPlotPanel(menu.transform as RectTransform, plotViewport,
                storage.PlotLayerRoot as RectTransform);
            _style = WorkshopStyle.Read(storage.BuildButton ?? template);

            if (_root == null)
            {
                var rootObject = new GameObject("UP Editor Dock Layout", typeof(RectTransform));
                rootObject.transform.SetParent(ScenarioEditorUiHierarchy.ParentFor(_canvas), false);
                _root = (RectTransform)rootObject.transform;
                Stretch(_root);
                var overlay = rootObject.AddComponent<Canvas>();
                overlay.overrideSorting = true;
                overlay.sortingOrder = 3000;
                rootObject.AddComponent<GraphicRaycaster>();
                _root.SetAsLastSibling();
                BuildChrome(template);
            }

            _objectsToggle = storage.ObjectsButton;
            ScenarioEditorHistory.SetDockVisible(false);
            ScenarioEditorTerrainPainter.SetDockVisible(false);
            // El panel y su boton deben permanecer por encima de los overlays
            // de Personajes, Niebla y del menu de pausa.
            EnsureSortingCanvas(_objectsToggle?.gameObject, 3101);
            _objectsMenu = FindActiveObjectsMenu();
            _resetPlotScroll = true;
            if (_objectsToggle != null) _objectsToggle.gameObject.name = "UP Objects Dock Toggle";
            ApplyLayout(true);
        }

        // CREA las pestanas HISTORIAL y TERRENO. El boton OBJETOS procede
        // de ScenarioEditorStorageMenu; los tres se ubican en PlaceCornerButton.
        private void BuildChrome(Button template)
        {
            _topBackground = NewImage("UP Top Bar", _root, _style.PanelSprite,
                _style.HeaderColor, false);
            SetRect((RectTransform)_topBackground.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(.5f, 1f), Vector2.zero,
                new Vector2(0f, BarHeight));
            _topBackground.SetActive(false);

            _leftTitle = NewImage("UP Editor Title", _root, _style.PanelSprite,
                _style.HeaderColor, false);
            var titleRect = (RectTransform)_leftTitle.transform;
            // CABECERA HOSPITAL: altura de 30 px (antes 50).
            // Asi se elimina el hueco entre el nombre y los controles de parcela.
            SetTopLeft(titleRect, 0f, 0f, PanelWidth, 30f);

            _historyToggle = CloneButton(template, _root, "UP History Dock Toggle",
                EditorLocalization.Get("editor.history.button"), ToggleHistory);
            _terrainToggle = CloneButton(template, _root, "UP Terrain Dock Toggle",
                EditorLocalization.Get("editor.terrain.toggle"), ToggleTerrain);
            _settingsToggle = CloneButton(template, _root, "UP Settings Toolbar Button",
                EditorLocalization.Get("editor.toolbar.settings"), ToggleSettingsPanel);

            _leftDivider = CreateDivider("UP Left Panel Divider", _root, SetLeftHeight);
            _rightDivider = CreateDivider("UP Right Panel Divider", _root, SetRightHeight);
            AddUpperDividerGrabArea(_rightDivider, SetRightHeight);
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
                "editor.scenario_settings.tab.fog",
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
            var fogFolder = GetPreferencesField<Transform>(clonedScreen,
                "_gameTabFolderBackground");
            fogFolder = fogFolder != null
                ? Instantiate(fogFolder.gameObject, fogFolder.parent, false).transform : null;
            if (fogFolder != null) fogFolder.name = "UP Fog Tab";

            for (var index = 0; index < tabKeys.Length; ++index)
            {
                var tabIndex = index;
                var title = EditorLocalization.Get(tabKeys[index]);
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
                    folder = index == 4 ? fogFolder : languageFolder;
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

            var close = GetPreferencesField<Button>(clonedScreen, "_closeButton");
            HideSettingsObject(close);
            HideSettingsObject(credits);
            ExpandNativeSettingsContents(clonedScreen);
            RedistributeNativeSettingsTabs(clonedScreen);
            _settingsCloseButton = CreateNativeSettingsCloseButton(
                settingsPanel != null ? settingsPanel : clone.transform);
            ApplyNativeSettingsPalette(clone.transform);
            ApplySettingsContentFrameColor();
            SetSettingsTab(0);
            clone.SetActive(true);
            _settingsOverlay.SetActive(false);
        }

        private void SetSettingsTab(int selectedIndex)
        {
            if (_settingsTabButtons == null || _settingsPages == null) return;
            _activeSettingsTab = Mathf.Clamp(selectedIndex, 0,
                Mathf.Min(_settingsTabButtons.Length, _settingsPages.Length) - 1);
            if (_activeSettingsTab == 1) RefreshDiseaseSettingsContents();
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
                if (folder != null) folder.gameObject.SetActive(selected);
                if (_settingsPages[index] != null)
                    _settingsPages[index].SetActive(selected);
            }
            // PreferencesScreen eleva la carpeta activa para que su borde quede encima.
            _settingsTabRoots[_activeSettingsTab]?.SetAsLastSibling();
            _settingsPages[_activeSettingsTab]?.transform.SetAsLastSibling();
            _settingsCloseButton?.transform.SetAsLastSibling();
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

        private Button CreateNativeSettingsCloseButton(Transform parent)
        {
            if (parent == null) return null;
            var template = Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(candidate => candidate != null && candidate.image != null &&
                    ((candidate.image.overrideSprite != null &&
                      candidate.image.overrideSprite.name == "T_UI_Button_Close_Red") ||
                     (candidate.image.sprite != null &&
                      candidate.image.sprite.name == "T_UI_Button_Close_Red")));
            if (template == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ajustes del escenario: no se encontro el boton X rojo nativo.");
                return null;
            }

            // Se clona el objeto completo: sprite rojo, sombra, animador y escala
            // nativa. Solo cambian el padre, el anclaje y la accion del clic.
            var button = Instantiate(template, parent, false);
            button.gameObject.name = "UP Native Settings Close";
            button.gameObject.SetActive(true);
            var rect = button.transform as RectTransform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(-12f, -12f);
            DisableLocalisers(button.transform);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(AcceptScenarioSettings);
            StandardiseClickSound(button);
            return button;
        }

        private void ApplyNativeSettingsPalette(Transform root)
        {
            if (root == null) return;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.GetComponentInParent<InputField>() != null ||
                    image.GetComponentInParent<TMP_InputField>() != null ||
                    IsWorkshopSettingsField(image.transform)) continue;
                var interior = IsSettingsInterior(image.transform);
                if (interior)
                {
                    image.color = _style.ButtonColor;
                    var interiorRect = image.rectTransform.rect;
                    if (!IsSettingsPageRoot(image.transform) &&
                        interiorRect.width >= 400f && interiorRect.height >= 120f)
                    {
                        image.overrideSprite = null;
                        image.sprite = null;
                        image.type = Image.Type.Simple;
                    }
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
                if (rect.width >= 400f && rect.height >= 150f)
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
                    outline.GetComponentInParent<InputField>() != null ||
                    outline.GetComponentInParent<TMP_InputField>() != null ||
                    IsWorkshopSettingsField(outline.transform)) continue;
                outline.effectColor = _style.ButtonColor;
            }
        }

        private static bool IsWorkshopSettingsField(Transform value)
        {
            for (var current = value; current != null; current = current.parent)
                if (current.name.StartsWith("UP Workshop Settings Field ",
                        StringComparison.Ordinal)) return true;
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
            if (_settingsPages == null) return;
            // El prefab nativo separa el marco (VideoTabContent) del Viewport.
            // Se recolorea su Image existente sin alterar sprite ni geometria.
            var frameColor = new Color32(56, 161, 223, 255);
            foreach (var page in _settingsPages)
            {
                var frame = page != null ? page.GetComponent<Image>() : null;
                if (frame != null) frame.color = frameColor;
            }
        }

        private void BuildGeneralSettingsContents()
        {
            if (_settingsPages == null || _settingsPages.Length == 0 ||
                _settingsPages[0] == null) return;
            var content = _settingsPages[0].transform.Find("Scroll View/Viewport/Content");
            if (content == null) return;

            _settingsDisplayName = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.display_name", 16f, 48f, false,
                false, .02f, .50f);
            _settingsDescriptionPerStar = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.description_per_star", 70f, 118f, true,
                false, .02f, .98f);
            _settingsInitialBalance = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.initial_balance", 202f, 48f, false, true,
                .02f, .32f);
            _settingsFailWarning = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.fail_warning", 202f, 48f, false, true,
                .35f, .65f);
            _settingsFailGameOver = CreateScenarioSettingsInput(content,
                "editor.scenario_settings.field.fail_game_over", 202f, 48f, false, true,
                .68f, .98f);
            RefreshScenarioSettingsFields();
        }

        private TMP_InputField CreateScenarioSettingsInput(Transform parent, string labelKey,
            float top, float height, bool multiline, bool integer = false,
            float anchorMinX = .02f, float anchorMaxX = .98f)
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

        private static bool TryGetWorkshopFieldTemplate(bool multiline,
            out GameObject panel, out Component input)
        {
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
                return;
            }
            var regularLabel = row.GetComponentsInChildren<Text>(true)
                .FirstOrDefault(text => text != null &&
                    !text.transform.IsChildOf(inputRoot));
            if (regularLabel != null)
            {
                regularLabel.text = value;
                regularLabel.color = Color.white;
            }
        }

        private void RefreshScenarioSettingsFields()
        {
            if (_level == null || _settingsDisplayName == null) return;
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            _settingsDisplayName.SetTextWithoutNotify(settings.DisplayName ?? "");
            _settingsDescriptionPerStar.SetTextWithoutNotify(string.Join("\n",
                settings.DescriptionPerStar ?? new string[0]));
            _settingsInitialBalance.SetTextWithoutNotify(
                settings.InitialBalance.ToString(CultureInfo.InvariantCulture));
            _settingsFailWarning.SetTextWithoutNotify(
                settings.FailStateBalanceWarning.ToString(CultureInfo.InvariantCulture));
            _settingsFailGameOver.SetTextWithoutNotify(
                settings.FailStateBalanceGameOver.ToString(CultureInfo.InvariantCulture));
        }

        private void AcceptScenarioSettings()
        {
            if (_level == null) return;
            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            settings.DisplayName = _settingsDisplayName?.text ?? "";
            var description = (_settingsDescriptionPerStar?.text ?? "")
                .Replace("\r\n", "\n").Replace('\r', '\n');
            settings.DescriptionPerStar = string.IsNullOrEmpty(description)
                ? new string[0] : description.Split(new[] { '\n' }, StringSplitOptions.None);
            if (!TryReadScenarioInteger(_settingsInitialBalance, out settings.InitialBalance) ||
                !TryReadScenarioInteger(_settingsFailWarning,
                    out settings.FailStateBalanceWarning) ||
                !TryReadScenarioInteger(_settingsFailGameOver,
                    out settings.FailStateBalanceGameOver)) return;
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

        private RectTransform CreateDivider(string name, Transform parent, Action<float> setter)
        {
            var go = NewImage(name, parent, null, new Color(0f, 0f, 0f, .01f), true);
            var handle = NewImage("Tirador", go.transform, _style.ButtonSprite,
                new Color(.18f, .72f, .96f, 1f), false);
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, .5f);
            handleRect.anchorMax = new Vector2(1f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(-8f, 6f);
            var divider = go.AddComponent<ScenarioEditorDockDivider>();
            divider.Initialise(_root, setter);
            return (RectTransform)go.transform;
        }

        private void AddUpperDividerGrabArea(RectTransform divider, Action<float> setter)
        {
            if (divider == null) return;
            var go = NewImage("Area superior de arrastre", divider, null,
                new Color(0f, 0f, 0f, .01f), true);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, DividerHeight);
            var drag = go.AddComponent<ScenarioEditorDockDivider>();
            drag.Initialise(_root, setter);
        }

        private void ToggleHistory()
        {
            _historyOpen = !_historyOpen;
            ScenarioEditorHistory.SetDockVisible(_historyOpen);
            ApplyLayout(true);
        }

        private void ToggleTerrain()
        {
            _terrainOpen = !_terrainOpen;
            ScenarioEditorTerrainPainter.SetDockVisible(_terrainOpen);
            ApplyLayout(true);
        }

        private void SetLeftHeight(float screenY)
        {
            if (_canvasRect == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, new Vector2(0f, screenY), _canvas.worldCamera, out local)) return;
            var top = _canvasRect.rect.yMax;
            _leftUpperHeight = top - local.y - BarHeight;
            _leftHeightChosen = true;
            ApplyLayout(true);
        }

        private void SetRightHeight(float screenY)
        {
            if (_canvasRect == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, new Vector2(0f, screenY), _canvas.worldCamera, out local)) return;
            var top = _canvasRect.rect.yMax;
            _rightUpperHeight = top - local.y - BarHeight;
            _rightHeightChosen = true;
            ApplyLayout(true);
        }

        // Unity notifica el cambio real de tamano del Canvas. No se necesita
        // Update/LateUpdate para comprobarlo ni repintar botones cada fotograma.
        private void OnRectTransformDimensionsChange()
        {
            if (_closing || _menu == null || _canvasRect == null ||
                !ScenarioEditorFlow.IsRunning) return;
            ApplyLayout(false);
        }

        private void ApplyLayout(bool force)
        {
            if (_canvasRect == null || _plotPanel == null) return;
            var width = _canvasRect.rect.width;
            var height = _canvasRect.rect.height;
            if (width <= PanelWidth * 2f || height <= BarHeight * 3f) return;
            if (!force && Mathf.Approximately(width, _lastCanvasWidth) &&
                Mathf.Approximately(height, _lastCanvasHeight))
            {
                return;
            }
            _lastCanvasWidth = width;
            _lastCanvasHeight = height;

            var available = height - BarHeight * 2f;
            if (!_leftHeightChosen)
                _leftUpperHeight = CalculateAutomaticPlotPanelHeight();
            if (!_rightHeightChosen) _rightUpperHeight = Mathf.Min(430f, available * .48f);
            _leftUpperHeight = Mathf.Clamp(_leftUpperHeight, MinimumUpperHeight,
                Mathf.Max(MinimumUpperHeight, available - MinimumLowerHeight));
            var rightMaximum = _terrainOpen
                ? available - MinimumLowerHeight
                : available;
            _rightUpperHeight = Mathf.Clamp(_rightUpperHeight, MinimumUpperHeight,
                Mathf.Max(MinimumUpperHeight, rightMaximum));

            RememberRect(_plotPanel);
            // PARCELAS: subir 20 px la parte superior SIN cambiar el borde
            // inferior, las barras ni el espacio del panel Objetos.
            const float plotTopGain = 20f;
            SetTopLeft(_plotPanel, 0f, BarHeight - plotTopGain,
                PanelWidth, _leftUpperHeight + plotTopGain);
            FitPlotPanel();

            PlaceCornerButton(_objectsToggle, false, false);
            PlaceCornerButton(_historyToggle, true, true);
            PlaceCornerButton(_terrainToggle, true, false);
            PlaceTopButtons(width);
            PlaceTileMenu();
            PlaceMapIdentity();

            ApplyObjectsPanel(_objectsMenu, true);
            if (_leftDivider != null)
            {
                // La linea y su punto de agarre permanecen centrados en el
                // limite original. Los botones se mantienen dentro del panel.
                SetTopLeft(_leftDivider, 0f,
                    BarHeight + _leftUpperHeight - DividerHeight * .5f,
                    PanelWidth, DividerHeight);
                var handle = _leftDivider.Find("Tirador") as RectTransform;
                if (handle != null)
                {
                    handle.anchorMin = new Vector2(0f, .5f);
                    handle.anchorMax = new Vector2(1f, .5f);
                    handle.pivot = new Vector2(.5f, .5f);
                    handle.anchoredPosition = Vector2.zero;
                    handle.sizeDelta = new Vector2(-8f, 6f);
                }
                _leftDivider.gameObject.SetActive(true);
                _leftDivider.SetAsLastSibling();
            }

            var rightContentHeight = height - BarHeight * 2f;
            float historyHeight;
            float terrainHeight;
            if (_historyOpen)
            {
                historyHeight = _rightUpperHeight;
                terrainHeight = _terrainOpen
                    ? Mathf.Max(0f, rightContentHeight - historyHeight)
                    : 0f;
            }
            else
            {
                historyHeight = 0f;
                terrainHeight = _terrainOpen ? rightContentHeight : 0f;
            }
            ScenarioEditorHistory.SetDockRect(PanelWidth, historyHeight, BarHeight);
            ScenarioEditorHistory.SetDockVisible(_historyOpen);
            ScenarioEditorTerrainPainter.SetDockRect(PanelWidth, terrainHeight,
                BarHeight + historyHeight, BarHeight);
            ScenarioEditorTerrainPainter.SetDockVisible(_terrainOpen);
            ApplyDockStyles();

            if (_rightDivider != null)
            {
                SetTopRight(_rightDivider, 0f,
                    BarHeight + historyHeight - DividerHeight * .5f,
                    PanelWidth, DividerHeight);
                _rightDivider.gameObject.SetActive(_historyOpen);
                _rightDivider.SetAsLastSibling();
            }
            SetSelected(_historyToggle, _historyOpen);
            SetSelected(_terrainToggle, _terrainOpen);
        }

        // Fondos de Historial/Terreno y controles internos.
        // No se ejecuta por fotograma: solo por construccion/redimensionado.
        private void ApplyDockStyles()
        {
            ScenarioEditorHistory.SetDockStyle(_style.PanelSprite, _style.PanelColor);
            ScenarioEditorTerrainPainter.SetDockStyle(_style.PanelSprite, _style.PanelColor);
            // Primera apertura: las vistas pueden haberse construido antes de
            // crear el Dock, por lo que sus Postfix iniciales no tenian estilo.
            var painter = AccessTools.Field(typeof(ScenarioEditorTerrainPainter),
                "_behaviour")?.GetValue(null) as ScenarioEditorTerrainPainterBehaviour;
            StyleTerrainButtons(painter);
            ArrangeTerrainTextureTiles(painter);
            foreach (var history in Resources.FindObjectsOfTypeAll<ScenarioEditorHistory>())
                if (history != null && history.gameObject.scene.IsValid())
                    StyleHistoryButtons(history);
        }

        // PARCELAS: botones Aislar, Anadir, Mover, Eliminar y filas.
        // Coordenadas de lista y cabecera ajustables en este metodo.
        private void FitPlotPanel()
        {
            if (_plotPanel == null) return;
            RememberRect(_plotPanel);
            if (_plotPanel.parent != _root) _plotPanel.SetParent(_root, false);
            ApplyPanelStyle(_plotPanel.gameObject);
            var panelImage = _plotPanel.GetComponent<Image>();
            if (panelImage != null)
            {
                panelImage.sprite = null;
                panelImage.type = Image.Type.Simple;
                panelImage.color = _style.PanelColor;
            }
            EnsureSortingCanvas(_plotPanel.gameObject, 2850);
            _plotPanel.SetAsLastSibling();
            if (_plotViewport != null)
            {
                RememberRect(_plotViewport);
                IgnoreParentLayout(_plotViewport);
                _plotViewport.anchorMin = _plotViewport.anchorMax = new Vector2(0f, 1f);
                _plotViewport.pivot = new Vector2(0f, 1f);
                _plotViewport.anchoredPosition = new Vector2(4f, -70f);
                // Mayor altura visible: las parcelas aprovechan el espacio
                // recuperado bajo el nombre del hospital.
                _plotViewport.sizeDelta = new Vector2(PanelWidth - 8f,
                    Mathf.Max(58f, _leftUpperHeight - 96f));
                var plotScroll = _plotViewport.GetComponent<ScrollRect>();
                InstallNativeVerticalScrollbar(_plotPanel, plotScroll,
                    "UP Plot Native Vertical Scrollbar", 72f, 46f);
                ResizePlotScrollContent(plotScroll);
            }
            var layerRoot = _storage?.PlotLayerRoot as RectTransform;
            if (layerRoot != null)
            {
                RememberRect(layerRoot);
                IgnoreParentLayout(layerRoot);
                layerRoot.anchorMin = layerRoot.anchorMax = new Vector2(0f, 1f);
                layerRoot.pivot = new Vector2(0f, 1f);
                layerRoot.anchoredPosition = new Vector2(5f, -34f);
                layerRoot.sizeDelta = new Vector2(PanelWidth - 10f, 30f);
            }
            var plotButtons = _storage?.PlotChromeButtons ?? new Button[0];
            for (var buttonIndex = 0; buttonIndex < plotButtons.Length; ++buttonIndex)
            {
                var button = plotButtons[buttonIndex];
                if (button == null) continue;
                RememberRect(button.transform as RectTransform);
                if (button.transform.parent == _menu.transform)
                    button.transform.SetParent(_root, false);
                if (buttonIndex >= 3)
                {
                    ApplyButtonStyle(button, _style.ButtonColor);
                    if (buttonIndex >= 4) StyleCompactButtonText(button); // Añadir, Mover, Eliminar
                }
                button.transform.SetAsLastSibling();
            }
            _storage?.RefreshLayerColors();
            var landscape = _plotPanel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "UP Landscape - Row");
            var landscapeImage = landscape?.GetComponent<Image>();
            if (landscapeImage != null) landscapeImage.color = _style.ButtonColor;
            foreach (var row in _plotPanel.GetComponentsInChildren<PlotSelectionItem>(true))
                ApplyPlotRow(row);
            if (_resetPlotScroll)
            {
                var scroll = _plotViewport?.GetComponent<ScrollRect>();
                if (scroll?.content != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
                _resetPlotScroll = false;
            }
        }

        private void ApplyPlotRow(PlotSelectionItem row)
        {
            if (row == null) return;
            var rect = row.transform as RectTransform;
            if (rect != null) rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, PanelWidth - 25f);
            var selectButton = AccessTools.Field(typeof(PlotSelectionItem), "_button")?
                .GetValue(row) as Button;
            ApplyButtonStyle(selectButton);
            CompactPlotRow(row, _style.ButtonSprite,
                _style.ButtonColor, _style.SelectedColor);
        }

        private float CalculateAutomaticPlotPanelHeight()
        {
            foreach (var row in _plotPanel.GetComponentsInChildren<PlotSelectionItem>(true))
                ApplyPlotRow(row);
            var scroll = _plotViewport?.GetComponent<ScrollRect>();
            var content = scroll?.content;
            if (content != null)
            {
                ResizePlotScrollContent(scroll);
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                var preferred = LayoutUtility.GetPreferredHeight(content);
                if (preferred > 1f) return 84f + preferred + 46f;
            }
            var count = Mathf.Max(1, _level?.WorldState?.HospitalPlots?.Count ?? 1);
            return 163f + count * 36f;
        }

        private void RefreshPlotPanelForCount()
        {
            if (_closing || _menu == null || _plotViewport == null || _plotPanel == null)
                return;
            _resetPlotScroll = true;
            ResizePlotScrollContent(_plotViewport.GetComponent<ScrollRect>());
            ApplyLayout(true);
        }

        private static void ResizePlotScrollContent(ScrollRect scroll)
        {
            var content = scroll?.content;
            if (content == null) return;
            var sizer = content.GetComponent<ScenarioEditorPlotContentSizer>() ??
                content.gameObject.AddComponent<ScenarioEditorPlotContentSizer>();
            sizer.Recalculate(content);
        }

        // BARRAS VERTICALES: clonar el Scrollbar vertical real de Ajustes.
        // No se reutiliza ni se gira ningun Slider de valores.
        internal static Scrollbar InstallNativeVerticalScrollbar(RectTransform host,
            ScrollRect scroll, string name, float top, float bottom, bool left = false)
        {
            if (_active == null || host == null || scroll == null) return null;
            var existing = host.Find(name)?.GetComponent<Scrollbar>();
            if (existing == null)
            {
                Scrollbar source = null;
                foreach (var screen in Resources.FindObjectsOfTypeAll<PreferencesScreen>())
                {
                    if (screen == null) continue;
                    source = screen.GetComponentsInChildren<Scrollbar>(true)
                        .Where(candidate => candidate != null &&
                            !candidate.transform.IsChildOf(host) &&
                            (candidate.direction == Scrollbar.Direction.BottomToTop ||
                             candidate.direction == Scrollbar.Direction.TopToBottom))
                        .OrderByDescending(candidate =>
                            Mathf.Abs((candidate.transform as RectTransform)?.rect.height ?? 0f))
                        .FirstOrDefault();
                    if (source != null) break;
                }
                if (source == null)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Interfaz: no se encontro el Scrollbar vertical nativo de Ajustes.");
                    return null;
                }
                existing = UnityEngine.Object.Instantiate(source, host, false);
                existing.gameObject.name = name;
                var layout = existing.GetComponent<LayoutElement>();
                if (layout != null) layout.enabled = false;
                existing.onValueChanged.RemoveAllListeners();
            }

            var oldScrollbar = scroll.verticalScrollbar;
            if (oldScrollbar != null && oldScrollbar.gameObject != existing.gameObject)
                oldScrollbar.gameObject.SetActive(false);

            var rect = existing.transform as RectTransform;
            rect.anchorMin = new Vector2(left ? 0f : 1f, 0f);
            rect.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            rect.pivot = new Vector2(left ? 0f : 1f, .5f);
            rect.offsetMin = new Vector2(left ? 2f : -14f, bottom);
            rect.offsetMax = new Vector2(left ? 14f : -2f, -top);
            rect.localScale = Vector3.one;
            existing.direction = Scrollbar.Direction.BottomToTop;
            existing.interactable = true;
            var handleGraphic = existing.handleRect == null
                ? null : existing.handleRect.GetComponent<Graphic>();
            if (handleGraphic != null)
            {
                // El ScrollRect modifica el tamano del asa, no su aspecto.
                // Conservar el sprite nativo y restaurar su tinte claro.
                handleGraphic.enabled = true;
                handleGraphic.color = Color.white;
                existing.targetGraphic = handleGraphic;
            }
            var colors = existing.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.82f, .82f, .82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.72f, .72f, .72f, 1f);
            colors.colorMultiplier = 1f;
            existing.colors = colors;
            existing.gameObject.SetActive(true);
            existing.transform.SetAsLastSibling();
            scroll.verticalScrollbar = existing;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 0f;
            if (scroll.viewport != null)
            {
                var minimum = scroll.viewport.offsetMin;
                var maximum = scroll.viewport.offsetMax;
                if (left)
                    minimum.x = 12f;
                else
                    maximum.x = -12f;
                scroll.viewport.offsetMin = minimum;
                scroll.viewport.offsetMax = maximum;
            }
            return existing;
        }

        private sealed class ScenarioEditorPauseSpacing : MonoBehaviour { }

        private void IgnoreParentLayout(RectTransform rect)
        {
            if (rect == null) return;
            var layout = rect.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = rect.gameObject.AddComponent<LayoutElement>();
                _createdLayouts.Add(layout);
            }
            else if (!_originalLayouts.ContainsKey(layout))
                _originalLayouts.Add(layout, layout.ignoreLayout);
            layout.ignoreLayout = true;
        }

        private static RectTransform FindPlotPanel(RectTransform menuRoot,
            RectTransform viewport, RectTransform layerRoot)
        {
            if (menuRoot == null || viewport == null) return viewport;
            var shared = layerRoot == null ? null : LowestCommonAncestor(viewport, layerRoot);
            if (shared != null && shared != menuRoot) return shared;

            RectTransform best = null;
            for (var current = viewport; current != null && current != menuRoot;
                current = current.parent as RectTransform)
            {
                var image = current.GetComponent<Image>();
                var width = Mathf.Abs(current.rect.width);
                if (image != null && width >= 160f && width <= 360f)
                    best = current;
            }
            return best ?? (viewport.parent as RectTransform) ?? viewport;
        }

        private static RectTransform LowestCommonAncestor(RectTransform first,
            RectTransform second)
        {
            if (first == null || second == null) return null;
            for (var left = first; left != null; left = left.parent as RectTransform)
                for (var right = second; right != null; right = right.parent as RectTransform)
                    if (ReferenceEquals(left, right)) return left;
            return null;
        }

        // CADA PARCELA: ancho, alto, texto a la izquierda y 5 checks a la derecha.
        // Ajustar checkSize/checkGap aqui; colores en ScenarioEditorPlotRowSkin.
        private static void CompactPlotRow(PlotSelectionItem row, Sprite sprite,
            Color normal, Color selected)
        {
            if (row == null) return;
            var rowRoot = row.transform as RectTransform;
            if (rowRoot == null) return;
            const float rowWidth = PanelWidth - 25f;
            const float checkSize = 23f;
            const float checkGap = 1f;
            const float labelInset = 9f;
            // 5 * 23 + 4 * 1 = 119 px; todos quedan dentro de 195 px.
            const float checksStart = rowWidth - (5f * checkSize + 4f * checkGap);
            const float labelRight = checksStart - 3f;
            var rowLayout = row.GetComponent<LayoutElement>() ??
                row.gameObject.AddComponent<LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = 36f;
            rowLayout.minWidth = rowLayout.preferredWidth = rowWidth;
            rowLayout.flexibleWidth = 0f;
            rowRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);

            // Nombre a la izquierda, cinco checks a su DERECHA, sin recortes;
            // el HorizontalLayoutGroup nativo no impone medidas fuera del panel.
            // Los toggles originales mantienen sus eventos y referencias.
            var nativeLayout = row.GetComponent<HorizontalLayoutGroup>();
            if (nativeLayout != null) nativeLayout.enabled = false;

            var selectButton = AccessTools.Field(typeof(PlotSelectionItem), "_button")?
                .GetValue(row) as Button;
            var text = AccessTools.Field(typeof(PlotSelectionItem), "_text")?
                .GetValue(row) as TMP_Text;
            if (selectButton != null && selectButton.transform != rowRoot)
            {
                var backgroundRect = selectButton.transform as RectTransform;
                if (backgroundRect != null)
                {
                    backgroundRect.SetParent(rowRoot, false);
                    backgroundRect.SetAsFirstSibling();
                    backgroundRect.anchorMin = Vector2.zero;
                    backgroundRect.anchorMax = Vector2.one;
                    backgroundRect.offsetMin = Vector2.zero;
                    backgroundRect.offsetMax = Vector2.zero;
                    var element = selectButton.GetComponent<LayoutElement>() ??
                        selectButton.gameObject.AddComponent<LayoutElement>();
                    element.ignoreLayout = true;
                }
            }
            if (text != null)
            {
                var textRect = text.transform as RectTransform;
                if (textRect != null)
                {
                    // El texto conserva su padre y su funcionalidad original.
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.offsetMin = new Vector2(labelInset, 0f);
                    textRect.offsetMax = new Vector2(labelRight - rowWidth, 0f);
                }
                text.enableAutoSizing = true;
                text.fontSizeMin = 9f;
                text.fontSizeMax = 12f;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.color = Color.white;
                var labelLayout = text.GetComponent<LayoutElement>();
                if (labelLayout != null) labelLayout.ignoreLayout = true;
            }
            var names = new[]
            {
                "_toggleState", "_toggleVisible", "_toggleLayerBase",
                "_toggleLayerBuilt", "_toggleLayerUnbuilt"
            };
            for (var index = 0; index < names.Length; ++index)
            {
                var toggle = AccessTools.Field(typeof(PlotSelectionItem), names[index])?
                    .GetValue(row) as Toggle;
                var rect = toggle?.transform as RectTransform;
                if (rect == null) continue;
                rect.SetParent(rowRoot, false);
                rect.SetAsLastSibling();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
                rect.pivot = new Vector2(0f, .5f);
                rect.anchoredPosition = new Vector2(checksStart + index *
                    (checkSize + checkGap), 0f);
                rect.sizeDelta = new Vector2(checkSize, checkSize);
                rect.localScale = Vector3.one;
                var element = toggle.GetComponent<LayoutElement>() ??
                    toggle.gameObject.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
            }
            var skin = row.GetComponent<ScenarioEditorPlotRowSkin>() ??
                row.gameObject.AddComponent<ScenarioEditorPlotRowSkin>();
            skin.Initialise(row, sprite, normal, selected);
        }

        // BARRA SUPERIOR: Baldosas/Camara/Personajes/Niebla/Estilos/Ajustes.
        // Medidas editables: gap, buttonWidth y rect.sizeDelta (altura 40).
        private void PlaceTopButtons(float canvasWidth)
        {
            var buttons = new[]
            {
                _storage?.BuildButton,
                _storage?.CameraButton,
                ScenarioCharacterViewer.ToggleButton,
                ScenarioFogViewer.ToggleButton,
                _storage?.StylesButton,
                _settingsToggle
            }.Where(button => button != null).ToArray();
            if (buttons.Length == 0) return;
            var centralWidth = Mathf.Max(1f, canvasWidth - PanelWidth * 2f);
            var gap = 8f;
            var buttonWidth = Mathf.Min(208f,
                (centralWidth - gap * (buttons.Length + 1)) / buttons.Length);
            var total = buttonWidth * buttons.Length + gap * (buttons.Length - 1);
            var x = PanelWidth + (centralWidth - total) * .5f;
            foreach (var button in buttons)
            {
                var rect = button.transform as RectTransform;
                if (rect == null) continue;
                RememberRect(rect);
                rect.SetParent(_root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                var buttonX = ReferenceEquals(button, _storage?.BuildButton)
                    ? PanelWidth : x;
                rect.anchoredPosition = new Vector2(buttonX, 0f);
                rect.sizeDelta = new Vector2(buttonWidth, 40f);
                ApplyButtonStyle(button);
                button.transform.SetAsLastSibling();
                x += buttonWidth + gap;
            }
        }

        // El submenu nativo de Baldosas conserva su jerarquia y eventos;
        // solo se acopla justo debajo de su boton superior.
        private void PlaceTileMenu()
        {
            var menu = _storage?.TileSelectionRoot;
            var buttonRect = _storage?.BuildButton?.transform as RectTransform;
            if (menu == null || buttonRect == null) return;
            RememberRect(menu);
            menu.SetParent(_root, false);
            menu.anchorMin = menu.anchorMax = new Vector2(0f, 1f);
            menu.pivot = new Vector2(0f, 1f);
            // El boton superior mide 40 px: el submenu comienza exactamente
            // bajo el, sin el hueco anterior de 10 px.
            menu.anchoredPosition = new Vector2(PanelWidth, -40f);
            menu.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                buttonRect.rect.width);
            var blocker = menu.GetComponent<Image>() ?? menu.gameObject.AddComponent<Image>();
            if (blocker.sprite == null) blocker.color = new Color(0f, 0f, 0f, .001f);
            blocker.raycastTarget = true;
            EnsureSortingCanvas(menu.gameObject, 3050);
            foreach (var button in menu.GetComponentsInChildren<Button>(true))
                ApplyButtonStyle(button, _style.ButtonColor);
            menu.SetAsLastSibling();
        }

        // PESTANAS: Objetos abajo izquierda, Historial arriba derecha,
        // Terreno abajo derecha. Todas comparten PanelWidth x BarHeight.
        private void PlaceCornerButton(Button button, bool right, bool top)
        {
            if (button == null) return;
            var rect = button.transform as RectTransform;
            if (rect == null) return;
            RememberRect(rect);
            rect.SetParent(_root, false);
            if (right)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
                rect.pivot = new Vector2(1f, top ? 1f : 0f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, top ? 1f : 0f);
                rect.pivot = new Vector2(0f, top ? 1f : 0f);
            }
            // Misma posicion original. SOLO se dibuja detras del panel;
            // la parte no tapada conserva el clic mediante su raycaster.
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(PanelWidth, BarHeight);
            ApplyButtonStyle(button);
            EnsureSortingCanvas(button.gameObject, 2750);
            button.transform.SetAsLastSibling();
        }

        private void PlaceMapIdentity()
        {
            var identity = _storage?.MapIdentityRoot;
            var rect = identity?.transform as RectTransform;
            if (rect == null || _leftTitle == null) return;
            RememberRect(rect);
            rect.SetParent(_leftTitle.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var label = identity.GetComponentInChildren<Text>(true);
            if (label != null) label.alignment = TextAnchor.MiddleCenter;
            var tmp = identity.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) tmp.alignment = TextAlignmentOptions.Center;
            rect.SetAsLastSibling();
        }

        // PANEL OBJETOS: superficie propia, fuera del ciclo de menus del HUD.
        // El componente nativo conserva exclusivamente la carga del catalogo.
        // TAMANO: PanelWidth, altura = hueco entre Parcelas y boton Objetos.
        // POSICION: (0, BarHeight), anclado abajo a la izquierda.
        private void ApplyObjectsPanel(LandscapeObjectsMenu menu, bool force)
        {
            if (_canvasRect == null) return;
            if (menu == null)
            {
                if (_objectsDockHost != null) return;
                _objectsMenu = null;
                _objectsPanelHeight = -1f;
                return;
            }
            var height = Mathf.Max(MinimumLowerHeight,
                _canvasRect.rect.height - BarHeight * 2f - _leftUpperHeight);
            var width = _objectsExpanded
                ? Mathf.Max(PanelWidth, _canvasRect.rect.width - PanelWidth)
                : PanelWidth;
            var created = _objectsDockHost == null ||
                !ReferenceEquals(_objectsHostOwner, menu);
            if (created)
            {
                if (_objectsDockHost != null) Destroy(_objectsDockHost.gameObject);
                var panel = new GameObject("UP Objects Panel", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                panel.transform.SetParent(_root, false);
                var host = (RectTransform)panel.transform;
                ApplyPanelStyle(panel);
                EnsureSortingCanvas(panel, 2850);
                if (!ScenarioEditorObjectDock.DetachFromNativeMenus(menu, host,
                    _level != null ? _level.HUD : null, _root))
                {
                    Destroy(panel);
                    _objectsDockHost = null;
                    _objectsHostOwner = null;
                    return;
                }
                _objectsDockHost = host;
                _objectsMenu = menu;
                _objectsHostOwner = menu;
                _objectsOpen = true;
            }
            if (!force && !created && ReferenceEquals(_objectsMenu, menu) &&
                Mathf.Abs(_objectsPanelHeight - height) < .1f &&
                Mathf.Abs(_objectsDockHost.rect.width - width) < .1f) return;
            _objectsPanelHeight = height;
            if (_objectsDockHost == null) return;
            var rect = _objectsDockHost;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(0f, BarHeight);
            rect.sizeDelta = new Vector2(width, height);
            rect.gameObject.SetActive(_objectsOpen);
            ScenarioEditorObjectDock.Configure(menu, rect, width, height);
            SetSelected(_objectsToggle, _objectsOpen);
        }

        private LandscapeObjectsMenu FindActiveObjectsMenu()
        {
            LandscapeObjectsMenu fallback = null;
            foreach (var menu in Resources.FindObjectsOfTypeAll<LandscapeObjectsMenu>())
            {
                if (menu == null || !menu.gameObject.scene.IsValid()) continue;
                if (menu.gameObject.activeInHierarchy) return menu;
                if (fallback == null) fallback = menu;
            }
            return fallback != null && fallback.gameObject.activeSelf ? fallback : null;
        }

        private void ApplyPanelStyle(GameObject panel)
        {
            if (panel == null) return;
            var image = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            // El panel de Objetos y las demas superficies tienen esquinas rectas.
            image.overrideSprite = null;
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = _style.PanelColor;
            image.raycastTarget = true;
        }

        private void EnsureSortingCanvas(GameObject root, int order)
        {
            if (root == null) return;
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.AddComponent<Canvas>();
                _createdCanvases.Add(canvas);
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            if (root.GetComponent<GraphicRaycaster>() == null)
                _createdRaycasters.Add(root.AddComponent<GraphicRaycaster>());
        }

        // ESTILO UNICO DE BOTONES: sprite, borde blanco, colores y sonido.
        // Cambiar los RGB en WorkshopStyle, no en cada boton por separado.
        private void ApplyButtonStyle(Button button, Color? tint = null)
        {
            if (button == null || _style == null) return;
            // La plantilla, el borde y el sonido se configuran una sola vez.
            if (_styledButtons.Add(button))
            {
                var animator = button.GetComponent<ButtonAnimator>();
                if (animator != null) animator.enabled = false;
                if (button.image != null)
                {
                    button.image.overrideSprite = null;
                    button.image.sprite = _style.ButtonSprite;
                    button.image.type = Image.Type.Sliced;
                }
                button.transition = Selectable.Transition.None;
                button.colors = _style.ButtonColors;
                var borderRoot = button.image != null ? button.image.gameObject : button.gameObject;
                var border = borderRoot.GetComponent<Outline>();
                if (border == null) border = borderRoot.AddComponent<Outline>();
                border.effectColor = Color.white;
                border.effectDistance = new Vector2(1f, -1f);
                border.useGraphicAlpha = true;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.color = Color.white;
                // CloneButton elimina los eventos runtime del prefab, incluido
                // ButtonSFX. Recuperar uno y el MISMO sonido en todos.
                StandardiseClickSound(button);
            }
            if (button.image != null)
            {
                var desired = tint ?? _style.ButtonColor;
                if (button.image.color != desired) button.image.color = desired;
            }
        }

        // Solo botones PEQUENOS: misma altura de letra y centrado real.
        // No tocar textos del menu, barra superior, ni pestañas de paneles.
        // TEXTO SOLO EN BOTONES PEQUENOS (Anadir/Mover/Eliminar y
        // Elevar/Bajar/Aplanar). FontSize, fuente y alineacion aqui.
        private static void StyleCompactButtonText(Button button)
        {
            if (button == null) return;
            var regular = button.transform.Find("UP Compact Label")?.GetComponent<Text>() ??
                button.GetComponentInChildren<Text>(true);
            if (regular == null)
            {
                // Los botones de parcela vienen con TMP; solo a esos tres
                // se les asigna la misma fuente Arial que usa Terreno.
                var tmp = button.GetComponentInChildren<TMP_Text>(true);
                if (tmp == null) return;
                var go = new GameObject("UP Compact Label", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text));
                go.transform.SetParent(button.transform, false);
                regular = go.GetComponent<Text>();
                regular.text = tmp.text;
                tmp.enabled = false;
            }
            var rect = regular.rectTransform;
            if (rect != null && rect != button.transform)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.offsetMin = new Vector2(2f, 0f);
                rect.offsetMax = new Vector2(-2f, 0f);
            }
            regular.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            regular.fontSize = 14;
            regular.fontStyle = FontStyle.Bold;
            regular.alignment = TextAnchor.MiddleCenter;
            regular.horizontalOverflow = HorizontalWrapMode.Overflow;
            regular.color = Color.white;
            regular.raycastTarget = false;
        }

        private static void StandardiseClickSound(Button button)
        {
            if (button == null) return;
            var sfx = button.GetComponent<ButtonSFX>();
            if (sfx == null) sfx = button.gameObject.AddComponent<ButtonSFX>();
            sfx.AudioEvent = ButtonSFX.ButtonAudioEvent.Default;
            // Evitar listeners duplicados en el mismo boton.
            sfx.UpdateListeners();
        }

        private void SetSelected(Button button, bool selected)
        {
            ApplyButtonStyle(button, selected ? _style.SelectedColor : _style.ButtonColor);
        }

        private Button CloneButton(Button source, Transform parent, string name,
            string text, Action action)
        {
            var button = Instantiate(source, parent, false);
            button.gameObject.name = name;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            button.gameObject.SetActive(true);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
            ApplyButtonStyle(button);
            return button;
        }

        private static GameObject NewImage(string name, Transform parent, Sprite sprite,
            Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return go;
        }

        private static void CreateLabel(Transform parent, string value, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void SetTopLeft(RectTransform rect, float x, float y,
            float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopRight(RectTransform rect, float x, float y,
            float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void OnDisable()
        {
            if (!ScenarioEditorFlow.IsRunning) ReleaseUi();
        }

        private void OnDestroy() => ReleaseUi();

        internal static void Shutdown()
        {
            var active = _active;
            // Invalidar primero la entrada estatica: aunque la instancia ya se
            // estuviera cerrando, ninguna nueva fila puede usar sus referencias.
            _active = null;
            if (active == null) return;
            // ReleaseUi devuelve algunos controles nativos a su padre original.
            // Ocultar antes el menu evita que reaparezcan durante ese frame.
            if (active._menu != null) active._menu.gameObject.SetActive(false);
            active.ReleaseUi();
        }

        private void ReleaseUi()
        {
            if (ReferenceEquals(_active, this)) _active = null;
            if (_closing) return;
            _closing = true;
            // Ocultar los controles inmediatamente: Destroy se ejecuta al
            // final del frame y no debe dejar restos en el menu principal.
            if (_root != null) _root.gameObject.SetActive(false);
            // El catalogo y su panel no pertenecen al registro de menus del HUD.
            if (_objectsMenu != null)
            {
                ScenarioEditorObjectDock.Forget(_objectsMenu);
                Destroy(_objectsMenu.gameObject);
            }
            if (_objectsDockHost != null) Destroy(_objectsDockHost.gameObject);
            _objectsDockHost = null;
            _objectsHostOwner = null;
            foreach (var pair in _originalRects)
            {
                var rect = pair.Key;
                var before = pair.Value;
                if (rect == null || before.Parent == null) continue;
                rect.SetParent(before.Parent, false);
                rect.SetSiblingIndex(Mathf.Min(before.Sibling,
                    Mathf.Max(0, before.Parent.childCount - 1)));
                rect.anchorMin = before.AnchorMin;
                rect.anchorMax = before.AnchorMax;
                rect.pivot = before.Pivot;
                rect.anchoredPosition = before.Position;
                rect.sizeDelta = before.Size;
                rect.localScale = before.Scale;
            }
            foreach (var pair in _originalLayouts)
                if (pair.Key != null) pair.Key.ignoreLayout = pair.Value;
            foreach (var raycaster in _createdRaycasters)
                if (raycaster != null) Destroy(raycaster);
            foreach (var canvas in _createdCanvases)
                if (canvas != null) Destroy(canvas);
            foreach (var layout in _createdLayouts)
                if (layout != null) Destroy(layout);
            if (_root != null) Destroy(_root.gameObject);
            // Los botones nativos tambien se desmontan al salir; no conservar
            // texturas procedurales entre sesiones del editor.
            if (_style != null)
            {
                if (_style.ButtonSprite != null) Destroy(_style.ButtonSprite);
                if (_style.ButtonTexture != null) Destroy(_style.ButtonTexture);
            }
            _originalRects.Clear();
            _originalLayouts.Clear();
            _styledButtons.Clear();
            _createdRaycasters.Clear();
            _createdCanvases.Clear();
            _createdLayouts.Clear();
            _root = null;
            _plotPanel = null;
            _plotViewport = null;
            _objectsMenu = null;
            _settingsOverlay = null;
            _settingsCloseButton = null;
            _settingsTabButtons = null;
            _settingsTabAnimators = null;
            _settingsTabRoots = null;
            _settingsTabFolderBackgrounds = null;
            _settingsPages = null;
            ReleaseDiseaseSettingsContents();
            _style = null;
            _storage = null;
            _menu = null;
            _level = null;
            _canvas = null;
            _canvasRect = null;
        }

        // PALETA CENTRAL: edita aqui el azul del fondo de paneles,
        // azul de botones normales y azul oscuro de los seleccionados.
        private sealed class WorkshopStyle
        {
            internal Sprite PanelSprite = null;
            internal Texture2D ButtonTexture;
            internal Sprite ButtonSprite;
            internal Color PanelColor = new Color32(10, 110, 170, 255);
            internal Color HeaderColor = new Color(.045f, .38f, .64f, 1f);
            // Referencia visual: celeste de los botones de la segunda imagen.
            internal Color ButtonColor = new Color(.14f, .62f, .89f, 1f);
            internal Color SelectedColor = new Color32(10, 110, 170, 255);
            internal ColorBlock ButtonColors = ColorBlock.defaultColorBlock;

            internal static WorkshopStyle Read(Button baldosa)
            {
                var style = new WorkshopStyle();
                // Los sprites de los botones nativos tienen el centro gris oscuro:
                // cualquier tintado azul quedaba oscuro, aun con color blanco.
                // Crear UNA textura blanca 9-slice, usada por todos los botones;
                // el tinte sera literalmente el color elegido, con el mismo marco.
                const int size = 32;
                style.ButtonTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                style.ButtonTexture.name = "UP Editor - Boton azul blanco";
                style.ButtonTexture.wrapMode = TextureWrapMode.Clamp;
                style.ButtonTexture.filterMode = FilterMode.Bilinear;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; ++y)
                    for (int x = 0; x < size; ++x)
                    {
                        var dx = Mathf.Max(Mathf.Abs(x - 15.5f) - 9.5f, 0f);
                        var dy = Mathf.Max(Mathf.Abs(y - 15.5f) - 9.5f, 0f);
                        var distance = Mathf.Sqrt(dx * dx + dy * dy);
                        var alpha = (byte)Mathf.RoundToInt(255f *
                            Mathf.Clamp01(6.5f - distance));
                        pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                    }
                style.ButtonTexture.SetPixels32(pixels);
                style.ButtonTexture.Apply(false, false);
                style.ButtonSprite = Sprite.Create(style.ButtonTexture,
                    new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
                style.ButtonSprite.name = "UP Editor - Boton uniforme";
                var colors = ColorBlock.defaultColorBlock;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.pressedColor = Color.white;
                colors.selectedColor = Color.white;
                colors.disabledColor = Color.white;
                colors.colorMultiplier = 1f;
                style.ButtonColors = colors;
                return style;
            }
        }
    }

    internal sealed class ScenarioEditorDockDivider : MonoBehaviour,
        IBeginDragHandler, IDragHandler
    {
        private RectTransform _root;
        private Action<float> _setter;

        internal void Initialise(RectTransform root, Action<float> setter)
        {
            _root = root;
            _setter = setter;
        }

        public void OnBeginDrag(PointerEventData eventData) => Apply(eventData);
        public void OnDrag(PointerEventData eventData) => Apply(eventData);

        private void Apply(PointerEventData eventData)
        {
            if (_root == null || _setter == null || eventData == null) return;
            _setter(eventData.position.y);
        }
    }

    internal sealed class ScenarioEditorPlotContentSizer : MonoBehaviour
    {
        private RectTransform _content;

        internal void Recalculate(RectTransform content)
        {
            _content = content;
            if (_content == null) return;
            var fitter = _content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.enabled = false;
            }
            var layout = _content.GetComponent<VerticalLayoutGroup>();
            var height = layout == null ? 0f : layout.padding.top + layout.padding.bottom;
            var rows = 0;
            for (var index = 0; index < _content.childCount; ++index)
            {
                var child = _content.GetChild(index) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                if (child.GetComponent<PlotSelectionItem>() == null &&
                    child.name != "UP Landscape - Row") continue;
                var element = child.GetComponent<LayoutElement>();
                var childHeight = element != null && element.preferredHeight > 0f
                    ? element.preferredHeight : child.rect.height;
                height += Mathf.Max(0f, childHeight);
                ++rows;
            }
            if (layout != null && rows > 1)
                height += (rows - 1) * layout.spacing;
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(0f, height));
        }

        private void OnTransformChildrenChanged()
        {
            if (_content != null) Recalculate(_content);
        }
    }

    internal sealed class ScenarioEditorPlotRowSkin : MonoBehaviour
    {
        private Button _button;
        private TMP_Text _label;
        // COLORES EXACTOS DE CHECKS: Base(magenta), Build(naranja),
        // Unbuild(verde). Los tres botones de arriba usan estos RGB.
        internal static readonly Color[] LayerColors =
        {
            new Color(240f / 255f, 90f / 255f, 140f / 255f, 1f),
            new Color(1f, .58f, .08f, 1f),
            new Color(.48f, .72f, .25f, 1f)
        };
        private Toggle[] _toggles;
        private Color[] _toggleColors;
        private Toggle _visibilityToggle;
        private GameObject _visibilityMark;
        private Sprite _sprite;
        private Color _normal;
        private Color _selectedColor;
        private bool _selected;
        private bool _initialised;

        internal void Initialise(PlotSelectionItem row, Sprite sprite,
            Color normal, Color selected)
        {
            _sprite = sprite;
            _normal = normal;
            _selectedColor = selected;
            if (!_initialised)
            {
                _button = AccessTools.Field(typeof(PlotSelectionItem), "_button")?
                    .GetValue(row) as Button;
                _label = AccessTools.Field(typeof(PlotSelectionItem), "_text")?
                    .GetValue(row) as TMP_Text;
                var nativeSelected = AccessTools.Field(typeof(PlotSelectionItem), "_selectedColor")?
                    .GetValue(row);
                if (_button?.targetGraphic != null && nativeSelected is Color)
                    _selected = ColorDistance(_button.targetGraphic.color,
                        (Color)nativeSelected) < .01f;
                var names = new[]
                {
                    "_toggleState", "_toggleVisible", "_toggleLayerBase",
                    "_toggleLayerBuilt", "_toggleLayerUnbuilt"
                };
                _toggles = names.Select(name =>
                    AccessTools.Field(typeof(PlotSelectionItem), name)?.GetValue(row) as Toggle)
                    .Where(toggle => toggle != null).ToArray();
                _toggleColors = _toggles.Select(toggle =>
                    toggle.targetGraphic == null ? Color.white : toggle.targetGraphic.color).ToArray();
                _visibilityToggle = AccessTools.Field(typeof(PlotSelectionItem), "_toggleVisible")?
                    .GetValue(row) as Toggle;
                CreateVisibilityMark();
                _initialised = true;
            }
            Apply();
        }

        internal void SetSelected(bool selected)
        {
            _selected = selected;
            Apply();
        }

        private void Apply()
        {
            // El evento nativo OnSelectHospitalPlot vuelve gris el nombre
            // de las parcelas no seleccionadas: restaurarlo siempre a blanco.
            if (_label != null)
            {
                _label.color = Color.white;
                _label.alpha = 1f;
            }
            if (_button?.targetGraphic is Image buttonImage)
            {
                if (_sprite != null)
                {
                    buttonImage.sprite = _sprite;
                    buttonImage.type = Image.Type.Sliced;
                }
                var baseColor = _selected ? _selectedColor : _normal;
                buttonImage.color = baseColor;
                var colors = _button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.pressedColor = new Color(.95f, .95f, .95f, 1f);
                colors.selectedColor = Color.white;
                _button.colors = colors;
            }
            if (_toggles == null) return;
            for (var index = 0; index < _toggles.Length; ++index)
            {
                var toggle = _toggles[index];
                if (toggle?.targetGraphic is Image background)
                    background.color = index >= 2 && index - 2 < LayerColors.Length
                        ? LayerColors[index - 2]
                        : _toggleColors[index];
                if (toggle?.graphic is Graphic mark) mark.color = Color.white;
            }
            RefreshVisibilityMark(_visibilityToggle != null && !_visibilityToggle.isOn);
        }

        private void CreateVisibilityMark()
        {
            if (_visibilityToggle == null || _visibilityMark != null) return;
            _visibilityMark = new GameObject("UP Marca X de visibilidad", typeof(RectTransform));
            _visibilityMark.transform.SetParent(_visibilityToggle.transform, false);
            var root = (RectTransform)_visibilityMark.transform;
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(14f, 14f);
            CreateVisibilityMarkStroke(root, 45f);
            CreateVisibilityMarkStroke(root, -45f);
            _visibilityMark.transform.SetAsLastSibling();
            _visibilityToggle.onValueChanged.AddListener(RefreshVisibilityMarkFromToggle);
        }

        private static void CreateVisibilityMarkStroke(Transform parent, float angle)
        {
            var stroke = new GameObject("Trazo", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            stroke.transform.SetParent(parent, false);
            var rect = (RectTransform)stroke.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(3f, 15f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            var image = stroke.GetComponent<Image>();
            image.color = new Color(.03f, .20f, .31f, 1f);
            image.raycastTarget = false;
        }

        private void RefreshVisibilityMark(bool active)
        {
            if (_visibilityMark != null) _visibilityMark.SetActive(active);
        }

        private void RefreshVisibilityMarkFromToggle(bool visible) =>
            RefreshVisibilityMark(!visible);

        private void OnDestroy()
        {
            if (_visibilityToggle != null)
                _visibilityToggle.onValueChanged.RemoveListener(RefreshVisibilityMarkFromToggle);
        }

        private static float ColorDistance(Color first, Color second) =>
            Mathf.Abs(first.r - second.r) + Mathf.Abs(first.g - second.g) +
            Mathf.Abs(first.b - second.b) + Mathf.Abs(first.a - second.a);
    }

    [HarmonyPatch(typeof(PlotSelectionItem), "OnSelectHospitalPlot")]
    internal static class ScenarioEditorPlotRowSelectionPatch
    {
        private static readonly System.Reflection.FieldInfo PlotField =
            AccessTools.Field(typeof(PlotSelectionItem), "_plot");

        private static void Postfix(PlotSelectionItem __instance, HospitalPlot hospitalPlot)
        {
            if (!ScenarioEditorFlow.IsRunning || __instance == null) return;
            var skin = __instance.GetComponent<ScenarioEditorPlotRowSkin>();
            if (skin != null)
                skin.SetSelected(ReferenceEquals(PlotField?.GetValue(__instance), hospitalPlot));
        }
    }

    [HarmonyPatch(typeof(PlotSelectionItem), "Setup")]
    internal static class ScenarioEditorPlotRowSetupPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlotSelectionItem __instance)
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.RegisterPlotRow(__instance);
        }
    }

    // CATALOGO DE OBJETOS en el panel NUEVO: buscador, Ver todo,
    // ScrollRect y miniaturas nativas trasladadas, sin duplicarlas.
    // NO afecta a las miniaturas de texturas del pincel de Terreno.
    internal static class ScenarioEditorObjectDock
    {
        private static LandscapeObjectsMenu _detachedMenu;
        private static readonly FieldInfo BuildEventsLevelField =
            AccessTools.Field(typeof(BuildEvents), "_level");
        private static readonly FieldInfo CursorModeField =
            AccessTools.Field(typeof(CursorManager), "_mode");
        private static readonly FieldInfo CursorModeStackField =
            AccessTools.Field(typeof(CursorManager), "_modeStack");
        private static readonly System.Reflection.FieldInfo ContentField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_listContent");
        private static readonly System.Reflection.FieldInfo SearchField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_searchBox");
        private static readonly System.Reflection.FieldInfo CreatingIconField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_creatingListIcon");

        // El catalogo se retira del registro de menus del HUD. Desde ese momento
        // ningun menu actual o futuro puede cerrarlo: el componente nativo queda
        // activo solo como cargador y sus controles viven en el panel propio.
        internal static bool DetachFromNativeMenus(LandscapeObjectsMenu native,
            RectTransform host, HUD hud, RectTransform logicParent)
        {
            if (native == null || host == null || hud == null || logicParent == null)
                return false;
            var createdMenusField = AccessTools.Field(typeof(HUD), "_createdMenus");
            var createdMenus = createdMenusField?.GetValue(hud) as System.Collections.IList;
            var index = createdMenus == null ? -1 : createdMenus.IndexOf(native);
            if (index < 0)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Objetos: el menu nativo no consta en HUD; no se puede desacoplar.");
                return false;
            }
            var contentObject = ContentField?.GetValue(native) as GameObject;
            var scroll = contentObject?.GetComponentInParent<ScrollRect>();
            var scrollRoot = scroll?.transform as RectTransform;
            var search = SearchField?.GetValue(native) as TMP_InputField;
            if (scrollRoot == null || search == null || native.transform.parent == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Objetos: no se encuentran la lista o el buscador del prefab.");
                return false;
            }

            // Trasladar todos los elementos FUNCIONALES al panel propio:
            // ScrollRect+miniaturas, filtro, etiqueta, flecha e indicador.
            scrollRoot.SetParent(host, false);
            if (!search.transform.IsChildOf(scrollRoot))
                search.transform.SetParent(host, false);
            var label = native.transform.Find("UP_SearchLabel");
            if (label != null) label.SetParent(host, false);
            var show = native.transform.Find("UP Objects - Show All");
            if (show != null) show.SetParent(host, false);
            var creatingIcon = CreatingIconField?.GetValue(native) as Image;
            if (creatingIcon != null && creatingIcon.transform.IsChildOf(native.transform))
                creatingIcon.transform.SetParent(host, false);
            var showButton = show == null ? null : show.GetComponent<Button>();
            if (showButton != null)
            {
                showButton.onClick.RemoveAllListeners();
                showButton.onClick.AddListener(ScenarioEditorDockLayout.ToggleObjectsExpanded);
                ScenarioEditorDockLayout.StyleToolbarButton(showButton, false);
            }
            ScenarioEditorCatalogLoadGate.Initialise(native, search);
            ScenarioEditorCatalogLoadGate.ShowAll(native);

            // El cargador ya no es un menu del HUD. Se anula tambien su cierre
            // local para que Update se dedique exclusivamente al catalogo.
            var allowEscapeField = AccessTools.Field(typeof(MenuBase),
                "_allowEscapeCloseMenu2");
            if (allowEscapeField == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Objetos: no se ha encontrado la bandera de cierre por Escape.");
                return false;
            }
            allowEscapeField.SetValue(native, false);
            AccessTools.Field(typeof(MenuBase), "_closeMenuButton")?
                .SetValue(native, null);
            AccessTools.Field(typeof(MenuBase), "_isCloseButtonRegistered")?
                .SetValue(native, false);

            createdMenus.RemoveAt(index);
            native.gameObject.name = "UP Objects Logic";
            native.transform.SetParent(logicParent, false);
            var logicRect = native.transform as RectTransform;
            if (logicRect != null)
            {
                logicRect.anchorMin = logicRect.anchorMax = Vector2.zero;
                logicRect.pivot = Vector2.zero;
                logicRect.anchoredPosition = new Vector2(-10000f, -10000f);
                logicRect.sizeDelta = Vector2.zero;
            }
            foreach (var graphic in native.GetComponents<Graphic>())
                graphic.enabled = false;
            native.gameObject.SetActive(true);
            _detachedMenu = native;
            return true;
        }

        // El cursor nativo necesita actualizar el FloorPlan del catalogo, pero
        // ya no debe crear ni destruir su ventana. Estas tres entradas son el
        // unico puente entre el cursor y el cargador persistente.
        internal static LandscapeObjectsMenu ResolveForCursor(HUD hud,
            bool includeInactive)
        {
            if (ScenarioEditorFlow.IsRunning && _detachedMenu != null)
                return _detachedMenu;
            return hud == null ? null : hud.CreateMenu<LandscapeObjectsMenu>(includeInactive);
        }

        internal static LandscapeObjectsMenu FindForCursor(HUD hud,
            bool includeInactive)
        {
            if (ScenarioEditorFlow.IsRunning && _detachedMenu != null)
                return _detachedMenu;
            return hud == null ? null : hud.FindMenu<LandscapeObjectsMenu>(includeInactive);
        }

        internal static void ReleaseFromCursor(HUD hud)
        {
            if (ScenarioEditorFlow.IsRunning && _detachedMenu != null) return;
            hud?.DestroyMenu<LandscapeObjectsMenu>();
        }

        // Las miniaturas nativas envian su seleccion a BuildEvents. Si el
        // usuario tenia activa otra herramienta, primero debe existir el
        // submodo de objetos del editor; de lo contrario el juego interpreta
        // la seleccion como una compra normal y abre el Ribbon jugable.
        internal static bool PrepareEditorPlacement(BuildEvents buildEvents)
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active ||
                _detachedMenu == null) return true;
            if (buildEvents == null) return false;
            var level = BuildEventsLevelField?.GetValue(buildEvents) as Level;
            var cursor = level?.CursorManager;
            if (level == null || cursor == null) return false;

            level.GameTime.IsPausedByMenu = true;
            if (ContainsMode(cursor, typeof(CursorEditHospitalItem))) return true;
            if (!ContainsMode(cursor, typeof(CursorEditHospital)))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Objetos: se rechazo una colocacion sin el cursor principal del editor.");
                return false;
            }
            level.HospitalEditEvents.OnBeginItemPlacement?.Invoke();
            var prepared = ContainsMode(cursor, typeof(CursorEditHospitalItem));
            if (!prepared)
                UnderPressureEditorPlugin.Log?.LogError(
                    "Objetos: no se pudo activar el submodo de colocacion del editor.");
            return prepared;
        }

        // La seleccion de objetos es el estado de reposo del editor, no una
        // herramienta que el usuario tenga que volver a activar. Solo se crea
        // cuando el cursor principal vuelve a ser realmente el modo superior;
        // cualquier herramienta nativa presente o futura mantiene su propio
        // modo arriba de la pila y queda respetada automaticamente.
        internal static void EnsureDefaultSelection(Level level)
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active ||
                level == null || !ReferenceEquals(level, ScenarioEditorSession.Level) ||
                ScenarioEditorTerrainPainter.BrushEnabled || ScenarioCharacterViewer.IsOpen ||
                ScenarioFogViewer.IsOpen || ScenarioLandscape.IsActive) return;
            var cursor = level.CursorManager;
            if (cursor == null || ContainsMode(cursor, typeof(CursorEditHospitalItem))) return;
            var current = CursorModeField?.GetValue(cursor);
            if (current == null || current.GetType() != typeof(CursorEditHospital)) return;

            level.GameTime.IsPausedByMenu = true;
            level.HospitalEditEvents.OnBeginItemPlacement?.Invoke();
        }

        private static bool ContainsMode(CursorManager cursor, Type type)
        {
            if (cursor == null || type == null) return false;
            var current = CursorModeField?.GetValue(cursor);
            if (current != null && type.IsInstanceOfType(current)) return true;
            var stack = CursorModeStackField?.GetValue(cursor) as System.Collections.IEnumerable;
            if (stack == null) return false;
            foreach (var mode in stack)
                if (mode != null && type.IsInstanceOfType(mode)) return true;
            return false;
        }

        internal static void Forget(LandscapeObjectsMenu menu)
        {
            if (ReferenceEquals(_detachedMenu, menu)) _detachedMenu = null;
        }

        internal static void Configure(LandscapeObjectsMenu menu, RectTransform host,
            float width, float height)
        {
            if (menu == null || host == null) return;
            var contentObject = ContentField?.GetValue(menu) as GameObject;
            var search = SearchField?.GetValue(menu) as TMP_InputField;
            var creatingIcon = CreatingIconField?.GetValue(menu) as Image;
            if (contentObject == null) return;
            var content = contentObject.transform as RectTransform;
            var scroll = content?.GetComponentInParent<ScrollRect>();
            var scrollRect = scroll?.transform as RectTransform;
            if (content == null || scroll == null || scrollRect == null) return;

            var oldSizer = content.GetComponent<ScenarioCatalogVisualBoundsSizer>();
            if (oldSizer != null) oldSizer.enabled = false;
            var nativeLayout = content.GetComponent<DynamicLayoutGroup>();
            if (nativeLayout != null) nativeLayout.enabled = false;
            var horizontalLayout = content.GetComponent<HorizontalLayoutGroup>();
            if (horizontalLayout != null) horizontalLayout.enabled = false;
            var verticalLayout = content.GetComponent<VerticalLayoutGroup>();
            if (verticalLayout != null) verticalLayout.enabled = false;
            var searchFollower = search?.GetComponent<ScenarioEditorSearchBelowPlots>();
            if (searchFollower != null) searchFollower.enabled = false;
            var oldBar = menu.transform.Find("UP_Catalog_HorizontalScrollbar");
            if (oldBar != null) oldBar.gameObject.SetActive(false);
            var oldBackdrop = menu.transform.Find("UP_Backdrop_Extension");
            if (oldBackdrop != null) oldBackdrop.gameObject.SetActive(false);
            if (creatingIcon != null)
            {
                foreach (var graphic in creatingIcon.GetComponentsInChildren<Graphic>(true))
                    graphic.enabled = false;
            }

            var grid = content.GetComponent<GridLayoutGroup>() ??
                content.gameObject.AddComponent<GridLayoutGroup>();
            grid.enabled = true;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(0, 0, 6, 8);
            grid.spacing = new Vector2(6f, 6f);
            grid.cellSize = new Vector2(97f, 88f);
            grid.constraintCount = Mathf.Max(2,
                Mathf.FloorToInt((width - 18f + grid.spacing.x) /
                    (grid.cellSize.x + grid.spacing.x)));

            var fitter = content.GetComponent<ContentSizeFitter>() ??
                content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.enabled = true;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 38f;
            scroll.content = content;
            if (scroll.viewport == null) scroll.viewport = content.parent as RectTransform;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.pivot = new Vector2(.5f, .5f);
            scrollRect.offsetMin = new Vector2(0f, 6f);
            scrollRect.offsetMax = new Vector2(-6f, -43f);
            if (scroll.viewport != null)
            {
                scroll.viewport.anchorMin = Vector2.zero;
                scroll.viewport.anchorMax = Vector2.one;
                scroll.viewport.offsetMin = Vector2.zero;
                scroll.viewport.offsetMax = new Vector2(-12f, 0f);
                var viewportImage = scroll.viewport.GetComponent<Image>() ??
                    scroll.viewport.gameObject.AddComponent<Image>();
                if (viewportImage.sprite == null)
                    viewportImage.color = new Color(0f, 0f, 0f, .001f);
                viewportImage.raycastTarget = true;
            }
            EnsureVerticalScrollbar(host, scroll);

            if (search != null)
            {
                var searchRect = search.transform as RectTransform;
                searchRect.anchorMin = searchRect.anchorMax = new Vector2(0f, 1f);
                searchRect.pivot = new Vector2(0f, 1f);
                searchRect.anchoredPosition = new Vector2(69f, -8f);
                searchRect.sizeDelta = new Vector2(91f, 27f);
                var label = host.Find("UP_SearchLabel") as RectTransform;
                if (label != null)
                {
                    label.anchorMin = label.anchorMax = new Vector2(0f, 1f);
                    label.pivot = new Vector2(0f, 1f);
                    label.anchoredPosition = new Vector2(7f, -8f);
                    label.sizeDelta = new Vector2(60f, 27f);
                }
                var show = host.Find("UP Objects - Show All") as RectTransform;
                if (show != null)
                {
                    show.anchorMin = show.anchorMax = new Vector2(0f, 1f);
                    show.pivot = new Vector2(0f, 1f);
                    show.anchoredPosition = new Vector2(164f, -8f);
                    show.sizeDelta = new Vector2(49f, 27f);
                }
            }
        }

        private static void EnsureVerticalScrollbar(RectTransform host, ScrollRect scroll)
        {
            if (host == null || scroll == null) return;
            var obsolete = host.Find("UP_Catalog_VerticalScrollbar");
            if (obsolete != null) UnityEngine.Object.Destroy(obsolete.gameObject);
            ScenarioEditorDockLayout.InstallNativeVerticalScrollbar(host, scroll,
                "UP Objects Native Vertical Scrollbar", 43f, 6f);
        }
    }

    internal sealed class ScenarioEditorCatalogVerticalSizer : MonoBehaviour
    {
        private RectTransform _content;
        private GridLayoutGroup _grid;
        private int _lastVisible = -1;
        private bool _dirty = true;
        private string _lastFilter;
        private int _lastItemCount = -1;

        internal void MarkIfFilterChanged(LandscapeObjectsMenu menu)
        {
            if (menu == null) return;
            var filter = AccessTools.Field(typeof(LandscapeObjectsMenu), "_filter")?
                .GetValue(menu) as string ?? string.Empty;
            var items = AccessTools.Field(typeof(LandscapeObjectsMenu), "_items")?
                .GetValue(menu) as System.Collections.IList;
            var count = items?.Count ?? 0;
            if (string.Equals(filter, _lastFilter, StringComparison.Ordinal) &&
                _lastItemCount == count) return;
            _lastFilter = filter;
            _lastItemCount = count;
            _dirty = true;
        }

        internal void Configure(RectTransform content, GridLayoutGroup grid)
        {
            _content = content;
            _grid = grid;
            _lastVisible = -1;
            _dirty = true;
        }

        internal void MarkDirty() => _dirty = true;
        private void OnTransformChildrenChanged() => _dirty = true;

        private void LateUpdate()
        {
            if (!_dirty || _content == null || _grid == null) return;
            _dirty = false;
            var count = 0;
            for (int i = 0; i < _content.childCount; i++)
                if (_content.GetChild(i).gameObject.activeSelf) count++;
            if (_lastVisible == count) return;
            _lastVisible = count;
            var rows = (count + 1) / 2;
            var height = _grid.padding.top + _grid.padding.bottom +
                rows * _grid.cellSize.y + Mathf.Max(0, rows - 1) * _grid.spacing.y;
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(0f, height));
        }
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Filter")]
    internal static class ScenarioEditorCatalogHeightPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(LandscapeObjectsMenu __instance)
        {
            if (!ScenarioEditorFlow.IsRunning || __instance == null) return;
            var content = AccessTools.Field(typeof(LandscapeObjectsMenu), "_listContent")?
                .GetValue(__instance) as GameObject;
            if (content != null)
                content.GetComponent<ScenarioEditorCatalogVerticalSizer>()?
                    .MarkIfFilterChanged(__instance);
        }
    }

    // Una sola consulta cubre cualquier control presente o futuro que cuelgue
    // de la jerarquia comun del editor.
    [HarmonyPatch]
    internal static class ScenarioEditorDockPointerPatch
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(EventSystem), "IsPointerOverGameObject", Type.EmptyTypes);
        private static void Postfix(ref bool __result)
        {
            if (!__result && ScenarioEditorDockLayout.PointerOverEditorUi())
                __result = true;
        }
    }

    // El InputManager nativo usa esta propiedad tanto para clics como para
    // rueda/zoom, por lo que se bloquean los dos caminos simultaneamente.
    [HarmonyPatch(typeof(InputManager), "get_IsMouseOverGui")]
    internal static class ScenarioEditorDockNativeInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && ScenarioEditorDockLayout.PointerOverEditorUi())
                __result = true;
        }
    }

    // El metodo original de EditorMaps.cs sigue gestionando atajos/cursores,
    // pero ya no sobrescribe el azul del estilo unico en cada fotograma.
    [HarmonyPatch(typeof(ScenarioEditorStorageMenu), "SetToolButtonSelected")]
    internal static class ScenarioEditorUniformToolTintPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Button button, bool selected)
        {
            if (!ScenarioEditorFlow.IsRunning) return true;
            return !ScenarioEditorDockLayout.OverrideToolTint(button, selected);
        }
    }

    // Recuperar el sonido del boton clonado despues de RemoveAllListeners().
    [HarmonyPatch(typeof(ScenarioEditorStorageMenu), "CloneButton")]
    internal static class ScenarioEditorButtonClickSoundPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Button __result)
        {
            if (!ScenarioEditorFlow.IsRunning || __result == null) return;
            var sfx = __result.GetComponent<ButtonSFX>();
            if (sfx == null) sfx = __result.gameObject.AddComponent<ButtonSFX>();
            sfx.AudioEvent = ButtonSFX.ButtonAudioEvent.Default;
            sfx.UpdateListeners();
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorHistory), "RefreshHistoryUI")]
    internal static class ScenarioEditorHistoryButtonStylePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ScenarioEditorHistory __instance)
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.StyleHistoryButtons(__instance);
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorTerrainPainterBehaviour), "BuildUi")]
    internal static class ScenarioEditorTerrainButtonStylePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ScenarioEditorTerrainPainterBehaviour __instance)
        {
            if (ScenarioEditorFlow.IsRunning)
            {
                ScenarioEditorDockLayout.StyleTerrainButtons(__instance);
                ScenarioEditorDockLayout.ArrangeTerrainTextureTiles(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorTerrainPainterBehaviour), "RefreshTexts")]
    internal static class ScenarioEditorTerrainRefreshStylePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ScenarioEditorTerrainPainterBehaviour __instance)
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.StyleTerrainButtons(__instance);
        }
    }

    [HarmonyPatch(typeof(ScenarioCharacterViewer), "RefreshToggleVisual")]
    internal static class ScenarioEditorCharacterToggleStylePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.StyleUtilityToggle(
                    ScenarioCharacterViewer.ToggleButton, ScenarioCharacterViewer.IsOpen);
        }
    }

    [HarmonyPatch(typeof(ScenarioFogViewer), "RefreshToggleVisual")]
    internal static class ScenarioEditorFogToggleStylePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.StyleUtilityToggle(
                    ScenarioFogViewer.ToggleButton, ScenarioFogViewer.IsOpen);
        }
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Setup")]
    internal static class ScenarioEditorObjectDockPatch
    {
        // Debe ejecutarse DESPUES de la preparacion nativa del catalogo,
        // para que el prefab ya tenga buscador, lista y controles completos.
        [HarmonyPriority(-1000)]
        private static void Postfix(LandscapeObjectsMenu __instance)
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.RegisterObjectsMenu(__instance);
        }
    }

    // CursorEditHospitalItem presupone que el catalogo es una ventana del HUD.
    // Sustituimos exclusivamente esas llamadas de propiedad por el puente
    // anterior; el resto de la logica nativa del cursor permanece intacta.
    [HarmonyPatch]
    internal static class ScenarioEditorObjectsCursorOwnershipPatch
    {
        private static readonly MethodInfo HudCreateDefinition = typeof(HUD)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic)
            .Single(method => method.Name == "CreateMenu" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(bool));
        private static readonly MethodInfo HudFindDefinition = typeof(HUD)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic)
            .Single(method => method.Name == "FindMenu" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(bool));
        private static readonly MethodInfo HudDestroyDefinition = typeof(HUD)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic)
            .Single(method => method.Name == "DestroyMenu" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 0);
        private static readonly MethodInfo ResolveMethod = AccessTools.Method(
            typeof(ScenarioEditorObjectDock), nameof(ScenarioEditorObjectDock.ResolveForCursor));
        private static readonly MethodInfo FindMethod = AccessTools.Method(
            typeof(ScenarioEditorObjectDock), nameof(ScenarioEditorObjectDock.FindForCursor));
        private static readonly MethodInfo ReleaseMethod = AccessTools.Method(
            typeof(ScenarioEditorObjectDock), nameof(ScenarioEditorObjectDock.ReleaseFromCursor));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Constructor(typeof(CursorEditHospitalItem),
                new[] { typeof(CursorManager), typeof(Level), typeof(HospitalPlot),
                    typeof(HospitalPlotLayer) });
            yield return AccessTools.Method(typeof(CursorEditHospitalItem),
                "OnSelectHospitalPlot");
            yield return AccessTools.Method(typeof(CursorEditHospitalItem), "Destroy");
            yield return AccessTools.Method(typeof(CursorEditHospital),
                "RefreshLandscapeItemMenu");
            // StartItemPlacement consulta el mismo menu para decidir si debe
            // abrir el Ribbon jugable. Debe reconocer tambien el catalogo
            // desacoplado del editor.
            yield return AccessTools.Method(typeof(BuildEvents), "StartItemPlacement");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                var called = instruction.operand as MethodInfo;
                if (IsLandscapeGeneric(called, HudCreateDefinition))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = ResolveMethod;
                }
                else if (IsLandscapeGeneric(called, HudFindDefinition))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = FindMethod;
                }
                else if (IsLandscapeGeneric(called, HudDestroyDefinition))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = ReleaseMethod;
                }
                yield return instruction;
            }
        }

        private static bool IsLandscapeGeneric(MethodInfo called,
            MethodInfo definition)
        {
            if (called == null || !called.IsGenericMethod ||
                called.GetGenericMethodDefinition() != definition) return false;
            var arguments = called.GetGenericArguments();
            return arguments.Length == 1 && arguments[0] == typeof(LandscapeObjectsMenu);
        }
    }

    [HarmonyPatch(typeof(BuildEvents), "StartItemPlacement")]
    internal static class ScenarioEditorObjectPlacementModePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BuildEvents __instance) =>
            ScenarioEditorObjectDock.PrepareEditorPlacement(__instance);
    }

    // Mantiene las acciones y la geometria nativas del menu de pausa.
    // Solo aplica el azul plano que ya usan los botones del editor.
    [HarmonyPatch(typeof(PauseMenu), "Setup")]
    internal static class ScenarioEditorPauseFlatButtonPatch
    {
        // El editor configura los eventos de Guardar/Abrir/Vaciar antes del sonido.
        [HarmonyPriority(-1000)]
        private static void Postfix(PauseMenu __instance)
        {
            ScenarioEditorDockLayout.StylePauseMenu(__instance);
        }
    }

}
