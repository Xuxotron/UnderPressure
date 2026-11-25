using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Storey
{
    // Una planta posee su estado lógico y una única raíz espacial. El contenido
    // nativo conserva sus coordenadas locales; nunca recibe desplazamientos Y.
    internal sealed class StoreyWorldInstance
    {
        internal readonly Level Level;
        internal readonly GameObject Root;
        internal readonly BuildEvents Events;
        internal WorldState WorldState;

        internal StoreyWorldInstance(Level level, int index, BuildEvents events)
        {
            Level = level;
            Events = events;
            Root = new GameObject("UnderPressure Storey " + index);
            Root.transform.position = new Vector3(0f, index * 2f, 0f);
            Root.SetActive(false);
        }
    }

    internal static class StoreyWorldManager
    {
        private static readonly MethodInfo CloneMethod =
            AccessTools.Method(typeof(WorldState.Config), "MemberwiseClone");
        private static readonly FieldInfo LevelMetagameField =
            AccessTools.Field(typeof(Level), "_metagame");
        private static readonly FieldInfo LevelBuildEventsField =
            AccessTools.Field(typeof(Level), "_buildEvents");
        private static readonly FieldInfo RoomVisualWorldStateField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_worldState");
        private static readonly Dictionary<Level, WorldState.Config> PrimaryConfigs =
            new Dictionary<Level, WorldState.Config>();
        private static readonly Dictionary<WorldState, StoreyWorldInstance> Instances =
            new Dictionary<WorldState, StoreyWorldInstance>();
        private static readonly Dictionary<RoomFloorPlanVisual, bool> LowerVisibility =
            new Dictionary<RoomFloorPlanVisual, bool>();

        private static StoreyWorldInstance _upper;
        private static StoreyWorldInstance _constructing;
        private static GameObject _toolbarRoot;

        internal static bool IsConstructingUpper => _constructing != null;

        internal static void CapturePrimaryConfig(
            WorldState.Config config, Level level)
        {
            if (_constructing != null || config == null || level == null) return;
            if (level.WorldState != null) return;
            PrimaryConfigs[level] = config;
        }

        internal static bool EnsureUpper(Level level)
        {
            if (level == null) return false;
            if (_upper != null && ReferenceEquals(_upper.Level, level)) return true;
            if (!PrimaryConfigs.TryGetValue(level, out var sourceConfig))
            {
                StoreyPlugin.Log?.LogError(
                    "No se capturó la configuración nativa del WorldState inferior.");
                return false;
            }
            Shutdown(false);

            var config = CloneMethod?.Invoke(sourceConfig, null) as WorldState.Config;
            var metagame = LevelMetagameField?.GetValue(level) as Metagame;
            var mainEvents = level.BuildEvents;
            var isolatedEvents = new BuildEvents();
            var previousPostConstruct = level.PostConstruct;
            var candidate = new StoreyWorldInstance(level, 1, isolatedEvents);
            _constructing = candidate;

            try
            {
                LevelBuildEventsField.SetValue(level, isolatedEvents);
                var dataViewConfig = level.Config.GetDataViewManagerConfig();
                var buildingConfig = level.Config.GetBuildingLogicConfig();
                var upperState = new WorldState(config, level, metagame,
                    level.VisualManager, dataViewConfig.ValueMaterial,
                    buildingConfig.RoomItemEditConfig);
                candidate.WorldState = upperState;
                Instances.Add(upperState, candidate);
                _upper = candidate;
                ConfigureGridPreview(candidate);
                StoreyPlugin.Log?.LogInfo(
                    "WorldState superior independiente creado: " +
                    upperState.HospitalPlots.Count + " parcelas; raíz de planta Y=2.");
                return true;
            }
            catch (Exception exception)
            {
                StoreyPlugin.Log?.LogError(
                    "No se pudo crear el WorldState superior: " + exception);
                UnityEngine.Object.Destroy(candidate.Root);
                return false;
            }
            finally
            {
                level.PostConstruct = previousPostConstruct;
                LevelBuildEventsField.SetValue(level, mainEvents);
                _constructing = null;
            }
        }

        internal static bool Owns(WorldState state) =>
            state != null && (Instances.ContainsKey(state) ||
                (_constructing != null && ReferenceEquals(_constructing.WorldState, state)));

        internal static Transform RootFor(RoomFloorPlanVisual visual)
        {
            if (visual == null) return null;
            var state = RoomVisualWorldStateField?.GetValue(visual) as WorldState;
            if (state != null && Instances.TryGetValue(state, out var instance))
                return instance.Root.transform;
            return _constructing?.Root.transform;
        }

        internal static Transform CurrentConstructionRoot() =>
            _constructing?.Root.transform;

        // Esta primera prueba muestra únicamente los tiles reales generados por
        // el segundo WorldState. No crea una rejilla sustitutiva ni la desplaza.
        private static void ConfigureGridPreview(StoreyWorldInstance instance)
        {
            foreach (var plot in instance.WorldState.HospitalPlots)
            {
                var map = plot?.HospitalMap;
                if (map == null) continue;
                var visual = map?.RoomVisual;
                if (visual != null)
                {
                    if (visual.WallsContainer != null)
                        visual.WallsContainer.gameObject.SetActive(false);
                    foreach (var item in visual.RoomItems)
                        item?.SetActive(false);
                }

                var perimeter = AccessTools.Field(typeof(HospitalMap),
                    "_footprintPerimeter")?.GetValue(map);
                var perimeterRoot = perimeter == null ? null :
                    AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root")?
                        .GetValue(perimeter) as Transform;
                if (perimeterRoot != null) perimeterRoot.gameObject.SetActive(false);
            }
        }

        internal static void ShowLower(Level level)
        {
            if (_upper == null || !ReferenceEquals(_upper.Level, level)) return;
            _upper.Root.SetActive(false);
            foreach (var pair in LowerVisibility)
                if (pair.Key != null) pair.Key.SetVisible(pair.Value);
            LowerVisibility.Clear();
        }

        internal static void ShowUpper(Level level)
        {
            if (_upper == null || !ReferenceEquals(_upper.Level, level)) return;
            LowerVisibility.Clear();
            foreach (var plot in level.WorldState.HospitalPlots)
            {
                var visual = plot?.HospitalMap?.RoomVisual;
                if (visual == null) continue;
                LowerVisibility[visual] = visual.IsVisible();
                visual.SetVisible(false);
            }
            _upper.Root.SetActive(true);
        }

        internal static void AttachToolbar(EditHospitalMenu menu, Level level)
        {
            if (menu == null || !EnsureUpper(level)) return;
            if (_toolbarRoot != null) UnityEngine.Object.Destroy(_toolbarRoot);

            var template = AccessTools.Field(typeof(EditHospitalMenu), "_quitButton")?
                .GetValue(menu) as Button;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (template == null || canvas == null)
            {
                StoreyPlugin.Log?.LogError(
                    "No se encontró la referencia nativa para los botones de planta.");
                return;
            }

            var parent = FindEditorUiRoot(canvas) ?? canvas.transform;
            _toolbarRoot = new GameObject("UP Storey Toolbar", typeof(RectTransform));
            _toolbarRoot.transform.SetParent(parent, false);
            var rootRect = (RectTransform)_toolbarRoot.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(.5f, 1f);
            rootRect.pivot = new Vector2(.5f, 1f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;

            CreateButton(template, rootRect,
                UnderPressure.ModLocalization.Get("storey.view.lower_grid"),
                110f, () => ShowLower(level));
            CreateButton(template, rootRect,
                UnderPressure.ModLocalization.Get("storey.view.upper_grid"),
                330f, () => ShowUpper(level));
            ShowLower(level);
        }

        private static Transform FindEditorUiRoot(Canvas canvas)
        {
            var rootCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            for (var index = 0; index < rootCanvas.transform.childCount; ++index)
            {
                var child = rootCanvas.transform.GetChild(index);
                if (child.name == "UP Scenario Editor UI") return child;
            }
            return null;
        }

        private static void CreateButton(Button template, Transform parent,
            string label, float x, Action action)
        {
            var button = UnityEngine.Object.Instantiate(template, parent, false);
            button.gameObject.name = "UP Storey - " + label;
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
            var rect = button.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -12f);
            rect.sizeDelta = new Vector2(208f, 40f);
            button.gameObject.SetActive(true);
        }

        internal static void Shutdown(bool clearConfigs)
        {
            if (_toolbarRoot != null)
            {
                UnityEngine.Object.Destroy(_toolbarRoot);
                _toolbarRoot = null;
            }
            foreach (var pair in LowerVisibility)
                if (pair.Key != null) pair.Key.SetVisible(pair.Value);
            LowerVisibility.Clear();

            var upper = _upper;
            _upper = null;
            _constructing = null;
            if (upper != null)
            {
                Instances.Remove(upper.WorldState);
                var level = upper.Level;
                var mainEvents = level?.BuildEvents;
                try
                {
                    if (level != null)
                        LevelBuildEventsField.SetValue(level, upper.Events);
                    upper.WorldState?.Destroy();
                }
                catch (Exception exception)
                {
                    StoreyPlugin.Log?.LogError(
                        "Error destruyendo el WorldState superior: " + exception);
                }
                finally
                {
                    if (level != null)
                        LevelBuildEventsField.SetValue(level, mainEvents);
                    if (upper.Root != null)
                        UnityEngine.Object.Destroy(upper.Root);
                }
            }
            if (clearConfigs) PrimaryConfigs.Clear();
        }
    }

    [HarmonyPatch(typeof(WorldState), MethodType.Constructor,
        new[] { typeof(WorldState.Config), typeof(Level), typeof(Metagame),
            typeof(VisualManager), typeof(Material), typeof(RoomItemVisualEdit.Config) })]
    [HarmonyAfter("artef.twopointhospital.underpressure.editor")]
    [HarmonyPriority(Priority.Last)]
    internal static class StoreyCaptureWorldConfigPatch
    {
        private static void Prefix(WorldState.Config __0, Level __1) =>
            StoreyWorldManager.CapturePrimaryConfig(__0, __1);
    }

    [HarmonyPatch(typeof(WorldState), "CalculateLighting")]
    internal static class StoreyIsolateLightingPatch
    {
        private static bool Prefix(WorldState __instance) =>
            !StoreyWorldManager.IsConstructingUpper &&
            !StoreyWorldManager.Owns(__instance);
    }

    [HarmonyPatch(typeof(WorldState), "BuildRoom")]
    internal static class StoreyIsolateRoomBuildPatch
    {
        private static bool Prefix(WorldState __instance) =>
            !StoreyWorldManager.IsConstructingUpper &&
            !StoreyWorldManager.Owns(__instance);
    }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), "UpdateFromRoom")]
    internal static class StoreyBindRoomVisualPatch
    {
        private static void Prefix(RoomFloorPlanVisual __instance)
        {
            var root = StoreyWorldManager.RootFor(__instance);
            var container = AccessTools.Field(typeof(RoomFloorPlanVisual), "_container")?
                .GetValue(__instance) as Transform;
            if (root != null && container != null && container.parent == root)
                container.SetParent(null, true);
        }

        private static void Postfix(RoomFloorPlanVisual __instance)
        {
            var root = StoreyWorldManager.RootFor(__instance);
            var container = AccessTools.Field(typeof(RoomFloorPlanVisual), "_container")?
                .GetValue(__instance) as Transform;
            if (root != null && container != null)
                container.SetParent(root, false);
        }
    }

    [HarmonyPatch(typeof(HospitalPlotFootprintPerimeter), MethodType.Constructor)]
    internal static class StoreyBindPerimeterPatch
    {
        private static void Postfix(HospitalPlotFootprintPerimeter __instance)
        {
            var root = StoreyWorldManager.CurrentConstructionRoot();
            var ownRoot = AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root")?
                .GetValue(__instance) as Transform;
            if (root != null && ownRoot != null) ownRoot.SetParent(root, false);
        }
    }

    [HarmonyPatch(typeof(HospitalMap), "CreateDemolishRoot")]
    internal static class StoreyBindDemolishRootPatch
    {
        private static void Postfix(HospitalMap __instance)
        {
            var root = StoreyWorldManager.CurrentConstructionRoot();
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
            StoreyWorldManager.AttachToolbar(__instance, __0);
    }

    [HarmonyPatch(typeof(Level), "Destroy")]
    internal static class StoreyLevelDestroyPatch
    {
        private static void Prefix() => StoreyWorldManager.Shutdown(true);
    }
}
