// Updated: 2026-10-14 - Agrupacion estructural del Editor desde GitHub beb96a79.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TH20.UI;
using TH20;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioEditorFlow.cs =====
// Updated: 2026-10-13
// Abre el editor nativo sobre un nivel original sin crear ni cargar partidas Sandbox.
namespace UnderPressure.Editor
{
    // Todos los elementos visuales creados por el editor cuelgan de una sola
    // raiz. Al abandonar el editor se oculta en el mismo frame y se destruye,
    // sin esperar a que el HUD nativo termine su transicion al menu principal.
    internal static class ScenarioEditorUiHierarchy
    {
        private const string RootName = "UP Scenario Editor UI";
        private static RectTransform _root;
        private static readonly List<RaycastResult> PointerHits =
            new List<RaycastResult>();

        internal static Transform ParentFor(Canvas canvas)
        {
            if (canvas == null) return null;
            // Algunos controles se recolocan bajo un Canvas de ordenacion propio.
            // La jerarquia comun siempre pertenece al Canvas raiz del HUD; usar
            // el Canvas intermedio destruiria por error toda la UI ya creada.
            canvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            if (_root != null && _root.gameObject != null &&
                _root.GetComponentInParent<Canvas>() == canvas)
                return _root;

            Destroy();
            var rootObject = new GameObject(RootName, typeof(RectTransform));
            rootObject.transform.SetParent(canvas.transform, false);
            _root = (RectTransform)rootObject.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.pivot = new Vector2(.5f, .5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = Vector2.zero;
            _root.SetAsLastSibling();
            return _root;
        }

        internal static void Attach(GameObject value, Canvas canvas)
        {
            if (value == null || canvas == null) return;
            var parent = ParentFor(canvas);
            if (parent != null && value.transform.parent != parent)
                value.transform.SetParent(parent, false);
        }

        internal static bool IsPointerOverUi(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (_root == null || _root.gameObject == null ||
                !_root.gameObject.activeInHierarchy || eventSystem == null) return false;
            var pointer = new PointerEventData(eventSystem) { position = screenPosition };
            PointerHits.Clear();
            eventSystem.RaycastAll(pointer, PointerHits);
            var result = false;
            for (var index = 0; index < PointerHits.Count; ++index)
            {
                var hit = PointerHits[index].gameObject;
                if (hit == null || !hit.transform.IsChildOf(_root)) continue;
                result = true;
                break;
            }
            PointerHits.Clear();
            return result;
        }

        internal static void Hide()
        {
            if (_root != null && _root.gameObject != null)
                _root.gameObject.SetActive(false);
        }

        internal static void Destroy()
        {
            if (_root == null || _root.gameObject == null)
            {
                _root = null;
                return;
            }
            _root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }
    }

    internal static class ScenarioEditorFlow
    {
        private enum FlowState
        {
            None,
            SelectingBase,
            Loading,
            Editing,
            Exiting
        }

        private const string MainButtonLayoutName = "Under Pressure - Scenario Editor Layout";
        private const string SelectorRootName = "Under Pressure - Scenario Level Selector";
        private static readonly FieldInfo SettingsButtonField =
            AccessTools.Field(typeof(OpeningScreenMenu), "_settingsButton");
        private static readonly FieldInfo OpeningScreenAppField =
            AccessTools.Field(typeof(OpeningScreen), "_app");
        private static readonly FieldInfo OpeningScreenOperationField =
            AccessTools.Field(typeof(OpeningScreen), "_currentOperationCoroutine");
        private static readonly FieldInfo OpeningScreenFadeOutField =
            AccessTools.Field(typeof(OpeningScreen), "_directorFadeOut");
        private static readonly FieldInfo OpeningScreenRootCanvasField =
            AccessTools.Field(typeof(OpeningScreen), "_rootCanvas");
        private static readonly FieldInfo OpeningScreenSceneContentField =
            AccessTools.Field(typeof(OpeningScreen), "_sceneContent");
        private static readonly FieldInfo QuitButtonField =
            AccessTools.Field(typeof(EditHospitalMenu), "_quitButton");
        private static readonly FieldInfo ShouldAutoSaveField =
            AccessTools.Field(typeof(Level), "_shouldAutoSaveThisFrame");
        private static FlowState _state;
        private static OpeningScreen _openingScreen;
        private static GameObject _selectorRoot;
        private static LevelConfig _selectedLevel;
        private static Level _level;

        internal static bool IsRunning => _state != FlowState.None;

        internal static void AddMainMenuButton(OpeningScreenMenu menu)
        {
            if (menu == null) return;
            var sourceButton = SettingsButtonField?.GetValue(menu) as DynamicButton;
            var sourceLayout = sourceButton?.transform?.parent;
            var layoutParent = sourceLayout?.parent;
            if (sourceButton == null || sourceLayout == null || layoutParent == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se encontró la estructura nativa de los botones del menú principal.");
                return;
            }

            for (var index = 0; index < layoutParent.childCount; ++index)
                if (layoutParent.GetChild(index).name == MainButtonLayoutName)
                    return;

            var layout = UnityEngine.Object.Instantiate(
                sourceLayout.gameObject, layoutParent, false);
            layout.name = MainButtonLayoutName;
            layout.transform.SetSiblingIndex(sourceLayout.GetSiblingIndex() + 1);
            layout.SetActive(true);

            var button = layout.GetComponentInChildren<DynamicButton>(true);
            if (button == null)
            {
                UnityEngine.Object.Destroy(layout);
                UnderPressureEditorPlugin.Log?.LogError(
                    "La copia visual del botón principal no contiene un DynamicButton.");
                return;
            }

            ConfigureButton(button, EditorLocalization.Get("scenario_editor.menu"),
                () => BeginSelection(menu, sourceLayout.gameObject));

            var tooltip = button.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null)
                tooltip.SetDataProvider(value =>
                    value.Text = EditorLocalization.Get("scenario_editor.tooltip"));

            if (layoutParent is RectTransform rectTransform)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

            UnderPressureEditorPlugin.Log?.LogInfo(
                "Botón del editor de escenarios creado y visible en el menú principal.");
        }

        internal static void QueueSelectedLevel(MetagameStateBase state)
        {
            if (_state != FlowState.Loading || _selectedLevel == null || state?.Owner == null)
                return;

            var data = state.Owner.GetStateMachineData<MetagameStateData>();
            if (data == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se encontró el estado de carga de la metapartida aislada.");
                return;
            }

            data.LoadLevel = _selectedLevel;
            data.OnLoadRestartLevel = false;
            data.OnLoadSaveOldLevel = false;
            data.CheckForCutscenes = false;
            data.CheckForPostCutscene = false;
            data.CheckForSuperBugMessages = false;
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Nivel original enviado directamente al cargador: " +
                _selectedLevel.GetLocalisedDisplayName() + ".");
        }

        internal static void OpenNativeEditor(Level level)
        {
            if (_state != FlowState.Loading || level == null) return;
            CleanupEditorUi();
            _level = level;
            _state = FlowState.Editing;
            try
            {
                level.HospitalEditEvents.OnStart?.Invoke();
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Editor nativo iniciado sobre " +
                    level.Config.GetLocalisedDisplayName() + ".");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "El editor nativo no pudo iniciarse: " + exception);
                _state = FlowState.Exiting;
                CleanupEditorUi();
                level.App.QuitToMenuDontSave();
            }
        }

        internal static void ReplaceNativeExit(EditHospitalMenu menu, Level level)
        {
            if (_state != FlowState.Editing || menu == null || level == null) return;
            var quitButton = QuitButtonField?.GetValue(menu) as Button;
            if (quitButton == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se encontró el botón de salida del editor nativo.");
                return;
            }

            DisableNativeLocalizers(quitButton.gameObject);
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(ExitToMainMenu);
            var label = quitButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = EditorLocalization.Get("scenario_editor.return_to_menu");
        }

        internal static void ResetAtOpeningScreen()
        {
            FinishEditorSession();
            DestroySelector();
            _openingScreen = null;
            _selectedLevel = null;
            _level = null;
            _state = FlowState.None;
        }

        internal static void CancelPendingAutosave(Level level)
        {
            if (!IsRunning || level == null) return;
            ShouldAutoSaveField?.SetValue(level, false);
        }

        private static void BeginSelection(OpeningScreenMenu menu, GameObject rowTemplate)
        {
            if (_state != FlowState.None || menu == null || rowTemplate == null) return;
            _openingScreen = UnityEngine.Object.FindObjectOfType<OpeningScreen>();
            var app = OpeningScreenAppField?.GetValue(_openingScreen) as App;
            var options = app?.SandboxSettingsConfig?.LevelOptions;
            if (_openingScreen == null || app == null || options == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se encontró la tabla original de hospitales disponibles.");
                return;
            }

            // Se recorren tambien los niveles de DLC deshabilitados para conocer
            // la procedencia de sus objetos antes de construir el catalogo.
            ScenarioEditorObjects.BuildDlcProvenance(options);

            var levels = new List<LevelConfig>();
            foreach (var option in options)
            {
                var level = option?.Level?.Instance;
                if (level == null || !DLCUtils.IsDLCInstalled(level.GetRequiredDlcPack()))
                    continue;
                if (!levels.Contains(level)) levels.Add(level);
            }

            if (levels.Count == 0)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "La tabla original no contiene hospitales instalados.");
                return;
            }

            _state = FlowState.SelectingBase;
            CreateSelector(menu, rowTemplate, levels);
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Selector propio creado con " + levels.Count + " hospitales originales.");
        }

        private static void CreateSelector(
            OpeningScreenMenu menu, GameObject rowTemplate, IReadOnlyList<LevelConfig> levels)
        {
            DestroySelector();
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null)
                throw new InvalidOperationException("No se encontró el Canvas del menú principal.");

            // Un único selector de hospitales y mapas, sin un segundo popup superpuesto.
            _selectorRoot = ScenarioEditorMapPicker.CreateSelector(
                canvas.transform, rowTemplate, levels,
                (level, mapPath) => StartSelectedLevel(level),
                CancelSelection);
        }

        private static void StartSelectedLevel(LevelConfig level)
        {
            if (_state != FlowState.SelectingBase || level == null || _openingScreen == null)
                return;

            try
            {
                _selectedLevel = level;
                _state = FlowState.Loading;
                DestroySelector();

                // Se evita StartGame porque entra en RestartAsync y borra los niveles del slot.
                // LoadAsync crea la metapartida temporal sin leer ni eliminar partidas existentes.
                var operation = _openingScreen.StartCoroutine(StartEditorSession(_openingScreen));
                OpeningScreenOperationField?.SetValue(_openingScreen, operation);
            }
            catch (Exception exception)
            {
                _selectedLevel = null;
                _state = FlowState.None;
                ScenarioEditorMapPicker.ClearPendingMap();
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo cargar el hospital original en el editor: " + exception);
            }
        }

        private static IEnumerator StartEditorSession(OpeningScreen openingScreen)
        {
            var app = OpeningScreenAppField?.GetValue(openingScreen) as App;
            var fadeOut = OpeningScreenFadeOutField?.GetValue(openingScreen) as PlayableDirector;
            var rootCanvas = OpeningScreenRootCanvasField?.GetValue(openingScreen) as Canvas;
            var sceneContent = OpeningScreenSceneContentField?.GetValue(openingScreen) as Transform;
            if (app == null || fadeOut == null || rootCanvas == null || sceneContent == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se encontró la transición nativa necesaria para abrir el hospital.");
                _selectedLevel = null;
                _state = FlowState.None;
                OpeningScreenOperationField?.SetValue(openingScreen, null);
                yield break;
            }

            fadeOut.Play();
            while (fadeOut.time < fadeOut.duration)
                yield return null;

            TooltipManager.Instance.PopGUIRoot(rootCanvas.transform);
            sceneContent.gameObject.SetActive(false);

            app.SetGameMode<GameModeCareer>();
            yield return app.GameMode.LoadAsync(true, 0);
            OpeningScreenOperationField?.SetValue(openingScreen, null);
        }

        private static void CancelSelection()
        {
            if (_state != FlowState.SelectingBase) return;
            DestroySelector();
            _openingScreen = null;
            _selectedLevel = null;
            _state = FlowState.None;
            ScenarioEditorMapPicker.ClearPendingMap();
        }

        private static void ExitToMainMenu()
        {
            if (_state != FlowState.Editing || _level == null) return;
            var level = _level;
            FinishEditorSession();
            level.App.QuitToMenuDontSave();
        }

        internal static void ExitToWorldMap()
        {
            if (_state != FlowState.Editing || _level?.App?.MetagameMap == null) return;
            var map = _level.App.MetagameMap;
            FinishEditorSession();
            map.Open();
        }

        internal static void FinishEditorSession()
        {
            if (_state != FlowState.Editing && _state != FlowState.Exiting &&
                !ScenarioEditorSession.Active)
            {
                CleanupEditorUi();
                return;
            }

            var level = _level ?? ScenarioEditorSession.Level;
            _state = FlowState.Exiting;
            CleanupEditorUi();
            try
            {
                level?.HospitalEditEvents?.OnEnd?.Invoke();
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "El editor nativo produjo un error al cerrarse: " + exception.Message);
            }
            finally
            {
                if (ScenarioEditorSession.Active) ScenarioEditorSession.Restore();
            }
            ScenarioEditorMapPicker.ClearPendingMap();
            _openingScreen = null;
            _selectedLevel = null;
            _level = null;
            _state = FlowState.None;
        }

        private static void CleanupEditorUi()
        {
            ScenarioEditorUiHierarchy.Hide();
            ScenarioEditorStorageMenu.Shutdown();
            ScenarioEditorDockLayout.Shutdown();
            ScenarioCharacterViewer.Detach();
            ScenarioFogViewer.Detach();
            ScenarioEditorTerrainPainter.Cleanup();
            ScenarioEditorHistory.StopHistory();
            ScenarioEditorUiHierarchy.Destroy();
        }

        private static void ConfigureButton(DynamicButton button, string text, Action action)
        {
            if (button == null) return;
            DisableNativeLocalizers(button.gameObject);
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => action?.Invoke());
            button.interactable = true;
            button.SetTMPText(text);
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value;
        }

        private static void Stretch(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void DestroySelector()
        {
            if (_selectorRoot == null) return;
            UnityEngine.Object.Destroy(_selectorRoot);
            _selectorRoot = null;
        }

        private static void DisableNativeLocalizers(GameObject root)
        {
            if (root == null) return;
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().FullName == "I2.Loc.Localize")
                    behaviour.enabled = false;
        }
    }

    [HarmonyPatch(typeof(OpeningScreenMenu), "Start")]
    internal static class ScenarioEditorMainMenuPatch
    {
        private static void Postfix(OpeningScreenMenu __instance) =>
            ScenarioEditorFlow.AddMainMenuButton(__instance);
    }

    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioEditorOpeningScreenPatch
    {
        private static void Prefix() => ScenarioEditorFlow.ResetAtOpeningScreen();
    }

    // Las salidas del menu de pausa no pasan por el boton inferior del editor.
    // Cerramos la sesion antes de que empiece cualquier transicion nativa para
    // que su UI no sobreviva durante la carga ni sobre el mapamundi.
    [HarmonyPatch(typeof(MetagameMap), "Open")]
    internal static class ScenarioEditorWorldMapExitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorFlow.FinishEditorSession();
        }
    }

    [HarmonyPatch(typeof(App), "QuitToMenu")]
    internal static class ScenarioEditorQuitToMenuExitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorFlow.FinishEditorSession();
        }
    }

    [HarmonyPatch(typeof(App), "QuitToMenuDontSave")]
    internal static class ScenarioEditorQuitToMenuDontSaveExitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorFlow.FinishEditorSession();
        }
    }

    [HarmonyPatch(typeof(MetagameStateBase), "Enter")]
    internal static class ScenarioEditorLevelQueuePatch
    {
        private static void Postfix(MetagameStateBase __instance) =>
            ScenarioEditorFlow.QueueSelectedLevel(__instance);
    }

    [HarmonyPatch(typeof(Metagame), "OnLevelLoaded")]
    internal static class ScenarioEditorLevelLoadedPatch
    {
        private static void Postfix(Level __0) => ScenarioEditorFlow.OpenNativeEditor(__0);
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioEditorExitButtonPatch
    {
        private static void Postfix(EditHospitalMenu __instance, Level __0) =>
            ScenarioEditorFlow.ReplaceNativeExit(__instance, __0);
    }

    [HarmonyPatch(typeof(GameMode), "AllowGameToBeSaved")]
    internal static class ScenarioEditorSavePermissionPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ScenarioEditorFlow.IsRunning)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Level), "HandleAutosave")]
    internal static class ScenarioEditorAutosavePatch
    {
        private static bool Prefix(Level __instance)
        {
            if (!ScenarioEditorFlow.IsRunning) return true;
            ScenarioEditorFlow.CancelPendingAutosave(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(App), "AutoSaveDeferred")]
    internal static class ScenarioEditorDeferredAutosavePatch
    {
        private static bool Prefix() => !ScenarioEditorFlow.IsRunning;
    }

    [HarmonyPatch(typeof(App), "QuickSaveInstantly")]
    internal static class ScenarioEditorImmediateSavePatch
    {
        private static bool Prefix() => !ScenarioEditorFlow.IsRunning;
    }
}
// ===== END ORIGINAL: ScenarioEditorFlow.cs =====

// ===== BEGIN ORIGINAL: ScenarioEditorMapPicker.cs =====
// Updated: 2026-10-13
// Selector unico y compacto de hospitales/mapas.
// Las filas visibles son botones NATIVOS de TH20 situados manualmente; sin Mask,
// ScrollRect, VerticalLayoutGroup ni ContentSizeFitter que puedan ocultarlas.
// Cada columna tiene un Scrollbar de Unity y admite rueda del raton.
namespace UnderPressure.Editor
{
    internal static class ScenarioEditorMapPicker
    {
        private sealed class MapChoice
        {
            internal string Path;
            internal string Name;
            internal LevelConfig Level;
        }

        private sealed class HospitalChoice
        {
            internal LevelConfig Level;
            internal string Label;
        }

        private sealed class RowEntry
        {
            internal string Label;
            internal Action OnClick;
            internal bool Green;
            internal bool Selected;
        }

        private sealed class ScrollColumn
        {
            internal RectTransform Viewport;
            internal Scrollbar Bar;
            internal float Width;
            internal float RowInset;
            internal readonly List<RowEntry> Entries = new List<RowEntry>();
            internal readonly List<GameObject> VisibleRows = new List<GameObject>();
            internal int FirstRow;
            internal bool ChangingBar;
        }

        private const int VisibleRows = 7;
        private const float RowHeight = 52f;
        private const float RowSpacing = 5f;
        private const float ColumnHeight = 422f;
        private const float RowInset = 6f;
        private const float PanelWidth = 785f;
        private const float PanelHeight = 560f;
        private const float LeftColumnWidth = 380f;
        private const float RightColumnWidth = 345f;
        private const float RightColumnX = 425f;

        private static GameObject _root;
        private static DynamicButton _nativeButtonTemplate;
        private static TMP_Text _nativeTextTemplate;
        private static Texture2D _buttonTexture;
        private static Sprite _buttonSprite;
        private static Action<LevelConfig, string> _onOpen;
        private static Action _onCancel;
        private static LevelConfig _selectedLevel;
        private static ScrollColumn _hospitalsColumn;
        private static ScrollColumn _mapsColumn;
        private static TMP_Text _selectedHospitalTitle;
        private static InputField _architectInput;
        private static string _currentArchitect;
        private static GameObject _architectDropdown;
        private static ScrollColumn _architectColumn;
        private static readonly List<HospitalChoice> _hospitalChoices = new List<HospitalChoice>();
        private static string _pendingMap;
        private static string _pendingArchitect;
        private static bool _pendingNewMap;

        internal static GameObject CreateSelector(
            Transform canvas, GameObject rowTemplate, IReadOnlyList<LevelConfig> levels,
            Action<LevelConfig, string> openMap, Action cancel)
        {
            if (canvas == null || rowTemplate == null || levels == null || levels.Count == 0)
                throw new ArgumentException("El selector necesita un Canvas, un boton y hospitales.");

            Reset();
            _nativeButtonTemplate = rowTemplate.GetComponentInChildren<DynamicButton>(true);
            if (_nativeButtonTemplate == null)
                throw new InvalidOperationException("No se encontro el DynamicButton nativo.");
            _nativeTextTemplate = _nativeButtonTemplate.GetComponentInChildren<TMP_Text>(true);
            if (_nativeTextTemplate == null)
                throw new InvalidOperationException("No se encontro el texto nativo del boton.");
            _buttonSprite = ScenarioEditorDockLayout.CreateEditorButtonSprite(out _buttonTexture);
            _onOpen = openMap;
            _onCancel = cancel;

            _root = Ui("UP_ScenarioEditorSelector", canvas, typeof(Image));
            Stretch((RectTransform)_root.transform);
            var overlay = _root.GetComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.58f);
            overlay.raycastTarget = true;
            _root.transform.SetAsLastSibling();
            var closeHandler = _root.AddComponent<ScenarioEditorSelectorCloseHandler>();
            closeHandler.Close = () =>
            {
                ClearPendingMap();
                _onCancel?.Invoke();
            };

            var panel = CreateNativeSettingsPanel(_root.transform);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            CreateHeader(panel.transform, EditorLocalization.Get("scenario_editor.base_scenario"),
                new Vector2(20f, -15f), new Vector2(LeftColumnWidth, 44f));
            _selectedHospitalTitle = CreateHeader(panel.transform,
                EditorLocalization.Get("scenario_editor.hospital"),
                new Vector2(RightColumnX, -15f), new Vector2(RightColumnWidth, 44f));

            _hospitalsColumn = CreateColumn(panel.transform,
                EditorLocalization.Get("scenario_editor.hospitals"),
                new Vector2(20f, -68f), new Vector2(LeftColumnWidth, ColumnHeight));
            _mapsColumn = CreateColumn(panel.transform,
                EditorLocalization.Get("scenario_editor.maps"),
                new Vector2(RightColumnX, -68f), new Vector2(RightColumnWidth, ColumnHeight),
                RowInset + 5f);

            // Este mismo tipo de boton se ve correctamente FUERA del area de listas.
            CreateNativeRow(panel.transform, EditorLocalization.Get("scenario_editor.return_to_menu"),
                new Vector2(20f, -497f), new Vector2(290f, 50f), () =>
                {
                    ClearPendingMap();
                    _onCancel?.Invoke();
                }, false);
            CreateHeader(panel.transform, EditorLocalization.Get("scenario_editor.architect_name"),
                new Vector2(340f, -497f), new Vector2(145f, 50f));
            _architectInput = CreateArchitectInput(panel.transform,
                new Vector2(490f, -501f), new Vector2(210f, 42f));
            CreateNativeRow(panel.transform, "▼", new Vector2(708f, -501f),
                new Vector2(42f, 42f), () => ToggleArchitectDropdown(panel.transform), false, false);

            foreach (var level in levels.Where(l => l != null).Distinct()
                                        .OrderBy(l => l.GetLocalisedDisplayName(),
                                                 StringComparer.CurrentCultureIgnoreCase))
            {
                var captured = level;
                var label = captured.GetLocalisedDisplayName();
                _hospitalChoices.Add(new HospitalChoice { Level = captured, Label = label });
                _hospitalsColumn.Entries.Add(new RowEntry
                {
                    Label = label,
                    OnClick = () => SelectHospital(captured)
                });
            }

            RefreshRows(_hospitalsColumn);
            ShowArchitectMaps(UnderPressureEditorPlugin.ArchitectName);
            Canvas.ForceUpdateCanvases();
            return _root;
        }

        private static GameObject CreateNativeSettingsPanel(Transform parent)
        {
            var videoTabField = AccessTools.Field(typeof(PreferencesScreen), "_videoTab");
            var nativeScreen = Resources.FindObjectsOfTypeAll<PreferencesScreen>()
                .FirstOrDefault(candidate => candidate != null &&
                    videoTabField?.GetValue(candidate) is Transform);
            var nativeVideoTab = nativeScreen == null
                ? null : videoTabField.GetValue(nativeScreen) as Transform;
            var nativePanel = nativeVideoTab?.parent as RectTransform;
            if (nativePanel == null)
                throw new InvalidOperationException(
                    "No se encontro el panel raiz nativo de PreferencesScreen.");

            // PreferencesScreen ya contiene el marco gris, borde y sombra nativos.
            // Se conserva su jerarquia completa y sus componentes de disposicion,
            // igual que en Ajustes del escenario; solo se ocultan sus controles.
            var screen = UnityEngine.Object.Instantiate(nativeScreen.gameObject, parent, false);
            screen.name = "UP Native Scenario Selector Screen";
            var screenRect = screen.transform as RectTransform;
            if (screenRect != null)
            {
                Stretch(screenRect);
                screenRect.localScale = Vector3.one;
            }
            foreach (var nestedCanvas in screen.GetComponentsInChildren<Canvas>(true))
                nestedCanvas.overrideSorting = false;
            var clonedScreen = screen.GetComponent<PreferencesScreen>();
            if (clonedScreen == null)
                throw new InvalidOperationException(
                    "No se pudo clonar PreferencesScreen para el selector.");
            clonedScreen.enabled = false;
            var panelTransform = FindCloneTransform(nativeScreen.transform, screen.transform,
                nativePanel);
            if (panelTransform == null)
                throw new InvalidOperationException(
                    "No se encontro el panel clonado de PreferencesScreen.");
            var panel = panelTransform.gameObject;
            panel.name = "UP Native Scenario Selector Panel";

            var hiddenTransforms = new[]
            {
                "_videoTabContents", "_audioTabContents", "_controlsTabContents",
                "_gameTabContents", "_languageTabContents",
                "_videoTabFolderBackground", "_audioTabFolderBackground",
                "_controlsTabFolderBackground", "_gameTabFolderBackground",
                "_languageTabFolderBackground",
                "_videoTabButton", "_audioTabButton", "_controlsTabButton",
                "_gameTabButton", "_languageTabButton", "_creditsButton", "_closeButton"
            };
            foreach (var fieldName in hiddenTransforms)
            {
                var value = AccessTools.Field(typeof(PreferencesScreen), fieldName)?.GetValue(nativeScreen);
                var source = value as Transform ?? (value as Component)?.transform;
                var clone = FindCloneTransform(nativeScreen.transform, screen.transform, source);
                if (clone != null) clone.gameObject.SetActive(false);
            }
            foreach (var behaviour in screen.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var typeName = behaviour.GetType().FullName;
                if (typeName == "UnderPressure.ModSettingsTab") behaviour.enabled = false;
                if (typeName != null && typeName.StartsWith("I2.Loc.", StringComparison.Ordinal))
                    behaviour.enabled = false;
                if (typeName == "TH20.SetTextToVersionNumber")
                    behaviour.gameObject.SetActive(false);
            }
            foreach (var child in screen.GetComponentsInChildren<Transform>(true))
                if (child != null && (child.name == "UnderPressureTabButton" ||
                                      child.name == "UnderPressureMasterToggle"))
                    child.gameObject.SetActive(false);
            var title = screen.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text != null && text.GetComponentInParent<Selectable>() == null)
                .OrderByDescending(text => text.fontSize)
                .FirstOrDefault();
            if (title != null) title.gameObject.SetActive(false);
            foreach (var graphic in screen.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            var panelImage = panel.GetComponent<Image>();
            if (panelImage != null) panelImage.raycastTarget = true;
            screen.SetActive(true);
            return panel;
        }

        private static Transform FindCloneTransform(Transform sourceRoot, Transform cloneRoot,
            Transform source)
        {
            if (sourceRoot == null || cloneRoot == null || source == null ||
                !source.IsChildOf(sourceRoot)) return null;
            var names = new Stack<string>();
            for (var current = source; current != null && current != sourceRoot;
                 current = current.parent)
                names.Push(current.name);
            var clone = cloneRoot;
            while (names.Count > 0)
            {
                clone = clone.Find(names.Pop());
                if (clone == null) return null;
            }
            return clone;
        }

        private static void SelectHospital(LevelConfig level)
        {
            if (level == null || _root == null) return;
            _selectedLevel = level;
            if (_selectedHospitalTitle != null)
                _selectedHospitalTitle.text = level.GetLocalisedDisplayName();

            // Solo cambia el texto de las filas visibles; no se recrea la columna izquierda.
            RefreshRows(_hospitalsColumn);

            _mapsColumn.Entries.Clear();
            _mapsColumn.FirstRow = 0;
            _mapsColumn.Entries.Add(new RowEntry
            {
                Label = EditorLocalization.Get("scenario_editor.new_map"),
                Green = true,
                OnClick = () => OpenSelected(null)
            });

            foreach (var map in ReadMaps(_currentArchitect, level.UniqueId))
            {
                var captured = map;
                _mapsColumn.Entries.Add(new RowEntry
                {
                    Label = map.Name,
                    OnClick = () => OpenMap(captured)
                });
            }
            RefreshRows(_mapsColumn);
        }

        private static void ShowArchitectMaps(string architect)
        {
            if (_root == null || _mapsColumn == null) return;
            _currentArchitect = string.IsNullOrWhiteSpace(architect)
                ? UnderPressureEditorPlugin.ArchitectName : architect.Trim();
            UnderPressureEditorPlugin.ArchitectNameSetting.Value = _currentArchitect;
            _selectedLevel = null;
            if (_selectedHospitalTitle != null)
                _selectedHospitalTitle.text = _currentArchitect;
            RefreshRows(_hospitalsColumn);

            _mapsColumn.Entries.Clear();
            _mapsColumn.FirstRow = 0;
            foreach (var map in ReadMaps(_currentArchitect, null))
            {
                var captured = map;
                _mapsColumn.Entries.Add(new RowEntry
                {
                    Label = captured.Name,
                    OnClick = () => OpenMap(captured)
                });
            }
            RefreshRows(_mapsColumn);
        }

        private static void OpenMap(MapChoice map)
        {
            if (map == null || map.Level == null)
            {
                if (_selectedHospitalTitle != null)
                    _selectedHospitalTitle.text = EditorLocalization.Get("scenario_editor.invalid_map");
                return;
            }
            _selectedLevel = map.Level;
            OpenSelected(map.Path);
        }

        private static void OpenSelected(string mapPath)
        {
            if (_selectedLevel == null || _onOpen == null) return;
            // Comprobar el mapa antes de abandonar la pantalla inicial. No
            // comenzar a cargar un escenario cuyo JSON/PNG no puede abrirse.
            if (!string.IsNullOrEmpty(mapPath))
            {
                try { ScenarioMapFormat.Read(mapPath); }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "No se puede abrir el mapa seleccionado: " + ex);
                    if (_selectedHospitalTitle != null)
                        _selectedHospitalTitle.text =
                            EditorLocalization.Get("scenario_editor.invalid_map");
                    return;
                }
            }
            _pendingNewMap = string.IsNullOrEmpty(mapPath);
            _pendingArchitect = _pendingNewMap
                ? UnderPressureEditorPlugin.ArchitectName : null;
            _pendingMap = mapPath;
            _onOpen(_selectedLevel, mapPath);
        }

        internal static void OnEditorOpened(Level level)
        {
            var mapPath = _pendingMap;
            if (_pendingNewMap)
            {
                if (level == null || !ScenarioEditorSession.Active) return;
                ScenarioEditorStorage.BeginNewMap(level, _pendingArchitect);
                _pendingNewMap = false;
                _pendingArchitect = null;
                return;
            }
            if (string.IsNullOrEmpty(mapPath)) return;
            // No consumir el mapa cuando el evento de apertura aun no termino
            // de inicializar el editor nativo. Si se pierde este valor se abre
            // silenciosamente el hospital original en lugar del mapa solicitado.
            if (level == null || !ScenarioEditorSession.Active)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Mapa pendiente; el editor aun no esta activo: " + mapPath);
                return;
            }
            _pendingMap = null;
            try
            {
                ScenarioEditorStorage.Load(level, mapPath);
                UnderPressureEditorPlugin.Log?.LogInfo("Editor: mapa cargado: " + mapPath);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudo cargar el mapa: " + exception);
            }
        }

        internal static void Reset()
        {
            CloseArchitectDropdown();
            _root = null;
            _nativeButtonTemplate = null;
            _nativeTextTemplate = null;
            if (_buttonSprite != null) UnityEngine.Object.Destroy(_buttonSprite);
            if (_buttonTexture != null) UnityEngine.Object.Destroy(_buttonTexture);
            _buttonSprite = null;
            _buttonTexture = null;
            _onOpen = null;
            _onCancel = null;
            _selectedLevel = null;
            _hospitalsColumn = null;
            _mapsColumn = null;
            _selectedHospitalTitle = null;
            _architectInput = null;
            _currentArchitect = null;
            // La seleccion puede sobrevivir al cierre/recreacion del Canvas
            // mientras se carga el nivel. Solo se borra al aplicarla o cancelar.
            _hospitalChoices.Clear();
        }

        internal static void ClearPendingMap()
        {
            _pendingMap = null;
            _pendingArchitect = null;
            _pendingNewMap = false;
        }

        private static List<MapChoice> ReadMaps(string architect, string hospitalId)
        {
            var result = new List<MapChoice>();
            var folder = ScenarioEditorStorage.FolderForArchitect(architect);
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder,
                         ScenarioMapPackage.SearchPattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var header = ScenarioMapFormat.ReadHeader(file);
                    if (!string.IsNullOrEmpty(hospitalId) &&
                        !string.Equals(header.baseHospitalId, hospitalId, StringComparison.Ordinal))
                        continue;
                    var hospital = _hospitalChoices.FirstOrDefault(choice =>
                        string.Equals(choice.Level.UniqueId, header.baseHospitalId,
                            StringComparison.Ordinal));
                    if (hospital == null) continue;
                    result.Add(new MapChoice
                    {
                        Path = file,
                        Name = string.IsNullOrWhiteSpace(header.name)
                            ? Path.GetFileNameWithoutExtension(file) : header.name,
                        Level = hospital.Level
                    });
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Mapa omitido: " + file + " - " + exception.Message);
                }
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static ScrollColumn CreateColumn(Transform parent, string name, Vector2 pos,
            Vector2 size, float rowInset = RowInset)
        {
            var host = Ui(name, parent, typeof(Image));
            var hostRect = (RectTransform)host.transform;
            hostRect.anchorMin = hostRect.anchorMax = new Vector2(0f, 1f);
            hostRect.pivot = new Vector2(0f, 1f);
            hostRect.anchoredPosition = pos;
            hostRect.sizeDelta = size;
            var background = host.GetComponent<Image>();
            background.color = new Color32(82, 82, 82, 255);
            background.raycastTarget = true;

            // NUNCA hay elementos fuera de pantalla: se crean solo las 7 filas visibles.
            // Por eso no necesitamos Mask / RectMask2D ni ContentSizeFitter.
            var viewport = Ui("VisibleButtons", host.transform, typeof(Image));
            Stretch((RectTransform)viewport.transform);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;

            var column = new ScrollColumn
            {
                Viewport = (RectTransform)viewport.transform,
                Width = size.x,
                RowInset = rowInset
            };

            var wheel = host.AddComponent<ScenarioEditorWheelHandler>();
            wheel.Scroll = delta =>
            {
                var maxFirst = Math.Max(0, column.Entries.Count - VisibleRows);
                if (maxFirst == 0 || Mathf.Approximately(delta, 0f)) return;
                column.FirstRow = Mathf.Clamp(column.FirstRow + (delta > 0f ? -2 : 2), 0, maxFirst);
                RefreshRows(column);
            };

            column.Bar = CreateScrollbar(host.transform);
            column.Bar.onValueChanged.AddListener(value =>
            {
                if (column.ChangingBar) return;
                var maxFirst = Math.Max(0, column.Entries.Count - VisibleRows);
                var row = Mathf.Clamp(Mathf.RoundToInt((1f - value) * maxFirst), 0, maxFirst);
                if (row == column.FirstRow) return;
                column.FirstRow = row;
                RefreshRows(column);
            });
            return column;
        }

        private static void RefreshRows(ScrollColumn column)
        {
            if (column == null || column.Viewport == null) return;
            foreach (var existing in column.VisibleRows)
            {
                if (existing == null) continue;
                existing.SetActive(false); // Evita solapamientos durante el Destroy diferido.
                UnityEngine.Object.Destroy(existing);
            }
            column.VisibleRows.Clear();

            var maxFirst = Math.Max(0, column.Entries.Count - VisibleRows);
            column.FirstRow = Mathf.Clamp(column.FirstRow, 0, maxFirst);
            var hasScroll = maxFirst > 0;
            column.Bar.gameObject.SetActive(hasScroll);
            column.ChangingBar = true;
            column.Bar.numberOfSteps = Math.Max(1, maxFirst + 1);
            column.Bar.size = Mathf.Clamp01((float)VisibleRows / Math.Max(1, column.Entries.Count));
            column.Bar.value = maxFirst == 0 ? 1f : 1f - (float)column.FirstRow / maxFirst;
            column.ChangingBar = false;

            var width = column.Width - (hasScroll ? 30f : 13f) -
                        (column.RowInset - RowInset);
            for (var row = 0; row < VisibleRows && row + column.FirstRow < column.Entries.Count; ++row)
            {
                var entry = column.Entries[row + column.FirstRow];
                var selected = entry.Selected;
                if (ReferenceEquals(column, _hospitalsColumn))
                    selected = _hospitalChoices.Any(
                        x => x.Label == entry.Label && x.Level == _selectedLevel);
                var buttonRow = CreateNativeRow(column.Viewport, entry.Label,
                    new Vector2(column.RowInset,
                        -(RowInset + row * (RowHeight + RowSpacing))),
                    new Vector2(width, RowHeight), entry.OnClick, entry.Green, selected);
                column.VisibleRows.Add(buttonRow);
            }
        }

        // Todos los botones, tanto el visible inferior como los de las listas,
        // usan IDENTICA creacion y posicion absoluta (ninguno depende de LayoutGroup).
        private static GameObject CreateNativeRow(
            Transform parent, string label, Vector2 position, Vector2 size, Action action,
            bool green, bool selected = false)
        {
            var row = Ui("UP_" + label, parent);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var button = UnityEngine.Object.Instantiate(_nativeButtonTemplate, row.transform, false);
            button.gameObject.name = "NativeButton";
            button.gameObject.SetActive(true);
            button.enabled = true;
            var buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchorMin = Vector2.zero;
                buttonRect.anchorMax = Vector2.one;
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.offsetMin = Vector2.zero;
                buttonRect.offsetMax = Vector2.zero;
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.localScale = Vector3.one;
            }

            DisableNativeLocalizers(button.gameObject);
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => action?.Invoke());
            button.interactable = true;
            button.SetTMPText(label);

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.gameObject.SetActive(true);
                text.enabled = true;
                text.enableAutoSizing = true;
                text.fontSizeMin = 13f;
                text.fontSizeMax = 23f;
                text.fontSize = Mathf.Min(text.fontSize, 23f);
                text.raycastTarget = false;
            }

            if (green)
            {
                var baseColor = new Color(0.22f, 0.96f, 0.22f, 1f);
                ScenarioEditorDockLayout.ApplyEditorButtonVisuals(button, _buttonSprite,
                    baseColor);
            }
            else if (selected)
            {
                ScenarioEditorDockLayout.ApplyEditorButtonVisuals(button, _buttonSprite,
                    ScenarioEditorDockLayout.EditorSelectedButtonColor);
            }
            else
                ScenarioEditorDockLayout.ApplyEditorButtonVisuals(button, _buttonSprite,
                    ScenarioEditorDockLayout.EditorButtonColor);
            return row;
        }

        private static TMP_Text CreateHeader(Transform parent, string label, Vector2 pos, Vector2 size)
        {
            var header = Ui("Header_" + label, parent);
            var rect = (RectTransform)header.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var textObject = UnityEngine.Object.Instantiate(_nativeTextTemplate.gameObject, header.transform, false);
            textObject.name = "Title";
            textObject.SetActive(true);
            DisableNativeLocalizers(textObject);
            var text = textObject.GetComponent<TMP_Text>();
            if (text == null) return null;
            Stretch(text.rectTransform);
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = 28f;
            text.raycastTarget = false;
            return text;
        }

        private static InputField CreateArchitectInput(
            Transform parent, Vector2 position, Vector2 size)
        {
            var root = Ui("UP_ArchitectName", parent, typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var background = root.GetComponent<Image>();
            background.color = new Color(0.86f, 0.89f, 0.92f, 1f);

            var textObject = Ui("Text", root.transform, typeof(Text));
            var textRect = (RectTransform)textObject.transform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(10f, 1f);
            textRect.offsetMax = new Vector2(-10f, -1f);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 20;
            text.color = new Color(0.07f, 0.10f, 0.12f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.resizeTextForBestFit = false;

            var input = root.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = text;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 64;
            input.text = UnderPressureEditorPlugin.ArchitectName;
            input.onValueChanged.AddListener(value =>
            {
                var name = (value ?? "").Trim();
                if (name.Length > 0)
                {
                    UnderPressureEditorPlugin.ArchitectNameSetting.Value = name;
                    ShowArchitectMaps(name);
                }
            });
            return input;
        }

        private static void ToggleArchitectDropdown(Transform panel)
        {
            if (_architectDropdown != null)
            {
                CloseArchitectDropdown();
                return;
            }
            var root = ScenarioEditorStorage.Folder;
            var names = Directory.Exists(root)
                ? Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name) &&
                                   !name.StartsWith("_", StringComparison.Ordinal))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
                : new List<string>();

            _architectDropdown = Ui("UP_ArchitectDropdown", panel, typeof(Image));
            var rect = (RectTransform)_architectDropdown.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(PanelWidth + 10f, -68f);
            rect.sizeDelta = new Vector2(260f, 422f);
            _architectDropdown.GetComponent<Image>().color =
                new Color(0.12f, 0.15f, 0.18f, 1f);
            _architectDropdown.transform.SetAsLastSibling();
            _architectColumn = CreateColumn(_architectDropdown.transform,
                "Architects", Vector2.zero, new Vector2(260f, 422f));
            foreach (var architect in names)
            {
                var selected = architect;
                _architectColumn.Entries.Add(new RowEntry
                {
                    Label = selected,
                    Selected = string.Equals(selected, _currentArchitect,
                        StringComparison.CurrentCultureIgnoreCase),
                    OnClick = () =>
                    {
                        if (_architectInput != null)
                            _architectInput.text = selected;
                        UnderPressureEditorPlugin.ArchitectNameSetting.Value = selected;
                        ShowArchitectMaps(selected);
                        CloseArchitectDropdown();
                    }
                });
            }
            if (names.Count == 0)
                _architectColumn.Entries.Add(new RowEntry
                {
                    Label = EditorLocalization.Get("scenario_editor.no_architects"),
                    OnClick = CloseArchitectDropdown
                });
            RefreshRows(_architectColumn);
        }

        private static void CloseArchitectDropdown()
        {
            if (_architectDropdown != null) UnityEngine.Object.Destroy(_architectDropdown);
            _architectDropdown = null;
            _architectColumn = null;
        }

        private static Scrollbar CreateScrollbar(Transform parent)
        {
            var track = Ui("Scrollbar", parent, typeof(Image), typeof(Scrollbar));
            var rect = (RectTransform)track.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-14f, 4f);
            rect.offsetMax = new Vector2(-3f, -4f);
            track.GetComponent<Image>().color = new Color(0.03f, 0.08f, 0.11f, 0.65f);

            var sliding = Ui("Sliding Area", track.transform);
            Stretch((RectTransform)sliding.transform);
            var handle = Ui("Handle", sliding.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            Stretch(handleRect);
            var handleImage = handle.GetComponent<Image>();
            var sourceImage = _nativeButtonTemplate.GetComponent<Image>();
            if (sourceImage != null)
            {
                handleImage.sprite = sourceImage.sprite;
                handleImage.type = sourceImage.type;
            }
            handleImage.color = new Color(0.08f, 0.77f, 0.93f, 1f);
            var scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.value = 1f;
            return scrollbar;
        }

        private static void DisableNativeLocalizers(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
        }

        private static GameObject Ui(string name, Transform parent, params Type[] components)
        {
            var types = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
            types.AddRange(components);
            var result = new GameObject(name, types.ToArray());
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    // Maneja la rueda del raton sin depender del ScrollRect o de mascaras UI.
    public sealed class ScenarioEditorWheelHandler : MonoBehaviour, IScrollHandler
    {
        internal Action<float> Scroll;

        public void OnScroll(PointerEventData eventData)
        {
            if (Scroll != null) Scroll(eventData.scrollDelta.y);
        }
    }

    // Replica el cierre por Escape usado por los menus nativos.
    public sealed class ScenarioEditorSelectorCloseHandler : MonoBehaviour
    {
        internal Action Close;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close?.Invoke();
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "OpenNativeEditor")]
    internal static class ScenarioEditorMapPickerOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Level level) => ScenarioEditorMapPicker.OnEditorOpened(level);
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "ResetAtOpeningScreen")]
    internal static class ScenarioEditorMapPickerResetPatch
    {
        private static void Prefix()
        {
            // Nunca perder la ruta mientras sigue en curso la carga del Editor.
            // Reset solo limpia el Canvas. La ruta se consume en OnEditorOpened.
            // El flujo puede transicionar por OpeningScreen antes de abrir Level:
            // NO borrar aqui _pendingMap, aunque IsRunning pase a false.
            ScenarioEditorMapPicker.Reset();
        }
    }
}
// ===== END ORIGINAL: ScenarioEditorMapPicker.cs =====

// ===== BEGIN ORIGINAL: ScenarioEditorSound.cs =====
// Updated: 2026-10-14 - Silenciar exclusivamente efectos de compra/cambio de parcela, sin mutear el editor.
namespace UnderPressure.Editor
{
    // No usar AudioListener.volume: eso silenció todo el juego mientras se editaba.
    // El sonido no deseado se dispara al comprar/abrir/cerrar parcelas. Se
    // bloquea sólo su reproducción, sin alterar ni el volumen ni las preferencias.
    internal static class ScenarioEditorSound
    {
        private static int _plotActionDepth;
        internal static bool DuringPlotAction => _plotActionDepth > 0 &&
            ScenarioEditorFlow.IsRunning;

        internal static void BeginPlotAction()
        {
            if (ScenarioEditorFlow.IsRunning)
                ++_plotActionDepth;
        }

        internal static void EndPlotAction()
        {
            if (_plotActionDepth > 0) --_plotActionDepth;
        }

        internal static bool IsPlotPurchaseSound(object source, object[] parameters)
        {
            if (!ScenarioEditorFlow.IsRunning) return false;
            if (DuringPlotAction) return true;
            // Hay variantes en las que el efecto se lanza después de la compra,
            // y el nombre del clip permite bloquearlo sin tocar otros sonidos.
            var clip = parameters != null && parameters.Length > 0 &&
                parameters[0] is UnityEngine.Object ? parameters[0] : null;
            if (clip == null && source != null)
            {
                try
                {
                    var property = AccessTools.Property(source.GetType(), "clip");
                    clip = property?.GetValue(source, null);
                }
                catch { return false; }
            }
            var name = (clip as UnityEngine.Object)?.name;
            if (string.IsNullOrEmpty(name)) return false;
            var text = name.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            return ((text.Contains("plot") || text.Contains("land") || text.Contains("tile")) &&
                (text.Contains("buy") || text.Contains("purchase") ||
                 text.Contains("unlock") || text.Contains("activate")));
        }
    }

    // Delimitar solamente las acciones que generan el sonido de compra de parcela.
    // TargetMethods evita errores si un método cambia entre versiones del juego.
    [HarmonyPatch]
    internal static class ScenarioEditorPlotSoundScopePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[] { "BuyAndBuildImmediately", "Buy", "Open", "Close" };
            foreach (var name in names)
            {
                var method = AccessTools.Method(typeof(HospitalPlot), name);
                if (method != null) yield return method;
            }
        }

        private static void Prefix(out bool __state)
        {
            __state = ScenarioEditorFlow.IsRunning;
            if (__state) ScenarioEditorSound.BeginPlotAction();
        }

        private static void Postfix(bool __state)
        {
            if (__state) ScenarioEditorSound.EndPlotAction();
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && __exception != null) ScenarioEditorSound.EndPlotAction();
            return __exception;
        }
    }

    // El sonido puede emitirse mediante AudioSource.Play o PlayOneShot.
    // Solo se interceptan esas llamadas; todos los demás sonidos siguen activos.
    [HarmonyPatch]
    internal static class ScenarioEditorPlotSoundPlaybackPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("UnityEngine.AudioSource") ??
                Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
            if (type == null) yield break;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly))
            {
                if (method.Name != "Play" && method.Name != "PlayOneShot" &&
                    method.Name != "PlayDelayed" && method.Name != "PlayScheduled") continue;
                try
                {
                    if (method.GetMethodBody() == null) continue;
                }
                catch { continue; }
                yield return method;
            }
        }

        private static bool Prefix(object __instance, object[] __args) =>
            !ScenarioEditorSound.IsPlotPurchaseSound(__instance, __args);
    }
}
// ===== END ORIGINAL: ScenarioEditorSound.cs =====
