// Conecta el editor nativo con una sesión aislada que nunca utiliza las partidas normales.
using System;
using System.Reflection;
using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;
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
        private static readonly FieldInfo SandboxButtonField =
            AccessTools.Field(typeof(OpeningScreenMenu), "_sandboxButton");
        private static readonly FieldInfo NewTabButtonField =
            AccessTools.Field(typeof(SandboxMenu), "_buttonNew");
        private static readonly FieldInfo BrowseTabButtonField =
            AccessTools.Field(typeof(SandboxMenu), "_buttonBrowse");
        private static readonly FieldInfo BrowseCloudTabButtonField =
            AccessTools.Field(typeof(SandboxMenu), "_buttonBrowseCloud");
        private static readonly FieldInfo StartButtonField =
            AccessTools.Field(typeof(SandboxMenu), "_buttonStart");
        private static readonly FieldInfo SettingsField =
            AccessTools.Field(typeof(SandboxMenu), "_settings");
        private static readonly FieldInfo QuitButtonField =
            AccessTools.Field(typeof(EditHospitalMenu), "_quitButton");
        private static readonly FieldInfo ShouldAutoSaveField =
            AccessTools.Field(typeof(Level), "_shouldAutoSaveThisFrame");
        private static readonly MethodInfo OpenNewSandboxTabMethod =
            AccessTools.Method(typeof(SandboxMenu), "OnNewGameTab");
        private static readonly MethodInfo StartSandboxLevelMethod =
            AccessTools.Method(typeof(SandboxMenu), "StartLevel");

        private static FlowState _state;
        private static Level _level;

        internal static bool IsRunning => _state != FlowState.None;

        internal static void AddMainMenuButton(OpeningScreenMenu menu)
        {
            if (menu == null) return;
            var sandboxButton = SandboxButtonField?.GetValue(menu) as DynamicButton;
            var sourceLayout = sandboxButton?.transform?.parent;
            var layoutParent = sourceLayout?.parent;
            if (sandboxButton == null || sourceLayout == null || layoutParent == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se encontró la estructura nativa del botón Sandbox.");
                return;
            }

            for (var index = 0; index < layoutParent.childCount; ++index)
                if (layoutParent.GetChild(index).name == MainButtonLayoutName)
                    return;

            var layout = UnityEngine.Object.Instantiate(
                sourceLayout.gameObject, layoutParent, false);
            layout.name = MainButtonLayoutName;
            layout.transform.SetSiblingIndex(sourceLayout.GetSiblingIndex() + 1);

            var button = layout.GetComponentInChildren<DynamicButton>(true);
            if (button == null)
            {
                UnityEngine.Object.Destroy(layout);
                UnderPressureEditorPlugin.Log?.LogError(
                    "La copia del botón Sandbox no contiene un DynamicButton.");
                return;
            }

            DisableNativeLocalizers(layout);
            button.name = "ScenarioEditorButton";
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => BeginSelection(menu));
            button.interactable = true;
            button.SetTMPText(EditorLocalization.Get("scenario_editor.menu"));

            var tooltip = button.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null)
                tooltip.SetDataProvider(value =>
                    value.Text = EditorLocalization.Get("scenario_editor.tooltip"));

            if (layoutParent is RectTransform rectTransform)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        internal static void PrepareBaseSelector(SandboxMenu menu)
        {
            if (_state != FlowState.SelectingBase || menu == null) return;
            try
            {
                OpenNewSandboxTabMethod.Invoke(menu, null);

                var newTab = NewTabButtonField?.GetValue(menu) as ButtonAnimator;
                var browseTab = BrowseTabButtonField?.GetValue(menu) as ButtonAnimator;
                var browseCloudTab = BrowseCloudTabButtonField?.GetValue(menu) as ButtonAnimator;
                var startButton = StartButtonField?.GetValue(menu) as ButtonAnimator;

                SetButtonText(newTab?.Button, "scenario_editor.base_scenario");
                SetButtonText(startButton?.Button, "scenario_editor.edit");
                if (browseTab != null) browseTab.gameObject.SetActive(false);
                if (browseCloudTab != null) browseCloudTab.gameObject.SetActive(false);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo preparar el selector de escenarios base: " + exception);
            }
        }

        internal static bool StartSelectedBase(SandboxMenu menu)
        {
            if (_state != FlowState.SelectingBase) return false;
            var settings = SettingsField?.GetValue(menu) as SandboxSettings;
            if (settings?.LevelConfig == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "El selector no contiene un escenario base válido.");
                return true;
            }

            try
            {
                _state = FlowState.Loading;
                // Los dos valores evitan guardar un nivel previo y fuerzan una sesión nueva.
                StartSandboxLevelMethod.Invoke(menu, new object[] { false, true });
            }
            catch (Exception exception)
            {
                _state = FlowState.SelectingBase;
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo cargar el escenario base en el editor: " + exception);
            }

            return true;
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
            var label = quitButton.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null)
                label.text = EditorLocalization.Get("scenario_editor.return_to_menu");
        }

        internal static void ResetAtOpeningScreen()
        {
            _level = null;
            _state = FlowState.None;
        }

        internal static void CancelPendingAutosave(Level level)
        {
            if (!IsRunning || level == null) return;
            ShouldAutoSaveField?.SetValue(level, false);
        }

        private static void BeginSelection(OpeningScreenMenu menu)
        {
            if (_state != FlowState.None || menu == null) return;
            _state = FlowState.SelectingBase;
            menu.OnSandbox?.Invoke();
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

        private static void SetButtonText(DynamicButton button, string key)
        {
            if (button == null) return;
            DisableNativeLocalizers(button.gameObject);
            button.SetTMPText(EditorLocalization.Get(key));
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

    [HarmonyPatch(typeof(SandboxMenu), "Setup")]
    internal static class ScenarioEditorBaseSelectorPatch
    {
        private static void Postfix(SandboxMenu __instance) =>
            ScenarioEditorFlow.PrepareBaseSelector(__instance);
    }

    [HarmonyPatch(typeof(SandboxMenu), "OnStartGame")]
    internal static class ScenarioEditorStartBasePatch
    {
        private static bool Prefix(SandboxMenu __instance) =>
            !ScenarioEditorFlow.StartSelectedBase(__instance);
    }

    [HarmonyPatch(typeof(SandboxStateInHospital), "OnLevelLoaded")]
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

    [HarmonyPatch(typeof(GameModeSandbox), "AllowGameToBeSaved")]
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
