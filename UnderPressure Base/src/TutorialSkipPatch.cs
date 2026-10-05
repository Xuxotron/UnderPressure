// Purpose: Skips tutorial-mode behaviour when tutorial suppression is enabled.
using BehaviorDesigner.Runtime.Tasks;
using HarmonyLib;

namespace UnderPressure
{
    [HarmonyPatch(typeof(TH20.BTA.Level.StartTutorialMode), "OnUpdate")]
    internal static class TutorialSkipPatch
    {
        private static bool Prefix(TH20.BTA.Level.StartTutorialMode __instance, ref TaskStatus __result)
        {
            if (!FirstLevelTutorial.ShouldSkip(__instance)) return true;
            __result = TaskStatus.Success;
            return false;
        }
    }
}
