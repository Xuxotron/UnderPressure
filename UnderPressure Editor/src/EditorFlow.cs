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
            _state = FlowState.Exiting;
            try
            {
                level.HospitalEditEvents.OnEnd?.Invoke();
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "El editor nativo produjo un error al cerrarse: " + exception.Message);
            }

            level.App.QuitToMenuDontSave();
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
        }

        private sealed class ScrollColumn
        {
            internal RectTransform Viewport;
            internal Scrollbar Bar;
            internal float Width;
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

        private static GameObject _root;
        private static DynamicButton _nativeButtonTemplate;
        private static TMP_Text _nativeTextTemplate;
        private static Action<LevelConfig, string> _onOpen;
        private static Action _onCancel;
        private static LevelConfig _selectedLevel;
        private static ScrollColumn _hospitalsColumn;
        private static ScrollColumn _mapsColumn;
        private static TMP_Text _selectedHospitalTitle;
        private static TMP_InputField _architectInput;
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
            _onOpen = openMap;
            _onCancel = cancel;

            _root = Ui("UP_ScenarioEditorSelector", canvas, typeof(Image));
            Stretch((RectTransform)_root.transform);
            var overlay = _root.GetComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.58f);
            overlay.raycastTarget = true;
            _root.transform.SetAsLastSibling();

            var panel = Ui("Panel", _root.transform, typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.69f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(864f, 595f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.175f, 0.200f, 0.225f, 0.97f);
            panelImage.raycastTarget = true;

            CreateHeader(panel.transform, EditorLocalization.Get("scenario_editor.base_scenario"),
                new Vector2(23f, -15f), new Vector2(418f, 44f));
            _selectedHospitalTitle = CreateHeader(panel.transform,
                EditorLocalization.Get("scenario_editor.hospital"),
                new Vector2(466f, -15f), new Vector2(375f, 44f));

            _hospitalsColumn = CreateColumn(panel.transform,
                EditorLocalization.Get("scenario_editor.hospitals"),
                new Vector2(22f, -68f), new Vector2(421f, ColumnHeight));
            _mapsColumn = CreateColumn(panel.transform,
                EditorLocalization.Get("scenario_editor.maps"),
                new Vector2(464f, -68f), new Vector2(379f, ColumnHeight));

            // Este mismo tipo de boton se ve correctamente FUERA del area de listas.
            CreateNativeRow(panel.transform, EditorLocalization.Get("scenario_editor.return_to_menu"),
                new Vector2(23f, -511f), new Vector2(418f, 56f), () =>
                {
                    ClearPendingMap();
                    _onCancel?.Invoke();
                }, false);
            CreateHeader(panel.transform, EditorLocalization.Get("scenario_editor.architect_name"),
                new Vector2(466f, -511f), new Vector2(190f, 56f));
            _architectInput = CreateArchitectInput(panel.transform,
                new Vector2(654f, -511f), new Vector2(187f, 56f));

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
            if (_hospitalChoices.Count > 0)
                SelectHospital(_hospitalChoices[0].Level);
            Canvas.ForceUpdateCanvases();
            return _root;
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

            foreach (var map in ReadMaps(level.UniqueId))
            {
                var path = map.Path;
                _mapsColumn.Entries.Add(new RowEntry
                {
                    Label = map.Name,
                    OnClick = () => OpenSelected(path)
                });
            }
            RefreshRows(_mapsColumn);
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
            _root = null;
            _nativeButtonTemplate = null;
            _nativeTextTemplate = null;
            _onOpen = null;
            _onCancel = null;
            _selectedLevel = null;
            _hospitalsColumn = null;
            _mapsColumn = null;
            _selectedHospitalTitle = null;
            _architectInput = null;
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

        private static List<MapChoice> ReadMaps(string hospitalId)
        {
            var result = new List<MapChoice>();
            var folder = ScenarioEditorStorage.Folder;
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder, "*.upmap", SearchOption.AllDirectories))
            {
                try
                {
                    var header = ScenarioMapFormat.ReadHeader(file);
                    if (!string.Equals(header.baseHospitalId, hospitalId, StringComparison.Ordinal)) continue;
                    result.Add(new MapChoice
                    {
                        Path = file,
                        Name = string.IsNullOrWhiteSpace(header.name)
                            ? Path.GetFileNameWithoutExtension(file) : header.name
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

        private static ScrollColumn CreateColumn(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var host = Ui(name, parent, typeof(Image));
            var hostRect = (RectTransform)host.transform;
            hostRect.anchorMin = hostRect.anchorMax = new Vector2(0f, 1f);
            hostRect.pivot = new Vector2(0f, 1f);
            hostRect.anchoredPosition = pos;
            hostRect.sizeDelta = size;
            var background = host.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.06f);
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
                Width = size.x
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

            var width = column.Width - (hasScroll ? 30f : 13f);
            for (var row = 0; row < VisibleRows && row + column.FirstRow < column.Entries.Count; ++row)
            {
                var entry = column.Entries[row + column.FirstRow];
                var label = entry.Label;
                if (ReferenceEquals(column, _hospitalsColumn))
                {
                    var selected = _hospitalChoices.FirstOrDefault(
                        x => x.Label == entry.Label && x.Level == _selectedLevel);
                    if (selected != null) label = "› " + label;
                }
                var buttonRow = CreateNativeRow(column.Viewport, label,
                    new Vector2(RowInset, -(RowInset + row * (RowHeight + RowSpacing))),
                    new Vector2(width, RowHeight), entry.OnClick, entry.Green);
                column.VisibleRows.Add(buttonRow);
            }
        }

        // Todos los botones, tanto el visible inferior como los de las listas,
        // usan IDENTICA creacion y posicion absoluta (ninguno depende de LayoutGroup).
        private static GameObject CreateNativeRow(
            Transform parent, string label, Vector2 position, Vector2 size, Action action, bool green)
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
                var colors = button.colors;
                colors.normalColor = new Color(0.22f, 0.96f, 0.22f, 1f);
                colors.highlightedColor = new Color(0.42f, 1f, 0.34f, 1f);
                colors.pressedColor = new Color(0.13f, 0.75f, 0.16f, 1f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
                if (button.targetGraphic != null)
                    button.targetGraphic.color = colors.normalColor;
            }
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

        private static TMP_InputField CreateArchitectInput(
            Transform parent, Vector2 position, Vector2 size)
        {
            var root = Ui("UP_ArchitectName", parent, typeof(Image), typeof(TMP_InputField));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0.94f, 0.97f, 0.98f, 1f);

            var viewport = Ui("Text Area", root.transform);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(12f, 5f);
            viewportRect.offsetMax = new Vector2(-12f, -5f);

            var textObject = UnityEngine.Object.Instantiate(
                _nativeTextTemplate.gameObject, viewport.transform, false);
            textObject.name = "Text";
            textObject.SetActive(true);
            DisableNativeLocalizers(textObject);
            var text = textObject.GetComponent<TMP_Text>();
            Stretch(text.rectTransform);
            text.color = new Color(0.07f, 0.10f, 0.12f, 1f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableAutoSizing = true;
            text.fontSizeMin = 13f;
            text.fontSizeMax = 22f;

            var input = root.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 64;
            input.text = UnderPressureEditorPlugin.ArchitectName;
            input.onEndEdit.AddListener(value =>
            {
                var name = (value ?? "").Trim();
                if (name.Length == 0) name = UnderPressureEditorPlugin.ArchitectName;
                UnderPressureEditorPlugin.ArchitectNameSetting.Value = name;
                input.SetTextWithoutNotify(name);
            });
            return input;
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
