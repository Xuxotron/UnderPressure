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
    [HarmonyPatch(typeof(RoomItemDefinition), nameof(RoomItemDefinition.AllowCollisionOutsideRoom))]
    internal static class ElectricalPanelRoomBoundaryPatch
    {
        private static void Postfix(RoomItemDefinition __instance, ref bool __result)
        {
            if (EnergyRoomItems.IsPanel(__instance)) __result = true;
        }
    }

    [HarmonyPatch(typeof(CursorRoomItem), "CursorUpdate")]
    internal static class EnergyDeskRotationSnapPatch
    {
        private static readonly FieldInfo DefinitionField = AccessTools.Field(typeof(CursorRoomItem), "_definition");
        private static readonly FieldInfo FloorPlanField = AccessTools.Field(typeof(CursorRoomItem), "_floorPlan");
        private static readonly FieldInfo RotationSnapField =
            AccessTools.Field(typeof(RoomItemDefinition), "_rotationSnap");

        private static void Prefix(CursorRoomItem __instance, out float __state)
        {
            __state = float.NaN;
            var definition = DefinitionField?.GetValue(__instance) as RoomItemDefinition;
            var floorPlan = FloorPlanField?.GetValue(__instance) as FloorPlan;
            if (definition == null || floorPlan?.Definition == null || RotationSnapField == null ||
                !ReferenceEquals(definition, EnergyRoomItems.MarketingDesk) ||
                !PowerPlantRoomRegistry.IsPowerPlant(floorPlan.Definition)) return;
            __state = definition.RotationSnap;
            RotationSnapField.SetValue(definition, 90f);
        }

        private static void Postfix(float __state)
        {
            Restore(__state);
        }

        private static Exception Finalizer(Exception __exception, float __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(float previous)
        {
            if (float.IsNaN(previous) || EnergyRoomItems.MarketingDesk == null || RotationSnapField == null) return;
            RotationSnapField.SetValue(EnergyRoomItems.MarketingDesk, previous);
        }
    }

    internal static class EnergyRoomItems
    {
        // AssetIDMapping reserves wrapper ID - 1 for the wrapped instance. Keep
        // these pairs apart; consecutive wrapper IDs collide with that rule.
        private const int BatterySharedId = 9112101;
        private const int PanelSharedId = 9112111;
        private const int TransformerSharedId = 9112121;
        private static readonly Guid BatteryGuid = new Guid("5c382c42-4eb7-4c8a-8aca-a5f902481101");
        private static readonly Guid PanelGuid = new Guid("5c382c42-4eb7-4c8a-8aca-a5f902481102");
        private static readonly Guid TransformerGuid = new Guid("5c382c42-4eb7-4c8a-8aca-a5f902481103");
        private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");

        internal const string BatteryTag = "under pressure energy battery";
        internal const string PanelTag = "under pressure electrical panel";
        internal const string TransformerTag = "under pressure transformer";

        internal static RoomItemDefinition Battery { get; private set; }
        internal static RoomItemDefinition Panel { get; private set; }
        internal static RoomItemDefinition Transformer { get; private set; }
        internal static RoomItemDefinition MarketingDesk { get; private set; }
        internal static InteractionDefinition MaintenanceDeskInteraction { get; private set; }
        internal static SharedInstance_TH20TH20_RoomItemDefinition BatteryShared { get; private set; }
        internal static SharedInstance_TH20TH20_RoomItemDefinition PanelShared { get; private set; }
        internal static SharedInstance_TH20TH20_RoomItemDefinition TransformerShared { get; private set; }

        internal static bool Ensure(Metagame metagame, RoomDefinition marketingRoom,
            SharedInstance<RoomDefinition>[] rooms)
        {
            var database = metagame?.RoomItemDatabase?.Instance;
            if (database?.RoomItems == null) return false;

            FindExisting(database.RoomItems);
            MarketingDesk = FindMarketingDesk(marketingRoom);
            var campaignTable = FindCampaignMenuSource(database.RoomItems);
            InstallDeskMaintenanceInteraction();
            var filingCabinet = FindItem(database.RoomItems, "filing cabinet", "filing", "archivador");
            var wallItem = FindWallItem(database.RoomItems);
            var pharmacyMachine = FindPharmacyMachine(rooms);
            var batteryVisual = FindBatteryVisual(metagame);
            var radiator = FindItem(database.RoomItems, "radiator");

            if (MarketingDesk == null || filingCabinet == null || wallItem == null || pharmacyMachine == null ||
                batteryVisual == null || radiator == null || PowerGridPlugin.PowerPanelPrefab == null ||
                PowerGridPlugin.PowerPanelSprite == null)
            {
                PowerGridPlugin.Log.LogError("No se pudieron localizar todos los objetos base de la Sala de energia: " +
                    $"desk={MarketingDesk != null}, filing={filingCabinet != null}, wall={wallItem != null}, " +
                    $"pharmacy={pharmacyMachine != null}, " +
                    $"battery={batteryVisual != null}, radiator={radiator != null}, " +
                    $"panelPrefab={PowerGridPlugin.PowerPanelPrefab != null}, " +
                    $"panelIcon={PowerGridPlugin.PowerPanelSprite != null}.");
                return false;
            }

            var additions = new List<SharedInstance<RoomItemDefinition>>();
            if (Battery == null)
            {
                Battery = CloneBase(filingCabinet, BatteryTag,
                    BatteryGuid,
                    "energy.battery.name", "energy.battery.description");
                Set(Battery, "_prefab", batteryVisual.GetPrefab(0));
                Set(Battery, "_blueprintPrefab", batteryVisual.GetBlueprintPrefab(0) ?? batteryVisual.GetPrefab(0));
                Set(Battery, "_cost", 2000);
                Set(Battery, "_singlePlace", false);
                DisableOwnInteractions(Battery);
                BatteryShared = CreateWrapper(Battery, BatterySharedId, "UnderPressure Energy Battery");
                additions.Add(BatteryShared);
            }

            if (Panel == null)
            {
                Panel = CloneBase(filingCabinet, PanelTag,
                    PanelGuid,
                    "energy.panel.name", "energy.panel.description");
                CopyWallPlacement(Panel, wallItem);
                Set(Panel, "_prefab", PowerGridPlugin.PowerPanelPrefab);
                Set(Panel, "_blueprintPrefab", PowerGridPlugin.PowerPanelPrefab);
                Set(Panel, "_icon", PowerGridPlugin.PowerPanelSprite);
                Set(Panel, "_iconWithoutBacking", PowerGridPlugin.PowerPanelSprite);
                Set(Panel, "_canBePlacedIn", Array.Empty<RoomDefinition.Type>());
                Set(Panel, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>());
                Set(Panel, "_singlePlace", false);
                Set(Panel, "_hasCollision", true);
                Set(Panel, "_occupyWallOnly", false);
                Set(Panel, "_affectsNavigation", false);
                Set(Panel, "_energyCost", 300);
                Set(Panel, "_generatesElectricity", false);
                Set(Panel, "_ignoredByJanitors", true);
                Set(Panel, "_maintenanceModifer", 0f);
                Set(Panel, "_prestige", 0f);
                Set(Panel, "_hospitalLevelPoints", 0f);
                Set(Panel, "_roomModifiers", Array.Empty<RoomModifier>());
                Set(Panel, "_interactionAttributeModifiers", Array.Empty<InteractionAttributeModifier>());
                Set(Panel, "_upgrades", Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>());
                DisableOwnInteractions(Panel);
                PanelShared = CreateWrapper(Panel, PanelSharedId, "UnderPressure Electrical Panel");
                additions.Add(PanelShared);
            }

            if (Transformer == null)
            {
                Transformer = CloneBase(pharmacyMachine, TransformerTag,
                    TransformerGuid,
                    "energy.transformer.name", "energy.transformer.description");
                Set(Transformer, "_singlePlace", true);
                Set(Transformer, "_generatesElectricity", false);
                Set(Transformer, "_energyCost", 0);
                Set(Transformer, "_interactionAttributeModifiers", Array.Empty<InteractionAttributeModifier>());
                Set(Transformer, "_ignoredByJanitors", true);
                Set(Transformer, "_maintenanceModifer", 0f);
                DisableOwnInteractions(Transformer);
                Set(Transformer, "_upgrades", Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>());
                TransformerShared = CreateWrapper(Transformer, TransformerSharedId, "UnderPressure Transformer");
                additions.Add(TransformerShared);
            }

            // Use the native radiator's exact room modifiers so both Energy Room
            // objects contribute the same amount of heat without imitating the effect.
            Set(Battery, "_roomModifiers", radiator.RoomModifiers);
            Set(Transformer, "_roomModifiers", radiator.RoomModifiers);
            Set(Transformer, "_ignoredByJanitors", true);
            EnergyCampaignRuntime.ConfigureTransformerDefinition(Transformer, campaignTable);

            ElectricityGameplay.Configure(database.RoomItems, rooms);

            AllowInEnergyRoom(MarketingDesk);

            if (additions.Count > 0)
            {
                var expanded = new SharedInstance<RoomItemDefinition>[database.RoomItems.Length + additions.Count];
                Array.Copy(database.RoomItems, expanded, database.RoomItems.Length);
                for (var i = 0; i < additions.Count; ++i)
                    expanded[database.RoomItems.Length + i] = additions[i];
                database.RoomItems = expanded;
                PowerGridPlugin.Log.LogInfo("Objetos de la Sala de energia registrados: bateria, cuadro y transformador.");
            }
            return true;
        }

        internal static RequiredItem Requirement(string group, RoomItemDefinition definition, int id)
        {
            if (definition == null) return null;
            var wrapper = WrapperFor(definition);
            if (wrapper == null)
            {
                PowerGridPlugin.Log.LogError("No existe un SharedInstance registrado para " + group + ".");
                return null;
            }
            return new RequiredItem
            {
                GroupName = group,
                Items = new SharedInstance<RoomItemDefinition>[] { wrapper }
            };
        }

        internal static bool IsTransformer(RoomItem item)
        {
            return item != null && ReferenceEquals(item.Definition, Transformer);
        }

        internal static bool IsBattery(RoomItem item) => item != null && IsBattery(item.Definition);

        internal static bool IsBattery(IRoomItemDefinition definition)
        {
            var item = definition as RoomItemDefinition;
            return item != null && (ReferenceEquals(item, Battery) || item.DebugTag == BatteryTag);
        }

        internal static bool IsTransformer(IRoomItemDefinition definition)
        {
            var item = definition as RoomItemDefinition;
            return item != null && (ReferenceEquals(item, Transformer) || item.DebugTag == TransformerTag);
        }

        internal static bool IsPanel(RoomItem item) => item != null && IsPanel(item.Definition);

        internal static bool IsPanel(IRoomItemDefinition definition)
        {
            var item = definition as RoomItemDefinition;
            return item != null && (ReferenceEquals(item, Panel) || item.DebugTag == PanelTag);
        }

        internal static bool IsCustomDefinition(IRoomItemDefinition definition)
        {
            var item = definition as RoomItemDefinition;
            if (item == null) return false;
            if (ReferenceEquals(item, Battery) || ReferenceEquals(item, Panel) || ReferenceEquals(item, Transformer))
                return true;
            var tag = item.DebugTag;
            return tag == BatteryTag || tag == PanelTag || tag == TransformerTag;
        }

        private static RoomItemDefinition CloneBase(RoomItemDefinition source, string tag, Guid guid,
            string nameKey, string descriptionKey)
        {
            var clone = (RoomItemDefinition)MemberwiseCloneMethod.Invoke(source, null);
            Set(clone, "_guid", guid);
            Set(clone, "_debugTag", tag);
            Set(clone, "_localisedName", EnergyLocalization.Create(nameKey));
            var description = EnergyLocalization.Create(descriptionKey);
            Set(clone, "_localisedDescription", description);
            Set(clone, "_functionalDescription", description);
            Set(clone, "_initiallyAvailable", true);
            Set(clone, "_mustBeWhiteListed", false);
            Set(clone, "_lockedFreeItem", false);
            Set(clone, "_silverCost", 0);
            Set(clone, "_dlcPackRequired", null);
            Set(clone, "_collaborativeResearchRequired", null);
            Set(clone, "_superBugVictoryRequired", null);
            Set(clone, "_canBePlacedIn", new[] { (RoomDefinition.Type)PowerPlantRoomRegistry.RoomTypeValue });
            Set(clone, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>());
            Set(clone, "_isSelectable", true);
            Set(clone, "_hasTooltip", true);
            Set(clone, "_canBePickedUp", true);
            Set(clone, "_canDragHoldSelect", true);
            Set(clone, "_canBeSold", true);
            return clone;
        }

        private static void DisableOwnInteractions(RoomItemDefinition item)
        {
            Set(item, "_interactions", Array.Empty<InteractionDefinition>());
            Set(item, "_minValidInteractions", 0);
        }

        private static void InstallDeskMaintenanceInteraction()
        {
            if (MarketingDesk?.Interactions == null) return;
            foreach (var interaction in MarketingDesk.Interactions)
                if (interaction != null && interaction.Name == "Maintenance")
                {
                    MaintenanceDeskInteraction = interaction;
                    return;
                }

            InteractionDefinition source = null;
            var bestScore = int.MinValue;
            foreach (var interaction in MarketingDesk.Interactions)
            {
                if (interaction == null) continue;
                var score = ScoreDeskInteraction(interaction);
                if (score > bestScore)
                {
                    source = interaction;
                    bestScore = score;
                }
            }
            if (source == null && MarketingDesk.Interactions.Length > 0)
                source = MarketingDesk.Interactions[0];
            if (source == null) return;

            var maintenance = (InteractionDefinition)MemberwiseCloneMethod.Invoke(source, null);
            maintenance.Name = "Maintenance";
            maintenance.Exclusive = true;
            maintenance.MaxQueue = 1;
            MaintenanceDeskInteraction = maintenance;
            var expanded = new InteractionDefinition[MarketingDesk.Interactions.Length + 1];
            Array.Copy(MarketingDesk.Interactions, expanded, MarketingDesk.Interactions.Length);
            expanded[expanded.Length - 1] = maintenance;
            MarketingDesk.Interactions = expanded;
            PowerGridPlugin.Log.LogInfo("Interaccion de teclado para la Sala de energia copiada de '" +
                                        (source.Name ?? "(sin nombre)") + "' (puntuacion " + bestScore + ").");
        }

        private static int ScoreDeskInteraction(InteractionDefinition interaction)
        {
            var score = ScoreDeskText(interaction.Name);
            foreach (var graph in interaction.AnimGraphs ?? Array.Empty<RuntimeAnimatorController>())
                if (graph != null) score += ScoreDeskText(graph.name) * 2;
            if (interaction.ObjectAnimGraph != null) score += ScoreDeskText(interaction.ObjectAnimGraph.name);
            if (interaction.DisableNavAgent) score += 2;
            return score;
        }

        private static int ScoreDeskText(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            var score = 0;
            if (Contains(value, "type") || Contains(value, "typing") || Contains(value, "keyboard")) score += 100;
            if (Contains(value, "computer") || Contains(value, "pc")) score += 80;
            if (Contains(value, "work")) score += 60;
            if (Contains(value, "marketing") || Contains(value, "marketeer")) score += 50;
            if (Contains(value, "desk")) score += 20;
            if (Contains(value, "sit")) score += 1;
            return score;
        }

        private static void FindExisting(SharedInstance<RoomItemDefinition>[] items)
        {
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item == null) continue;
                if (item.DebugTag == BatteryTag)
                {
                    Battery = item;
                    BatteryShared = shared as SharedInstance_TH20TH20_RoomItemDefinition;
                }
                else if (item.DebugTag == PanelTag)
                {
                    Panel = item;
                    PanelShared = shared as SharedInstance_TH20TH20_RoomItemDefinition;
                }
                else if (item.DebugTag == TransformerTag)
                {
                    Transformer = item;
                    TransformerShared = shared as SharedInstance_TH20TH20_RoomItemDefinition;
                }
            }
        }

        private static RoomItemDefinition FindMarketingDesk(RoomDefinition marketingRoom)
        {
            var required = marketingRoom?.GetRequiredItems();
            if (required == null) return null;
            foreach (var requirement in required)
                if (requirement?.Items != null)
                    foreach (var shared in requirement.Items)
                    {
                        var item = shared?.Instance;
                        if (item != null && (Contains(Identity(item), "marketing desk") ||
                                             (Contains(Identity(item), "marketing") && Contains(Identity(item), "desk"))))
                            return item;
                    }
            return null;
        }

        private static RoomItemDefinition FindCampaignMenuSource(SharedInstance<RoomItemDefinition>[] items)
        {
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item?.HoverMenuPrefab != null && item.SelectMenuPrefab != null &&
                    item.HoverMenuPrefab.GetComponent<HoverMenuMarketing>() != null &&
                    item.SelectMenuPrefab.GetComponent<SelectMenuMarketing>() != null)
                    return item;
            }
            throw new InvalidOperationException("No se ha encontrado la mesa original con los dos menus de campana.");
        }

        private static RoomItemDefinition FindPharmacyMachine(SharedInstance<RoomDefinition>[] rooms)
        {
            RoomDefinition pharmacy = null;
            foreach (var shared in rooms)
                if (shared?.Instance != null && shared.Instance._type == RoomDefinition.Type.Pharmacy)
                {
                    pharmacy = shared.Instance;
                    break;
                }
            var required = pharmacy?.GetRequiredItems();
            if (required == null) return null;
            foreach (var requirement in required)
                if (requirement != null && requirement.ContainsType(RoomItemDefinition.Type.Machine) && requirement.Items != null)
                    foreach (var shared in requirement.Items)
                        if (shared?.Instance != null) return shared.Instance;
            return null;
        }

        private static RoomItemDefinition FindBatteryVisual(Metagame metagame)
        {
            var landscape = metagame?.LandscapeItemDatabase?.Instance?.RoomItems;
            if (landscape == null) return null;
            foreach (var shared in landscape)
            {
                var item = shared?.Instance;
                if (item != null && (Contains(Identity(item), "grid battery pack v1") ||
                                     Contains(Identity(item), "a_grid_battery_pack_v1")))
                    return item;
            }
            return null;
        }

        private static RoomItemDefinition FindItem(SharedInstance<RoomItemDefinition>[] items, params string[] terms)
        {
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item == null) continue;
                var identity = Identity(item);
                foreach (var term in terms)
                    if (Contains(identity, term)) return item;
            }
            return null;
        }

        private static RoomItemDefinition FindWallItem(SharedInstance<RoomItemDefinition>[] items)
        {
            RoomItemDefinition fallback = null;
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item == null || !item.PlaceOnWall || !item.OccupyWallOnly) continue;
                if (fallback == null) fallback = item;
                var identity = Identity(item);
                if (Contains(identity, "poster") || Contains(identity, "picture") ||
                    Contains(identity, "painting")) return item;
            }
            return fallback;
        }

        private static void CopyWallPlacement(RoomItemDefinition target, RoomItemDefinition source)
        {
            Set(target, "_placeOnWall", source.PlaceOnWall);
            Set(target, "_occupyWallOnly", source.OccupyWallOnly);
            Set(target, "_allowOnCorner", source.AllowOnCorner);
            Set(target, "_gridSnap", source.GridSnap);
            Set(target, "_rotationSnap", source.RotationSnap);
            Set(target, "_defaultRotation", source.DefaultRotation);
            Set(target, "_wallMagnetism", source.WallMagnetism);
            Set(target, "_wallMagnetismRotation", source.WallMagnetismRotation);
            Set(target, "_wallMagnetismDistance", source.WallMagnetismDistance);
            Set(target, "_fixedWallPlacement", source.FixedWallPlacement);
        }

        private static string Identity(RoomItemDefinition item)
        {
            var prefab = item.GetPrefab(0);
            return (item.DebugTag ?? string.Empty) + " " + (item.GetSanitizedName() ?? string.Empty) + " " +
                   (prefab == null ? string.Empty : prefab.name);
        }

        private static bool Contains(string value, string term) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        internal static void AllowInEnergyRoom(RoomItemDefinition item)
        {
            if (item == null) return;
            var roomType = (RoomDefinition.Type)PowerPlantRoomRegistry.RoomTypeValue;
            var allowed = item.CanBePlacedInRoomTypes;
            if (allowed != null && allowed.Length > 0 && Array.IndexOf(allowed, roomType) < 0)
            {
                var expanded = new RoomDefinition.Type[allowed.Length + 1];
                Array.Copy(allowed, expanded, allowed.Length);
                expanded[expanded.Length - 1] = roomType;
                Set(item, "_canBePlacedIn", expanded);
            }
            var forbidden = item.CantBePlacedInRoomTypes;
            if (forbidden == null || Array.IndexOf(forbidden, roomType) < 0) return;
            var reduced = new List<RoomDefinition.Type>(forbidden);
            reduced.Remove(roomType);
            Set(item, "_cantBePlacedIn", reduced.ToArray());
        }

        private static SharedInstance_TH20TH20_RoomItemDefinition WrapperFor(RoomItemDefinition item)
        {
            if (ReferenceEquals(item, Battery)) return BatteryShared;
            if (ReferenceEquals(item, Panel)) return PanelShared;
            if (ReferenceEquals(item, Transformer)) return TransformerShared;
            return null;
        }

        private static SharedInstance_TH20TH20_RoomItemDefinition CreateWrapper(RoomItemDefinition item, int id,
            string objectName)
        {
            var wrapper = ScriptableObject.CreateInstance<SharedInstance_TH20TH20_RoomItemDefinition>();
            wrapper.name = objectName;
            wrapper.hideFlags = HideFlags.DontUnloadUnusedAsset;
            wrapper.ID = id;
            wrapper.Instance = item;
            return wrapper;
        }

        private static void Set(object target, string fieldName, object value)
        {
            var field = AccessTools.Field(target.GetType(), fieldName);
            if (field == null)
                PowerGridPlugin.Log.LogWarning($"Campo no encontrado: {target.GetType().Name}.{fieldName}");
            else
                field.SetValue(target, value);
        }
    }

    [HarmonyPatch(typeof(Level), "OnTimelineUpdated")]
    internal static class EnergyDailyConsumptionPatch
    {
        private static void Postfix(Level __instance)
        {
            if (__instance?.WorldState == null) return;
            PowerGridPrototype.ConsumeDailyMonthlyEnergy(__instance);
        }
    }

    // Room type 1000 is intentionally outside the game's fixed analytics arrays.
    // The gameplay supports it, but the optional monthly telemetry does not. Never
    // let that telemetry-only indexing error terminate the simulation.
    [HarmonyPatch(typeof(TH20.Analytics.LevelAnalyticsManager), "SendBatchedMonthlyRoomTypeSummaryData")]
    internal static class EnergyRoomAnalyticsGuardPatch
    {
        private static bool _reported;

        private static Exception Finalizer(Exception __exception)
        {
            if (!(__exception is IndexOutOfRangeException)) return __exception;
            if (!_reported)
            {
                _reported = true;
                PowerGridPlugin.Log.LogWarning("Omitida analitica mensual incompatible con el tipo de Sala de energia.");
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(ObjectInteraction), "StartInteraction")]
    internal static class EnergyDeskTypingAnimationPatch
    {
        private static readonly HashSet<string> LoggedControllers = new HashSet<string>();
        private static readonly Dictionary<ObjectInteraction, RuntimeAnimatorController> ForcedGraphs =
            new Dictionary<ObjectInteraction, RuntimeAnimatorController>();
        private static readonly Dictionary<ObjectInteraction, EnergyDeskTypingDriver> TypingDrivers =
            new Dictionary<ObjectInteraction, EnergyDeskTypingDriver>();
        private static readonly MethodInfo GetCharacterGraphMethod =
            AccessTools.Method(typeof(ObjectInteraction), "GetAnimGraphForCharacter");

        private static void Prefix(ObjectInteraction __instance, Character __0)
        {
            if (!IsEnergyDeskJanitorInteraction(__instance, __0)) return;
            // The native marketing desk controllers expose only Exit and LoopIndex.
            // Loop 0 is the seated idle seen in the test; inject loop 1 before the
            // controller is pushed so it starts in its keyboard-working loop.
            __instance.AddPendingVariable("LoopIndex", 1);
        }

        private static void Postfix(ObjectInteraction __instance, Character __0, bool __result)
        {
            if (!__result || !IsEnergyDeskJanitorInteraction(__instance, __0) || !(__0 is Staff staff)) return;
            var desk = __instance.ParentRoomItem;

            // The room behaviour can reserve the desk's idle sitting interaction. The cloned
            // maintenance interaction is selected by inspecting the real controller names, so
            // push its keyboard graph when it differs from the idle graph.
            var nativeGraph = GetCharacterGraph(__instance, staff);
            RuntimeAnimatorController typingGraph = null;
            foreach (var candidate in desk.Interactions)
            {
                if (candidate?.Definition != EnergyRoomItems.MaintenanceDeskInteraction) continue;
                typingGraph = GetCharacterGraph(candidate, staff);
                break;
            }
            if (typingGraph != null && typingGraph != nativeGraph)
            {
                staff.PushAnimationGraph(typingGraph, 0.25f, null);
                ForcedGraphs[__instance] = typingGraph;
            }

            var animator = staff.Animator;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            var controllerName = animator.runtimeAnimatorController.name ?? "(sin nombre)";
            var parameterSummary = new List<string>();
            foreach (var parameter in animator.parameters)
            {
                parameterSummary.Add(parameter.name + ":" + parameter.type);
                var name = parameter.name ?? string.Empty;
                if (string.Equals(name, "LoopIndex", StringComparison.OrdinalIgnoreCase) &&
                    parameter.type == AnimatorControllerParameterType.Int)
                    animator.SetInteger(name, 1);
                var workParameter = ContainsToken(name, "work") || ContainsToken(name, "type") ||
                                    ContainsToken(name, "keyboard") || ContainsToken(name, "action") ||
                                    ContainsToken(name, "interact") || ContainsToken(name, "use");
                if (!workParameter) continue;
                if (parameter.type == AnimatorControllerParameterType.Bool)
                    __instance.SetBool(name, true);
                else if (parameter.type == AnimatorControllerParameterType.Trigger)
                    __instance.SetTrigger(name);
                else if (parameter.type == AnimatorControllerParameterType.Int)
                    animator.SetInteger(name, 1);
            }
            var host = staff.Visual?.CharacterGameObject;
            if (host != null)
            {
                var driver = host.AddComponent<EnergyDeskTypingDriver>();
                driver.Initialise(staff, __instance);
                TypingDrivers[__instance] = driver;
            }
            if (LoggedControllers.Add(controllerName))
            {
                var clips = animator.runtimeAnimatorController.animationClips;
                var clipNames = new List<string>();
                foreach (var clip in clips ?? Array.Empty<AnimationClip>())
                    if (clip != null) clipNames.Add(clip.name);
                PowerGridPlugin.Log.LogInfo("Escritorio de energia usa el controlador nativo '" + controllerName +
                                            "' con parametros: " + string.Join(", ", parameterSummary.ToArray()) +
                                            "; clips: " + string.Join(", ", clipNames.ToArray()));
            }
        }

        private static bool ContainsToken(string value, string token) =>
            value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static RuntimeAnimatorController GetCharacterGraph(ObjectInteraction interaction, Character character) =>
            GetCharacterGraphMethod?.Invoke(interaction, new object[] { character }) as RuntimeAnimatorController;

        private static bool IsEnergyDeskJanitorInteraction(ObjectInteraction interaction, Character character)
        {
            if (!(character is Staff staff) || staff.Definition == null ||
                staff.Definition._type != StaffDefinition.Type.Janitor) return false;
            var desk = interaction?.ParentRoomItem;
            return desk != null && ReferenceEquals(desk.Definition, EnergyRoomItems.MarketingDesk) &&
                   desk.OwningRoom != null && PowerPlantRoomRegistry.IsPowerPlant(desk.OwningRoom.Definition);
        }

        internal static void BeforeInteractionEnds(ObjectInteraction interaction, Character character)
        {
            if (interaction != null && TypingDrivers.TryGetValue(interaction, out var driver))
            {
                TypingDrivers.Remove(interaction);
                if (driver != null) UnityEngine.Object.Destroy(driver);
            }
            if (interaction == null || character == null || !ForcedGraphs.TryGetValue(interaction, out var graph))
                return;
            ForcedGraphs.Remove(interaction);
            character.PopAnimationGraph(graph, 0.25f, false);
        }
    }

    internal sealed class EnergyDeskTypingDriver : MonoBehaviour
    {
        private Staff _staff;
        private ObjectInteraction _interaction;

        internal void Initialise(Staff staff, ObjectInteraction interaction)
        {
            _staff = staff;
            _interaction = interaction;
        }

        private void LateUpdate()
        {
            if (_staff == null || _interaction == null || _staff.Interaction != _interaction)
            {
                Destroy(this);
                return;
            }

            // The interaction controller re-applies its default seated loop after
            // StartInteraction. Hold the native Marketing desk's typing loop for the
            // lifetime of this work interaction instead of setting it only once.
            var animator = _staff.Animator;
            if (animator == null) return;
            foreach (var parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Int &&
                    string.Equals(parameter.name, "LoopIndex", StringComparison.OrdinalIgnoreCase))
                    animator.SetInteger(parameter.name, 1);
        }
    }

    [HarmonyPatch(typeof(ObjectInteraction), "EndInteractionInner")]
    internal static class EnergyDeskExitNavigationPatch
    {
        private static void Prefix(ObjectInteraction __instance, Character __0)
        {
            EnergyDeskTypingAnimationPatch.BeforeInteractionEnds(__instance, __0);
        }

        private static void Postfix(ObjectInteraction __instance, Character __0)
        {
            if (!(__0 is Staff staff) || staff.Definition == null ||
                staff.Definition._type != StaffDefinition.Type.Janitor) return;
            var desk = __instance?.ParentRoomItem;
            if (desk == null || !ReferenceEquals(desk.Definition, EnergyRoomItems.MarketingDesk) ||
                desk.OwningRoom == null || !PowerPlantRoomRegistry.IsPowerPlant(desk.OwningRoom.Definition)) return;

            // Leave the chair on the same reachable socket used to enter it, then restore the
            // navigation agent even when the source interaction did not request that itself.
            staff.Position = __instance.WorldStartPosition;
            staff.NavPath.PutBackInNavWorld();
            staff.NavPath.StopBeingKinematic();
        }
    }
}
