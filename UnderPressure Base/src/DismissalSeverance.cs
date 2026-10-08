// Purpose: Charges dismissal severance from the employee's current salary and accumulated tenure.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    internal static class DismissalSeverance
    {
        private const int MaximumComputableMonths = 48;
        private const int MonthsPerYear = 12;
        private const double DaysPerYear = 365.25d;
        private const double DaysPerMonth = 30.4375d;
        private static readonly Type ModifyBalanceParamsType =
            AccessTools.Inner(typeof(FinanceManager), "ModifyBalanceParams");
        private static readonly MethodInfo ModifyBalanceMethod =
            AccessTools.Method(typeof(FinanceManager), "ModifyBalance");
        private static readonly FieldInfo AmountField =
            AccessTools.Field(ModifyBalanceParamsType, "Amount");

        internal static int CalculateComputableMonths(Staff staff)
        {
            if (staff == null)
                return 0;

            // Se replica el desglose visible del juego y se descartan los días sobrantes.
            var completedDays = staff.DaysInHospital;
            var completedYears = (int)Math.Floor(completedDays / DaysPerYear);
            var remainingDays = completedDays - (int)(completedYears * DaysPerYear);
            var completedMonths = completedYears * MonthsPerYear +
                (int)Math.Floor(remainingDays / DaysPerMonth);
            return Mathf.Clamp(completedMonths, 0, MaximumComputableMonths);
        }

        internal static int CalculateAmount(int currentSalary, int computableMonths)
        {
            if (currentSalary <= 0 || computableMonths <= 0)
                return 0;

            var amount = (double)currentSalary *
                Math.Min(computableMonths, MaximumComputableMonths) * 0.02d;
            return amount >= int.MaxValue
                ? int.MaxValue
                : Mathf.RoundToInt((float)amount);
        }

        internal static void Charge(Staff staff)
        {
            if (staff == null)
                return;

            var months = CalculateComputableMonths(staff);
            var amount = CalculateAmount(staff.GetSalary(), months);
            var finance = staff.Level?.FinanceManager;
            if (amount <= 0 || finance == null || ModifyBalanceParamsType == null ||
                ModifyBalanceMethod == null || AmountField == null)
                return;

            var parameters = Activator.CreateInstance(ModifyBalanceParamsType, true);
            AmountField.SetValue(parameters, -amount);
            ModifyBalanceMethod.Invoke(finance, new[] { parameters });
        }
    }

    [HarmonyPatch]
    internal static class DismissalSeverancePatch
    {
        private static readonly Type FiredModeChangeType =
            AccessTools.Inner(typeof(Staff), "FiredModeChange");

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Character), "ChangeMode");

        private static void Postfix(Character __instance, object __0, bool __result)
        {
            if (!__result || !UnderPressurePlugin.ShouldUseDismissalSeverance ||
                FiredModeChangeType == null || __0 == null || __0.GetType() != FiredModeChangeType)
                return;

            DismissalSeverance.Charge(__instance as Staff);
        }
    }

    [HarmonyPatch]
    internal static class DismissalSeveranceMessagePatch
    {
        private const string AmountColour = "#FF6A3D";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InspectorDataStaff), "Fire");
            yield return AccessTools.Method(typeof(SelectMenuStaff), "FireButton");
        }

        private static string GetStaffRecordTextWithDismissalCost(Staff staff)
        {
            var record = GameStringUtils.GetStaffRecordText(staff);
            var amount = UnderPressurePlugin.ShouldUseDismissalSeverance
                ? DismissalSeverance.CalculateAmount(staff.GetSalary(),
                    DismissalSeverance.CalculateComputableMonths(staff))
                : 0;
            var currency = StringUtils.FormatCurrency(amount, false, true);
            var colouredAmount = "<color=" + AmountColour + ">" + currency + "</color>";
            return record + "\n" + string.Format(
                ModLocalization.Get("staff.dismissal_penalty"), colouredAmount);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(GameStringUtils), "GetStaffRecordText");
            var replacement = AccessTools.Method(typeof(DismissalSeveranceMessagePatch),
                nameof(GetStaffRecordTextWithDismissalCost));
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }
    }
}
