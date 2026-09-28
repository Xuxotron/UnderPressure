using System;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    internal static class LampUnlocks
    {
        private static readonly RoomDefinition.Type[] EveryRoom = Array.Empty<RoomDefinition.Type>();

        internal static bool IsSupportedLamp(object candidate)
        {
            var definition = candidate as RoomItemDefinition;
            if (definition == null) return false;

            var prefab = definition.GetPrefab(0);
            var prefabName = prefab == null ? string.Empty : prefab.name;
            return Is(prefabName, "A_Prop_GP_Lamp_V1") ||
                   Is(prefabName, "A_Prop_Psychiatry_Lamp_V1") ||
                   Is(prefabName, "A_Prop_Staff_Room_Lamp_V1") ||
                   Is(prefabName, "A_Prop_Marketing_Lamp_V1");
        }

        internal static RoomDefinition.Type[] UnrestrictedRoomTypes => EveryRoom;

        private static bool Is(string value, string expected) =>
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch(typeof(Metagame), "HasUnlocked", new[] { typeof(ISilverUnlockable) })]
    internal static class LampUnlockPatch
    {
        private static void Postfix(ISilverUnlockable __0, ref bool __result)
        {
            if (UnderPressurePlugin.ShouldUnlockLamps && LampUnlocks.IsSupportedLamp(__0))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(RoomItemDefinition), nameof(RoomItemDefinition.CanBePlacedIn))]
    internal static class LampRoomPlacementPatch
    {
        private static void Postfix(RoomItemDefinition __instance, ref bool __result)
        {
            if (UnderPressurePlugin.ShouldUnlockLamps && LampUnlocks.IsSupportedLamp(__instance))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(RoomItemDefinition), "get_CanBePlacedInRoomTypes")]
    internal static class LampAvailableRoomTypesPatch
    {
        private static void Postfix(RoomItemDefinition __instance, ref RoomDefinition.Type[] __result)
        {
            if (UnderPressurePlugin.ShouldUnlockLamps && LampUnlocks.IsSupportedLamp(__instance))
                __result = LampUnlocks.UnrestrictedRoomTypes;
        }
    }
}
