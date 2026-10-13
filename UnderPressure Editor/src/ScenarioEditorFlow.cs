// Updated: 2026-10-13
// Abre el editor nativo sobre un nivel original sin crear ni cargar partidas Sandbox.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

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
