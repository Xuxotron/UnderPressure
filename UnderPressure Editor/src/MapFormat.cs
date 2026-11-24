// Updated: 2026-11-16 - Aplicar availableRooms en un WorldState.Config runtime aislado.
// Mantiene el contrato config.json, sus PNG/capas y la aplicacion jugable del escenario.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioMapFormat
    {
        internal const int Version = 3;
        internal const string ConfigFileName = "config.json";
        internal const string PerimeterNone = "None";
        internal const string PerimeterOriginal = "Original";
        internal const string PerimeterCustom = "Custom";
        private const int MaxHeaderBytes = 1024 * 1024;
        private const int MaxPlotBytes = 32 * 1024 * 1024;
        private const int MaxPngBytes = 32 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex ZeroTransformFields = new Regex(
            @",\s*""(?:x|y|z|rotation)""\s*:\s*-?0(?:\.0+)?(?:[eE][+-]?\d+)?(?=\s*[,}])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex RotationField = new Regex(
            @"""rotation""\s*:", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex VersionField = new Regex(
            @"""version""\s*:\s*(-?\d+)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        [Serializable]
        internal sealed class Map
        {
            public int version = Version;
            public string name = "";
            public string author = "";
            public string baseHospitalId = "";
            public string baseHospitalName = "";
            public string description = "";
            public string DisplayName = "";
            public string[] DescriptionPerStar = new string[0];
            public int InitialBalance = 1000000;
            public int FailStateBalanceWarning = -150000;
            public int FailStateBalanceGameOver = -300000;
            public bool patientSettingsConfigured;
            public int maxPatients = 250;
            public float patientSpawnRate = 48f;
            public bool neverSpawnPatients;
            public bool usePatientSelector = true;
            public float patientSelectionRandomJitter = .35f;
            public bool environmentSettingsConfigured;
            public float initialTemperature;
            public float initialAttractiveness;
            public float initialHygiene = 1f;
            public bool scenarioSettingsConfigured;
            public bool illnessesConfigured;
            public WeightedIllnessData[] illnesses = new WeightedIllnessData[0];
            public bool availableRoomsConfigured;
            public string[] availableRooms = new string[0];
            public bool objectivesConfigured;
            public ObjectiveData[] objectives = new ObjectiveData[0];
            public bool emergenciesConfigured;
            public EmergencyData[] emergencies = new EmergencyData[0];
            public string terrainManifest = "";
            public string fogOfWarFile = "";
            public int fogTileWidth;
            public int fogTileHeight;
            public int fogMaxDistance;
            public int fogAnchorX;
            public int fogAnchorY;
            public bool fogUseCameraBoundingBoxExtents;
            public string plotPerimeter = PerimeterNone;
            public string[] perimeterPrefabs = new string[0];
            public string builtRoomStyle = "";
            public string buildingRoomStyle = "";
            public string unbuiltRoomStyle = "";
            public string sceneFile = "";
            // Nombre del JSON de paisaje, independiente de las parcelas.
            public string landscapeFile = "";
            // Trafico ambiental y puntos globales de llegada/salida.
            public string trafficFile = "";
            public PlotReference[] plots = new PlotReference[0];
        }

        [Serializable]
        private sealed class MapDocument
        {
            public int version = Version;
            public string name = "";
            public string author = "";
            public string displayName = "";
            public string description = "";
            public string terrainManifest = "";
            public string sceneFile = "";
            public string landscapeFile = "";
            public string trafficFile = "";
        }

        [Serializable]
        internal sealed class BaseHospitalData
        {
            public string id = "";
            public string name = "";
        }

        [Serializable]
        internal sealed class BalanceData
        {
            public int initial = 1000000;
            public int warning = -150000;
            public int gameOver = -300000;
        }

        [Serializable]
        internal sealed class PatientData
        {
            public int maxPatients = 250;
            public float patientSpawnRate = 48f;
            public bool neverSpawnPatients;
            public bool usePatientSelector = true;
            public float patientSelectionRandomJitter = .35f;
        }

        [Serializable]
        internal sealed class EnvironmentData
        {
            public float initialTemperature;
            public float initialAttractiveness;
            public float initialHygiene = 1f;
        }

        [Serializable]
        internal sealed class FogData
        {
            public string file = "";
            public int width;
            public int height;
            public int maxDistance;
            public int anchorX;
            public int anchorY;
            public bool useCameraBoundingBoxExtents;
        }

        [Serializable]
        internal sealed class PerimeterData
        {
            public string type = PerimeterNone;
            public string[] prefabs = new string[0];
        }

        [Serializable]
        internal sealed class RoomStylesData
        {
            public string built = "";
            public string building = "";
            public string unbuilt = "";
        }

        [Serializable]
        internal sealed class WeightedIllnessData
        {
            public string definition = "";
            public string room = "";
            public bool deprecated;
            public bool unlocked = true;
            public int minWeight = 20;
            public int maxWeight = 100;
            public int minStarRating;
            public int minPatientsSpawned;
        }

        [Serializable]
        internal sealed class ObjectiveData
        {
            public string id = "";
            public string name = "";
            public string template = "";
            public bool isStar;
            public int starIndex;
            public string taskName = "";
            public bool waitForCompletion = true;
            public bool isDiscovered;
            public bool isReplayable;
            public bool startImmediately = true;
            public int scheduleIndex = -1;
            public string scheduleName = "";
            public bool scheduleEnabled = true;
            public bool scheduleStartsWithCooldown = true;
            public int minimumCooldownDays = 150;
            public int maximumCooldownDays = 250;
            public int weight = 1;
            public int scoreType;
            public int requiredHighScore = -1;
            public bool evaluateOnTimeElapse;
            public bool canAbandon;
            public string durationDays = "30";
            public string probability = "1";
            public string difficulty = "0";
            public string cooldownSeconds = "20";
            public ObjectiveRuleData[] requirements = new ObjectiveRuleData[0];
            public ObjectiveRuleData[] completion = new ObjectiveRuleData[0];
            public ObjectiveRuleData[] failure = new ObjectiveRuleData[0];
            public ObjectiveRuleData[] abandon = new ObjectiveRuleData[0];
        }

        [Serializable]
        internal sealed class ObjectiveRuleData
        {
            public string type = "";
            public string value = "";
            public string reference = "";
        }

        [Serializable]
        internal sealed class EmergencyData
        {
            public string template = "";
            public int scheduleIndex = -1;
            public string scheduleName = "";
            public bool scheduleEnabled = true;
            public bool scheduleStartsWithCooldown = true;
            public int minimumCooldownDays = 150;
            public int maximumCooldownDays = 250;
            public int weight = 1;
            public int failureAction = 1;
            public int scoreType = (int)TH20.Objective.ObjectiveScoring.TimedStandard;
            public string arrivalMethod = "";
            public string arrivalMethodType = "";
            public bool issueWarning = true;
            public bool rejectable = true;
            public int patients = 7;
            public float diagnosis = 80f;
            public int success = 70;
            public float arrivalRate = 1.5f;
            public int durationDays = 30;
            public int daysUntilStart = 3;
            public string[] illnesses = new string[0];
            public EmergencyRuleData[] prerequisites = new EmergencyRuleData[0];
            public EmergencyRuleData[] rewards = new EmergencyRuleData[0];
        }

        [Serializable]
        internal sealed class EmergencyRuleData
        {
            public int type;
            public string value = "";
            public string reference = "";
            public bool lessThan;
        }

        [Serializable]
        internal sealed class PlotReference
        {
            public int index;
            public string dataFile = "";
            public string imageFile = "";
        }

        [Serializable]
        internal sealed class Plot
        {
            public int index;
            public int storey;
            public string nameTerm = "";
            public int cost;
            public Color color = Color.white;
            public bool initiallyOpen;
            public bool built;
            public bool instaBuild;
            public bool hideUntilAvailable;
            public bool notHiddenInSandbox;
            public bool includePerimiterInNavMesh = true;
            public bool useEnergyUI;
            public int energyUnitsGenerated;
            public float timeToBuild = 60f;
            public float footprintYOffset;
            public bool buildObjectiveAutoStart;
            public bool buildObjectiveStartOnPrereqsMet;
            public int mergeInto = -1;
            public int ambulanceBayEntranceSide = (int)TH20.GridDirection.Max;
            public bool customRoomStyles;
            public string builtRoomStyle = "";
            public string buildingRoomStyle = "";
            public string unbuiltRoomStyle = "";
            public PlotPrerequisite[] prerequisites = new PlotPrerequisite[0];
            public Item[] baseItems = new Item[0];
            public Item[] builtItems = new Item[0];
            public Item[] unbuiltItems = new Item[0];
        }

        [Serializable]
        internal sealed class PlotPrerequisite
        {
            public int[] anyBought = new int[0];
        }

        // Objeto interno: sigue usando rotation para no alterar los sistemas nativos.
        [Serializable]
        internal sealed class Item
        {
            public string guid = "";
            public string tag = "";
            public string prefab = "";
            public float x;
            public float y;
            public float z;
            public float rotation;
        }

        // Representacion de lectura para el formato compacto nuevo.
        // NO incluye rotation: el lector antiguo lo proporciona de forma aislada.
        [Serializable]
        private sealed class CompactItem
        {
            public string guid = "";
            public string tag = "";
            public string prefab = "";
            public float x = 0f;
            public float y = 0f;
            public float z = 0f;
            public float r = 0f;
        }

        [Serializable]
        internal sealed class Scene
        {
            public Item[] items = new Item[0];
            public OriginalChange[] sourceChanges = new OriginalChange[0];
        }

        [Serializable]
        internal sealed class OriginalChange
        {
            public string path = "";
            public bool deleted;
            public string guid = "";
            public string tag = "";
            public string prefab = "";
            public float x;
            public float y;
            public float z;
            public float rotation;
        }

        internal sealed class LoadedPlot
        {
            internal PlotReference Reference;
            internal Plot Data;
            internal byte[] ImagePng;
        }

        internal sealed class LoadedMap
        {
            internal Map Header;
            internal ScenarioMapLocalization.Table Translations =
                new ScenarioMapLocalization.Table();
            internal Scene SceneData = new Scene();
            internal readonly List<LoadedPlot> Plots = new List<LoadedPlot>();
            internal byte[] FogOfWarPng;
            internal string PackagePath;
            internal string ContentFolder;
        }

        internal static Map ReadHeader(string file)
        {
            if (!string.Equals(Path.GetExtension(file), ScenarioMapPackage.Extension,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El mapa debe ser un paquete .upmap.");
            return ParseHeader(ScenarioMapPackage.ReadText(file, ConfigFileName, MaxHeaderBytes));
        }

        internal static bool TryReadVersion(string file, out int version)
        {
            version = Version;
            try
            {
                if (!string.Equals(Path.GetExtension(file), ScenarioMapPackage.Extension,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                var prefix = ScenarioMapPackage.ReadTextPrefix(
                    file, ConfigFileName, 4096, MaxHeaderBytes);
                var match = VersionField.Match(prefix);
                return match.Success && int.TryParse(match.Groups[1].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out version);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "No se pudo leer la version del mapa " + file + ": " + exception.Message);
                return false;
            }
        }

        internal static Map ReadLooseHeader(string file)
        {
            return ParseHeader(ReadText(file, MaxHeaderBytes));
        }

        private static Map ParseHeader(string json)
        {
            var document = JsonUtility.FromJson<MapDocument>(json);
            if (document == null)
                throw new InvalidDataException("config.json ilegible.");

            // Las versiones anteriores guardaban todos los campos directamente
            // en la raiz. Se conservan tal cual y se completan al volver a guardar.
            if (document.version < Version)
            {
                var legacy = JsonUtility.FromJson<Map>(json) ?? new Map();
                legacy.plots = ParseOptionalObjects<PlotReference>(json, "plots");
                legacy.illnesses = ParseOptionalObjects<WeightedIllnessData>(json, "illnesses");
                legacy.availableRooms = ParseOptionalStrings(json, "availableRooms");
                legacy.objectives = HasTopLevelField(json, "objectives")
                    ? ParseObjectives(json) : new ObjectiveData[0];
                legacy.emergencies = HasTopLevelField(json, "emergencies")
                    ? ParseEmergencies(json) : new EmergencyData[0];
                return ValidateHeader(legacy);
            }

            BaseHospitalData hospital;
            BalanceData balance;
            FogData fog;
            PerimeterData perimeter;
            RoomStylesData roomStyles;
            PatientData patients;
            EnvironmentData environment;
            var hasHospital = TryParseObject(json, "baseHospital", out hospital);
            var hasBalance = TryParseObject(json, "balance", out balance);
            var hasFog = TryParseObject(json, "fog", out fog);
            var hasPerimeter = TryParseObject(json, "perimeter", out perimeter);
            var hasRoomStyles = TryParseObject(json, "roomStyles", out roomStyles);
            var hasPatients = TryParseObject(json, "patients", out patients);
            var hasEnvironment = TryParseObject(json, "environment", out environment);
            hospital = hospital ?? new BaseHospitalData();
            balance = balance ?? new BalanceData();
            fog = fog ?? new FogData();
            perimeter = perimeter ?? new PerimeterData();
            roomStyles = roomStyles ?? new RoomStylesData();
            patients = patients ?? new PatientData();
            environment = environment ?? new EnvironmentData();
            var header = document == null ? null : new Map
            {
                version = document.version,
                name = document.name ?? "",
                author = document.author ?? "",
                baseHospitalId = hospital.id ?? "",
                baseHospitalName = hospital.name ?? "",
                description = document.description ?? "",
                DisplayName = HasTopLevelField(json, "displayName")
                    ? document.displayName ?? ""
                    : document.name ?? "",
                DescriptionPerStar = string.IsNullOrEmpty(document.description)
                    ? new string[0] : new[] { document.description },
                InitialBalance = balance.initial,
                FailStateBalanceWarning = balance.warning,
                FailStateBalanceGameOver = balance.gameOver,
                patientSettingsConfigured = hasPatients,
                maxPatients = patients.maxPatients,
                patientSpawnRate = patients.patientSpawnRate,
                neverSpawnPatients = patients.neverSpawnPatients,
                usePatientSelector = patients.usePatientSelector,
                patientSelectionRandomJitter = patients.patientSelectionRandomJitter,
                environmentSettingsConfigured = hasEnvironment,
                initialTemperature = environment.initialTemperature,
                initialAttractiveness = environment.initialAttractiveness,
                initialHygiene = environment.initialHygiene,
                scenarioSettingsConfigured = hasHospital || hasBalance,
                illnessesConfigured = HasTopLevelField(json, "illnesses"),
                illnesses = ParseOptionalObjects<WeightedIllnessData>(json, "illnesses"),
                availableRoomsConfigured = HasTopLevelField(json, "availableRooms"),
                availableRooms = ParseOptionalStrings(json, "availableRooms"),
                objectivesConfigured = HasTopLevelField(json, "objectives"),
                objectives = HasTopLevelField(json, "objectives")
                    ? ParseObjectives(json)
                    : new ObjectiveData[0],
                emergenciesConfigured = HasTopLevelField(json, "emergencies"),
                emergencies = HasTopLevelField(json, "emergencies")
                    ? ParseEmergencies(json)
                    : new EmergencyData[0],
                terrainManifest = document.terrainManifest ?? "",
                fogOfWarFile = fog.file ?? "",
                fogTileWidth = fog.width,
                fogTileHeight = fog.height,
                fogMaxDistance = fog.maxDistance,
                fogAnchorX = fog.anchorX,
                fogAnchorY = fog.anchorY,
                fogUseCameraBoundingBoxExtents = fog.useCameraBoundingBoxExtents,
                plotPerimeter = perimeter.type ?? PerimeterNone,
                perimeterPrefabs = perimeter.prefabs ?? new string[0],
                builtRoomStyle = roomStyles.built ?? "",
                buildingRoomStyle = roomStyles.building ?? "",
                unbuiltRoomStyle = roomStyles.unbuilt ?? "",
                sceneFile = document.sceneFile ?? "",
                landscapeFile = document.landscapeFile ?? "",
                trafficFile = document.trafficFile ?? "",
                plots = ParseOptionalObjects<PlotReference>(json, "plots")
            };
            return ValidateHeader(header);
        }

        private static Map ValidateHeader(Map header)
        {
            if (header != null) header.plotPerimeter = NormalizePerimeter(header.plotPerimeter);
            if (header != null && header.perimeterPrefabs == null)
                header.perimeterPrefabs = new string[0];
            if (header == null ||
                header.plots == null || header.plots.Length > 128 ||
                HasInvalidAuthor(header.author) ||
                header.perimeterPrefabs.Length > 32 ||
                HasInvalidStyleId(header.builtRoomStyle) ||
                HasInvalidStyleId(header.buildingRoomStyle) ||
                HasInvalidStyleId(header.unbuiltRoomStyle) ||
                HasInvalidStyleId(header.sceneFile) ||
                HasInvalidStyleId(header.trafficFile) ||
                HasInvalidStyleId(header.fogOfWarFile) ||
                (header.patientSettingsConfigured &&
                 (header.maxPatients < 1 || InvalidNumber(header.patientSpawnRate) ||
                  header.patientSpawnRate < 0f ||
                  InvalidNumber(header.patientSelectionRandomJitter) ||
                  header.patientSelectionRandomJitter < 0f ||
                  header.patientSelectionRandomJitter > 1f)) ||
                (header.environmentSettingsConfigured &&
                 (InvalidNumber(header.initialTemperature) ||
                  InvalidNumber(header.initialAttractiveness) ||
                  InvalidNumber(header.initialHygiene) ||
                  header.initialHygiene < 0f || header.initialHygiene > 1f)) ||
                Array.Exists(header.perimeterPrefabs, HasInvalidStyleId))
                throw new InvalidDataException("config.json contiene datos no permitidos.");
            return header;
        }

        private static bool HasInvalidAuthor(string value)
        {
            return value != null && (value.Length > 120 ||
                value.IndexOfAny(new[] {'\r', '\n'}) >= 0);
        }

        private static bool HasInvalidStyleId(string value)
        {
            return value != null && (value.Length > 160 || value.IndexOfAny(new[] {'\r', '\n'}) >= 0);
        }

        private static bool InvalidNumber(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }

        internal static string NormalizePerimeter(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, PerimeterNone, StringComparison.OrdinalIgnoreCase))
                return PerimeterNone;
            if (string.Equals(value, PerimeterOriginal, StringComparison.OrdinalIgnoreCase))
                return PerimeterOriginal;
            if (string.Equals(value, PerimeterCustom, StringComparison.OrdinalIgnoreCase))
                return PerimeterCustom;
            throw new InvalidDataException("Modo de perimetro de parcelas desconocido: " + value);
        }

        internal static Scene ReadScene(string path)
        {
            var json = ReadText(path, MaxPlotBytes);
            var scene = JsonUtility.FromJson<Scene>(json) ?? new Scene();
            scene.items = ParseOptionalObjects<Item>(json, "items");
            scene.sourceChanges = ParseOptionalObjects<OriginalChange>(json, "sourceChanges");
            return scene;
        }

        internal static LoadedMap Read(string file)
        {
            if (!string.Equals(Path.GetExtension(file), ScenarioMapPackage.Extension,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El mapa debe ser un paquete .upmap.");
            var package = Path.GetFullPath(file);
            var folder = ScenarioMapPackage.Materialize(package);
            var result = ReadLoose(Path.Combine(folder, ConfigFileName));
            result.PackagePath = package;
            result.ContentFolder = folder;
            return result;
        }

        internal static LoadedMap ReadLoose(string file)
        {
            var header = ReadLooseHeader(file);
            var folder = Path.GetDirectoryName(Path.GetFullPath(file));
            var result = new LoadedMap
            {
                Header = header,
                ContentFolder = folder,
                Translations = ScenarioMapLocalization.Read(folder)
            };
            if (!string.IsNullOrEmpty(header.fogOfWarFile))
            {
                if (header.fogTileWidth <= 0 || header.fogTileHeight <= 0 ||
                    header.fogMaxDistance <= 0)
                    throw new InvalidDataException("Definicion Fog of War incompleta.");
                var fogPath = Resolve(folder, header.fogOfWarFile, ".png");
                var fogInfo = new FileInfo(fogPath);
                if (!fogInfo.Exists || fogInfo.Length < 24 || fogInfo.Length > MaxPngBytes)
                    throw new InvalidDataException("PNG Fog of War vacio o excesivo: " +
                        header.fogOfWarFile);
                result.FogOfWarPng = File.ReadAllBytes(fogPath);
            }
            // Los experimentos anteriores con scene.json permanecen ignorados.
            // No se borra ni sobrescribe ningun archivo original.
            result.SceneData = new Scene();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.plots.Length; ++i)
            {
                var reference = header.plots[i];
                if (reference == null || reference.index != i)
                    throw new InvalidDataException("Las parcelas deben estar ordenadas por indice consecutivo.");
                var dataPath = Resolve(folder, reference.dataFile, ".json");
                var imagePath = Resolve(folder, reference.imageFile, ".png");
                if (!names.Add(reference.dataFile) || !names.Add(reference.imageFile))
                    throw new InvalidDataException("Archivos de parcelas duplicados.");
                var plot = ReadPlot(dataPath);
                if (plot.index != reference.index)
                    throw new InvalidDataException("Indice de parcela incompatible: " + reference.dataFile);
                var pngInfo = new FileInfo(imagePath);
                if (!pngInfo.Exists || pngInfo.Length < 24 || pngInfo.Length > MaxPngBytes)
                    throw new InvalidDataException("PNG de parcela vacio o excesivo: " + reference.imageFile);
                var imageBytes = File.ReadAllBytes(imagePath);
                if (imageBytes.Length < 24 || imageBytes.Length > MaxPngBytes)
                    throw new InvalidDataException("PNG de parcela vacio o excesivo: " + reference.imageFile);
                result.Plots.Add(new LoadedPlot { Reference = reference, Data = plot, ImagePng = imageBytes });
            }
            return result;
        }

        internal static string Resolve(string folder, string file, string extension)
        {
            if (string.IsNullOrEmpty(file) ||
                !string.Equals(Path.GetFileName(file), file, StringComparison.Ordinal) ||
                !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
                file.IndexOf('/') >= 0 || file.IndexOf('\\') >= 0 || file.IndexOf(':') >= 0 ||
                file == "." || file == ".." || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Nombre de archivo no permitido: " + file);
            return Path.Combine(folder, file);
        }

        private static string ReadText(string path, int maximum)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > maximum)
                throw new InvalidDataException("Archivo inexistente o demasiado grande: " + path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        internal static Plot ReadPlot(string path)
        {
            var json = ReadText(path, MaxPlotBytes);
            var plot = JsonUtility.FromJson<Plot>(json);
            if (plot == null)
                throw new InvalidDataException("Parcela JSON ilegible: " + Path.GetFileName(path));
            plot.baseItems = HasTopLevelField(json, "baseItems")
                ? ParseCompactPlotItems(json, "baseItems") : new Item[0];
            plot.builtItems = HasTopLevelField(json, "builtItems")
                ? ParseCompactPlotItems(json, "builtItems") : new Item[0];
            plot.unbuiltItems = HasTopLevelField(json, "unbuiltItems")
                ? ParseCompactPlotItems(json, "unbuiltItems") : new Item[0];
            plot.builtRoomStyle = plot.builtRoomStyle ?? "";
            plot.buildingRoomStyle = plot.buildingRoomStyle ?? "";
            plot.unbuiltRoomStyle = plot.unbuiltRoomStyle ?? "";
            if (HasInvalidStyleId(plot.builtRoomStyle) ||
                HasInvalidStyleId(plot.buildingRoomStyle) ||
                HasInvalidStyleId(plot.unbuiltRoomStyle))
                throw new InvalidDataException("Estilo de parcela incorrecto.");
            var rawPrerequisites = HasTopLevelField(json, "prerequisites")
                ? ArrayElements(json, "prerequisites") : new List<string>();
            var requirements = new List<PlotPrerequisite>();
            foreach (var raw in rawPrerequisites)
            {
                var requirement = JsonUtility.FromJson<PlotPrerequisite>(raw);
                if (requirement == null)
                    throw new InvalidDataException("Requisito de parcela ilegible.");
                var indices = HasTopLevelField(raw, "anyBought")
                    ? ArrayElements(raw, "anyBought") : new List<string>();
                requirement.anyBought = new int[indices.Count];
                for (var i = 0; i < indices.Count; i++)
                {
                    int parsed;
                    if (!int.TryParse(indices[i], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out parsed))
                        throw new InvalidDataException("Indice de requisito de parcela incorrecto.");
                    requirement.anyBought[i] = parsed;
                }
                requirements.Add(requirement);
            }
            plot.prerequisites = requirements.ToArray();
            return plot;
        }

        // El formato nuevo lee solo r. El archivo OldLoaderMap.cs, mientras exista,
        // recupera rotation para los JSON antiguos mediante un parche independiente.
        private static Item[] ParseCompactPlotItems(string json, string key)
        {
            var entries = ArrayElements(json, key);
            var result = new Item[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var raw = JsonUtility.FromJson<CompactItem>(entries[i]);
                if (raw == null)
                    throw new InvalidDataException("Objeto ilegible en " + key);
                result[i] = new Item
                {
                    guid = raw.guid ?? "",
                    tag = raw.tag ?? "",
                    prefab = raw.prefab ?? "",
                    x = raw.x, y = raw.y, z = raw.z,
                    rotation = raw.r
                };
            }
            return result;
        }

        internal static T[] ParseObjects<T>(string json, string key) where T : class
        {
            var entries = ArrayElements(json, key);
            var result = new T[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                result[i] = JsonUtility.FromJson<T>(entries[i]);
                if (result[i] == null)
                    throw new InvalidDataException("Elemento JSON ilegible en " + key);
            }
            return result;
        }

        private static T[] ParseOptionalObjects<T>(string json, string key) where T : class
        {
            return HasTopLevelField(json, key)
                ? ParseObjects<T>(json, key) : new T[0];
        }

        private static string[] ParseOptionalStrings(string json, string key)
        {
            return HasTopLevelField(json, key)
                ? ParseStrings(json, key) : new string[0];
        }

        private static ObjectiveData[] ParseObjectives(string json)
        {
            var entries = ArrayElements(json, "objectives");
            var result = new ObjectiveData[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                var raw = entries[index];
                var value = JsonUtility.FromJson<ObjectiveData>(raw);
                if (value == null)
                    throw new InvalidDataException("Objetivo ilegible en config.json.");
                value.requirements = ParseOptionalObjects<ObjectiveRuleData>(raw, "requirements");
                value.completion = ParseOptionalObjects<ObjectiveRuleData>(raw, "completion");
                value.failure = ParseOptionalObjects<ObjectiveRuleData>(raw, "failure");
                value.abandon = ParseOptionalObjects<ObjectiveRuleData>(raw, "abandon");
                result[index] = value;
            }
            return result;
        }

        private static EmergencyData[] ParseEmergencies(string json)
        {
            var entries = ArrayElements(json, "emergencies");
            var result = new EmergencyData[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                var raw = entries[index];
                var value = JsonUtility.FromJson<EmergencyData>(raw);
                if (value == null)
                    throw new InvalidDataException("Emergencia ilegible en config.json.");
                value.illnesses = ParseOptionalStrings(raw, "illnesses");
                value.prerequisites = ParseOptionalObjects<EmergencyRuleData>(raw, "prerequisites");
                value.rewards = ParseOptionalObjects<EmergencyRuleData>(raw, "rewards");
                result[index] = value;
            }
            return result;
        }

        private static string[] ParseStrings(string json, string key)
        {
            var entries = ArrayElements(json, key);
            var result = new string[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                var wrapper = JsonUtility.FromJson<StringValue>(
                    "{\"value\":" + entries[index] + "}");
                if (wrapper == null)
                    throw new InvalidDataException("Texto ilegible en " + key + ".");
                result[index] = wrapper.value ?? "";
            }
            return result;
        }

        // La compatibilidad temporal accede a estos elementos sin reescribir archivos.
        internal static List<string> ArrayElements(string json, string key)
        {
            var label = "\"" + key + "\"";
            var at = json.IndexOf(label, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidDataException("Falta el array JSON: " + key);
            at += label.Length;
            while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
            if (at >= json.Length || json[at++] != ':')
                throw new InvalidDataException("Array JSON incorrecto: " + key);
            while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
            if (at >= json.Length || json[at++] != '[')
                throw new InvalidDataException("Array JSON incorrecto: " + key);

            var results = new List<string>();
            var elementStart = at;
            var depth = 0;
            var quoted = false;
            var escaped = false;
            for (; at < json.Length; ++at)
            {
                var c = json[at];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '}' || c == ']')
                {
                    if (depth > 0) { depth--; continue; }
                    if (c != ']') throw new InvalidDataException("Array JSON sin cerrar: " + key);
                    var final = json.Substring(elementStart, at - elementStart).Trim();
                    if (final.Length > 0) results.Add(final);
                    return results;
                }
                if (c == ',' && depth == 0)
                {
                    var element = json.Substring(elementStart, at - elementStart).Trim();
                    if (element.Length == 0)
                        throw new InvalidDataException("Elemento vacio en array JSON: " + key);
                    results.Add(element);
                    elementStart = at + 1;
                }
            }
            throw new InvalidDataException("Array JSON sin cerrar: " + key);
        }

        private static string SerializeCompactPlotItem(Item item)
        {
            var json = JsonUtility.ToJson(item, false);
            if (string.IsNullOrEmpty(json) ||
                !json.Contains("\"guid\"") ||
                !json.Contains("\"tag\"") ||
                !json.Contains("\"prefab\"") ||
                !json.Contains("\"rotation\""))
                throw new InvalidDataException("Unity no serializo correctamente un objeto de parcela.");
            // Comparacion exacta con el cero serializado: valores minimos no se redondean.
            json = ZeroTransformFields.Replace(json, "");
            return RotationField.Replace(json, "\"r\":");
        }

        private static string SerializeObjects<T>(T[] objects, bool compactPlotItems = false) where T : class
        {
            var text = new StringBuilder("[");
            if (objects != null)
                for (var i = 0; i < objects.Length; i++)
                {
                    if (objects[i] == null)
                        throw new InvalidDataException("El array JSON contiene una referencia vacia.");
                    if (i > 0) text.Append(',');
                    var serialized = compactPlotItems && objects[i] is Item
                        ? SerializeCompactPlotItem((Item)(object)objects[i])
                        : JsonUtility.ToJson(objects[i], false);
                    if (!compactPlotItems && objects[i] is Item &&
                        (!serialized.Contains("\"guid\"") ||
                         !serialized.Contains("\"x\"") ||
                         !serialized.Contains("\"rotation\"")))
                        throw new InvalidDataException("Unity no serializo correctamente un objeto de escena.");
                    if (objects[i] is PlotReference &&
                        (!serialized.Contains("\"dataFile\"") ||
                         !serialized.Contains("\"imageFile\"")))
                        throw new InvalidDataException("Unity no serializo una referencia de parcela.");
                    text.Append(serialized);
                }
            text.Append(']');
            return text.ToString();
        }

        private static string SerializePlot(Plot plot)
        {
            var json = JsonUtility.ToJson(plot, true);
            json = ReplaceArray(json, "baseItems", SerializeObjects(plot.baseItems, true));
            json = ReplaceArray(json, "builtItems", SerializeObjects(plot.builtItems, true));
            json = ReplaceArray(json, "unbuiltItems", SerializeObjects(plot.unbuiltItems, true));
            var requirements = new StringBuilder("[");
            if (plot.prerequisites != null)
                for (var i = 0; i < plot.prerequisites.Length; i++)
                {
                    var item = plot.prerequisites[i];
                    if (item == null) throw new InvalidDataException("Requisito de parcela vacio.");
                    if (i > 0) requirements.Append(',');
                    var itemJson = JsonUtility.ToJson(item, false);
                    var ints = item.anyBought ?? new int[0];
                    var values = new string[ints.Length];
                    for (var j = 0; j < ints.Length; j++)
                        values[j] = ints[j].ToString(System.Globalization.CultureInfo.InvariantCulture);
                    requirements.Append(ReplaceArray(itemJson, "anyBought", "[" + string.Join(",", values) + "]"));
                }
            requirements.Append(']');
            return ReplaceArray(json, "prerequisites", requirements.ToString());
        }

        private static string SerializeScene(Scene scene)
        {
            var json = ReplaceArray(JsonUtility.ToJson(scene, true),
                "items", SerializeObjects(scene.items));
            return ReplaceArray(json, "sourceChanges", SerializeObjects(scene.sourceChanges));
        }

        private static string SerializeMap(Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var hospital = new BaseHospitalData
                { id = map.baseHospitalId ?? "", name = map.baseHospitalName ?? "" };
            var balance = new BalanceData
            {
                initial = map.InitialBalance,
                warning = map.FailStateBalanceWarning,
                gameOver = map.FailStateBalanceGameOver
            };
            var patients = new PatientData
            {
                maxPatients = map.maxPatients,
                patientSpawnRate = map.patientSpawnRate,
                neverSpawnPatients = map.neverSpawnPatients,
                usePatientSelector = map.usePatientSelector,
                patientSelectionRandomJitter = map.patientSelectionRandomJitter
            };
            var environment = new EnvironmentData
            {
                initialTemperature = map.initialTemperature,
                initialAttractiveness = map.initialAttractiveness,
                initialHygiene = map.initialHygiene
            };
            var fog = new FogData
            {
                file = map.fogOfWarFile ?? "",
                width = map.fogTileWidth,
                height = map.fogTileHeight,
                maxDistance = map.fogMaxDistance,
                anchorX = map.fogAnchorX,
                anchorY = map.fogAnchorY,
                useCameraBoundingBoxExtents = map.fogUseCameraBoundingBoxExtents
            };
            var perimeter = new PerimeterData
            {
                type = NormalizePerimeter(map.plotPerimeter),
                prefabs = map.perimeterPrefabs ?? new string[0]
            };
            var roomStyles = new RoomStylesData
            {
                built = map.builtRoomStyle ?? "",
                building = map.buildingRoomStyle ?? "",
                unbuilt = map.unbuiltRoomStyle ?? ""
            };

            var text = new StringBuilder();
            text.AppendLine("{");
            AppendMapLine(text, "version", map.version.ToString(
                System.Globalization.CultureInfo.InvariantCulture), true);
            AppendMapLine(text, "name", SerializeString(map.name), true);
            AppendMapLine(text, "author", SerializeString(map.author), true);
            AppendMapLine(text, "displayName", SerializeString(map.DisplayName), true);
            AppendMapLine(text, "baseHospital", "{\"id\":" + SerializeString(hospital.id) +
                ",\"name\":" + SerializeString(hospital.name) + "}", true);
            AppendMapLine(text, "description", SerializeString(map.description), true);
            AppendMapLine(text, "balance", "{\"initial\":" + Integer(balance.initial) +
                ",\"warning\":" + Integer(balance.warning) +
                ",\"gameOver\":" + Integer(balance.gameOver) + "}", true);
            AppendMapLine(text, "patients", JsonUtility.ToJson(patients, false), true);
            AppendMapLine(text, "environment", JsonUtility.ToJson(environment, false), true);
            text.AppendLine("    \"illnesses\": [");
            var illnesses = map.illnesses ?? new WeightedIllnessData[0];
            for (var i = 0; i < illnesses.Length; i++)
            {
                if (illnesses[i] == null)
                    throw new InvalidDataException("La lista de enfermedades contiene una entrada vacia.");
                text.Append("        ").Append(JsonUtility.ToJson(illnesses[i], false));
                if (i < illnesses.Length - 1) text.Append(',');
                text.AppendLine();
            }
            text.AppendLine("    ],");
            AppendMapLine(text, "availableRooms",
                SerializeStrings(map.availableRooms ?? new string[0]), true);
            text.AppendLine("    \"objectives\": [");
            var objectives = map.objectives ?? new ObjectiveData[0];
            for (var i = 0; i < objectives.Length; i++)
            {
                if (objectives[i] == null)
                    throw new InvalidDataException("La lista de objetivos contiene una entrada vacia.");
                var objective = JsonUtility.ToJson(objectives[i], false);
                objective = ReplaceArray(objective, "requirements",
                    SerializeObjects(objectives[i].requirements));
                objective = ReplaceArray(objective, "completion",
                    SerializeObjects(objectives[i].completion));
                objective = ReplaceArray(objective, "failure",
                    SerializeObjects(objectives[i].failure));
                objective = ReplaceArray(objective, "abandon",
                    SerializeObjects(objectives[i].abandon));
                text.Append("        ").Append(objective);
                if (i < objectives.Length - 1) text.Append(',');
                text.AppendLine();
            }
            text.AppendLine("    ],");
            text.AppendLine("    \"emergencies\": [");
            var emergencies = map.emergencies ?? new EmergencyData[0];
            for (var i = 0; i < emergencies.Length; i++)
            {
                if (emergencies[i] == null)
                    throw new InvalidDataException("La lista de emergencias contiene una entrada vacia.");
                var emergency = JsonUtility.ToJson(emergencies[i], false);
                emergency = ReplaceArray(emergency, "illnesses",
                    SerializeStrings(emergencies[i].illnesses));
                emergency = ReplaceArray(emergency, "prerequisites",
                    SerializeObjects(emergencies[i].prerequisites));
                emergency = ReplaceArray(emergency, "rewards",
                    SerializeObjects(emergencies[i].rewards));
                text.Append("        ").Append(emergency);
                if (i < emergencies.Length - 1) text.Append(',');
                text.AppendLine();
            }
            text.AppendLine("    ],");
            AppendMapLine(text, "terrainManifest", SerializeString(map.terrainManifest), true);
            AppendMapLine(text, "fog", "{\"file\":" + SerializeString(fog.file) +
                ",\"width\":" + Integer(fog.width) +
                ",\"height\":" + Integer(fog.height) +
                ",\"maxDistance\":" + Integer(fog.maxDistance) +
                ",\"anchorX\":" + Integer(fog.anchorX) +
                ",\"anchorY\":" + Integer(fog.anchorY) +
                ",\"useCameraBoundingBoxExtents\":" +
                (fog.useCameraBoundingBoxExtents ? "true" : "false") + "}", true);
            AppendMapLine(text, "perimeter", "{\"type\":" + SerializeString(perimeter.type) +
                ",\"prefabs\":" + SerializeStrings(perimeter.prefabs) + "}", true);
            AppendMapLine(text, "roomStyles", "{\"built\":" + SerializeString(roomStyles.built) +
                ",\"building\":" + SerializeString(roomStyles.building) +
                ",\"unbuilt\":" + SerializeString(roomStyles.unbuilt) + "}", true);
            AppendMapLine(text, "sceneFile", SerializeString(map.sceneFile), true);
            AppendMapLine(text, "landscapeFile", SerializeString(map.landscapeFile), true);
            AppendMapLine(text, "trafficFile", SerializeString(map.trafficFile), true);
            AppendMapLine(text, "plots", SerializeObjects(map.plots), false);
            text.Append('}');
            return text.ToString();
        }

        private static void AppendMapLine(StringBuilder target, string name, string value,
            bool comma)
        {
            target.Append("    \"").Append(name).Append("\": ").Append(value ?? "null");
            if (comma) target.Append(',');
            target.AppendLine();
        }

        private static string SerializeString(string value)
        {
            var json = JsonUtility.ToJson(new StringValue { value = value ?? "" }, false);
            var colon = json.IndexOf(':');
            if (colon < 0 || json.Length < colon + 3)
                throw new InvalidDataException("Unity no pudo serializar un texto del mapa.");
            return json.Substring(colon + 1, json.Length - colon - 2);
        }

        private static string Integer(int value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string SerializeStrings(string[] values)
        {
            var text = new StringBuilder("[");
            if (values != null)
                for (var i = 0; i < values.Length; i++)
                {
                    if (i > 0) text.Append(',');
                    text.Append(SerializeString(values[i]));
                }
            return text.Append(']').ToString();
        }

        private static T ParseObject<T>(string json, string key) where T : class
        {
            var source = ExtractObject(json, key);
            var value = JsonUtility.FromJson<T>(source);
            if (value == null)
                throw new InvalidDataException("Objeto JSON incorrecto: " + key);
            return value;
        }

        private static bool TryParseObject<T>(string json, string key, out T value) where T : class
        {
            value = null;
            if (!HasTopLevelField(json, key)) return false;
            value = ParseObject<T>(json, key);
            return true;
        }

        private static bool HasTopLevelField(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            var objectDepth = 0;
            var arrayDepth = 0;
            var quoted = false;
            var escaped = false;
            for (var index = 0; index < json.Length; index++)
            {
                var current = json[index];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '"') quoted = false;
                    continue;
                }
                if (current == '"')
                {
                    if (objectDepth == 1 && arrayDepth == 0)
                    {
                        var end = index + 1;
                        while (end < json.Length && json[end] != '"')
                        {
                            if (json[end] == '\\') end++;
                            end++;
                        }
                        if (end < json.Length && end - index - 1 == key.Length &&
                            string.CompareOrdinal(json, index + 1, key, 0, key.Length) == 0)
                        {
                            var colon = end + 1;
                            while (colon < json.Length && char.IsWhiteSpace(json[colon])) colon++;
                            if (colon < json.Length && json[colon] == ':') return true;
                        }
                    }
                    quoted = true;
                }
                else if (current == '{') objectDepth++;
                else if (current == '}') objectDepth--;
                else if (current == '[') arrayDepth++;
                else if (current == ']') arrayDepth--;
            }
            return false;
        }

        private static string ExtractObject(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("JSON vacio: " + key);
            var label = "\"" + key + "\"";
            var at = json.IndexOf(label, StringComparison.Ordinal);
            if (at < 0) throw new InvalidDataException("Falta el objeto JSON: " + key);
            at += label.Length;
            while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
            if (at >= json.Length || json[at++] != ':')
                throw new InvalidDataException("Campo JSON incorrecto: " + key);
            while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
            if (at >= json.Length || json[at] != '{')
                throw new InvalidDataException("El campo JSON no es un objeto: " + key);
            var start = at;
            var depth = 0;
            var quoted = false;
            var escaped = false;
            for (; at < json.Length; at++)
            {
                var current = json[at];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '"') quoted = false;
                    continue;
                }
                if (current == '"') quoted = true;
                else if (current == '{') depth++;
                else if (current == '}' && --depth == 0)
                    return json.Substring(start, at - start + 1);
            }
            throw new InvalidDataException("Objeto JSON sin cerrar: " + key);
        }

        [Serializable]
        private sealed class StringValue
        {
            public string value = "";
        }

        // Landscape usa listas de objetos de escena; Unity puede omitir sus
        // elementos al serializar un contenedor con arrays de clases anidadas.
        // Escribir SIEMPRE cada elemento independientemente, como con las parcelas.
        private static string SerializeLandscape(ScenarioLandscape.Data landscape)
        {
            if (landscape.objects == null || landscape.added == null)
                throw new InvalidDataException("Landscape contiene arrays de objetos nulos.");

            for (var i = 0; i < landscape.objects.Length; i++)
            {
                var item = landscape.objects[i];
                if (item == null || string.IsNullOrEmpty(item.id) ||
                    string.IsNullOrEmpty(item.name))
                    throw new InvalidDataException("Objeto original de Landscape sin identidad: " + i);
            }

            var json = JsonUtility.ToJson(landscape, true);
            json = ReplaceArray(json, "objects", SerializeObjects(landscape.objects));
            return ReplaceArray(json, "added", SerializeObjects(landscape.added));
        }

        private static string SerializeTraffic(ScenarioTraffic.Data traffic)
        {
            if (traffic.objects == null || traffic.added == null)
                throw new InvalidDataException("Traffic contiene arrays de objetos nulos.");
            var json = JsonUtility.ToJson(traffic, true);
            json = ReplaceArray(json, "objects", SerializeObjects(traffic.objects));
            return ReplaceArray(json, "added", SerializeObjects(traffic.added));
        }

        private static string ReplaceArray(string json, string key, string array)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("No se pudo serializar el JSON: " + key);
            var label = "\"" + key + "\"";
            var at = json.IndexOf(label, StringComparison.Ordinal);
            if (at < 0)
            {
                var close = json.LastIndexOf('}');
                if (close < 0) throw new InvalidDataException("JSON incompleto: " + key);
                var before = json.Substring(0, close).TrimEnd();
                var separator = before.EndsWith("{", StringComparison.Ordinal) ? "" : ",";
                return before + separator + "\n    " + label + ": " + array +
                       "\n}" + json.Substring(close + 1);
            }
            at += label.Length;
            while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
            if (at >= json.Length || json[at++] != ':')
                throw new InvalidDataException("Campo JSON incorrecto: " + key);
            while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
            if (at >= json.Length || json[at] != '[')
                throw new InvalidDataException("El campo JSON no es un array: " + key);
            var open = at;
            var depth = 0;
            var quoted = false;
            var escaped = false;
            for (; at < json.Length; at++)
            {
                var c = json[at];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(0, open) + array + json.Substring(at + 1);
                }
            }
            throw new InvalidDataException("Array JSON sin cerrar: " + key);
        }

        internal static void WriteJson(string path, object value)
        {
            var plot = value as Plot;
            var map = value as Map;
            var scene = value as Scene;
            var landscape = value as ScenarioLandscape.Data;
            var traffic = value as ScenarioTraffic.Data;
            var text = plot != null ? SerializePlot(plot) :
                       map != null ? SerializeMap(map) :
                       scene != null ? SerializeScene(scene) :
                       landscape != null ? SerializeLandscape(landscape) :
                       traffic != null ? SerializeTraffic(traffic) :
                       JsonUtility.ToJson(value, true);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("No se pudo serializar el JSON: " + path);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Utf8);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        internal static void WriteBytes(string path, byte[] content)
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, content);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        internal static Texture2D DecodeTexture(byte[] png, string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, png, false))
                    throw new InvalidDataException("No se pudo interpretar el PNG: " + name);
                if (texture.width < 4 || texture.height < 4 ||
                    (long)texture.width * texture.height > 16000000L)
                    throw new InvalidDataException("Dimensiones invalidas: " + name);
                texture.name = name;
                texture.hideFlags = HideFlags.DontSave;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
        }
    }
}

namespace UnderPressure.Editor
{
    internal sealed class UnderPressureFinanceConfigInstance :
        SharedInstance<FinanceManager.Config> { }

    internal sealed class UnderPressureCharacterConfigInstance :
        SharedInstance<CharacterManager.Config> { }

    internal sealed class UnderPressureWorldStateConfigInstance :
        SharedInstance<WorldState.Config> { }

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
        private static bool _trafficApplied;
        private static bool _introductionShown;
        private static ScenarioCareerMapTerrainRetry _retry;
        private static UnderPressureFinanceConfigInstance _runtimeFinanceWrapper;
        private static UnderPressureCharacterConfigInstance _runtimeCharacterWrapper;
        private static readonly List<UnityEngine.Object> RuntimeScenarioAssets =
            new List<UnityEngine.Object>();
        internal static bool IsCustomSession => _active || _path != null;

        internal static void Prepare(LevelConfig level, string path)
        {
            if (level == null || string.IsNullOrEmpty(path)) throw new ArgumentException("Mapa incorrecto.");
            var full = Path.GetFullPath(path);
            var folder = Path.GetFullPath(ScenarioEditorStorage.Folder).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
            if (!ScenarioWorkshopLibrary.AllowedStartsWith(
                    full, folder, StringComparison.OrdinalIgnoreCase) ||
                !ScenarioWorkshopLibrary.AllowedDirectPackage(
                    full, ScenarioEditorStorage.Folder))
                throw new InvalidDataException(
                    "El mapa no pertenece a una fuente local o de Workshop autorizada.");
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
            _trafficApplied = false;
            _introductionShown = false;
            ScenarioTraffic.RestoreRuntime();
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
            foreach (var asset in RuntimeScenarioAssets)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            RuntimeScenarioAssets.Clear();
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
            var language = EditorLocalization.CurrentLanguageCode();
            var descriptions = new string[3];
            for (var star = 1; star <= 3; ++star)
            {
                var fallback = header.DescriptionPerStar == null ||
                               header.DescriptionPerStar.Length == 0
                    ? "" : header.DescriptionPerStar[Math.Min(
                        header.DescriptionPerStar.Length - 1, star - 1)] ?? "";
                var localized = _map.Translations?.Resolve(
                    ScenarioMapLocalization.StarKey(star), language) ?? "";
                descriptions[star - 1] = string.IsNullOrEmpty(localized)
                    ? fallback : localized;
            }
            AccessTools.Field(typeof(LevelConfig), "DescriptionPerStar")?.SetValue(
                clone, descriptions);
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

            if (header.illnessesConfigured || header.patientSettingsConfigured)
            {
                var characters = config.GetCharacterManagerConfig();
                if (characters != null)
                {
                    var characterClone = (CharacterManager.Config)CloneMethod.Invoke(characters, null);
                    if (header.illnessesConfigured)
                    {
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
                    }
                    if (header.patientSettingsConfigured)
                    {
                        AccessTools.Field(typeof(CharacterManager.Config), "_maxPatients")
                            ?.SetValue(characterClone, header.maxPatients);
                        AccessTools.Field(typeof(CharacterManager.Config), "_patientSpawnRate")
                            ?.SetValue(characterClone, header.patientSpawnRate);
                        AccessTools.Field(typeof(CharacterManager.Config), "NeverSpawnPatients")
                            ?.SetValue(characterClone, header.neverSpawnPatients);
                        AccessTools.Field(typeof(CharacterManager.Config), "_usePatientSelector")
                            ?.SetValue(characterClone, header.usePatientSelector);
                        AccessTools.Field(typeof(CharacterManager.Config),
                                "_patientSelectionRandomJitter")
                            ?.SetValue(characterClone, header.patientSelectionRandomJitter);
                    }
                    _runtimeCharacterWrapper = ScriptableObject.CreateInstance<
                        UnderPressureCharacterConfigInstance>();
                    _runtimeCharacterWrapper.name = "UP Scenario Character Config";
                    _runtimeCharacterWrapper.hideFlags = HideFlags.DontSave;
                    _runtimeCharacterWrapper.Instance = characterClone;
                    AccessTools.Field(typeof(LevelConfig), "CharacterManagerConfig")?.SetValue(
                        clone, _runtimeCharacterWrapper);
                }
            }
            if (header.objectivesConfigured)
            {
                var levelScript = ScenarioObjectiveCatalog.ApplyStars(config,
                    header.objectives, RuntimeScenarioAssets);
                if (levelScript != null)
                    AccessTools.Field(typeof(LevelConfig), "LevelScriptConfig")
                        ?.SetValue(clone, levelScript);
            }
            var challengeConfig = config.GetChallengeManagerConfig();
            if (header.objectivesConfigured)
                challengeConfig = ScenarioObjectiveCatalog.ApplyChallenges(config,
                    challengeConfig, header.objectives, RuntimeScenarioAssets);
            if (header.emergenciesConfigured)
            {
                var challengeManager = ScenarioEmergencyCatalog.Apply(config,
                    challengeConfig, header.emergencies, RuntimeScenarioAssets);
                if (challengeManager != null)
                    AccessTools.Field(typeof(LevelConfig), "ChallengeManagerConfig")
                        ?.SetValue(clone, challengeManager);
            }
            else if (header.objectivesConfigured)
            {
                var challengeManager = ScenarioObjectiveCatalog.WrapChallenges(
                    challengeConfig, RuntimeScenarioAssets);
                if (challengeManager != null)
                    AccessTools.Field(typeof(LevelConfig), "ChallengeManagerConfig")
                        ?.SetValue(clone, challengeManager);
            }
            if (header.availableRoomsConfigured)
                ScenarioEditorStorage.ApplyAvailableRoomsToConfig(
                    clone, header.availableRooms, RuntimeScenarioAssets);
            config = clone;
        }

        internal static void ShowIntroduction(Level level)
        {
            if (_introductionShown || !_active || level?.Advisor == null || _map?.Header == null)
                return;
            _introductionShown = true;
            var message = _map.Translations?.Resolve(
                ScenarioMapLocalization.DescriptionKey,
                EditorLocalization.CurrentLanguageCode()) ?? "";
            if (string.IsNullOrWhiteSpace(message))
                message = _map.Header.description ?? "";
            if (string.IsNullOrWhiteSpace(message)) return;
            level.Advisor.PushMessageForce(new AdvisorMessageDefinition
            {
                Message = message,
                Duration = 20f,
                ShowIndefinitely = false,
                UserCanDismiss = true,
                DisplayType = AdvisorDisplayType.Information
            }, false);
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
                var upper = (WorldState.Config)CloneMethod.Invoke(config, null);
                var lowerWrappers = new List<FullInspector.SharedInstance<HospitalPlotDefinition>>();
                var upperWrappers = new List<FullInspector.SharedInstance<HospitalPlotDefinition>>();
                var lowerDefinitions = new List<HospitalPlotDefinition>();
                var upperDefinitions = new List<HospitalPlotDefinition>();
                for (var index = 0; index < _plotAssets.Wrappers.Count; ++index)
                {
                    var wrapper = _plotAssets.Wrappers[index];
                    var definition = _plotAssets.Definitions[index];
                    if (global::UnderPressure.Storey.StoreyApi.GetStorey(definition) ==
                        global::UnderPressure.Storey.StoreyApi.UpperStorey)
                    {
                        upperWrappers.Add(wrapper);
                        upperDefinitions.Add(definition);
                    }
                    else
                    {
                        lowerWrappers.Add(wrapper);
                        lowerDefinitions.Add(definition);
                    }
                }
                cloned.HospitalPlots = lowerWrappers;
                upper.HospitalPlots = upperWrappers;
                // RoomFloorPlanVisual no crea los VISUALES de la capa Base
                // cuando este flag es false (valor nativo de muchos hospitales).
                // La escena original los contiene precocinados para el juego base,
                // pero el mapa personalizado debe mostrar sus propias modificaciones.
                cloned.createBaseLandscapeItems = true;
                // El segundo mapa contiene exclusivamente sus parcelas. No crea
                // terreno ni paisaje propios; su presentación completa se
                // desplaza después a +2 m por UnderPressure.Storey.
                upper.createBaseLandscapeItems = false;
                if (!string.IsNullOrEmpty(_map.Header.fogOfWarFile))
                {
                    _runtimeFog = ScenarioEditorFogOfWar.CreateDefinition(
                        _map.Header, _map.FogOfWarPng);
                    cloned.FogOfWarDefinition = _runtimeFog;
                }
                if (_map.Header.environmentSettingsConfigured)
                {
                    cloned.initialTemperature = _map.Header.initialTemperature;
                    cloned.initialAttractiveness = _map.Header.initialAttractiveness;
                    cloned.initialHygiene = _map.Header.initialHygiene;
                }
                ScenarioHospitalStyles.Apply(level, _map.Header, cloned, lowerDefinitions);
                ScenarioHospitalStyles.Apply(level, _map.Header, upper, upperDefinitions);
                global::UnderPressure.Storey.StoreyApi.PrepareUpperWorld(
                    level, upperWrappers.Count == 0 ? null : upper);
                config = cloned;
                _active = true;
                _worldConfigApplied = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: mapas preparados con " + lowerWrappers.Count +
                    " parcelas inferiores y " + upperWrappers.Count + " superiores.");
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
            if (!_trafficApplied)
            {
                var trafficFile = _map?.Header?.trafficFile;
                if (!string.IsNullOrEmpty(trafficFile))
                    ScenarioTraffic.ApplyRuntime(Path.Combine(_map.ContentFolder, trafficFile));
                _trafficApplied = true;
            }
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
            foreach (var plot in level == null
                         ? new List<HospitalPlot>()
                         : global::UnderPressure.Storey.StoreyApi.GetPlots(level))
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
        private static void Postfix(Level __0, bool __1)
        {
            ScenarioCareerMapLoader.LoadTerrain(__0);
            if (!__1) ScenarioCareerMapLoader.ShowIntroduction(__0);
        }
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
                foreach (var definition in Definitions)
                    global::UnderPressure.Storey.StoreyApi.UnregisterDefinition(definition);
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
                    if (model.storey != global::UnderPressure.Storey.StoreyApi.LowerStorey &&
                        model.storey != global::UnderPressure.Storey.StoreyApi.UpperStorey)
                        throw new InvalidDataException(
                            "Planta invalida en la parcela " + index + ": " + model.storey);
                    global::UnderPressure.Storey.StoreyApi.RegisterDefinition(
                        definition, model.storey);
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
                    if (merged >= 0)
                    {
                        if (config.storey != map.Plots[merged].Data.storey)
                            throw new InvalidDataException(
                                "No se pueden fusionar parcelas de plantas distintas: " + i);
                        result.Definitions[i].MergeInto = result.Wrappers[merged];
                    }
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
                if (!result.Definitions.Any(definition =>
                        global::UnderPressure.Storey.StoreyApi.GetStorey(definition) ==
                        global::UnderPressure.Storey.StoreyApi.LowerStorey))
                    throw new InvalidDataException(
                        "El mapa debe conservar al menos una parcela inferior.");
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
