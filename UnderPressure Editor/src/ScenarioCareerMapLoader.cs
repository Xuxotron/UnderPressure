// Updated: 2026-10-13 - Guardado nativo independiente y recarga de mapas personalizados.
// Carga .upmap + .json/.png tanto al crear como al restaurar una partida.
// Cada .sav personalizado conserva el vinculo con su mapa sin sustituir la partida del hospital base.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioCareerMapLoader
    {
        private const string SaveLinkSignature = "UNDERPRESSURE_CUSTOM_MAP_SAVE_1";
        private const string SaveLinkExtension = ".upmapref";
        // Rango reservado para las parcelas de un unico mapa activo. Cada parcela
        // usa un ID para la definicion y el siguiente para su SharedInstance.
        private const int PlotSaveIdBase = 9113000;
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo AssetIdsField =
            AccessTools.Field(typeof(SaveSystem), "_assetIDs");
        private static readonly MethodInfo SaveLocationForSlotMethod =
            AccessTools.Method(typeof(SaveSystem), "SaveLocationForSlot");
        private static readonly MethodInfo SanitiseSaveNameMethod =
            AccessTools.Method(typeof(SaveSystem), "SanitiseFileNameCharacters");
        private static readonly string[] SerializerFields =
            { "_serializerLevel", "_serializerMetagame", "_serializerRoomTemplates" };
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

        internal static bool MatchesLevelId(string levelId) =>
            _base != null && string.Equals(levelId, _base.UniqueId, StringComparison.Ordinal);

        internal static bool Match(Level level) =>
            level != null && level.Config != null && MatchesConfig(level.Config);

        internal static bool TryGetSaveForMap(
            SaveSystem saveSystem, string levelId, string mapPath, out SaveFileHeader result)
        {
            result = null;
            if (saveSystem == null || string.IsNullOrEmpty(levelId) || string.IsNullOrEmpty(mapPath))
                return false;
            var fullMapPath = Path.GetFullPath(mapPath);
            var saves = saveSystem.SaveFiles;
            if (saves == null) return false;
            foreach (var header in saves)
            {
                if (header == null || header.IsBroken ||
                    !string.Equals(header.LevelID, levelId, StringComparison.Ordinal) ||
                    !TryReadSaveLink(header.FilePath, out var linkedPath) ||
                    !string.Equals(linkedPath, fullMapPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (result == null || header.Date > result.Date) result = header;
            }
            return result != null;
        }

        internal static bool TryGetCurrentMapSave(
            SaveSystem saveSystem, string levelId, out SaveFileHeader result) =>
            TryGetSaveForMap(saveSystem, levelId, _path, out result);

        internal static void StartPreparingForLoad(Metagame metagame)
        {
            EnsureRuntimeAssets(metagame);
            _active = true;
        }

        internal static bool TryGetVanillaSave(
            SaveSystem saveSystem, string levelId, bool includeBroken, out SaveFileHeader result)
        {
            result = null;
            if (saveSystem == null || string.IsNullOrEmpty(levelId)) return false;
            var saves = saveSystem.SaveFiles;
            if (saves == null) return false;
            foreach (var header in saves)
            {
                if (header == null || (!includeBroken && header.IsBroken) ||
                    !string.Equals(header.LevelID, levelId, StringComparison.Ordinal) ||
                    IsCustomSave(header))
                    continue;
                if (result == null || header.Date > result.Date) result = header;
            }
            return result != null;
        }

        internal static bool TryPrepareLinkedSave(LevelConfig level, SaveFileHeader header)
        {
            if (level == null || header == null || !TryReadSaveLink(header.FilePath, out var mapPath))
                return false;
            Prepare(level, mapPath);
            return true;
        }

        private static bool IsCustomSave(SaveFileHeader header) =>
            header != null && TryReadSaveLink(header.FilePath, out _);

        private static bool TryReadSaveLink(string savePath, out string mapPath)
        {
            mapPath = null;
            if (string.IsNullOrEmpty(savePath)) return false;
            var linkPath = savePath + SaveLinkExtension;
            if (!File.Exists(linkPath)) return false;
            try
            {
                var lines = File.ReadAllLines(linkPath, Encoding.UTF8);
                if (lines.Length != 2 || !string.Equals(lines[0], SaveLinkSignature,
                        StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[1]))
                    return false;
                var root = Path.GetFullPath(ScenarioEditorStorage.Folder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(root, lines[1]));
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    !candidate.EndsWith(".upmap", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(candidate))
                    return false;
                mapPath = candidate;
                return true;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "No se pudo leer el vinculo del guardado personalizado: " + exception.Message);
                return false;
            }
        }

        internal static string GetAutomaticSaveName(SaveData data, string requestedName)
        {
            if (!string.IsNullOrEmpty(requestedName) || !IsCustomSession ||
                data?.Level == null || !Match(data.Level) || string.IsNullOrEmpty(_path))
                return requestedName;
            var relative = GetRelativeMapPath(_path);
            return "UP_" + data.Level.UniqueID + "_" + StablePathHash(relative);
        }

        internal static void UpdateSaveLink(
            SaveSystem saveSystem, SaveData data, string saveName, int slot, bool succeeded)
        {
            if (!succeeded || saveSystem == null || data?.Level == null) return;
            try
            {
                var actualName = string.IsNullOrEmpty(saveName) ? data.Level.UniqueID : saveName;
                var sanitised = SanitiseSaveNameMethod?.Invoke(null, new object[] { actualName }) as string;
                var folder = SaveLocationForSlotMethod?.Invoke(saveSystem, new object[] { slot }) as string;
                if (string.IsNullOrEmpty(sanitised) || string.IsNullOrEmpty(folder))
                    throw new InvalidOperationException("No se pudo resolver la ruta nativa del guardado.");
                var linkPath = Path.Combine(folder, sanitised + ".sav") + SaveLinkExtension;
                if (!IsCustomSession || !Match(data.Level) || string.IsNullOrEmpty(_path))
                {
                    if (File.Exists(linkPath)) File.Delete(linkPath);
                    return;
                }
                var relative = GetRelativeMapPath(_path);
                var temporary = linkPath + ".tmp";
                Directory.CreateDirectory(folder);
                File.WriteAllText(temporary, SaveLinkSignature + Environment.NewLine + relative,
                    new UTF8Encoding(false));
                File.Copy(temporary, linkPath, true);
                File.Delete(temporary);
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: guardado vinculado al mapa personalizado " + relative + ".");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Carrera: no se pudo vincular el guardado con su mapa: " + exception);
            }
        }

        private static string GetRelativeMapPath(string path)
        {
            var root = Path.GetFullPath(ScenarioEditorStorage.Folder)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El mapa no esta en la carpeta maps.");
            return full.Substring(root.Length);
        }

        private static string StablePathHash(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var character in value.ToUpperInvariant())
            {
                hash ^= character;
                hash *= prime;
            }
            return hash.ToString("X16");
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
            var mapping = AssetIdsField?.GetValue(saveSystem) as BiDictionary<int, object>;
            if (saveSystem == null || mapping == null || _plotAssets == null)
                throw new InvalidOperationException("No esta disponible el mapa de IDs del guardado.");
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
            _registeredSaveSystem = saveSystem;
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
            var mapping = AssetIdsField?.GetValue(_registeredSaveSystem) as BiDictionary<int, object>;
            if (mapping != null)
            {
                foreach (var pair in RegisteredSaveAssets)
                    if (mapping.TryGetValue(pair.Key, out var current) &&
                        ReferenceEquals(current, pair.Value))
                        mapping.Remove(pair.Key);
                RefreshSerializerMappings(_registeredSaveSystem, mapping);
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

        // Prefijo del constructor WorldState: insertar la lista real de parcelas antes del foreach nativo.
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
            try
            {
                var folder = Path.GetDirectoryName(_path);
                var roots = UnityEngine.Object.FindObjectsOfType<Transform>();
                var root = roots.FirstOrDefault(t => t != null &&
                    t.name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase));
                if (root == null) return;
                // EXACTAMENTE el mismo filtro y orden que usa el pintor al
                // asignar indices a los Pass (alpha_N.png y terrain_mesh.bin).
                var renderers = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r != null &&
                        r.gameObject.name.StartsWith("Pass", StringComparison.OrdinalIgnoreCase) &&
                        r.sharedMaterials != null && r.sharedMaterials.Length != 0 &&
                        r.sharedMaterials[0] != null)
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
                // Los PNG solo alteran las texturas; la geometria se restaura
                // por separado desde terrain_mesh.bin tras cargar el nivel.
                ApplyTerrainMeshes(folder, renderers);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudieron aplicar las texturas del mapa: " + ex);
            }
        }

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
            SaveFileHeader matching;
            if (ScenarioCareerMapLoader.TryGetCurrentMapSave(
                    __instance?.SaveSystem, __0.UniqueId, out matching))
            {
                __1 = matching;
                __2 = false;
            }
            else
            {
                __1 = null;
                __2 = true;
            }
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

    [HarmonyPatch(typeof(SaveSystem), "GetSaveForLevel")]
    internal static class ScenarioCareerMapSaveSelectionPatch
    {
        private static bool Prefix(SaveSystem __instance, string __0, bool __1,
            ref SaveFileHeader __result)
        {
            if (ScenarioEditorFlow.IsRunning) return true;
            if (ScenarioCareerMapLoader.IsCustomSession &&
                ScenarioCareerMapLoader.MatchesLevelId(__0))
                ScenarioCareerMapLoader.TryGetCurrentMapSave(__instance, __0, out __result);
            else
                ScenarioCareerMapLoader.TryGetVanillaSave(__instance, __0, __1, out __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "SaveImplementation")]
    internal static class ScenarioCareerMapSaveNamePatch
    {
        private static void Prefix(SaveData __1, ref string __2) =>
            __2 = ScenarioCareerMapLoader.GetAutomaticSaveName(__1, __2);
    }

    [HarmonyPatch(typeof(SaveSystem), "SaveLevelImplementationInner")]
    internal static class ScenarioCareerMapSaveLinkPatch
    {
        private static void Postfix(SaveSystem __instance, SaveData __0, string __1,
            int __2, bool __result) =>
            ScenarioCareerMapLoader.UpdateSaveLink(__instance, __0, __1, __2, __result);
    }

    [HarmonyPatch(typeof(MetagameMap), "Open")]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("artef.twopointhospital.underpressure")]
    internal static class ScenarioCareerMapAutoLoadPatch
    {
        private static readonly Type StartupType =
            AccessTools.TypeByName("UnderPressure.SkipFrontEndForAutoLoadPatch");
        private static readonly MethodInfo KeepVisibleGetter =
            StartupType == null ? null :
            AccessTools.PropertyGetter(StartupType, "KeepLoadingScreenVisible");
        private static readonly MethodInfo SelectedSaveGetter =
            StartupType == null ? null :
            AccessTools.PropertyGetter(StartupType, "SelectedLevelSave");

        private static void Prefix(MetagameMap __instance)
        {
            if (ScenarioEditorFlow.IsRunning || ScenarioCareerMapLoader.IsCustomSession ||
                KeepVisibleGetter == null || SelectedSaveGetter == null ||
                !(KeepVisibleGetter.Invoke(null, null) is bool active) || !active)
                return;
            var header = SelectedSaveGetter.Invoke(null, null) as SaveFileHeader;
            var level = header == null || __instance?.Metagame?.LevelList == null
                ? null
                : __instance.Metagame.LevelList.GetLevelConfigByID(header.LevelID);
            if (level == null) return;
            try
            {
                if (ScenarioCareerMapLoader.TryPrepareLinkedSave(level, header))
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Carrera: autocarga preparada para una partida de mapa personalizado.");
            }
            catch (Exception exception)
            {
                ScenarioCareerMapLoader.Cancel();
                UnderPressureEditorPlugin.Log?.LogError(
                    "Carrera: no se pudo preparar el mapa de la partida mas reciente: " + exception);
            }
        }
    }
    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioCareerMapResetPatch
    {
        private static void Postfix() => ScenarioCareerMapLoader.Cancel();
    }
}
