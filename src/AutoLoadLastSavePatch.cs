using System.Collections;
using System.Reflection;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    [HarmonyPatch(typeof(OpeningScreen), "StartFadeIn")]
    internal static class AutoContinueOpeningScreenPatch
    {
        private static readonly FieldInfo AppField = AccessTools.Field(typeof(OpeningScreen), "_app");
        private static readonly MethodInfo ContinueMethod =
            AccessTools.Method(typeof(OpeningScreen), "OnContinuePressed");
        private static bool _attempted;

        private static void Postfix(OpeningScreen __instance, ref IEnumerator __result)
        {
            if (_attempted || !UnderPressurePlugin.ShouldAutoLoadLastSave)
                return;

            __result = ContinueAfterFadeIn(__instance, __result);
        }

        private static IEnumerator ContinueAfterFadeIn(OpeningScreen openingScreen, IEnumerator fadeIn)
        {
            yield return fadeIn;

            if (_attempted || !UnderPressurePlugin.ShouldAutoLoadLastSave)
                yield break;

            _attempted = true;
            var app = AppField?.GetValue(openingScreen) as App;
            var saveSystem = app?.SaveSystem;
            if (saveSystem == null || saveSystem.MostRecentMetagameSaveSlotIndex < 0)
            {
                UnderPressurePlugin.Log.LogInfo(
                    "Autocarga omitida: no existe ninguna carrera guardada.");
                yield break;
            }

            if (ContinueMethod == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: no se encontró OpeningScreen.OnContinuePressed.");
                yield break;
            }

            UnderPressurePlugin.Log.LogInfo(
                $"Iniciando la carrera guardada en el slot {saveSystem.MostRecentMetagameSaveSlotIndex + 1}.");
            ContinueMethod.Invoke(openingScreen, null);
        }
    }

    /// <summary>
    /// Continues from the newest level save only after the native career load has
    /// completed.  Scheduling the level through MetagameStateData preserves the
    /// same state transition used by MetagameStatePlayer.LaunchHospital without
    /// depending on either campaign-map menu.
    /// </summary>
    [HarmonyPatch(typeof(GameModeCareer), "PostLoad")]
    internal static class AutoLoadLastSavePatch
    {
        private static bool _attempted;

        private static void Postfix(GameModeCareer __instance)
        {
            if (_attempted || !UnderPressurePlugin.ShouldAutoLoadLastSave)
                return;

            _attempted = true;

            var metagameMap = __instance?.MetagameMap;
            var app = metagameMap?.App;
            var saveSystem = app?.SaveSystem;
            var metagame = metagameMap?.Metagame;
            var stateMachine = metagameMap?.StateMachine;
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

            UnderPressurePlugin.Log.LogInfo(
                $"Autocarga preparada para el hospital guardado más reciente: {saveHeader.LevelID}.");
        }
    }
}
