// Purpose: Groups startup screen skipping and automatic save loading.
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderPressure
{
    [HarmonyPatch(typeof(App), "LoadAndShowOpeningScreen")]
    internal static class SkipFrontEndForAutoLoadPatch
    {
        private static readonly FieldInfo MostRecentLevelSavesField =
            AccessTools.Field(typeof(SaveSystem), "_mMostRecentSave");
        private static readonly FieldInfo MostRecentMetagameSlotField =
            AccessTools.Field(typeof(SaveSystem), "_mostRecentMetagameSaveSlotIndex");
        private static readonly FieldInfo RoomTemplatesInitialisedField =
            AccessTools.Field(typeof(RoomTemplatesManager), "_initialised");

        internal static bool KeepLoadingScreenVisible { get; private set; }
        internal static SaveFileHeader SelectedLevelSave { get; private set; }
        internal static int SelectedSaveSlot { get; private set; } = -1;

        private static void Postfix(App __instance, ref IEnumerator __result)
        {
            if (!UnderPressurePlugin.ShouldAutoLoadLastSave || __instance?.SaveSystem == null)
                return;

            if (!TrySelectMostRecentHospital(__instance.SaveSystem))
            {
                UnderPressurePlugin.Log.LogInfo(
                    "Autocarga omitida: ninguna campaña contiene hospitales guardados.");
                return;
            }

            var originalMetagameSlot = __instance.SaveSystem.MostRecentMetagameSaveSlotIndex;
            MostRecentMetagameSlotField.SetValue(__instance.SaveSystem, SelectedSaveSlot);
            __result = RunWithNativeFrontEndSkip(__instance, originalMetagameSlot, __result);
        }

        private static bool TrySelectMostRecentHospital(SaveSystem saveSystem)
        {
            SelectedLevelSave = null;
            SelectedSaveSlot = -1;

            var savesBySlot = MostRecentLevelSavesField?.GetValue(saveSystem) as SaveFileHeader[];
            if (savesBySlot == null)
                return false;

            for (var slot = 0; slot < savesBySlot.Length; ++slot)
            {
                var candidate = savesBySlot[slot];
                if (candidate == null || candidate.IsBroken ||
                    saveSystem.GetMetagameSaveHeaderForSlot(slot) == null)
                    continue;

                if (SelectedLevelSave != null && candidate.Date <= SelectedLevelSave.Date)
                    continue;

                SelectedLevelSave = candidate;
                SelectedSaveSlot = slot;
            }

            return SelectedLevelSave != null;
        }

        private static IEnumerator RunWithNativeFrontEndSkip(
            App app, int originalMetagameSlot, IEnumerator original)
        {
            var saveSystem = app.SaveSystem;
            var previousValue = DebugVars.SkipFrontEnd.Value;
            InitialiseRoomTemplatesForSkippedFrontEnd(app);
            DebugVars.SkipFrontEnd.Value = true;
            KeepLoadingScreenVisible = true;
            UnderPressurePlugin.Log.LogInfo(
                $"Hospital más reciente global: slot {SelectedSaveSlot + 1}, " +
                $"nivel {SelectedLevelSave.LevelID}, fecha {SelectedLevelSave.Date:O}.");
            try
            {
                yield return original;
            }
            finally
            {
                DebugVars.SkipFrontEnd.Value = previousValue;
                MostRecentMetagameSlotField.SetValue(saveSystem, originalMetagameSlot);
            }
        }

        private static void InitialiseRoomTemplatesForSkippedFrontEnd(App app)
        {
            try
            {
                var manager = app?.RoomTemplatesManager;
                if (manager == null || app.SaveSystem == null) return;
                if (RoomTemplatesInitialisedField?.GetValue(manager) is bool initialised && initialised) return;

                // This is the exact pair normally executed by
                // OpeningScreen.ShowNormalContent. SkipFrontEnd bypasses that method, so
                // perform it before Career begins loading rather than racing the level.
                app.SaveSystem.LoadRoomTemplatesSaveData(manager);
                manager.RestoreFromSave(app);
                var loaded = 0;
                foreach (var group in manager.RoomTemplates.Values) loaded += group.Count;
                UnderPressurePlugin.Log.LogInfo(
                    "Plantillas de salas restauradas antes de la autocarga: " + loaded + ".");
            }
            catch (System.Exception exception)
            {
                UnderPressurePlugin.Log.LogError(
                    "No se pudieron restaurar las plantillas antes de la autocarga: " + exception);
            }
        }

        internal static void ReleaseLoadingScreen(App app, bool hide)
        {
            KeepLoadingScreenVisible = false;
            if (hide)
                app?.LoadSaveProgressScreen?.Hide();
        }
    }

    [HarmonyPatch(typeof(LoadSaveProgressScreen), "Hide")]
    internal static class KeepLoadingScreenVisiblePatch
    {
        private static bool Prefix()
        {
            return !SkipFrontEndForAutoLoadPatch.KeepLoadingScreenVisible;
        }
    }

    /// <summary>
    /// Continues from the newest level save when the native campaign map opens.
    /// At that point the career is fully restored, but its fade-in has not begun.
    /// Scheduling the level through MetagameStateData preserves the same state
    /// transition used by MetagameStatePlayer.LaunchHospital without displaying
    /// or depending on either campaign-map menu.
    /// </summary>
    [HarmonyPatch(typeof(MetagameMap), "Open")]
    internal static class AutoLoadLastSavePatch
    {
        private static readonly FieldInfo ReadyToStartField =
            AccessTools.Field(typeof(MetagameMap), "<IsReadyToStart>k__BackingField");
        private static bool _attempted;

        private static bool Prefix(MetagameMap __instance)
        {
            if (_attempted || !UnderPressurePlugin.ShouldAutoLoadLastSave)
                return true;

            _attempted = true;

            var app = __instance?.App;
            var saveSystem = app?.SaveSystem;
            var metagame = __instance?.Metagame;
            var stateMachine = __instance?.StateMachine;
            if (app == null || saveSystem == null || metagame == null || stateMachine == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: la carrera terminó de cargar sin todos los estados necesarios.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            var saveHeader = SkipFrontEndForAutoLoadPatch.SelectedLevelSave;
            if (saveHeader == null || string.IsNullOrEmpty(saveHeader.LevelID))
            {
                UnderPressurePlugin.Log.LogInfo(
                    "Autocarga omitida: la ranura de carrera no contiene hospitales guardados.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            var levelConfig = metagame.LevelList?.GetLevelConfigByID(saveHeader.LevelID);
            if (levelConfig == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"Autocarga cancelada: no existe LevelConfig para el nivel guardado '{saveHeader.LevelID}'.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            if (app.ShowMessageBoxIfSaveHeaderCantLoad(saveHeader, levelConfig))
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"Autocarga cancelada: la cabecera del nivel '{saveHeader.LevelID}' no se puede cargar.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            var stateData = stateMachine.GetStateMachineData<MetagameStateData>();
            if (stateData == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: no está disponible MetagameStateData.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            stateData.LoadLevel = levelConfig;
            stateData.OnLoadRestartLevel = false;
            stateData.OnLoadSaveOldLevel = true;
            app.LoadSaveProgressScreen.Show(levelConfig);
            __instance.RootTransform.gameObject.SetActive(false);
            app.MetagameMapScene.RootObject.SetActive(false);
            ReadyToStartField.SetValue(__instance, true);
            __instance.StateMachine.Update();
            SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, false);

            UnderPressurePlugin.Log.LogInfo(
                $"Carga directa preparada para {saveHeader.LevelID}; se cancela la apertura del mapa de campaña.");
            return false;
        }
    }
    // Purpose: Skips the game's introductory splash screens and opens the main interface directly.
    [HarmonyPatch(typeof(SplashScreen), "StartIntro")]
    internal static class SkipIntroScreensPatch
    {
        private static bool Prefix(SplashScreen __instance, ref IEnumerator __result)
        {
            if (!UnderPressurePlugin.ShouldSkipIntroScreens)
                return true;

            __result = LoadMainDirectly(__instance);
            return false;
        }

        private static IEnumerator LoadMainDirectly(SplashScreen screen)
        {
            SetActive(screen, "_segaIdentVideo", false);
            SetActive(screen, "_twoPointSplash", false);
            SetActive(screen, "_legalSplash", false);
            SetActive(screen, "_masterFadeObject", false);
            SetActive(screen, "_splashFadeObject", false);
            SetActive(screen, "_loadingIcon", true);

            UnityEngine.Debug.Log("[UnderPressure] Pantallas iniciales omitidas. Cargando escena Main.");
            yield return null;

            var operation = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Additive);
            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f)
                yield return null;

            operation.allowSceneActivation = true;
            yield return operation;

            SetActive(screen, "_camera", false);
            SetActive(screen, "_loadingIcon", false);
            SceneManager.UnloadSceneAsync("SplashScreen");
        }

        private static void SetActive(SplashScreen screen, string fieldName, bool active)
        {
            var value = AccessTools.Field(typeof(SplashScreen), fieldName)?.GetValue(screen);
            if (value is Component component)
                component.gameObject.SetActive(active);
            else if (value is GameObject gameObject)
                gameObject.SetActive(active);
        }
    }
}
