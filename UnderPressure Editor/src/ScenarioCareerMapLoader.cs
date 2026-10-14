// Updated: 2026-10-14 - Restaurar decoracion nativa ingame; no ocultar LandscapeItems_Base.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using FullInspector;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioCareerMapLoader
    {
        private const int PlotSaveIdBase = 9113000;
        private static FieldInfo AssetIdsField =>
            AccessTools.Field(typeof(SaveSystem), "_assetIDs");
        private static readonly string[] SerializerFields =
            { "_serializerLevel", "_serializerMetagame", "_serializerRoomTemplates" };
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly List<Texture2D> TemporaryTextures = new List<Texture2D>();
        private static readonly List<Material> TemporaryMaterials = new List<Material>();
        private static readonly Dictionary<Renderer, Material[]> OriginalRendererMaterials =
            new Dictionary<Renderer, Material[]>();
        private static readonly Dictionary<MeshFilter, Mesh> OriginalRendererMeshes =
            new Dictionary<MeshFilter, Mesh>();
        private static readonly Dictionary<MeshCollider, Mesh> OriginalColliderMeshes =
            new Dictionary<MeshCollider, Mesh>();
        private static readonly List<Mesh> TemporaryMeshes = new List<Mesh>();
        private static LevelConfig _base;
        private static string _path;
        private static bool _active;
        private static ScenarioMapFormat.LoadedMap _map;
        private static ScenarioPlotRuntime.Collection _plotAssets;
        private static SaveSystem _registeredSaveSystem;
        private static readonly Dictionary<int, object> RegisteredSaveAssets =
            new Dictionary<int, object>();
        private static bool _worldConfigApplied;
        private static bool _sceneApplied;
        private static bool _terrainApplied;
        private static ScenarioCareerMapTerrainRetry _retry;
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
            if (_retry != null) UnityEngine.Object.Destroy(_retry.gameObject);
            _retry = null;
            _sceneApplied = false;
            _terrainApplied = false;
            RestoreTerrainMeshes();
            foreach (var pair in OriginalRendererMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            OriginalRendererMaterials.Clear();
            foreach (var material in TemporaryMaterials)
                if (material != null) UnityEngine.Object.Destroy(material);
            TemporaryMaterials.Clear();
            foreach (var texture in TemporaryTextures)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            TemporaryTextures.Clear();
            ScenarioEditorScene.Clear();
            UnregisterSaveAssets();
            _plotAssets?.DestroyAssets();
            _plotAssets = null;
            _base = null;
            _path = null;
            _map = null;
            _active = false;
            _worldConfigApplied = false;
        }

        internal static bool MatchesConfig(LevelConfig config) =>
            config != null && _base != null &&
            string.Equals(config.UniqueId, _base.UniqueId, StringComparison.Ordinal);

        internal static bool Match(Level level) =>
            level != null && level.Config != null && MatchesConfig(level.Config);

        internal static bool TryGetNativeSave(
            SaveSystem saveSystem, string levelId, out SaveFileHeader result)
        {
            result = null;
            if (saveSystem == null || string.IsNullOrEmpty(levelId)) return false;
            result = saveSystem.GetSaveForLevel(levelId, false);
            return result != null;
        }

        internal static bool TryGetCurrentMapSave(
            SaveSystem saveSystem, string levelId, out SaveFileHeader result) =>
            TryGetNativeSave(saveSystem, levelId, out result);

        internal static void StartPreparingForLoad(Metagame metagame)
        {
            EnsureRuntimeAssets(metagame);
            _active = true;
        }

        private static void EnsureRuntimeAssets(Metagame metagame)
        {
            if (_plotAssets != null) return;
            var templates = _base?.GetWorldStateConfig()?.HospitalPlots;
            if (templates == null || metagame == null || _map == null)
                throw new InvalidOperationException(
                    "No estan disponibles las definiciones necesarias para restaurar el mapa.");
            var prepared = ScenarioPlotRuntime.Build(_map, templates, metagame);
            _plotAssets = prepared;
            try
            {
                RegisterSaveAssets(metagame.App?.SaveSystem);
            }
            catch
            {
                UnregisterSaveAssets();
                prepared.DestroyAssets();
                _plotAssets = null;
                throw;
            }
        }

        private static void RegisterSaveAssets(SaveSystem saveSystem)
        {
            if (saveSystem == null || _plotAssets == null)
                throw new InvalidOperationException("No esta disponible el mapa de IDs del guardado.");
            var mapping = AssetIdsField?.GetValue(saveSystem) as BiDictionary<int, object>;
            if (mapping == null)
                throw new InvalidOperationException("No esta disponible el mapa de IDs del guardado.");
            _registeredSaveSystem = saveSystem;
            for (var index = 0; index < _plotAssets.Wrappers.Count; ++index)
            {
                var definitionId = PlotSaveIdBase + index * 2;
                var wrapperId = definitionId + 1;
                var wrapper = _plotAssets.Wrappers[index];
                var definition = _plotAssets.Definitions[index];
                wrapper.ID = wrapperId;
                RegisterSaveObject(mapping, definitionId, definition);
                RegisterSaveObject(mapping, wrapperId, wrapper);
            }
            RefreshSerializerMappings(saveSystem, mapping);
        }

        private static void RegisterSaveObject(BiDictionary<int, object> mapping, int id, object value)
        {
            if (mapping.TryGetValue(id, out var existing))
            {
                if (ReferenceEquals(existing, value)) return;
                throw new InvalidOperationException("El ID externo de parcela ya esta ocupado: " + id);
            }
            mapping.Add(id, value);
            RegisteredSaveAssets.Add(id, value);
        }

        private static void UnregisterSaveAssets()
        {
            var saveSystem = _registeredSaveSystem;
            var mapping = saveSystem == null
                ? null
                : AssetIdsField?.GetValue(saveSystem) as BiDictionary<int, object>;
            if (mapping != null)
            {
                foreach (var pair in RegisteredSaveAssets)
                    if (mapping.TryGetValue(pair.Key, out var current) &&
                        ReferenceEquals(current, pair.Value))
                        mapping.Remove(pair.Key);
                RefreshSerializerMappings(saveSystem, mapping);
            }
            RegisteredSaveAssets.Clear();
            _registeredSaveSystem = null;
        }

        private static void RefreshSerializerMappings(
            SaveSystem saveSystem, BiDictionary<int, object> mapping)
        {
            if (saveSystem == null || mapping == null) return;
            foreach (var fieldName in SerializerFields)
            {
                var serializer = AccessTools.Field(typeof(SaveSystem), fieldName)
                    ?.GetValue(saveSystem) as FullSerializerSave.fsSerializer;
                serializer?.SetIDObjectMapping(mapping.FirstToSecond, mapping.SecondToFirst);
            }
        }

        // Insertar la lista de parcelas externas antes de construir WorldState.
        internal static void StartBuilding(ref WorldState.Config config, Level level, Metagame metagame)
        {
            if (!Match(level) || _map == null || config?.HospitalPlots == null) return;
            if (_worldConfigApplied) return;
            EnsureRuntimeAssets(metagame);
            try
            {
                var cloned = (WorldState.Config)CloneMethod.Invoke(config, null);
                cloned.HospitalPlots = _plotAssets.Wrappers
                    .ConvertAll<FullInspector.SharedInstance<HospitalPlotDefinition>>(item => item);
                ScenarioHospitalStyles.Apply(level, _map.Header, cloned, _plotAssets.Definitions);
                config = cloned;
                _active = true;
                _worldConfigApplied = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: WorldState creado con " + cloned.HospitalPlots.Count + " parcelas personalizadas.");
            }
            catch
            {
                Cancel();
                throw;
            }
        }

        internal static void LoadTerrain(Level level)
        {
            if (!_active || !Match(level) || _path == null) return;
            // No instanciar decoracion global experimental: podria restaurar
            // objetos eliminados o duplicarlos encima de parcelas editables.
            // scene.json anterior no se elimina del disco.
            if (!_sceneApplied) _sceneApplied = true;
            if (_terrainApplied) return;
            try
            {
                var folder = Path.GetDirectoryName(_path);
                var roots = UnityEngine.Object.FindObjectsOfType<Transform>();
                var root = roots.FirstOrDefault(t => t != null &&
                    t.name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase));
                if (root == null)
                {
                    // OnLevelLoaded puede dispararse antes de que Terrain_ exista.
                    // Reintentar durante la inicializacion, sin duplicar objetos.
                    EnsureTerrainRetry(level);
                    return;
                }
                // EXACTAMENTE el mismo filtro y orden que usa el pintor al
                // asignar indices a los Pass (alpha_N.png y terrain_mesh.bin).
                var renderers = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r != null &&
                        r.gameObject.name.StartsWith("Pass", StringComparison.OrdinalIgnoreCase) &&
                        r.sharedMaterials != null && r.sharedMaterials.Length != 0 &&
                        r.sharedMaterials[0] != null)
                    .ToList();
                if (renderers.Count == 0)
                {
                    EnsureTerrainRetry(level);
                    return;
                }
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
                // Los PNG solo alteran las texturas; la geometria se restaura
                // por separado desde terrain_mesh.bin tras cargar el nivel.
                ApplyTerrainMeshes(folder, renderers);
                _terrainApplied = true;
                if (_retry != null) UnityEngine.Object.Destroy(_retry.gameObject);
                _retry = null;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: decoracion y terreno .upmap aplicados: " + _path);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudieron aplicar las texturas del mapa: " + ex);
            }
        }

        private static void EnsureTerrainRetry(Level level)
        {
            if (_retry != null) return;
            var gameObject = new GameObject("UP Career Terrain Loader");
            _retry = gameObject.AddComponent<ScenarioCareerMapTerrainRetry>();
            _retry.Initialise(level);
        }

        internal static bool IsTerrainApplied => _terrainApplied;

        private static void RestoreTerrainMeshes()
        {
            // Nunca dejar las mallas clonadas en otros hospitales o sesiones.
            foreach (var pair in OriginalColliderMeshes)
                if (pair.Key != null) pair.Key.sharedMesh = pair.Value;
            OriginalColliderMeshes.Clear();
            foreach (var pair in OriginalRendererMeshes)
                if (pair.Key != null) pair.Key.sharedMesh = pair.Value;
            OriginalRendererMeshes.Clear();
            foreach (var mesh in TemporaryMeshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            TemporaryMeshes.Clear();
        }

        private static void ApplyTerrainMeshes(string folder, IList<Renderer> passes)
        {
            var path = Path.Combine(folder, "terrain_mesh.bin");
            if (!File.Exists(path)) return;
            // No aplicar parcialmente un sidecar danado ni modificar los assets
            // compartidos del juego. El formato UPM1 guarda Y local por pass.
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != 0x55504D31)
                        throw new InvalidDataException("terrain_mesh.bin: firma UPM1 no valida.");
                    var total = reader.ReadInt32();
                    if (total < 0 || total > passes.Count)
                        throw new InvalidDataException("terrain_mesh.bin: numero de pases invalido.");
                    var seen = new HashSet<int>();
                    var applied = 0;
                    for (var entryIndex = 0; entryIndex < total; ++entryIndex)
                    {
                        var passIndex = reader.ReadInt32();
                        var vertexCount = reader.ReadInt32();
                        if (passIndex < 0 || passIndex >= passes.Count || !seen.Add(passIndex) ||
                            vertexCount < 0 || vertexCount > 2000000)
                            throw new InvalidDataException("terrain_mesh.bin: indice o vertices invalidos.");
                        var renderer = passes[passIndex];
                        var filter = renderer.GetComponent<MeshFilter>();
                        var source = filter == null ? null : filter.sharedMesh;
                        if (source == null || !source.isReadable || source.vertexCount != vertexCount)
                            throw new InvalidDataException("terrain_mesh.bin: malla incompatible en pase " + passIndex);
                        // Leer todas las alturas antes de cambiar el filtro.
                        var heights = new float[vertexCount];
                        for (var v = 0; v < vertexCount; ++v)
                        {
                            var y = reader.ReadSingle();
                            if (float.IsNaN(y) || float.IsInfinity(y))
                                throw new InvalidDataException("terrain_mesh.bin: altura no valida.");
                            heights[v] = y;
                        }
                        if (OriginalRendererMeshes.ContainsKey(filter))
                            throw new InvalidOperationException("El terreno personalizado ya fue aplicado.");
                        var cloned = UnityEngine.Object.Instantiate(source);
                        cloned.name = source.name + "_UP_PLAYABLE_TERRAIN";
                        var vertices = cloned.vertices;
                        for (var v = 0; v < vertices.Length; ++v)
                        {
                            var position = vertices[v];
                            position.y = heights[v];
                            vertices[v] = position;
                        }
                        cloned.vertices = vertices;
                        cloned.RecalculateBounds();
                        cloned.RecalculateNormals();
                        OriginalRendererMeshes.Add(filter, source);
                        TemporaryMeshes.Add(cloned);
                        filter.sharedMesh = cloned;
                        var collider = filter.GetComponent<MeshCollider>();
                        if (collider != null && collider.sharedMesh == source)
                        {
                            OriginalColliderMeshes.Add(collider, collider.sharedMesh);
                            collider.sharedMesh = null;
                            collider.sharedMesh = cloned;
                        }
                        applied++;
                    }
                    if (stream.Position != stream.Length)
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "terrain_mesh.bin contiene datos adicionales tras las mallas.");
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Carrera: terrain_mesh.bin aplicado a " + applied + " mallas del mapa.");
                }
            }
            catch (Exception ex)
            {
                RestoreTerrainMeshes();
                UnderPressureEditorPlugin.Log?.LogError(
                    "Carrera: no se pudo aplicar terrain_mesh.bin; se recuperaron las mallas originales: " + ex);
            }
        }
    }

    // Correccion diferida sin archivos ni servicios adicionales.
    internal sealed class ScenarioCareerMapTerrainRetry : MonoBehaviour
    {
        private Level _level;
        private int _attempts;

        internal void Initialise(Level level) => _level = level;

        private void Update()
        {
            if (!ScenarioCareerMapLoader.IsCustomSession ||
                ScenarioCareerMapLoader.IsTerrainApplied || _level == null)
            {
                Destroy(gameObject);
                return;
            }
            if (++_attempts > 300)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Carrera: terreno personalizado no disponible tras 300 fotogramas.");
                Destroy(gameObject);
                return;
            }
            ScenarioCareerMapLoader.LoadTerrain(_level);
        }
    }

    [HarmonyPatch(typeof(App), "LoadLevel", new[] {
        typeof(LevelConfig), typeof(SaveFileHeader), typeof(bool), typeof(bool) })]
    internal static class ScenarioCareerMapIgnoreSavePatch
    {
        private static void Prefix(App __instance, LevelConfig __0,
            ref SaveFileHeader __1, ref bool __2)
        {
            if (!ScenarioCareerMapLoader.IsCustomSession || ScenarioEditorFlow.IsRunning) return;
            if (!ScenarioCareerMapLoader.MatchesConfig(__0))
            {
                ScenarioCareerMapLoader.Cancel();
                return;
            }
            ScenarioCareerMapLoader.StartPreparingForLoad(__instance?.Metagame);
            // GetSaveForLevel(baseId) devuelve tambien partidas del hospital
            // ORIGINAL. Esas partidas sustituyen las parcelas del .upmap al
            // deserializarse. Hasta que haya identificacion propia del mapa
            // no se admite recuperarlas como si fueran de este escenario.
            __1 = null;
            __2 = true;
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Carrera: iniciando el .upmap como escenario nuevo, sin save del hospital base.");
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

    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioCareerMapResetPatch
    {
        private static void Postfix() => ScenarioCareerMapLoader.Cancel();
    }
}