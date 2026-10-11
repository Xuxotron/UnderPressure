// Purpose: Groups staff hiring, traits, and dismissal management.
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
        private const string AmountColour = "#f97e3b";

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

    // Only JobApplicant calls GenerateRandomTraits. Qualifications are generated separately.
    [HarmonyPatch(typeof(CharacterTraitsManager), "GenerateRandomTraits")]
    internal static class ImperfectStaffPatch
    {
        private static readonly System.Random Random = new System.Random();
        private static readonly HashSet<string> Positive = new HashSet<string>
        {
            "Trait_Charming", "Trait_MovementSpeed_Higher", "Trait_Happiness_Higher",
            "Trait_Training_Learn_Fast", "Trait_Training_Teach_Fast", "Trait_Hygiene_High",
            "Trait_Entertainer", "Trait_GreenFingers", "Trait_Energy_Higher",
            "Trait_Funny", "Trait_Healer", "Trait_Inspiring", "Trait_Pay_Low"
        };
        private static readonly HashSet<string> Negative = new HashSet<string>
        {
            "Trait_MovementSpeed_Lower", "Trait_Happiness_Lower", "Trait_Training_Learn_Slow",
            "Trait_WeakBladder", "Trait_Hygiene_Low", "Trait_Litterer", "Trait_Nasty",
            "Trait_ShortTemper", "Trait_Hangry", "Trait_Narcolepsy", "Trait_Boring",
            "Trait_Dirty", "Trait_Energy_Lower", "Trait_Evil", "Trait_NauseaInducing",
            "Trait_Pay_High", "Trait_ToiletRage"
        };

        private static void Postfix(CharacterTraitsManager __instance, StaffDefinition.Type __0,
            ref CharacterTraits __result)
        {
            if (!UnderPressurePlugin.ShouldUseImperfectStaff || __result == null) return;

            var chosen = new List<CharacterTraitDefinition>(3);
            var all = __instance.AllTraits.List;
            if (!Choose(all, Negative, __0, chosen) ||
                !Choose(all, Positive, __0, chosen) ||
                !Choose(all, Positive, __0, chosen))
            {
                UnderPressurePlugin.Log.LogWarning("No se pudo generar un candidato con 2 rasgos buenos y 1 malo.");
                return;
            }

            var flavour = (string)AccessTools.Field(typeof(CharacterTraits), "_flavourTraits")
                .GetValue(__result);
            __result = new CharacterTraits(chosen, flavour);
        }

        private static bool Choose(Dictionary<CharacterTraitDefinition, int> all,
            HashSet<string> category, StaffDefinition.Type staffType,
            List<CharacterTraitDefinition> chosen)
        {
            var candidates = new List<KeyValuePair<CharacterTraitDefinition, int>>();
            var total = 0;
            foreach (var pair in all)
            {
                var trait = pair.Key;
                var term = trait.ShortNameLocalisedMale.Term;
                var start = term == null ? -1 : term.IndexOf("Trait_", StringComparison.Ordinal);
                var end = term == null ? -1 : term.IndexOf("_ShortName", StringComparison.Ordinal);
                if (start < 0 || end <= start ||
                    !category.Contains(term.Substring(start, end - start)) ||
                    !trait.IsValidFor(staffType) || !trait.CanAdd(chosen) || pair.Value <= 0)
                    continue;
                candidates.Add(pair);
                total += pair.Value;
            }
            if (total <= 0) return false;
            var roll = Random.Next(total);
            foreach (var pair in candidates)
            {
                roll -= pair.Value;
                if (roll < 0)
                {
                    chosen.Add(pair.Key);
                    return true;
                }
            }
            return false;
        }
    }
}
