// Updated: 2026-10-24 - Cargador ingame de config.json, parcelas y decoracion Base.
// Traslado estructural desde CareerMaps.cs, sin modificaciones funcionales.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioCareerMapLoader.cs =====
// Updated: 2026-10-14 - Restaurar decoracion nativa ingame; no ocultar LandscapeItems_Base.
namespace UnderPressure.Editor
{
    internal sealed class UnderPressureFinanceConfigInstance :
        SharedInstance<FinanceManager.Config> { }

    internal sealed class UnderPressureCharacterConfigInstance :
        SharedInstance<CharacterManager.Config> { }

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
        // La escena de Unity trae copias estaticas de los objetos Base.
        // Se ocultan SOLO esas copias cuando el mapa personalizado ya
        // ha generado sus propios objetos, preservando el resto de exteriores.
        private static readonly Dictionary<Renderer, bool> OriginalLandscapeRenderers =
            new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Collider, bool> OriginalLandscapeColliders =
            new Dictionary<Collider, bool>();
        private static bool _originalLandscapeSuppressed;
        private static bool _loggedMissingBaseVisuals;
        private static LevelConfig _base;
        private static string _path;
        private static bool _active;
        private static ScenarioMapFormat.LoadedMap _map;
        private static ScenarioPlotRuntime.Collection _plotAssets;
        private static FogOfWarLevelTextureDefinition _runtimeFog;
        private static SaveSystem _registeredSaveSystem;
        private static readonly Dictionary<int, object> RegisteredSaveAssets =
            new Dictionary<int, object>();
        private static bool _worldConfigApplied;
        private static bool _sceneApplied;
        private static bool _terrainApplied;
        private static ScenarioCareerMapTerrainRetry _retry;
        private static UnderPressureFinanceConfigInstance _runtimeFinanceWrapper;
        private static UnderPressureCharacterConfigInstance _runtimeCharacterWrapper;
        internal static bool IsCustomSession => _active || _path != null;

        internal static void Prepare(LevelConfig level, string path)
        {
            if (level == null || string.IsNullOrEmpty(path)) throw new ArgumentException("Mapa incorrecto.");
            var full = Path.GetFullPath(path);
            var folder = Path.GetFullPath(ScenarioEditorStorage.Folder).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
            if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
                !ScenarioMapPackage.IsDirectPackage(full, ScenarioEditorStorage.Folder))
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
            RestoreOriginalLandscape();
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
            if (_runtimeFog != null)
            {
                if (_runtimeFog.FogOfWarTexture != null)
                    UnityEngine.Object.Destroy(_runtimeFog.FogOfWarTexture);
                UnityEngine.Object.Destroy(_runtimeFog);
            }
            _runtimeFog = null;
            if (_runtimeFinanceWrapper != null)
                UnityEngine.Object.Destroy(_runtimeFinanceWrapper);
            _runtimeFinanceWrapper = null;
            if (_runtimeCharacterWrapper != null)
                UnityEngine.Object.Destroy(_runtimeCharacterWrapper);
            _runtimeCharacterWrapper = null;
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

        internal static void ApplyScenarioSettings(ref LevelConfig config)
        {
            if (config == null || _map?.Header == null ||
                !_map.Header.scenarioSettingsConfigured) return;
            var header = _map.Header;
            var clone = (LevelConfig)CloneMethod.Invoke(config, null);
            AccessTools.Field(typeof(LevelConfig), "DisplayName")?.SetValue(
                clone, header.DisplayName ?? "");
            AccessTools.Field(typeof(LevelConfig), "DescriptionPerStar")?.SetValue(
                clone, header.DescriptionPerStar ?? new string[0]);
            clone.DisplayNameLocalised = default(LocalisedString);
            clone.DescriptionPerStarLocalised = new LocalisedString[0];
            clone.FailStateBalanceWarning = header.FailStateBalanceWarning;
            clone.FailStateBalanceGameOver = header.FailStateBalanceGameOver;

            var finance = config.GetFinanceManagerConfig();
            if (finance != null)
            {
                var financeClone = (FinanceManager.Config)CloneMethod.Invoke(finance, null);
                AccessTools.Field(typeof(FinanceManager.Config), "InitialBalance")?.SetValue(
                    financeClone, header.InitialBalance);
                _runtimeFinanceWrapper = ScriptableObject.CreateInstance<
                    UnderPressureFinanceConfigInstance>();
                _runtimeFinanceWrapper.name = "UP Scenario Finance Config";
                _runtimeFinanceWrapper.hideFlags = HideFlags.DontSave;
                _runtimeFinanceWrapper.Instance = financeClone;
                AccessTools.Field(typeof(LevelConfig), "FinanceManagerConfig")?.SetValue(
                    clone, _runtimeFinanceWrapper);
            }

            if (header.illnessesConfigured)
            {
                var characters = config.GetCharacterManagerConfig();
                if (characters != null)
                {
                    var characterClone = (CharacterManager.Config)CloneMethod.Invoke(characters, null);
                    var weighted = new List<WeightedIllness>();
                    foreach (var saved in header.illnesses ??
                             new ScenarioMapFormat.WeightedIllnessData[0])
                    {
                        var definition = ScenarioDiseaseCatalog.Resolve(config, saved.definition);
                        if (definition == null)
                        {
                            UnderPressureEditorPlugin.Log?.LogError(
                                "Carrera: enfermedad desconocida en config.json: " + saved.definition);
                            continue;
                        }
                        weighted.Add(new WeightedIllness
                        {
                            Definition = definition,
                            Deprecated = saved.deprecated,
                            Unlocked = saved.unlocked,
                            MinWeight = saved.minWeight,
                            MaxWeight = saved.maxWeight,
                            MinStarRating = saved.minStarRating,
                            MinPatientsSpawned = saved.minPatientsSpawned
                        });
                    }
                    characterClone._weightedIllnesses = weighted.ToArray();
                    _runtimeCharacterWrapper = ScriptableObject.CreateInstance<
                        UnderPressureCharacterConfigInstance>();
                    _runtimeCharacterWrapper.name = "UP Scenario Character Config";
                    _runtimeCharacterWrapper.hideFlags = HideFlags.DontSave;
                    _runtimeCharacterWrapper.Instance = characterClone;
                    AccessTools.Field(typeof(LevelConfig), "CharacterManagerConfig")?.SetValue(
                        clone, _runtimeCharacterWrapper);
                }
            }
            config = clone;
        }

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
                // RoomFloorPlanVisual no crea los VISUALES de la capa Base
                // cuando este flag es false (valor nativo de muchos hospitales).
                // La escena original los contiene precocinados para el juego base,
                // pero el mapa personalizado debe mostrar sus propias modificaciones.
                cloned.createBaseLandscapeItems = true;
                if (!string.IsNullOrEmpty(_map.Header.fogOfWarFile))
                {
                    _runtimeFog = ScenarioEditorFogOfWar.CreateDefinition(
                        _map.Header, _map.FogOfWarPng);
                    cloned.FogOfWarDefinition = _runtimeFog;
                }
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
            SuppressOriginalLandscape(level);
            if (_terrainApplied) return;
            try
            {
                var folder = _map.ContentFolder;
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
                if (_originalLandscapeSuppressed)
                {
                    if (_retry != null) UnityEngine.Object.Destroy(_retry.gameObject);
                    _retry = null;
                }
                else EnsureTerrainRetry(level);
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: terreno de config.json aplicado: " + _path);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudieron aplicar las texturas del mapa: " + ex);
            }
        }

        // NO afecta a objetos exteriores ajenos a LandscapeItems_Base
        // (edificios, arboles y otros elementos colocados directamente en Unity).
        private static void SuppressOriginalLandscape(Level level)
        {
            if (_originalLandscapeSuppressed || !_worldConfigApplied ||
                !Match(level) || ScenarioEditorFlow.IsRunning) return;

            var original = GameObject.Find("LandscapeItems_Base");
            if (original == null)
            {
                EnsureTerrainRetry(level);
                return;
            }

            var editableRenderers = new HashSet<Renderer>();
            var editableColliders = new HashSet<Collider>();
            var baseObjects = 0;
            var baseVisuals = 0;
            foreach (var plot in level.WorldState?.HospitalPlots ??
                new List<HospitalPlot>())
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (item == null || item.Layer != HospitalPlotLayer.Base) continue;
                    ++baseObjects;
                    var visual = item.Visual?.GameObject;
                    if (visual == null) continue;
                    ++baseVisuals;
                    foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                        if (renderer != null) editableRenderers.Add(renderer);
                    foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
                        if (collider != null) editableColliders.Add(collider);
                }
            }

            // Nunca dejar un mapa vacio si los visuales Base todavia no se crearon.
            // Si la parcela ha eliminado TODA la capa Base, cero objetos es valido.
            if (baseObjects > 0 && baseVisuals == 0)
            {
                if (!_loggedMissingBaseVisuals)
                {
                    _loggedMissingBaseVisuals = true;
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Carrera: esperando visuales Base del mapa antes de ocultar Unity.");
                }
                EnsureTerrainRetry(level);
                return;
            }

            var hiddenRenderers = 0;
            var hiddenColliders = 0;
            foreach (var renderer in original.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || editableRenderers.Contains(renderer)) continue;
                OriginalLandscapeRenderers.Add(renderer, renderer.enabled);
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    ++hiddenRenderers;
                }
            }
            foreach (var collider in original.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || editableColliders.Contains(collider)) continue;
                OriginalLandscapeColliders.Add(collider, collider.enabled);
                if (collider.enabled)
                {
                    collider.enabled = false;
                    ++hiddenColliders;
                }
            }

            _originalLandscapeSuppressed = true;
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Carrera: Base del mapa visible (" + baseVisuals + "/" + baseObjects +
                " objetos). Escena original LandscapeItems_Base oculta: " +
                hiddenRenderers + " renderizadores y " + hiddenColliders +
                " colliders. Resto del escenario Unity conservado.");
            if (_terrainApplied && _retry != null)
            {
                UnityEngine.Object.Destroy(_retry.gameObject);
                _retry = null;
            }
        }

        private static void RestoreOriginalLandscape()
        {
            foreach (var pair in OriginalLandscapeRenderers)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in OriginalLandscapeColliders)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            OriginalLandscapeRenderers.Clear();
            OriginalLandscapeColliders.Clear();
            _originalLandscapeSuppressed = false;
            _loggedMissingBaseVisuals = false;
        }

        private static void EnsureTerrainRetry(Level level)
        {
            if (_retry != null) return;
            var gameObject = new GameObject("UP Career Terrain Loader");
            _retry = gameObject.AddComponent<ScenarioCareerMapTerrainRetry>();
            _retry.Initialise(level);
        }

        internal static bool IsTerrainApplied => _terrainApplied;
        internal static bool IsOriginalLandscapeSuppressed => _originalLandscapeSuppressed;

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
            if (!ScenarioCareerMapLoader.IsCustomSession || _level == null ||
                (ScenarioCareerMapLoader.IsTerrainApplied &&
                 ScenarioCareerMapLoader.IsOriginalLandscapeSuppressed))
            {
                Destroy(gameObject);
                return;
            }
            if (++_attempts > 300)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Carrera: no se pudo completar la carga del terreno o la decoracion Base tras 300 fotogramas.");
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
        private static void Prefix(App __instance, ref LevelConfig __0,
            ref SaveFileHeader __1, ref bool __2)
        {
            if (!ScenarioCareerMapLoader.IsCustomSession || ScenarioEditorFlow.IsRunning) return;
            if (!ScenarioCareerMapLoader.MatchesConfig(__0))
            {
                ScenarioCareerMapLoader.Cancel();
                return;
            }
            ScenarioCareerMapLoader.StartPreparingForLoad(__instance?.Metagame);
            ScenarioCareerMapLoader.ApplyScenarioSettings(ref __0);
            // GetSaveForLevel(baseId) devuelve tambien partidas del hospital
            // ORIGINAL. Esas partidas sustituyen las parcelas de config.json al
            // deserializarse. Hasta que haya identificacion propia del mapa
            // no se admite recuperarlas como si fueran de este escenario.
            __1 = null;
            __2 = true;
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Carrera: iniciando config.json como escenario nuevo, sin save del hospital base.");
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
// ===== END ORIGINAL: ScenarioCareerMapLoader.cs =====

// ===== BEGIN ORIGINAL: ScenarioPlotRuntime.cs =====
// Updated: 2026-10-13
// Convierte los JSON/PNG en parcelas nativas; resuelve cada objeto por GUID + DebugTag.
// 2026-10-13: resuelve duplicados sin abortar; prefab opcional mejora precision.
namespace UnderPressure.Editor
{
    internal sealed class UnderPressurePlotInstance : SharedInstance<HospitalPlotDefinition> { }

    internal static class ScenarioPlotRuntime
    {
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BaseField = AccessTools.Field(typeof(HospitalPlotDefinition), "Items");
        private static readonly FieldInfo BuiltField = AccessTools.Field(typeof(HospitalPlotDefinition), "BuiltItems");
        private static readonly FieldInfo UnbuiltField = AccessTools.Field(typeof(HospitalPlotDefinition), "UnbuiltItems");

        internal sealed class Collection
        {
            internal readonly List<HospitalPlotDefinition> Definitions = new List<HospitalPlotDefinition>();
            internal readonly List<UnderPressurePlotInstance> Wrappers = new List<UnderPressurePlotInstance>();
            internal readonly List<Texture2D> Textures = new List<Texture2D>();
            internal void DestroyAssets()
            {
                foreach (var wrapper in Wrappers)
                    if (wrapper != null) UnityEngine.Object.Destroy(wrapper);
                foreach (var texture in Textures)
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                Wrappers.Clear();
                Textures.Clear();
            }
        }

        internal static Collection Build(ScenarioMapFormat.LoadedMap map,
            IList<SharedInstance<HospitalPlotDefinition>> templates, Metagame metagame)
        {
            if (templates == null || templates.Count == 0 || BaseField == null || BuiltField == null || UnbuiltField == null)
                throw new InvalidOperationException("El hospital no tiene definiciones originales para crear parcelas.");
            var defs = CreateDefinitionIndex(templates, metagame);
            var result = new Collection();
            try
            {
                int canvasWidth = -1, canvasHeight = -1;
                for (var index = 0; index < map.Plots.Count; ++index)
                {
                    var entry = map.Plots[index];
                    var model = entry.Data;
                    var source = templates[Math.Min(index, templates.Count - 1)]?.Instance;
                    if (source == null)
                        throw new InvalidDataException("No se encontro plantilla para la parcela " + index);
                    var texture = ScenarioMapFormat.DecodeTexture(entry.ImagePng, "UP_Plot_" + index);
                    result.Textures.Add(texture);
                    if (canvasWidth == -1)
                    {
                        canvasWidth = texture.width;
                        canvasHeight = texture.height;
                    }
                    else if (texture.width != canvasWidth || texture.height != canvasHeight)
                        throw new InvalidDataException("Todos los PNG deben usar el mismo lienzo del hospital.");

                    var definition = (HospitalPlotDefinition)CloneMethod.Invoke(source, null);
                    definition.FloorImage = texture;
                    definition.Cost = model.cost;
                    definition.Color = model.color;
                    definition.InitiallyOpen = model.initiallyOpen;
                    definition.Built = model.built;
                    definition.InstaBuild = model.instaBuild;
                    definition.HideUntilAvailable = model.hideUntilAvailable;
                    definition.NotHiddenInSandbox = model.notHiddenInSandbox;
                    definition.IncludePerimiterInNavMesh = model.includePerimiterInNavMesh;
                    definition.UseEnergyUI = model.useEnergyUI;
                    definition.EnergyUnitsGenerated = model.energyUnitsGenerated;
                    definition.TimeToBuild = model.timeToBuild;
                    definition.FootprintYOffset = model.footprintYOffset;
                    definition.AmbulanceBayEntranceSide = (GridDirection)model.ambulanceBayEntranceSide;
                    definition.BuildObjectiveAutoStart = model.buildObjectiveAutoStart;
                    definition.BuildObjectiveStartOnPrereqsMet = model.buildObjectiveStartOnPrereqsMet;
                    if (!string.IsNullOrEmpty(model.nameTerm))
                        definition.NameLocalised = new TH20.LocalisedString(model.nameTerm);
                    // El campo MergeInto se resuelve una vez creadas todas las referencias.
                    definition.MergeInto = null;
                    if (index >= templates.Count)
                    {
                        definition.Prerequisites = new List<HospitalPlotPrerequisite>();
                        definition.BuildObjective = null;
                    }
                    BaseField.SetValue(definition, ConvertItems(model.baseItems, defs));
                    BuiltField.SetValue(definition, ConvertItems(model.builtItems, defs));
                    UnbuiltField.SetValue(definition, ConvertItems(model.unbuiltItems, defs));
                    var wrapper = ScriptableObject.CreateInstance<UnderPressurePlotInstance>();
                    wrapper.name = "UP_Plot_" + index;
                    wrapper.hideFlags = HideFlags.DontSave;
                    wrapper.Instance = definition;
                    result.Definitions.Add(definition);
                    result.Wrappers.Add(wrapper);
                }
                var targetsField = AccessTools.Field(typeof(HospitalPlotPrerequisitePlotBought), "_plot");
                for (var i = 0; i < result.Definitions.Count; ++i)
                {
                    var config = map.Plots[i].Data;
                    var merged = config.mergeInto;
                    if (merged < -1 || merged >= result.Wrappers.Count || merged == i)
                        throw new InvalidDataException("Referencia MergeInto invalida en parcela " + i);
                    if (merged >= 0) result.Definitions[i].MergeInto = result.Wrappers[merged];
                    var prereqs = new List<HospitalPlotPrerequisite>();
                    if (config.prerequisites != null)
                        foreach (var entry in config.prerequisites)
                        {
                            if (entry?.anyBought == null || entry.anyBought.Length == 0 || targetsField == null)
                                throw new InvalidDataException("Requisito de parcela incompleto: " + i);
                            var alternatives = new SharedInstance<HospitalPlotDefinition>[entry.anyBought.Length];
                            for (var j = 0; j < alternatives.Length; ++j)
                            {
                                var target = entry.anyBought[j];
                                if (target < 0 || target >= result.Wrappers.Count || target == i)
                                    throw new InvalidDataException("Indice de requisito invalido: " + target);
                                alternatives[j] = result.Wrappers[target];
                            }
                            var prerequisite = new HospitalPlotPrerequisitePlotBought();
                            targetsField.SetValue(prerequisite, alternatives);
                            prereqs.Add(prerequisite);
                        }
                    result.Definitions[i].Prerequisites = prereqs;
                }
                return result;
            }
            catch
            {
                result.DestroyAssets();
                throw;
            }
        }

        private static List<HospitalPlotItem> ConvertItems(ScenarioMapFormat.Item[] items,
            DefinitionIndex definitions)
        {
            var result = new List<HospitalPlotItem>();
            if (items == null) return result;
            foreach (var item in items)
            {
                if (item == null)
                    throw new InvalidDataException("Hay un objeto nulo en los datos de la parcela.");
                var wrapper = definitions.Resolve(item);
                result.Add(new HospitalPlotItem
                {
                    Definition = wrapper,
                    Position = new Vector3(item.x, item.y, item.z),
                    Rotation = item.rotation
                });
            }
            return result;
        }

        // Un GUID NO identifica un modelo de forma univoca en TH20: multiples
        // objetos comparten GUID, y algunos DebugTag aparecen con GUID distintos.
        // Por tanto solo la pareja (GUID, DebugTag) identifica de forma segura
        // una definicion. Los indices secundarios son para mapas sin pareja exacta
        // (p. ej. migraciones entre versiones) y solo si existe UN candidato.
        private sealed class DefinitionIndex
        {
            private readonly Dictionary<string, List<SharedInstance<RoomItemDefinition>>> _exact =
                new Dictionary<string, List<SharedInstance<RoomItemDefinition>>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<SharedInstance<RoomItemDefinition>>> _tags =
                new Dictionary<string, List<SharedInstance<RoomItemDefinition>>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<SharedInstance<RoomItemDefinition>>> _guids =
                new Dictionary<string, List<SharedInstance<RoomItemDefinition>>>(StringComparer.OrdinalIgnoreCase);

            // Posiciones originales de los objetos del hospital base. En los JSON
            // antiguos no existe el prefab, pero la posicion de la mayoria de los
            // objetos permite distinguir variantes con GUID y nombre repetidos.
            private sealed class Placement
            {
                internal SharedInstance<RoomItemDefinition> Wrapper;
                internal Vector3 Position;
            }
            private readonly Dictionary<string, List<Placement>> _placements =
                new Dictionary<string, List<Placement>>(StringComparer.OrdinalIgnoreCase);

            internal void AddPlacement(HospitalPlotItem item)
            {
                var wrapper = item?.Definition;
                var definition = wrapper?.Instance;
                if (definition == null) return;
                var guid = definition.GUID == Guid.Empty ? "" : definition.GUID.ToString("D");
                var tag = Normalize(definition.DebugTag);
                if (guid.Length == 0 || tag.Length == 0) return;
                var key = Pair(guid, tag);
                List<Placement> list;
                if (!_placements.TryGetValue(key, out list))
                {
                    list = new List<Placement>();
                    _placements.Add(key, list);
                }
                list.Add(new Placement { Wrapper = wrapper, Position = item.Position });
            }

            private static string Normalize(string value) => (value ?? "").Trim();
            private static string Pair(string guid, string tag) => guid + "|" + tag;

            internal void Add(SharedInstance<RoomItemDefinition> wrapper)
            {
                var definition = wrapper?.Instance;
                if (definition == null) return;
                var guid = definition.GUID == Guid.Empty ? "" : definition.GUID.ToString("D");
                var tag = Normalize(definition.DebugTag);
                if (guid.Length != 0 && tag.Length != 0)
                    Append(_exact, Pair(guid, tag), wrapper);
                if (guid.Length != 0) Append(_guids, guid, wrapper);
                if (tag.Length != 0) Append(_tags, tag, wrapper);
            }

            private static void Append(
                Dictionary<string, List<SharedInstance<RoomItemDefinition>>> index,
                string key, SharedInstance<RoomItemDefinition> wrapper)
            {
                List<SharedInstance<RoomItemDefinition>> list;
                if (!index.TryGetValue(key, out list))
                {
                    list = new List<SharedInstance<RoomItemDefinition>>();
                    index.Add(key, list);
                }
                foreach (var found in list)
                    if (ReferenceEquals(found?.Instance, wrapper.Instance)) return;
                list.Add(wrapper);
            }

            // En el juego puede haber varias instancias de RoomItemDefinition que
            // comparten GUID y DebugTag, especialmente entre DLC y plot templates.
            // Lanzar una excepcion por ello impedia cargar TODOS los mapas anteriores.
            // Para mapas nuevos, el prefab resuelve esa colision. Para los anteriores
            // elegimos un candidato nativo estable, priorizando el indice original.
            private readonly HashSet<string> _reported =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private SharedInstance<RoomItemDefinition> Choose(
                Dictionary<string, List<SharedInstance<RoomItemDefinition>>> index,
                string key, string prefab, string label, Vector3 position)
            {
                List<SharedInstance<RoomItemDefinition>> candidates;
                if (!index.TryGetValue(key, out candidates) || candidates.Count == 0)
                    return null;
                if (candidates.Count == 1) return candidates[0];

                var target = Normalize(prefab);
                if (target.Length > 0)
                {
                    // El nombre del prefab procede de la definicion nativa al guardar.
                    // Incluso si varios wrappers tienen el mismo prefab, son
                    // visualmente el mismo modelo y la primera definicion es estable.
                    foreach (var wrapper in candidates)
                    {
                        var definition = wrapper?.Instance;
                        if (definition == null) continue;
                        try
                        {
                            var source = definition.GetPrefab(0);
                            if (source != null && string.Equals(Normalize(source.name), target,
                                    StringComparison.OrdinalIgnoreCase))
                                return wrapper;
                        }
                        catch (Exception) { }
                    }
                    if (_reported.Add("mismatch:" + key + ":" + target))
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Prefab no localizado; se utilizara la definicion nativa de " + label +
                            ": " + target);
                }

                // En mapas anteriores al campo prefab, usar las posiciones
                // originales del hospital base cuando haya varias definiciones.
                // Solo se consideran las definiciones que realmente coinciden
                // con esta clave exacta (GUID + DebugTag).
                if (ReferenceEquals(index, _exact))
                {
                    List<Placement> matches;
                    if (_placements.TryGetValue(key, out matches) && matches.Count > 0)
                    {
                        Placement closest = null;
                        var distance = float.MaxValue;
                        foreach (var placement in matches)
                        {
                            if (placement.Wrapper?.Instance == null) continue;
                            var d = (placement.Position - position).sqrMagnitude;
                            if (d < distance)
                            {
                                distance = d;
                                closest = placement;
                            }
                        }
                        if (closest != null)
                        {
                            // La posicion identica de un objeto original es un
                            // indicador mas preciso que el orden de los wrappers.
                            return closest.Wrapper;
                        }
                    }
                }

                // No descartar 1.740 objetos de un mapa antiguo por colisiones
                // en el indice. Orden de insercion: parcelas base, paisaje, sala.
                // Es preferible a crear modelos aleatorios por GUID exclusivamente.
                if (_reported.Add("duplicate:" + key))
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Definiciones duplicadas para " + label + ": " + candidates.Count +
                        ". Se usara la primera coincidencia GUID + DebugTag.");
                return candidates[0];
            }

            internal SharedInstance<RoomItemDefinition> Resolve(ScenarioMapFormat.Item item)
            {
                var guid = Normalize(item.guid);
                var tag = Normalize(item.tag);
                var prefab = Normalize(item.prefab);
                if (string.Equals(guid, Guid.Empty.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    guid = "";
                if (guid.Length == 0 && tag.Length == 0)
                    throw new InvalidDataException("Objeto sin GUID ni DebugTag en el JSON.");

                if (guid.Length > 0 && tag.Length > 0)
                {
                    var exact = Choose(_exact, Pair(guid, tag), prefab, tag + " / " + guid,
                        new Vector3(item.x, item.y, item.z));
                    if (exact != null) return exact;
                }
                // Nunca seleccionar por GUID ignorando un DebugTag diferente.
                if (tag.Length > 0)
                {
                    var byTag = Choose(_tags, tag, prefab, "DebugTag " + tag,
                        new Vector3(item.x, item.y, item.z));
                    if (byTag != null) return byTag;
                    throw new InvalidDataException("No existe el objeto " + tag + " / " + guid +
                        " en las definiciones del juego.");
                }
                var byGuid = Choose(_guids, guid, prefab, "GUID " + guid,
                    new Vector3(item.x, item.y, item.z));
                if (byGuid != null) return byGuid;
                throw new InvalidDataException("No existe el objeto GUID " + guid +
                    " en las definiciones del juego.");
            }
        }

        private static DefinitionIndex CreateDefinitionIndex(
            IList<SharedInstance<HospitalPlotDefinition>> templates, Metagame metagame)
        {
            var index = new DefinitionIndex();
            foreach (var template in templates)
            {
                var plot = template?.Instance;
                if (plot == null) continue;
                foreach (HospitalPlotLayer layer in Enum.GetValues(typeof(HospitalPlotLayer)))
                {
                    var items = plot.GetItems(layer);
                    if (items == null) continue;
                    foreach (var item in items)
                    {
                        index.Add(item?.Definition);
                        index.AddPlacement(item);
                    }
                }
            }
            var landscape = metagame?.LandscapeItemDatabase?.Instance?.RoomItems;
            if (landscape != null) foreach (var item in landscape) index.Add(item);
            var regular = metagame?.RoomItemDatabase?.Instance?.RoomItems;
            if (regular != null) foreach (var item in regular) index.Add(item);
            return index;
        }
    }
}
// ===== END ORIGINAL: ScenarioPlotRuntime.cs =====
