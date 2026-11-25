using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace UnderPressure.Storey
{
    internal sealed class StoreyInstance
    {
        internal readonly Level Level;
        internal readonly BuildEvents Events;
        internal readonly GameObject Root;
        internal readonly GameObject ContentRoot;
        internal readonly GameObject GridRoot;
        internal WorldState State;

        internal StoreyInstance(Level level, BuildEvents events, int index)
        {
            Level = level;
            Events = events;
            Root = new GameObject("UnderPressure Storey " + index);
            Root.transform.position = new Vector3(0f, index * 2f, 0f);

            ContentRoot = new GameObject("World Content");
            ContentRoot.transform.SetParent(Root.transform, false);
            ContentRoot.SetActive(false);

            GridRoot = new GameObject("Vector Grid");
            GridRoot.transform.SetParent(Root.transform, false);
            GridRoot.SetActive(false);
        }
    }

    internal sealed class StoreyPlotRecord
    {
        internal int Index;
        internal readonly HospitalPlotDefinition Definition;
        internal HospitalPlot Plot;
        internal StoreyInstance Storey;

        internal StoreyPlotRecord(int index, HospitalPlot plot, StoreyInstance storey)
        {
            Index = index;
            Plot = plot;
            Definition = plot.Definition;
            Storey = storey;
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
        private static readonly FieldInfo VisualState =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_worldState");
        private static readonly FieldInfo VisualContainer =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_container");
        private static readonly FieldInfo PlotMap =
            AccessTools.Field(typeof(HospitalPlot), "_hospitalMap");
        private static readonly FieldInfo PlotBought =
            AccessTools.Field(typeof(HospitalPlot), "_bought");
        private static readonly FieldInfo PlotState =
            AccessTools.Field(typeof(HospitalPlot), "_state");
        private static readonly FieldInfo PlotTimeLeft =
            AccessTools.Field(typeof(HospitalPlot), "_timeLeftToBuild");
        private static readonly FieldInfo PlotTilesBuilt =
            AccessTools.Field(typeof(HospitalPlot), "_tilesBuilt");
        private static readonly FieldInfo PlotHiddenLayers =
            AccessTools.Field(typeof(HospitalPlot), "_hiddenLayers");
        private static readonly MethodInfo PlotRefresh =
            AccessTools.Method(typeof(HospitalPlot), "RefreshHospitalMap");
        private static readonly FieldInfo PlotSelectionPlot =
            AccessTools.Field(typeof(PlotSelectionItem), "_plot");
        private static readonly FieldInfo CursorSelectedPlot =
            AccessTools.Field(typeof(CursorEditHospital), "_hospitalPlot");
        private static readonly FieldInfo CursorPreviews =
            AccessTools.Field(typeof(CursorEditHospital), "_tileMapPreview");
        private static readonly MethodInfo CursorCreatePreview =
            AccessTools.Method(typeof(CursorEditHospital), "CreateTilemapPreview");
        private static readonly FieldInfo StateNavMesh =
            AccessTools.Field(typeof(WorldState), "_navMesh");
        private static readonly FieldInfo StateBuiltMaps =
            AccessTools.Field(typeof(WorldState), "_builtHospitalMaps");
        private static readonly FieldInfo StateAnchor =
            AccessTools.Field(typeof(WorldState), "<Anchor>k__BackingField");
        private static readonly FieldInfo NativeNavMeshData =
            AccessTools.Field(typeof(TH20.NavMesh), "_navMeshData");
        private static readonly FieldInfo NativeNavMeshInstance =
            AccessTools.Field(typeof(TH20.NavMesh), "_navMeshDataInstance");
        private static readonly MethodInfo NavMeshUpdateFromMap =
            AccessTools.Method(typeof(TH20.NavMesh), "UpdateFromHospitalMap");
        private static readonly Dictionary<Level, WorldState.Config> Configs =
            new Dictionary<Level, WorldState.Config>();
        private static readonly Dictionary<WorldState, StoreyInstance> Storeys =
            new Dictionary<WorldState, StoreyInstance>();
        private static readonly List<StoreyPlotRecord> PlotRecords =
            new List<StoreyPlotRecord>();

        private static StoreyInstance _lower;
        private static StoreyInstance _upper;
        private static StoreyInstance _constructing;
        private static GameObject _toolbar;
        private static Material _gridMaterial;
        private static EditHospitalMenu _menu;
        private static bool _refreshing;

        internal static bool Constructing => _constructing != null;

        internal static void Register()
        {
            UnderPressure.StoreyMapRegistry.RegisterRuntime(
                GetAllPlots, GetWorld, Refresh, OnPlotAdded, OnPlotRemoved);
        }

        internal static void Unregister()
        {
            UnderPressure.StoreyMapRegistry.UnregisterRuntime();
        }

        internal static void CaptureConfig(WorldState.Config config, Level level)
        {
            if (_constructing != null || config == null || level == null ||
                level.WorldState != null) return;
            Configs[level] = config;
        }

        internal static bool IsUpper(WorldState state) =>
            state != null && _upper != null && ReferenceEquals(_upper.State, state);

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
            _lower = new StoreyInstance(level, null, 0) { State = level.WorldState };
            _lower.ContentRoot.SetActive(true);
            Storeys.Add(_lower.State, _lower);
            for (var index = 0; index < level.WorldState.HospitalPlots.Count; ++index)
                PlotRecords.Add(new StoreyPlotRecord(
                    index, level.WorldState.HospitalPlots[index], _lower));
            AttachExistingContent(_lower);
            if (!TryGetBounds(_lower.State, out var fullBounds))
                return false;
            BuildVectorGrid(_lower, fullBounds);

            var isolatedEvents = new BuildEvents();
            var upper = new StoreyInstance(level, isolatedEvents, 1);
            var normalEvents = level.BuildEvents;
            var savedPostConstruct = level.PostConstruct;
            _constructing = upper;
            try
            {
                LevelBuildEvents.SetValue(level, isolatedEvents);
                var upperConfig = CloneConfig.Invoke(source, null) as WorldState.Config;
                upperConfig.HospitalPlots = new List<FullInspector.SharedInstance<HospitalPlotDefinition>>();
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
                Storeys.Add(state, upper);
                _upper = upper;
                RemoveUpperSeed();
                BuildVectorGrid(upper, fullBounds);
                foreach (var record in PlotRecords.ToArray())
                    if (UnderPressure.StoreyMapRegistry.GetStorey(record.Definition) ==
                        UnderPressure.StoreyMapRegistry.UpperStorey)
                        MovePlot(record, upper);
                StoreyPlugin.Log?.LogInfo(
                    "Segundo WorldState creado; parcelas separadas entre Y=0 e Y=2.");
                return true;
            }
            catch (Exception exception)
            {
                StoreyPlugin.Log?.LogError(
                    "No se pudo crear el segundo WorldState: " + exception);
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

        private static void RemoveUpperSeed()
        {
            if (_upper?.State == null || _upper.State.HospitalPlots.Count == 0) return;
            var seed = _upper.State.HospitalPlots[0];
            _upper.State.HospitalPlots.RemoveAt(0);
            if (seed.HospitalMap != null)
            {
                _upper.State.DestroyHospitalMap(seed.HospitalMap);
                PlotMap.SetValue(seed, null);
            }
            seed.Destroy();
            var navMesh = StateNavMesh.GetValue(_upper.State) as TH20.NavMesh;
            if (navMesh != null) navMesh.Destroy();
            StateNavMesh.SetValue(_upper.State, null);
            _upper.State.ExteriorState = default(BoolArray2D);
            _upper.ContentRoot.SetActive(false);
        }

        private static void AttachExistingContent(StoreyInstance storey)
        {
            if (storey?.State == null) return;
            foreach (var plot in storey.State.HospitalPlots)
                AttachPlotContent(plot, storey);
        }

        private static void AttachPlotContent(HospitalPlot plot, StoreyInstance storey)
        {
            var map = plot?.HospitalMap;
            if (map == null || storey == null) return;
            var visual = map.RoomVisual;
            var container = visual == null ? null : VisualContainer.GetValue(visual) as Transform;
            if (container != null && container.parent != storey.ContentRoot.transform)
                container.SetParent(storey.ContentRoot.transform, true);
            var perimeter = map.FootprintPerimeter;
            var perimeterRoot = perimeter == null ? null :
                AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root")?
                    .GetValue(perimeter) as Transform;
            if (perimeterRoot != null && perimeterRoot.parent != storey.ContentRoot.transform)
                perimeterRoot.SetParent(storey.ContentRoot.transform, true);
            var demolishRoot = AccessTools.Field(typeof(HospitalMap), "_demolishRoot")?
                .GetValue(map) as Transform;
            if (demolishRoot != null && demolishRoot.parent != storey.ContentRoot.transform)
                demolishRoot.SetParent(storey.ContentRoot.transform, true);
        }

        private static IList<HospitalPlot> GetAllPlots(Level level)
        {
            if (level == null || _lower == null || !ReferenceEquals(_lower.Level, level))
                return level?.WorldState?.HospitalPlots ?? (IList<HospitalPlot>)new HospitalPlot[0];
            return PlotRecords.OrderBy(record => record.Index)
                .Select(record => record.Plot).ToList();
        }

        private static WorldState GetWorld(HospitalPlot plot)
        {
            var record = PlotRecords.FirstOrDefault(item => ReferenceEquals(item.Plot, plot));
            return record?.Storey?.State;
        }

        private static void OnPlotAdded(Level level, HospitalPlot plot)
        {
            if (plot == null || _lower == null || !ReferenceEquals(_lower.Level, level) ||
                PlotRecords.Any(record => ReferenceEquals(record.Plot, plot))) return;
            var nextIndex = PlotRecords.Count == 0 ? 0 :
                PlotRecords.Max(record => record.Index) + 1;
            PlotRecords.Add(new StoreyPlotRecord(nextIndex, plot, _lower));
            UnderPressure.StoreyMapRegistry.SetStorey(
                plot.Definition, UnderPressure.StoreyMapRegistry.GroundStorey);
            AttachPlotContent(plot, _lower);
        }

        private static void OnPlotRemoved(Level level, HospitalPlot plot)
        {
            if (plot == null || _lower == null || !ReferenceEquals(_lower.Level, level)) return;
            var record = PlotRecords.FirstOrDefault(item => ReferenceEquals(item.Plot, plot));
            if (record == null) return;
            var removedStorey = record.Storey;
            PlotRecords.Remove(record);
            var ordered = PlotRecords.OrderBy(item => item.Index).ToArray();
            for (var index = 0; index < ordered.Length; ++index)
                ordered[index].Index = index;
            if (ReferenceEquals(removedStorey, _upper) &&
                removedStorey.State.HospitalPlots.Count == 0)
                removedStorey.ContentRoot.SetActive(false);
            RefreshRowArrows();
        }

        private static void Refresh(Level level)
        {
            if (_refreshing || level == null) return;
            _refreshing = true;
            try
            {
                var menu = _menu;
                var lowerGrid = _lower?.GridRoot.activeSelf ?? false;
                var upperGrid = _upper?.GridRoot.activeSelf ?? false;
                Destroy(false);
                if (!Ensure(level)) return;
                _lower.GridRoot.SetActive(lowerGrid);
                _upper.GridRoot.SetActive(upperGrid);
                if (menu != null) AttachToolbar(menu, level);
            }
            finally
            {
                _refreshing = false;
            }
        }

        internal static void TogglePlot(HospitalPlot plot)
        {
            var record = PlotRecords.FirstOrDefault(item => ReferenceEquals(item.Plot, plot));
            if (record == null || _lower == null || _upper == null) return;
            MovePlot(record, ReferenceEquals(record.Storey, _upper) ? _lower : _upper);
        }

        private static void MovePlot(StoreyPlotRecord record, StoreyInstance target)
        {
            if (record?.Plot == null || target?.State == null ||
                ReferenceEquals(record.Storey, target)) return;
            var source = record.Storey;
            var level = target.Level;
            var oldPlot = record.Plot;
            var wasVisible = oldPlot.IsVisible();
            var bought = PlotBought.GetValue(oldPlot);
            var state = PlotState.GetValue(oldPlot);
            var timeLeft = PlotTimeLeft.GetValue(oldPlot);
            var tilesBuilt = PlotTilesBuilt.GetValue(oldPlot);
            var hiddenLayers = PlotHiddenLayers.GetValue(oldPlot) as bool[];
            hiddenLayers = hiddenLayers == null ? null : (bool[])hiddenLayers.Clone();

            CursorEditHospital cursor = null;
            level.CursorManager?.TryGetActiveMode(out cursor);
            var selected = cursor == null ? null : CursorSelectedPlot.GetValue(cursor) as HospitalPlot;
            var wasSelected = ReferenceEquals(selected, oldPlot);
            var normalEvents = level.BuildEvents;
            HospitalPlot replacement = null;
            try
            {
                LevelBuildEvents.SetValue(level, target.Events ?? normalEvents);
                _constructing = ReferenceEquals(target, _upper) ? target : null;
                replacement = new HospitalPlot(record.Definition, target.State, level);
                var insertion = PlotRecords.Count(item => item.Index < record.Index &&
                    ReferenceEquals(item.Storey, target));
                target.State.HospitalPlots.Insert(insertion, replacement);
                PlotBought.SetValue(replacement, bought);
                PlotState.SetValue(replacement, state);
                PlotTimeLeft.SetValue(replacement, timeLeft);
                PlotTilesBuilt.SetValue(replacement, tilesBuilt);
                if (hiddenLayers != null) PlotHiddenLayers.SetValue(replacement, hiddenLayers);
                PlotRefresh.Invoke(replacement, new object[] { false, true });
                replacement.SetVisible(wasVisible);
                AttachPlotContent(replacement, target);
                // Validar el grafo de destino antes de retirar la instancia original.
                RebuildNavigation(target);

                LevelBuildEvents.SetValue(level, source.Events ?? normalEvents);
                source.State.HospitalPlots.Remove(oldPlot);
                if (oldPlot.HospitalMap != null)
                {
                    source.State.DestroyHospitalMap(oldPlot.HospitalMap);
                    PlotMap.SetValue(oldPlot, null);
                }
                oldPlot.Destroy();

                record.Plot = replacement;
                record.Storey = target;
                UnderPressure.StoreyMapRegistry.SetStorey(record.Definition,
                    ReferenceEquals(target, _upper)
                        ? UnderPressure.StoreyMapRegistry.UpperStorey
                        : UnderPressure.StoreyMapRegistry.GroundStorey);
                target.ContentRoot.SetActive(true);
                if (ReferenceEquals(source, _upper) && source.State.HospitalPlots.Count == 0)
                    source.ContentRoot.SetActive(false);
                try
                {
                    RebuildNavigation(source);
                }
                catch (Exception navigationException)
                {
                    StoreyPlugin.Log?.LogError(
                        "La parcela cambió de planta, pero falló la actualización del grafo de origen: " +
                        navigationException);
                }
            }
            catch (Exception exception)
            {
                if (replacement != null && !ReferenceEquals(record.Plot, replacement))
                {
                    try
                    {
                        LevelBuildEvents.SetValue(level, target.Events ?? normalEvents);
                        target.State.HospitalPlots.Remove(replacement);
                        if (replacement.HospitalMap != null)
                        {
                            target.State.DestroyHospitalMap(replacement.HospitalMap);
                            PlotMap.SetValue(replacement, null);
                        }
                        replacement.Destroy();
                    }
                    catch (Exception rollbackException)
                    {
                        StoreyPlugin.Log?.LogError(
                            "No se pudo retirar la parcela incompleta: " + rollbackException);
                    }
                }
                StoreyPlugin.Log?.LogError("No se pudo cambiar la parcela de planta: " + exception);
                throw;
            }
            finally
            {
                _constructing = null;
                LevelBuildEvents.SetValue(level, normalEvents);
            }

            RebindRows(oldPlot, replacement);
            RebindCursorPreview(cursor, oldPlot, replacement, target);
            if (wasSelected)
                level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(replacement);
            RefreshRowArrows();
        }

        private static void RebindRows(HospitalPlot oldPlot, HospitalPlot replacement)
        {
            foreach (var row in Resources.FindObjectsOfTypeAll<PlotSelectionItem>())
            {
                if (!ReferenceEquals(PlotSelectionPlot.GetValue(row), oldPlot)) continue;
                PlotSelectionPlot.SetValue(row, replacement);
                var control = row.GetComponent<StoreyPlotRowControl>();
                control?.Bind(row, replacement);
            }
        }

        private static void RebindCursorPreview(CursorEditHospital cursor,
            HospitalPlot oldPlot, HospitalPlot replacement, StoreyInstance target)
        {
            var previews = cursor == null ? null :
                CursorPreviews.GetValue(cursor) as Dictionary<HospitalPlot, GameObject>;
            if (previews == null || CursorCreatePreview == null) return;
            if (previews.TryGetValue(oldPlot, out var oldPreview))
            {
                previews.Remove(oldPlot);
                if (oldPreview != null) UnityEngine.Object.Destroy(oldPreview);
            }
            CursorCreatePreview.Invoke(cursor, new object[] { replacement });
            if (previews.TryGetValue(replacement, out var preview) && preview != null)
                preview.transform.SetParent(target.Root.transform, false);
        }

        private static void RefreshRowArrows()
        {
            foreach (var control in Resources.FindObjectsOfTypeAll<StoreyPlotRowControl>())
                control.RefreshArrow();
        }

        private static void RebuildNavigation(StoreyInstance storey)
        {
            if (storey?.State == null) return;
            var existing = StateNavMesh.GetValue(storey.State) as TH20.NavMesh;
            var built = StateBuiltMaps.GetValue(storey.State) as List<HospitalMap>;
            if (built == null || built.Count == 0)
            {
                existing?.Destroy();
                StateNavMesh.SetValue(storey.State, null);
                return;
            }
            var first = built[0];
            var anchor = first.Anchor;
            var previousAnchor = StateAnchor.GetValue(storey.State);
            var navMesh = new TH20.NavMesh(
                first.FloorPlan.Width(), first.FloorPlan.Height(), anchor, 0);
            try
            {
                StateAnchor.SetValue(storey.State, anchor);
                StateNavMesh.SetValue(storey.State, navMesh);
                foreach (var map in built)
                    NavMeshUpdateFromMap.Invoke(navMesh, new object[] { anchor, map });
                storey.State.UpdateNavigation();
                PlaceNavMesh(storey, navMesh);
                existing?.Destroy();
            }
            catch
            {
                navMesh.Destroy();
                StateNavMesh.SetValue(storey.State, existing);
                StateAnchor.SetValue(storey.State, previousAnchor);
                throw;
            }
        }

        private static void PlaceNavMesh(StoreyInstance storey, TH20.NavMesh navMesh)
        {
            if (storey == null || navMesh == null || ReferenceEquals(storey, _lower)) return;
            var data = NativeNavMeshData.GetValue(navMesh) as NavMeshData;
            if (data == null) return;
            var current = (NavMeshDataInstance)NativeNavMeshInstance.GetValue(navMesh);
            if (current.valid) UnityEngine.AI.NavMesh.RemoveNavMeshData(current);
            var placed = UnityEngine.AI.NavMesh.AddNavMeshData(
                data, storey.Root.transform.position, Quaternion.identity);
            NativeNavMeshInstance.SetValue(navMesh, placed);
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

        internal static Transform ContentRootFor(RoomFloorPlanVisual visual)
        {
            if (_constructing != null) return _constructing.ContentRoot.transform;
            var state = VisualState.GetValue(visual) as WorldState;
            return state != null && Storeys.TryGetValue(state, out var storey)
                ? storey.ContentRoot.transform : null;
        }

        internal static Transform ConstructingContentRoot() =>
            _constructing?.ContentRoot.transform;

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

        internal static void AttachToolbar(EditHospitalMenu menu, Level level)
        {
            if (menu == null || !Ensure(level)) return;
            _menu = menu;
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
            if (_toolbar != null) UnityEngine.Object.Destroy(_toolbar);
            _toolbar = null;
            if (_lower != null)
            {
                DetachExistingContent(_lower);
                Storeys.Remove(_lower.State);
                UnityEngine.Object.Destroy(_lower.Root);
            }
            _lower = null;
            PlotRecords.Clear();
            var upper = _upper;
            _upper = null;
            _constructing = null;
            if (upper != null)
            {
                Storeys.Remove(upper.State);
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
            if (clearConfigs)
            {
                Configs.Clear();
                UnderPressure.StoreyMapRegistry.Clear();
                _menu = null;
            }
        }

        private static void DetachExistingContent(StoreyInstance storey)
        {
            if (storey?.State == null) return;
            foreach (var plot in storey.State.HospitalPlots)
            {
                var map = plot?.HospitalMap;
                var visual = map?.RoomVisual;
                var container = visual == null ? null :
                    VisualContainer.GetValue(visual) as Transform;
                if (container != null && container.parent == storey.ContentRoot.transform)
                    container.SetParent(null, true);
                var perimeter = map?.FootprintPerimeter;
                var perimeterRoot = perimeter == null ? null :
                    AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root")?
                        .GetValue(perimeter) as Transform;
                if (perimeterRoot != null && perimeterRoot.parent == storey.ContentRoot.transform)
                    perimeterRoot.SetParent(null, true);
                var demolishRoot = map == null ? null :
                    AccessTools.Field(typeof(HospitalMap), "_demolishRoot")?
                        .GetValue(map) as Transform;
                if (demolishRoot != null && demolishRoot.parent == storey.ContentRoot.transform)
                    demolishRoot.SetParent(null, true);
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
        }

        internal void RefreshArrow()
        {
            if (_arrow == null) return;
            _arrow.text = UnderPressure.StoreyMapRegistry.GetStorey(_plot) ==
                          UnderPressure.StoreyMapRegistry.UpperStorey
                ? "↑" : "↓";
        }

        private void Create(PlotSelectionItem row)
        {
            var root = new GameObject("UP Atajo de planta", typeof(RectTransform),
                typeof(Button));
            root.transform.SetParent(row.transform, false);
            _button = root.GetComponent<Button>();
            var sound = root.AddComponent<ButtonSFX>();
            sound.AudioEvent = ButtonSFX.ButtonAudioEvent.Default;
            _button.onClick.AddListener(() => StoreyRuntime.TogglePlot(_plot));

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
            var rect = _button?.transform as RectTransform;
            if (toggleRect == null || rect == null) return;
            rect.SetParent(row.transform, false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
            rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(toggleRect.anchoredPosition.x - 17f, 0f);
            rect.sizeDelta = new Vector2(16f, toggleRect.rect.height);
            rect.localScale = Vector3.one;
            var layout = _button.GetComponent<LayoutElement>() ??
                _button.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        private void OnDestroy()
        {
            _button?.onClick.RemoveAllListeners();
        }
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
        private static bool Prefix(WorldState __instance) =>
            !StoreyRuntime.Constructing && !StoreyRuntime.IsUpper(__instance);
    }

    [HarmonyPatch(typeof(WorldState), "BuildRoom")]
    internal static class StoreyRoomBuildIsolationPatch
    {
        private static bool Prefix(WorldState __instance) =>
            !StoreyRuntime.Constructing && !StoreyRuntime.IsUpper(__instance);
    }

    [HarmonyPatch(typeof(HospitalMap), "RemoveTerrainDetailMeshes")]
    internal static class StoreyTerrainIsolationPatch
    {
        private static bool Prefix() => !StoreyRuntime.Constructing;
    }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), "UpdateFromRoom")]
    internal static class StoreyContentRootPatch
    {
        private static void Prefix(RoomFloorPlanVisual __instance)
        {
            var root = StoreyRuntime.ContentRootFor(__instance);
            var container = AccessTools.Field(typeof(RoomFloorPlanVisual), "_container")?
                .GetValue(__instance) as Transform;
            if (root != null && container != null && container.parent == root)
                container.SetParent(null, true);
        }

        private static void Postfix(RoomFloorPlanVisual __instance)
        {
            var root = StoreyRuntime.ContentRootFor(__instance);
            var container = AccessTools.Field(typeof(RoomFloorPlanVisual), "_container")?
                .GetValue(__instance) as Transform;
            if (root != null && container != null) container.SetParent(root, false);
        }
    }

    [HarmonyPatch(typeof(HospitalPlotFootprintPerimeter), MethodType.Constructor,
        new[] { typeof(GameObject[]), typeof(float), typeof(bool) })]
    internal static class StoreyPerimeterRootPatch
    {
        private static void Postfix(HospitalPlotFootprintPerimeter __instance)
        {
            var root = StoreyRuntime.ConstructingContentRoot();
            var ownRoot = AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root")?
                .GetValue(__instance) as Transform;
            if (root != null && ownRoot != null) ownRoot.SetParent(root, false);
        }
    }

    [HarmonyPatch(typeof(HospitalMap), "CreateDemolishRoot")]
    internal static class StoreyDemolishRootPatch
    {
        private static void Postfix(HospitalMap __instance)
        {
            var root = StoreyRuntime.ConstructingContentRoot();
            var ownRoot = AccessTools.Field(typeof(HospitalMap), "_demolishRoot")?
                .GetValue(__instance) as Transform;
            if (root != null && ownRoot != null) ownRoot.SetParent(root, false);
        }
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
        private static void Postfix(PlotSelectionItem __instance, HospitalPlot __0)
        {
            if (__instance == null || __0 == null) return;
            var control = __instance.GetComponent<StoreyPlotRowControl>() ??
                __instance.gameObject.AddComponent<StoreyPlotRowControl>();
            control.Bind(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(Level), "Destroy")]
    internal static class StoreyDestroyPatch
    {
        private static void Prefix() => StoreyRuntime.Destroy(true);
    }
}
