// Updated: 2026-10-15 - Reparado guardado Landscape: arrays objects y added escritos y verificados individualmente.
// Mantiene el contrato del mapa .upmap, sus PNG y las capas Base/Built/Unbuilt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioMapFormat
    {
        internal const int Version = 2;
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

        [Serializable]
        internal sealed class Map
        {
            public int version = Version;
            public string name = "";
            public string baseHospitalId = "";
            public string baseHospitalName = "";
            public string description = "";
            public string DisplayName = "";
            public string[] DescriptionPerStar = new string[0];
            public int InitialBalance = 1000000;
            public int FailStateBalanceWarning = -150000;
            public int FailStateBalanceGameOver = -300000;
            public bool scenarioSettingsConfigured;
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
            public PlotReference[] plots = new PlotReference[0];
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
            internal Scene SceneData = new Scene();
            internal readonly List<LoadedPlot> Plots = new List<LoadedPlot>();
            internal byte[] FogOfWarPng;
        }

        internal static Map ReadHeader(string file)
        {
            var json = ReadText(file, MaxHeaderBytes);
            var header = JsonUtility.FromJson<Map>(json);
            if (header != null) header.plots = ParseObjects<PlotReference>(json, "plots");
            if (header != null) header.plotPerimeter = NormalizePerimeter(header.plotPerimeter);
            if (header != null && header.perimeterPrefabs == null)
                header.perimeterPrefabs = new string[0];
            if (header == null || header.version != Version ||
                string.IsNullOrWhiteSpace(header.name) ||
                string.IsNullOrWhiteSpace(header.baseHospitalId) ||
                header.plots == null || header.plots.Length == 0 || header.plots.Length > 128 ||
                header.perimeterPrefabs.Length > 32 ||
                HasInvalidStyleId(header.builtRoomStyle) ||
                HasInvalidStyleId(header.buildingRoomStyle) ||
                HasInvalidStyleId(header.unbuiltRoomStyle) ||
                HasInvalidStyleId(header.sceneFile) ||
                HasInvalidStyleId(header.fogOfWarFile) ||
                Array.Exists(header.perimeterPrefabs, HasInvalidStyleId))
                throw new InvalidDataException("Cabecera .upmap incorrecta o version incompatible.");
            return header;
        }

        private static bool HasInvalidStyleId(string value)
        {
            return value != null && (value.Length > 160 || value.IndexOfAny(new[] {'\r', '\n'}) >= 0);
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
            scene.items = ParseObjects<Item>(json, "items");
            scene.sourceChanges = json.Contains("\"sourceChanges\"")
                ? ParseObjects<OriginalChange>(json, "sourceChanges")
                : new OriginalChange[0];
            return scene;
        }

        internal static LoadedMap Read(string file)
        {
            var header = ReadHeader(file);
            var result = new LoadedMap { Header = header };
            var folder = Path.GetDirectoryName(Path.GetFullPath(file));
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
            plot.baseItems = ParseCompactPlotItems(json, "baseItems");
            plot.builtItems = ParseCompactPlotItems(json, "builtItems");
            plot.unbuiltItems = ParseCompactPlotItems(json, "unbuiltItems");
            var rawPrerequisites = ArrayElements(json, "prerequisites");
            var requirements = new List<PlotPrerequisite>();
            foreach (var raw in rawPrerequisites)
            {
                var requirement = JsonUtility.FromJson<PlotPrerequisite>(raw);
                if (requirement == null)
                    throw new InvalidDataException("Requisito de parcela ilegible.");
                var indices = ArrayElements(raw, "anyBought");
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
            return ReplaceArray(JsonUtility.ToJson(map, true), "plots", SerializeObjects(map.plots));
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
            var text = plot != null ? SerializePlot(plot) :
                       map != null ? SerializeMap(map) :
                       scene != null ? SerializeScene(scene) :
                       landscape != null ? SerializeLandscape(landscape) :
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
