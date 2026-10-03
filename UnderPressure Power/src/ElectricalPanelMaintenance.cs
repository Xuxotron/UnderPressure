using System;
using System.Reflection;
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
            __result = connectedTiles * ElectricalPanelSpecialParameters.WearPerConnectedTile;
        }
    }

    /// <summary>
    /// Los cuadros guardados antes de incorporar Maintenance conservan _attributes = null.
    /// RoomItem.RestoreFromSave no reconstruye ese campo desde la definición actual, por lo
    /// que se aplica la misma inicialización privada que usa el constructor de un objeto nuevo.
    /// </summary>
    [HarmonyPatch(typeof(RoomItem), "RestoreFromSave")]
    internal static class ElectricalPanelMaintenanceSaveMigrationPatch
    {
        private static readonly MethodInfo CreateAttributesMethod =
            AccessTools.Method(typeof(RoomItem), "CreateAttributes");
        private static readonly MethodInfo SetupMaintenanceCallbacksMethod =
            AccessTools.Method(typeof(RoomItem), "SetupMaintenanceCallbacks");

        private static void Postfix(RoomItem __instance)
        {
            if (!EnergyRoomItems.IsPanel(__instance) || __instance.MaintenanceLevel != null) return;
            if (CreateAttributesMethod == null || SetupMaintenanceCallbacksMethod == null)
            {
                PowerGridPlugin.Log.LogError(
                    "No se pudo migrar el mantenimiento de un cuadro electrico guardado.");
                return;
            }

            CreateAttributesMethod.Invoke(__instance, null);
            SetupMaintenanceCallbacksMethod.Invoke(__instance, new object[] { false });
            PowerGridPlugin.Log.LogInfo(
                "Atributo de mantenimiento restaurado en un cuadro electrico antiguo.");
        }
    }
}
