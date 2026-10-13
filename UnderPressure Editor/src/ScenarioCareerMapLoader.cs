// Updated: 2026-10-13
// Carga .upmap + .json/.png al crear una partida nueva. No toca las definiciones originales.
// Las partidas del juego base permanecen intactas; el guardado de carrera personalizado sigue bloqueado.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioCareerMapLoader
    {
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly List<Texture2D> TemporaryTextures = new List<Texture2D>();
        private static readonly List<Material> TemporaryMaterials = new List<Material>();
        private static readonly Dictionary<Renderer, Material[]> OriginalRendererMaterials =
            new Dictionary<Renderer, Material[]>();
        private static LevelConfig _base;
        private static string _path;
        private static bool _active;
        private static ScenarioMapFormat.LoadedMap _map;
        private static ScenarioPlotRuntime.Collection _plotAssets;
        internal static bool IsCustomSession => _active || _path != null;

        internal static void Prepare(LevelConfig level, string path)
        {
            if (level == null || string.IsNullOrEmpty(path)) throw new ArgumentException("Mapa incorrecto.");
            var full = Path.GetFullPath(path);
            var folder = Path.GetFullPath(ScenarioEditorStorage.Folder).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
            if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".upmap", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El mapa no esta en la carpeta maps.");
            var read = ScenarioMapFormat.Read(full);
            if (!string.Equals(read.Header.baseHospitalId, level.UniqueId, StringComparison.Ordinal))
                throw new InvalidDataException("Este mapa corresponde a otro hospital.");
            Cancel();
            _map = read;
            _base = level;
            _path = full;
            UnderPressureEditorPlugin.Log?.LogInfo("Carrera: mapa personalizado seleccionado: " + _path);
        }

        internal static void Cancel()
        {
            foreach (var pair in OriginalRendererMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            OriginalRendererMaterials.Clear();
            foreach (var material in TemporaryMaterials)
                if (material != null) UnityEngine.Object.Destroy(material);
            TemporaryMaterials.Clear();
            foreach (var texture in TemporaryTextures)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            TemporaryTextures.Clear();
            _plotAssets?.DestroyAssets();
            _plotAssets = null;
            _base = null;
            _path = null;
            _map = null;
            _active = false;
        }

        internal static bool MatchesConfig(LevelConfig config) =>
            config != null && _base != null &&
            string.Equals(config.UniqueId, _base.UniqueId, StringComparison.Ordinal);

        internal static bool Match(Level level) =>
            level != null && level.Config != null && MatchesConfig(level.Config);

        // Prefijo del constructor WorldState: insertar la lista real de parcelas antes del foreach nativo.
        internal static void StartBuilding(ref WorldState.Config config, Level level, Metagame metagame)
        {
            if (!Match(level) || _map == null || config?.HospitalPlots == null) return;
            if (_plotAssets != null) return;
            var prepared = ScenarioPlotRuntime.Build(_map, config.HospitalPlots, metagame);
            try
            {
                var cloned = (WorldState.Config)CloneMethod.Invoke(config, null);
                cloned.HospitalPlots = prepared.Wrappers
                    .ConvertAll<FullInspector.SharedInstance<HospitalPlotDefinition>>(item => item);
                config = cloned;
                _plotAssets = prepared;
                _active = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: WorldState creado con " + cloned.HospitalPlots.Count + " parcelas personalizadas.");
            }
            catch
            {
                prepared.DestroyAssets();
                throw;
            }
        }

        internal static void LoadTerrain(Level level)
        {
            if (!_active || !Match(level) || _path == null) return;
            try
            {
                var folder = Path.GetDirectoryName(_path);
                var roots = UnityEngine.Object.FindObjectsOfType<Transform>();
                var root = roots.FirstOrDefault(t => t != null &&
                    t.name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase));
                if (root == null) return;
                var renderers = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r != null && r.gameObject.name.StartsWith("Pass", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                for (var i = 0; i < renderers.Count; i++)
                {
                    var path = Path.Combine(folder, "alpha_" + i + ".png");
                    if (!File.Exists(path)) continue;
                    var renderer = renderers[i];
                    var originals = renderer.sharedMaterials;
                    if (originals == null || originals.Length == 0 || originals[0] == null ||
                        !originals[0].HasProperty("_Control")) continue;
                    if (!OriginalRendererMaterials.ContainsKey(renderer))
                        OriginalRendererMaterials.Add(renderer, originals);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }
                    TemporaryTextures.Add(texture);
                    var material = new Material(originals[0]);
                    TemporaryMaterials.Add(material);
                    material.SetTexture("_Control", texture);
                    var assigned = (Material[])originals.Clone();
                    assigned[0] = material;
                    renderer.sharedMaterials = assigned;
                }
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudieron aplicar las texturas del mapa: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(App), "LoadLevel", new[] {
        typeof(LevelConfig), typeof(SaveFileHeader), typeof(bool), typeof(bool) })]
    internal static class ScenarioCareerMapIgnoreSavePatch
    {
        private static void Prefix(LevelConfig __0, ref SaveFileHeader __1, ref bool __2)
        {
            if (!ScenarioCareerMapLoader.IsCustomSession || ScenarioEditorFlow.IsRunning) return;
            if (!ScenarioCareerMapLoader.MatchesConfig(__0))
            {
                ScenarioCareerMapLoader.Cancel();
                return;
            }
            __1 = null;
            __2 = true;
        }
    }

    [HarmonyPatch(typeof(WorldState), MethodType.Constructor,
        new[] { typeof(WorldState.Config), typeof(Level), typeof(Metagame),
            typeof(VisualManager), typeof(Material), typeof(RoomItemVisualEdit.Config) })]
    internal static class ScenarioCareerMapWorldBeginPatch
    {
        private static void Prefix(ref WorldState.Config __0, Level __1, Metagame __2)
        {
            if (ScenarioCareerMapLoader.IsCustomSession && !ScenarioEditorFlow.IsRunning)
                ScenarioCareerMapLoader.StartBuilding(ref __0, __1, __2);
        }
    }

    [HarmonyPatch(typeof(Metagame), "OnLevelLoaded")]
    internal static class ScenarioCareerMapTerrainLoadedPatch
    {
        private static void Postfix(Level __0) => ScenarioCareerMapLoader.LoadTerrain(__0);
    }

    [HarmonyPatch(typeof(GameMode), "AllowGameToBeSaved")]
    internal static class ScenarioCareerMapNoVanillaSavePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ScenarioCareerMapLoader.IsCustomSession) __result = false;
        }
    }
    [HarmonyPatch(typeof(Level), "HandleAutosave")]
    internal static class ScenarioCareerMapNoAutosavePatch
    {
        private static bool Prefix() => !ScenarioCareerMapLoader.IsCustomSession;
    }
    [HarmonyPatch(typeof(App), "AutoSaveDeferred")]
    internal static class ScenarioCareerMapNoDeferredSavePatch
    {
        private static bool Prefix() => !ScenarioCareerMapLoader.IsCustomSession;
    }
    [HarmonyPatch(typeof(App), "QuickSaveInstantly")]
    internal static class ScenarioCareerMapNoQuickSavePatch
    {
        private static bool Prefix() => !ScenarioCareerMapLoader.IsCustomSession;
    }
    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioCareerMapResetPatch
    {
        private static void Postfix() => ScenarioCareerMapLoader.Cancel();
    }
}
