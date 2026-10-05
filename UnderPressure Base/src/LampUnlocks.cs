// Purpose: Unlocks supported lamps and makes them available across compatible room types.
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    internal static class LampUnlocks
    {
        private static readonly RoomDefinition.Type[] EveryRoom = Array.Empty<RoomDefinition.Type>();

        internal static bool IsSupportedLamp(object candidate)
        {
            var definition = candidate as IRoomItemDefinition;
            if (definition == null) return false;

            var prefab = definition.GetPrefab(0);
            var prefabName = prefab == null ? string.Empty : prefab.name;
            return Is(prefabName, "RI_GP_Lamp") ||
                   Is(prefabName, "RI_Psych_Lamp") ||
                   Is(prefabName, "RI_Staff_Room_Lamp") ||
                   Is(prefabName, "RI_Marketing_Lamp");
        }

        internal static RoomDefinition.Type[] UnrestrictedRoomTypes => EveryRoom;

        private static bool Is(string value, string expected) =>
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch(typeof(WorldState), nameof(WorldState.GetItemsForRoom))]
    internal static class LampRoomCatalogPatch
    {
        private static readonly System.Reflection.FieldInfo MetagameField =
            AccessTools.Field(typeof(WorldState), "_metagame");
        private static readonly System.Reflection.FieldInfo MetagameConfigField =
            AccessTools.Field(typeof(Metagame), "_config");

        private static void Postfix(WorldState __instance, RoomDefinition.Type __0,
            List<IRoomItemDefinition> __2)
        {
            if (!UnderPressurePlugin.ShouldUnlockLamps || __2 == null) return;
            var metagame = MetagameField?.GetValue(__instance) as Metagame;
            var config = metagame == null ? null : MetagameConfigField?.GetValue(metagame);
            var databaseWrapper = config == null
                ? null
                : AccessTools.Field(config.GetType(), "RoomItemDatabase")?.GetValue(config);
            var database = databaseWrapper == null
                ? null
                : AccessTools.Field(databaseWrapper.GetType(), "Instance")?.GetValue(databaseWrapper) as RoomItemDatabase;
            var definitions = AccessTools.Field(typeof(RoomItemDatabase), "RoomItems")?.GetValue(database) as IEnumerable;
            if (definitions == null) return;

            foreach (var wrapper in definitions)
            {
                var definition = wrapper == null
                    ? null
                    : AccessTools.Field(wrapper.GetType(), "Instance")?.GetValue(wrapper) as IRoomItemDefinition;
                if (!LampUnlocks.IsSupportedLamp(definition) ||
                    !definition.CanBePlacedIn(__0) || __2.Contains(definition)) continue;
                __2.Add(definition);
            }
        }
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

    [HarmonyPatch(typeof(Metagame), nameof(Metagame.IsBlacklisted))]
    internal static class LampBlacklistPatch
    {
        private static void Postfix(IRoomItemDefinition __0, ref bool __result)
        {
            if (UnderPressurePlugin.ShouldUnlockLamps && LampUnlocks.IsSupportedLamp(__0))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Metagame), nameof(Metagame.IsWhitelisted))]
    internal static class LampWhitelistPatch
    {
        private static void Postfix(IRoomItemDefinition __0, ref bool __result)
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
