using System;
using System.Collections.Generic;
using System.Reflection;
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
        private static readonly Dictionary<Level, WorldState.Config> Configs =
            new Dictionary<Level, WorldState.Config>();
        private static readonly Dictionary<WorldState, StoreyInstance> Storeys =
            new Dictionary<WorldState, StoreyInstance>();

        private static StoreyInstance _lower;
        private static StoreyInstance _upper;
        private static StoreyInstance _constructing;
        private static GameObject _toolbar;
        private static Material _gridMaterial;

        internal static bool Constructing => _constructing != null;

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
            BuildVectorGrid(_lower);

            var isolatedEvents = new BuildEvents();
            var upper = new StoreyInstance(level, isolatedEvents, 1);
            var normalEvents = level.BuildEvents;
            var savedPostConstruct = level.PostConstruct;
            _constructing = upper;
            try
            {
                LevelBuildEvents.SetValue(level, isolatedEvents);
                var state = new WorldState(
                    CloneConfig.Invoke(source, null) as WorldState.Config,
                    level,
                    LevelMetagame.GetValue(level) as Metagame,
                    level.VisualManager,
                    level.Config.GetDataViewManagerConfig().ValueMaterial,
                    level.Config.GetBuildingLogicConfig().RoomItemEditConfig);
                upper.State = state;
                Storeys.Add(state, upper);
                _upper = upper;
                BuildVectorGrid(upper);
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

        private static void BuildVectorGrid(StoreyInstance storey)
        {
            if (!TryGetBounds(storey.State, out var bounds)) return;
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

        internal static Transform ContentRootFor(RoomFloorPlanVisual visual)
        {
            if (_constructing != null) return _constructing.ContentRoot.transform;
            var state = VisualState.GetValue(visual) as WorldState;
            return state != null && Storeys.TryGetValue(state, out var storey)
                ? storey.ContentRoot.transform : null;
        }

        internal static Transform ConstructingContentRoot() =>
            _constructing?.ContentRoot.transform;

        internal static void ShowLowerGrid()
        {
            if (_lower == null || _upper == null) return;
            _upper.GridRoot.SetActive(false);
            _lower.GridRoot.SetActive(true);
        }

        internal static void ShowUpperGrid()
        {
            if (_lower == null || _upper == null) return;
            _lower.GridRoot.SetActive(false);
            _upper.GridRoot.SetActive(true);
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
                "storey.view.lower_grid"), 110f, ShowLowerGrid);
            AddButton(template, rect, UnderPressure.ModLocalization.Get(
                "storey.view.upper_grid"), 330f, ShowUpperGrid);
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
            if (_lower != null) UnityEngine.Object.Destroy(_lower.Root);
            _lower = null;
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
            if (clearConfigs) Configs.Clear();
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

    [HarmonyPatch(typeof(Level), "Destroy")]
    internal static class StoreyDestroyPatch
    {
        private static void Prefix() => StoreyRuntime.Destroy(true);
    }
}
