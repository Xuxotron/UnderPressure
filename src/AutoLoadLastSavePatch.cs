using System.Collections;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    [HarmonyPatch(typeof(App), "LoadAndShowOpeningScreen")]
    internal static class SkipFrontEndForAutoLoadPatch
    {
        private static void Postfix(App __instance, ref IEnumerator __result)
        {
            if (!UnderPressurePlugin.ShouldAutoLoadLastSave ||
                __instance?.SaveSystem == null ||
                __instance.SaveSystem.MostRecentMetagameSaveSlotIndex < 0)
                return;

            __result = RunWithNativeFrontEndSkip(__result);
        }

        private static IEnumerator RunWithNativeFrontEndSkip(IEnumerator original)
        {
            var previousValue = DebugVars.SkipFrontEnd.Value;
            DebugVars.SkipFrontEnd.Value = true;
            UnderPressurePlugin.Log.LogInfo(
                "Omitiendo visualmente la pantalla inicial mediante el flujo nativo SkipFrontEnd.");
            try
            {
                yield return original;
            }
            finally
            {
                DebugVars.SkipFrontEnd.Value = previousValue;
            }
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
        private static bool _attempted;

        private static void Prefix(MetagameMap __instance)
        {
            if (_attempted || !UnderPressurePlugin.ShouldAutoLoadLastSave)
                return;

            _attempted = true;

            var app = __instance?.App;
            var saveSystem = app?.SaveSystem;
            var metagame = __instance?.Metagame;
            var stateMachine = __instance?.StateMachine;
            if (app == null || saveSystem == null || metagame == null || stateMachine == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: la carrera terminó de cargar sin todos los estados necesarios.");
                return;
            }

            var saveHeader = saveSystem.MostRecentSave;
            if (saveHeader == null || string.IsNullOrEmpty(saveHeader.LevelID))
            {
                UnderPressurePlugin.Log.LogInfo(
                    "Autocarga omitida: la ranura de carrera no contiene hospitales guardados.");
                return;
            }

            var levelConfig = metagame.LevelList?.GetLevelConfigByID(saveHeader.LevelID);
            if (levelConfig == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"Autocarga cancelada: no existe LevelConfig para el nivel guardado '{saveHeader.LevelID}'.");
                return;
            }

            if (app.ShowMessageBoxIfSaveHeaderCantLoad(saveHeader, levelConfig))
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"Autocarga cancelada: la cabecera del nivel '{saveHeader.LevelID}' no se puede cargar.");
                return;
            }

            var stateData = stateMachine.GetStateMachineData<MetagameStateData>();
            if (stateData == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: no está disponible MetagameStateData.");
                return;
            }

            stateData.LoadLevel = levelConfig;
            stateData.OnLoadRestartLevel = false;
            stateData.OnLoadSaveOldLevel = true;
            app.LoadSaveProgressScreen.Show(levelConfig);

            UnderPressurePlugin.Log.LogInfo(
                $"Carga directa preparada para el hospital guardado más reciente: {saveHeader.LevelID}.");
        }
    }
}
