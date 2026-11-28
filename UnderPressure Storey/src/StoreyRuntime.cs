using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace UnderPressure.Storey
{
    internal sealed class StoreyInstance
    {
        internal readonly Level Level;
        internal readonly BuildEvents Events;
        internal readonly GameObject Root;
        internal readonly GameObject GridRoot;
        internal WorldState State;

        internal StoreyInstance(Level level, BuildEvents events, int index)
        {
            Level = level;
            Events = events;
            Root = new GameObject("UnderPressure Storey " + index);
            Root.transform.position = new Vector3(0f, index * 2f, 0f);

            GridRoot = new GameObject("Vector Grid");
            GridRoot.transform.SetParent(Root.transform, false);
            GridRoot.SetActive(false);
        }
    }

    internal static class StoreyRuntime
    {
        private static readonly MethodInfo CloneConfig =
            AccessTools.Method(typeof(WorldState.Config), "MemberwiseClone");
        private static readonly FieldInfo LevelMetagame =
            AccessTools.Field(typeof(Level), "_metagame");
        private static readonly FieldInfo LevelBuildEvents =
            AccessTools.Field(typeof(Level), "_buildEvents");
        private static readonly FieldInfo PlotMap =
            AccessTools.Field(typeof(HospitalPlot), "_hospitalMap");
        private static readonly FieldInfo StateNavMesh =
            AccessTools.Field(typeof(WorldState), "_navMesh");
        private static readonly FieldInfo NavMeshData =
            AccessTools.Field(typeof(TH20.NavMesh), "_navMeshData");
        private static readonly FieldInfo NavMeshDataInstance =
            AccessTools.Field(typeof(TH20.NavMesh), "_navMeshDataInstance");
        private static readonly FieldInfo LevelWorldState =
            AccessTools.Field(typeof(Level), "_worldState");
        private static readonly FieldInfo BuildEventsWorldState =
            AccessTools.Field(typeof(BuildEvents), "_worldState");
        private static readonly FieldInfo BuildingLogicWorldState =
            AccessTools.Field(typeof(BuildingLogic), "_worldState");
        private static readonly FieldInfo BuildingLogicBuildEvents =
            AccessTools.Field(typeof(BuildingLogic), "_buildEvents");
        private static readonly FieldInfo PlotWorldState =
            AccessTools.Field(typeof(HospitalPlot), "_worldState");
        private static readonly FieldInfo HospitalMapWorldState =
            AccessTools.Field(typeof(HospitalMap), "_worldState");
        private static readonly FieldInfo StateUnbuiltHospitalMaps =
            AccessTools.Field(typeof(WorldState), "_unbuiltHospitalMaps");
        private static readonly FieldInfo RoomVisualWorldState =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_worldState");
        private static readonly FieldInfo RoomVisualBuildEvents =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_buildEvents");
        private static readonly FieldInfo RoomItemVisualBuildEvents =
            AccessTools.Field(typeof(RoomItemVisual), "_buidEvents");
        private static readonly MethodInfo RoomRegisterEvents =
            AccessTools.Method(typeof(Room), "RegisterEvents");
        private static readonly MethodInfo RoomUnregisterEvents =
            AccessTools.Method(typeof(Room), "UnregisterEvents");
        private static readonly FieldInfo CursorWorldState =
            AccessTools.Field(typeof(CursorEditHospital), "_worldState");
        private static readonly FieldInfo ItemCursorWorldState =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_worldState");
        private static readonly FieldInfo SelectCursorWorldState =
            AccessTools.Field(typeof(CursorSelect), "_worldState");
        private static readonly FieldInfo SelectCursorBuildEvents =
            AccessTools.Field(typeof(CursorSelect), "_buildEvents");
        private static readonly FieldInfo CursorManagerMode =
            AccessTools.Field(typeof(CursorManager), "_mode");
        private static readonly FieldInfo CursorManagerModeStack =
            AccessTools.Field(typeof(CursorManager), "_modeStack");
        private static readonly FieldInfo CursorWorldPosition =
            AccessTools.Field(typeof(CursorManager), "_worldPosition");
        private static readonly FieldInfo CursorWorldPositionSmoothed =
            AccessTools.Field(typeof(CursorManager), "_worldPositionSmoothed");
        private static readonly FieldInfo CursorVisualisation =
            AccessTools.Field(typeof(CursorManager), "_visualisation");
        private static readonly FieldInfo HospitalMapDemolishRoot =
            AccessTools.Field(typeof(HospitalMap), "_demolishRoot");
        private static readonly FieldInfo HospitalMapFootprintPerimeter =
            AccessTools.Field(typeof(HospitalMap), "_footprintPerimeter");
        private static readonly FieldInfo FootprintPerimeterRoot =
            AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root");
        private static readonly FieldInfo RoomItemEditGameObject =
            AccessTools.Field(typeof(RoomItemVisualEdit), "_gameObject");
        private static readonly MethodInfo LightingRoomDeleted =
            AccessTools.Method(typeof(RoomLightingManager), "OnRoomDeleted");
        private static readonly FieldInfo LightingBuiltRooms =
            AccessTools.Field(typeof(RoomLightingManager), "_builtRooms");
        private static readonly FieldInfo RoomLightConfig =
            AccessTools.Field(typeof(RoomLightingManager), "_config");
        private static readonly FieldInfo RoomLightHeight =
            AccessTools.Field(typeof(RoomLightingManager), "_roomLightHeight");
        private static readonly FieldInfo RoomLightBaseBias =
            AccessTools.Field(typeof(RoomLightingManager), "_roomLightBaseBias");
        private static readonly Dictionary<Level, WorldState.Config> Configs =
            new Dictionary<Level, WorldState.Config>();
        private static readonly List<HospitalPlot> PlotOrder = new List<HospitalPlot>();
        private static readonly Dictionary<HospitalPlot, StoreyInstance> PlotStoreys =
            new Dictionary<HospitalPlot, StoreyInstance>();
        private static readonly Dictionary<HospitalMap, StoreyInstance> MapStoreys =
            new Dictionary<HospitalMap, StoreyInstance>();
        private static readonly Dictionary<Renderer, bool> HiddenRenderers =
            new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Character, bool> CharacterVisibility =
            new Dictionary<Character, bool>();
        private static readonly HashSet<UnityEngine.AI.NavMeshData> RaisedNavigationData =
            new HashSet<UnityEngine.AI.NavMeshData>();

        private static StoreyInstance _lower;
        private static StoreyInstance _upper;
        private static StoreyInstance _constructing;
        private static StoreyInstance _active;
        private static GameObject _toolbar;
        private static Material _gridMaterial;

        internal static bool Constructing => _constructing != null;

        internal struct LevelContext
        {
            internal WorldState State;
            internal BuildEvents Events;
            internal WorldState EventsState;
            internal WorldState BuildingState;
            internal BuildEvents BuildingEvents;
            internal StoreyInstance Constructing;
        }

        internal static void Register() => UnderPressure.StoreyMapRegistry.RegisterRuntime(
            GetPlots, GetWorld, TogglePlot, Refresh, ActivateGround, GetActiveStorey,
            ActivateStorey, OnPlotAdded, OnPlotRemoved);

        internal static void Unregister() => UnderPressure.StoreyMapRegistry.UnregisterRuntime();

        internal static void CaptureConfig(WorldState.Config config, Level level)
        {
            if (_constructing != null || config == null || level == null ||
                level.WorldState != null) return;
            Configs[level] = config;
        }

        internal static bool Ensure(Level level)
        {
            if (_upper != null && ReferenceEquals(_upper.Level, level)) return true;
            StoreyPlugin.Log?.LogInfo(
                "Inicializando los WorldState de plantas para el nivel activo.");
            if (level == null || !Configs.TryGetValue(level, out var source))
            {
                StoreyPlugin.Log?.LogError(
                    "No se capturó la configuración del WorldState inferior.");
                return false;
            }

            Destroy(false);
            _lower = new StoreyInstance(level, level.BuildEvents, 0) { State = level.WorldState };
            PlotOrder.Clear();
            PlotStoreys.Clear();
            MapStoreys.Clear();
            foreach (var plot in _lower.State.HospitalPlots)
            {
                PlotOrder.Add(plot);
                PlotStoreys[plot] = _lower;
                if (plot.HospitalMap == null) continue;
                MapStoreys[plot.HospitalMap] = _lower;
                AttachMapVisuals(plot.HospitalMap, _lower);
            }
            if (!TryGetBounds(_lower.State, out var bounds)) return false;
            BuildVectorGrid(_lower, bounds);

            var normalEvents = level.BuildEvents;
            // BuildEvents pertenece al nivel y contiene los suscriptores de la interfaz
            // nativa. Los dos WorldState comparten ese bus; el contexto cambia solamente
            // el mundo sobre el que ejecuta cada operacion.
            var upper = new StoreyInstance(level, normalEvents, 1);
            var savedPostConstruct = level.PostConstruct;
            _constructing = upper;
            try
            {
                LevelBuildEvents.SetValue(level, normalEvents);
                var upperConfig = CloneConfig.Invoke(source, null) as WorldState.Config;
                upperConfig.HospitalPlots =
                    new List<FullInspector.SharedInstance<HospitalPlotDefinition>>();
                if (source.HospitalPlots != null && source.HospitalPlots.Count > 0)
                    upperConfig.HospitalPlots.Add(source.HospitalPlots[0]);
                var state = new WorldState(
                    upperConfig,
                    level,
                    LevelMetagame.GetValue(level) as Metagame,
                    level.VisualManager,
                    level.Config.GetDataViewManagerConfig().ValueMaterial,
                    level.Config.GetBuildingLogicConfig().RoomItemEditConfig);
                upper.State = state;
                _upper = upper;
                LevelWorldState.SetValue(level, state);
                RemoveUpperSeed();
                BuildVectorGrid(upper, bounds);
                Activate(_lower);
                foreach (var plot in PlotOrder.Where(plot =>
                             UnderPressure.StoreyMapRegistry.GetStorey(plot) == 1).ToArray())
                    MovePlot(plot, _upper, false);
                EnsureUpperNavigationHeight();
                ElevatorRuntime.Initialise(level, _lower, _upper);
                StoreyPlugin.Log?.LogInfo(
                    "Simulacion simultanea activada: ambos WorldState se actualizaran siempre.");
                var lowerPlots = PlotOrder.Count(plot =>
                    PlotStoreys.TryGetValue(plot, out var storey) &&
                    ReferenceEquals(storey, _lower));
                var upperPlots = PlotOrder.Count(plot =>
                    PlotStoreys.TryGetValue(plot, out var storey) &&
                    ReferenceEquals(storey, _upper));
                StoreyPlugin.Log?.LogInfo(
                    "Segundo WorldState creado; parcelas inferiores=" + lowerPlots +
                    ", superiores=" + upperPlots + ".");
                return true;
            }
            catch (Exception exception)
            {
                StoreyPlugin.Log?.LogError(
                    "No se pudo crear el segundo WorldState: " + exception);
                DetachLowerMapVisuals();
                UnityEngine.Object.Destroy(_lower.Root);
                UnityEngine.Object.Destroy(upper.Root);
                _lower = null;
                return false;
            }
            finally
            {
                level.PostConstruct = savedPostConstruct;
                LevelBuildEvents.SetValue(level, normalEvents);
                _constructing = null;
            }
        }

        private static void RemoveUpperSeed()
        {
            if (_upper?.State == null || _upper.State.HospitalPlots.Count == 0) return;
            var seed = _upper.State.HospitalPlots[0];
            _upper.State.HospitalPlots.RemoveAt(0);
            if (seed.HospitalMap != null)
            {
                RemoveFromSharedLighting(seed.HospitalMap);
                _upper.State.DestroyHospitalMap(seed.HospitalMap);
                PlotMap.SetValue(seed, null);
            }
            seed.Destroy();
            var navMesh = StateNavMesh.GetValue(_upper.State) as TH20.NavMesh;
            navMesh?.Destroy();
            StateNavMesh.SetValue(_upper.State, null);
            _upper.State.ExteriorState = default(BoolArray2D);
        }

        private static void BuildVectorGrid(StoreyInstance storey, GridBounds bounds)
        {
            var minX = bounds.Min.X;
            var minZ = bounds.Min.Y;
            var maxX = bounds.Max.X;
            var maxZ = bounds.Max.Y;
            var vertices = new List<Vector3>((maxX - minX + maxZ - minZ + 4) * 2);
            const float cellSize = 2f;
            const float lineHeight = .08f;
            var worldMinX = (minX - .5f) * cellSize;
            var worldMaxX = (maxX + .5f) * cellSize;
            var worldMinZ = (minZ - .5f) * cellSize;
            var worldMaxZ = (maxZ + .5f) * cellSize;

            for (var x = minX; x <= maxX + 1; ++x)
            {
                var worldX = (x - .5f) * cellSize;
                vertices.Add(new Vector3(worldX, lineHeight, worldMinZ));
                vertices.Add(new Vector3(worldX, lineHeight, worldMaxZ));
            }
            for (var z = minZ; z <= maxZ + 1; ++z)
            {
                var worldZ = (z - .5f) * cellSize;
                vertices.Add(new Vector3(worldMinX, lineHeight, worldZ));
                vertices.Add(new Vector3(worldMaxX, lineHeight, worldZ));
            }

            var indices = new int[vertices.Count];
            for (var index = 0; index < indices.Length; ++index) indices[index] = index;
            var mesh = new Mesh
            {
                name = "UP Storey Vector Grid",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > 65535
                    ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            var grid = new GameObject("Grid Lines", typeof(MeshFilter), typeof(MeshRenderer));
            grid.transform.SetParent(storey.GridRoot.transform, false);
            grid.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = grid.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetGridMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static bool TryGetBounds(WorldState state, out GridBounds result)
        {
            result = default(GridBounds);
            var found = false;
            foreach (var plot in state.HospitalPlots)
            {
                var map = plot?.HospitalMap;
                if (map == null) continue;
                result = found ? result | map.Bounds : map.Bounds;
                found = true;
            }
            return found;
        }

        private static Material GetGridMaterial()
        {
            if (_gridMaterial != null) return _gridMaterial;
            var shader = Shader.Find("Hidden/Internal-Colored") ??
                Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException(
                "No existe un shader compatible con líneas transparentes.");
            _gridMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (_gridMaterial.HasProperty("_Color"))
                _gridMaterial.SetColor("_Color", new Color(.95f, 1f, 1f, .57f));
            if (_gridMaterial.HasProperty("_SrcBlend"))
            {
                _gridMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _gridMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _gridMaterial.SetInt("_ZWrite", 0);
            }
            _gridMaterial.renderQueue = 3000;
            return _gridMaterial;
        }

        internal static void ToggleLowerGrid()
        {
            if (_lower == null) return;
            _lower.GridRoot.SetActive(!_lower.GridRoot.activeSelf);
        }

        internal static void ToggleUpperGrid()
        {
            if (_upper == null) return;
            _upper.GridRoot.SetActive(!_upper.GridRoot.activeSelf);
        }

        internal static void BindPlotRow(PlotSelectionItem row, HospitalPlot plot)
        {
            if (row == null || plot == null) return;
            var control = row.GetComponent<StoreyPlotRowControl>() ??
                row.gameObject.AddComponent<StoreyPlotRowControl>();
            control.Bind(row, plot);
        }

        private static IList<HospitalPlot> GetPlots(Level level) =>
            _lower != null && ReferenceEquals(_lower.Level, level)
                ? PlotOrder.ToArray()
                : (IList<HospitalPlot>)level?.WorldState?.HospitalPlots ?? new HospitalPlot[0];

        private static WorldState GetWorld(HospitalPlot plot) =>
            plot != null && PlotStoreys.TryGetValue(plot, out var storey)
                ? storey.State : null;

        private static void Refresh(Level level)
        {
            if (level == null) return;
            Destroy(false);
            if (!Ensure(level)) throw new InvalidOperationException(
                "No se pudo reconstruir el segundo WorldState.");
        }

        private static void ActivateGround(Level level)
        {
            if (_lower != null && ReferenceEquals(_lower.Level, level)) Activate(_lower);
        }

        private static int GetActiveStorey(Level level) =>
            _active != null && ReferenceEquals(_active.Level, level) &&
            ReferenceEquals(_active, _upper)
                ? UnderPressure.StoreyMapRegistry.UpperStorey
                : UnderPressure.StoreyMapRegistry.GroundStorey;

        private static void ActivateStorey(Level level, int storey)
        {
            if (!Ensure(level)) return;
            Activate(storey == UnderPressure.StoreyMapRegistry.UpperStorey
                ? _upper : _lower);
        }

        private static void OnPlotAdded(Level level, HospitalPlot plot)
        {
            if (plot == null || _active == null || !ReferenceEquals(_active.Level, level)) return;
            if (!PlotOrder.Contains(plot)) PlotOrder.Add(plot);
            PlotStoreys[plot] = _active;
            if (plot.HospitalMap != null)
            {
                MapStoreys[plot.HospitalMap] = _active;
                SynchronizePlotShellLighting(plot.HospitalMap, _active);
            }
            UnderPressure.StoreyMapRegistry.SetStorey(plot.Definition,
                ReferenceEquals(_active, _upper) ? 1 : 0);
        }

        private static void OnPlotRemoved(Level level, HospitalPlot plot)
        {
            if (plot == null || _lower == null || !ReferenceEquals(_lower.Level, level)) return;
            PlotOrder.Remove(plot);
            PlotStoreys.Remove(plot);
            if (plot.HospitalMap != null) MapStoreys.Remove(plot.HospitalMap);
        }

        internal static int GetStorey(FloorPlan floorPlan)
        {
            var map = floorPlan?.HospitalMap;
            return map != null && MapStoreys.TryGetValue(map, out var storey) &&
                   ReferenceEquals(storey, _upper) ? 1 : 0;
        }

        internal static bool EnterWorld(WorldState world, out StoreyInstance previous)
        {
            previous = _constructing;
            if (_upper != null && ReferenceEquals(world, _upper.State)) _constructing = _upper;
            else if (_lower != null && ReferenceEquals(world, _lower.State)) _constructing = _lower;
            return true;
        }

        internal static void ExitContext(StoreyInstance previous) => _constructing = previous;

        private static void Activate(StoreyInstance storey)
        {
            if (storey?.State == null || storey.Level == null) return;
            var changed = !ReferenceEquals(_active, storey);
            _active = storey;
            // El Level conserva siempre el mundo inferior como contexto de simulacion.
            // La planta activa solo determina la interaccion y la representacion visual.
            if (_lower?.State != null)
            {
                LevelWorldState.SetValue(storey.Level, _lower.State);
                LevelBuildEvents.SetValue(storey.Level, _lower.Events);
            }
            SynchronizeBuildingLogic(storey);
            SynchronizeCursorModes(storey);
            storey.Level.CursorManager?.SetPlaneOffset(0f);
            RefreshVisualStorey();
            if (changed)
            {
                var index = ReferenceEquals(storey, _upper)
                    ? UnderPressure.StoreyMapRegistry.UpperStorey
                    : UnderPressure.StoreyMapRegistry.GroundStorey;
                UnderPressure.StoreyMapRegistry.NotifyActiveStoreyChanged(storey.Level, index);
                StoreyPlugin.Log?.LogInfo("Plano lógico activo: " + index + ".");
            }
        }

        internal static void EnterStoreyContext(StoreyInstance storey,
            out LevelContext previous)
        {
            previous = default(LevelContext);
            if (storey?.Level == null || storey.State == null) return;
            previous.State = LevelWorldState.GetValue(storey.Level) as WorldState;
            previous.Events = LevelBuildEvents.GetValue(storey.Level) as BuildEvents;
            previous.EventsState = BuildEventsWorldState.GetValue(storey.Events) as WorldState;
            var buildingLogic = storey.Level.BuildingLogic;
            previous.BuildingState = BuildingLogicWorldState.GetValue(buildingLogic) as WorldState;
            previous.BuildingEvents = BuildingLogicBuildEvents.GetValue(buildingLogic) as BuildEvents;
            previous.Constructing = _constructing;
            LevelWorldState.SetValue(storey.Level, storey.State);
            LevelBuildEvents.SetValue(storey.Level, storey.Events);
            BuildEventsWorldState.SetValue(storey.Events, storey.State);
            BuildingLogicWorldState.SetValue(buildingLogic, storey.State);
            BuildingLogicBuildEvents.SetValue(buildingLogic, storey.Events);
            _constructing = storey;
        }

        internal static void ExitStoreyContext(Level level, LevelContext previous)
        {
            if (level == null) return;
            LevelWorldState.SetValue(level, previous.State ?? _lower?.State);
            LevelBuildEvents.SetValue(level, previous.Events ?? _lower?.Events);
            var events = previous.Events ?? _lower?.Events;
            if (events != null)
                BuildEventsWorldState.SetValue(events, previous.EventsState ?? _lower?.State);
            if (level.BuildingLogic != null)
            {
                BuildingLogicWorldState.SetValue(level.BuildingLogic,
                    previous.BuildingState ?? _active?.State ?? _lower?.State);
                BuildingLogicBuildEvents.SetValue(level.BuildingLogic,
                    previous.BuildingEvents ?? _lower?.Events);
            }
            _constructing = previous.Constructing;
        }

        internal static void EnterActiveInteractionContext(Level level,
            out LevelContext previous)
        {
            var storey = _active != null && ReferenceEquals(_active.Level, level)
                ? _active : _lower;
            EnterStoreyContext(storey, out previous);
        }

        internal static void EnterActiveInteractionContext(out LevelContext previous) =>
            EnterActiveInteractionContext(_lower?.Level, out previous);

        internal static void EnterConstructionContext(string operation,
            out LevelContext previous)
        {
            EnterActiveInteractionContext(out previous);
            if (ReferenceEquals(_active, _upper) &&
                (operation == "TransitionToNewRoomState" ||
                 operation == "TransitionToCopyRoomTemplateBlueprintState" ||
                 operation == "StartItemPlacement"))
                StoreyPlugin.Log?.LogInfo(
                    "Construccion nativa en plano superior: " + operation + ".");
        }

        internal static void ExitCurrentContext(LevelContext previous) =>
            ExitStoreyContext(_lower?.Level, previous);

        internal static void EnterRoomContext(Room room, Level level,
            out LevelContext previous)
        {
            var storey = GetStorey(room?.FloorPlan?.WorldState) ?? _lower;
            if (storey == null || !ReferenceEquals(storey.Level, level))
            {
                previous = default(LevelContext);
                return;
            }
            EnterStoreyContext(storey, out previous);
        }

        internal static void EnterPlotContext(HospitalPlot plot,
            out LevelContext previous)
        {
            if (plot != null && PlotStoreys.TryGetValue(plot, out var storey))
                EnterStoreyContext(storey, out previous);
            else previous = default(LevelContext);
        }

        internal static void UpdateUpperWorld(Level level)
        {
            if (_upper?.State == null || level == null ||
                !ReferenceEquals(_upper.Level, level) ||
                level.MetagameMap == null || level.MetagameMap.IsVisible) return;
            EnterStoreyContext(_upper, out var previous);
            try
            {
                _upper.State.Update(GameTime.deltaTime);
            }
            finally
            {
                ExitStoreyContext(level, previous);
            }
        }

        internal static void RefreshVisualStorey()
        {
            if (_lower?.State == null || _upper?.State == null || _active == null) return;
            RefreshStoreyRenderers(_lower);
            RefreshStoreyRenderers(_upper);
            RefreshCharacterVisibility(true);
        }

        private static void RefreshStoreyRenderers(StoreyInstance storey)
        {
            if (storey?.State == null) return;
            foreach (var map in storey.State.HospitalMaps)
                RefreshMapRenderers(map, storey);
        }

        private static void RefreshMapRenderers(HospitalMap map, StoreyInstance storey)
        {
            if (map == null || storey == null) return;
            var stacked = HasOtherStoreyAt(map, storey);
            var storeyVisible = ReferenceEquals(storey, _active);
            if (storeyVisible || !stacked)
            {
                SetMapRenderers(map, storey, true);
                return;
            }

            if (ReferenceEquals(storey, _upper))
            {
                SetMapRenderers(map, storey, false);
                return;
            }

            // En la planta inferior se conservan el mapa estructural y los objetos
            // originales definidos por la parcela. Solo se oculta lo construido despues.
            SetHierarchyRenderers(map.RoomVisual?.GameObject, true);
            SetHierarchyRenderers(HospitalMapDemolishRoot.GetValue(map) as Transform, true);
            var perimeter = HospitalMapFootprintPerimeter.GetValue(map) as
                HospitalPlotFootprintPerimeter;
            SetHierarchyRenderers(perimeter == null
                ? null : FootprintPerimeterRoot.GetValue(perimeter) as Transform, true);
            foreach (var item in GetStructuralFloorPlans(map).SelectMany(plan => plan.Items))
                if (item != null && item.HasBeenPurchased)
                    SetHierarchyRenderers(item.Visual?.GameObject, false);
            foreach (var room in storey.State.AllRooms.Where(room =>
                         room != null && !ReferenceEquals(room, map.Room) &&
                         ReferenceEquals(room.FloorPlan?.HospitalMap, map)))
                SetHierarchyRenderers(room.FloorPlanVisual?.GameObject, false);
        }

        private static void SetMapRenderers(HospitalMap map, StoreyInstance storey,
            bool visible)
        {
            SetHierarchyRenderers(map.RoomVisual?.GameObject, visible);
            SetHierarchyRenderers(HospitalMapDemolishRoot.GetValue(map) as Transform, visible);
            var perimeter = HospitalMapFootprintPerimeter.GetValue(map) as
                HospitalPlotFootprintPerimeter;
            SetHierarchyRenderers(perimeter == null
                ? null : FootprintPerimeterRoot.GetValue(perimeter) as Transform, visible);
            foreach (var room in storey.State.AllRooms.Where(room =>
                         ReferenceEquals(room?.FloorPlan?.HospitalMap, map)))
                SetHierarchyRenderers(room.FloorPlanVisual?.GameObject, visible);
        }

        private static void SetHierarchyRenderers(GameObject root, bool visible)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                SetRendererVisibility(renderer, visible);
        }

        private static void SetHierarchyRenderers(Transform root, bool visible) =>
            SetHierarchyRenderers(root?.gameObject, visible);

        private static IEnumerable<FloorPlan> GetStructuralFloorPlans(HospitalMap map)
        {
            if (map?.FloorPlan != null) yield return map.FloorPlan;
            if (map?.CorridorFloorPlan != null &&
                !ReferenceEquals(map.CorridorFloorPlan, map.FloorPlan))
                yield return map.CorridorFloorPlan;
        }

        private static bool HasOtherStoreyAt(HospitalMap map, StoreyInstance storey)
        {
            var other = GetOtherStorey(storey);
            if (map == null || other?.State == null) return false;
            var bounds = map.Bounds;
            return other.State.HospitalMaps.Any(otherMap =>
                otherMap != null && bounds.Intersects(otherMap.Bounds));
        }

        internal static void RefreshMovingCharacterVisibility() =>
            RefreshCharacterVisibility(false);

        private static void RefreshCharacterVisibility(bool force)
        {
            var characters = _lower?.Level?.CharacterManager?.AllCharacters;
            if (characters == null) return;
            var activeUpper = ReferenceEquals(_active, _upper);
            foreach (var character in characters)
            {
                var gameObject = character?.GameObject;
                if (gameObject == null) continue;
                var visible = ShouldRenderCharacter(character, activeUpper);
                if (!force && CharacterVisibility.TryGetValue(character, out var previous) &&
                    previous == visible) continue;
                CharacterVisibility[character] = visible;
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                    SetRendererVisibility(renderer, visible);
            }
        }

        internal static bool ShouldRenderCharacter(Character character) =>
            ShouldRenderCharacter(character, ReferenceEquals(_active, _upper));

        private static bool ShouldRenderCharacter(Character character, bool activeUpper)
        {
            if (character == null || _lower?.State == null || _upper?.State == null)
                return true;
            var characterUpper = ElevatorRuntime.IsCharacterUpper(character);
            var storey = characterUpper ? _upper : _lower;
            var map = storey.State.GetHospitalMapAtWorldPosition(character.Position);
            if (map == null || !HasOtherStoreyAt(map, storey)) return true;
            return characterUpper == activeUpper;
        }

        internal static void RefreshItemVisual(RoomItemVisual visual, RoomItem item)
        {
            var storey = GetStorey(item);
            var root = visual?.GameObject;
            if (storey == null || root == null || _active == null) return;
            var map = item.FloorPlan?.HospitalMap;
            var visible = ReferenceEquals(storey, _active) ||
                          !HasOtherStoreyAt(map, storey) ||
                          ReferenceEquals(storey, _lower) &&
                          !item.HasBeenPurchased;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                SetRendererVisibility(renderer, visible);
        }

        internal static void AlignRoomItemEditVisual(RoomItemVisualEdit visual,
            RoomItem item)
        {
            if (visual == null || item?.FloorPlan == null) return;
            var root = RoomItemEditGameObject.GetValue(visual) as GameObject;
            var parent = root?.transform.parent;
            if (parent == null) return;

            var localPosition = root.transform.localPosition;
            localPosition.y = item.FloorPlan.GetAnchorWorldPos().y - parent.position.y;
            root.transform.localPosition = localPosition;
        }

        private static void SetRendererVisibility(Renderer renderer, bool visible)
        {
            if (renderer == null) return;
            if (!visible)
            {
                if (!HiddenRenderers.ContainsKey(renderer))
                    HiddenRenderers[renderer] = renderer.enabled;
                if (renderer.enabled) renderer.enabled = false;
            }
            else if (HiddenRenderers.TryGetValue(renderer, out var wasEnabled))
            {
                renderer.enabled = wasEnabled;
                HiddenRenderers.Remove(renderer);
            }
        }

        private static void SynchronizeCursorModes(StoreyInstance storey)
        {
            var manager = storey?.Level?.CursorManager;
            if (manager == null) return;
            var modes = new List<TH20.CursorMode>();
            var active = CursorManagerMode.GetValue(manager) as TH20.CursorMode;
            if (active != null) modes.Add(active);
            if (CursorManagerModeStack.GetValue(manager) is IEnumerable<TH20.CursorMode> stack)
                modes.AddRange(stack.Where(mode => mode != null && !modes.Contains(mode)));
            foreach (var mode in modes)
            {
                if (mode is CursorSelect select)
                {
                    SelectCursorWorldState.SetValue(select, storey.State);
                    SelectCursorBuildEvents.SetValue(select, storey.Events);
                }
                if (mode is CursorEditHospital cursor)
                    CursorWorldState.SetValue(cursor, storey.State);
                if (!(mode is CursorEditHospitalItem itemCursor)) continue;
                ItemCursorWorldState.SetValue(itemCursor, storey.State);
                BindItemCursor(itemCursor, storey.Events);
            }
        }

        private static void SynchronizeBuildingLogic(StoreyInstance storey)
        {
            var buildingLogic = storey?.Level?.BuildingLogic;
            if (buildingLogic == null || storey.State == null || storey.Events == null) return;
            BuildingLogicWorldState.SetValue(buildingLogic, storey.State);
            BuildingLogicBuildEvents.SetValue(buildingLogic, storey.Events);
        }

        private static void BindItemCursor(CursorEditHospitalItem cursor, BuildEvents target)
        {
            if (cursor == null || target == null) return;
            BindItemCursor(cursor, _lower?.Events, false);
            BindItemCursor(cursor, _upper?.Events, false);
            BindItemCursor(cursor, target, true);
        }

        private static void BindItemCursor(CursorEditHospitalItem cursor,
            BuildEvents events, bool add)
        {
            if (events == null) return;
            BindCursorEvent(events, nameof(BuildEvents.OnRoomItemAdded), cursor,
                "OnRoomItemAdded", add);
            BindCursorEvent(events, nameof(BuildEvents.OnRoomItemPlaced), cursor,
                "OnRoomItemAdded", add);
            BindCursorEvent(events, nameof(BuildEvents.OnBeginItemEdit), cursor,
                "OnBeginItemEdit", add);
            BindCursorEvent(events, nameof(BuildEvents.OnRoomItemDestroyed), cursor,
                "OnRoomItemDestroyed", add);
            BindCursorEvent(events, nameof(BuildEvents.OnMoveRoomEnd), cursor,
                "OnMoveRoomEnd", add);
        }

        private static void BindCursorEvent(BuildEvents events, string fieldName,
            CursorEditHospitalItem cursor, string methodName, bool add)
        {
            var field = AccessTools.Field(typeof(BuildEvents), fieldName);
            var method = AccessTools.Method(typeof(CursorEditHospitalItem), methodName);
            if (field == null || method == null) throw new MissingMemberException(
                typeof(CursorEditHospitalItem).FullName, methodName);
            var handler = Delegate.CreateDelegate(field.FieldType, cursor, method);
            var current = field.GetValue(events) as Delegate;
            var contains = current?.GetInvocationList().Contains(handler) == true;
            if (add && !contains) current = Delegate.Combine(current, handler);
            else if (!add && contains) current = Delegate.Remove(current, handler);
            field.SetValue(events, current);
        }

        internal static void ActivatePlot(HospitalPlot plot)
        {
            if (plot != null && PlotStoreys.TryGetValue(plot, out var storey)) Activate(storey);
        }

        internal static void TogglePlot(HospitalPlot plot)
        {
            if (plot == null || !PlotStoreys.TryGetValue(plot, out var source) ||
                _lower == null || _upper == null) return;
            var target = ReferenceEquals(source, _upper) ? _lower : _upper;
            MovePlot(plot, target, true);
        }

        private static void MovePlot(HospitalPlot plot, StoreyInstance target, bool select)
        {
            if (plot == null || target == null ||
                !PlotStoreys.TryGetValue(plot, out var source) || ReferenceEquals(source, target))
                return;
            var level = source.Level;
            var map = plot.HospitalMap;
            if (map == null) return;
            try
            {
                TransferMapOwnership(plot, map, source, target);
                PlotStoreys[plot] = target;
                UnderPressure.StoreyMapRegistry.SetStorey(plot.Definition,
                    ReferenceEquals(target, _upper) ? 1 : 0);
                RecalculateWorldLighting();
                Activate(select ? target : source);
                RefreshRowArrows();
                if (select) level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plot);
            }
            catch (Exception exception)
            {
                StoreyPlugin.Log?.LogError(
                    "No se pudo transferir la parcela sin reconstruirla: " + exception);
                try
                {
                    TransferMapOwnership(plot, map, target, source);
                    PlotStoreys[plot] = source;
                    UnderPressure.StoreyMapRegistry.SetStorey(plot.Definition,
                        ReferenceEquals(source, _upper) ? 1 : 0);
                }
                catch (Exception restoreException)
                {
                    StoreyPlugin.Log?.LogError(
                        "No se pudo devolver la propiedad del mapa a su planta de origen: " +
                        restoreException);
                }
                Activate(source);
                RefreshRowArrows();
                if (select) level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plot);
            }
            finally
            {
                _constructing = null;
            }
        }

        private static void TransferMapOwnership(HospitalPlot plot, HospitalMap map,
            StoreyInstance source, StoreyInstance target)
        {
            if (plot == null || map == null || source?.State == null || target?.State == null)
                throw new ArgumentNullException(nameof(map));

            var level = source.Level;
            var rooms = source.State.AllRooms.Concat(target.State.AllRooms)
                .Where(room => room?.FloorPlan != null &&
                               ReferenceEquals(room.FloorPlan.HospitalMap, map))
                .Concat(map.Room == null ? Enumerable.Empty<Room>() : new[] { map.Room })
                .Distinct().ToArray();
            var floorPlans = rooms.Select(room => room.FloorPlan)
                .Concat(map.FloorPlan == null
                    ? Enumerable.Empty<FloorPlan>() : new[] { map.FloorPlan })
                .Concat(map.CorridorFloorPlan == null
                    ? Enumerable.Empty<FloorPlan>() : new[] { map.CorridorFloorPlan })
                .Where(floorPlan => floorPlan != null).Distinct().ToArray();
            var roomItems = floorPlans.SelectMany(floorPlan => floorPlan.Items)
                .Distinct().ToArray();

            // Las salas se suscriben al BuildEvents que Level expone al registrarlas.
            // Retirarlas de ambos contextos hace la operacion repetible y permite
            // devolver el mismo mapa al origen si falla cualquier registro posterior.
            foreach (var events in new[] { source.Events, target.Events }.Distinct())
            {
                LevelBuildEvents.SetValue(level, events);
                foreach (var room in rooms) RoomUnregisterEvents.Invoke(room, null);
            }

            source.State.HospitalMaps.Remove(map);
            target.State.HospitalMaps.Remove(map);
            GetUnbuiltHospitalMaps(source.State).Remove(map);
            GetUnbuiltHospitalMaps(target.State).Remove(map);
            source.State.HospitalPlots.Remove(plot);
            target.State.HospitalPlots.Remove(plot);
            foreach (var room in rooms)
            {
                source.State.AllRooms.Remove(room);
                target.State.AllRooms.Remove(room);
            }
            foreach (var item in roomItems)
            {
                source.State.RemoveNeedSatisfyingRoomItem(item);
                target.State.RemoveNeedSatisfyingRoomItem(item);
            }

            PlotWorldState.SetValue(plot, target.State);
            HospitalMapWorldState.SetValue(map, target.State);
            target.State.HospitalPlots.Add(plot);
            foreach (var room in rooms) target.State.AllRooms.Add(room);
            foreach (var item in roomItems) target.State.AddNeedSatisfyingRoomItem(item);

            MapStoreys[map] = target;
            AttachMapVisuals(map, target);
            foreach (var room in rooms) RebindRoomVisual(room.FloorPlanVisual, target);

            _constructing = target;
            LevelWorldState.SetValue(level, target.State);
            LevelBuildEvents.SetValue(level, target.Events);
            foreach (var room in rooms) RoomRegisterEvents.Invoke(room, null);
            if (plot.Built) target.State.SetBuiltHospitalMap(map);
            else target.State.SetUnbuiltHospitalPlot(map);
            SynchronizePlotShellLighting(map, target);

            source.State.UpdateNavigation();
            source.State.CalculateLighting();
            EnsureUpperNavigationHeight();
        }

        internal static bool IsUpperNavigation(TH20.NavMesh navMesh) =>
            navMesh != null && _upper?.State != null &&
            ReferenceEquals(StateNavMesh.GetValue(_upper.State), navMesh);

        internal static float NavigationHeight(TH20.NavMesh navMesh) =>
            IsUpperNavigation(navMesh)
                ? UnderPressure.StoreyMapRegistry.StoreyHeight : 0f;

        internal static float NavigationHeight(WorldState state) =>
            ReferenceEquals(GetStorey(state), _upper)
                ? UnderPressure.StoreyMapRegistry.StoreyHeight : 0f;

        internal static void EnsureUpperNavigationHeight(TH20.NavMesh updated = null)
        {
            var navMesh = _upper?.State == null
                ? null : StateNavMesh.GetValue(_upper.State) as TH20.NavMesh;
            if (navMesh == null || updated != null && !ReferenceEquals(navMesh, updated))
                return;
            var data = NavMeshData.GetValue(navMesh) as UnityEngine.AI.NavMeshData;
            if (data == null || !RaisedNavigationData.Add(data)) return;

            var current = (UnityEngine.AI.NavMeshDataInstance)
                NavMeshDataInstance.GetValue(navMesh);
            if (current.valid) current.Remove();
            var position = data.position + Vector3.up *
                UnderPressure.StoreyMapRegistry.StoreyHeight;
            var raised = UnityEngine.AI.NavMesh.AddNavMeshData(
                data, position, data.rotation);
            NavMeshDataInstance.SetValue(navMesh, raised);
            StoreyPlugin.Log?.LogInfo(
                "Malla de navegación superior instalada físicamente a " +
                UnderPressure.StoreyMapRegistry.StoreyHeight.ToString("F2") + " m.");
        }

        private static List<HospitalMap> GetUnbuiltHospitalMaps(WorldState state) =>
            StateUnbuiltHospitalMaps.GetValue(state) as List<HospitalMap> ??
            throw new MissingFieldException(typeof(WorldState).FullName,
                "_unbuiltHospitalMaps");

        private static void RebindRoomVisual(RoomFloorPlanVisual visual,
            StoreyInstance target)
        {
            if (visual == null) return;
            RoomVisualWorldState.SetValue(visual, target.State);
            RoomVisualBuildEvents.SetValue(visual, target.Events);
            AttachRoot(visual.GameObject?.transform, target);
            foreach (var itemVisual in visual.RoomItems)
                if (itemVisual != null)
                    RoomItemVisualBuildEvents.SetValue(itemVisual, target.Events);
        }

        private static void RemoveFromSharedLighting(HospitalMap map)
        {
            if (map?.Room == null || _lower?.Level?.VisualManager?.RoomLightingManager == null) return;
            LightingRoomDeleted.Invoke(_lower.Level.VisualManager.RoomLightingManager,
                new object[] { map.Room });
        }

        private static void SynchronizePlotShellLighting(HospitalMap map,
            StoreyInstance storey)
        {
            var manager = _lower?.Level?.VisualManager?.RoomLightingManager;
            var room = map?.Room;
            var floorPlan = room?.FloorPlan;
            if (manager == null || floorPlan == null) return;
            var registered = LightingBuiltRooms.GetValue(manager) as List<FloorPlan>;
            if (registered == null) return;

            var contains = registered.Contains(floorPlan);
            if (ReferenceEquals(storey, _upper))
            {
                // La sala-base de una parcela representa el exterior y no debe crear
                // un volumen interior sobre la planta inferior ni duplicar la luz arriba.
                if (contains) RemoveFromSharedLighting(map);
                return;
            }

            if (!contains && map.Plot?.Built == true) manager.BuildRoom(room);
        }

        private static void RefreshRowArrows()
        {
            foreach (var control in Resources.FindObjectsOfTypeAll<StoreyPlotRowControl>())
                control.RefreshArrow();
        }

        private static void AttachMapVisuals(HospitalMap map, StoreyInstance storey)
        {
            if (map == null || storey?.Root == null) return;
            AttachRoot(map.RoomVisual?.GameObject?.transform, storey);
            AttachRoot(HospitalMapDemolishRoot.GetValue(map) as Transform, storey);
            var perimeter = HospitalMapFootprintPerimeter.GetValue(map) as
                HospitalPlotFootprintPerimeter;
            if (perimeter != null)
                AttachRoot(FootprintPerimeterRoot.GetValue(perimeter) as Transform, storey);
            NormalizeFootprint(perimeter);
        }

        private static void DetachLowerMapVisuals()
        {
            if (_lower?.State == null) return;
            foreach (var plot in _lower.State.HospitalPlots)
            {
                var map = plot?.HospitalMap;
                if (map == null) continue;
                DetachRoot(map.RoomVisual?.GameObject?.transform);
                DetachRoot(HospitalMapDemolishRoot.GetValue(map) as Transform);
                var perimeter = HospitalMapFootprintPerimeter.GetValue(map) as
                    HospitalPlotFootprintPerimeter;
                if (perimeter != null)
                    DetachRoot(FootprintPerimeterRoot.GetValue(perimeter) as Transform);
            }
        }

        private static void DetachRoot(Transform root)
        {
            if (root != null) root.SetParent(null, true);
        }

        private static void AttachRoot(Transform root, StoreyInstance storey)
        {
            if (root == null || storey?.Root == null) return;
            root.SetParent(storey.Root.transform, false);
            var local = root.localPosition;
            local.y = 0f;
            root.localPosition = local;
        }

        internal static void SetStoreyWorldPosition(Transform value,
            Vector3 logicalPosition)
        {
            if (value == null) return;
            var upperRoot = _upper?.Root?.transform;
            var lowerRoot = _lower?.Root?.transform;
            var root = upperRoot != null &&
                       (ReferenceEquals(value, upperRoot) || value.IsChildOf(upperRoot))
                ? upperRoot
                : lowerRoot != null &&
                  (ReferenceEquals(value, lowerRoot) || value.IsChildOf(lowerRoot))
                    ? lowerRoot : null;
            value.position = root == null ? logicalPosition : root.TransformPoint(logicalPosition);
        }

        internal static IEnumerable<CodeInstruction> ConvertCorridorWorldPositions(
            IEnumerable<CodeInstruction> source)
        {
            var nativeSetter = AccessTools.PropertySetter(typeof(Transform),
                nameof(Transform.position));
            var replacement = AccessTools.Method(typeof(StoreyRuntime),
                nameof(SetStoreyWorldPosition));
            var replaced = false;
            foreach (var code in source)
            {
                if (code.Calls(nativeSetter))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                    replaced = true;
                }
                yield return code;
            }
            if (!replaced) throw new InvalidOperationException(
                "No se encontró la asignación nativa de posición de muros interiores.");
        }

        internal static void NormalizeRoomVisual(RoomFloorPlanVisual visual,
            FloorPlan floorPlan)
        {
            var map = floorPlan?.HospitalMap;
            if (visual == null || map == null) return;
            if (!MapStoreys.TryGetValue(map, out var storey))
            {
                var plot = map.Plot;
                if (plot == null || !PlotStoreys.TryGetValue(plot, out storey)) return;
                MapStoreys[map] = storey;
            }
            AttachRoot(visual.GameObject?.transform, storey);
            RefreshMapRenderers(map, storey);
        }

        internal static void RegisterRebuiltPlotMap(HospitalPlot plot)
        {
            if (plot == null || !PlotStoreys.TryGetValue(plot, out var storey)) return;
            var map = plot.HospitalMap;
            if (map == null) return;
            foreach (var stale in MapStoreys.Where(pair =>
                         !ReferenceEquals(pair.Key, map) &&
                         ReferenceEquals(pair.Key?.Plot, plot)).Select(pair => pair.Key).ToArray())
                MapStoreys.Remove(stale);
            var changed = !MapStoreys.TryGetValue(map, out var registered) ||
                          !ReferenceEquals(registered, storey);
            MapStoreys[map] = storey;
            AttachMapVisuals(map, storey);
            foreach (var room in storey.State.AllRooms.Where(room =>
                         ReferenceEquals(room?.FloorPlan?.HospitalMap, map)))
                RebindRoomVisual(room.FloorPlanVisual, storey);
            SynchronizePlotShellLighting(map, storey);
            RefreshMapRenderers(map, storey);
            if (changed)
                StoreyPlugin.Log?.LogInfo(
                    "HospitalMap reconstruido y registrado en planta " +
                    (ReferenceEquals(storey, _upper) ? "superior" : "inferior") + ".");
        }

        internal static void NormalizeFootprint(HospitalPlotFootprintPerimeter perimeter)
        {
            if (perimeter == null) return;
            var root = FootprintPerimeterRoot.GetValue(perimeter) as Transform;
            if (root == null || root.parent == null) return;
            var storey = ReferenceEquals(root.parent, _upper?.Root?.transform)
                ? _upper : ReferenceEquals(root.parent, _lower?.Root?.transform) ? _lower : null;
            if (storey == null) return;
            AttachRoot(root, storey);
            foreach (var instance in perimeter.Instances)
            {
                if (instance == null) continue;
                var local = instance.transform.localPosition;
                local.y = 0f;
                instance.transform.localPosition = local;
            }
        }

        internal static void ApplyFloorPlanHeight(FloorPlan floorPlan, ref Vector3 position)
        {
            var map = floorPlan?.HospitalMap;
            if (map != null && MapStoreys.TryGetValue(map, out var storey))
                position.y += storey.Root.transform.position.y;
        }

        internal static void ApplyActivePlaneHeight(ref float offset)
        {
            if (_active?.Root != null) offset += _active.Root.transform.position.y;
        }

        internal static float PlotSelectionPlane(float nativeHeight, WorldState state)
        {
            var storey = GetStorey(state);
            return nativeHeight + (storey?.Root != null
                ? storey.Root.transform.position.y : 0f);
        }

        internal static void ApplyActiveCursorHeight(CursorManager manager)
        {
            if (manager == null || _active?.Root == null) return;
            var height = _active.Root.transform.position.y;
            if (Mathf.Approximately(height, 0f)) return;

            var worldPosition = (Vector3)CursorWorldPosition.GetValue(manager);
            worldPosition.y = height;
            CursorWorldPosition.SetValue(manager, worldPosition);

            var smoothedPosition = (Vector3)CursorWorldPositionSmoothed.GetValue(manager);
            smoothedPosition.y = height;
            CursorWorldPositionSmoothed.SetValue(manager, smoothedPosition);

            var visualisation = CursorVisualisation.GetValue(manager) as GameObject;
            if (visualisation == null) return;
            var visualPosition = visualisation.transform.position;
            visualPosition.y = smoothedPosition.y;
            visualisation.transform.position = visualPosition;
        }

        internal static Vector3 FloorPlanWorldOrigin(FloorPlan floorPlan)
        {
            var anchor = floorPlan.Anchor;
            var map = floorPlan.HospitalMap;
            var storey = map != null && MapStoreys.TryGetValue(map, out var registered)
                ? registered : _constructing;
            return new Vector3(anchor.X * GridCoord.CellSize,
                ReferenceEquals(storey, _upper)
                    ? UnderPressure.StoreyMapRegistry.StoreyHeight : 0f,
                anchor.Y * GridCoord.CellSize);
        }

        internal static void RegenerateExteriorLighting(RoomLightingManager manager,
            bool[,] exteriorState, Vector3 origin)
        {
            if (manager == null) return;
            var lowerState = _lower?.State?.ExteriorState.Values;
            if (lowerState == null)
            {
                manager.RegenerateExteriorVolumeLights(exteriorState, origin);
                return;
            }
            var anchor = _lower.State.Anchor;
            var lowerOrigin = new Vector3(anchor.X * GridCoord.CellSize, 0f,
                anchor.Y * GridCoord.CellSize);
            manager.RegenerateExteriorVolumeLights(lowerState, lowerOrigin);
        }

        internal static float ShadowCastingFloorY(RoomLightingManager manager,
            Vector3 volumeCenter)
        {
            var config = RoomLightConfig.GetValue(manager) as RoomLightingManagerConfig;
            if (config == null) return 0f;
            var height = (float)RoomLightHeight.GetValue(manager);
            var bias = (float)RoomLightBaseBias.GetValue(manager);
            var nativeCenter = bias + .5f * Mathf.Max(0f, height - bias) +
                               config.RoomLightOffset.y;
            var floor = volumeCenter.y - nativeCenter;
            return Mathf.Round(floor / UnderPressure.StoreyMapRegistry.StoreyHeight) *
                   UnderPressure.StoreyMapRegistry.StoreyHeight;
        }

        internal static void RegenerateInteriorLighting()
        {
            _lower?.Level?.VisualManager?.RoomLightingManager?
                .RegenerateInteriorVolumeLights();
        }

        private static void RecalculateWorldLighting()
        {
            _lower?.State?.CalculateLighting();
            _upper?.State?.CalculateLighting();
        }

        internal static void AttachToolbar(EditHospitalMenu menu, Level level)
        {
            if (menu == null || !Ensure(level)) return;
            if (_toolbar != null) UnityEngine.Object.Destroy(_toolbar);
            var template = AccessTools.Field(typeof(EditHospitalMenu), "_quitButton")?
                .GetValue(menu) as Button;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (template == null || canvas == null) return;
            var parent = FindEditorRoot(canvas) ?? canvas.transform;
            _toolbar = new GameObject("UP Storey Grid Toolbar", typeof(RectTransform));
            _toolbar.transform.SetParent(parent, false);
            var rect = (RectTransform)_toolbar.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            AddButton(template, rect, UnderPressure.ModLocalization.Get(
                "storey.view.lower_grid"), 110f, ToggleLowerGrid);
            AddButton(template, rect, UnderPressure.ModLocalization.Get(
                "storey.view.upper_grid"), 330f, ToggleUpperGrid);
        }

        private static Transform FindEditorRoot(Canvas canvas)
        {
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            for (var index = 0; index < root.transform.childCount; ++index)
            {
                var child = root.transform.GetChild(index);
                if (child.name == "UP Scenario Editor UI") return child;
            }
            return null;
        }

        private static void AddButton(Button template, Transform parent,
            string label, float x, Action action)
        {
            var button = UnityEngine.Object.Instantiate(template, parent, false);
            button.gameObject.name = "UP Storey Grid - " + label;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = 17f;
            }
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -12f);
            rect.sizeDelta = new Vector2(208f, 40f);
            button.gameObject.SetActive(true);
        }

        internal static void Destroy(bool clearConfigs)
        {
            ElevatorRuntime.Destroy();
            RestoreHiddenVisuals();
            var lower = _lower;
            var upper = _upper;
            if (lower?.Level != null && lower.State != null)
            {
                LevelWorldState.SetValue(lower.Level, lower.State);
                LevelBuildEvents.SetValue(lower.Level, lower.Events);
                if (lower.Level.CursorManager != null &&
                    lower.Level.CursorManager.TryGetActiveMode<CursorEditHospitalItem>(
                        out var itemCursor))
                {
                    ItemCursorWorldState.SetValue(itemCursor, lower.State);
                    BindItemCursor(itemCursor, lower.Events);
                }
            }
            if (upper != null)
                foreach (var plot in upper.State?.HospitalPlots.ToArray() ??
                         new HospitalPlot[0])
                    if (plot?.HospitalMap != null) RemoveFromSharedLighting(plot.HospitalMap);
            if (clearConfigs && _toolbar != null) UnityEngine.Object.Destroy(_toolbar);
            if (clearConfigs) _toolbar = null;
            DetachLowerMapVisuals();
            if (_lower != null) UnityEngine.Object.Destroy(_lower.Root);
            _lower = null;
            _active = null;
            PlotOrder.Clear();
            PlotStoreys.Clear();
            MapStoreys.Clear();
            RaisedNavigationData.Clear();
            _upper = null;
            _constructing = null;
            if (upper != null)
            {
                var normalEvents = upper.Level?.BuildEvents;
                try
                {
                    if (upper.Level != null)
                        LevelBuildEvents.SetValue(upper.Level, upper.Events);
                    upper.State?.Destroy();
                }
                catch (Exception exception)
                {
                    StoreyPlugin.Log?.LogError(
                        "Error destruyendo el segundo WorldState: " + exception);
                }
                finally
                {
                    if (upper.Level != null)
                        LevelBuildEvents.SetValue(upper.Level, normalEvents);
                    UnityEngine.Object.Destroy(upper.Root);
                }
            }
            if (_gridMaterial != null) UnityEngine.Object.Destroy(_gridMaterial);
            _gridMaterial = null;
            if (clearConfigs) Configs.Clear();
        }

        private static void RestoreHiddenVisuals()
        {
            foreach (var pair in HiddenRenderers.ToArray())
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            HiddenRenderers.Clear();
            CharacterVisibility.Clear();
        }

        internal static StoreyInstance GetStorey(WorldState state)
        {
            if (ReferenceEquals(state, _lower?.State)) return _lower;
            if (ReferenceEquals(state, _upper?.State)) return _upper;
            return null;
        }

        internal static StoreyInstance GetStorey(RoomItem item) =>
            GetStorey(item?.FloorPlan?.WorldState);

        internal static StoreyInstance GetOtherStorey(StoreyInstance storey)
        {
            if (ReferenceEquals(storey, _lower)) return _upper;
            if (ReferenceEquals(storey, _upper)) return _lower;
            return null;
        }

        internal static void RunInStorey(StoreyInstance storey, Action action)
        {
            if (storey == null || action == null) return;
            EnterStoreyContext(storey, out var previous);
            try
            {
                action();
            }
            finally
            {
                ExitStoreyContext(storey.Level, previous);
            }
        }
    }

    internal sealed class StoreyPlotRowControl : MonoBehaviour
    {
        private HospitalPlot _plot;
        private Button _button;
        private TMP_Text _arrow;

        internal void Bind(PlotSelectionItem row, HospitalPlot plot)
        {
            _plot = plot;
            if (_button == null) Create(row);
            Position(row);
            RefreshArrow();
            _button.interactable = true;
        }

        internal void RefreshArrow()
        {
            if (_arrow == null) return;
            _arrow.text = UnderPressure.StoreyMapRegistry.GetStorey(_plot) ==
                          UnderPressure.StoreyMapRegistry.UpperStorey ? "↑" : "↓";
        }

        private void Create(PlotSelectionItem row)
        {
            var root = new GameObject("UP Atajo de planta", typeof(RectTransform),
                typeof(Button));
            root.transform.SetParent(row.transform, false);
            _button = root.GetComponent<Button>();
            _button.onClick.AddListener(() => UnderPressure.StoreyMapRegistry.Toggle(_plot));
            var sound = root.AddComponent<ButtonSFX>();
            sound.AudioEvent = ButtonSFX.ButtonAudioEvent.Default;

            var arrowObject = new GameObject("Flecha", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            arrowObject.transform.SetParent(root.transform, false);
            var arrowRect = (RectTransform)arrowObject.transform;
            arrowRect.anchorMin = Vector2.zero;
            arrowRect.anchorMax = Vector2.one;
            arrowRect.offsetMin = Vector2.zero;
            arrowRect.offsetMax = Vector2.zero;
            _arrow = arrowObject.GetComponent<TextMeshProUGUI>();
            var label = AccessTools.Field(typeof(PlotSelectionItem), "_text")?
                .GetValue(row) as TMP_Text;
            if (label != null) _arrow.font = label.font;
            _arrow.fontSize = 16f;
            _arrow.fontStyle = FontStyles.Bold;
            _arrow.alignment = TextAlignmentOptions.Center;
            _arrow.color = Color.white;
            _arrow.raycastTarget = true;
            _button.targetGraphic = _arrow;
        }

        private void Position(PlotSelectionItem row)
        {
            var toggle = AccessTools.Field(typeof(PlotSelectionItem), "_toggleState")?
                .GetValue(row) as Toggle;
            var toggleRect = toggle?.transform as RectTransform;
            var rowRect = row.transform as RectTransform;
            var rect = _button?.transform as RectTransform;
            if (toggleRect == null || rowRect == null || rect == null) return;
            var corners = new Vector3[4];
            toggleRect.GetWorldCorners(corners);
            var dollarLeft = rowRect.InverseTransformPoint(corners[0]).x;
            rect.SetParent(row.transform, false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
            rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(
                dollarLeft - rowRect.rect.xMin - 1f, 0f);
            rect.sizeDelta = new Vector2(16f, toggleRect.rect.height);
            rect.localScale = Vector3.one;
            var layout = _button.GetComponent<LayoutElement>() ??
                _button.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        private void OnDestroy() => _button?.onClick.RemoveAllListeners();
    }

    [HarmonyPatch(typeof(WorldState), MethodType.Constructor,
        new[] { typeof(WorldState.Config), typeof(Level), typeof(Metagame),
            typeof(VisualManager), typeof(Material), typeof(RoomItemVisualEdit.Config) })]
    [HarmonyAfter("artef.twopointhospital.underpressure.editor")]
    [HarmonyPriority(Priority.Last)]
    internal static class StoreyCaptureConfigPatch
    {
        private static void Prefix(WorldState.Config __0, Level __1) =>
            StoreyRuntime.CaptureConfig(__0, __1);
    }

    [HarmonyPatch(typeof(WorldState), "CalculateLighting")]
    internal static class StoreyLightingIsolationPatch
    {
        private static readonly MethodInfo NativeExterior = AccessTools.Method(
            typeof(RoomLightingManager), nameof(RoomLightingManager.RegenerateExteriorVolumeLights));
        private static readonly MethodInfo Replacement = AccessTools.Method(
            typeof(StoreyRuntime), nameof(StoreyRuntime.RegenerateExteriorLighting));

        private static void Prefix(WorldState __instance, out StoreyInstance __state) =>
            StoreyRuntime.EnterWorld(__instance, out __state);

        private static void Postfix(StoreyInstance __state) =>
            StoreyRuntime.ExitContext(__state);

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> source)
        {
            var replaced = false;
            foreach (var code in source)
            {
                if (code.Calls(NativeExterior))
                {
                    replaced = true;
                    yield return new CodeInstruction(OpCodes.Call, Replacement)
                        .MoveLabelsFrom(code);
                }
                else yield return code;
            }
            if (!replaced) throw new InvalidOperationException(
                "No se encontró la regeneración nativa de iluminación exterior.");
        }
    }

    [HarmonyPatch(typeof(HospitalMap), "RemoveTerrainDetailMeshes")]
    internal static class StoreyTerrainIsolationPatch
    {
        private static bool Prefix() => !StoreyRuntime.Constructing;
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    [HarmonyAfter("artef.twopointhospital.underpressure.editor")]
    [HarmonyPriority(Priority.Last)]
    internal static class StoreyToolbarPatch
    {
        private static void Postfix(EditHospitalMenu __instance, Level __0) =>
            StoreyRuntime.AttachToolbar(__instance, __0);
    }

    [HarmonyPatch(typeof(PlotSelectionItem), "Setup")]
    [HarmonyAfter("artef.twopointhospital.underpressure.editor")]
    [HarmonyPriority(Priority.Last)]
    internal static class StoreyPlotRowPatch
    {
        private static void Postfix(PlotSelectionItem __instance, HospitalPlot __0) =>
            StoreyRuntime.BindPlotRow(__instance, __0);
    }

    [HarmonyPatch(typeof(PlotSelectionItem), "<Setup>b__11_0")]
    internal static class StoreyPlotSelectionPatch
    {
        private static readonly FieldInfo PlotField =
            AccessTools.Field(typeof(PlotSelectionItem), "_plot");

        private static void Prefix(PlotSelectionItem __instance) =>
            StoreyRuntime.ActivatePlot(PlotField.GetValue(__instance) as HospitalPlot);
    }

    [HarmonyPatch(typeof(CursorEditHospital), "OnSelectHospitalPlot")]
    internal static class StoreyNativePlotSelectionPatch
    {
        private static void Prefix(HospitalPlot __0) => StoreyRuntime.ActivatePlot(__0);
        private static void Postfix() => StoreyRuntime.RegenerateInteriorLighting();
    }

    [HarmonyPatch(typeof(FloorPlan), nameof(FloorPlan.GetAnchorWorldPos))]
    internal static class StoreyFloorPlanAnchorPatch
    {
        private static void Postfix(FloorPlan __instance, ref Vector3 __result) =>
            StoreyRuntime.ApplyFloorPlanHeight(__instance, ref __result);
    }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), "UpdateFromRoom")]
    internal static class StoreyFloorPlanVisualPatch
    {
        private static void Postfix(RoomFloorPlanVisual __instance, FloorPlan __0) =>
            StoreyRuntime.NormalizeRoomVisual(__instance, __0);
    }

    [HarmonyPatch(typeof(TH20.NavMesh), "UpdateFromRooms")]
    internal static class StoreyNavigationHeightPatch
    {
        private static void Postfix(TH20.NavMesh __instance) =>
            StoreyRuntime.EnsureUpperNavigationHeight(__instance);
    }

    [HarmonyPatch]
    internal static class StoreyCorridorWallPositionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CorridorWallsVisual),
                "CreateWallObjects");
            yield return AccessTools.Method(typeof(CorridorWallsVisual),
                "AddPillarObject");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> source) =>
            StoreyRuntime.ConvertCorridorWorldPositions(source);
    }

    [HarmonyPatch(typeof(RoomItemVisual), "UpdateFrom")]
    internal static class StoreyRoomItemVisualPatch
    {
        private static void Postfix(RoomItemVisual __instance, RoomItem __0) =>
            StoreyRuntime.RefreshItemVisual(__instance, __0);
    }

    [HarmonyPatch(typeof(RoomItemVisualEdit), "UpdateFrom")]
    internal static class StoreyRoomItemEditVisualPatch
    {
        private static void Postfix(RoomItemVisualEdit __instance, RoomItem __0) =>
            StoreyRuntime.AlignRoomItemEditVisual(__instance, __0);
    }

    [HarmonyPatch(typeof(HospitalPlot), "RefreshHospitalMap")]
    internal static class StoreyRebuiltPlotMapPatch
    {
        private static void Postfix(HospitalPlot __instance) =>
            StoreyRuntime.RegisterRebuiltPlotMap(__instance);
    }

    [HarmonyPatch(typeof(HospitalPlotFootprintPerimeter), "Refresh")]
    internal static class StoreyFootprintRootPatch
    {
        private static void Postfix(HospitalPlotFootprintPerimeter __instance) =>
            StoreyRuntime.NormalizeFootprint(__instance);
    }

    [HarmonyPatch(typeof(CursorManager), nameof(CursorManager.SetPlaneOffset))]
    internal static class StoreyCursorPlanePatch
    {
        private static void Prefix(ref float __0) =>
            StoreyRuntime.ApplyActivePlaneHeight(ref __0);
    }

    [HarmonyPatch(typeof(CursorManager), "UpdateCursorPosition")]
    internal static class StoreyCursorPositionPatch
    {
        private static void Postfix(CursorManager __instance) =>
            StoreyRuntime.ApplyActiveCursorHeight(__instance);
    }

    [HarmonyPatch(typeof(CursorManager), "Update")]
    internal static class StoreyCursorInteractionContextPatch
    {
        private static void Prefix(out StoreyRuntime.LevelContext __state) =>
            StoreyRuntime.EnterActiveInteractionContext(out __state);

        private static Exception Finalizer(StoreyRuntime.LevelContext __state,
            Exception __exception)
        {
            StoreyRuntime.ExitCurrentContext(__state);
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class StoreyBuildingLogicContextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[]
            {
                "TransitionToNewRoomState",
                "TransitionToEditRoomBlueprintState",
                "TransitionToCopyRoomBlueprintState",
                "TransitionToEditRoomObjectsState",
                "StartEditingRoomObject",
                "StopEditingRoomObject",
                "TransitionToCopyRoomTemplateBlueprintState",
                "TransitionToNullState",
                "ApplyRoomChanges",
                "ChangeRoomBuildMode",
                "TryAcceptRoomChanges",
                "AcceptRoomChanges"
            };
            foreach (var name in names)
            {
                var method = AccessTools.Method(typeof(BuildingLogic), name);
                if (method != null) yield return method;
            }
        }

        private static void Prefix(MethodBase __originalMethod,
            out StoreyRuntime.LevelContext __state) =>
            StoreyRuntime.EnterConstructionContext(__originalMethod.Name, out __state);

        private static Exception Finalizer(StoreyRuntime.LevelContext __state,
            Exception __exception)
        {
            StoreyRuntime.ExitCurrentContext(__state);
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class StoreyBuildEventsContextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[]
            {
                "StartItemPlacement",
                "StartItemEdit",
                "BeginItemEditBuildMode",
                "MoveRoom",
                "RoomItemDestroy"
            };
            foreach (var name in names)
            {
                var method = AccessTools.Method(typeof(BuildEvents), name);
                if (method != null) yield return method;
            }
        }

        private static void Prefix(MethodBase __originalMethod,
            out StoreyRuntime.LevelContext __state) =>
            StoreyRuntime.EnterConstructionContext(__originalMethod.Name, out __state);

        private static Exception Finalizer(StoreyRuntime.LevelContext __state,
            Exception __exception)
        {
            StoreyRuntime.ExitCurrentContext(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SelectMenuHospitalPlot), "Setup")]
    internal static class StoreyPlotMenuContextPatch
    {
        private static void Prefix(Room __0, Level __1,
            out StoreyRuntime.LevelContext __state) =>
            StoreyRuntime.EnterRoomContext(__0, __1, out __state);

        private static Exception Finalizer(Level __1, StoreyRuntime.LevelContext __state,
            Exception __exception)
        {
            StoreyRuntime.ExitStoreyContext(__1, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(HospitalPlot), nameof(HospitalPlot.Buy))]
    internal static class StoreyPlotPurchaseContextPatch
    {
        private static void Prefix(HospitalPlot __instance,
            out StoreyRuntime.LevelContext __state) =>
            StoreyRuntime.EnterPlotContext(__instance, out __state);

        private static Exception Finalizer(StoreyRuntime.LevelContext __state,
            Exception __exception)
        {
            StoreyRuntime.ExitCurrentContext(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Level), "Update")]
    internal static class StoreyParallelWorldUpdatePatch
    {
        private static void Postfix(Level __instance)
        {
            StoreyRuntime.UpdateUpperWorld(__instance);
            StoreyRuntime.RefreshMovingCharacterVisibility();
        }
    }

    [HarmonyPatch(typeof(StatusIconManager), "SetEmitterVisible")]
    internal static class StoreyCharacterStatusIconPatch
    {
        private static void Prefix(IStatusIconEmitter __0, ref bool __1)
        {
            if (__0 is Character character && !StoreyRuntime.ShouldRenderCharacter(character))
                __1 = false;
        }
    }

    [HarmonyPatch(typeof(CursorSelectionHelpers), nameof(CursorSelectionHelpers.GetPlot))]
    internal static class StoreyPlotSelectionPlanePatch
    {
        private static readonly FieldInfo FootprintHeight =
            AccessTools.Field(typeof(HospitalPlotDefinition), "FootprintYOffset");
        private static readonly MethodInfo Replacement =
            AccessTools.Method(typeof(StoreyRuntime), nameof(StoreyRuntime.PlotSelectionPlane));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var codes = source.ToList();
            var replaced = false;
            for (var index = 0; index < codes.Count; ++index)
            {
                if (codes[index].opcode != OpCodes.Ldfld ||
                    !Equals(codes[index].operand, FootprintHeight)) continue;
                codes.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(index + 2, new CodeInstruction(OpCodes.Call, Replacement));
                replaced = true;
                break;
            }
            if (!replaced) throw new InvalidOperationException(
                "No se localizó la altura nativa del plano de selección de parcelas.");
            return codes;
        }
    }

    [HarmonyPatch(typeof(RoomLightingManager), "RegenerateInteriorVolumeLights")]
    internal static class StoreyInteriorLightingOriginPatch
    {
        private static readonly MethodInfo AnchorGetter =
            AccessTools.PropertyGetter(typeof(FloorPlan), nameof(FloorPlan.Anchor));
        private static readonly MethodInfo ToWorld =
            AccessTools.Method(typeof(GridCoordUtils), nameof(GridCoordUtils.ToWorldPosition));
        private static readonly MethodInfo Replacement =
            AccessTools.Method(typeof(StoreyRuntime), nameof(StoreyRuntime.FloorPlanWorldOrigin));
        private static readonly FieldInfo VectorY =
            AccessTools.Field(typeof(Vector3), nameof(Vector3.y));
        private static readonly MethodInfo ShadowFloor = AccessTools.Method(
            typeof(StoreyRuntime), nameof(StoreyRuntime.ShadowCastingFloorY));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var codes = source.ToList();
            var replacedOrigin = false;
            var replacedShadowFloor = false;
            for (var index = 0; index + 1 < codes.Count; ++index)
            {
                if (codes[index].Calls(AnchorGetter) && codes[index + 1].Calls(ToWorld))
                {
                    codes[index] = new CodeInstruction(OpCodes.Call, Replacement)
                        .MoveLabelsFrom(codes[index]);
                    codes[index + 1].opcode = OpCodes.Nop;
                    codes[index + 1].operand = null;
                    replacedOrigin = true;
                }
                if (index + 2 >= codes.Count || codes[index].opcode != OpCodes.Ldloca_S ||
                    codes[index + 1].opcode != OpCodes.Ldc_R4 ||
                    !(codes[index + 1].operand is float value) || value != 0f ||
                    codes[index + 2].opcode != OpCodes.Stfld ||
                    !Equals(codes[index + 2].operand, VectorY)) continue;
                codes[index + 1] = new CodeInstruction(OpCodes.Ldarg_0)
                    .MoveLabelsFrom(codes[index + 1]);
                codes.Insert(index + 2,
                    new CodeInstruction(OpCodes.Ldloc, codes[index].operand));
                codes.Insert(index + 3, new CodeInstruction(OpCodes.Call, ShadowFloor));
                replacedShadowFloor = true;
                index += 4;
            }
            if (!replacedOrigin) throw new InvalidOperationException(
                "No se encontró el origen nativo de iluminación interior.");
            if (!replacedShadowFloor) throw new InvalidOperationException(
                "No se encontró la altura nativa del volumen de sombra interior.");
            return codes;
        }
    }

    [HarmonyPatch(typeof(Level), "Destroy")]
    internal static class StoreyDestroyPatch
    {
        private static void Prefix() => StoreyRuntime.Destroy(true);
    }

    [HarmonyPatch(typeof(Metagame), "OnLevelLoaded")]
    [HarmonyAfter("artef.twopointhospital.underpressure.editor")]
    [HarmonyPriority(Priority.Last)]
    internal static class StoreyLevelLoadedPatch
    {
        private static void Postfix(Level __0)
        {
            if (StoreyRuntime.Ensure(__0))
                StoreyPlugin.Log?.LogInfo(
                    "Segundo WorldState inicializado al completar la carga jugable del nivel.");
        }
    }
}
