using System;
using System.Collections.Generic;
using System.Reflection;
using FullInspector;
using FullInspector.Generated.SharedInstance;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.PowerGrid
{
    internal enum Cost
    {
        Mensual,
        Tarea
    }

    internal enum Power
    {
        Bajo,
        Alto
    }

    internal static class ElectricityGameplay
    {
        private static readonly FieldInfo EnergyCostField = AccessTools.Field(typeof(RoomItemDefinition), "_energyCost");
        private static readonly FieldInfo FinanceReferenceField =
            AccessTools.Field(typeof(InteractionAttributeModifier), "_financeModifier");
        private static readonly FieldInfo InteractionModifiersField =
            AccessTools.Field(typeof(RoomItemDefinition), "_interactionAttributeModifiers");
        private static bool _configured;

        internal static void Configure(SharedInstance<RoomItemDefinition>[] items,
            SharedInstance<RoomDefinition>[] rooms)
        {
            if (items == null || _configured) return;

            var changed = 0;
            var perUseChanged = 0;
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item == null || EnergyRoomItems.IsTransformer(item) || EnergyRoomItems.IsCell(item)) continue;
                if (!TryGetConfiguredRule(item, out var consumption, out var billing, out _, out _)) continue;
                if (billing == Cost.Mensual)
                {
                    changed += SetMonthlyCost(item, consumption);
                }
                else
                {
                    perUseChanged += EnsurePerUseCost(item, consumption);
                }
            }

            _configured = true;
            PowerGridPlugin.Log.LogInfo("Reglas electricas aplicadas: " + changed +
                                        " costes mensuales actualizados y " + perUseChanged +
                                        " costes por uso actualizados.");
        }

        internal static bool TryGetConfiguredRule(IRoomItemDefinition definition, out int consumption,
            out Cost billing, out Power power, out float height)
        {
            consumption = 0;
            billing = Cost.Mensual;
            power = Power.Bajo;
            height = 0f;
            var item = definition as RoomItemDefinition;
            var prefabName = item?.GetPrefab(0)?.name;
            if (string.IsNullOrEmpty(prefabName)) return false;
            foreach (var rule in ElectricObjectCatalog.Objects)
            {
                if (!string.Equals(prefabName, rule.Prefab, StringComparison.OrdinalIgnoreCase)) continue;
                consumption = rule.Consumo;
                billing = rule.Cobro;
                power = rule.Red;
                height = rule.Altura;
                return true;
            }
            return false;
        }

        internal static bool RequiresPower(RoomItem item) =>
            item != null && RequiresPower(item.Definition);

        internal static bool RequiresPower(IRoomItemDefinition definition)
        {
            if (!_configured || definition == null || EnergyRoomItems.IsTransformer(definition) ||
                EnergyRoomItems.IsCell(definition) ||
                EnergyRoomItems.IsPanel(definition)) return false;
            if (definition.EnergyCost(0) > 0) return true;
            foreach (var modifier in definition.InteractionAttributeModifiers ??
                     Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance != null && finance.EnergyCost > 0) return true;
            }
            return false;
        }

        internal static bool ShowsElectricityIcon(IRoomItemDefinition definition)
        {
            if (!_configured || definition == null || EnergyRoomItems.IsTransformer(definition) ||
                EnergyRoomItems.IsCell(definition) ||
                EnergyRoomItems.IsBattery(definition) ||
                EnergyRoomItems.IsPanel(definition)) return false;
            if (definition.EnergyCost(0) > 0) return true;
            foreach (var modifier in definition.InteractionAttributeModifiers ??
                     Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance != null && finance.EnergyCost > 0) return true;
            }
            return false;
        }

        internal enum CostKind
        {
            None,
            Monthly,
            PerUse
        }

        internal static bool TryGetDisplayCost(RoomItem item, out int cost, out CostKind kind)
        {
            cost = 0;
            kind = CostKind.None;
            if (item?.Definition == null) return false;
            if (TryGetConfiguredRule(item.Definition, out var configuredCost, out var billing, out _, out _))
            {
                cost = Math.Max(0, configuredCost);
                kind = billing == Cost.Mensual ? CostKind.Monthly : CostKind.PerUse;
                return cost > 0;
            }
            if (item.EnergyCost > 0)
            {
                cost = item.EnergyCost;
                kind = CostKind.Monthly;
                return true;
            }

            foreach (var modifier in item.Definition.InteractionAttributeModifiers ??
                     Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance == null || finance.EnergyCost <= cost) continue;
                cost = finance.EnergyCost;
            }
            if (cost <= 0) return false;
            kind = CostKind.PerUse;
            return true;
        }

        internal static bool TryGetPowerType(IRoomItemDefinition definition, out Power power)
        {
            if (TryGetConfiguredRule(definition, out _, out _, out power, out _)) return true;
            power = Power.Bajo;
            if (definition == null) return false;
            if (definition.EnergyCost(0) > 0) return true;
            foreach (var modifier in definition.InteractionAttributeModifiers ??
                     Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance == null || finance.EnergyCost <= 0) continue;
                power = Power.Alto;
                return true;
            }
            return false;
        }

        internal static int GetDailyRecurringDemand(Level level)
        {
            if (level?.WorldState?.AllRooms == null) return 0;
            var total = 0;
            foreach (var room in level.WorldState.AllRooms)
            {
                var items = room?.FloorPlan?.Items;
                if (items == null) continue;
                foreach (var item in items)
                    if (TryGetDisplayCost(item, out var cost, out var kind) && kind == CostKind.Monthly)
                        total += Math.Max(0, cost);
            }
            return total;
        }

        private static int SetMonthlyCost(RoomItemDefinition item, int value)
        {
            if (item.EnergyCost(0) == value) return 0;
            EnergyCostField?.SetValue(item, value);
            PowerGridPlugin.Log.LogInfo("Consumo electrico mensual " + value + ": " + Identity(item));
            return 1;
        }

        private static int EnsurePerUseCost(RoomItemDefinition item, int value)
        {
            var changed = 0;
            var found = false;
            foreach (var modifier in item.InteractionAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance == null) continue;
                found = true;
                var privateFinanceWrapper =
                    ScriptableObject.CreateInstance<SharedInstance_TH20TH20_FinanceModifier>();
                privateFinanceWrapper.name = "UnderPressure Per Use Energy " + item.DebugTag;
                privateFinanceWrapper.hideFlags = HideFlags.HideAndDontSave;
                privateFinanceWrapper.Instance = CloneFinanceModifier(finance, value);
                FinanceReferenceField?.SetValue(modifier, privateFinanceWrapper);
                changed++;
            }
            if (found)
            {
                if (changed > 0)
                    PowerGridPlugin.Log.LogInfo("Consumo electrico por uso " + value + ": " + Identity(item));
                return changed;
            }

            // Algunos aparatos, entre ellos el secamanos original, tienen una interacción válida
            // pero carecen de modificador financiero. Se asigna el consumo a esa interacción para
            // que la visualización, la facturación y el suministro consulten los mismos datos.
            InteractionDefinition interaction = null;
            foreach (var candidate in item.Interactions ?? Array.Empty<InteractionDefinition>())
                if (candidate != null && !candidate.Deprecated)
                {
                    interaction = candidate;
                    break;
                }
            if (interaction == null || InteractionModifiersField == null)
            {
                PowerGridPlugin.Log.LogWarning("No se encontro interaccion util para el coste por uso: " +
                                               Identity(item));
                return 0;
            }

            var financeWrapper = ScriptableObject.CreateInstance<SharedInstance_TH20TH20_FinanceModifier>();
            financeWrapper.name = "UnderPressure Per Use Energy " + item.DebugTag;
            financeWrapper.hideFlags = HideFlags.HideAndDontSave;
            financeWrapper.Instance = new FinanceModifier { EnergyCost = value };
            var appended = new InteractionAttributeModifier
            {
                _interactionType = interaction.Type,
                _interactionName = interaction.Name,
                _objectModifiers = Array.Empty<ObjectAttributeModifier>(),
                _characterModifiers = Array.Empty<CharacterAttributeModifier>(),
                _characterModifiersWhileInteracting = Array.Empty<CharacterAttributeModifier>(),
                _characterStatusEffects = Array.Empty<SharedInstance<CharacterStatusEffectDefinition>>(),
                _characterModifiersRandom = Array.Empty<CharacterAttributeModifier>()
            };
            FinanceReferenceField?.SetValue(appended, financeWrapper);
            var existing = item.InteractionAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>();
            var expanded = new InteractionAttributeModifier[existing.Length + 1];
            Array.Copy(existing, expanded, existing.Length);
            expanded[existing.Length] = appended;
            InteractionModifiersField.SetValue(item, expanded);
            PowerGridPlugin.Log.LogInfo("Consumo electrico por uso " + value + " creado en " +
                                        interaction.Name + ": " + Identity(item));
            return 1;
        }

        private static FinanceModifier CloneFinanceModifier(FinanceModifier source, int energyCost)
        {
            var clone = new FinanceModifier();
            for (var type = typeof(FinanceModifier); type != null; type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsInitOnly)
                        field.SetValue(clone, field.GetValue(source));
            clone.EnergyCost = energyCost;
            return clone;
        }

        private static FinanceModifier GetFinanceModifier(InteractionAttributeModifier modifier)
        {
            if (modifier == null) return null;
            var reference = FinanceReferenceField?.GetValue(modifier);
            if (reference == null) return null;
            var type = reference.GetType();
            while (type != null)
            {
                var field = type.GetField("Instance", BindingFlags.Instance | BindingFlags.Public |
                                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(reference) as FinanceModifier;
                type = type.BaseType;
            }
            return null;
        }

        private static string Identity(RoomItemDefinition item)
        {
            var prefab = item.GetPrefab(0);
            return (item.DebugTag ?? string.Empty) + " " + (item.GetSanitizedName() ?? string.Empty) + " " +
                   (prefab == null ? string.Empty : prefab.name);
        }

    }

    [HarmonyPatch(typeof(ObjectInteraction), "IsAvailable")]
    internal static class ElectricityInteractionAvailabilityPatch
    {
        private static void Postfix(ObjectInteraction __instance, ref bool __result)
        {
            if (!__result) return;
            var item = __instance?.ParentRoomItem;
            if (ElectricityGameplay.RequiresPower(item) && !PowerGridPrototype.IsPowered(item))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(RoomItem), "IsFunctional")]
    internal static class ElectricityRoomItemFunctionalPatch
    {
        private static void Postfix(RoomItem __instance, ref bool __result)
        {
            if (__result && ElectricityGameplay.RequiresPower(__instance) &&
                !PowerGridPrototype.IsPowered(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(FinanceManager), "PayEnergyBill")]
    internal static class StoredEnergyBillPatch
    {
        private static readonly FieldInfo MonthlyEnergyField = AccessTools.Field(typeof(FinanceManager), "_energyBill");
        private static readonly FieldInfo PerUseEnergyField = AccessTools.Field(typeof(FinanceManager), "_energyBillPerUse");
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(FinanceManager), "_level");

        private struct BillState
        {
            internal int OriginalInstalled;
            internal bool UsesDailyAccumulator;
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(FinanceManager __instance, out BillState __state)
        {
            var level = LevelField?.GetValue(__instance) as Level;
            var installed = MonthlyEnergyField == null ? 0 : (int)MonthlyEnergyField.GetValue(__instance);
            var usesDailyAccumulator = PowerGridPrototype.TryGetAccruedDailyEnergyBill(__instance, out var monthly);
            if (!usesDailyAccumulator) monthly = installed;
            var perUse = PerUseEnergyField == null ? 0 : (int)PerUseEnergyField.GetValue(__instance);
            __state = new BillState
            {
                OriginalInstalled = installed,
                UsesDailyAccumulator = usesDailyAccumulator
            };
            var multiplier = 1f - EnergyCampaignSystem.GetEffect(level, EnergyCampaignKind.HackPowerCompany);
            monthly = Mathf.RoundToInt(monthly * Mathf.Clamp01(multiplier));
            perUse = Mathf.RoundToInt(perUse * Mathf.Clamp01(multiplier));
            MonthlyEnergyField?.SetValue(__instance, monthly);
            PerUseEnergyField?.SetValue(__instance, perUse);
        }

        private static Exception Finalizer(Exception __exception, FinanceManager __instance, BillState __state)
        {
            // La campaña reduce el dinero pagado, no la demanda física. El total diario ya
            // facturado se reinicia, mientras el valor nativo instalado se conserva para que
            // añadir o retirar objetos continúe funcionando normalmente.
            MonthlyEnergyField?.SetValue(__instance, __state.OriginalInstalled);
            if (__exception == null && __state.UsesDailyAccumulator)
                PowerGridPrototype.ClearAccruedDailyEnergyBill(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(FinanceManager), "get_EnergyBills")]
    internal static class AccruedDailyEnergyBillPreviewPatch
    {
        private static readonly FieldInfo MonthlyEnergyField = AccessTools.Field(typeof(FinanceManager), "_energyBill");

        [HarmonyPriority(Priority.First)]
        private static void Prefix(FinanceManager __instance, out int __state)
        {
            __state = MonthlyEnergyField == null ? 0 : (int)MonthlyEnergyField.GetValue(__instance);
            if (PowerGridPrototype.TryGetAccruedDailyEnergyBill(__instance, out var accrued))
                MonthlyEnergyField?.SetValue(__instance, accrued);
        }

        private static Exception Finalizer(Exception __exception, FinanceManager __instance, int __state)
        {
            MonthlyEnergyField?.SetValue(__instance, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(FinanceManager), "ModifyBalanceFromObjectInteraction")]
    internal static class DailyTaskEnergyPatch
    {
        private static void Postfix(FinanceManager __instance, FinanceModifier __2)
        {
            if (__2 == null || __2.EnergyCost <= 0) return;
            PowerGridPrototype.RecordTaskEnergy(__instance, __2.EnergyCost);
        }
    }

}
