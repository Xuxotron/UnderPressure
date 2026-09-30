using HarmonyLib;
using TH20;

namespace UnderPressure
{
    [HarmonyPatch(typeof(PricesMenu2Row), "Setup")]
    internal static class HideFreeDiagnosisPriceRowsPatch
    {
        private static bool Prefix(PricesMenu2Row __instance, IPriceModifier modifiable)
        {
            if (!UnderPressurePlugin.ShouldDisableDiagnosisCharges || !(modifiable is RoomDefinition))
                return true;

            // Las filas de diagnóstico son las únicas cuyo modificable es una sala.
            __instance.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(__instance.gameObject);
            return false;
        }
    }
}
