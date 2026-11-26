using System;
using System.Collections.Generic;
using System.Reflection;
using BehaviorDesigner.Runtime.Tasks;
using FullInspector;
using FullInspector.Generated.SharedInstance;
using FullSerializerSave;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderPressure.Storey
{
    internal static class ElevatorDefinition
    {
        internal const string DebugTag = "under pressure storey elevator";
        internal const string InteractionName = "UnderPressureElevator";
        private const string SocketName = "UP_ELEVATOR_START";
        private const int SharedId = 9210101;
        private static readonly Guid DefinitionGuid =
            new Guid("8f67d688-e313-48b4-95cb-e0df4716f101");
        private static readonly MethodInfo MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");

        internal static RoomItemDefinition Definition { get; private set; }
        internal static SharedInstance_TH20TH20_RoomItemDefinition Shared { get; private set; }

        internal static bool Ensure(Metagame metagame)
        {
            var database = metagame?.RoomItemDatabase?.Instance;
            if (database?.RoomItems == null) return false;
            FindExisting(database.RoomItems);
            if (Definition == null)
            {
                var source = FindSource(database.RoomItems);
                if (source == null)
                {
                    StoreyPlugin.Log?.LogError(
                        "No se encontró el archivador nativo usado como base estructural del ascensor.");
                    return false;
                }

                Definition = (RoomItemDefinition)MemberwiseCloneMethod.Invoke(source, null);
                var prefab = BuildPlaceholderPrefab();
                var interaction = new InteractionDefinition
                {
                    Type = InteractionAttributeModifier.Type.Use,
                    Name = InteractionName,
                    Exclusive = true,
                    MaxQueue = 8,
                    Sockets = new[] { SocketName },
                    ParticleEffects = Array.Empty<string>(),
                    GlobalParticleEffects = Array.Empty<string>(),
                    AnimGraphs = Array.Empty<RuntimeAnimatorController>(),
                    AnimGraphsAlternate = Array.Empty<RuntimeAnimatorController>(),
                    ObjectAnimGraphEx = Array.Empty<RuntimeAnimatorController>(),
                    CanInterrupt = true,
                    IgnoreRoomCheck = true,
                    DisableLookAt = true,
                    DisableNavAgent = false,
                    IncludeInBounds = false,
                    IgnoreStartRotation = false
                };

                Set(Definition, "_guid", DefinitionGuid);
                Set(Definition, "_debugTag", DebugTag);
                Set(Definition, "_localisedName",
                    global::UnderPressure.ModLocalization.Create("storey.elevator.name"));
                var description = global::UnderPressure.ModLocalization.Create(
                    "storey.elevator.description");
                Set(Definition, "_localisedDescription", description);
                Set(Definition, "_functionalDescription", description);
                Set(Definition, "_prefab", prefab);
                Set(Definition, "_blueprintPrefab", prefab);
                Set(Definition, "_cost", 0);
                Set(Definition, "_energyCost", 0);
                Set(Definition, "_initiallyAvailable", true);
                Set(Definition, "_mustBeWhiteListed", false);
                Set(Definition, "_lockedFreeItem", false);
                Set(Definition, "_silverCost", 0);
                Set(Definition, "_saveInRoomLayout", true);
                Set(Definition, "_prestige", 0f);
                Set(Definition, "_hospitalLevelPoints", 0f);
                Set(Definition, "_placeOnWall", false);
                Set(Definition, "_occupyWallOnly", false);
                Set(Definition, "_allowOnCorner", true);
                Set(Definition, "_gridSnap", 1f);
                Set(Definition, "_rotationSnap", 90f);
                Set(Definition, "_defaultRotation", 0f);
                Set(Definition, "_wallMagnetism", false);
                Set(Definition, "_singlePlace", false);
                Set(Definition, "_hasCollision", true);
                Set(Definition, "_useVerticalCollision", false);
                Set(Definition, "_collideWithSameType", true);
                Set(Definition, "_collideWithRugs", true);
                Set(Definition, "_moveOutOfWay", false);
                Set(Definition, "_ignoreValidation", false);
                Set(Definition, "_isSelectable", true);
                Set(Definition, "_hasTooltip", true);
                Set(Definition, "_showQueuePositions", true);
                Set(Definition, "_showStatusIcon", false);
                Set(Definition, "_affectsNavigation", true);
                Set(Definition, "_removeWalls", false);
                Set(Definition, "_canBePlacedIn", Array.Empty<RoomDefinition.Type>());
                Set(Definition, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>());
                Set(Definition, "_attributes", Array.Empty<ObjectAttributes.Definition>());
                Set(Definition, "_maintenanceModifer", 0f);
                Set(Definition, "_ignoredByJanitors", true);
                Set(Definition, "_roomModifiers", Array.Empty<RoomModifier>());
                Set(Definition, "_interactionAttributeModifiers",
                    Array.Empty<InteractionAttributeModifier>());
                Set(Definition, "_upgrades",
                    Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>());
                Set(Definition, "_singleInteractor", true);
                Set(Definition, "_interactionsAlwayAnimate", false);
                Set(Definition, "_minValidInteractions", 1);
                Set(Definition, "_interactions", new[] { interaction });
                Set(Definition, "_filters", Array.Empty<RoomItemFilter>());
                Set(Definition, "_components", Array.Empty<EntityComponent>());
                Set(Definition, "_canBePickedUp", true);
                Set(Definition, "_canDragHoldSelect", true);
                Set(Definition, "_canBeSold", true);
                Set(Definition, "_generatesElectricity", false);

                Shared = ScriptableObject.CreateInstance<
                    SharedInstance_TH20TH20_RoomItemDefinition>();
                Shared.name = "UnderPressure Storey Elevator";
                Shared.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Shared.ID = SharedId;
                Shared.Instance = Definition;

                var expanded = new SharedInstance<RoomItemDefinition>[
                    database.RoomItems.Length + 1];
                Array.Copy(database.RoomItems, expanded, database.RoomItems.Length);
                expanded[expanded.Length - 1] = Shared;
                database.RoomItems = expanded;
                StoreyPlugin.Log?.LogInfo("Ascensor de plantas registrado como RoomItem nativo.");
            }

            RegisterSaveAssets(metagame);
            return true;
        }

        internal static void AddToWorld(WorldState world)
        {
            if (world == null || Definition == null) return;
            if (!world.AvailableRoomItems.Contains(Definition))
                world.AvailableRoomItems.Add(Definition);
        }

        internal static bool IsElevator(IRoomItemDefinition definition)
        {
            var item = definition as RoomItemDefinition;
            return item != null && (ReferenceEquals(item, Definition) ||
                                    item.DebugTag == DebugTag);
        }

        internal static bool IsElevator(RoomItem item) =>
            item != null && IsElevator(item.Definition);

        internal static bool IsElevator(ObjectInteraction interaction) =>
            interaction != null && IsElevator(interaction.ParentRoomItem) &&
            interaction.Name == InteractionName;

        private static void FindExisting(SharedInstance<RoomItemDefinition>[] items)
        {
            foreach (var shared in items)
            {
                var definition = shared?.Instance;
                if (definition == null || definition.DebugTag != DebugTag) continue;
                Definition = definition;
                Shared = shared as SharedInstance_TH20TH20_RoomItemDefinition;
                return;
            }
        }

        private static RoomItemDefinition FindSource(
            SharedInstance<RoomItemDefinition>[] items)
        {
            foreach (var shared in items)
            {
                var item = shared?.Instance;
                if (item == null) continue;
                var identity = (item.DebugTag ?? string.Empty) + " " +
                               (item.GetSanitizedName() ?? string.Empty) + " " +
                               (item.GetPrefab(0)?.name ?? string.Empty);
                if (identity.IndexOf("filing cabinet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    identity.IndexOf("archivador", StringComparison.OrdinalIgnoreCase) >= 0)
                    return item;
            }
            return null;
        }

        private static GameObject BuildPlaceholderPrefab()
        {
            var root = new GameObject("UP Storey Elevator Placeholder");
            root.hideFlags = HideFlags.HideAndDontSave;
            var dark = CreateMaterial("UP Elevator Dark", new Color(.16f, .18f, .2f));
            var arrow = CreateMaterial("UP Elevator Arrow", new Color(.86f, .88f, .9f));

            AddCube(root.transform, "Plataforma", new Vector3(0f, .04f, 0f),
                new Vector3(1.8f, .08f, 1.8f), dark, true);
            AddCube(root.transform, "Pared trasera", new Vector3(0f, .85f, .82f),
                new Vector3(1.8f, 1.7f, .16f), dark, true);
            AddCube(root.transform, "Flecha - eje", new Vector3(0f, .095f, -.05f),
                new Vector3(.14f, .025f, .72f), arrow, false);
            AddCube(root.transform, "Flecha - izquierda", new Vector3(-.18f, .095f, -.42f),
                new Vector3(.14f, .025f, .48f), arrow, false, -45f);
            AddCube(root.transform, "Flecha - derecha", new Vector3(.18f, .095f, -.42f),
                new Vector3(.14f, .025f, .48f), arrow, false, 45f);

            var socket = new GameObject(SocketName);
            socket.transform.SetParent(root.transform, false);
            socket.transform.localPosition = new Vector3(0f, 0f, -1.1f);
            socket.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            // El objeto original actúa como prefab en memoria. Se mantiene activo para que
            // las copias nativas también nazcan activas, pero fuera del mundo visible.
            root.transform.position = new Vector3(0f, -10000f, 0f);
            root.SetActive(true);
            UnityEngine.Object.DontDestroyOnLoad(root);
            return root;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var material = new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(material);
            return material;
        }

        private static void AddCube(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material, bool collision, float yaw = 0f)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            cube.transform.localScale = scale;
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            if (!collision) UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
        }

        private static void RegisterSaveAssets(Metagame metagame)
        {
            var saveSystem = metagame?.App?.SaveSystem;
            var mapping = saveSystem == null ? null :
                AccessTools.Field(typeof(SaveSystem), "_assetIDs")?.GetValue(saveSystem)
                    as BiDictionary<int, object>;
            if (mapping == null || Shared == null || Definition == null) return;
            var added = Register(mapping, SharedId, Shared) +
                        Register(mapping, SharedId - 1, Definition);
            if (added == 0) return;
            foreach (var fieldName in new[]
                     { "_serializerLevel", "_serializerMetagame", "_serializerRoomTemplates" })
            {
                var serializer = AccessTools.Field(typeof(SaveSystem), fieldName)?
                    .GetValue(saveSystem) as fsSerializer;
                serializer?.SetIDObjectMapping(mapping.FirstToSecond, mapping.SecondToFirst);
            }
        }

        private static int Register(BiDictionary<int, object> mapping, int id, object value)
        {
            if (mapping.ContainsValue(value)) return 0;
            if (mapping.ContainsKey(id))
            {
                StoreyPlugin.Log?.LogError(
                    "El ID de guardado " + id + " del ascensor ya está ocupado.");
                return 0;
            }
            mapping.Add(id, value);
            return 1;
        }

        private static void Set(object target, string fieldName, object value)
        {
            var field = AccessTools.Field(target.GetType(), fieldName);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, fieldName);
            field.SetValue(target, value);
        }
    }

    internal sealed class ElevatorRouteDriver : MonoBehaviour
    {
        private void Update() => ElevatorRuntime.TickRoutes();
    }

    internal static class ElevatorRuntime
    {
        private sealed class RouteRequest
        {
            internal Character Character;
            internal NavPath NavPath;
            internal RoomItem Source;
            internal RoomItem Target;
            internal InteractionController Controller;
            internal Vector3 Destination;
            internal float Rotation;
            internal INavPathResult Callback;
            internal float ArrivalDistance;
            internal bool Transferred;
        }

        private static readonly Dictionary<RoomItem, RoomItem> Pairs =
            new Dictionary<RoomItem, RoomItem>();
        private static readonly Dictionary<Character, RouteRequest> Routes =
            new Dictionary<Character, RouteRequest>();
        private static readonly HashSet<Character> InternalNavigation =
            new HashSet<Character>();
        private static readonly FieldInfo NavPathCharacter =
            AccessTools.Field(typeof(NavPath), "_character");
        private static readonly MethodInfo DestroyRoomItem =
            AccessTools.Method(typeof(BuildEvents), "RoomItemDestroy");
        private static readonly LocalisedString InvalidOtherStorey =
            global::UnderPressure.ModLocalization.Create(
                "storey.elevator.invalid_other_storey");

        private static Level _level;
        private static StoreyInstance _lower;
        private static StoreyInstance _upper;
        private static bool _pairedOperation;
        private static ElevatorRouteDriver _driver;

        internal static void Initialise(Level level, StoreyInstance lower,
            StoreyInstance upper)
        {
            Destroy();
            _level = level;
            _lower = lower;
            _upper = upper;
            if (!ElevatorDefinition.Ensure(level?.App?.Metagame)) return;
            ElevatorDefinition.AddToWorld(lower?.State);
            ElevatorDefinition.AddToWorld(upper?.State);
            Bind(lower?.Events, true);
            Bind(upper?.Events, true);
            if (lower?.Root != null)
                _driver = lower.Root.AddComponent<ElevatorRouteDriver>();
            RebuildPairs();
        }

        internal static void Destroy()
        {
            Bind(_lower?.Events, false);
            Bind(_upper?.Events, false);
            foreach (var route in new List<RouteRequest>(Routes.Values))
            {
                route.Controller?.Destroy();
                route.Controller = null;
            }
            Routes.Clear();
            InternalNavigation.Clear();
            Pairs.Clear();
            if (_driver != null) UnityEngine.Object.Destroy(_driver);
            _driver = null;
            _level = null;
            _lower = null;
            _upper = null;
            _pairedOperation = false;
        }

        private static void Bind(BuildEvents events, bool bind)
        {
            if (events == null) return;
            if (bind)
            {
                events.OnRoomItemPlaced += OnRoomItemPlaced;
                events.OnRoomItemDestroyed += OnRoomItemDestroyed;
            }
            else
            {
                events.OnRoomItemPlaced -= OnRoomItemPlaced;
                events.OnRoomItemDestroyed -= OnRoomItemDestroyed;
            }
        }

        internal static bool ValidateCounterpart(RoomItem item)
        {
            if (!ElevatorDefinition.IsElevator(item)) return true;
            if (!TryGetTarget(item, out var targetStorey, out var targetRoom,
                    out var targetLocal))
            {
                item.SetValid(false, "Other storey is unavailable",
                    InvalidOtherStorey.ToString());
                return false;
            }

            var collisions = targetRoom.FloorPlan.GetCollisionItemsAtCoord(targetLocal);
            if (collisions == null) return true;
            Pairs.TryGetValue(item, out var existingPair);
            foreach (var collision in collisions)
                if (collision != null && !ReferenceEquals(collision, existingPair))
                {
                    item.SetValid(false, "Other storey tile is occupied",
                        InvalidOtherStorey.ToString());
                    return false;
                }
            return targetStorey != null;
        }

        private static bool TryGetTarget(RoomItem source, out StoreyInstance targetStorey,
            out Room targetRoom, out GridCoord targetLocal)
        {
            targetStorey = StoreyRuntime.GetOtherStorey(StoreyRuntime.GetStorey(source));
            targetRoom = null;
            targetLocal = default(GridCoord);
            if (source?.FloorPlan == null || targetStorey?.State == null) return false;
            var worldCoord = source.FloorPlan.Anchor + source.LocalCoord;
            var map = targetStorey.State.GetHospitalMapAtWorldPosition(worldCoord);
            if (map?.Plot == null || !map.Plot.Bought || !map.Plot.Built) return false;
            targetRoom = targetStorey.State.GetRoomAtWorldCoord(worldCoord, true, true);
            if (targetRoom?.FloorPlan == null) return false;
            targetLocal = worldCoord - targetRoom.FloorPlan.Anchor;
            try
            {
                if (!targetRoom.FloorPlan[targetLocal]) return false;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
            return true;
        }

        private static void OnRoomItemPlaced(RoomItem item, FloorPlan plan)
        {
            if (_pairedOperation || !ElevatorDefinition.IsElevator(item)) return;
            if (!ValidateCounterpart(item))
            {
                DestroyItem(item);
                return;
            }

            Pairs.TryGetValue(item, out var oldPair);
            _pairedOperation = true;
            try
            {
                if (oldPair != null)
                {
                    Unlink(item);
                    DestroyItem(oldPair);
                }
                if (!TryGetTarget(item, out var targetStorey, out var targetRoom,
                        out _)) return;
                RoomItem counterpart = null;
                StoreyRuntime.RunInStorey(targetStorey, () =>
                {
                    var sourcePosition = item.WorldPosition;
                    counterpart = RoomItemAlgorithms.SpawnItem(
                        ElevatorDefinition.Definition,
                        new Vector3(sourcePosition.x, 0f, sourcePosition.z),
                        0f, item.Rotation, _level, targetRoom);
                    if (counterpart == null) return;
                    counterpart.HasBeenPurchased = true;
                    if (targetRoom.IsOpen)
                        targetStorey.State.AddNeedSatisfyingRoomItem(counterpart);
                    targetStorey.Events.OnRoomItemPlaced.InvokeSafe(
                        counterpart, targetRoom.FloorPlan);
                });
                if (counterpart == null)
                {
                    StoreyPlugin.Log?.LogError(
                        "La validación nativa impidió crear la pareja del ascensor; se cancela la colocación.");
                    DestroyItem(item);
                    return;
                }
                Link(item, counterpart);
            }
            finally
            {
                _pairedOperation = false;
            }
        }

        private static void OnRoomItemDestroyed(RoomItem item)
        {
            if (_pairedOperation || !ElevatorDefinition.IsElevator(item) ||
                !Pairs.TryGetValue(item, out var pair)) return;
            _pairedOperation = true;
            try
            {
                Unlink(item);
                DestroyItem(pair);
            }
            finally
            {
                _pairedOperation = false;
            }
        }

        private static void DestroyItem(RoomItem item)
        {
            if (item == null || item.HasBeenDestroyed()) return;
            var storey = StoreyRuntime.GetStorey(item);
            StoreyRuntime.RunInStorey(storey, () =>
                DestroyRoomItem.Invoke(storey.Events, new object[] { item }));
        }

        private static void Link(RoomItem first, RoomItem second)
        {
            if (first == null || second == null) return;
            Pairs[first] = second;
            Pairs[second] = first;
        }

        private static void Unlink(RoomItem item)
        {
            if (item == null || !Pairs.TryGetValue(item, out var pair)) return;
            Pairs.Remove(item);
            Pairs.Remove(pair);
        }

        private static void RebuildPairs()
        {
            Pairs.Clear();
            if (ElevatorDefinition.Definition == null) return;
            var lowerItems = _lower?.State?.GetRoomItemsOfType(ElevatorDefinition.Definition) ??
                             new List<RoomItem>();
            var upperItems = _upper?.State?.GetRoomItemsOfType(ElevatorDefinition.Definition) ??
                             new List<RoomItem>();
            var used = new HashSet<RoomItem>();
            foreach (var lower in lowerItems)
            {
                RoomItem match = null;
                foreach (var upper in upperItems)
                {
                    if (used.Contains(upper)) continue;
                    var a = lower.WorldPosition;
                    var b = upper.WorldPosition;
                    if (Mathf.Abs(a.x - b.x) > .05f || Mathf.Abs(a.z - b.z) > .05f ||
                        Mathf.Abs(Mathf.DeltaAngle(lower.Rotation, upper.Rotation)) > .1f) continue;
                    match = upper;
                    break;
                }
                if (match == null) continue;
                used.Add(match);
                Link(lower, match);
            }
        }

        internal static bool TryRoute(NavPath path, Vector3 destination, float rotation,
            INavPathResult callback, float arrivalDistance)
        {
            var character = NavPathCharacter.GetValue(path) as Character;
            if (character == null || InternalNavigation.Contains(character) || Pairs.Count == 0)
                return false;
            var sourceStorey = character.Position.y >=
                               global::UnderPressure.StoreyMapRegistry.StoreyHeight * .5f
                ? _upper : _lower;
            var targetStorey = destination.y >=
                               global::UnderPressure.StoreyMapRegistry.StoreyHeight * .5f
                ? _upper : _lower;
            if (sourceStorey == null || targetStorey == null ||
                ReferenceEquals(sourceStorey, targetStorey)) return false;

            RoomItem source = null;
            RoomItem target = null;
            var best = float.MaxValue;
            foreach (var pair in Pairs)
            {
                if (!ReferenceEquals(StoreyRuntime.GetStorey(pair.Key), sourceStorey) ||
                    !ReferenceEquals(StoreyRuntime.GetStorey(pair.Value), targetStorey)) continue;
                var sourceInteraction = FirstInteraction(pair.Key);
                var targetInteraction = FirstInteraction(pair.Value);
                if (sourceInteraction == null || targetInteraction == null) continue;
                var score = HorizontalDistance(character.Position,
                                sourceInteraction.WorldStartPosition) +
                            HorizontalDistance(destination, targetInteraction.WorldStartPosition);
                if (score >= best) continue;
                best = score;
                source = pair.Key;
                target = pair.Value;
            }
            if (source == null || target == null) return false;

            if (Routes.TryGetValue(character, out var previous))
                CancelRoute(previous, EPathStatus.Interrupted);
            var request = new RouteRequest
            {
                Character = character,
                NavPath = path,
                Source = source,
                Target = target,
                Destination = destination,
                Rotation = rotation,
                Callback = callback,
                ArrivalDistance = arrivalDistance
            };
            Routes[character] = request;
            InternalNavigation.Add(character);
            try
            {
                request.Controller = new InteractionController(
                    character, FirstInteraction(source), true);
            }
            finally
            {
                InternalNavigation.Remove(character);
            }
            return true;
        }

        internal static void TickRoutes()
        {
            if (Routes.Count == 0) return;
            foreach (var request in new List<RouteRequest>(Routes.Values))
            {
                if (request.Character == null || request.Character.HasBeenDestroyed() ||
                    request.Controller == null)
                {
                    CancelRoute(request, EPathStatus.Failure);
                    continue;
                }
                TaskStatus status;
                InternalNavigation.Add(request.Character);
                try
                {
                    status = request.Controller.OnUpdate();
                }
                finally
                {
                    InternalNavigation.Remove(request.Character);
                }
                if (status == TaskStatus.Running) continue;
                if (status != TaskStatus.Success || !request.Transferred)
                {
                    CancelRoute(request, EPathStatus.Failure);
                    continue;
                }
                Routes.Remove(request.Character);
                request.Controller.Destroy();
                request.Controller = null;
                InternalNavigation.Add(request.Character);
                try
                {
                    request.NavPath.MoveTo(request.Destination, request.Rotation,
                        request.Callback, request.ArrivalDistance);
                }
                finally
                {
                    InternalNavigation.Remove(request.Character);
                }
            }
        }

        internal static void OnInteractionStarted(ObjectInteraction interaction,
            Character character, bool started)
        {
            if (!started || !ElevatorDefinition.IsElevator(interaction) ||
                character == null || !Routes.TryGetValue(character, out var request) ||
                !ReferenceEquals(interaction.ParentRoomItem, request.Source)) return;
            var targetInteraction = FirstInteraction(request.Target);
            if (targetInteraction == null) return;
            var target = targetInteraction.WorldStartPosition;
            character.NavPath.Warp(target);
            character.Position = target;
            character.Rotation = targetInteraction.StartRotation;
            request.Transferred = true;
        }

        private static void CancelRoute(RouteRequest request, EPathStatus status)
        {
            if (request == null) return;
            if (request.Character != null) Routes.Remove(request.Character);
            request.Controller?.Destroy();
            request.Controller = null;
            request.Callback?.OnPathComplete(status);
        }

        private static ObjectInteraction FirstInteraction(RoomItem item)
        {
            if (item?.Interactions == null) return null;
            foreach (var interaction in item.Interactions)
                if (ElevatorDefinition.IsElevator(interaction)) return interaction;
            return null;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }
    }

    [HarmonyPatch(typeof(Metagame), "RestoreFromSave")]
    internal static class ElevatorMetagameRestorePatch
    {
        private static void Postfix(Metagame __instance) =>
            ElevatorDefinition.Ensure(__instance);
    }

    [HarmonyPatch(typeof(WorldState), MethodType.Constructor,
        new[] { typeof(WorldState.Config), typeof(Level), typeof(Metagame),
            typeof(VisualManager), typeof(Material), typeof(RoomItemVisualEdit.Config) })]
    internal static class ElevatorWorldStateConstructorPatch
    {
        private static void Postfix(WorldState __instance, Metagame __2)
        {
            ElevatorDefinition.Ensure(__2);
            ElevatorDefinition.AddToWorld(__instance);
        }
    }

    [HarmonyPatch(typeof(WorldState), "RestoreFromSave")]
    internal static class ElevatorWorldStateRestorePatch
    {
        private static void Postfix(WorldState __instance) =>
            ElevatorDefinition.AddToWorld(__instance);
    }

    [HarmonyPatch(typeof(CursorRoomItem), "ValidatePlacement")]
    internal static class ElevatorPlacementValidationPatch
    {
        private static readonly FieldInfo Item =
            AccessTools.Field(typeof(CursorRoomItem), "_roomItem");

        private static void Postfix(CursorRoomItem __instance, ref bool __result)
        {
            var item = Item.GetValue(__instance) as RoomItem;
            if (!ElevatorDefinition.IsElevator(item)) return;
            __result = __result && ElevatorRuntime.ValidateCounterpart(item);
        }
    }

    [HarmonyPatch(typeof(ObjectInteraction), "StartInteraction")]
    internal static class ElevatorInteractionPatch
    {
        private static void Postfix(ObjectInteraction __instance, Character __0,
            bool __result) =>
            ElevatorRuntime.OnInteractionStarted(__instance, __0, __result);
    }

    [HarmonyPatch(typeof(NavPath), "MoveTo",
        new[] { typeof(Vector3), typeof(float), typeof(INavPathResult), typeof(float) })]
    internal static class ElevatorNavigationPatch
    {
        private static bool Prefix(NavPath __instance, Vector3 __0, float __1,
            INavPathResult __2, float __3) =>
            !ElevatorRuntime.TryRoute(__instance, __0, __1, __2, __3);
    }
}
