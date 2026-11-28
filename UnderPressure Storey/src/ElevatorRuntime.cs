using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly Dictionary<Character, StoreyInstance> CharacterStoreys =
            new Dictionary<Character, StoreyInstance>();
        private static readonly Dictionary<RoomItem, StoreyInstance> ElevatorStoreys =
            new Dictionary<RoomItem, StoreyInstance>();
        private static readonly HashSet<Character> RouteFailureLogged =
            new HashSet<Character>();
        private static readonly FieldInfo NavPathCharacter =
            AccessTools.Field(typeof(NavPath), "_character");
        private static readonly FieldInfo RoomBuildingNavMeshHospitalMap =
            AccessTools.Field(typeof(RoomBuildingNavMesh), "_hospitalMap");
        private static readonly FieldInfo InteractionStartSocket =
            AccessTools.Field(typeof(ObjectInteraction), "_startSocket");
        private static readonly FieldInfo MoveToTargetObject =
            AccessTools.Field(typeof(TH20.BTA.MoveTo), "_targetObject");
        private static readonly FieldInfo NavMeshCanReachPath =
            AccessTools.Field(typeof(TH20.NavMesh), "_canReachPath");
        private static readonly FieldInfo NavMeshWorldBounds =
            AccessTools.Field(typeof(TH20.NavMesh), "_worldNavBounds");
        private static readonly FieldInfo NavMeshAreaLookup =
            AccessTools.Field(typeof(TH20.NavMesh), "_areaLookup");
        private static readonly MethodInfo BuildNavMeshAreaLookup =
            AccessTools.Method(typeof(NavMeshHelpers), "BuildNavMeshAreaLookup");
        private static readonly MethodInfo DestroyRoomItem =
            AccessTools.Method(typeof(BuildEvents), "RoomItemDestroy");
        private static readonly MethodInfo StartItemPlacement =
            AccessTools.Method(typeof(BuildEvents), "StartItemPlacement");
        private static readonly LocalisedString InvalidOtherStorey =
            global::UnderPressure.ModLocalization.Create(
                "storey.elevator.invalid_other_storey");
        private static readonly LocalisedString ElevatorRequired =
            global::UnderPressure.ModLocalization.Create(
                "storey.elevator.required");
        private static readonly LocalisedString LastElevatorRequired =
            global::UnderPressure.ModLocalization.Create(
                "storey.elevator.last_required");
        private static readonly List<HospitalPlot> RequiredPlots =
            new List<HospitalPlot>();
        private static readonly HashSet<HospitalPlot> RequiredPlotNotified =
            new HashSet<HospitalPlot>();

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
            if (!ReferenceEquals(upper?.Events, lower?.Events)) Bind(upper?.Events, true);
            if (lower?.Root != null)
                _driver = lower.Root.AddComponent<ElevatorRouteDriver>();
            RebuildPairs();
            QueueMissingRequiredPlots();
            StoreyPlugin.Log?.LogInfo(
                "Ascensores inicializados sobre dos WorldState; parejas restauradas=" +
                (Pairs.Count / 2) + ".");
        }

        internal static void Destroy()
        {
            Bind(_lower?.Events, false);
            if (!ReferenceEquals(_upper?.Events, _lower?.Events)) Bind(_upper?.Events, false);
            foreach (var route in new List<RouteRequest>(Routes.Values))
            {
                route.Controller?.Destroy();
                route.Controller = null;
            }
            Routes.Clear();
            InternalNavigation.Clear();
            CharacterStoreys.Clear();
            ElevatorStoreys.Clear();
            RouteFailureLogged.Clear();
            Pairs.Clear();
            RequiredPlots.Clear();
            RequiredPlotNotified.Clear();
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
                events.OnHospitalPlotBuilt += OnHospitalPlotBuilt;
            }
            else
            {
                events.OnRoomItemPlaced -= OnRoomItemPlaced;
                events.OnRoomItemDestroyed -= OnRoomItemDestroyed;
                events.OnHospitalPlotBuilt -= OnHospitalPlotBuilt;
            }
        }

        private static void OnHospitalPlotBuilt(HospitalPlot plot) =>
            QueueRequiredPlot(plot);

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
            var sourceStorey = StoreyRuntime.GetStorey(item);
            StoreyPlugin.Log?.LogInfo(
                "Colocación de ascensor recibida en planta " +
                (ReferenceEquals(sourceStorey, _upper) ? "superior" : "inferior") +
                ", posición=" + item.WorldPosition + ".");
            if (!ValidateCounterpart(item))
            {
                StoreyPlugin.Log?.LogWarning(
                    "Ascensor rechazado: la misma celda no es válida en el otro WorldState.");
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
                Link(item, sourceStorey, counterpart, targetStorey);
                ResolveRequiredPlots();
                StoreyPlugin.Log?.LogInfo(
                    "Pareja de ascensores creada entre ambos WorldState en posición XZ=" +
                    item.WorldPosition.x.ToString("F2") + "," +
                    item.WorldPosition.z.ToString("F2") + ".");
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

        private static void Link(RoomItem first, StoreyInstance firstStorey,
            RoomItem second, StoreyInstance secondStorey)
        {
            if (first == null || second == null) return;
            Pairs[first] = second;
            Pairs[second] = first;
            ElevatorStoreys[first] = firstStorey;
            ElevatorStoreys[second] = secondStorey;
        }

        private static void Unlink(RoomItem item)
        {
            if (item == null || !Pairs.TryGetValue(item, out var pair)) return;
            Pairs.Remove(item);
            Pairs.Remove(pair);
            ElevatorStoreys.Remove(item);
            ElevatorStoreys.Remove(pair);
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
                Link(lower, _lower, match, _upper);
            }
        }

        private static void QueueMissingRequiredPlots()
        {
            if (_upper?.State?.HospitalPlots == null) return;
            foreach (var plot in _upper.State.HospitalPlots)
                QueueRequiredPlot(plot);
        }

        private static void QueueRequiredPlot(HospitalPlot plot)
        {
            if (!IsUpperPlot(plot) || !plot.Bought || !plot.Built ||
                HasUpperAccess(plot.HospitalMap) || RequiredPlots.Contains(plot)) return;
            RequiredPlots.Add(plot);
            StoreyPlugin.Log?.LogInfo(
                "La parcela superior construida requiere su primer ascensor: " +
                plot.Definition + ".");
        }

        private static bool IsUpperPlot(HospitalPlot plot) =>
            ReferenceEquals(StoreyRuntime.GetStorey(
                plot?.HospitalMap?.FloorPlan?.WorldState), _upper);

        private static IEnumerable<RoomItem> UpperAccessItems(HospitalMap map)
        {
            if (map == null) yield break;
            foreach (var pair in Pairs)
            {
                var item = pair.Key;
                if (!ReferenceEquals(GetElevatorStorey(item), _upper) ||
                    !ReferenceEquals(item?.FloorPlan?.HospitalMap, map) ||
                    item.HasBeenDestroyed() || pair.Value == null ||
                    pair.Value.HasBeenDestroyed() || FirstInteraction(item) == null ||
                    FirstInteraction(pair.Value) == null) continue;
                yield return item;
            }
        }

        private static bool HasUpperAccess(HospitalMap map) =>
            UpperAccessItems(map).Any();

        private static List<Vector3> UpperAccessPositions(HospitalMap map) =>
            UpperAccessItems(map)
                .Select(FirstInteraction)
                .Where(interaction => interaction != null)
                .Select(interaction => interaction.WorldStartPosition)
                .ToList();

        private static void ResolveRequiredPlots()
        {
            for (var index = RequiredPlots.Count - 1; index >= 0; --index)
            {
                var plot = RequiredPlots[index];
                if (plot != null && plot.Bought && plot.Built &&
                    !HasUpperAccess(plot.HospitalMap)) continue;
                RequiredPlots.RemoveAt(index);
                if (plot != null) RequiredPlotNotified.Remove(plot);
            }
        }

        private static void TickRequiredPlacement()
        {
            ResolveRequiredPlots();
            if (RequiredPlots.Count == 0 || _level == null ||
                _level.HospitalHUDManager == null ||
                _level.HospitalHUDManager.ShowingPauseMenu ||
                _level.BuildingLogic == null ||
                _level.BuildingLogic.CurrentState != BuildingLogic.State.Null) return;

            if (_level.CursorManager.TryGetActiveMode<CursorRoomItem>(out var activeItem))
                return;

            var plot = RequiredPlots[0];
            var plan = plot?.HospitalMap?.FloorPlan;
            if (plan == null) return;
            StoreyRuntime.ActivatePlot(plot);
            StoreyRuntime.RunInStorey(_upper, () =>
                StartItemPlacement.Invoke(_upper.Events,
                    new object[] { ElevatorDefinition.Definition, plan, true }));
            if (!RequiredPlotNotified.Add(plot)) return;
            _level.InWorldMessages?.ShowMessage(ElevatorRequired.ToString(),
                plan.GetAnchorWorldPos() + Vector3.up, 4f,
                InWorldMessages.MessageType.Info);
        }

        internal static void UseUpperAccessAsEntrance(RoomItem item,
            ObjectInteraction interaction, RoomBuildingNavMesh navMesh,
            ref Vector3? entrance)
        {
            if (!ReferenceEquals(StoreyRuntime.GetStorey(item), _upper) ||
                interaction == null) return;
            var positions = UpperAccessPositions(item.FloorPlan?.HospitalMap);
            if (positions.Count == 0) return;
            var start = interaction.WorldStartPosition;
            foreach (var position in positions)
                if (navMesh == null || navMesh.CanReach(start, position))
                {
                    entrance = position;
                    return;
                }
            entrance = positions[0];
        }

        internal static void RestoreUpperAccessConnectivity(RoomBuildingNavMesh navMesh)
        {
            var map = RoomBuildingNavMeshHospitalMap.GetValue(navMesh) as HospitalMap;
            if (!ReferenceEquals(StoreyRuntime.GetStorey(map?.FloorPlan?.WorldState), _upper))
                return;
            var invalidItems = navMesh.InvalidItems;
            if (invalidItems == null || invalidItems.Count == 0) return;
            foreach (var item in invalidItems.ToArray())
                if (HasValidUpperAccessPath(navMesh, item)) invalidItems.Remove(item);
        }

        private static bool HasValidUpperAccessPath(RoomBuildingNavMesh navMesh,
            RoomItem item)
        {
            if (item?.FloorPlan == null) return false;
            var accessPositions = UpperAccessPositions(item.FloorPlan.HospitalMap);
            if (accessPositions.Count == 0) return false;
            var required = item.Definition.MinValidInteractions;
            var valid = 0;
            foreach (var interaction in item.Interactions)
            {
                if (interaction.Definition.IgnoreRoomCheck)
                {
                    --required;
                    continue;
                }
                if (accessPositions.Any(position =>
                        navMesh.CanReach(interaction.WorldStartPosition, position))) ++valid;
            }
            return valid >= Math.Max(0, required);
        }

        internal static bool CanRemove(RoomItem item, bool showMessage)
        {
            if (_pairedOperation || !ElevatorDefinition.IsElevator(item) ||
                !Pairs.TryGetValue(item, out var pair)) return true;
            var upperItem = ReferenceEquals(GetElevatorStorey(item), _upper)
                ? item : pair;
            var map = upperItem?.FloorPlan?.HospitalMap;
            if (map?.Plot == null || !map.Plot.Bought || !map.Plot.Built ||
                UpperAccessItems(map).Count() > 1) return true;
            if (showMessage)
                _level?.InWorldMessages?.ShowMessage(LastElevatorRequired.ToString(),
                    item.WorldPosition + Vector3.up, 4f,
                    InWorldMessages.MessageType.Info);
            return false;
        }

        internal static bool TryRoute(NavPath path, Vector3 destination, float rotation,
            INavPathResult callback, float arrivalDistance)
        {
            var character = NavPathCharacter.GetValue(path) as Character;
            if (character == null || InternalNavigation.Contains(character) || Pairs.Count == 0)
                return false;
            var sourceStorey = GetCharacterStorey(character);
            var targetStorey = GetTargetStorey(callback, destination);
            if (sourceStorey == null || targetStorey == null ||
                ReferenceEquals(sourceStorey, targetStorey)) return false;

            RoomItem source = null;
            RoomItem target = null;
            var best = float.MaxValue;
            var candidates = 0;
            var sourceReachable = 0;
            var targetReachable = 0;
            var sourceNavMesh = sourceStorey.State.NavMesh;
            var targetNavMesh = targetStorey.State.NavMesh;
            foreach (var pair in Pairs)
            {
                if (!ReferenceEquals(GetElevatorStorey(pair.Key), sourceStorey) ||
                    !ReferenceEquals(GetElevatorStorey(pair.Value), targetStorey)) continue;
                ++candidates;
                var sourceInteraction = FirstInteraction(pair.Key);
                var targetInteraction = FirstInteraction(pair.Value);
                if (sourceInteraction == null || targetInteraction == null) continue;
                if (!sourceNavMesh.CanReach(character.Position,
                        sourceInteraction.WorldStartPosition)) continue;
                ++sourceReachable;
                if (!targetNavMesh.CanReach(targetInteraction.WorldStartPosition,
                        destination)) continue;
                ++targetReachable;
                var score = HorizontalDistance(character.Position,
                                sourceInteraction.WorldStartPosition) +
                            HorizontalDistance(destination, targetInteraction.WorldStartPosition);
                if (score >= best) continue;
                best = score;
                source = pair.Key;
                target = pair.Value;
            }
            if (source == null || target == null)
            {
                if (RouteFailureLogged.Add(character))
                    StoreyPlugin.Log?.LogWarning(
                        "No se encontró una ruta de ascensor válida: candidatas=" + candidates +
                        ", accesibles en origen=" + sourceReachable +
                        ", accesibles en destino=" + targetReachable +
                        ", destino=" + destination + ".");
                return false;
            }
            RouteFailureLogged.Remove(character);

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
            StoreyPlugin.Log?.LogInfo(
                "Ruta entre plantas iniciada: " +
                (ReferenceEquals(sourceStorey, _upper) ? "superior" : "inferior") +
                " -> " +
                (ReferenceEquals(targetStorey, _upper) ? "superior" : "inferior") +
                ", destino=" + destination + ".");
            return true;
        }

        internal static bool PrepareNavigation(NavPath path, Vector3 destination,
            float rotation, INavPathResult callback, float arrivalDistance) =>
            !TryRoute(path, destination, rotation, callback, arrivalDistance);

        internal static void TickRoutes()
        {
            TickRequiredPlacement();
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
                StoreyPlugin.Log?.LogInfo(
                    "Ruta entre plantas reanudada hacia el destino original " +
                    request.Destination + ".");
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
            ApplyCharacterStorey(character, GetElevatorStorey(request.Target));
            character.NavPath.Warp(target);
            character.Position = target;
            character.Rotation = targetInteraction.StartRotation;
            request.Transferred = true;
            StoreyPlugin.Log?.LogInfo(
                "Ascensor transferido a " + target +
                " sobre la malla física de su planta.");
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

        internal static void CorrectStartPosition(ObjectInteraction interaction,
            ref Vector3 position)
        {
            if (!ElevatorDefinition.IsElevator(interaction)) return;
            var socket = InteractionStartSocket.GetValue(interaction) as Transform;
            position = socket == null ? Vector3.zero : socket.localPosition;
        }

        internal static void CorrectWorldStartPosition(ObjectInteraction interaction,
            ref Vector3 position)
        {
            if (!ElevatorDefinition.IsElevator(interaction)) return;
            var item = interaction.ParentRoomItem;
            var socket = InteractionStartSocket.GetValue(interaction) as Transform;
            if (item == null)
            {
                position = Vector3.zero;
                return;
            }
            var local = socket == null ? Vector3.zero : socket.localPosition;
            position = Quaternion.Euler(0f, item.WorldRotation, 0f) * local +
                       item.WorldPosition;
        }

        internal static void CorrectStartRotation(ObjectInteraction interaction,
            ref Quaternion rotation)
        {
            if (!ElevatorDefinition.IsElevator(interaction)) return;
            var socket = InteractionStartSocket.GetValue(interaction) as Transform;
            rotation = socket == null ? Quaternion.identity : socket.localRotation;
        }

        internal static void CorrectWorldStartRotation(ObjectInteraction interaction,
            ref float rotation)
        {
            if (!ElevatorDefinition.IsElevator(interaction)) return;
            var socket = InteractionStartSocket.GetValue(interaction) as Transform;
            rotation = (socket == null ? 0f : socket.localEulerAngles.y) +
                       (interaction.ParentRoomItem?.WorldRotation ?? 0f);
        }

        internal static bool IsCharacterUpper(Character character) =>
            ReferenceEquals(GetCharacterStorey(character), _upper);

        internal static void PlaceStaffOnStorey(Staff staff, Vector3 position)
        {
            if (staff == null) return;
            var height = global::UnderPressure.StoreyMapRegistry.StoreyHeight;
            ApplyCharacterStorey(staff, position.y >= height * .5f ? _upper : _lower);
            staff.NavPath?.Warp(position);
            staff.Position = position;
            StoreyPlugin.Log?.LogInfo("Empleado colocado en planta " +
                                     (position.y >= height * .5f ? "superior" : "inferior") +
                                     ".");
        }

        internal static void ProjectStaffPositionToActiveStorey(ref Vector3 position)
        {
            if (_level == null || global::UnderPressure.StoreyMapRegistry.GetActiveStorey(
                    _level) != global::UnderPressure.StoreyMapRegistry.UpperStorey) return;
            position.y = global::UnderPressure.StoreyMapRegistry.StoreyHeight;
        }

        private static StoreyInstance GetCharacterStorey(Character character)
        {
            if (character == null) return null;
            if (CharacterStoreys.TryGetValue(character, out var registered))
                return registered;
            var height = global::UnderPressure.StoreyMapRegistry.StoreyHeight;
            return character.Position.y >= height * .5f ? _upper : _lower;
        }

        private static StoreyInstance GetTargetStorey(INavPathResult callback,
            Vector3 destination)
        {
            if (callback is InteractionController controller)
            {
                var interactionStorey = StoreyRuntime.GetStorey(
                    controller.Interaction?.ParentRoomItem);
                if (interactionStorey != null) return interactionStorey;
            }
            if (callback is TH20.BTA.MoveTo moveTo)
            {
                var target = MoveToTargetObject.GetValue(moveTo) as
                    TH20.BT_Types.SharedItemRef;
                var itemStorey = StoreyRuntime.GetStorey(target?.Get);
                if (itemStorey != null) return itemStorey;
            }
            return destination.y >= global::UnderPressure.StoreyMapRegistry.StoreyHeight * .5f
                ? _upper : _lower;
        }

        private static void ApplyCharacterStorey(Character character,
            StoreyInstance storey)
        {
            if (character == null || storey == null) return;
            CharacterStoreys[character] = storey;
        }

        private static StoreyInstance GetElevatorStorey(RoomItem item)
        {
            if (item != null && ElevatorStoreys.TryGetValue(item, out var registered))
                return registered;
            return StoreyRuntime.GetStorey(item);
        }

        internal static bool CanReachOnNavigation(TH20.NavMesh navMesh,
            Vector3 start, Vector3 end)
        {
            var height = StoreyRuntime.NavigationHeight(navMesh);
            start.y = height;
            end.y = height;
            if (!UnityEngine.AI.NavMesh.SamplePosition(start, out _, .25f, -1) ||
                !UnityEngine.AI.NavMesh.SamplePosition(end, out _, .25f, -1)) return false;
            var path = NavMeshCanReachPath.GetValue(navMesh) as UnityEngine.AI.NavMeshPath;
            if (path == null) return false;
            UnityEngine.AI.NavMesh.CalculatePath(start, end, -1, path);
            return path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
        }

        internal static void RebuildAreaLookupAtStoreyHeight(TH20.NavMesh navMesh,
            float lookupHeight)
        {
            var bounds = (Bounds)NavMeshWorldBounds.GetValue(navMesh);
            var center = bounds.center;
            center.y = StoreyRuntime.NavigationHeight(navMesh);
            var lookup = BuildNavMeshAreaLookup.Invoke(null,
                new object[] { center, lookupHeight });
            NavMeshAreaLookup.SetValue(navMesh, lookup);
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }
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

    [HarmonyPatch(typeof(InteractionAlgorithms), "ValidateInteractionStart",
        new[]
        {
            typeof(ItemValidateMode), typeof(RoomItem), typeof(Vector3?),
            typeof(ObjectInteraction), typeof(WorldState),
            typeof(RoomBuildingNavMesh), typeof(Vector3)
        })]
    internal static class ElevatorInteractionEntranceValidationPatch
    {
        private static void Prefix(RoomItem __1, ref Vector3? __2,
            ObjectInteraction __3, RoomBuildingNavMesh __5) =>
            ElevatorRuntime.UseUpperAccessAsEntrance(__1, __3, __5, ref __2);
    }

    [HarmonyPatch(typeof(RoomBuildingNavMesh), "ValidateConnectivity")]
    internal static class ElevatorRoomConnectivityPatch
    {
        private static void Postfix(RoomBuildingNavMesh __instance) =>
            ElevatorRuntime.RestoreUpperAccessConnectivity(__instance);
    }

    [HarmonyPatch(typeof(RoomBuildingNavMesh), "ValidPosition")]
    internal static class StoreyRoomNavPositionPatch
    {
        // RoomBuildingNavMesh es una malla temporal y local que el juego construye
        // siempre en Y=0, también cuando valida objetos de la planta superior.
        private static void Prefix(ref Vector3 __0) => __0.y = 0f;
    }

    [HarmonyPatch(typeof(BuildEvents), "CursorDeleteObject")]
    internal static class ElevatorCursorDeletePatch
    {
        private static bool Prefix(ICursorSelectable __0) =>
            !(__0 is RoomItem item) || ElevatorRuntime.CanRemove(item, true);
    }

    [HarmonyPatch(typeof(BuildEvents), "RoomItemDestroy")]
    internal static class ElevatorDestroyGuardPatch
    {
        private static bool Prefix(RoomItem __0) =>
            ElevatorRuntime.CanRemove(__0, false);
    }

    [HarmonyPatch(typeof(ObjectInteraction), "StartInteraction")]
    internal static class ElevatorInteractionPatch
    {
        private static void Postfix(ObjectInteraction __instance, Character __0,
            bool __result) =>
            ElevatorRuntime.OnInteractionStarted(__instance, __0, __result);
    }

    [HarmonyPatch(typeof(ObjectInteraction), "get_StartPosition")]
    internal static class ElevatorStartPositionPatch
    {
        private static void Postfix(ObjectInteraction __instance, ref Vector3 __result) =>
            ElevatorRuntime.CorrectStartPosition(__instance, ref __result);
    }

    [HarmonyPatch(typeof(ObjectInteraction), "get_WorldStartPosition")]
    internal static class ElevatorWorldStartPositionPatch
    {
        private static void Postfix(ObjectInteraction __instance, ref Vector3 __result) =>
            ElevatorRuntime.CorrectWorldStartPosition(__instance, ref __result);
    }

    [HarmonyPatch(typeof(ObjectInteraction), "get_StartRotation")]
    internal static class ElevatorStartRotationPatch
    {
        private static void Postfix(ObjectInteraction __instance, ref Quaternion __result) =>
            ElevatorRuntime.CorrectStartRotation(__instance, ref __result);
    }

    [HarmonyPatch(typeof(ObjectInteraction), "get_WorldStartRotation")]
    internal static class ElevatorWorldStartRotationPatch
    {
        private static void Postfix(ObjectInteraction __instance, ref float __result) =>
            ElevatorRuntime.CorrectWorldStartRotation(__instance, ref __result);
    }

    [HarmonyPatch(typeof(NavPath), "MoveTo",
        new[] { typeof(Vector3), typeof(float), typeof(INavPathResult), typeof(float) })]
    internal static class ElevatorNavigationPatch
    {
        private static bool Prefix(NavPath __instance, Vector3 __0, float __1,
            INavPathResult __2, float __3) =>
            ElevatorRuntime.PrepareNavigation(__instance, __0, __1, __2, __3);
    }

    [HarmonyPatch(typeof(TH20.NavMesh), nameof(TH20.NavMesh.CanReach))]
    internal static class StoreyCanReachPatch
    {
        private static bool Prefix(TH20.NavMesh __instance, Vector3 __0,
            Vector3 __1, ref bool __result)
        {
            if (!StoreyRuntime.IsUpperNavigation(__instance)) return true;
            __result = ElevatorRuntime.CanReachOnNavigation(__instance, __0, __1);
            return false;
        }
    }

    [HarmonyPatch(typeof(TH20.NavMesh), "UpdateNavmeshIslandIDs")]
    internal static class StoreyNavMeshAreaLookupPatch
    {
        private static bool Prefix(TH20.NavMesh __instance, float __0)
        {
            if (!StoreyRuntime.IsUpperNavigation(__instance)) return true;
            ElevatorRuntime.RebuildAreaLookupAtStoreyHeight(__instance, __0);
            return false;
        }
    }

    [HarmonyPatch(typeof(StaffPickedUpState), "Finished")]
    internal static class StoreyStaffPlacementPatch
    {
        private static void Postfix(StaffPickedUpState __instance, bool __result,
            Vector3 ____desiredPosition)
        {
            if (__result)
                ElevatorRuntime.PlaceStaffOnStorey(__instance.GetOwner<Staff>(),
                    ____desiredPosition);
        }
    }

    [HarmonyPatch(typeof(StaffPickedUpState), "SetPosition")]
    internal static class StoreyStaffHeldPositionPatch
    {
        private static void Prefix(ref Vector3 __0) =>
            ElevatorRuntime.ProjectStaffPositionToActiveStorey(ref __0);
    }

    [HarmonyPatch(typeof(CursorStaffHeld), "CursorUpdate")]
    internal static class StoreyStaffCursorContextPatch
    {
        private static void Prefix(Level ____level, ref WorldState ____worldState,
            out StoreyRuntime.LevelContext __state)
        {
            StoreyRuntime.EnterActiveInteractionContext(____level, out __state);
            ____worldState = ____level?.WorldState;
        }

        private static Exception Finalizer(Level ____level,
            StoreyRuntime.LevelContext __state, Exception __exception)
        {
            StoreyRuntime.ExitStoreyContext(____level, __state);
            return __exception;
        }
    }
}
