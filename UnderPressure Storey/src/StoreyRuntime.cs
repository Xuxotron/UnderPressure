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
        private static readonly MethodInfo PlotRefresh =
            AccessTools.Method(typeof(HospitalPlot), "RefreshHospitalMap");
        private static readonly FieldInfo CursorHospitalWorldState =
            AccessTools.Field(typeof(CursorEditHospital), "_worldState");
        private static readonly Dictionary<Level, WorldState.Config> Configs =
            new Dictionary<Level, WorldState.Config>();
        private static readonly List<HospitalPlot> PlotOrder = new List<HospitalPlot>();
        private static readonly Dictionary<HospitalPlot, StoreyInstance> PlotStoreys =
            new Dictionary<HospitalPlot, StoreyInstance>();

        private static StoreyInstance _lower;
        private static StoreyInstance _upper;
        private static StoreyInstance _constructing;
        private static StoreyInstance _active;
        private static GameObject _toolbar;
        private static Material _gridMaterial;

        internal static bool Constructing => _constructing != null;
        internal static bool IsUpper(WorldState state) =>
            state != null && _upper != null && ReferenceEquals(state, _upper.State);
        internal static float CoordinateHeight =>
            (_constructing ?? _active)?.Root.transform.position.y ?? 0f;

        internal static void Register() => UnderPressure.StoreyMapRegistry.RegisterRuntime(
            GetAllPlots, GetWorld, Refresh, ActivateGround, OnPlotAdded, OnPlotRemoved);

        internal static void Unregister() =>
            UnderPressure.StoreyMapRegistry.UnregisterRuntime();

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
            foreach (var plot in _lower.State.HospitalPlots)
            {
                PlotOrder.Add(plot);
                PlotStoreys[plot] = _lower;
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
                RemoveUpperSeed();
                BuildVectorGrid(upper, bounds);
                foreach (var plot in PlotOrder.Where(plot =>
                             UnderPressure.StoreyMapRegistry.GetStorey(plot) ==
                             UnderPressure.StoreyMapRegistry.UpperStorey).ToArray())
                    MovePlot(plot, upper, false);
                Activate(_lower);
                StoreyPlugin.Log?.LogInfo(
                    "Segundo WorldState creado; rejillas vectoriales en Y=0 e Y=2.");
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

        internal static void TogglePlot(HospitalPlot plot)
        {
            if (plot == null || !PlotStoreys.TryGetValue(plot, out var source) ||
                _lower == null || _upper == null) return;
            StopActiveEditModes(source.Level);
            MovePlot(plot, ReferenceEquals(source, _upper) ? _lower : _upper, true);
        }

        internal static void ActivatePlot(HospitalPlot plot)
        {
            if (plot == null || !PlotStoreys.TryGetValue(plot, out var storey)) return;
            if (!ReferenceEquals(_active, storey)) StopActiveEditModes(storey.Level);
            Activate(storey);
        }

        private static void StopActiveEditModes(Level level)
        {
            var cursor = level?.CursorManager;
            if (cursor == null) return;
            // Se cierran mediante su ciclo nativo mientras Level conserva todavía
            // los eventos del plano de origen; así se desuscriben del bus correcto.
            cursor.PopMode<CursorEditHospitalItem>();
            cursor.PopMode<CursorEditHospitalBuild>();
            cursor.PopMode<CursorEditHospitalMovePlot>();
        }

        private static void Activate(StoreyInstance storey)
        {
            if (storey?.Level == null || storey.State == null) return;
            _active = storey;
            LevelWorldState.SetValue(storey.Level, storey.State);
            LevelBuildEvents.SetValue(storey.Level, storey.Events);
            if (storey.Level.CursorManager != null &&
                storey.Level.CursorManager.TryGetActiveMode<CursorEditHospital>(out var cursor))
                CursorHospitalWorldState.SetValue(cursor, storey.State);
            storey.Level.CursorManager?.SetPlaneOffset(0f);
        }

        private static void MovePlot(HospitalPlot plot, StoreyInstance target, bool select)
        {
            if (plot == null || target?.State == null ||
                !PlotStoreys.TryGetValue(plot, out var source) || ReferenceEquals(source, target)) return;
            var level = target.Level;
            var previousActive = _active;
            var removedFromSource = false;
            var addedToTarget = false;
            try
            {
                _constructing = source;
                LevelBuildEvents.SetValue(level, source.Events);
                if (plot.HospitalMap != null)
                {
                    source.State.DestroyHospitalMap(plot.HospitalMap);
                    PlotMap.SetValue(plot, null);
                }
                source.State.HospitalPlots.Remove(plot);
                removedFromSource = true;

                _constructing = target;
                LevelBuildEvents.SetValue(level, target.Events);
                PlotWorldState.SetValue(plot, target.State);
                target.State.HospitalPlots.Add(plot);
                addedToTarget = true;
                PlotRefresh.Invoke(plot, new object[] { false, true });
                PlotStoreys[plot] = target;
                UnderPressure.StoreyMapRegistry.SetStorey(plot.Definition,
                    ReferenceEquals(target, _upper)
                        ? UnderPressure.StoreyMapRegistry.UpperStorey
                        : UnderPressure.StoreyMapRegistry.GroundStorey);
            }
            catch (Exception exception)
            {
                StoreyPlugin.Log?.LogError(
                    "No se pudo trasladar la parcela entre plantas: " + exception);
                try
                {
                    _constructing = target;
                    LevelBuildEvents.SetValue(level, target.Events);
                    if (plot.HospitalMap != null)
                    {
                        target.State.DestroyHospitalMap(plot.HospitalMap);
                        PlotMap.SetValue(plot, null);
                    }
                    if (addedToTarget) target.State.HospitalPlots.Remove(plot);
                    _constructing = source;
                    LevelBuildEvents.SetValue(level, source.Events);
                    PlotWorldState.SetValue(plot, source.State);
                    if (removedFromSource && !source.State.HospitalPlots.Contains(plot))
                        source.State.HospitalPlots.Add(plot);
                    PlotRefresh.Invoke(plot, new object[] { false, true });
                    PlotStoreys[plot] = source;
                }
                catch (Exception rollbackException)
                {
                    StoreyPlugin.Log?.LogError(
                        "No se pudo restaurar la parcela después del error: " + rollbackException);
                }
                throw;
            }
            finally
            {
                _constructing = null;
                Activate(select ? target : previousActive ?? _lower);
            }
            RefreshRowArrows();
            if (select) level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plot);
        }

        private static IList<HospitalPlot> GetAllPlots(Level level) =>
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
            if (!Ensure(level))
                throw new InvalidOperationException(
                    "No se pudo reconstruir el segundo WorldState.");
        }

        private static void ActivateGround(Level level)
        {
            if (_lower != null && ReferenceEquals(_lower.Level, level)) Activate(_lower);
        }

        private static void OnPlotAdded(Level level, HospitalPlot plot)
        {
            if (plot == null || _lower == null || !ReferenceEquals(_lower.Level, level) ||
                PlotStoreys.ContainsKey(plot)) return;
            var world = PlotWorldState.GetValue(plot) as WorldState;
            var storey = _upper != null && ReferenceEquals(world, _upper.State) ? _upper : _lower;
            PlotOrder.Add(plot);
            PlotStoreys[plot] = storey;
            UnderPressure.StoreyMapRegistry.SetStorey(plot.Definition,
                ReferenceEquals(storey, _upper)
                    ? UnderPressure.StoreyMapRegistry.UpperStorey
                    : UnderPressure.StoreyMapRegistry.GroundStorey);
            RefreshRowArrows();
        }

        private static void OnPlotRemoved(Level level, HospitalPlot plot)
        {
            if (plot == null || _lower == null || !ReferenceEquals(_lower.Level, level)) return;
            PlotOrder.Remove(plot);
            PlotStoreys.Remove(plot);
            RefreshRowArrows();
        }

        private static void RefreshRowArrows()
        {
            foreach (var control in Resources.FindObjectsOfTypeAll<StoreyPlotRowControl>())
                control.RefreshArrow();
        }

        internal static Vector3 FloorPlanWorldOrigin(FloorPlan floorPlan)
        {
            if (floorPlan == null) return Vector3.zero;
            var anchor = floorPlan.Anchor;
            var height = PlotStoreys.TryGetValue(floorPlan.HospitalMap?.Plot, out var storey)
                ? storey.Root.transform.position.y : 0f;
            return new Vector3(anchor.X * GridCoord.CellSize, height,
                anchor.Y * GridCoord.CellSize);
        }

        internal static void AttachToolbar(EditHospitalMenu menu, Level level)
        {
            if (menu == null || level == null) return;
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

        internal static void InitialiseAfterNativeCursor(Level level)
        {
            if (!Ensure(level))
                StoreyPlugin.Log?.LogError(
                    "El segundo WorldState no pudo iniciarse después del cursor nativo.");
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
            if (lower?.Level != null && lower.State != null)
            {
                LevelWorldState.SetValue(lower.Level, lower.State);
                LevelBuildEvents.SetValue(lower.Level, lower.Events);
            }
            if (_toolbar != null) UnityEngine.Object.Destroy(_toolbar);
            _toolbar = null;
            if (lower != null) UnityEngine.Object.Destroy(lower.Root);
            _lower = null;
            PlotOrder.Clear();
            PlotStoreys.Clear();
            var upper = _upper;
            _upper = null;
            _constructing = null;
            _active = null;
            if (upper != null)
            {
                var normalEvents = lower?.Events ?? upper.Level?.BuildEvents;
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
        private static bool Prefix(WorldState __instance) =>
            !StoreyRuntime.Constructing && !StoreyRuntime.IsUpper(__instance);
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

    [HarmonyPatch(typeof(CursorEditHospital), MethodType.Constructor,
        new[] { typeof(CursorManager), typeof(Level), typeof(CursorEditHospital.Config) })]
    [HarmonyPriority(Priority.Last)]
    internal static class StoreyEditorCursorReadyPatch
    {
        private static void Postfix(Level __1) =>
            StoreyRuntime.InitialiseAfterNativeCursor(__1);
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
    }

    [HarmonyPatch(typeof(CursorEditHospitalItem), "OnSelectHospitalPlot")]
    internal static class StoreyStaleItemSelectionPatch
    {
        private static readonly FieldInfo WorldField =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_worldState");

        private static bool Prefix(CursorEditHospitalItem __instance, HospitalPlot __0)
        {
            var expected = UnderPressure.StoreyMapRegistry.GetWorld(null, __0);
            return expected == null || ReferenceEquals(WorldField.GetValue(__instance), expected);
        }
    }

    [HarmonyPatch(typeof(GridCoord), nameof(GridCoord.GridCoordToWorldPosition),
        new[] { typeof(int), typeof(int) })]
    internal static class StoreyGridWorldPositionPatch
    {
        private static void Postfix(ref Vector3 __result) =>
            __result.y += StoreyRuntime.CoordinateHeight;
    }

    [HarmonyPatch(typeof(CursorManager), nameof(CursorManager.SetPlaneOffset))]
    internal static class StoreyCursorPlanePatch
    {
        private static void Prefix(ref float __0) => __0 += StoreyRuntime.CoordinateHeight;
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

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var codes = source.ToList();
            for (var index = 0; index + 1 < codes.Count; ++index)
            {
                if (!codes[index].Calls(AnchorGetter) || !codes[index + 1].Calls(ToWorld)) continue;
                codes[index] = new CodeInstruction(OpCodes.Call, Replacement)
                    .MoveLabelsFrom(codes[index]);
                codes[index + 1].opcode = OpCodes.Nop;
                codes[index + 1].operand = null;
                return codes;
            }
            throw new InvalidOperationException(
                "No se encontró la conversión nativa de origen de iluminación interior.");
        }
    }

    [HarmonyPatch(typeof(Level), "Destroy")]
    internal static class StoreyDestroyPatch
    {
        private static void Prefix() => StoreyRuntime.Destroy(true);
    }
}
