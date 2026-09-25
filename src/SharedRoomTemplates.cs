using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    internal static class SharedRoomTemplatesRuntime
    {
        [ThreadStatic] internal static bool ValidatingSharedTemplate;
    }

    [HarmonyPatch(typeof(RibbonMenuRoomsState), "SetRoomTemplatesList")]
    internal static class SharedRoomTemplatesListPatch
    {
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(RibbonMenuRoomsState), "_level");
        private static readonly FieldInfo TemplatesField = AccessTools.Field(typeof(RibbonMenuRoomsState), "_roomTemplates");
        private static readonly MethodInfo AddRowMethod = AccessTools.Method(typeof(RibbonMenuRoomsState), "AddRoomTemplateRow");

        private static void Postfix(RibbonMenuRoomsState __instance)
        {
            if (!UnderPressurePlugin.ShouldShareRoomTemplates) return;
            var level = LevelField?.GetValue(__instance) as Level;
            var visible = TemplatesField?.GetValue(__instance) as List<RoomTemplate>;
            var manager = level?.App?.RoomTemplatesManager;
            if (visible == null || manager?.RoomTemplates == null || AddRowMethod == null) return;

            foreach (var roomType in manager.RoomTemplates.Values)
                foreach (var template in roomType.Values)
                {
                    if (template == null || visible.Contains(template)) continue;
                    visible.Add(template);
                    AddRowMethod.Invoke(__instance, new object[] { template });
                }
        }
    }

    [HarmonyPatch(typeof(RibbonMenuRoomsState), "RefreshRowMode")]
    internal static class SharedRoomTemplateValidationScopePatch
    {
        private static void Prefix(RibbonRoomRow __0)
        {
            SharedRoomTemplatesRuntime.ValidatingSharedTemplate =
                UnderPressurePlugin.ShouldShareRoomTemplates && __0 != null && __0.RoomTemplate != null;
        }

        private static void Finalizer()
        {
            SharedRoomTemplatesRuntime.ValidatingSharedTemplate = false;
        }
    }

    [HarmonyPatch(typeof(Metagame), "HasUnlocked", typeof(ISilverUnlockable))]
    internal static class SharedRoomTemplateUnlockPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (SharedRoomTemplatesRuntime.ValidatingSharedTemplate) __result = true;
        }
    }
}
