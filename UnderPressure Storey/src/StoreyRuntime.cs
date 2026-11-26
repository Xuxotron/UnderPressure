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
        private static readonly FieldInfo LevelWorldState =
            AccessTools.Field(typeof(Level), "_worldState");
        private static readonly FieldInfo PlotWorldState =
            AccessTools.Field(typeof(HospitalPlot), "_worldState");
        private static readonly FieldInfo CursorWorldState =
            AccessTools.Field(typeof(CursorEditHospital), "_worldState");
        private static readonly FieldInfo ItemCursorWorldState =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_worldState");
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
        private static readonly MethodInfo LightingRoomDeleted =
            AccessTools.Method(typeof(RoomLightingManager), "OnRoomDeleted");
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

        private static StoreyInstance _lower;
        private static StoreyInstance _upper;
        private static StoreyInstance _constructing;
        private static StoreyInstance _active;
        private static GameObject _toolbar;
        private static Material _gridMaterial;

        internal static bool Constructing => _constructing != null;

        internal static void Register() => UnderPressure.StoreyMapRegistry.RegisterRuntime(
            GetPlots, GetWorld, TogglePlot, Refresh, ActivateGround, OnPlotAdded, OnPlotRemoved);

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

            var isolatedEvents = new BuildEvents();
            var upper = new StoreyInstance(level, isolatedEvents, 1);
            var normalEvents = level.BuildEvents;
            var savedPostConstruct = level.PostConstruct;
            _constructing = upper;
            try
            {
                LevelBuildEvents.SetValue(level, isolatedEvents);
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
                isolatedEvents.Initialise(level);
                RemoveUpperSeed();
                BuildVectorGrid(upper, bounds);
                Activate(_lower);
                foreach (var plot in PlotOrder.Where(plot =>
                             UnderPressure.StoreyMapRegistry.GetStorey(plot) == 1).ToArray())
                    MovePlot(plot, _upper, false);
                StoreyPlugin.Log?.LogInfo(
                    "Segundo WorldState creado; rejillas vectoriales en Y=0 e Y=2.");
                return true;
            }
            catch (Exception exception)
            {
                StoreyPlugin.Log?.LogError(
                    "No se pudo crear el segundo WorldState: " + exception);
                DetachLowerMapVisuals();
                UnityEngine.Object.Destroy(_lower.Root);
                UnityEngine.Object.Destroy(upper.Root);
                isolatedEvents.Destroy();
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

        private static void OnPlotAdded(Level level, HospitalPlot plot)
        {
            if (plot == null || _active == null || !ReferenceEquals(_active.Level, level)) return;
            if (!PlotOrder.Contains(plot)) PlotOrder.Add(plot);
            PlotStoreys[plot] = _active;
            if (plot.HospitalMap != null) MapStoreys[plot.HospitalMap] = _active;
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
            _active = storey;
            LevelWorldState.SetValue(storey.Level, storey.State);
            LevelBuildEvents.SetValue(storey.Level, storey.Events);
            if (storey.Level.CursorManager != null &&
                storey.Level.CursorManager.TryGetActiveMode<CursorEditHospital>(out var cursor))
                CursorWorldState.SetValue(cursor, storey.State);
            if (storey.Level.CursorManager != null &&
                storey.Level.CursorManager.TryGetActiveMode<CursorEditHospitalItem>(
                    out var itemCursor))
            {
                ItemCursorWorldState.SetValue(itemCursor, storey.State);
                BindItemCursor(itemCursor, storey.Events);
            }
            storey.Level.CursorManager?.SetPlaneOffset(0f);
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
            var oldMap = plot.HospitalMap;
            HospitalMap newMap = null;
            try
            {
                _constructing = source;
                LevelBuildEvents.SetValue(level, source.Events);
                if (oldMap != null)
                {
                    if (ReferenceEquals(source, _upper)) RemoveFromSharedLighting(oldMap);
                    MapStoreys.Remove(oldMap);
                    source.State.DestroyHospitalMap(oldMap);
                    PlotMap.SetValue(plot, null);
                }
                source.State.HospitalPlots.Remove(plot);
                PlotWorldState.SetValue(plot, target.State);
                if (!target.State.HospitalPlots.Contains(plot)) target.State.HospitalPlots.Add(plot);

                // HospitalPlot.RefreshHospitalMap destruye y desvincula el mapa anterior
                // antes de construir el siguiente. Mantener dos HospitalMap de la misma
                // parcela a la vez deja vivos dos juegos de LandscapeRoomItem y la
                // destruccion tardia del origen elimina los visuales recien reconstruidos.
                _constructing = target;
                LevelBuildEvents.SetValue(level, target.Events);
                newMap = target.State.CreateHospitalMap(plot, false);
                PlotMap.SetValue(plot, newMap);
                MapStoreys[newMap] = target;
                AttachMapVisuals(newMap, target);
                if (plot.Built) target.State.SetBuiltHospitalMap(newMap);
                else target.State.SetUnbuiltHospitalPlot(newMap);

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
                StoreyPlugin.Log?.LogError("No se pudo trasladar la parcela; se reconstruira en su planta de origen: " + exception);
                if (newMap != null)
                {
                    try
                    {
                        _constructing = target;
                        LevelBuildEvents.SetValue(level, target.Events);
                        RemoveFromSharedLighting(newMap);
                        target.State.DestroyHospitalMap(newMap);
                    }
                    catch (Exception cleanup)
                    {
                        StoreyPlugin.Log?.LogError("No se pudo limpiar el mapa destino fallido: " + cleanup);
                    }
                    MapStoreys.Remove(newMap);
                }
                PlotMap.SetValue(plot, null);
                target.State.HospitalPlots.Remove(plot);
                PlotWorldState.SetValue(plot, source.State);
                if (!source.State.HospitalPlots.Contains(plot)) source.State.HospitalPlots.Add(plot);
                try
                {
                    _constructing = source;
                    LevelBuildEvents.SetValue(level, source.Events);
                    var restoredMap = source.State.CreateHospitalMap(plot, false);
                    PlotMap.SetValue(plot, restoredMap);
                    MapStoreys[restoredMap] = source;
                    AttachMapVisuals(restoredMap, source);
                    if (plot.Built) source.State.SetBuiltHospitalMap(restoredMap);
                    else source.State.SetUnbuiltHospitalPlot(restoredMap);
                    PlotStoreys[plot] = source;
                    UnderPressure.StoreyMapRegistry.SetStorey(plot.Definition,
                        ReferenceEquals(source, _upper) ? 1 : 0);
                    RecalculateWorldLighting();
                    RefreshRowArrows();
                    if (select) level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plot);
                }
                catch (Exception restoreException)
                {
                    StoreyPlugin.Log?.LogError(
                        "No se pudo reconstruir la parcela en su planta de origen: " +
                        restoreException);
                }
                Activate(source);
            }
            finally
            {
                _constructing = null;
            }
        }

        private static void RemoveFromSharedLighting(HospitalMap map)
        {
            if (map?.Room == null || _lower?.Level?.VisualManager?.RoomLightingManager == null) return;
            LightingRoomDeleted.Invoke(_lower.Level.VisualManager.RoomLightingManager,
                new object[] { map.Room });
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

        internal static void NormalizeRoomVisual(RoomFloorPlanVisual visual,
            FloorPlan floorPlan)
        {
            var map = floorPlan?.HospitalMap;
            if (visual == null || map == null ||
                !MapStoreys.TryGetValue(map, out var storey)) return;
            AttachRoot(visual.GameObject?.transform, storey);
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
                    upper.Events?.Destroy();
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
}
