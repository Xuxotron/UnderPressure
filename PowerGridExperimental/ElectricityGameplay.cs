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
            if (items == null) return;

            var changed = 0;
            var perUseChanged = 0;
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item == null || EnergyRoomItems.IsTransformer(item)) continue;
                var identity = Identity(item);

                // Specific-use appliances take precedence over the generic monitor rule.
                // A vending machine may contain a transform called Screen, but its requested
                // tariff is 50 rather than the 100 used by computer workstations.
                if (IsVendingMachine(item, identity))
                    changed += SetMonthlyCost(item, 50);
                else if (IsRequestedComputerDesk(item))
                    changed += SetMonthlyCost(item, 100);

                if (IsHandDryer(identity)) perUseChanged += EnsurePerUseCost(item, 5);
            }

            _configured = true;
            PowerGridPlugin.Log.LogInfo("Reglas electricas aplicadas: " + changed +
                                        " costes mensuales actualizados y " + perUseChanged +
                                        " costes por uso actualizados.");
        }

        internal static bool RequiresPower(RoomItem item) =>
            item != null && RequiresPower(item.Definition);

        internal static bool RequiresPower(IRoomItemDefinition definition)
        {
            if (!_configured || definition == null || EnergyRoomItems.IsTransformer(definition) ||
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

        internal static int GetMonthlyEnergyDemand(Level level)
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
            foreach (var modifier in item.InteractionAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance == null || finance.EnergyCost == value) continue;
                finance.EnergyCost = value;
                changed++;
            }
            if (changed > 0)
            {
                PowerGridPlugin.Log.LogInfo("Consumo electrico por uso " + value + ": " + Identity(item));
                return changed;
            }

            // Some appliances (the native hand dryer among them) have a usable interaction
            // but no finance modifier at all. Attach the tariff to that exact interaction so
            // display, billing and power requirements all continue to use the same global data.
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

        private static bool IsRequestedComputerDesk(RoomItemDefinition item)
        {
            var prefab = item?.GetPrefab(0);
            if (prefab == null) return false;
            return string.Equals(prefab.name, "RI_Reception", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(prefab.name, "RI_Ward_Nurse_Station", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(prefab.name, "RI_OfficeDesk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVendingMachine(RoomItemDefinition item, string identity)
        {
            if (Contains(identity, "vending") || Contains(identity, "drink machine") ||
                Contains(identity, "snack machine")) return true;
            foreach (var modifier in item.InteractionAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>())
            {
                var finance = GetFinanceModifier(modifier);
                if (finance != null && (finance.Type == FinanceModifier.EType.VendingMachine_Drink ||
                                        finance.Type == FinanceModifier.EType.VendingMachine_Snack)) return true;
            }
            return false;
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

        private static bool IsHandDryer(string identity) =>
            Contains(identity, "hand dryer") || Contains(identity, "handdryer") ||
            Contains(identity, "hand_dryer") || Contains(identity, "secamanos");

        private static string Identity(RoomItemDefinition item)
        {
            var prefab = item.GetPrefab(0);
            return (item.DebugTag ?? string.Empty) + " " + (item.GetSanitizedName() ?? string.Empty) + " " +
                   (prefab == null ? string.Empty : prefab.name);
        }

        private static bool Contains(string value, string term) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
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
            internal int OriginalMonthly;
            internal int PerUseEnergy;
        }

        private static void Prefix(FinanceManager __instance, out BillState __state)
        {
            var monthly = MonthlyEnergyField == null ? 0 : (int)MonthlyEnergyField.GetValue(__instance);
            var perUse = PerUseEnergyField == null ? 0 : (int)PerUseEnergyField.GetValue(__instance);
            __state = new BillState { OriginalMonthly = monthly, PerUseEnergy = Math.Max(0, perUse) };
            var level = LevelField?.GetValue(__instance) as Level;
            var multiplier = 1f - EnergyCampaignSystem.GetEffect(level, EnergyCampaignKind.HackPowerCompany);
            monthly = Mathf.RoundToInt(monthly * Mathf.Clamp01(multiplier));
            perUse = Mathf.RoundToInt(perUse * Mathf.Clamp01(multiplier));
            MonthlyEnergyField?.SetValue(__instance, monthly);
            PerUseEnergyField?.SetValue(__instance, perUse);
        }

        private static void Postfix(FinanceManager __instance, BillState __state)
        {
            // The campaign discounts the money paid, not the physical demand. The native
            // recurring monthly value must survive payment; only the per-use accumulator resets.
            MonthlyEnergyField?.SetValue(__instance, __state.OriginalMonthly);
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
