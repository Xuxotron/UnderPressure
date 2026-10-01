using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    internal static class ResalePenalties
    {
        private static readonly FieldInfo RoomCostField =
            AccessTools.Field(typeof(RoomDefinition), "_cost");

        internal static int ApplyObjectRefund(int value) => Mathf.FloorToInt(value * 0.8f);

        internal static int ApplyRoomRefund(int value) => Mathf.FloorToInt(value * 0.5f);

        internal static int GetRoomBaseCost(FloorPlan floorPlan) =>
            floorPlan?.Definition == null || RoomCostField == null
                ? 0
                : (int)RoomCostField.GetValue(floorPlan.Definition);
    }

    [HarmonyPatch(typeof(RoomItem), "SellValue")]
    internal static class RoomItemResalePenaltyPatch
    {
        private static void Postfix(ref int __result)
        {
            if (UnderPressurePlugin.ShouldUseResalePenalties)
                __result = ResalePenalties.ApplyObjectRefund(__result);
        }
    }

    [HarmonyPatch(typeof(GameAlgorithms), "CalculateSellCostOfRoom")]
    internal static class RoomResalePenaltyPatch
    {
        private static void Postfix(FloorPlan __0, ref int __result)
        {
            if (!UnderPressurePlugin.ShouldUseResalePenalties) return;
            // Los objetos ya llegan con su devolución del 80%; solo se sustituye la
            // devolución completa de la construcción y sus muros por el 50%.
            var roomBaseCost = ResalePenalties.GetRoomBaseCost(__0);
            __result -= roomBaseCost - ResalePenalties.ApplyRoomRefund(roomBaseCost);
        }
    }
}
