// Purpose: Skips the game's introductory splash screens and opens the main interface directly.
using System.Collections;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderPressure
{
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
