using System;
using HarmonyLib;
using TH20;

namespace UnderPressure.PowerGrid
{
    /// <summary>
    /// Excepción dinámica del cuadro: su desgaste continuo depende de las celdas
    /// de baja tensión que la topología eléctrica haya asignado a esa instancia.
    /// </summary>
    [HarmonyPatch(typeof(RoomItem), "GetAttributeModifierOverTime")]
    internal static class ElectricalPanelMaintenancePatch
    {
        private static void Postfix(RoomItem __instance, string __0, ref float __result)
        {
            if (!EnergyRoomItems.IsPanel(__instance) ||
                !string.Equals(__0, ObjectAttributes.Type.Maintenance.ToString(),
                    StringComparison.OrdinalIgnoreCase)) return;

            var connectedTiles = PowerGridPrototype.ConnectedLowVoltageTiles(__instance);
            __result = connectedTiles * ElectricalPanelParameters.WearPerConnectedTile;
        }
    }
}
