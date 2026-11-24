// Purpose: Mantiene dos mapas nativos independientes y presenta el superior a +2 m.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Storey
{
    public static class StoreyApi
    {
        public const int LowerStorey = 0;
        public const int UpperStorey = 1;
        public const float UpperVisualHeight = 2f;

        public static void RegisterDefinition(HospitalPlotDefinition value, int storey) =>
            StoreyRuntime.RegisterDefinition(value, storey);
        public static void UnregisterDefinition(HospitalPlotDefinition value) =>
            StoreyRuntime.UnregisterDefinition(value);
        public static int GetStorey(HospitalPlotDefinition value) =>
            StoreyRuntime.GetStorey(value);
        public static int GetActiveStorey(Level level) => StoreyRuntime.GetActiveStorey(level);
        public static bool HasUpperStorey(Level level) => StoreyRuntime.HasUpperStorey(level);
        public static void SetActiveStorey(Level level, int storey) =>
            StoreyRuntime.SetActiveStorey(level, storey);
        public static WorldState GetWorldState(Level level, int storey) =>
            StoreyRuntime.GetWorldState(level, storey);
        public static WorldState.Config GetWorldConfig(Level level, int storey) =>
            StoreyRuntime.GetWorldConfig(level, storey);
        public static IList<HospitalPlot> GetPlots(Level level) =>
            StoreyRuntime.GetPlots(level);
        public static IList<HospitalPlot> GetPlots(Level level, int storey) =>
            StoreyRuntime.GetPlots(level, storey);
        public static void PrepareUpperWorld(Level level, WorldState.Config config) =>
            StoreyRuntime.PrepareUpperWorld(level, config);
        public static void ReplaceUpperWorld(Level level, WorldState.Config config) =>
            StoreyRuntime.ReplaceUpperWorld(level, config);
        public static HospitalPlot SetPlotStorey(Level level, HospitalPlot plot, int storey,
            bool buildImmediately = false) =>
            StoreyRuntime.SetPlotStorey(level, plot, storey, buildImmediately);
        public static void RemovePlot(Level level, HospitalPlot plot) =>
            StoreyRuntime.RemovePlot(level, plot);
        public static void Recalculate(Level level, int storey) =>
            StoreyRuntime.Recalculate(level, storey);
    }

    internal static class StoreyRuntime
    {
        private static readonly FieldInfo LevelWorld = AccessTools.Field(typeof(Level), "_worldState");
        private static readonly FieldInfo LevelEvents = AccessTools.Field(typeof(Level), "_buildEvents");
        private static readonly FieldInfo LevelVisuals = AccessTools.Field(typeof(Level), "_visualManager");
        private static readonly FieldInfo LevelData = AccessTools.Field(typeof(Level), "_dataViewManager");
        private static readonly FieldInfo LevelBuilding = AccessTools.Field(typeof(Level), "_buildingLogic");
        private static readonly FieldInfo WorldConfig = AccessTools.Field(typeof(WorldState), "_config");
        private static readonly FieldInfo PlotWorld = AccessTools.Field(typeof(HospitalPlot), "_worldState");
        private static readonly FieldInfo PlotLevel = AccessTools.Field(typeof(HospitalPlot), "_level");
        private static readonly FieldInfo VisualContainer =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_container");
        private static readonly FieldInfo VisualFloorPlan =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_floorPlan");
        private static readonly FieldInfo VisualFloors =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_floorsContainer");
        private static readonly FieldInfo MapDemolishRoot =
            AccessTools.Field(typeof(HospitalMap), "_demolishRoot");
        private static readonly FieldInfo MapPerimeter =
            AccessTools.Field(typeof(HospitalMap), "_footprintPerimeter");
        private static readonly FieldInfo PerimeterRoot =
            AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root");
        private static readonly FieldInfo FootprintObject =
            AccessTools.Field(typeof(HospitalPlotFootprintVisual), "_gameObject");
        private static readonly FieldInfo CursorPlane = AccessTools.Field(typeof(CursorManager), "_planeOffset");
        private static readonly FieldInfo CursorVisualisation =
            AccessTools.Field(typeof(CursorManager), "_visualisation");
        private static readonly FieldInfo CursorSmoothed =
            AccessTools.Field(typeof(CursorManager), "_worldPositionSmoothed");
        private static readonly FieldInfo CursorMode = AccessTools.Field(typeof(CursorManager), "_mode");
        private static readonly FieldInfo CursorModes = AccessTools.Field(typeof(CursorManager), "_modeStack");
        private static readonly FieldInfo PrerequisitePlots =
            AccessTools.Field(typeof(HospitalPlotPrerequisitePlotBought), "_plot");
        private static readonly FieldInfo InteriorLight =
            AccessTools.Field(typeof(RoomLightingManager), "_interiorLight");
        private static readonly FieldInfo ExteriorLight =
            AccessTools.Field(typeof(RoomLightingManager), "_exteriorLight");
        private static readonly FieldInfo CameraLightBuffers =
            AccessTools.Field(typeof(RoomLightingManager), "_cameraLightCommandBuffers");
        private static readonly FieldInfo CameraClearBuffers =
            AccessTools.Field(typeof(RoomLightingManager), "_clearShadowMaskCommandBuffers");
        private static readonly FieldInfo LightShadowBuffers =
            AccessTools.Field(typeof(RoomLightingManager), "_shadowMaskCommandBuffers");

        private static readonly Dictionary<HospitalPlotDefinition, int> DefinitionStoreys =
            new Dictionary<HospitalPlotDefinition, int>();
        private static readonly Dictionary<HospitalPlotDefinition, long> DefinitionOrders =
            new Dictionary<HospitalPlotDefinition, long>();
        private static readonly Dictionary<Level, StoreyLevel> Levels =
            new Dictionary<Level, StoreyLevel>();
        private static readonly Dictionary<Level, WorldState.Config> PreparedUpper =
            new Dictionary<Level, WorldState.Config>();
        private static readonly Dictionary<WorldState, Plane> WorldPlanes =
            new Dictionary<WorldState, Plane>();
        private static readonly Dictionary<RoomLightingManager, Plane> LightingPlanes =
            new Dictionary<RoomLightingManager, Plane>();
        private static readonly Dictionary<HospitalPlotFootprintPerimeter, Plane> PerimeterPlanes =
            new Dictionary<HospitalPlotFootprintPerimeter, Plane>();
        private static readonly Dictionary<RoomItemVisual, FloorPlan> ItemFloors =
            new Dictionary<RoomItemVisual, FloorPlan>();
        private static readonly Dictionary<HospitalPlot, bool> PlotVisibility =
            new Dictionary<HospitalPlot, bool>();
        private static readonly Dictionary<RoomFloorPlanVisual, List<HiddenObject>> HiddenInterior =
            new Dictionary<RoomFloorPlanVisual, List<HiddenObject>>();
        private static long _nextOrder;
        private static bool _changingCursorPlane;
        private static bool _changingVisibility;
        private static bool _creatingUpperWorld;

        internal static void RegisterDefinition(HospitalPlotDefinition value, int storey)
        {
            if (value == null) return;
            ValidateStorey(storey);
            DefinitionStoreys[value] = storey;
            if (!DefinitionOrders.ContainsKey(value)) DefinitionOrders[value] = _nextOrder++;
        }

        internal static void UnregisterDefinition(HospitalPlotDefinition value)
        {
            if (value == null) return;
            DefinitionStoreys.Remove(value);
            DefinitionOrders.Remove(value);
        }

        internal static int GetStorey(HospitalPlotDefinition value) =>
            value != null && DefinitionStoreys.TryGetValue(value, out var storey)
                ? storey : StoreyApi.LowerStorey;

        internal static int GetActiveStorey(Level level) =>
            level != null && Levels.TryGetValue(level, out var state)
                ? state.ActiveStorey : StoreyApi.LowerStorey;

        internal static bool HasUpperStorey(Level level)
        {
            var upper = GetPlane(level, StoreyApi.UpperStorey, false);
            return upper?.World?.HospitalPlots != null && upper.World.HospitalPlots.Count != 0;
        }

        internal static WorldState GetWorldState(Level level, int storey)
        {
            ValidateStorey(storey);
            return GetPlane(level, storey, true)?.World;
        }

        internal static WorldState.Config GetWorldConfig(Level level, int storey)
        {
            ValidateStorey(storey);
            return GetPlane(level, storey, true)?.Config;
        }

        internal static IList<HospitalPlot> GetPlots(Level level, int storey)
        {
            ValidateStorey(storey);
            var plots = GetPlane(level, storey, true)?.World?.HospitalPlots;
            return plots == null ? (IList<HospitalPlot>)new List<HospitalPlot>() :
                new List<HospitalPlot>(plots);
        }

        internal static IList<HospitalPlot> GetPlots(Level level)
        {
            var state = GetLevel(level, true);
            var result = new List<HospitalPlot>();
            if (state?.Lower?.World?.HospitalPlots != null)
                result.AddRange(state.Lower.World.HospitalPlots);
            if (state?.Upper?.World?.HospitalPlots != null)
                result.AddRange(state.Upper.World.HospitalPlots);
            result.Sort((a, b) => PlotOrder(a).CompareTo(PlotOrder(b)));
            return result;
        }

        private static long PlotOrder(HospitalPlot plot) => plot?.Definition != null &&
            DefinitionOrders.TryGetValue(plot.Definition, out var order) ? order : long.MaxValue;

        internal static void PrepareUpperWorld(Level level, WorldState.Config config)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            PreparedUpper[level] = config;
        }

        internal static void AttachLevel(Level level)
        {
            if (level == null) return;
            var state = GetLevel(level, true);
            if (!PreparedUpper.TryGetValue(level, out var config)) return;
            PreparedUpper.Remove(level);
            state.ReplaceUpper(config);
        }

        internal static void ReplaceUpperWorld(Level level, WorldState.Config config)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            GetLevel(level, true).ReplaceUpper(config);
        }

        internal static void SetActiveStorey(Level level, int storey)
        {
            ValidateStorey(storey);
            var state = GetLevel(level, true);
            if (state == null) return;
            if (storey == StoreyApi.UpperStorey && state.Upper == null)
                storey = StoreyApi.LowerStorey;
            state.Activate(storey);
        }

        internal static HospitalPlot SetPlotStorey(Level level, HospitalPlot plot, int storey,
            bool buildImmediately)
        {
            ValidateStorey(storey);
            if (level == null || plot?.Definition == null) return plot;
            var state = GetLevel(level, true);
            var source = FindPlane(plot);
            if (source == null) throw new InvalidOperationException(
                "La parcela no pertenece a ninguno de los mapas.");
            if (source.Storey == storey)
            {
                RegisterDefinition(plot.Definition, storey);
                state.Activate(storey);
                return plot;
            }
            if (source.Storey == StoreyApi.LowerStorey &&
                storey == StoreyApi.UpperStorey && source.World.HospitalPlots.Count <= 1)
                throw new InvalidOperationException(
                    "El segundo mapa necesita conservar al menos una parcela en el mapa inferior.");
            var definition = plot.Definition;
            var visible = !PlotVisibility.TryGetValue(plot, out var remembered) || remembered;
            var target = state.GetPlane(storey);
            if (target == null)
            {
                var config = EmptyUpperConfig(state.Lower.Config);
                config.HospitalPlots.Add(
                    SharedInstanceUtils.GetSharedInstance<HospitalPlotDefinition>(definition));
                var built = definition.Built;
                if (buildImmediately) definition.Built = true;
                RegisterDefinition(definition, storey);
                HospitalPlot initialReplacement;
                try
                {
                    target = state.ReplaceUpper(config);
                    initialReplacement = target?.World?.HospitalPlots?.FirstOrDefault(candidate =>
                        ReferenceEquals(candidate.Definition, definition));
                    if (initialReplacement == null)
                        throw new InvalidOperationException(
                            "El segundo mapa no ha creado la parcela trasladada.");
                    if (buildImmediately)
                    {
                        using (target.Enter())
                        using (target.BeginLogical())
                        {
                            initialReplacement.BuyAndBuildImmediately();
                            initialReplacement.Close();
                        }
                    }
                }
                catch
                {
                    if (target != null) state.DestroyUpper();
                    RegisterDefinition(definition, source.Storey);
                    throw;
                }
                finally { definition.Built = built; }
                using (source.Enter()) DestroyPlot(source, plot);
                PlotVisibility[initialReplacement] = visible;
                initialReplacement.SetVisible(visible);
                state.Activate(storey);
                return initialReplacement;
            }
            RegisterDefinition(definition, storey);
            HospitalPlot replacement = null;
            try
            {
                using (target.Enter())
                using (target.BeginLogical())
                {
                    var built = definition.Built;
                    if (buildImmediately) definition.Built = true;
                    try
                    {
                        replacement = new HospitalPlot(definition, target.World, level);
                        target.World.HospitalPlots.Add(replacement);
                        if (buildImmediately)
                        {
                            replacement.BuyAndBuildImmediately();
                            replacement.Close();
                        }
                    }
                    finally { definition.Built = built; }
                    target.AttachPlot(replacement);
                    target.World.CalculateLighting();
                    target.World.UpdateNavigation();
                }
            }
            catch
            {
                if (replacement != null)
                    using (target.Enter()) DestroyPlot(target, replacement);
                RegisterDefinition(definition, source.Storey);
                throw;
            }
            AddDefinitionToConfig(target, definition);
            using (source.Enter()) DestroyPlot(source, plot);
            PlotVisibility.Remove(plot);
            PlotVisibility[replacement] = visible;
            replacement.SetVisible(visible);
            if (source.Storey == StoreyApi.UpperStorey &&
                source.World.HospitalPlots.Count == 0)
                state.DestroyUpper();
            state.Activate(storey);
            return replacement;
        }

        internal static void RemovePlot(Level level, HospitalPlot plot)
        {
            var plane = FindPlane(plot);
            if (level == null || plane == null) return;
            var definition = plot.Definition;
            using (plane.Enter()) DestroyPlot(plane, plot);
            UnregisterDefinition(definition);
            if (plane.Storey == StoreyApi.UpperStorey && plane.World.HospitalPlots.Count == 0)
            {
                GetLevel(level, true).DestroyUpper();
                RefreshInterior(level);
                return;
            }
            Recalculate(level, plane.Storey);
            RefreshInterior(level);
        }

        private static void DestroyPlot(Plane plane, HospitalPlot plot)
        {
            var definition = plot.Definition;
            RestorePlotInterior(plot);
            if (plot.HospitalMap != null) plane.World.DestroyHospitalMap(plot.HospitalMap);
            plot.Destroy();
            plane.World.HospitalPlots.Remove(plot);
            plane.Config?.HospitalPlots?.RemoveAll(wrapper =>
                ReferenceEquals(wrapper?.Instance, definition));
            PlotVisibility.Remove(plot);
        }

        private static void AddDefinitionToConfig(Plane plane, HospitalPlotDefinition definition)
        {
            if (plane?.Config?.HospitalPlots == null || definition == null) return;
            if (plane.Config.HospitalPlots.Any(wrapper =>
                    ReferenceEquals(wrapper?.Instance, definition))) return;
            plane.Config.HospitalPlots.Add(
                SharedInstanceUtils.GetSharedInstance<HospitalPlotDefinition>(definition));
        }

        internal static void Recalculate(Level level, int storey)
        {
            var plane = GetPlane(level, storey, false);
            if (plane?.World == null) return;
            using (plane.Enter())
            using (plane.BeginLogical())
            {
                plane.World.CalculateLighting();
                plane.World.UpdateNavigation();
            }
        }

        internal static void BeforeLevelDestroy(Level level)
        {
            if (level != null && Levels.TryGetValue(level, out var state))
                state.Activate(StoreyApi.LowerStorey);
        }

        internal static void AfterLevelDestroy(Level level)
        {
            if (level == null || !Levels.TryGetValue(level, out var state)) return;
            state.DestroyUpper();
            Levels.Remove(level);
            PreparedUpper.Remove(level);
        }

        internal static void UpdateInactive(Level level, float deltaTime)
        {
            if (level == null || !Levels.TryGetValue(level, out var state) || state.Upper == null)
                return;
            if (level.MetagameMap != null && level.MetagameMap.IsVisible) return;
            var inactive = state.ActiveStorey == StoreyApi.UpperStorey ? state.Lower : state.Upper;
            using (inactive.Enter())
            using (inactive.BeginLogical())
            {
                inactive.World.Update(deltaTime);
                // VisualManager.Update usa un reloj estatico global. Invocarlo una
                // segunda vez duplicaria su velocidad; solo la iluminacion pertenece
                // realmente al mapa inactivo y necesita mantenerse actualizada.
                inactive.Visuals.RoomLightingManager.Update();
            }
        }

        internal static void SelectPlot(HospitalPlot plot)
        {
            var level = PlotLevel?.GetValue(plot) as Level;
            var plane = FindPlane(plot);
            if (level != null && plane != null) SetActiveStorey(level, plane.Storey);
        }

        internal static void RecordPlotVisibility(HospitalPlot plot, bool value)
        {
            if (plot != null && !_changingVisibility) PlotVisibility[plot] = value;
        }

        internal static void VisibilityChanged(HospitalPlot plot)
        {
            if (!_changingVisibility)
                RefreshInterior(PlotLevel?.GetValue(plot) as Level);
        }

        internal static void VisibilityChanged(RoomFloorPlanVisual visual)
        {
            if (_changingVisibility) return;
            var floor = VisualFloorPlan?.GetValue(visual) as FloorPlan;
            RefreshInterior(PlotLevel?.GetValue(floor?.HospitalMap?.Plot) as Level);
        }

        private static void RefreshInterior(Level level)
        {
            if (level == null || !Levels.TryGetValue(level, out var state)) return;
            var suppress = state.ActiveStorey == StoreyApi.UpperStorey && state.Upper != null;
            try
            {
                _changingVisibility = true;
                foreach (var plot in state.Lower.World.HospitalPlots)
                    ApplyInterior(state.Lower.World, plot,
                        suppress && SupportsUpper(state, plot.Definition));
            }
            finally { _changingVisibility = false; }
        }

        private static bool SupportsUpper(StoreyLevel state, HospitalPlotDefinition lower)
        {
            if (state.Upper?.World?.HospitalPlots == null || lower == null) return false;
            foreach (var plot in state.Upper.World.HospitalPlots)
                foreach (var requirement in plot.Definition.Prerequisites ??
                         new List<HospitalPlotPrerequisite>())
                    if (requirement is HospitalPlotPrerequisitePlotBought bought &&
                        (PrerequisitePlots?.GetValue(bought) as
                            SharedInstance<HospitalPlotDefinition>[])?.Any(value =>
                            ReferenceEquals(value?.Instance, lower)) == true) return true;
            return false;
        }

        private static void ApplyInterior(WorldState world, HospitalPlot plot, bool suppress)
        {
            if (plot?.HospitalMap == null) return;
            if (!plot.IsVisible()) suppress = false;
            foreach (var room in world.AllRooms)
            {
                if (room?.FloorPlanVisual == null ||
                    !ReferenceEquals(room.FloorPlan?.HospitalMap, plot.HospitalMap)) continue;
                var visual = room.FloorPlanVisual;
                if (!suppress) { RestoreInterior(visual); continue; }
                Hide(visual, (VisualFloors?.GetValue(visual) as Transform)?.gameObject);
                foreach (var item in room.FloorPlan.Items ?? new List<RoomItem>())
                    if (item?.Visual?.GameObject != null && !IsStructural(item))
                        Hide(visual, item.Visual.GameObject);
                if (!ReferenceEquals(visual, plot.HospitalMap.RoomVisual)) continue;
                foreach (var item in room.FloorPlan.LandscapeItems ?? new List<LandscapeRoomItem>())
                    if (item?.Visual?.GameObject != null && IsInside(item, plot.HospitalMap))
                        Hide(visual, item.Visual.GameObject);
            }
        }

        private static bool IsInside(RoomItem item, HospitalMap map)
        {
            var local = GridCoordUtils.ToGridCoord(item.WorldPosition) - map.Anchor;
            return map.FloorPlan.ValidCoord(local) && map.FloorPlan[local];
        }

        private static bool IsStructural(RoomItem item)
        {
            var type = item?.Definition?.ItemType ?? RoomItemDefinition.Type.Default;
            return type == RoomItemDefinition.Type.Door || type == RoomItemDefinition.Type.Window ||
                   type == RoomItemDefinition.Type.DEPRECATED_WholeWallDoor ||
                   type == RoomItemDefinition.Type.ServingHatch ||
                   type == RoomItemDefinition.Type.SideDoor;
        }

        private static void Hide(RoomFloorPlanVisual owner, GameObject value)
        {
            if (owner == null || value == null) return;
            if (!HiddenInterior.TryGetValue(owner, out var hidden))
                HiddenInterior[owner] = hidden = new List<HiddenObject>();
            if (hidden.Any(entry => ReferenceEquals(entry.Value, value))) return;
            hidden.Add(new HiddenObject(value, value.activeSelf));
            value.SetActive(false);
        }

        private static void RestoreInterior(RoomFloorPlanVisual owner)
        {
            if (owner == null || !HiddenInterior.TryGetValue(owner, out var hidden)) return;
            foreach (var entry in hidden)
                if (entry.Value != null) entry.Value.SetActive(entry.WasActive);
            HiddenInterior.Remove(owner);
        }

        private static void RestorePlotInterior(HospitalPlot plot)
        {
            foreach (var visual in HiddenInterior.Keys.Where(value =>
                     ReferenceEquals((VisualFloorPlan?.GetValue(value) as FloorPlan)
                         ?.HospitalMap?.Plot, plot)).ToArray()) RestoreInterior(visual);
        }

        internal static LogicalScope BeginVisual(RoomFloorPlanVisual visual, FloorPlan floor = null)
        {
            floor = floor ?? VisualFloorPlan?.GetValue(visual) as FloorPlan;
            return FindPlane(floor?.WorldState)?.BeginLogical();
        }

        internal static void EndVisual(RoomFloorPlanVisual visual, FloorPlan floor,
            LogicalScope scope)
        {
            try
            {
                floor = floor ?? VisualFloorPlan?.GetValue(visual) as FloorPlan;
                FindPlane(floor?.WorldState)?.AttachRoot(VisualContainer?.GetValue(visual) as Transform);
            }
            finally { scope?.Dispose(); }
        }

        internal static LogicalScope BeginItem(RoomItemVisual visual, RoomItem item)
        {
            if (visual != null && item?.FloorPlan != null) ItemFloors[visual] = item.FloorPlan;
            return FindPlane(item?.FloorPlan?.WorldState)?.BeginLogical();
        }

        internal static LogicalScope BeginItem(RoomItemVisual visual) =>
            visual != null && ItemFloors.TryGetValue(visual, out var floor)
                ? FindPlane(floor?.WorldState)?.BeginLogical() : null;

        internal static LogicalScope BeginPerimeter(HospitalPlotFootprintPerimeter value) =>
            value != null && PerimeterPlanes.TryGetValue(value, out var plane)
                ? plane.BeginLogical() : null;

        internal static void RegisterRoomVisual(RoomFloorPlanVisual visual, WorldState world) =>
            FindPlane(world)?.AttachRoot(VisualContainer?.GetValue(visual) as Transform);

        internal static void RegisterFootprint(HospitalPlotFootprintVisual visual,
            HospitalPlot plot) => FindPlane(plot)?.AttachRoot(
                (FootprintObject?.GetValue(visual) as GameObject)?.transform);

        internal static void RegisterPreview(CursorEditHospital cursor, HospitalPlot plot)
        {
            var field = AccessTools.Field(typeof(CursorEditHospital), "_tileMapPreview");
            var previews = field?.GetValue(cursor) as Dictionary<HospitalPlot, GameObject>;
            if (previews != null && previews.TryGetValue(plot, out var preview))
                FindPlane(plot)?.AttachRoot(preview?.transform);
        }

        internal static void OffsetLighting(RoomLightingManager manager, ref Vector3 origin)
        {
            if (manager != null && LightingPlanes.TryGetValue(manager, out var plane) &&
                plane.Storey == StoreyApi.UpperStorey)
                origin += Vector3.up * StoreyApi.UpperVisualHeight;
        }

        internal static bool RenderLighting(RoomLightingManager manager) =>
            manager == null || !LightingPlanes.TryGetValue(manager, out var plane) ||
            plane.Storey == StoreyApi.LowerStorey ||
            plane.Owner.ActiveStorey == StoreyApi.UpperStorey;

        private static void ClearLightingCommands(RoomLightingManager manager)
        {
            if (manager == null) return;
            foreach (var field in new[] { CameraLightBuffers, CameraClearBuffers, LightShadowBuffers })
            {
                if (!(field?.GetValue(manager) is IDictionary buffers)) continue;
                foreach (DictionaryEntry entry in buffers)
                    if (entry.Value is UnityEngine.Rendering.CommandBuffer command)
                        command.Clear();
            }
        }

        internal static void AdjustCursorPlane(CursorManager cursor, ref float value)
        {
            if (_changingCursorPlane || cursor == null) return;
            foreach (var state in Levels.Values)
            {
                if (!ReferenceEquals(state.Level.CursorManager, cursor)) continue;
                state.NativeCursorPlane = value;
                if (state.ActiveStorey == StoreyApi.UpperStorey)
                    value += StoreyApi.UpperVisualHeight;
                return;
            }
        }

        internal static void PresentCursor(CursorManager cursor)
        {
            if (cursor == null) return;
            foreach (var state in Levels.Values)
            {
                if (!ReferenceEquals(state.Level.CursorManager, cursor) ||
                    state.ActiveStorey != StoreyApi.UpperStorey) continue;
                var visual = CursorVisualisation?.GetValue(cursor) as GameObject;
                if (visual != null && CursorSmoothed?.GetValue(cursor) is Vector3 logical)
                    visual.transform.position = logical +
                        Vector3.up * StoreyApi.UpperVisualHeight;
                return;
            }
        }

        internal static bool AllowTerrainCache() => !_creatingUpperWorld;

        internal static bool EvaluatePlotPrerequisite(
            HospitalPlotPrerequisitePlotBought prerequisite, Level level, ref bool result)
        {
            if (prerequisite == null || level == null) return true;
            var required = PrerequisitePlots?.GetValue(prerequisite) as
                SharedInstance<HospitalPlotDefinition>[];
            if (required == null) return true;
            var plots = GetPlots(level);
            result = required.Any(candidate => candidate?.Instance != null &&
                plots.Any(plot => ReferenceEquals(plot?.Definition, candidate.Instance) &&
                                  plot.Bought));
            return false;
        }

        internal static void Clear()
        {
            foreach (var state in Levels.Values.ToArray())
                try { state.DestroyUpper(); } catch { }
            foreach (var visual in HiddenInterior.Keys.ToArray()) RestoreInterior(visual);
            Levels.Clear(); PreparedUpper.Clear(); WorldPlanes.Clear(); LightingPlanes.Clear();
            PerimeterPlanes.Clear(); ItemFloors.Clear(); PlotVisibility.Clear();
            DefinitionStoreys.Clear(); DefinitionOrders.Clear(); _nextOrder = 0;
        }

        private static StoreyLevel GetLevel(Level level, bool create)
        {
            if (level == null) return null;
            if (Levels.TryGetValue(level, out var state)) return state;
            var world = LevelWorld?.GetValue(level) as WorldState;
            if (!create || world == null) return null;
            state = new StoreyLevel(level, world,
                LevelEvents?.GetValue(level) as BuildEvents,
                LevelVisuals?.GetValue(level) as VisualManager,
                LevelData?.GetValue(level) as DataViewManager,
                LevelBuilding?.GetValue(level) as BuildingLogic);
            Levels[level] = state;
            RegisterPlane(state.Lower);
            foreach (var plot in state.Lower.World.HospitalPlots)
                RegisterDefinition(plot?.Definition, StoreyApi.LowerStorey);
            return state;
        }

        private static Plane GetPlane(Level level, int storey, bool create) =>
            GetLevel(level, create)?.GetPlane(storey);
        private static Plane FindPlane(WorldState world) =>
            world != null && WorldPlanes.TryGetValue(world, out var plane) ? plane : null;
        private static Plane FindPlane(HospitalPlot plot) =>
            FindPlane(PlotWorld?.GetValue(plot) as WorldState);

        private static void RegisterPlane(Plane plane)
        {
            if (plane?.World != null) WorldPlanes[plane.World] = plane;
            var lighting = plane?.Visuals?.RoomLightingManager;
            if (lighting != null) LightingPlanes[lighting] = plane;
        }

        private static void UnregisterPlane(Plane plane)
        {
            if (plane?.World != null) WorldPlanes.Remove(plane.World);
            var lighting = plane?.Visuals?.RoomLightingManager;
            if (lighting != null) LightingPlanes.Remove(lighting);
            foreach (var value in PerimeterPlanes.Where(entry => ReferenceEquals(entry.Value, plane))
                         .Select(entry => entry.Key).ToArray()) PerimeterPlanes.Remove(value);
        }

        private static WorldState.Config EmptyUpperConfig(WorldState.Config source)
        {
            var clone = (WorldState.Config)typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);
            clone.HospitalPlots = new List<SharedInstance<HospitalPlotDefinition>>();
            clone.createBaseLandscapeItems = false;
            clone.FogOfWarDefinition = null;
            return clone;
        }

        private static void ValidateStorey(int storey)
        {
            if (storey != StoreyApi.LowerStorey && storey != StoreyApi.UpperStorey)
                throw new ArgumentOutOfRangeException(nameof(storey));
        }

        internal sealed class StoreyLevel
        {
            internal readonly Level Level;
            internal readonly Plane Lower;
            internal Plane Upper;
            internal int ActiveStorey;
            internal float NativeCursorPlane;

            internal StoreyLevel(Level level, WorldState world, BuildEvents events,
                VisualManager visuals, DataViewManager data, BuildingLogic building)
            {
                Level = level;
                Lower = new Plane(this, StoreyApi.LowerStorey,
                    WorldConfig?.GetValue(world) as WorldState.Config, world, events, visuals,
                    data, building, null);
                NativeCursorPlane = level.CursorManager != null && CursorPlane != null
                    ? (float)CursorPlane.GetValue(level.CursorManager) : 0f;
            }

            internal Plane GetPlane(int storey) => storey == StoreyApi.UpperStorey ? Upper : Lower;

            internal Plane ReplaceUpper(WorldState.Config config)
            {
                var reactivate = ActiveStorey == StoreyApi.UpperStorey;
                if (reactivate) Activate(StoreyApi.LowerStorey);
                DestroyUpper();
                if (config?.HospitalPlots == null || config.HospitalPlots.Count == 0) return null;
                config.createBaseLandscapeItems = false;
                config.FogOfWarDefinition = null;
                var root = new GameObject("Under Pressure - Upper Map - " + Level.UniqueID);
                root.transform.position = Vector3.zero;
                var events = new BuildEvents();
                var visuals = new VisualManager(Level.Config.GetVisualManagerConfig(), events, Level);
                var plane = new Plane(this, StoreyApi.UpperStorey, config, null, events,
                    visuals, null, null, root);
                Upper = plane;
                RegisterPlane(plane);
                try
                {
                    using (new ConstructionBindingScope(Level, plane))
                    using (plane.BeginLogical())
                    {
                        var dataConfig = Level.Config.GetDataViewManagerConfig();
                        var buildConfig = Level.Config.GetBuildingLogicConfig();
                        var renderer = PostProcessingRendererProxy.Instance;
                        var previousPostProcessing = renderer?.PostProcessRendererData;
                        var previousFog = previousPostProcessing?.FogOfWarDefinition;
                        try
                        {
                            _creatingUpperWorld = true;
                            plane.World = new WorldState(config, Level, Level.Metagame, visuals,
                                dataConfig.ValueMaterial, buildConfig.RoomItemEditConfig);
                        }
                        finally
                        {
                            _creatingUpperWorld = false;
                            if (renderer != null)
                                renderer.PostProcessRendererData = previousPostProcessing;
                            if (previousPostProcessing != null)
                                previousPostProcessing.FogOfWarDefinition = previousFog;
                        }
                        WorldPlanes[plane.World] = plane;
                        plane.Data = new DataViewManager(dataConfig, Level, plane.World, visuals);
                        plane.Building = new BuildingLogic(buildConfig, Level, plane.World,
                            visuals, plane.Data, events);
                    }
                    using (new BindingScope(Level, plane, true)) events.Initialise(Level);
                    plane.AttachAll();
                    plane.Present(reactivate);
                    Activate(reactivate ? StoreyApi.UpperStorey : StoreyApi.LowerStorey);
                    StoreyPlugin.Log?.LogInfo("Segundo mapa nativo creado con " +
                        plane.World.HospitalPlots.Count + " parcelas, sin terreno y a +2,00 m.");
                    return plane;
                }
                catch
                {
                    try { plane.Destroy(); } catch { }
                    UnregisterPlane(plane); Upper = null; Activate(StoreyApi.LowerStorey); throw;
                }
            }

            internal void Activate(int storey)
            {
                var plane = GetPlane(storey) ?? Lower;
                ActiveStorey = plane.Storey;
                ApplyBindings(Level, plane, true);
                RetargetCursor(Level.CursorManager, Lower, Upper, plane);
                if (Upper != null)
                {
                    Upper.Present(ActiveStorey == StoreyApi.UpperStorey);
                    Upper.LightingActive(ActiveStorey == StoreyApi.UpperStorey);
                }
                try
                {
                    _changingCursorPlane = true;
                    CursorPlane?.SetValue(Level.CursorManager, NativeCursorPlane +
                        (ActiveStorey == StoreyApi.UpperStorey ? StoreyApi.UpperVisualHeight : 0f));
                }
                finally { _changingCursorPlane = false; }
                RefreshInterior(Level);
            }

            internal void DestroyUpper()
            {
                if (Upper == null) return;
                var plane = Upper; Upper = null;
                using (new BindingScope(Level, plane, true)) plane.Destroy();
                UnregisterPlane(plane);
                ActiveStorey = StoreyApi.LowerStorey;
                ApplyBindings(Level, Lower, true);
            }
        }

        internal sealed class Plane
        {
            internal readonly StoreyLevel Owner;
            internal readonly int Storey;
            internal readonly WorldState.Config Config;
            internal WorldState World;
            internal readonly BuildEvents Events;
            internal readonly VisualManager Visuals;
            internal DataViewManager Data;
            internal BuildingLogic Building;
            internal readonly GameObject RootObject;
            internal Transform Root => RootObject == null ? null : RootObject.transform;
            private int _logicalDepth;

            internal Plane(StoreyLevel owner, int storey, WorldState.Config config,
                WorldState world, BuildEvents events, VisualManager visuals,
                DataViewManager data, BuildingLogic building, GameObject root)
            {
                Owner = owner; Storey = storey; Config = config; World = world; Events = events;
                Visuals = visuals; Data = data; Building = building; RootObject = root;
            }

            internal IDisposable Enter() => new BindingScope(Owner.Level, this, true);
            internal LogicalScope BeginLogical()
            {
                if (Storey != StoreyApi.UpperStorey || Root == null) return null;
                if (_logicalDepth++ == 0) Root.position = Vector3.zero;
                return new LogicalScope(this);
            }
            internal void EndLogical()
            {
                if (Root == null || _logicalDepth <= 0) return;
                if (--_logicalDepth == 0) Root.position = Vector3.up * StoreyApi.UpperVisualHeight;
            }
            internal void Present(bool value)
            {
                if (RootObject == null) return;
                Root.position = _logicalDepth == 0 ? Vector3.up * StoreyApi.UpperVisualHeight :
                    Vector3.zero;
                RootObject.SetActive(value);
            }
            internal void LightingActive(bool value)
            {
                var lighting = Visuals?.RoomLightingManager;
                var interior = InteriorLight?.GetValue(lighting) as Light;
                var exterior = ExteriorLight?.GetValue(lighting) as Light;
                if (interior != null) interior.gameObject.SetActive(value);
                if (exterior != null) exterior.gameObject.SetActive(value);
                if (!value) ClearLightingCommands(lighting);
            }
            internal void AttachAll()
            {
                if (World == null || Storey != StoreyApi.UpperStorey) return;
                foreach (var plot in World.HospitalPlots) AttachPlot(plot);
                foreach (var room in World.AllRooms)
                    AttachRoot(room?.FloorPlanVisual?.GameObject?.transform);
            }
            internal void AttachPlot(HospitalPlot plot)
            {
                if (plot?.HospitalMap == null || Storey != StoreyApi.UpperStorey) return;
                var map = plot.HospitalMap;
                AttachRoot(map.RoomVisual?.GameObject?.transform);
                AttachRoot(MapDemolishRoot?.GetValue(map) as Transform);
                var perimeter = MapPerimeter?.GetValue(map) as HospitalPlotFootprintPerimeter;
                if (perimeter == null) return;
                PerimeterPlanes[perimeter] = this;
                AttachRoot(PerimeterRoot?.GetValue(perimeter) as Transform);
            }
            internal void AttachRoot(Transform value)
            {
                if (Storey != StoreyApi.UpperStorey || value == null || Root == null ||
                    ReferenceEquals(value, Root) || value.IsChildOf(Root)) return;
                var position = value.position; var rotation = value.rotation; var scale = value.localScale;
                value.SetParent(Root, false); value.localPosition = position;
                value.localRotation = rotation; value.localScale = scale;
            }
            internal void Destroy()
            {
                Building?.Destroy(); Data?.Destroy(); World?.Destroy(); Visuals?.Destroy();
                Events?.Destroy();
                if (RootObject != null) UnityEngine.Object.Destroy(RootObject);
                World = null; Data = null; Building = null;
            }
        }

        internal sealed class LogicalScope : IDisposable
        {
            private Plane _plane;
            internal LogicalScope(Plane plane) { _plane = plane; }
            public void Dispose() { var value = _plane; _plane = null; value?.EndLogical(); }
        }

        private sealed class BindingScope : IDisposable
        {
            private readonly Level _level;
            private readonly Bindings _before;
            internal BindingScope(Level level, Plane plane, bool all)
            { _level = level; _before = Bindings.Capture(level); ApplyBindings(level, plane, all); }
            public void Dispose() => _before.Apply(_level);
        }

        private sealed class ConstructionBindingScope : IDisposable
        {
            private readonly Level _level;
            private readonly Bindings _before;

            internal ConstructionBindingScope(Level level, Plane plane)
            {
                _level = level;
                _before = Bindings.Capture(level);
                LevelWorld?.SetValue(level, null);
                LevelEvents?.SetValue(level, plane.Events);
                LevelVisuals?.SetValue(level, plane.Visuals);
                LevelData?.SetValue(level, null);
                LevelBuilding?.SetValue(level, null);
            }

            public void Dispose() => _before.Apply(_level);
        }

        private sealed class Bindings
        {
            internal object World, Events, Visuals, Data, Building;
            internal static Bindings Capture(Level level) => new Bindings
            {
                World = LevelWorld?.GetValue(level), Events = LevelEvents?.GetValue(level),
                Visuals = LevelVisuals?.GetValue(level), Data = LevelData?.GetValue(level),
                Building = LevelBuilding?.GetValue(level)
            };
            internal void Apply(Level level)
            {
                LevelWorld?.SetValue(level, World); LevelEvents?.SetValue(level, Events);
                LevelVisuals?.SetValue(level, Visuals); LevelData?.SetValue(level, Data);
                LevelBuilding?.SetValue(level, Building);
            }
        }

        private static void ApplyBindings(Level level, Plane plane, bool all)
        {
            if (all) LevelWorld?.SetValue(level, plane.World);
            LevelEvents?.SetValue(level, plane.Events); LevelVisuals?.SetValue(level, plane.Visuals);
            if (!all) return;
            LevelData?.SetValue(level, plane.Data); LevelBuilding?.SetValue(level, plane.Building);
        }

        private static void RetargetCursor(CursorManager cursor, Plane lower, Plane upper, Plane active)
        {
            if (cursor == null) return;
            var modes = new HashSet<object>();
            var mode = CursorMode?.GetValue(cursor); if (mode != null) modes.Add(mode);
            if (CursorModes?.GetValue(cursor) is IEnumerable stack)
                foreach (var value in stack) if (value != null) modes.Add(value);
            foreach (var value in modes)
                for (var type = value.GetType(); type != null; type = type.BaseType)
                    foreach (var field in type.GetFields(BindingFlags.Instance |
                                 BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.DeclaredOnly))
                    {
                        object replacement;
                        if (field.FieldType == typeof(WorldState)) replacement = active.World;
                        else if (field.FieldType == typeof(BuildEvents)) replacement = active.Events;
                        else if (field.FieldType == typeof(VisualManager)) replacement = active.Visuals;
                        else if (field.FieldType == typeof(DataViewManager)) replacement = active.Data;
                        else if (field.FieldType == typeof(BuildingLogic)) replacement = active.Building;
                        else continue;
                        var current = field.GetValue(value);
                        if (current == null || Belongs(current, lower) || Belongs(current, upper))
                            field.SetValue(value, replacement);
                    }
        }

        private static bool Belongs(object value, Plane plane) => plane != null &&
            (ReferenceEquals(value, plane.World) || ReferenceEquals(value, plane.Events) ||
             ReferenceEquals(value, plane.Visuals) || ReferenceEquals(value, plane.Data) ||
             ReferenceEquals(value, plane.Building));

        private sealed class HiddenObject
        {
            internal readonly GameObject Value; internal readonly bool WasActive;
            internal HiddenObject(GameObject value, bool active) { Value = value; WasActive = active; }
        }
    }

    [HarmonyPatch(typeof(Level), MethodType.Constructor)]
    internal static class StoreyLevelConstructorPatch
    { private static void Postfix(Level __instance) => StoreyRuntime.AttachLevel(__instance); }

    [HarmonyPatch(typeof(Level), nameof(Level.Update))]
    internal static class StoreyLevelUpdatePatch
    { private static void Postfix(Level __instance) =>
        StoreyRuntime.UpdateInactive(__instance, GameTime.deltaTime); }

    [HarmonyPatch(typeof(Level), nameof(Level.Destroy))]
    internal static class StoreyLevelDestroyPatch
    {
        private static void Prefix(Level __instance) => StoreyRuntime.BeforeLevelDestroy(__instance);
        private static void Postfix(Level __instance) => StoreyRuntime.AfterLevelDestroy(__instance);
    }

    [HarmonyPatch(typeof(CursorEditHospital), "OnSelectHospitalPlot")]
    internal static class StoreyPlotSelectionPatch
    { private static void Prefix(HospitalPlot __0) => StoreyRuntime.SelectPlot(__0); }

    [HarmonyPatch(typeof(HospitalPlot), nameof(HospitalPlot.SetVisible))]
    internal static class StoreyPlotVisibilityPatch
    {
        private static void Prefix(HospitalPlot __instance, bool __0) =>
            StoreyRuntime.RecordPlotVisibility(__instance, __0);
        private static void Postfix(HospitalPlot __instance) =>
            StoreyRuntime.VisibilityChanged(__instance);
    }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), nameof(RoomFloorPlanVisual.SetVisible))]
    internal static class StoreyRoomVisibilityPatch
    { private static void Postfix(RoomFloorPlanVisual __instance) =>
        StoreyRuntime.VisibilityChanged(__instance); }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), MethodType.Constructor)]
    internal static class StoreyRoomVisualConstructorPatch
    { private static void Postfix(RoomFloorPlanVisual __instance, WorldState __0) =>
        StoreyRuntime.RegisterRoomVisual(__instance, __0); }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), "UpdateFromRoom")]
    internal static class StoreyRoomVisualUpdatePatch
    {
        private static void Prefix(RoomFloorPlanVisual __instance, FloorPlan __0,
            ref StoreyRuntime.LogicalScope __state) =>
            __state = StoreyRuntime.BeginVisual(__instance, __0);
        private static void Postfix(RoomFloorPlanVisual __instance, FloorPlan __0,
            StoreyRuntime.LogicalScope __state) => StoreyRuntime.EndVisual(__instance, __0, __state);
        private static Exception Finalizer(RoomFloorPlanVisual __instance, FloorPlan __0,
            StoreyRuntime.LogicalScope __state, Exception __exception)
        { StoreyRuntime.EndVisual(__instance, __0, __state); return __exception; }
    }

    [HarmonyPatch]
    internal static class StoreyRoomVisualMutationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RoomFloorPlanVisual), "CreateWallObjects");
            yield return AccessTools.Method(typeof(RoomFloorPlanVisual), "CreateFloorTileObjects");
        }
        private static void Prefix(RoomFloorPlanVisual __instance,
            ref StoreyRuntime.LogicalScope __state) =>
            __state = StoreyRuntime.BeginVisual(__instance);
        private static void Postfix(StoreyRuntime.LogicalScope __state) => __state?.Dispose();
        private static Exception Finalizer(StoreyRuntime.LogicalScope __state, Exception value)
        { __state?.Dispose(); return value; }
    }

    [HarmonyPatch(typeof(RoomItemVisual), "UpdateFrom")]
    internal static class StoreyRoomItemUpdatePatch
    {
        private static void Prefix(RoomItemVisual __instance, RoomItem __0,
            ref StoreyRuntime.LogicalScope __state) =>
            __state = StoreyRuntime.BeginItem(__instance, __0);
        private static void Postfix(StoreyRuntime.LogicalScope __state) => __state?.Dispose();
        private static Exception Finalizer(StoreyRuntime.LogicalScope __state, Exception value)
        { __state?.Dispose(); return value; }
    }

    [HarmonyPatch(typeof(RoomItemVisual), "set_WorldPosition")]
    internal static class StoreyRoomItemPositionPatch
    {
        private static void Prefix(RoomItemVisual __instance,
            ref StoreyRuntime.LogicalScope __state) =>
            __state = StoreyRuntime.BeginItem(__instance);
        private static void Postfix(StoreyRuntime.LogicalScope __state) => __state?.Dispose();
        private static Exception Finalizer(StoreyRuntime.LogicalScope __state, Exception value)
        { __state?.Dispose(); return value; }
    }

    [HarmonyPatch(typeof(HospitalPlotFootprintPerimeter),
        nameof(HospitalPlotFootprintPerimeter.Refresh))]
    internal static class StoreyPerimeterPatch
    {
        private static void Prefix(HospitalPlotFootprintPerimeter __instance,
            ref StoreyRuntime.LogicalScope __state) =>
            __state = StoreyRuntime.BeginPerimeter(__instance);
        private static void Postfix(StoreyRuntime.LogicalScope __state) => __state?.Dispose();
        private static Exception Finalizer(StoreyRuntime.LogicalScope __state, Exception value)
        { __state?.Dispose(); return value; }
    }

    [HarmonyPatch(typeof(HospitalPlotFootprintVisual), MethodType.Constructor,
        typeof(HospitalPlotFootprintVisual.Config), typeof(HospitalPlot))]
    internal static class StoreyFootprintPatch
    { private static void Postfix(HospitalPlotFootprintVisual __instance, HospitalPlot __1) =>
        StoreyRuntime.RegisterFootprint(__instance, __1); }

    [HarmonyPatch(typeof(CursorEditHospital), "CreateTilemapPreview")]
    internal static class StoreyPreviewPatch
    { private static void Postfix(CursorEditHospital __instance, HospitalPlot __0) =>
        StoreyRuntime.RegisterPreview(__instance, __0); }

    [HarmonyPatch(typeof(RoomLightingManager), "AddRoomLight")]
    internal static class StoreyLightingTransformPatch
    { private static void Prefix(RoomLightingManager __instance, ref Vector3 __1) =>
        StoreyRuntime.OffsetLighting(__instance, ref __1); }

    [HarmonyPatch(typeof(RoomLightingManager), "OnPreRender")]
    internal static class StoreyLightingRenderPatch
    { private static bool Prefix(RoomLightingManager __instance) =>
        StoreyRuntime.RenderLighting(__instance); }

    [HarmonyPatch(typeof(CursorManager), nameof(CursorManager.SetPlaneOffset))]
    internal static class StoreyCursorPlanePatch
    { private static void Prefix(CursorManager __instance, ref float __0) =>
        StoreyRuntime.AdjustCursorPlane(__instance, ref __0); }

    [HarmonyPatch(typeof(CursorManager), "UpdateCursorPosition")]
    internal static class StoreyCursorVisualPatch
    { private static void Postfix(CursorManager __instance) =>
        StoreyRuntime.PresentCursor(__instance); }

    // El mapa superior no posee terreno: no debe capturar ni restaurar las
    // capas del Terrain compartido por la escena inferior.
    [HarmonyPatch(typeof(WorldState), "CacheTerrainDetailLayers")]
    internal static class StoreyUpperTerrainCachePatch
    { private static bool Prefix() => StoreyRuntime.AllowTerrainCache(); }

    [HarmonyPatch(typeof(HospitalPlotPrerequisitePlotBought),
        nameof(HospitalPlotPrerequisitePlotBought.Valid))]
    internal static class StoreyPlotPrerequisitePatch
    {
        private static bool Prefix(HospitalPlotPrerequisitePlotBought __instance,
            Level __0, ref bool __result) =>
            StoreyRuntime.EvaluatePlotPrerequisite(__instance, __0, ref __result);
    }
}
