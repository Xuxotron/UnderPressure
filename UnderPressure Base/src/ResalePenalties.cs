using System;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    internal static class ResalePenalties
    {
        [ThreadStatic]
        private static int _roomSaleCalculationDepth;

        internal static bool IsCalculatingRoomSale => _roomSaleCalculationDepth > 0;

        internal static void BeginRoomSaleCalculation() => _roomSaleCalculationDepth++;

        internal static void EndRoomSaleCalculation()
        {
            if (_roomSaleCalculationDepth > 0) _roomSaleCalculationDepth--;
        }

        internal static int ApplyObjectRefund(int value) => Mathf.FloorToInt(value * 0.8f);

        internal static int ApplyRoomRefund(int value) => Mathf.FloorToInt(value * 0.5f);
    }

    [HarmonyPatch(typeof(RoomItem), "SellValue")]
    internal static class RoomItemResalePenaltyPatch
    {
        private static void Postfix(ref int __result)
        {
            if (UnderPressurePlugin.ShouldUseResalePenalties &&
                !ResalePenalties.IsCalculatingRoomSale)
                __result = ResalePenalties.ApplyObjectRefund(__result);
        }
    }

    [HarmonyPatch(typeof(GameAlgorithms), "CalculateSellCostOfRoom")]
    internal static class RoomResalePenaltyPatch
    {
        private static void Prefix(out bool __state)
        {
            __state = UnderPressurePlugin.ShouldUseResalePenalties;
            if (__state) ResalePenalties.BeginRoomSaleCalculation();
        }

        private static void Postfix(ref int __result, bool __state)
        {
            if (__state) __result = ResalePenalties.ApplyRoomRefund(__result);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            // El contador se limpia también si el cálculo nativo falla, para no contaminar ventas posteriores.
            if (__state) ResalePenalties.EndRoomSaleCalculation();
            return __exception;
        }
    }
}
