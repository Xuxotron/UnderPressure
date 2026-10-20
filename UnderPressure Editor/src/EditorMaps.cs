// Updated: 2026-10-15 - Alt transfiere sin duplicar y sin alterar Ctrl multiple.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using FullInspector;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TH20;
using TMPro;
using UnderPressure.LoaderRuntime;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioEditorStorage.cs =====
// Updated: 2026-10-14 - Desactivar Escenario experimental y restaurar edicion nativa de parcelas.
// Fix: cerrar dialogo siempre y guardar identidad de prefab.
// Crear parcelas desde subcursores y guardar capas JSON vacias sin perder objetos.
// Alta de parcelas no destructiva: mantiene originales y registra controles nativos.
// Mapas .upmap JSON + archivos plot_XX.json y plot_XX.png por parcela.
// Sin lectura de los formatos antiguos de prueba.
namespace UnderPressure.Editor
{
    internal static class ScenarioEditorStorage
    {
        private static ScenarioPlotRuntime.Collection _currentCustomPlots;
        private static readonly List<string> PlotNames = new List<string>();
        // Nombre de la sesion actual: solo se actualiza despues de un guardado
        // o de una carga completos, nunca al abrir un cuadro de dialogo.
        private static Level _namedMapLevel;
        private static string _namedMap;
        private static Level _architectLevel;
        private static string _architectName;
        private static Level _perimeterModeLevel;
        private static string _perimeterMode = ScenarioMapFormat.PerimeterNone;
        private static string[] _perimeterPrefabs = new string[0];
        private static string _builtRoomStyle = "";
        private static string _buildingRoomStyle = "";
        private static string _unbuiltRoomStyle = "";

        internal static string CurrentMapName(Level level)
        {
            return ReferenceEquals(_namedMapLevel, level) &&
                   !string.IsNullOrWhiteSpace(_namedMap)
                ? _namedMap : EditorLocalization.Get("scenario_editor.new_map");
        }

        internal static string NameForSave(Level level)
        {
            return ReferenceEquals(_namedMapLevel, level) ? _namedMap ?? "" : "";
        }

        internal static string CurrentMapFolder(Level level)
        {
            var name = NameForSave(level);
            if (string.IsNullOrWhiteSpace(name)) return null;
            return Path.Combine(Folder, SafeName(CurrentArchitect(level)), SafeName(name));
        }

        internal static string FolderForArchitect(string architect)
        {
            var name = string.IsNullOrWhiteSpace(architect)
                ? UnderPressureEditorPlugin.ArchitectName : architect.Trim();
            return Path.Combine(Folder, SafeName(name));
        }

        internal static bool TryReadCurrentFog(Level level,
            out ScenarioMapFormat.Map header, out byte[] png)
        {
            header = null;
            png = null;
            var name = NameForSave(level);
            if (string.IsNullOrWhiteSpace(name)) return false;
            var folder = Path.Combine(Folder, SafeName(CurrentArchitect(level)), SafeName(name));
            var main = Path.Combine(folder, SafeName(name) + ".upmap");
            if (!File.Exists(main)) return false;
            try
            {
                var loaded = ScenarioMapFormat.Read(main);
                header = loaded.Header;
                png = loaded.FogOfWarPng;
                return header != null && !string.IsNullOrEmpty(header.fogOfWarFile) &&
                       png != null && png.Length >= 24;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Fog of War: no se pudo releer el último guardado: " + exception.Message);
                header = null;
                png = null;
                return false;
            }
        }

        private static void MarkCurrentMap(Level level, string name)
        {
            _namedMapLevel = level;
            _namedMap = name;
        }

        internal static void BeginNewMap(Level level, string architect)
        {
            _architectLevel = level;
            _architectName = string.IsNullOrWhiteSpace(architect)
                ? UnderPressureEditorPlugin.ArchitectName : architect.Trim();
        }

        private static string CurrentArchitect(Level level)
        {
            return ReferenceEquals(_architectLevel, level) &&
                   !string.IsNullOrWhiteSpace(_architectName)
                ? _architectName : UnderPressureEditorPlugin.ArchitectName;
        }
        internal static string CurrentPerimeterMode(Level level)
        {
            if (!ReferenceEquals(_perimeterModeLevel, level))
            {
                _perimeterModeLevel = level;
                _perimeterMode = ScenarioMapFormat.PerimeterNone;
                _perimeterPrefabs = new string[0];
                _builtRoomStyle = "";
                _buildingRoomStyle = "";
                _unbuiltRoomStyle = "";
            }
            return _perimeterMode;
        }

        internal static void SetPerimeterMode(Level level, string mode)
        {
            _perimeterModeLevel = level;
            _perimeterMode = ScenarioMapFormat.NormalizePerimeter(mode);
        }

        internal static ScenarioMapFormat.Map CurrentStyles(Level level)
        {
            CurrentPerimeterMode(level);
            return new ScenarioMapFormat.Map
            {
                plotPerimeter = _perimeterMode,
                perimeterPrefabs = (string[])_perimeterPrefabs.Clone(),
                builtRoomStyle = _builtRoomStyle,
                buildingRoomStyle = _buildingRoomStyle,
                unbuiltRoomStyle = _unbuiltRoomStyle
            };
        }

        internal static void SetStyles(Level level, ScenarioMapFormat.Map source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _perimeterModeLevel = level;
            _perimeterMode = ScenarioMapFormat.NormalizePerimeter(source.plotPerimeter);
            _perimeterPrefabs = source.perimeterPrefabs == null
                ? new string[0] : (string[])source.perimeterPrefabs.Clone();
            _builtRoomStyle = source.builtRoomStyle ?? "";
            _buildingRoomStyle = source.buildingRoomStyle ?? "";
            _unbuiltRoomStyle = source.unbuiltRoomStyle ?? "";
        }

        private static readonly System.Reflection.MethodInfo CloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo WorldConfigField =
            AccessTools.Field(typeof(WorldState), "_config");
        private static readonly System.Reflection.FieldInfo ItemsField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "Items");
        private static readonly System.Reflection.FieldInfo BuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "BuiltItems");
        private static readonly System.Reflection.FieldInfo UnbuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "UnbuiltItems");

        internal sealed class MapInfo
        {
            internal string Path;
            internal string Name;
            internal string BaseId;
            internal string BaseName;
            internal bool Valid;
            internal DateTime ModifiedUtc;
        }

        // Raiz unica: <juego>/Mods/maps, compartida por Editor y modo Carrera.
        // Migra las dos ubicaciones de prueba sin sobrescribir los mapas existentes.
        private static bool _checkedLegacyFolders;
        internal static string Folder
        {
            get
            {
                var target = Path.Combine(Paths.PluginPath, "maps");
                if (!_checkedLegacyFolders)
                {
                    _checkedLegacyFolders = true;
                    MigrateLegacyMaps(target);
                }
                return target;
            }
        }

        private static void MigrateLegacyMaps(string target)
        {
            var oldFolders = new[]
            {
                Path.Combine(Paths.PluginPath, "UnderPressure", "maps"),
                Path.Combine(Paths.ConfigPath, "UnderPressure", "maps")
            };
            foreach (var old in oldFolders)
            {
                if (!Directory.Exists(old) ||
                    string.Equals(Path.GetFullPath(old), Path.GetFullPath(target),
                        StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    Directory.CreateDirectory(target);
                    // El juego crea maps/<hospital>/<mapa>/<mapa>.upmap.
                    // No se copia ni borra jamas un mapa que ya exista en destino.
                    foreach (var hospital in Directory.GetDirectories(old))
                    {
                        var toHospital = Path.Combine(target, Path.GetFileName(hospital));
                        if (!Directory.Exists(toHospital))
                        {
                            Directory.Move(hospital, toHospital);
                            continue;
                        }
                        foreach (var map in Directory.GetDirectories(hospital))
                        {
                            var toMap = Path.Combine(toHospital, Path.GetFileName(map));
                            if (!Directory.Exists(toMap)) Directory.Move(map, toMap);
                            else UnderPressureEditorPlugin.Log?.LogWarning(
                                "Mapa conservado en ruta anterior (destino ya existe): " + map);
                        }
                        // Mapas sueltos: mover solo los ficheros que no existen.
                        foreach (var file in Directory.GetFiles(hospital))
                        {
                            var toFile = Path.Combine(toHospital, Path.GetFileName(file));
                            if (!File.Exists(toFile)) File.Move(file, toFile);
                        }
                        if (Directory.GetFileSystemEntries(hospital).Length == 0)
                            Directory.Delete(hospital);
                    }
                    foreach (var file in Directory.GetFiles(old))
                    {
                        var toFile = Path.Combine(target, Path.GetFileName(file));
                        if (!File.Exists(toFile)) File.Move(file, toFile);
                    }
                    if (Directory.GetFileSystemEntries(old).Length == 0)
                        Directory.Delete(old);
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Mapas migrados de " + old + " a " + target);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "No se pudieron trasladar todos los mapas de " + old + ": " + exception);
                }
            }
        }

        internal static List<MapInfo> ListSaved()
        {
            var result = new List<MapInfo>();
            if (!Directory.Exists(Folder)) return result;
            foreach (var path in Directory.GetFiles(Folder, "*.upmap", SearchOption.AllDirectories))
                result.Add(ReadInfo(path));
            // La fecha real del .upmap cambia al volver a guardar, sin cambiar el JSON.
            result.Sort((left, right) =>
            {
                var cmp = right.ModifiedUtc.CompareTo(left.ModifiedUtc);
                return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });
            return result;
        }

        private static MapInfo ReadInfo(string path)
        {
            var result = new MapInfo
            {
                Path = path, Name = Path.GetFileNameWithoutExtension(path),
                BaseId = "", BaseName = "Desconocido", Valid = false,
                ModifiedUtc = File.GetLastWriteTimeUtc(path)
            };
            try
            {
                var header = ScenarioMapFormat.ReadHeader(path);
                result.Name = header.name;
                result.BaseId = header.baseHospitalId;
                result.BaseName = header.baseHospitalName;
                result.Valid = true;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Mapa ilegible " + path + ": " + ex.Message);
            }
            return result;
        }

        internal static string Save(Level level, string displayName)
        {
            RequireEditor(level);
            // Una seleccion multiple se retira temporalmente de la parcela
            // mientras sigue pegada al cursor. Guardar entonces perderia objetos.
            // Los cursores retiran temporalmente los objetos que se estan moviendo
            // de FloorPlan y de HospitalPlotDefinition. No guardar hasta confirmar.
            if (ScenarioEditorScene.IsDragging)
                throw new InvalidOperationException(
                    "Termina de colocar el objeto de Escenario antes de guardar.");
            if (level.CursorManager != null &&
                (level.CursorManager.IsModeActive<CursorRoomMove>() ||
                 level.CursorManager.IsModeActive<CursorRoomItem>()))
                throw new InvalidOperationException(
                    "Termina de colocar o mover los objetos antes de guardar el mapa.");
            var name = (displayName ?? "").Trim();
            if (name.Length == 0 || name.Length > 80)
                throw new ArgumentException("Introduce un nombre de mapa de 1 a 80 caracteres.");
            var baseId = level.Config.UniqueId;
            var baseName = level.Config.GetLocalisedDisplayName();
            var folder = Path.Combine(Folder, SafeName(CurrentArchitect(level)), SafeName(name));
            Directory.CreateDirectory(folder);
            var main = Path.Combine(folder, SafeName(name) + ".upmap");
            var originalDescription = name;
            if (File.Exists(main))
            {
                try
                {
                    var existing = ScenarioMapFormat.ReadHeader(main);
                    if (!string.IsNullOrEmpty(existing.description)) originalDescription = existing.description;
                }
                catch (Exception) { /* Los datos de prueba no se importan. */ }
            }
            var worldPlots = level.WorldState.HospitalPlots;
            var refs = new List<ScenarioMapFormat.PlotReference>();
            var saved = new List<Tuple<string, ScenarioMapFormat.Plot, string, byte[]>>();
            for (var i = 0; i < worldPlots.Count; ++i)
            {
                var definition = worldPlots[i]?.Definition;
                if (definition?.FloorImage == null)
                    throw new InvalidOperationException("Parcela " + i + " sin imagen de suelo.");
                var filename = "plot_" + i.ToString("D2");
                var plot = new ScenarioMapFormat.Plot
                {
                    index = i,
                    name = i < PlotNames.Count && !string.IsNullOrEmpty(PlotNames[i])
                        ? PlotNames[i] : EditorLocalization.Format("editor.plot.default_name", i + 1),
                    nameTerm = definition.NameLocalised.Term,
                    cost = definition.Cost,
                    color = definition.Color,
                    initiallyOpen = definition.InitiallyOpen,
                    built = definition.Built,
                    instaBuild = definition.InstaBuild,
                    hideUntilAvailable = definition.HideUntilAvailable,
                    notHiddenInSandbox = definition.NotHiddenInSandbox,
                    includePerimiterInNavMesh = definition.IncludePerimiterInNavMesh,
                    useEnergyUI = definition.UseEnergyUI,
                    energyUnitsGenerated = definition.EnergyUnitsGenerated,
                    timeToBuild = definition.TimeToBuild,
                    footprintYOffset = definition.FootprintYOffset,
                    ambulanceBayEntranceSide = (int)definition.AmbulanceBayEntranceSide,
                    buildObjectiveAutoStart = definition.BuildObjectiveAutoStart,
                    buildObjectiveStartOnPrereqsMet = definition.BuildObjectiveStartOnPrereqsMet,
                    mergeInto = GetMergeIndex(worldPlots, definition),
                    prerequisites = SavePrerequisites(worldPlots, definition),
                    baseItems = SaveLiveItems(worldPlots[i], HospitalPlotLayer.Base),
                    builtItems = SaveLiveItems(worldPlots[i], HospitalPlotLayer.Built),
                    unbuiltItems = SaveLiveItems(worldPlots[i], HospitalPlotLayer.Unbuilt)
                };
                var jsonName = filename + ".json";
                var pngName = filename + ".png";
                byte[] png;
                try { png = ImageConversion.EncodeToPNG(definition.FloorImage); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("No se pudo exportar la imagen de la parcela " + i, ex);
                }
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("PNG de parcela vacio: " + i);
                refs.Add(new ScenarioMapFormat.PlotReference
                {
                    index = i, name = plot.name, dataFile = jsonName, imageFile = pngName
                });
                saved.Add(Tuple.Create(jsonName, plot, pngName, png));
            }
            if (refs.Count == 0) throw new InvalidOperationException("El hospital no tiene parcelas.");
            foreach (var entry in saved)
            {
                ScenarioMapFormat.WriteJson(Path.Combine(folder, entry.Item1), entry.Item2);
                ScenarioMapFormat.WriteBytes(Path.Combine(folder, entry.Item3), entry.Item4);
            }
            // Los objetos exteriores se guardan aparte de las parcelas.
            // Captura de solo lectura: NO altera ni duplica la escena de Unity.
            // Una revision del mismo mapa conserva las alteraciones ya existentes.
            var landscapePath = Path.Combine(folder, ScenarioLandscape.FileName);
            var previousLandscape = File.Exists(landscapePath)
                ? ScenarioLandscape.Read(landscapePath) : null;
            var landscape = ScenarioLandscape.Capture(previousLandscape);
            ScenarioMapFormat.WriteJson(landscapePath, landscape);
            var verifiedLandscape = ScenarioLandscape.Read(landscapePath);
            if (verifiedLandscape.objects.Length != landscape.objects.Length ||
                verifiedLandscape.added.Length != landscape.added.Length)
                throw new InvalidDataException("El inventario Landscape no coincide con el guardado.");

            // Escenario experimental desactivado: scene.json NO se utiliza.
            var header = new ScenarioMapFormat.Map
            {
                sceneFile = "",
                landscapeFile = ScenarioLandscape.FileName,
                name = name,
                baseHospitalId = baseId,
                baseHospitalName = baseName,
                description = originalDescription,
                terrainManifest = ScenarioEditorTerrainPainter.GetManifest(level),
                plots = refs.ToArray()
            };
            var styles = CurrentStyles(level);
            header.plotPerimeter = styles.plotPerimeter;
            header.perimeterPrefabs = styles.perimeterPrefabs;
            header.builtRoomStyle = styles.builtRoomStyle;
            header.buildingRoomStyle = styles.buildingRoomStyle;
            header.unbuiltRoomStyle = styles.unbuiltRoomStyle;
            // La máscara conserva su resolución nativa (128 o 256) y la
            // definición se registra en la cabecera; no se genera ningún .asset.
            ScenarioEditorFogOfWar.Export(folder, header);
            // Verificar TODAS las parcelas antes de publicar el indice. Nunca
            // informar de un guardado correcto si ha desaparecido una capa.
            foreach (var entry in saved)
            {
                var filename = Path.Combine(folder, entry.Item1);
                // Unity omite ocasionalmente los arrays vacios al serializar JSON.
                // El formato fuerza su presencia y normaliza solo los declarados [].
                var check = ScenarioMapFormat.ReadPlot(filename);
                if (check.index != entry.Item2.index ||
                    check.baseItems.Length != entry.Item2.baseItems.Length ||
                    check.builtItems.Length != entry.Item2.builtItems.Length ||
                    check.unbuiltItems.Length != entry.Item2.unbuiltItems.Length)
                    throw new InvalidDataException("Los objetos de " + entry.Item1 +
                        " no coinciden con lo exportado.");
                VerifySavedItems(entry.Item2.baseItems, check.baseItems, entry.Item1, "Base");
                VerifySavedItems(entry.Item2.builtItems, check.builtItems, entry.Item1, "Built");
                VerifySavedItems(entry.Item2.unbuiltItems, check.unbuiltItems, entry.Item1, "Unbuilt");
            }
            // Guardar .upmap al final: nunca publicarlo antes de escribir los datos referenciados.
            ScenarioMapFormat.WriteJson(main, header);
            var verified = ScenarioMapFormat.Read(main);
            if (verified.Plots.Count != saved.Count)
                throw new InvalidDataException("El mapa guardado no coincide con las parcelas del Editor.");
            UnderPressureEditorPlugin.Log?.LogInfo("Mapa exportado y verificado: " + main + " (" + refs.Count + " parcelas)");
            MarkCurrentMap(level, name);
            return main;
        }

        // Comparar tambien identidad y transformacion, no solo cantidades.
        // De lo contrario un archivo podria contener N objetos vacios y superar
        // la verificacion pese a haber perdido posiciones o rotaciones.
        private static void VerifySavedItems(ScenarioMapFormat.Item[] before,
            ScenarioMapFormat.Item[] after, string file, string layer)
        {
            for (var i = 0; i < before.Length; i++)
            {
                var a = before[i];
                var b = after[i];
                if (a == null || b == null || a.guid != b.guid || a.tag != b.tag || a.prefab != b.prefab ||
                    Math.Abs(a.x - b.x) > 0.001f || Math.Abs(a.y - b.y) > 0.001f ||
                    Math.Abs(a.z - b.z) > 0.001f || Math.Abs(a.rotation - b.rotation) > 0.001f)
                    throw new InvalidDataException("Error al verificar objeto " + i +
                        " de " + layer + " en " + file);
            }
        }

        private static int GetMergeIndex(IList<HospitalPlot> all, HospitalPlotDefinition definition)
        {
            var target = definition.MergeInto?.Instance;
            if (target == null) return -1;
            for (var i = 0; i < all.Count; ++i)
                if (ReferenceEquals(all[i]?.Definition, target)) return i;
            return -1;
        }

        private static ScenarioMapFormat.PlotPrerequisite[] SavePrerequisites(
            IList<HospitalPlot> plots, HospitalPlotDefinition definition)
        {
            var output = new List<ScenarioMapFormat.PlotPrerequisite>();
            if (definition.Prerequisites == null) return output.ToArray();
            var sourceField = AccessTools.Field(typeof(HospitalPlotPrerequisitePlotBought), "_plot");
            foreach (var requirement in definition.Prerequisites)
            {
                var owned = requirement as HospitalPlotPrerequisitePlotBought;
                if (owned == null)
                    throw new InvalidDataException("Tipo de requisito de parcela no reconocido.");
                var targets = sourceField?.GetValue(owned) as SharedInstance<HospitalPlotDefinition>[];
                if (targets == null) throw new InvalidDataException("Requisito de parcela incompleto.");
                var indices = new List<int>();
                foreach (var target in targets)
                {
                    var found = -1;
                    for (var i = 0; i < plots.Count; ++i)
                        if (ReferenceEquals(plots[i].Definition, target?.Instance)) { found = i; break; }
                    if (found < 0)
                        throw new InvalidDataException("Requisito que referencia una parcela ajena al mapa.");
                    indices.Add(found);
                }
                output.Add(new ScenarioMapFormat.PlotPrerequisite { anyBought = indices.ToArray() });
            }
            return output.ToArray();
        }

        // El Cursor nativo edita FloorPlan.LandscapeItems en tiempo real. La lista
        // de HospitalPlotDefinition puede estar desfasada durante una operacion
        // de colocar/mover/borrar. Nunca exportar una capa visible desde esa copia.
        // Si la capa no esta visible, conservamos sus objetos guardados en la definicion.
        private static ScenarioMapFormat.Item[] SaveLiveItems(HospitalPlot plot, HospitalPlotLayer layer)
        {
            var definition = plot?.Definition;
            if (definition == null) throw new InvalidDataException("Parcela sin definicion.");
            var stored = definition.GetItems(layer);
            var map = plot.HospitalMap;
            if (map?.FloorPlan == null || !plot.IsLayerVisible(layer))
                return SaveItems(stored);

            var result = new List<ScenarioMapFormat.Item>();
            // Objetos fijos no-landscape no llevan la propiedad Layer en FloorPlan.
            // Los conservamos en su capa original (la interfaz nativa de decoracion
            // manipula LandscapeRoomItem, no estos otros objetos).
            if (stored != null)
                foreach (var item in stored)
                {
                    var itemDefinition = item?.Definition?.Instance;
                    if (itemDefinition == null)
                        throw new InvalidDataException("Referencia de objeto vacia en parcela.");
                    if (itemDefinition.ItemType != RoomItemDefinition.Type.Landscape)
                        result.Add(SaveItem(itemDefinition, item.Position, item.Rotation));
                }

            var visibleLandscapeCount = 0;
            foreach (var sceneItem in map.FloorPlan.LandscapeItems)
            {
                if (sceneItem == null || sceneItem.Layer != layer) continue;
                visibleLandscapeCount++;
                var itemDefinition = sceneItem.Definition as RoomItemDefinition;
                if (itemDefinition == null)
                    throw new InvalidDataException("Objeto de escena sin definicion en parcela.");
                result.Add(SaveItem(itemDefinition, sceneItem.LocalPosition, sceneItem.Rotation));
            }
            // Si la definicion aun conserva decoracion y la escena no contiene
            // ninguno de esos objetos, la representacion visual no esta lista.
            // No sobrescribir una lista con objetos por una lista accidentalmente vacia.
            if (visibleLandscapeCount == 0 && stored != null && stored.Any(item =>
                    item?.Definition?.Instance?.ItemType == RoomItemDefinition.Type.Landscape) &&
                !ScenarioEditorScene.WasPlotLayerModified(plot, layer))
                throw new InvalidOperationException("No se pueden leer los objetos de la capa " +
                    layer + " de la parcela " + definition.NameLocalised.Term +
                    ". Cambia de parcela y vuelve a seleccionar antes de guardar.");
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Parcela " + definition.NameLocalised.Term + " / " + layer +
                ": " + result.Count + " objetos exportados desde la escena.");
            return result.ToArray();
        }

        private static ScenarioMapFormat.Item SaveItem(RoomItemDefinition definition, Vector3 position, float rotation)
        {
            if (definition == null) throw new InvalidDataException("Objeto sin definicion.");
            if (definition.GUID == Guid.Empty && string.IsNullOrEmpty(definition.DebugTag))
                throw new InvalidDataException("Un objeto no tiene GUID ni DebugTag; no se puede guardar sin perderlo.");
            return new ScenarioMapFormat.Item
            {
                guid = definition.GUID.ToString("D"), tag = definition.DebugTag ?? "",
                prefab = GetPrefabIdentity(definition),
                x = position.x, y = position.y, z = position.z, rotation = rotation
            };
        }

        private static string GetPrefabIdentity(RoomItemDefinition definition)
        {
            // Algunos items nativos no tienen prefab propio; no bloquear el guardado.
            try
            {
                var prefab = definition.GetPrefab(0);
                return prefab != null ? prefab.name ?? "" : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static ScenarioMapFormat.Item[] SaveItems(List<HospitalPlotItem> source)
        {
            var result = new List<ScenarioMapFormat.Item>();
            if (source == null) return result.ToArray();
            foreach (var item in source)
            {
                var definition = item?.Definition?.Instance;
                if (definition == null)
                    throw new InvalidDataException("Objeto sin definicion durante el guardado.");
                result.Add(SaveItem(definition, item.Position, item.Rotation));
            }
            return result.ToArray();
        }

        internal static void Load(Level level, string path)
        {
            RequireEditor(level);
            var full = Path.GetFullPath(path);
            var allowed = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".upmap", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Solo se pueden abrir mapas de la carpeta maps.");
            var map = ScenarioMapFormat.Read(full);
            if (!string.Equals(map.Header.baseHospitalId, level.Config.UniqueId, StringComparison.Ordinal))
                throw new InvalidDataException("El mapa corresponde al hospital " + map.Header.baseHospitalName);
            var world = level.WorldState;
            var templates = level.Config.GetWorldStateConfig()?.HospitalPlots;
            var prepared = ScenarioPlotRuntime.Build(map, templates, level.App.Metagame);
            // No importar decoracion global experimental.
            // Restaurar cualquier resto de una sesion anterior antes de editar.
            ScenarioEditorScene.Clear();
            try
            {
                // Compatibilidad con parcelas antiguas: RGB negro es zona externa,
                // pero algunos PNG de prueba lo guardaban con alfa 255 y tapaban Tile View.
                foreach (var floor in prepared.Textures)
                    MakeOutsideTransparent(floor);
                // El editor parte de un hospital ya creado: el lienzo del PNG debe coincidir
                // con el de la escena existente. La silueta puede cambiar libremente.
                var originalTexture = templates?[0]?.Instance?.FloorImage;
                if (originalTexture == null || prepared.Textures[0].width != originalTexture.width ||
                    prepared.Textures[0].height != originalTexture.height)
                    throw new InvalidDataException("El PNG debe conservar el tamano del lienzo base del hospital.");
                StopActiveEditorTools(level);
                var previousStyles = CurrentStyles(level);
            var originalDefinitions = world.HospitalPlots.Select(plot => plot.Definition).ToList();
                try
                {
                    ReplacePlots(level, prepared.Definitions, map.Header);
                    RebindNativeEditorPlots(level);
                }
                catch
                {
                    try
                    {
                        ReplacePlots(level, originalDefinitions, previousStyles);
                        RebindNativeEditorPlots(level);
                    }
                    catch (Exception ex)
                    {
                        UnderPressureEditorPlugin.Log?.LogError("No se pudo restaurar la escena original: " + ex);
                    }
                    throw;
                }
                var previous = _currentCustomPlots;
                _currentCustomPlots = prepared;
                PlotNames.Clear();
                foreach (var entry in map.Plots)
                    PlotNames.Add(string.IsNullOrWhiteSpace(entry.Data.name)
                        ? EditorLocalization.Format("editor.plot.default_name", entry.Data.index + 1)
                        : entry.Data.name);
                RefreshPlotButtons(level);
                // La seleccion debe apuntar a la NUEVA instancia, nunca a una
                // de las parcelas destruidas por ReplacePlots.
                var selected = AccessTools.Field(typeof(CursorEditHospital), "_hospitalPlot");
                CursorEditHospital activeCursor;
                if (level.CursorManager.TryGetActiveMode<CursorEditHospital>(out activeCursor) &&
                    !ReferenceEquals(selected?.GetValue(activeCursor), world.HospitalPlots[0]))
                    throw new InvalidOperationException("La seleccion de parcelas no se ha sincronizado.");
                previous?.DestroyAssets();
                world.CalculateLighting();
                world.UpdateNavigation();
                UnderPressureEditorPlugin.Log?.LogInfo("Mapa cargado con " + world.HospitalPlots.Count + " parcelas: " + full);
                SetStyles(level, map.Header);
                ScenarioEditorFogOfWar.ApplyLoaded(map.Header, map.FogOfWarPng);
                // Evitar que decoracion original importada vuelva a mezclarse
                // con objetos de parcelas. Solo permanecen objetos nativos.
                ScenarioEditorFixedGeometry.Scan(level, true);
                var landscapeFile = string.IsNullOrEmpty(map.Header.landscapeFile)
                    ? ScenarioLandscape.FileName : map.Header.landscapeFile;
                ScenarioLandscape.SetMapFile(Path.Combine(
                    Path.GetDirectoryName(full), landscapeFile));
                ScenarioEditorStorageMenu.NotifyLoadedMap();
                MarkCurrentMap(level, map.Header.name);
                _architectLevel = level;
                _architectName = ArchitectFromPath(full);
            }
            catch
            {
                if (!ReferenceEquals(_currentCustomPlots, prepared)) prepared.DestroyAssets();
                throw;
            }
        }

        // Una parcela Unbuilt crea las vallas nativas durante su constructor.
        // En el Editor construimos su representacion directamente como Built,
        // sin tocar el estado guardado en .json / .upmap. El propio cursor nativo
        // se encargara despues de BuyAndBuildImmediately() y Close().
        private static HospitalPlot CreateEditorPlotWithoutUnbuiltFences(
            HospitalPlotDefinition definition, WorldState world, Level level)
        {
            var savedBuilt = definition.Built;
            definition.Built = true;
            try
            {
                return new HospitalPlot(definition, world, level);
            }
            finally
            {
                definition.Built = savedBuilt;
            }
        }

        private static void ReplacePlots(Level level, IList<HospitalPlotDefinition> definitions,
            ScenarioMapFormat.Map styles)
        {
            var world = level.WorldState;
            var worldConfig = WorldConfigField?.GetValue(world) as WorldState.Config;
            if (worldConfig == null)
                throw new InvalidOperationException("No se pudo acceder a la configuracion visual del hospital.");
            var existing = world.HospitalPlots.ToArray();
            foreach (var plot in existing)
            {
                if (plot.HospitalMap != null) world.DestroyHospitalMap(plot.HospitalMap);
                plot.Destroy();
            }
            world.HospitalPlots.Clear();
            var previousPrefabs = worldConfig.PerimeterPrefabs;
            var previousBuilt = worldConfig._roomBuiltDefinition;
            var previousBuilding = worldConfig._roomBuildingDefinition;
            var previousUnbuilt = worldConfig._roomUnbuiltDefinition;
            try
            {
                ScenarioHospitalStyles.Apply(level, styles, worldConfig, definitions);
                foreach (var definition in definitions)
                {
                    var plot = new HospitalPlot(definition, world, level);
                    world.HospitalPlots.Add(plot);
                    level.HospitalEditEvents.OnHospitalPlotUpdated?.Invoke(plot);
                }
            }
            finally
            {
                worldConfig.PerimeterPrefabs = previousPrefabs;
                worldConfig._roomBuiltDefinition = previousBuilt;
                worldConfig._roomBuildingDefinition = previousBuilding;
                worldConfig._roomUnbuiltDefinition = previousUnbuilt;
            }
        }

        // La carga puede sustituir todas las parcelas de un hospital. El cursor
        // nativo mantiene sus propias referencias a las parcelas y miniaturas:
        // actualizarlas es imprescindible ANTES de permitir otra seleccion.
        private static void RebindNativeEditorPlots(Level level)
        {
            CursorEditHospital cursor;
            if (level.CursorManager == null ||
                !level.CursorManager.TryGetActiveMode<CursorEditHospital>(out cursor) || cursor == null)
                throw new InvalidOperationException("No esta activo el cursor nativo de parcelas.");
            var previewField = AccessTools.Field(typeof(CursorEditHospital), "_tileMapPreview");
            var previews = previewField?.GetValue(cursor) as Dictionary<HospitalPlot, GameObject>;
            var create = AccessTools.Method(typeof(CursorEditHospital), "CreateTilemapPreview",
                new[] { typeof(HospitalPlot) });
            var selectedField = AccessTools.Field(typeof(CursorEditHospital), "_hospitalPlot");
            if (previews == null || create == null || selectedField == null)
                throw new InvalidOperationException("No se pueden sincronizar las parcelas del Editor.");

            selectedField.SetValue(cursor, null); // El objeto anterior ya puede estar destruido.
            foreach (var preview in previews.Values)
                if (preview != null) UnityEngine.Object.Destroy(preview);
            previews.Clear();
            foreach (var plot in level.WorldState.HospitalPlots)
            {
                // Repetir SOLO el inicio nativo del Editor, no reconstruir parcelas
                // existentes ni dejar las nuevas en estado Unbuilt con vallas.
                plot.BuyAndBuildImmediately();
                plot.Close();
                create.Invoke(cursor, new object[] { plot });
                var isPreviewVisible = AccessTools.Field(typeof(CursorEditHospital),
                    "_tileMapVisible")?.GetValue(cursor);
                if (isPreviewVisible is bool && (bool)isPreviewVisible &&
                    previews.TryGetValue(plot, out var tilePreview) && tilePreview != null)
                    tilePreview.SetActive(plot.IsVisible());
            }
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Editor: controles 3D sincronizados con " + previews.Count + " parcelas.");
        }

        // Las parcelas se agregan sin reconstruir las anteriores: destruir/recrear
        // sus PlotSelectionItem dejaba sus controles inutilizables tras crear una nueva.
        internal static int AddNewPlot(Level level)
        {
            RequireEditor(level);
            var world = level.WorldState;
            var originalPlots = world.HospitalPlots.ToArray();
            if (originalPlots.Length == 0 || originalPlots.Length >= 128)
                throw new InvalidOperationException("No se pueden crear mas parcelas.");

            // El editor puede estar en la paleta de objetos (CursorEditHospitalItem):
            // TryGetActiveMode solo comprueba el modo superior de CursorManager.
            // Salir de la herramienta activa devuelve el control al cursor principal
            // SIN destruir ni reconstruir ninguna parcela existente.
            StopActiveEditorTools(level);
            // Validar controles ANTES de cambiar las definiciones del hospital.
            CursorEditHospital nativeCursor;
            if (level.CursorManager == null ||
                !level.CursorManager.TryGetActiveMode<CursorEditHospital>(out nativeCursor))
                throw new InvalidOperationException("El cursor del editor nativo no esta activo.");
            var previewMethod = AccessTools.Method(typeof(CursorEditHospital), "CreateTilemapPreview");
            var previewField = AccessTools.Field(typeof(CursorEditHospital), "_tileMapPreview");
            var previews = previewField?.GetValue(nativeCursor) as Dictionary<HospitalPlot, GameObject>;
            var menu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            var root = menu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                "_plotSelectionRoot")?.GetValue(menu) as Transform;
            var prefab = menu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                "_plotSelectionButtonPrefab")?.GetValue(menu) as GameObject;
            var colors = menu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                "_editFrameColors")?.GetValue(menu) as Color[];
            if (previewMethod == null || previews == null || root == null ||
                prefab == null || colors == null || colors.Length < 3)
                throw new InvalidOperationException("No se pudieron preparar los controles nativos de parcelas.");

            var template = originalPlots[0].Definition;
            var texture = template.FloorImage;
            if (texture == null) throw new InvalidOperationException("No hay lienzo para la nueva parcela.");
            var width = texture.width;
            var height = texture.height;
            var used = new bool[width * height];
            foreach (var plot in originalPlots)
            {
                var pixels = plot.Definition.FloorImage.GetPixels32();
                if (pixels.Length != used.Length)
                    throw new InvalidDataException("Las parcelas no comparten el mismo lienzo.");
                for (var k = 0; k < pixels.Length; k++)
                    if (pixels[k].r > 10 || pixels[k].g > 10 || pixels[k].b > 10)
                        used[k] = true;
            }
            const int size = 6;
            var distances = new int[used.Length];
            for (var k = 0; k < distances.Length; k++) distances[k] = int.MaxValue;
            var queue = new Queue<int>();
            for (var k = 0; k < used.Length; k++)
                if (used[k]) { distances[k] = 0; queue.Enqueue(k); }
            while (queue.Count != 0)
            {
                var pixel = queue.Dequeue();
                var x = pixel % width;
                var y = pixel / width;
                var nextDistance = distances[pixel] + 1;
                if (x > 0 && distances[pixel - 1] > nextDistance)
                { distances[pixel - 1] = nextDistance; queue.Enqueue(pixel - 1); }
                if (x + 1 < width && distances[pixel + 1] > nextDistance)
                { distances[pixel + 1] = nextDistance; queue.Enqueue(pixel + 1); }
                if (y > 0 && distances[pixel - width] > nextDistance)
                { distances[pixel - width] = nextDistance; queue.Enqueue(pixel - width); }
                if (y + 1 < height && distances[pixel + width] > nextDistance)
                { distances[pixel + width] = nextDistance; queue.Enqueue(pixel + width); }
            }
            var chosenX = -1;
            var chosenY = -1;
            var bestScore = double.MaxValue;
            for (var y = 4; y < height - size - 4; y++)
                for (var x = 4; x < width - size - 4; x++)
                {
                    var available = true;
                    for (var dy = -2; dy < size + 2 && available; dy++)
                        for (var dx = -2; dx < size + 2 && available; dx++)
                            if (used[(y + dy) * width + (x + dx)]) available = false;
                    if (!available) continue;
                    var d = distances[(y + size / 2) * width + x + size / 2];
                    if (d == int.MaxValue) d = 6;
                    var deltaX = x + size / 2 - width / 2;
                    var deltaY = y + size / 2 - height / 2;
                    var score = Math.Abs(d - 6) * 10000.0 +
                                deltaX * (double)deltaX + deltaY * (double)deltaY;
                    if (score < bestScore) { bestScore = score; chosenX = x; chosenY = y; }
                }
            if (chosenX < 0) throw new InvalidOperationException("No hay espacio libre para otra parcela.");

            var newTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "UP_EDITOR_NEW_PLOT", hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            HospitalPlot added = null;
            GameObject buttonObject = null;
            ScenarioPlotRuntime.Collection collection = null;
            bool createdCollection = false;
            var previousNames = PlotNames.Count;
            try
            {
                var pixels = new Color32[width * height];
                for (var p = 0; p < pixels.Length; p++)
                    pixels[p] = new Color32(0, 0, 0, 0);
                for (var dy = 0; dy < size; dy++)
                    for (var dx = 0; dx < size; dx++)
                        pixels[(chosenY + dy) * width + chosenX + dx] =
                            new Color32(255, 255, 255, 128);
                newTexture.SetPixels32(pixels);
                newTexture.Apply(false, false);

                var def = (HospitalPlotDefinition)CloneMethod.Invoke(template, null);
                def.FloorImage = newTexture;
                def.Cost = 10000;
                def.InitiallyOpen = false;
                def.Built = false; // Estado inicial real al jugar el escenario.
                def.InstaBuild = false;
                def.MergeInto = null;
                def.Prerequisites = new List<HospitalPlotPrerequisite>();
                def.BuildObjective = null;
                def.BuildObjectiveAutoStart = false;
                def.BuildObjectiveStartOnPrereqsMet = false;
                ItemsField.SetValue(def, new List<HospitalPlotItem>());
                BuiltField.SetValue(def, new List<HospitalPlotItem>());
                UnbuiltField.SetValue(def, new List<HospitalPlotItem>());

                var worldConfig = WorldConfigField?.GetValue(world) as WorldState.Config;
                if (worldConfig == null)
                    throw new InvalidOperationException("No se pudo acceder a la configuracion del perimetro.");
                var perimeterPrefabs = worldConfig.PerimeterPrefabs;
                var builtRoom = worldConfig._roomBuiltDefinition;
                var buildingRoom = worldConfig._roomBuildingDefinition;
                var unbuiltRoom = worldConfig._roomUnbuiltDefinition;
                try
                {
                    ScenarioHospitalStyles.Apply(level, CurrentStyles(level), worldConfig,
                        new[] { def });
                    added = new HospitalPlot(def, world, level);
                    world.HospitalPlots.Add(added);
                    added.BuyAndBuildImmediately();
                    added.Close();
                }
                finally
                {
                    worldConfig.PerimeterPrefabs = perimeterPrefabs;
                    worldConfig._roomBuiltDefinition = builtRoom;
                    worldConfig._roomBuildingDefinition = buildingRoom;
                    worldConfig._roomUnbuiltDefinition = unbuiltRoom;
                }

                // El cursor nativo conserva un diccionario privado de previews:
                // registrar SOLO la nueva parcela evita errores al usar Tile View.
                previewMethod.Invoke(nativeCursor, new object[] { added });
                var isTileViewVisible = AccessTools.Field(typeof(CursorEditHospital),
                    "_tileMapVisible")?.GetValue(nativeCursor);
                if (isTileViewVisible is bool && (bool)isTileViewVisible &&
                    previews.TryGetValue(added, out var tilePreview) && tilePreview != null)
                    tilePreview.SetActive(added.IsVisible());
                while (PlotNames.Count < originalPlots.Length)
                    PlotNames.Add(EditorLocalization.Format(
                        "editor.plot.default_name", PlotNames.Count + 1));
                PlotNames.Add(EditorLocalization.Format(
                    "editor.plot.default_name", originalPlots.Length + 1));

                // IMPORTANTE: NO destruir/recrear los controles anteriores.
                buttonObject = UnityEngine.Object.Instantiate(prefab, root, false);
                var control = buttonObject.GetComponent<PlotSelectionItem>();
                if (control == null)
                    throw new InvalidOperationException("No se encontro PlotSelectionItem en el boton nuevo.");
                control.Setup(added, false, level.HospitalEditEvents, colors);
                var label = AccessTools.Field(typeof(PlotSelectionItem),
                    "_text")?.GetValue(control) as TMP_Text;
                if (label != null) label.text = PlotNames[originalPlots.Length];

                if (_currentCustomPlots == null)
                {
                    _currentCustomPlots = new ScenarioPlotRuntime.Collection();
                    createdCollection = true;
                }
                collection = _currentCustomPlots;
                collection.Textures.Add(newTexture);
                collection.Definitions.Add(def);

                if (world.HospitalPlots.Count != originalPlots.Length + 1 ||
                    !originalPlots.SequenceEqual(world.HospitalPlots.Take(originalPlots.Length)))
                    throw new InvalidOperationException("El alta de parcela altero las parcelas anteriores.");
                level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(added);
                world.CalculateLighting();
                world.UpdateNavigation();
                UnderPressureEditorPlugin.Log?.LogInfo("Parcela " + (originalPlots.Length + 1) +
                    " agregada sin modificar las " + originalPlots.Length + " parcelas anteriores.");
                return originalPlots.Length;
            }
            catch
            {
                if (buttonObject != null) UnityEngine.Object.Destroy(buttonObject);
                if (added != null)
                {
                    if (added.HospitalMap != null)
                        world.DestroyHospitalMap(added.HospitalMap);
                    added.Destroy();
                    world.HospitalPlots.Remove(added);
                }
                if (added != null && previews.TryGetValue(added, out var preview))
                {
                    previews.Remove(added);
                    if (preview != null) UnityEngine.Object.Destroy(preview);
                }
                if (collection != null)
                {
                    collection.Textures.Remove(newTexture);
                    collection.Definitions.Remove(added?.Definition);
                    if (createdCollection && collection.Definitions.Count == 0)
                        _currentCustomPlots = null;
                }
                while (PlotNames.Count > previousNames) PlotNames.RemoveAt(PlotNames.Count - 1);
                UnityEngine.Object.Destroy(newTexture);
                throw;
            }
        }

        // HospitalMap identifica las casillas por RGB (ignora alfa); el alfa
        // es exclusivamente visual y debe ser cero fuera de la parcela.
        private static void MakeOutsideTransparent(Texture2D texture)
        {
            if (texture == null) return;
            var pixels = texture.GetPixels32();
            bool changed = false;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r != 0 || pixels[i].g != 0 || pixels[i].b != 0 || pixels[i].a == 0)
                    continue;
                pixels[i].a = 0;
                changed = true;
            }
            if (changed)
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
            }
        }

        // Borra la parcela seleccionada, no necesariamente la ultima. Las otras
        // instancias y botones nativos permanecen intactos.
        internal static void DeletePlot(Level level, HospitalPlot selected)
        {
            RequireEditor(level);
            var world = level.WorldState;
            var plots = world.HospitalPlots;
            if (plots.Count <= 1)
                throw new InvalidOperationException("El hospital debe conservar al menos una parcela.");
            int index = -1;
            for (var i = 0; i < plots.Count; i++)
                if (ReferenceEquals(plots[i], selected)) { index = i; break; }
            if (index < 0)
                throw new InvalidOperationException("Selecciona la parcela que quieres eliminar.");

            StopActiveEditorTools(level);
            var menu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            var root = menu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                "_plotSelectionRoot")?.GetValue(menu) as Transform;
            if (root == null)
                throw new InvalidOperationException("No se encontro el selector de parcelas.");
            var controls = root.GetComponentsInChildren<PlotSelectionItem>(true);
            var field = AccessTools.Field(typeof(PlotSelectionItem), "_plot");
            var control = controls.FirstOrDefault(entry =>
                ReferenceEquals(field?.GetValue(entry), selected));
            if (control == null)
                throw new InvalidOperationException("No se encontro el boton de la parcela seleccionada.");

            // Cambiar la seleccion antes del Destroy, porque CursorEditHospital
            // llama Close() sobre la antigua parcela al cambiar de seleccion.
            var next = plots[index == 0 ? 1 : index - 1];
            level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(next);
            CursorEditHospital nativeCursor;
            if (level.CursorManager != null &&
                level.CursorManager.TryGetActiveMode<CursorEditHospital>(out nativeCursor))
            {
                var previews = AccessTools.Field(typeof(CursorEditHospital),
                    "_tileMapPreview")?.GetValue(nativeCursor) as Dictionary<HospitalPlot, GameObject>;
                if (previews != null && previews.TryGetValue(selected, out var preview))
                {
                    previews.Remove(selected);
                    if (preview != null) UnityEngine.Object.Destroy(preview);
                }
            }
            control.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(control.gameObject);
            if (selected.HospitalMap != null) world.DestroyHospitalMap(selected.HospitalMap);
            selected.Destroy();
            plots.RemoveAt(index);
            if (index < PlotNames.Count) PlotNames.RemoveAt(index);
            foreach (var remaining in plots)
            {
                if (ReferenceEquals(remaining.Definition.MergeInto?.Instance, selected.Definition))
                    remaining.Definition.MergeInto = null;
                // Una parcela borrada nunca debe quedar como requisito de otra.
                var requirements = remaining.Definition.Prerequisites;
                var prereqField = AccessTools.Field(typeof(HospitalPlotPrerequisitePlotBought), "_plot");
                if (requirements == null || prereqField == null) continue;
                for (var i = requirements.Count - 1; i >= 0; i--)
                {
                    var bought = requirements[i] as HospitalPlotPrerequisitePlotBought;
                    if (bought == null) continue;
                    var targets = prereqField.GetValue(bought) as SharedInstance<HospitalPlotDefinition>[];
                    if (targets != null && targets.Any(item =>
                        ReferenceEquals(item?.Instance, selected.Definition)))
                        requirements.RemoveAt(i);
                }
            }
            world.CalculateLighting();
            world.UpdateNavigation();
            UnderPressureEditorPlugin.Log?.LogInfo("Parcela " + (index + 1) +
                " eliminada, quedan " + plots.Count + " parcelas.");
        }

        private static void RefreshPlotButtons(Level level, int selectedIndex = 0)
        {
            var menu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            if (menu == null) return;
            var menuType = typeof(EditHospitalMenu);
            var root = AccessTools.Field(menuType, "_plotSelectionRoot")?.GetValue(menu) as Transform;
            var prefab = AccessTools.Field(menuType, "_plotSelectionButtonPrefab")?.GetValue(menu) as GameObject;
            var colors = AccessTools.Field(menuType, "_editFrameColors")?.GetValue(menu) as Color[];
            if (root == null || prefab == null || colors == null)
                throw new InvalidOperationException("No se encontraron los controles nativos de parcelas.");
            if (level.WorldState.HospitalPlots == null || level.WorldState.HospitalPlots.Count == 0)
                throw new InvalidOperationException("No quedan parcelas para mostrar en el Editor.");
            var old = root.GetComponentsInChildren<PlotSelectionItem>(true);
            foreach (var entry in old)
            {
                entry.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(entry.gameObject);
            }
            for (var i = 0; i < level.WorldState.HospitalPlots.Count; i++)
            {
                var obj = UnityEngine.Object.Instantiate(prefab, root, false);
                var control = obj.GetComponent<PlotSelectionItem>();
                control.Setup(level.WorldState.HospitalPlots[i], i == selectedIndex, level.HospitalEditEvents, colors);
                if (i < PlotNames.Count)
                {
                    var label = AccessTools.Field(typeof(PlotSelectionItem), "_text")?.GetValue(control) as TMP_Text;
                    if (label != null) label.text = PlotNames[i];
                }
            }
            if (level.WorldState.HospitalPlots.Count > 0)
                level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(
                    level.WorldState.HospitalPlots[Mathf.Clamp(selectedIndex, 0, level.WorldState.HospitalPlots.Count - 1)]);
        }

        private static void StopActiveEditorTools(Level level)
        {
            var cursor = level.CursorManager;
            if (cursor == null) return;
            cursor.PopMode<CursorRoomItem>();
            cursor.PopMode<CursorEditHospitalItem>();
            cursor.PopMode<CursorEditHospitalMovePlot>();
            cursor.PopMode<CursorEditHospitalBuild>();
        }

        internal static void ReleaseCustomAssets()
        {
            // Ejecutar una vez cerrada la escena del editor.
            _currentCustomPlots?.DestroyAssets();
            _currentCustomPlots = null;
            PlotNames.Clear();
            _namedMapLevel = null;
            _namedMap = null;
            _architectLevel = null;
            _architectName = null;
            ScenarioEditorScene.Clear();
            _perimeterModeLevel = null;
            _perimeterMode = ScenarioMapFormat.PerimeterNone;
            _perimeterPrefabs = new string[0];
            _builtRoomStyle = "";
            _buildingRoomStyle = "";
            _unbuiltRoomStyle = "";
        }

        private static void RequireEditor(Level level)
        {
            if (!ScenarioEditorSession.Active || !ReferenceEquals(ScenarioEditorSession.Level, level))
                throw new InvalidOperationException("Solo se permite usar mapas dentro del editor de escenarios.");
        }

        private static string SafeName(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            value = value.Trim(' ', '.').Replace(' ', '_');
            if (value.Length == 0) throw new ArgumentException("Nombre sin caracteres validos.");
            return value;
        }

        private static string ArchitectFromPath(string mapPath)
        {
            var root = Path.GetFullPath(Folder).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(mapPath);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return UnderPressureEditorPlugin.ArchitectName;
            var relative = full.Substring(root.Length);
            var separator = relative.IndexOfAny(new[]
            {
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar
            });
            return separator > 0 ? relative.Substring(0, separator) :
                UnderPressureEditorPlugin.ArchitectName;
        }
    }

    // Capa global de decoracion: no es una parcela, no ocupa una casilla de
    // HospitalPlots y los objetos no dependen de ningun FloorPlan.
    internal static class ScenarioEditorScene
    {
        internal sealed class SceneObject
        {
            internal GameObject Visual;
            internal RoomItemDefinition Definition;
            // Vacio = objeto nuevo; con valor = decoracion original convertida
            // temporalmente en un prefab editable, sin tocar el terreno.
            internal string SourcePath;
            internal Vector3 BasePosition;
            internal float BaseRotation;
        }

        private static readonly List<SceneObject> Objects = new List<SceneObject>();
        private static readonly Dictionary<string, ScenarioMapFormat.OriginalChange> SourceChanges =
            new Dictionary<string, ScenarioMapFormat.OriginalChange>(StringComparer.Ordinal);
        private static readonly Dictionary<Renderer, bool> SourceRenderers =
            new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Collider, bool> SourceColliders =
            new Dictionary<Collider, bool>();
        private static bool _sourcesExamined;
        private static bool _loggedMissingSource;
        private static readonly Dictionary<HospitalPlot, HashSet<HospitalPlotLayer>> ModifiedLayers =
            new Dictionary<HospitalPlot, HashSet<HospitalPlotLayer>>();

        internal static bool WasPlotLayerModified(HospitalPlot plot, HospitalPlotLayer layer) =>
            plot != null && ModifiedLayers.TryGetValue(plot, out var layers) && layers.Contains(layer);

        internal static void MarkPlotLayerModified(HospitalPlot plot, HospitalPlotLayer layer)
        {
            if (plot == null) return;
            if (!ModifiedLayers.TryGetValue(plot, out var layers))
                ModifiedLayers[plot] = layers = new HashSet<HospitalPlotLayer>();
            layers.Add(layer);
        }
        private static GameObject _root;
        private static Level _level;
        private static bool _editor;
        private static bool _selected;
        private static bool _visible = true;
        private static SceneObject _selectedObject;
        private static object _selectedNativeItem;
        internal static object SelectedNativeItem => _selectedNativeItem;
        private static SceneObject _dragObject;
        private static HospitalPlot _originalPlot;
        private static object _nativeOriginal;
        private static GameObject _hiddenNativeVisual;
        private static bool _hiddenNativeWasActive;
        private static bool _createdForDrag;
        private static bool _awaitMouseRelease;
        private static Vector3 _originalScenePosition;
        private static Vector3 _offset;

        internal static bool IsSelected => _selected && _editor && _level != null;
        internal static bool IsDragging => _dragObject != null;
        internal static bool Visible => _visible;
        internal static SceneObject SelectedObject => _selectedObject;
        internal static IReadOnlyList<SceneObject> SceneObjects => Objects;

        internal static void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null) _root.SetActive(visible);
            if (!visible) CancelDrag();
        }

        internal static void RemoveObject(SceneObject entry) => Remove(entry);

        internal static void Select(Level level)
        {
            if (!ScenarioEditorSession.Active ||
                !ReferenceEquals(level, ScenarioEditorSession.Level)) return;
            _level = level;
            _editor = true;
            _selected = true;
            _selectedObject = null;
            _selectedNativeItem = null;
            ImportOriginalDecoration();
            UnderPressureEditorPlugin.Log?.LogInfo("Editor: capa Escenario seleccionada (tecla a la izquierda del 1).");
        }

        internal static void Deselect()
        {
            _selected = false;
            CancelDrag();
        }

        internal static void Clear()
        {
            CancelDrag();
            foreach (var entry in Objects)
                if (entry?.Visual != null) UnityEngine.Object.Destroy(entry.Visual);
            Objects.Clear();
            // Restaurar los estados encontrados al entrar. El codigo protegido
            // ScenarioEditorFixedGeometry.Restore sigue siendo el responsable
            // de restaurar los renderizadores estaticos del editor.
            foreach (var state in SourceRenderers)
                if (state.Key != null) state.Key.enabled = state.Value;
            foreach (var state in SourceColliders)
                if (state.Key != null) state.Key.enabled = state.Value;
            SourceRenderers.Clear();
            SourceColliders.Clear();
            SourceChanges.Clear();
            _sourcesExamined = false;
            _loggedMissingSource = false;
            ModifiedLayers.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _selectedObject = null;
            _selectedNativeItem = null;
            _selected = false;
            _visible = true;
            _editor = false;
            _level = null;
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("UP Scenario - Global Decoration");
            _root.SetActive(_visible);
        }

        internal static void Load(Level level, ScenarioMapFormat.Scene data, bool editor)
        {
            Clear();
            _level = level;
            _editor = editor;
            SetVisible(true);
            foreach (var change in data?.sourceChanges ?? new ScenarioMapFormat.OriginalChange[0])
            {
                if (change == null || string.IsNullOrEmpty(change.path) ||
                    SourceChanges.ContainsKey(change.path))
                    throw new InvalidDataException("Escenario: identidad original duplicada o vacia.");
                SourceChanges.Add(change.path, change);
            }
            if (editor) ImportOriginalDecoration();
            else ApplySourceChangesInGame();
            if (data?.items == null) return;
            foreach (var item in data.items)
            {
                var definition = FindDefinition(level, item);
                if (definition == null)
                    throw new InvalidDataException("Escenario: no se reconoce el objeto " +
                        (item?.tag ?? "(sin etiqueta)"));
                Add(definition, new Vector3(item.x, item.y, item.z), item.rotation);
            }
            UnderPressureEditorPlugin.Log?.LogInfo("Escenario: " + Objects.Count + " objetos globales cargados.");
        }

        internal static void Validate(Level level, ScenarioMapFormat.Scene data)
        {
            if (data?.items == null) return;
            foreach (var item in data.items)
                if (FindDefinition(level, item) == null)
                    throw new InvalidDataException("Escenario: definición no encontrada: " +
                        (item?.tag ?? "(vacía)"));
        }

        private static RoomItemDefinition FindDefinition(Level level, ScenarioMapFormat.Item data)
        {
            if (data == null) return null;
            var definitions = level?.App?.Metagame?.LandscapeItemDatabase?.Instance?.RoomItems;
            if (definitions == null) return null;
            RoomItemDefinition candidate = null;
            foreach (var reference in definitions)
            {
                var definition = reference?.Instance as RoomItemDefinition;
                if (definition == null) continue;
                if (!string.Equals(definition.GUID.ToString("D"), data.guid,
                    StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(definition.DebugTag ?? "", data.tag ?? "",
                    StringComparison.OrdinalIgnoreCase)) continue;
                string prefabName;
                try { prefabName = definition.GetPrefab(0)?.name ?? ""; }
                catch { continue; }
                if (!string.IsNullOrEmpty(data.prefab) &&
                    !string.Equals(prefabName, data.prefab, StringComparison.Ordinal))
                    continue;
                if (candidate != null && !ReferenceEquals(candidate, definition))
                    throw new InvalidDataException("Escenario: definición ambigua: " + data.tag);
                candidate = definition;
            }
            return candidate;
        }

        internal static SceneObject Add(RoomItemDefinition definition, Vector3 position, float rotation)
        {
            if (definition == null) return null;
            var prefab = definition.GetPrefab(0) as GameObject;
            if (prefab == null)
                throw new InvalidOperationException("Escenario: el objeto " + definition.DebugTag +
                    " no tiene prefab colocable.");
            EnsureRoot();
            var visual = UnityEngine.Object.Instantiate(prefab, position,
                Quaternion.Euler(0f, rotation, 0f), _root.transform);
            visual.name = "UP Scene - " + prefab.name;
            visual.SetActive(true);
            // Un objeto sin collider sigue siendo seleccionable a traves de su geometria,
            // y no se añade un collider ficticio a la escena ni a sus originales.
            var entry = new SceneObject { Visual = visual, Definition = definition };
            Objects.Add(entry);
            return entry;
        }

        internal static ScenarioMapFormat.Scene Capture()
        {
            var scene = new ScenarioMapFormat.Scene();
            var items = new List<ScenarioMapFormat.Item>();
            foreach (var entry in Objects)
            {
                if (entry?.Visual == null || entry.Definition == null ||
                    !string.IsNullOrEmpty(entry.SourcePath)) continue;
                var position = entry.Visual.transform.position;
                var def = entry.Definition;
                items.Add(new ScenarioMapFormat.Item
                {
                    guid = def.GUID.ToString("D"), tag = def.DebugTag ?? "",
                    prefab = def.GetPrefab(0)?.name ?? "",
                    x = position.x, y = position.y, z = position.z,
                    rotation = entry.Visual.transform.eulerAngles.y
                });
            }
            scene.items = items.ToArray();
            // Conservar las eliminaciones incluso sin un prefab activo.
            foreach (var entry in Objects)
            {
                if (entry?.Visual == null || string.IsNullOrEmpty(entry.SourcePath)) continue;
                var pos = entry.Visual.transform.position;
                var angle = entry.Visual.transform.eulerAngles.y;
                if ((pos - entry.BasePosition).sqrMagnitude < .00001f &&
                    Mathf.Abs(Mathf.DeltaAngle(angle, entry.BaseRotation)) < .01f)
                {
                    SourceChanges.Remove(entry.SourcePath);
                    continue;
                }
                SourceChanges[entry.SourcePath] = DescribeOriginal(entry.SourcePath,
                    entry.Definition, pos, angle, false);
            }
            scene.sourceChanges = SourceChanges.Values.OrderBy(v => v.path,
                StringComparer.Ordinal).ToArray();
            return scene;
        }

        internal static bool TryPlaceNativeObject(CursorRoomItem cursor, ref bool result)
        {
            if (!IsSelected) return true;
            if (!_visible) { result = false; return false; }
            if (cursor?.RoomItem == null) return true;
            // Se emplea la definicion del objeto que se esta colocando,
            // pero NO se agrega al FloorPlan de ninguna parcela.
            var definition = cursor.RoomItem.Definition as RoomItemDefinition;
            if (definition == null) return true;
            Vector3 point;
            if (!GroundPoint(Input.mousePosition, out point)) return false;
            try
            {
                Add(definition, point, cursor.RoomItem.Rotation);
                result = false; // Dejar activo el cursor y colocar más copias.
                return false;
            }
            catch (Exception error)
            {
                UnderPressureEditorPlugin.Log?.LogError("Escenario: error al colocar: " + error);
                result = false;
                return false;
            }
        }

        internal static bool GetGroundPoint(Vector2 screen, out Vector3 position) =>
            GroundPoint(screen, out position);

        private static bool GroundPoint(Vector2 screen, out Vector3 position)
        {
            position = Vector3.zero;
            var camera = Camera.main;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var component = hit.collider;
                if (component == null) continue;
                var name = component.transform.root.name;
                if (name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase) ||
                    component.name.StartsWith("Pass", StringComparison.OrdinalIgnoreCase))
                {
                    position = hit.point;
                    return true;
                }
            }
            var plane = new Plane(Vector3.up, Vector3.zero);
            float distance;
            if (!plane.Raycast(ray, out distance)) return false;
            position = ray.GetPoint(distance);
            return true;
        }

        internal static bool TryPick(Vector2 screen, out SceneObject picked)
        {
            picked = null;
            var camera = Camera.main;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                foreach (var candidate in Objects)
                {
                    if (candidate?.Visual == null || !candidate.Visual.activeInHierarchy) continue;
                    if (!hit.collider.transform.IsChildOf(candidate.Visual.transform)) continue;
                    picked = candidate;
                    return true;
                }
            }
            // Objetos sin colisionadores: comprobar triangulos 3D y distancia real
            // con el mismo metodo que se usa para las parcelas.
            float closest = float.PositiveInfinity;
            foreach (var candidate in Objects)
            {
                if (candidate?.Visual == null) continue;
                float distance;
                if (!ScenarioEditorObjectInteraction.RayPickVisual(candidate.Visual, ray,
                    out distance) || distance >= closest) continue;
                closest = distance;
                picked = candidate;
            }
            return picked != null;
        }

        internal static void UpdateEditor(Level level)
        {
            if (!IsSelected || !ReferenceEquals(level, _level) || !_visible) return;
            // Ctrl pertenece a la seleccion rectangular entre parcelas.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                return;
            if (_dragObject != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelDrag();
                    return;
                }
                if (_createdForDrag && _dragObject.Visual == null) { CancelDrag(); return; }
                if (_awaitMouseRelease)
                {
                    if (!Input.GetMouseButton(0)) _awaitMouseRelease = false;
                    return;
                }
                Vector3 point;
                if (GroundPoint(Input.mousePosition, out point) && _dragObject.Visual != null)
                    _dragObject.Visual.transform.position = point + _offset;
                if (Input.GetMouseButtonDown(0) &&
                    (UnityEngine.EventSystems.EventSystem.current == null ||
                     !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
                    CommitDrag();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Delete) && _selectedObject != null)
            {
                Remove(_selectedObject);
                _selectedObject = null;
                return;
            }
            if (!Input.GetMouseButtonDown(0) ||
                (UnityEngine.EventSystems.EventSystem.current != null &&
                 UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())) return;
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            // Un clic normal selecciona, sin recoger ni desplazar el objeto.
            SceneObject original;
            HospitalPlot owner;
            object item;
            RoomItemDefinition definition;
            if (!ScenarioEditorObjectInteraction.TryPickForScene(out original, out owner,
                out item, out definition)) return;
            if (original != null)
            {
                _selectedObject = original;
                _selectedNativeItem = null;
                if (shift || alt) StartDrag(original, shift);
                return;
            }
            if (owner == null || definition == null)
            {
                _selectedObject = null;
                _selectedNativeItem = null;
                return;
            }
            // En Escenario, un clic normal TAMBIEN selecciona objetos de las
            // parcelas. No crea copias ni cambia su propietario al seleccionar.
            _selectedNativeItem = item;
            _selectedObject = null;
            if (!shift && !alt) return;
            var go = ScenarioEditorObjectInteraction.ReadNativeVisual(item);
            if (go == null) return;
            try
            {
                var copy = Add(definition, go.transform.position, go.transform.eulerAngles.y);
                _originalPlot = shift ? null : owner;
                _nativeOriginal = shift ? null : item;
                if (alt && !shift)
                {
                    // El usuario mueve una instancia, NO debe ver una segunda.
                    // El objeto nativo solo se oculta hasta confirmar el destino.
                    _hiddenNativeVisual = go;
                    _hiddenNativeWasActive = go.activeSelf;
                    go.SetActive(false);
                }
                _createdForDrag = true;
                _awaitMouseRelease = true;
                _dragObject = copy;
                _selectedObject = copy;
                _selectedNativeItem = null;
                _offset = Vector3.zero;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("Escenario: no se pudo recoger el objeto: " + ex);
            }
        }

        private static void StartDrag(SceneObject original, bool clone)
        {
            _dragObject = clone
                ? Add(original.Definition, original.Visual.transform.position,
                    original.Visual.transform.eulerAngles.y)
                : original;
            _createdForDrag = clone;
            _originalScenePosition = original.Visual.transform.position;
            _awaitMouseRelease = true;
            Vector3 ground;
            _offset = GroundPoint(Input.mousePosition, out ground)
                ? _dragObject.Visual.transform.position - ground : Vector3.zero;
        }

        private static void CancelDrag()
        {
            if (_hiddenNativeVisual != null)
                _hiddenNativeVisual.SetActive(_hiddenNativeWasActive);
            _hiddenNativeVisual = null;
            if (_dragObject != null && _createdForDrag) Remove(_dragObject);
            else if (_dragObject?.Visual != null)
                _dragObject.Visual.transform.position = _originalScenePosition;
            _dragObject = null;
            _originalPlot = null;
            _nativeOriginal = null;
            _createdForDrag = false;
            _awaitMouseRelease = false;
            _offset = Vector3.zero;
        }

        // Alt retira el original sin lanzar RoomItem.Destroy(): dicho evento
        // altera los datos de seleccion del cursor nativo, incluido Ctrl.
        private static bool RemoveNativeOriginalForAlt(HospitalPlot owner, object original)
        {
            var item = original as LandscapeRoomItem;
            var floor = owner?.HospitalMap?.FloorPlan;
            if (item == null || floor == null || item.FloorPlan != floor ||
                !floor.LandscapeItems.Contains(item)) return false;
            var records = owner.Definition?.GetItems(item.Layer);
            if (records == null) return false;

            // Guardar la referencia al registro real de esta instancia, no
            // eliminar por tipo: podria haber cientos de objetos identicos.
            var index = -1;
            var near = float.MaxValue;
            var count = 0;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record?.Definition?.Instance == null ||
                    !ReferenceEquals(record.Definition.Instance, item.Definition)) continue;
                count++;
                var distance = (record.Position - item.LocalPosition).sqrMagnitude;
                if (distance < near) { near = distance; index = i; }
            }
            // Si la lista de definicion esta desfasada, no eliminar una
            // entrada de otro objeto. El FloorPlan visible es el origen real.
            if (near > 0.04f && count != 1) index = -1;

            try
            {
                floor.RemoveItem(item);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Alt: error al retirar el original del FloorPlan: " + ex);
                return false;
            }
            if (index >= 0) records.RemoveAt(index);
            try
            {
                var visual = item.Visual;
                if (visual != null) { visual.Destroy(); item.Visual = null; }
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Alt: objeto retirado, fallo la limpieza visual: " + ex.Message);
            }
            MarkPlotLayerModified(owner, item.Layer);
            return true;
        }

        private static void CommitDrag()
        {
            if (_originalPlot != null && _nativeOriginal != null)
            {
                if (_dragObject?.Visual == null ||
                    !RemoveNativeOriginalForAlt(_originalPlot, _nativeOriginal))
                {
                    CancelDrag();
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Alt: traslado cancelado, original conservado.");
                    return;
                }
            }
            _dragObject = null;
            _originalPlot = null;
            _nativeOriginal = null;
            _hiddenNativeVisual = null;
            _createdForDrag = false;
            _awaitMouseRelease = false;
        }

        // LOCALIZACION DE DECORACION ORIGINAL.
        // LandscapeItems_Base existe en la escena Unity y no pertenece a ninguna
        // parcela; sus renderizadores estaticos son ocultados en el editor por
        // ScenarioEditorFixedGeometry (mecanismo protegido).
        private static Transform OriginalRoot()
        {
            var found = GameObject.Find("LandscapeItems_Base");
            return found != null ? found.transform : null;
        }

        private static string OriginalPath(Transform root, Transform node)
        {
            var parts = new List<string>();
            for (var tr = node; tr != null && !ReferenceEquals(tr, root); tr = tr.parent)
                parts.Add(tr.GetSiblingIndex().ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static Transform FindOriginal(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var tr = root;
            foreach (var component in path.Split('/'))
            {
                int index;
                if (!int.TryParse(component, out index) || index < 0 || index >= tr.childCount)
                    return null;
                tr = tr.GetChild(index);
            }
            return tr;
        }

        private static void HideSource(Transform source)
        {
            if (source == null) return;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!SourceRenderers.ContainsKey(renderer))
                    SourceRenderers.Add(renderer, renderer.enabled);
                renderer.enabled = false;
            }
            foreach (var collider in source.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null) continue;
                if (!SourceColliders.ContainsKey(collider))
                    SourceColliders.Add(collider, collider.enabled);
                collider.enabled = false;
            }
        }

        private static ScenarioMapFormat.OriginalChange DescribeOriginal(string path,
            RoomItemDefinition definition, Vector3 position, float rotation, bool deleted)
        {
            return new ScenarioMapFormat.OriginalChange
            {
                path = path, deleted = deleted,
                guid = definition?.GUID.ToString("D") ?? "",
                tag = definition?.DebugTag ?? "",
                prefab = definition?.GetPrefab(0)?.name ?? "",
                x = position.x, y = position.y, z = position.z,
                rotation = rotation
            };
        }

        private static string NormalizedSourceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = name.Trim();
            if (name.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 7).Trim();
            var paren = name.LastIndexOf('(');
            if (paren > 0 && name.EndsWith(")", StringComparison.Ordinal))
            {
                int suffix;
                if (int.TryParse(name.Substring(paren + 1, name.Length - paren - 2),
                    out suffix)) name = name.Substring(0, paren).Trim();
            }
            return name;
        }

        private static bool AlreadyNative(Level level, RoomItemDefinition definition, Vector3 point)
        {
            foreach (var plot in level.WorldState.HospitalPlots)
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (item == null || !ReferenceEquals(item.Definition, definition)) continue;
                    var go = item.Visual?.GameObject;
                    if (go != null && (go.transform.position - point).sqrMagnitude < .09f)
                        return true;
                }
            }
            return false;
        }

        private static void ImportOriginalDecoration()
        {
            if (_sourcesExamined || !_editor || _level?.WorldState == null) return;
            var root = OriginalRoot();
            if (root == null)
            {
                if (!_loggedMissingSource)
                {
                    _loggedMissingSource = true;
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Escenario: LandscapeItems_Base no disponible; se reintentara al seleccionar la capa.");
                }
                return;
            }
            var definitions = _level.App?.Metagame?.LandscapeItemDatabase?.Instance?.RoomItems;
            if (definitions == null) return;
            var prefabs = new Dictionary<string, RoomItemDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in definitions)
            {
                var definition = reference?.Instance as RoomItemDefinition;
                if (definition == null || definition.ItemType != RoomItemDefinition.Type.Landscape)
                    continue;
                try
                {
                    var prefab = definition.GetPrefab(0);
                    if (prefab != null && !prefabs.ContainsKey(prefab.name))
                        prefabs.Add(prefab.name, definition);
                    var tag = definition.DebugTag;
                    if (!string.IsNullOrEmpty(tag) && !prefabs.ContainsKey(tag))
                        prefabs.Add(tag, definition);
                }
                catch { }
            }
            if (prefabs.Count == 0)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Escenario: todavia no hay prefabs de decoracion disponibles.");
                return;
            }
            _sourcesExamined = true;
            var imported = 0;
            var skippedNative = 0;
            var visited = new HashSet<Transform>();
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (node == null || ReferenceEquals(node, root)) continue;
                RoomItemDefinition definition;
                if (!prefabs.TryGetValue(NormalizedSourceName(node.name), out definition)) continue;
                // No convertir hijos de un objeto ya importado, aunque esten
                // varios niveles por debajo de su raiz.
                bool nested = false;
                for (var ancestor = node.parent; ancestor != null &&
                    !ReferenceEquals(ancestor, root); ancestor = ancestor.parent)
                    if (visited.Contains(ancestor)) { nested = true; break; }
                if (nested) continue;
                visited.Add(node);
                var key = OriginalPath(root, node);
                var change = SourceChanges.TryGetValue(key, out var found) ? found : null;
                if (AlreadyNative(_level, definition, node.position))
                {
                    ++skippedNative;
                    continue;
                }
                // Se desactiva SOLO la decoracion original representada por este
                // prefab; el terreno, las parcelas y la geometria no emparejada
                // permanecen intactos.
                HideSource(node);
                if (change != null && change.deleted) continue;
                var position = change == null ? node.position : new Vector3(change.x,change.y,change.z);
                var rotation = change == null ? node.eulerAngles.y : change.rotation;
                var visual = Add(definition, position, rotation);
                if (visual != null)
                {
                    visual.SourcePath = key;
                    visual.BasePosition = node.position;
                    visual.BaseRotation = node.eulerAngles.y;
                    ++imported;
                }
            }
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Escenario: " + imported + " adornos originales editables, " + skippedNative +
                " representados por parcelas. Las mallas sin prefab reconocido no se modifican.");
        }

        private static void ApplySourceChangesInGame()
        {
            if (SourceChanges.Count == 0) return;
            var root = OriginalRoot();
            if (root == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Escenario: no se encontraron los objetos originales para aplicar los cambios.");
                return;
            }
            foreach (var change in SourceChanges.Values)
            {
                var source = FindOriginal(root, change.path);
                if (source == null)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Escenario: no se encontro el objeto original " + change.path);
                    continue;
                }
                if (change.deleted)
                {
                    HideSource(source);
                    continue;
                }
                var definition = FindDefinition(_level, new ScenarioMapFormat.Item
                {
                    guid = change.guid, tag = change.tag, prefab = change.prefab
                });
                if (definition == null)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Escenario: definicion no encontrada para " + change.path);
                    continue;
                }
                HideSource(source);
                Add(definition, new Vector3(change.x,change.y,change.z), change.rotation);
            }
        }

        private static void Remove(SceneObject entry)
        {
            if (entry == null) return;
            Objects.Remove(entry);
            if (!string.IsNullOrEmpty(entry.SourcePath))
                SourceChanges[entry.SourcePath] = DescribeOriginal(entry.SourcePath,
                    entry.Definition, entry.BasePosition, entry.BaseRotation, true);
            if (entry.Visual != null) UnityEngine.Object.Destroy(entry.Visual);
        }
    }

    [HarmonyPatch(typeof(CursorRoomItem), "PlaceItem")]
    internal static class ScenarioEditorScenePlacePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorRoomItem __instance, ref bool __result)
        {
            // Parcela 0 no tiene FloorPlan: bloquear la colocacion en la
            // antigua parcela nativa mientras Landscape sea la seleccion.
            if (ScenarioLandscape.IsActive)
            {
                __result = false;
                return false;
            }
            return ScenarioEditorScene.TryPlaceNativeObject(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioEditorMapReleasePatch
    {
        private static void Postfix() => ScenarioEditorStorage.ReleaseCustomAssets();
    }

    // Panel Unity UI: evita depender de GUILayout/GUI y utiliza botones nativos del editor.
    internal sealed class ScenarioEditorStorageMenu : MonoBehaviour
    {
        private static ScenarioEditorStorageMenu _active;
        private readonly List<GameObject> _buttons = new List<GameObject>();
        private EditHospitalMenu _menu;
        private Level _level;
        private Button _template;
        private bool _templateWasActive;
        private GameObject _dialog;
        private InputField _nameInput;
        private Text _statusText;
        private GameObject _mapIdentityRoot;
        private Text _mapIdentityLabel;
        private bool _showMapCoordinates;
        private Font _font;
        private HospitalPlot _selectedPlot;
        private TMP_Dropdown _layerDropdown;
        private CanvasGroup _layerDropdownCanvasGroup;
        private bool _createdLayerDropdownCanvasGroup;
        private float _layerDropdownOriginalAlpha;
        private bool _layerDropdownOriginalInteractable;
        private bool _layerDropdownOriginalBlocksRaycasts;
        private readonly Button[] _layerButtons = new Button[3];
        private GameObject _layerButtonsRoot;
        private RectTransform _plotListViewport;
        private Image _landscapeSelectBackground;
        private Outline _landscapeBorder;
        private Button _landscapeButton;
        private Toggle _landscapeVisibilityToggle;
        private float _nextCursorCoordinateRefresh;
        private Button _addPlotButton;
        private Button _movePlotButton;
        private Button _deletePlotButton;
        private Button _cameraButton;
        private TMP_Text _cameraButtonText;
        private Button _objectsButton;
        private Button _buildButton;
        private Button _stylesButton;
        private bool _stylesDialogOpen;
        private TMP_Text _plotInfoText;
        private Button _nativeCameraButton;
        private Button _nativeObjectsButton;
        private Button _nativeBuildButton;
        private Button _nativeMoveButton;
        private int _initialVisibilityFrames;
        private Button _plotIsolationButton;
        private bool _plotIsolation;
        private readonly Dictionary<HospitalPlot, bool> _plotVisibilityBeforeIsolation =
            new Dictionary<HospitalPlot, bool>();
        internal static bool PlotIsolationActive => _active != null && _active._plotIsolation;
        internal Button ObjectsButton => _objectsButton;
        internal Button BuildButton => _buildButton;
        internal Button CameraButton => _cameraButton;
        internal Button StylesButton => _stylesButton;
        internal Transform PlotLayerRoot => _layerButtonsRoot?.transform;
        internal GameObject MapIdentityRoot => _mapIdentityRoot;
        internal Button[] PlotChromeButtons => new[]
        {
            _layerButtons[0], _layerButtons[1], _layerButtons[2],
            _plotIsolationButton, _addPlotButton, _movePlotButton, _deletePlotButton
        };

        private static string T(string key) => EditorLocalization.Get(key);

        internal static bool IsDialogOpen => _active != null && _active._dialog != null;

        internal static bool CloseOpenDialog()
        {
            if (!IsDialogOpen) return false;
            _active.CloseDialog();
            return true;
        }

        internal static void ShowSaveFromPause() => _active?.ShowSave();
        internal static void ShowLoadFromPause() => _active?.ShowLoad();
        internal static void ShowEmptyMapFromPause() => _active?.ShowEmptyMapConfirmation();
        internal static void DeactivateNativeTools() => _active?.StopNativeTools();

        internal void Initialise(EditHospitalMenu menu, Level level)
        {
            if (_menu != null) return;
            _active = this;
            ScenarioEditorScene.Clear();
            _menu = menu;
            _level = level;
            _layerDropdown = AccessTools.Field(typeof(EditHospitalMenu), "_plotLayerDropdown")?
                .GetValue(menu) as TMP_Dropdown;
            _template = AccessTools.Field(typeof(EditHospitalMenu), "_quitButton")?.GetValue(menu) as Button;
            if (_template == null)
            {
                UnderPressureEditorPlugin.Log?.LogError("El prefab del editor no contiene su boton de salida.");
                return;
            }
            _templateWasActive = _template.gameObject.activeSelf;
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            AddLayerButtons();
            AddStylesToolbarButton();
            AddPlotIsolationButton();
            InitialisePlotActions();
            _selectedPlot = level.WorldState.HospitalPlots.Count > 0
                ? level.WorldState.HospitalPlots[0] : null;
            level.HospitalEditEvents.OnSelectHospitalPlot += OnPlotSelected;
            AddMapIdentity();
            PrepareNativeToolbar();
            RefreshMapIdentity();
            _initialVisibilityFrames = 12;
            ScenarioEditorDockLayout.Attach(_menu, _level, this, _template, _plotListViewport);
        }

        private void PrepareNativeToolbar()
        {
            AddTileViewToPlotMenu();
            AddTopEditorButtons();
            _template.gameObject.SetActive(false);
        }

        private void AddTileViewToPlotMenu()
        {
            var native = AccessTools.Field(typeof(EditHospitalMenu), "_tileMapPreviewButton")?
                .GetValue(_menu) as Button;
            var rootValue = AccessTools.Field(typeof(EditHospitalMenu), "_tileSelectionRoot")?
                .GetValue(_menu);
            var root = rootValue as Transform;
            if (root == null && rootValue is GameObject)
                root = ((GameObject)rootValue).transform;
            if (native == null || root == null) return;
            var tileTemplate = root.GetComponentInChildren<Button>(true) ?? _template;
            var button = CloneButton(tileTemplate, root, T("editor.toolbar.tile_view"),
                () => native.onClick.Invoke());
            button.gameObject.name = "UP View Tiles";
            var layout = button.GetComponent<LayoutElement>() ??
                button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 31f;
            button.transform.SetAsFirstSibling();
            native.gameObject.SetActive(false);
            _buttons.Add(button.gameObject);
        }

        private void AddTopEditorButtons()
        {
            var canvas = _menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _nativeObjectsButton = AccessTools.Field(typeof(EditHospitalMenu), "_objectsButton")?
                .GetValue(_menu) as Button;
            _nativeBuildButton = AccessTools.Field(typeof(EditHospitalMenu), "_buildButton")?
                .GetValue(_menu) as Button;
            _nativeCameraButton = AccessTools.Field(typeof(EditHospitalMenu), "_toggleCameraButton")?
                .GetValue(_menu) as Button;
            if (_nativeBuildButton != null)
            {
                _buildButton = AddTopButton(canvas.transform, T("editor.toolbar.edit_plot"),
                    -550f, () => ActivateNativeTool(_nativeBuildButton));
                _buildButton.gameObject.name = "UP Tiles Toolbar Button";
                _nativeBuildButton.gameObject.SetActive(false);
            }
            if (_nativeObjectsButton != null)
            {
                _objectsButton = AddTopButton(canvas.transform, T("editor.objects.title"),
                    -330f, () => ActivateNativeTool(_nativeObjectsButton));
                _objectsButton.gameObject.name = "UP Objects Dock Toggle";
                _nativeObjectsButton.gameObject.SetActive(false);
            }
            if (_nativeCameraButton != null)
            {
                _cameraButton = AddTopButton(canvas.transform, "", -110f, () =>
                {
                    _nativeCameraButton.onClick.Invoke();
                    RefreshCameraButton();
                });
                _cameraButton.gameObject.name = "UP Camera Toolbar Button";
                _cameraButtonText = _cameraButton.GetComponentInChildren<TMP_Text>(true);
                _nativeCameraButton.gameObject.SetActive(false);
                RefreshCameraButton();
            }
        }

        private static void ActivateNativeTool(Button native)
        {
            ScenarioCharacterViewer.Close();
            ScenarioFogViewer.Close();
            native?.onClick.Invoke();
        }

        private void StopNativeTools()
        {
            var cursor = _level?.CursorManager;
            if (cursor == null) return;
            cursor.PopMode<CursorEditHospitalItem>();
            cursor.PopMode<CursorEditHospitalBuild>();
            cursor.PopMode<CursorEditHospitalMovePlot>();
        }

        private Button AddTopButton(Transform parent, string label, float x, Action action)
        {
            var button = CloneButton(_template, parent, label, action);
            var rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(x, -12f);
                rect.sizeDelta = new Vector2(208f, 40f);
            }
            _buttons.Add(button.gameObject);
            return button;
        }

        private void RefreshCameraButton()
        {
            if (_cameraButtonText == null || _level?.CameraLogic == null) return;
            var field = AccessTools.Field(_level.CameraLogic.GetType(), "_boundCamera");
            var bounded = field?.GetValue(_level.CameraLogic) as bool?;
            _cameraButtonText.text = T(bounded == false
                ? "editor.toolbar.free_camera" : "editor.toolbar.fixed_camera");
        }

        private void SetNativeButtonLabel(string fieldName, string key)
        {
            var button = AccessTools.Field(typeof(EditHospitalMenu), fieldName)?
                .GetValue(_menu) as Button;
            if (button == null) return;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            label.text = T(key);
            label.enableAutoSizing = true;
            label.fontSizeMin = 11f;
            label.fontSizeMax = 17f;
        }

        private void AddLayerButtons()
        {
            var dropdownRect = _layerDropdown?.transform as RectTransform;
            if (dropdownRect == null) return;
            _layerDropdownCanvasGroup = _layerDropdown.GetComponent<CanvasGroup>();
            if (_layerDropdownCanvasGroup == null)
            {
                _layerDropdownCanvasGroup = _layerDropdown.gameObject.AddComponent<CanvasGroup>();
                _createdLayerDropdownCanvasGroup = true;
            }
            _layerDropdownOriginalAlpha = _layerDropdownCanvasGroup.alpha;
            _layerDropdownOriginalInteractable = _layerDropdownCanvasGroup.interactable;
            _layerDropdownOriginalBlocksRaycasts = _layerDropdownCanvasGroup.blocksRaycasts;
            _layerDropdownCanvasGroup.alpha = 0f;
            _layerDropdownCanvasGroup.interactable = false;
            _layerDropdownCanvasGroup.blocksRaycasts = false;

            _layerButtonsRoot = AddUi("UP Plot Layer Buttons", dropdownRect.parent);
            var root = _layerButtonsRoot.GetComponent<RectTransform>();
            root.anchorMin = dropdownRect.anchorMin;
            root.anchorMax = dropdownRect.anchorMax;
            root.pivot = dropdownRect.pivot;
            root.anchoredPosition = dropdownRect.anchoredPosition;
            root.sizeDelta = new Vector2(244f, dropdownRect.sizeDelta.y);
            root.localRotation = dropdownRect.localRotation;
            root.localScale = dropdownRect.localScale;
            var layout = _layerButtonsRoot.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var keys = new[] { "editor.layer.base", "editor.layer.built", "editor.layer.unbuilt" };
            for (var i = 0; i < _layerButtons.Length; ++i)
            {
                var layerIndex = i;
                var button = CloneButton(_template, root, T(keys[i]), () => SelectLayer(layerIndex));
                button.gameObject.name = "UP Plot Layer - " + ((HospitalPlotLayer)i);
                var rect = button.transform as RectTransform;
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(i / 3f, 0f);
                    rect.anchorMax = new Vector2((i + 1f) / 3f, 1f);
                    rect.pivot = new Vector2(.5f, .5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.zero;
                    rect.offsetMin = new Vector2(i == 0 ? 0f : 2f, 0f);
                    rect.offsetMax = new Vector2(i == 2 ? 0f : -2f, 0f);
                }
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 8f;
                    label.fontSizeMax = 14f;
                    label.alignment = TextAlignmentOptions.Center;
                }
                _layerButtons[i] = button;
            }
            _layerDropdown.onValueChanged.AddListener(OnLayerDropdownValueChanged);
            _level.HospitalEditEvents.OnSelectHospitalPlotLayer += OnLayerSelected;
            RefreshLayerButtons((HospitalPlotLayer)Mathf.Clamp(_layerDropdown.value, 0, 2));
            _buttons.Add(_layerButtonsRoot);
        }

        private void SelectLayer(int index)
        {
            if (_layerDropdown == null || index < 0 || index >= _layerButtons.Length) return;
            if (_layerDropdown.value != index)
                _layerDropdown.value = index;
            else
                _level.HospitalEditEvents.OnSelectHospitalPlotLayer?.Invoke((HospitalPlotLayer)index);
        }

        private void OnLayerDropdownValueChanged(int index) =>
            RefreshLayerButtons((HospitalPlotLayer)Mathf.Clamp(index, 0, 2));

        private void OnLayerSelected(HospitalPlotLayer layer)
        {
            var index = Mathf.Clamp((int)layer, 0, 2);
            if (_layerDropdown != null && _layerDropdown.value != index)
                _layerDropdown.SetValueWithoutNotify(index);
            RefreshLayerButtons((HospitalPlotLayer)index);
        }

        private void SetLayerControlsVisible(bool visible)
        {
            if (_layerButtonsRoot != null) _layerButtonsRoot.SetActive(visible);
        }

        private void RefreshLayerButtons(HospitalPlotLayer selected)
        {
            var nativeColors = AccessTools.Field(typeof(EditHospitalMenu), "_editFrameColors")
                ?.GetValue(_menu) as Color[];
            var fallback = new[]
            {
                new Color(.78f, .80f, .80f, 1f),
                new Color(.08f, .37f, .61f, 1f),
                new Color(.39f, .62f, .22f, 1f)
            };
            for (var i = 0; i < _layerButtons.Length; ++i)
            {
                var button = _layerButtons[i];
                if (button == null) continue;
                var active = i == (int)selected;
                var colors = button.colors;
                var activeColor = nativeColors != null && nativeColors.Length > i
                    ? nativeColors[i] : fallback[i];
                activeColor.a = 1f;
                colors.normalColor = active ? activeColor : new Color(.035f, .43f, .70f, 1f);
                colors.highlightedColor = active
                    ? Color.Lerp(activeColor, Color.white, .18f)
                    : new Color(.08f, .58f, .86f, 1f);
                colors.pressedColor = active
                    ? Color.Lerp(activeColor, Color.black, .18f)
                    : new Color(.025f, .32f, .55f, 1f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
            }
        }

        // Cabecera intercambiable justo encima del historial de objetos.
        private void AddMapIdentity()
        {
            var root = AddUi("UP Map Name or Coordinates", transform);
            _mapIdentityRoot = root;
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-20f, -12f);
            rect.sizeDelta = new Vector2(184f, 40f);
            var image = root.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                _showMapCoordinates = !_showMapCoordinates;
                if (_showMapCoordinates)
                {
                    _nextCursorCoordinateRefresh = 0f;
                    RefreshCursorCoordinates();
                }
                else
                    RefreshMapIdentity();
            });
            var textRoot = AddUi("Text", root.transform);
            Stretch(textRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            _mapIdentityLabel = textRoot.AddComponent<Text>();
            _mapIdentityLabel.font = _font;
            _mapIdentityLabel.fontSize = 15;
            _mapIdentityLabel.fontStyle = FontStyle.Bold;
            _mapIdentityLabel.alignment = TextAnchor.MiddleRight;
            _mapIdentityLabel.color = Color.white;
            _mapIdentityLabel.raycastTarget = false;
            _mapIdentityLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _mapIdentityLabel.verticalOverflow = VerticalWrapMode.Truncate;
            var shadow = root.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
            _buttons.Add(root);
        }

        private void RefreshMapIdentity()
        {
            if (_mapIdentityLabel == null || _level == null || _showMapCoordinates) return;
            _mapIdentityLabel.text = ScenarioEditorStorage.CurrentMapName(_level);
        }

        private void RefreshCursorCoordinates()
        {
            if (!_showMapCoordinates || _mapIdentityLabel == null ||
                Time.unscaledTime < _nextCursorCoordinateRefresh)
                return;
            _nextCursorCoordinateRefresh = Time.unscaledTime + 0.07f;
            var camera = Camera.main;
            if (camera == null || Input.mousePosition.x < 0f ||
                Input.mousePosition.y < 0f || Input.mousePosition.x > Screen.width ||
                Input.mousePosition.y > Screen.height)
            {
                _mapIdentityLabel.text = "X: --   Z: --";
                return;
            }
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            Vector3 point;
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, camera.farClipPlane))
                point = hit.point;
            else
            {
                float distance;
                if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out distance) ||
                    distance < 0f)
                {
                    _mapIdentityLabel.text = "X: --   Z: --";
                    return;
                }
                point = ray.GetPoint(distance);
            }
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            _mapIdentityLabel.text = "X: " + point.x.ToString("0.00", invariant) +
                "   Z: " + point.z.ToString("0.00", invariant);
        }

        private void AddToolbarButton(string name, int row, Action action)
        {
            var button = CloneButton(_template, transform, name, action);
            var rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-18f - row * 174f, -18f);
                rect.sizeDelta = new Vector2(168f, 34f);
            }
            _buttons.Add(button.gameObject);
        }

        private void AddStylesToolbarButton()
        {
            var canvas = _menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _stylesButton = AddTopButton(canvas.transform, T("editor.toolbar.styles"),
                550f, ShowStyles);
            _stylesButton.gameObject.name = "UP Styles Toolbar Button";
        }

        // Bloq Mayus intercala el aislamiento; el usuario tambien puede
        // pulsar el boton. El bloqueo nunca modifica la propiedad de un objeto.
        private void AddPlotIsolationButton()
        {
            if (_layerButtonsRoot == null) return;
            _plotIsolationButton = CloneButton(_template, _layerButtonsRoot.transform,
                T("editor.plot.isolate.off"), () => SetPlotIsolation(!_plotIsolation));
            var rect = _plotIsolationButton.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(2f / 3f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 4f);
                rect.sizeDelta = new Vector2(-4f, 26f);
            }
            RefreshPlotIsolationButton();
        }

        private void SetPlotIsolation(bool enabled)
        {
            if (_plotIsolation == enabled) return;
            if (enabled)
            {
                _plotVisibilityBeforeIsolation.Clear();
                foreach (var plot in _level.WorldState.HospitalPlots)
                    if (plot != null) _plotVisibilityBeforeIsolation[plot] = plot.IsVisible();
                _plotIsolation = true;
                ApplyPlotIsolation();
            }
            else
            {
                _plotIsolation = false;
                RestorePlotVisibility();
            }
            RefreshPlotIsolationButton();
        }

        private void ApplyPlotIsolation()
        {
            if (!_plotIsolation || _level?.WorldState?.HospitalPlots == null) return;
            foreach (var plot in _level.WorldState.HospitalPlots)
            {
                if (plot == null) continue;
                if (!_plotVisibilityBeforeIsolation.ContainsKey(plot))
                    _plotVisibilityBeforeIsolation[plot] = plot.IsVisible();
                var selected = ReferenceEquals(plot, _selectedPlot);
                if (plot.IsVisible() != selected) plot.SetVisible(selected);
            }
        }

        private void RestorePlotVisibility()
        {
            foreach (var original in _plotVisibilityBeforeIsolation)
                if (original.Key?.HospitalMap != null &&
                    original.Key.IsVisible() != original.Value)
                    original.Key.SetVisible(original.Value);
            _plotVisibilityBeforeIsolation.Clear();
        }

        private void RefreshPlotIsolationButton()
        {
            if (_plotIsolationButton == null) return;
            var label = _plotIsolationButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = T("editor.plot.isolate.off");
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = 17f;
            }
            SetToolButtonSelected(_plotIsolationButton, _plotIsolation);
        }

        private static void SetToolButtonSelected(Button button, bool selected)
        {
            if (button != null && button.image != null)
                button.image.color = selected
                    ? new Color(.02f, .55f, .90f, 1f)
                    : new Color(.035f, .43f, .70f, 1f);
        }

        private void OnPlotSelected(HospitalPlot plot)
        {
            // El juego vuelve a emitir el plot seleccionado al refrescar la
            // interfaz. No salir de Escenario si NO se ha escogido otra parcela.
            var changed = !ReferenceEquals(_selectedPlot, plot);
            _selectedPlot = plot;
            if (changed || ScenarioLandscape.IsActive)
            {
                ScenarioEditorScene.Deselect();
                ScenarioLandscape.Deselect();
                SetLayerControlsVisible(true);
                RefreshLandscapeButton();
            }
            if (_plotIsolation) ApplyPlotIsolation();
            RefreshMapIdentity();
        }

        private void InitialisePlotActions()
        {
            var root = AccessTools.Field(typeof(EditHospitalMenu),
                "_plotSelectionRoot")?.GetValue(_menu) as Transform;
            if (root == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("No hay panel nativo de parcelas.");
                return;
            }
            _plotInfoText = AccessTools.Field(typeof(EditHospitalMenu), "_infoText")?
                .GetValue(_menu) as TMP_Text;
            if (_plotInfoText != null && _layerButtonsRoot != null)
            {
                var infoRect = _plotInfoText.transform as RectTransform;
                infoRect.SetParent(_layerButtonsRoot.transform, false);
                infoRect.anchorMin = new Vector2(0f, 1f);
                infoRect.anchorMax = new Vector2(2f / 3f, 1f);
                infoRect.pivot = new Vector2(.5f, 0f);
                infoRect.anchoredPosition = new Vector2(0f, 4f);
                infoRect.sizeDelta = new Vector2(-4f, 26f);
                _plotInfoText.margin = Vector4.zero;
                _plotInfoText.alignment = TextAlignmentOptions.Center;
            }
            // Cabecera propia en el LayoutGroup del listado original.
            // NO clonar PlotSelectionItem: sus eventos y checks son privados
            // de una parcela y no deben aplicarse a Landscape.
            AddLandscapeSelector(root);
            var scroll = root.GetComponentInParent<ScrollRect>();
            _plotListViewport = scroll != null
                ? scroll.GetComponent<RectTransform>()
                : root.parent as RectTransform;
            _addPlotButton = CloneButton(_template, transform, T("editor.plot.add"), () =>
            {
                try { ScenarioEditorStorage.AddNewPlot(_level); }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                    ShowError(T("editor.plot.add.title"), T("editor.plot.add.error"));
                }
            });
            _deletePlotButton = CloneButton(_template, transform,
                T("editor.plot.delete"), ShowDeleteConfirmation);
            _nativeMoveButton = AccessTools.Field(typeof(EditHospitalMenu), "_moveButton")?
                .GetValue(_menu) as Button;
            if (_nativeMoveButton != null)
            {
                _movePlotButton = CloneButton(_template, transform,
                    T("editor.toolbar.move_plot"), () => _nativeMoveButton.onClick.Invoke());
                _nativeMoveButton.gameObject.SetActive(false);
                _buttons.Add(_movePlotButton.gameObject);
            }
            _buttons.Add(_addPlotButton.gameObject);
            _buttons.Add(_deletePlotButton.gameObject);
            PositionPlotActions();
        }

        private void AddLandscapeSelector(Transform listRoot)
        {
            var row = AddUi("UP Landscape - Row", listRoot);
            var rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(211f, 29f);
            var layout = row.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 29f;
            var background = row.AddComponent<Image>();
            background.color = new Color(0.29f, 0.34f, 0.38f, 0.96f);
            var button = row.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                ScenarioEditorScene.Deselect();
                ScenarioEditorObjectInteraction.ClearCurrentSelection();
                if (_plotIsolation) SetPlotIsolation(false);
                // Parcela 0: ninguna parcela aparece seleccionada, sin enviar
                // null al CursorEditHospital nativo (que no lo admite).
                _selectedPlot = null;
                var plotUiMethod = AccessTools.Method(typeof(PlotSelectionItem),
                    "OnSelectHospitalPlot");
                if (plotUiMethod != null)
                    foreach (var plotUi in listRoot.GetComponentsInChildren<PlotSelectionItem>(true))
                        plotUiMethod.Invoke(plotUi, new object[] { null });
                SetLayerControlsVisible(false);
                var plotInfo = AccessTools.Field(typeof(EditHospitalMenu),
                    "_infoText")?.GetValue(_menu) as TMP_Text;
                if (plotInfo != null) plotInfo.text = "Landscape";
                try { ScenarioLandscape.Activate(_level); }
                catch (Exception ex)
                {
                    ScenarioLandscape.Deselect();
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Landscape: error al iniciar seleccion exterior: " + ex);
                }
                RefreshLandscapeButton();
            });
            var label = AddUi("UP Landscape - Text", row.transform);
            Stretch(label.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var text = label.AddComponent<Text>();
            text.font = _font;
            text.fontSize = 13;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.text = "   Landscape";
            text.raycastTarget = false;
            AddLandscapeVisibilityCheck(row.transform);
            row.transform.SetAsFirstSibling();
            var outline = row.AddComponent<Outline>();
            outline.effectDistance = new Vector2(2f, -2f);
            outline.effectColor = new Color(0.07f, 0.72f, 1f, 1f);
            outline.enabled = false;
            _landscapeBorder = outline;
            _landscapeButton = button;
            _landscapeSelectBackground = background;
            _buttons.Add(row);
            RefreshLandscapeButton();
        }

        // Casilla blanca independiente de la seleccion. Solo activa/oculta
        // los objetos originales de Landscape; ninguna parcela cambia.
        private void AddLandscapeVisibilityCheck(Transform row)
        {
            var check = AddUi("UP Landscape - Visibility Check", row);
            var rect = check.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(1f, .5f);
            rect.anchoredPosition = new Vector2(-7f, 0f);
            rect.sizeDelta = new Vector2(19f, 19f);
            var background = check.AddComponent<Image>();
            background.color = Color.white;
            var interior = AddUi("UP Landscape - Check Interior", check.transform);
            var interiorRect = interior.GetComponent<RectTransform>();
            interiorRect.anchorMin = Vector2.zero;
            interiorRect.anchorMax = Vector2.one;
            interiorRect.offsetMin = new Vector2(2f, 2f);
            interiorRect.offsetMax = new Vector2(-2f, -2f);
            var dark = interior.AddComponent<Image>();
            dark.color = new Color(.18f, .22f, .26f, 1f);
            dark.raycastTarget = false;
            var mark = AddUi("UP Landscape - Check Mark", check.transform);
            Stretch(mark.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var tick = mark.AddComponent<Text>();
            tick.font = _font;
            tick.fontSize = 18;
            tick.fontStyle = FontStyle.Bold;
            tick.alignment = TextAnchor.MiddleCenter;
            tick.color = Color.white;
            tick.text = "✓";
            tick.raycastTarget = false;
            _landscapeVisibilityToggle = check.AddComponent<Toggle>();
            _landscapeVisibilityToggle.targetGraphic = background;
            _landscapeVisibilityToggle.graphic = tick;
            _landscapeVisibilityToggle.isOn = ScenarioLandscape.ObjectsVisible;
            _landscapeVisibilityToggle.onValueChanged.AddListener(visible =>
                ScenarioLandscape.SetObjectsVisible(visible));
        }

        private void RefreshLandscapeButton()
        {
            if (_landscapeButton != null)
            {
                var colors = _landscapeButton.colors;
                var active = ScenarioLandscape.IsActive;
                colors.normalColor = active
                    ? new Color(0.02f, 0.55f, 0.90f, 1f)
                    : new Color(0.23f, 0.37f, 0.49f, 0.94f);
                colors.highlightedColor = active
                    ? new Color(0.04f, 0.70f, 1f, 1f)
                    : new Color(0.31f, 0.48f, 0.61f, 1f);
                colors.pressedColor = new Color(0.02f, 0.42f, 0.72f, 1f);
                colors.selectedColor = colors.highlightedColor;
                _landscapeButton.colors = colors;
            }
            if (_landscapeBorder != null)
                _landscapeBorder.enabled = ScenarioLandscape.IsActive;
        }

        // Atajos limitados a la sesion del Editor. InputManager.GetKeyDown
        // ignora las teclas cuando el jugador esta editando un campo de texto.
        private void Update()
        {
            if (_level == null || _menu == null || !ScenarioEditorSession.Active ||
                !ReferenceEquals(ScenarioEditorSession.Level, _level) || IsDialogOpen)
                return;

            RefreshCursorCoordinates();
            RefreshCameraButton();
            if (_objectsButton != null && _nativeObjectsButton != null)
                _objectsButton.interactable = _nativeObjectsButton.interactable;
            if (_buildButton != null && _nativeBuildButton != null)
                _buildButton.interactable = _nativeBuildButton.interactable;
            if (_cameraButton != null && _nativeCameraButton != null)
                _cameraButton.interactable = _nativeCameraButton.interactable;
            if (_movePlotButton != null && _nativeMoveButton != null)
                _movePlotButton.interactable = _nativeMoveButton.interactable;
            var customToolOpen = ScenarioCharacterViewer.IsOpen || ScenarioFogViewer.IsOpen;
            var cursor = _level.CursorManager;
            SetToolButtonSelected(_objectsButton, !customToolOpen && cursor != null &&
                cursor.IsModeActive<CursorEditHospitalItem>());
            SetToolButtonSelected(_buildButton, !customToolOpen && cursor != null &&
                cursor.IsModeActive<CursorEditHospitalBuild>());
            SetToolButtonSelected(_movePlotButton, !customToolOpen && cursor != null &&
                cursor.IsModeActive<CursorEditHospitalMovePlot>());
            SetToolButtonSelected(_stylesButton, _stylesDialogOpen);
            var input = _level.InputManager;
            if (input == null) return;
            ScenarioLandscape.UpdateSelection();
            if (Input.GetKeyDown(KeyCode.CapsLock) && !TextFieldFocused())
            {
                SetPlotIsolation(!_plotIsolation);
                return;
            }
            // Capa Escenario suspendida hasta que se identifiquen objetos
            // originales editables sin duplicar las parcelas.

            for (int i = 0; i < ScenarioEditorHotkeyMap.PlotKeys.Length; i++)
            {
                if (!input.GetKeyDown(ScenarioEditorHotkeyMap.PlotKeys[i])) continue;
                var plots = _level.WorldState?.HospitalPlots;
                if (plots != null && i < plots.Count)
                    _level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plots[i]);
                return;
            }

            // Landscape no tiene Base / Built / Unbuilt.
            if (ScenarioLandscape.IsActive) return;
            for (int i = 0; i < ScenarioEditorHotkeyMap.LayerKeys.Length; i++)
            {
                if (!input.GetKeyDown(ScenarioEditorHotkeyMap.LayerKeys[i])) continue;
                // Pasar por el desplegable nativo para mantener sincronizados
                // el estado del cursor, el color del marco y el texto Base/Built/Unbuilt.
                if (_layerDropdown != null)
                    _layerDropdown.value = i;
                else
                    _level.HospitalEditEvents.OnSelectHospitalPlotLayer?.Invoke((HospitalPlotLayer)i);
                return;
            }
        }

        private static bool TextFieldFocused()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            return selected != null &&
                (selected.GetComponentInParent<TMP_InputField>() != null ||
                 selected.GetComponentInParent<InputField>() != null);
        }

        private void LateUpdate()
        {
            // Los tamaños del editor nativo se calculan por LayoutGroup.
            // Anclar los botones a la base real de la lista evita que se muevan
            // cuando hay muchas parcelas o se modifica la resolucion.
            PositionPlotActions();
            RefreshPlotSizeText();
            if (_initialVisibilityFrames > 0)
            {
                --_initialVisibilityFrames;
                if (_initialVisibilityFrames == 0 || _initialVisibilityFrames == 8 ||
                    _initialVisibilityFrames == 4)
                    ReapplyVisiblePlotItems();
            }
        }

        internal static void NotifyLoadedMap()
        {
            ScenarioEditorObjectInteraction.ClearCurrentSelection();
            ScenarioLandscape.RefreshAfterMapLoad();
            if (_active == null) return;
            if (_active._landscapeVisibilityToggle != null)
                _active._landscapeVisibilityToggle.SetIsOnWithoutNotify(
                    ScenarioLandscape.ObjectsVisible);
            _active._initialVisibilityFrames = 12;
            // Los antiguos objetos HospitalPlot se destruyeron al cargar.
            // Olvidar esas referencias y aplicar el bloqueo a las parcelas nuevas.
            var plots = _active._level?.WorldState?.HospitalPlots;
            if (plots != null)
                foreach (var old in _active._plotVisibilityBeforeIsolation.Keys.ToArray())
                    if (!plots.Contains(old))
                        _active._plotVisibilityBeforeIsolation.Remove(old);
            if (_active._plotIsolation) _active.ApplyPlotIsolation();
        }

        // El juego regenera las visuales despues de construir los botones
        // nativos. Reaplicar la visibilidad inicial evita tener que apagar y
        // encender sus casillas para que aparezca la decoracion.
        private void ReapplyVisiblePlotItems()
        {
            var plots = _level?.WorldState?.HospitalPlots;
            if (plots == null) return;
            foreach (var plot in plots)
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                // Respetar la casilla nativa de visibilidad de la parcela.
                // Nunca forzar visibles los objetos de una parcela oculta.
                if (!plot.IsVisible()) continue;
                foreach (var item in items)
                {
                    var visual = item?.Visual?.GameObject;
                    if (visual != null && !visual.activeSelf)
                        visual.SetActive(true);
                }
            }
        }

        private void PositionPlotActions()
        {
            if (_plotListViewport == null || _addPlotButton == null ||
                _movePlotButton == null || _deletePlotButton == null)
                return;
            var parentRect = transform as RectTransform;
            if (parentRect == null) return;
            var corners = new Vector3[4];
            _plotListViewport.GetWorldCorners(corners);
            var width = Mathf.Abs((corners[3] - corners[0]).magnitude /
                                  Mathf.Max(parentRect.lossyScale.x, 0.0001f));
            var buttonWidth = Mathf.Max(52f, (width - 8f) / 3f);
            var add = _addPlotButton.transform as RectTransform;
            var move = _movePlotButton.transform as RectTransform;
            var remove = _deletePlotButton.transform as RectTransform;
            if (add == null || move == null || remove == null) return;
            foreach (var rect in new[] { add, move, remove })
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(buttonWidth, 31f);
            }
            // El origen de cada boton es su esquina superior izquierda.
            var offset = parentRect.TransformVector(new Vector3(2f, -4f, 0f));
            add.position = corners[0] + offset;
            move.position = corners[0] + offset +
                            parentRect.TransformVector(new Vector3(buttonWidth + 2f, 0f, 0f));
            remove.position = corners[0] + offset +
                              parentRect.TransformVector(new Vector3((buttonWidth + 2f) * 2f, 0f, 0f));
        }

        private void RefreshPlotSizeText()
        {
            if (_plotInfoText == null || _selectedPlot?.HospitalMap?.FloorPlan == null) return;
            // Misma medida y unidad que EditHospitalMenu.OnHospitalPlotUpdated, sin prefijo.
            var size = _selectedPlot.HospitalMap.FloorPlan.TileCount * MathUtils.Square(2);
            _plotInfoText.text = size + " m²";
        }

        private HospitalPlot SelectedPlot()
        {
            var plots = _level?.WorldState?.HospitalPlots;
            if (plots == null || plots.Count == 0) return null;
            return plots.FirstOrDefault(plot => ReferenceEquals(plot, _selectedPlot));
        }

        private void ShowError(string title, string message)
        {
            BuildDialog(title);
            if (_dialog != null)
                ShowStatus(message, true);
        }

        private void ShowDeleteConfirmation()
        {
            var selected = SelectedPlot();
            if (selected == null)
            {
                ShowError(T("editor.plot.delete.title"),
                    T("editor.plot.delete.select_first"));
                return;
            }
            if (_level.WorldState.HospitalPlots.Count <= 1)
            {
                ShowError(T("editor.plot.delete.title"),
                    T("editor.plot.delete.minimum"));
                return;
            }
            var index = _level.WorldState.HospitalPlots.IndexOf(selected);
            BuildDialog(T("editor.plot.delete.title"));
            if (_dialog == null) return;
            AddText(_dialogPanel,
                EditorLocalization.Format("editor.plot.delete.confirm", index + 1),
                25, 88, 482, 135, 19, TextAnchor.MiddleCenter);
            var close = _dialogPanel.Find("UP Map - Cerrar");
            if (close != null)
            {
                var cancel = close.GetComponentInChildren<TMP_Text>(true);
                if (cancel != null) cancel.text = T("common.cancel");
                else
                {
                    var ordinary = close.GetComponentInChildren<Text>(true);
                    if (ordinary != null) ordinary.text = T("common.cancel");
                }
            }
            AddDialogButton(T("editor.plot.delete"), new Vector2(-85f, -289f), () =>
            {
                try
                {
                    ScenarioEditorStorage.DeletePlot(_level, selected);
                    CloseDialog();
                }
                catch (Exception exception)
                {
                    ShowStatus(T("editor.plot.delete.error"), true);
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                }
            });
        }

        private void ShowEmptyMapConfirmation()
        {
            BuildDialog(T("editor.map.empty"));
            if (_dialog == null) return;
            AddText(_dialogPanel, T("editor.map.empty.warning"),
                24f, 72f, 484f, 170f, 19, TextAnchor.MiddleCenter);
            SetDialogCloseLabel(T("common.cancel"));
            AddDialogButton(T("editor.map.empty.accept"), new Vector2(-85f, -289f), () =>
            {
                CloseDialog();
                ScenarioEditorTerrainPainter.ClearMap(_level);
            });
        }

        private void SetDialogCloseLabel(string value)
        {
            var close = _dialogPanel?.Find("UP Map - Cerrar");
            if (close == null) return;
            var label = close.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = value;
                return;
            }
            var ordinary = close.GetComponentInChildren<Text>(true);
            if (ordinary != null) ordinary.text = value;
        }

        private void ShowSave()
        {
            BuildDialog(T("editor.map.save"));
            if (_dialog == null) return;
            AddText(_dialogPanel, T("editor.map.name"), 16, 62, 500, 28, 20, TextAnchor.MiddleLeft);
            var inputRoot = AddUi("Nombre editable", _dialogPanel);
            SetRect(inputRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(16f, -94f), new Vector2(500f, 37f));
            var inputBackground = inputRoot.AddComponent<Image>();
            inputBackground.color = new Color(0.96f, 0.96f, 0.96f, 1f);
            _nameInput = inputRoot.AddComponent<InputField>();
            _nameInput.targetGraphic = inputBackground;
            _nameInput.characterLimit = 80;
            var text = AddText(inputRoot.transform, "", 10, 3, 478, 31, 19, TextAnchor.MiddleLeft);
            text.color = Color.black;
            text.supportRichText = false;
            _nameInput.textComponent = text;
            // La etiqueta InputText del campo NO contiene texto fijo: Unity
            // escribira aqui el valor de InputField y nunca se solapara.
            _nameInput.text = string.IsNullOrWhiteSpace(ScenarioEditorStorage.NameForSave(_level))
                ? T("editor.map.new_default") : ScenarioEditorStorage.NameForSave(_level);
            AddDialogButton(T("common.save"), new Vector2(-85f, -289f), () =>
            {
                try
                {
                    var path = ScenarioEditorStorage.Save(_level, _nameInput.text);
                    RefreshMapIdentity();
                    ShowStatus(EditorLocalization.Format("editor.map.saved",
                        System.IO.Path.GetFileName(path)), false);
                }
                catch (Exception exception)
                {
                    ShowStatus(T("editor.map.save.error"), true);
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                }
            });
        }

        private void ShowStyles()
        {
            if (_stylesDialogOpen)
            {
                CloseDialog();
                return;
            }
            BuildDialog(T("editor.map.styles"));
            _stylesDialogOpen = _dialog != null;
            if (_dialog == null) return;
            SetToolButtonSelected(_stylesButton, true);
            // El fondo modal cubre el Canvas. Mantener este conmutador por
            // encima permite cerrarlo pulsando de nuevo el mismo botón.
            _stylesButton.transform.SetAsLastSibling();
            var catalog = ScenarioHospitalStyles.Build(_level,
                T("editor.map.style.original"),
                T("editor.map.style.none"));
            AddStyleRow(T("editor.map.style.perimeter"), 66f,
                catalog.Perimeters, choice =>
                {
                    var current = ScenarioEditorStorage.CurrentStyles(_level);
                    current.plotPerimeter = choice.Mode;
                    current.perimeterPrefabs = choice.PrefabNames ?? new string[0];
                    ScenarioEditorStorage.SetStyles(_level, current);
                }, () =>
                {
                    var current = ScenarioEditorStorage.CurrentStyles(_level);
                    return catalog.Perimeters.FindIndex(choice =>
                        choice.Mode == current.plotPerimeter &&
                        (choice.Mode != ScenarioMapFormat.PerimeterCustom ||
                         choice.PrefabNames.SequenceEqual(current.perimeterPrefabs)));
                });
            AddRoomStyleRow(T("editor.map.style.built"), 111f,
                catalog.Built, map => map.builtRoomStyle, (map, value) => map.builtRoomStyle = value);
            AddRoomStyleRow(T("editor.map.style.building"), 156f,
                catalog.Building, map => map.buildingRoomStyle, (map, value) => map.buildingRoomStyle = value);
            AddRoomStyleRow(T("editor.map.style.unbuilt"), 201f,
                catalog.Unbuilt, map => map.unbuiltRoomStyle, (map, value) => map.unbuiltRoomStyle = value);
            AddText(_dialogPanel, T("editor.map.style.help"),
                20f, 247f, 492f, 38f, 15, TextAnchor.MiddleCenter);
        }

        private void AddRoomStyleRow(string title, float y,
            IList<ScenarioHospitalStyles.RoomChoice> choices,
            Func<ScenarioMapFormat.Map, string> get,
            Action<ScenarioMapFormat.Map, string> set)
        {
            AddText(_dialogPanel, title, 18f, y, 220f, 35f, 17, TextAnchor.MiddleLeft);
            var current = ScenarioEditorStorage.CurrentStyles(_level);
            var index = choices.ToList().FindIndex(choice =>
                string.Equals(choice.Id, get(current), StringComparison.Ordinal));
            if (index < 0) index = 0;
            Button button = null;
            Action refresh = () =>
            {
                var label = button?.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = choices[index].Label;
            };
            button = AddDialogButton(choices[index].Label, new Vector2(120f, -y - 17f), () =>
            {
                index = (index + 1) % choices.Count;
                var map = ScenarioEditorStorage.CurrentStyles(_level);
                set(map, choices[index].Id);
                ScenarioEditorStorage.SetStyles(_level, map);
                refresh();
            });
            var rect = button.transform as RectTransform;
            if (rect != null) rect.sizeDelta = new Vector2(250f, 35f);
        }

        private void AddStyleRow(string title, float y,
            IList<ScenarioHospitalStyles.PerimeterChoice> choices,
            Action<ScenarioHospitalStyles.PerimeterChoice> select,
            Func<int> currentIndex)
        {
            AddText(_dialogPanel, title, 18f, y, 220f, 35f, 17, TextAnchor.MiddleLeft);
            var index = currentIndex();
            if (index < 0) index = 0;
            Button button = null;
            Action refresh = () =>
            {
                var label = button?.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = choices[index].Label;
            };
            button = AddDialogButton(choices[index].Label, new Vector2(120f, -y - 17f), () =>
            {
                index = (index + 1) % choices.Count;
                select(choices[index]);
                refresh();
            });
            var rect = button.transform as RectTransform;
            if (rect != null) rect.sizeDelta = new Vector2(250f, 35f);
        }

        private void ShowLoad()
        {
            BuildDialog(T("editor.map.open") + " .upmap");
            if (_dialog == null) return;
            var list = ScenarioEditorStorage.ListSaved();
            if (list.Count == 0)
            {
                AddText(_dialogPanel, T("editor.map.none"),
                    20, 100, 480, 40, 18, TextAnchor.MiddleCenter);
                return;
            }
            var scrollRoot = AddUi("Lista de mapas", _dialogPanel);
            SetRect(scrollRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -63f),
                new Vector2(500f, 195f));
            var scroll = scrollRoot.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewport = AddUi("Viewport", scrollRoot.transform);
            Stretch(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var viewImage = viewport.AddComponent<Image>();
            viewImage.color = new Color(0f, 0f, 0f, 0.14f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var content = AddUi("Contenido", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 6, 6);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;

            foreach (var map in list)
            {
                var selected = map;
                var matches = map.Valid && string.Equals(map.BaseId, _level.Config.UniqueId,
                    StringComparison.Ordinal);
                var name = map.Name + "  —  " + map.BaseName;
                if (!map.Valid) name += T("editor.map.invalid_suffix");
                else if (!matches) name += T("editor.map.other_hospital_suffix");
                var button = CloneButton(_template, content.transform, name, () =>
                {
                    try
                    {
                        ScenarioEditorStorage.Load(_level, selected.Path);
                        RefreshMapIdentity();
                        CloseDialog();
                    }
                    catch (Exception exception)
                    {
                        ShowStatus(T("editor.map.open.error"), true);
                        UnderPressureEditorPlugin.Log?.LogError(exception);
                    }
                });
                button.interactable = matches;
                var element = button.gameObject.GetComponent<LayoutElement>() ??
                              button.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 39f;
                element.preferredHeight = 39f;
                element.flexibleWidth = 1f;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.fontSize = Mathf.Min(label.fontSize, 19f);
                    // Reservar una franja a la derecha para la fecha.
                    var labelRect = label.transform as RectTransform;
                    if (labelRect != null)
                        labelRect.offsetMax = new Vector2(Mathf.Min(labelRect.offsetMax.x, -100f),
                            labelRect.offsetMax.y);
                }
                var dateRoot = AddUi("Fecha del mapa", button.transform);
                var dateRect = dateRoot.GetComponent<RectTransform>();
                dateRect.anchorMin = new Vector2(1f, 0f);
                dateRect.anchorMax = new Vector2(1f, 1f);
                dateRect.pivot = new Vector2(1f, 0.5f);
                dateRect.sizeDelta = new Vector2(100f, 0f);
                dateRect.anchoredPosition = new Vector2(-9f, 0f);
                var date = dateRoot.AddComponent<Text>();
                date.font = _font;
                date.fontSize = 14;
                date.fontStyle = FontStyle.Normal;
                date.text = map.ModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy");
                date.alignment = TextAnchor.MiddleRight;
                date.color = new Color(1f, 1f, 1f, 0.57f);
                date.raycastTarget = false;
            }
            // La lista se crea despues del boton Cerrar. Poner Cerrar encima
            // de cualquier elemento desplazable para que siempre reciba clics.
            var close = _dialogPanel.Find("UP Map - Cerrar");
            if (close != null) close.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        private void BuildDialog(string title)
        {
            CloseDialog();
            ScenarioEditorTerrainPainter.DisableTools();
            var canvas = _menu.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se encontro el Canvas del editor.");
                return;
            }
            var backdrop = AddUi("UP Map Dialog Background", canvas.transform);
            Stretch(backdrop.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            backdrop.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.68f);
            backdrop.transform.SetAsLastSibling();
            _dialog = backdrop;
            var panel = AddUi("Panel", backdrop.transform);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(532f, 372f);
            rect.anchoredPosition = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0.18f, 0.21f, 0.25f, 0.98f);
            _dialogPanel = panel.transform;
            AddText(_dialogPanel, title, 14, 12, 500, 42, 26, TextAnchor.MiddleCenter);
            _statusText = AddText(_dialogPanel, "", 17, 302, 493, 27, 16, TextAnchor.MiddleLeft);
            _statusText.color = Color.white;
            AddReliableCloseButton();
        }

        private Transform _dialogPanel;

        private void AddReliableCloseButton()
        {
            // NO clonar el Button de salida del editor: incluye callbacks nativos
            // de salida y puede conservar listeners persistentes del prefab.
            var root = AddUi("UP Map - Cerrar", _dialogPanel);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(85f, -289f);
            rect.sizeDelta = new Vector2(158f, 35f);

            var background = root.AddComponent<Image>();
            var nativeGraphic = _template != null ? _template.targetGraphic as Image : null;
            if (nativeGraphic == null && _template != null)
                nativeGraphic = _template.GetComponent<Image>();
            if (nativeGraphic != null)
            {
                background.sprite = nativeGraphic.sprite;
                background.type = nativeGraphic.type;
                background.color = nativeGraphic.color;
                background.pixelsPerUnitMultiplier = nativeGraphic.pixelsPerUnitMultiplier;
            }
            else background.color = new Color(0.53f, 0.62f, 0.65f, 1f);
            background.raycastTarget = true;

            var button = root.AddComponent<Button>();
            button.targetGraphic = background;
            if (_template != null)
            {
                button.transition = _template.transition;
                button.colors = _template.colors;
                button.spriteState = _template.spriteState;
            }
            button.onClick.AddListener(CloseDialog);

            var textRoot = AddUi("Etiqueta", root.transform);
            Stretch(textRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var nativeLabel = _template != null ? _template.GetComponentInChildren<TMP_Text>(true) : null;
            if (nativeLabel != null)
            {
                var label = UnityEngine.Object.Instantiate(nativeLabel, textRoot.transform, false);
                var labelRect = label.transform as RectTransform;
                if (labelRect != null) Stretch(labelRect, Vector2.zero, Vector2.one);
                foreach (var component in label.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                        component.enabled = false;
                label.text = T("editor.map.close");
                label.enableAutoSizing = false;
                label.fontSize = Mathf.Min(label.fontSize, 17f);
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                label.gameObject.SetActive(true);
            }
            else
            {
                var label = textRoot.AddComponent<Text>();
                label.font = _font;
                label.text = T("editor.map.close");
                label.fontSize = 17;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.raycastTarget = false;
            }
            root.transform.SetAsLastSibling();
        }

        private Button AddDialogButton(string title, Vector2 localPosition, Action action)
        {
            var copy = CloneButton(_template, _dialogPanel, title, action);
            var rect = copy.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = localPosition;
                rect.sizeDelta = new Vector2(158f, 35f);
            }
            return copy;
        }

        private static Button CloneButton(Button source, Transform parent, string text, Action action)
        {
            var button = UnityEngine.Object.Instantiate(source, parent, false);
            button.gameObject.name = "UP Map - " + text;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
            button.gameObject.SetActive(true);
            return button;
        }

        private Text AddText(Transform parent, string content, float x, float y, float width,
            float height, int size, TextAnchor alignment)
        {
            var root = AddUi("Text", parent);
            SetRect(root.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
            var label = root.AddComponent<Text>();
            label.font = _font;
            label.text = content;
            label.fontSize = size;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private void ShowStatus(string message, bool error)
        {
            if (_statusText == null) return;
            _statusText.text = message;
            _statusText.color = error ? new Color(1f, 0.54f, 0.44f, 1f) : Color.white;
        }

        private static GameObject AddUi(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            return root;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot,
            Vector2 anchored, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = anchored;
            rect.sizeDelta = size;
        }

        private void CloseDialog()
        {
            // Ocultar en el mismo frame; Destroy se completa al final del frame.
            if (_dialog != null)
            {
                _dialog.SetActive(false);
                UnityEngine.Object.Destroy(_dialog);
            }
            _dialog = null;
            _stylesDialogOpen = false;
            SetToolButtonSelected(_stylesButton, false);
            _dialogPanel = null;
            _nameInput = null;
            _statusText = null;
        }

        private void OnDestroy()
        {
            ScenarioLandscape.Release();
            if (_plotIsolation) SetPlotIsolation(false);
            if (_level != null)
            {
                _level.HospitalEditEvents.OnSelectHospitalPlot -= OnPlotSelected;
                _level.HospitalEditEvents.OnSelectHospitalPlotLayer -= OnLayerSelected;
            }
            if (_layerDropdown != null)
                _layerDropdown.onValueChanged.RemoveListener(OnLayerDropdownValueChanged);
            if (_layerDropdownCanvasGroup != null)
            {
                _layerDropdownCanvasGroup.alpha = _layerDropdownOriginalAlpha;
                _layerDropdownCanvasGroup.interactable = _layerDropdownOriginalInteractable;
                _layerDropdownCanvasGroup.blocksRaycasts = _layerDropdownOriginalBlocksRaycasts;
                if (_createdLayerDropdownCanvasGroup)
                    UnityEngine.Object.Destroy(_layerDropdownCanvasGroup);
            }
            if (_template != null) _template.gameObject.SetActive(_templateWasActive);
            if (ReferenceEquals(_active, this)) _active = null;
            CloseDialog();
            foreach (var button in _buttons)
                if (button != null) UnityEngine.Object.Destroy(button);
            _buttons.Clear();
        }
    }

    // En el Editor se reservan las teclas numericas para seleccionar parcelas
    // y F1-F3 para seleccionar las capas. Fuera del Editor, controles intactos.
    internal static class ScenarioEditorHotkeyMap
    {
        internal const KeyCode SceneKey = KeyCode.BackQuote;
        internal static bool SceneKeyPressed()
        {
            // BackQuote identifica la tecla fisica en teclados US; Unity puede
            // traducirla a º/ª en una distribucion española.
            if (UnityEngine.Input.GetKeyDown(SceneKey)) return true;
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (selected != null &&
                (selected.GetComponentInParent<TMP_InputField>() != null ||
                 selected.GetComponentInParent<InputField>() != null)) return false;
            var typed = UnityEngine.Input.inputString;
            return !string.IsNullOrEmpty(typed) &&
                (typed.IndexOf('º') >= 0 || typed.IndexOf('ª') >= 0);
        }
        internal static readonly KeyCode[] PlotKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8,
            KeyCode.Alpha9, KeyCode.Alpha0
        };

        internal static readonly KeyCode[] LayerKeys =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3
        };

        internal static bool HasReservedKeyDown()
        {
            if (ScenarioEditorSession.Active && UnityEngine.Input.GetKeyDown(KeyCode.CapsLock))
                return true;
            foreach (var key in PlotKeys)
                if (UnityEngine.Input.GetKeyDown(key)) return true;
            foreach (var key in LayerKeys)
                if (UnityEngine.Input.GetKeyDown(key)) return true;
            return false;
        }
    }

    // Los atajos de juego son acciones de Rewired: bloquearlas solo en los
    // fotogramas de nuestros atajos evita abrir menus de hospital en paralelo.
    [HarmonyPatch(typeof(InputManager), "GetButtonDown")]
    internal static class ScenarioEditorReserveNativeHotkeysPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorHotkeyMap.HasReservedKeyDown())
                return true;
            __result = false;
            return false;
        }
    }

    // Las vallas naranjas se crean en HospitalPlotFootprintPerimeter.Refresh()
    // usando WorldState.PerimeterPrefabs. Omitir BuildPerimeter no elimina las
    // que se crearon ANTES de que ScenarioEditorSession.Active fuese true.
    // Ocultamos las raices de esos perimetros mientras corre el Editor; solo
    // los objetos de ese sistema, nunca la decoracion ni los muros del hospital.
    internal static class ScenarioEditorPerimeterVisibility
    {
        private static readonly System.Reflection.FieldInfo RootField =
            AccessTools.Field(typeof(HospitalPlotFootprintPerimeter), "_root");
        private static readonly Dictionary<Transform, bool> PriorVisibility =
            new Dictionary<Transform, bool>();

        internal static void Hide(HospitalPlotFootprintPerimeter perimeter)
        {
            if (!ScenarioEditorFlow.IsRunning || perimeter == null || RootField == null) return;
            var root = RootField.GetValue(perimeter) as Transform;
            if (root == null || root.gameObject == null) return;
            if (!PriorVisibility.ContainsKey(root))
            {
                PriorVisibility.Add(root, root.gameObject.activeSelf);
                if (perimeter.Instances.Count > 0)
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Editor: ocultando perimetro con " + perimeter.Instances.Count +
                        " objetos nativos.");
            }
            root.gameObject.SetActive(false);
        }

        internal static void HideExisting()
        {
            if (!ScenarioEditorFlow.IsRunning) return;
            var level = ScenarioEditorSession.Level;
            if (level?.WorldState?.HospitalPlots == null) return;
            foreach (var plot in level.WorldState.HospitalPlots)
                if (plot?.HospitalMap != null)
                    Hide(plot.HospitalMap.FootprintPerimeter);
        }

        internal static void Restore()
        {
            foreach (var entry in PriorVisibility)
                if (entry.Key != null && entry.Key.gameObject != null)
                    entry.Key.gameObject.SetActive(entry.Value);
PriorVisibility.Clear();
        }
    }

    // El constructor tambien se ejecuta al generar mapas y parcelas nuevos.
    // Ocultar la raiz antes de que Refresh agregue cualquier prefab hace que
    // ningun objeto de ese sistema pueda aparecer en el Editor.
    [HarmonyPatch(typeof(HospitalPlotFootprintPerimeter), MethodType.Constructor,
        new[] { typeof(GameObject[]), typeof(float), typeof(bool) })]
    internal static class ScenarioEditorPerimeterConstructorPatch
    {
        private static void Postfix(HospitalPlotFootprintPerimeter __instance) =>
            ScenarioEditorPerimeterVisibility.Hide(__instance);
    }

    // Ocultar tambien los perimetros que ya existian al abrir el Editor.
    [HarmonyPatch(typeof(HospitalEditEvents), "StartEdit")]
    internal static class ScenarioEditorExistingPerimetersPatch
    {
        private static void Postfix() => ScenarioEditorPerimeterVisibility.HideExisting();
    }

    // En el juego jugable, BuildPerimeter conserva su comportamiento nativo.
    [HarmonyPatch(typeof(HospitalMap), "BuildPerimeter")]
    internal static class ScenarioEditorSkipPlotPerimeterPatch
    {
        private static bool Prefix() => !ScenarioEditorFlow.IsRunning;
    }

    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioEditorPerimeterVisibilityRestorePatch
    {
        private static void Postfix() => ScenarioEditorPerimeterVisibility.Restore();
    }

    // Los cursores nativos no deben recibir clics mientras se abre el cuadro de mapas.
    [HarmonyPatch(typeof(CursorEditHospitalBuild), "CursorUpdate")]
    internal static class ScenarioEditorBlockBuildDuringDialogPatch
    {
        private static bool Prefix() => !ScenarioEditorStorageMenu.IsDialogOpen;
    }

    [HarmonyPatch(typeof(CursorEditHospitalMovePlot), "CursorUpdate")]
    internal static class ScenarioEditorBlockMoveDuringDialogPatch
    {
        private static bool Prefix() => !ScenarioEditorStorageMenu.IsDialogOpen;
    }

    [HarmonyPatch(typeof(CursorEditHospitalItem), "CursorUpdate")]
    internal static class ScenarioEditorBlockItemsDuringDialogPatch
    {
        private static bool Prefix() => !ScenarioEditorStorageMenu.IsDialogOpen;
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioEditorStorageUiPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level __0)
        {
            if (!ScenarioEditorSession.Active || __instance == null || __0 == null) return;
            var component = __instance.GetComponent<ScenarioEditorStorageMenu>() ??
                            __instance.gameObject.AddComponent<ScenarioEditorStorageMenu>();
            component.Initialise(__instance, __0);
        }
    }

    // El HUD ya instancia una copia del prefab de pausa para cada nivel.
    // Personalizamos únicamente esa instancia durante el editor; el menú del
    // juego normal y su prefab permanecen intactos.
    [HarmonyPatch(typeof(PauseMenu), "Setup")]
    internal static class ScenarioEditorPauseMenuPatch
    {
        private static readonly FieldInfo QuickSaveField =
            AccessTools.Field(typeof(PauseMenu), "_quickSaveButton");
        private static readonly FieldInfo QuickLoadField =
            AccessTools.Field(typeof(PauseMenu), "_quickLoadButton");
        private static readonly FieldInfo RestartField =
            AccessTools.Field(typeof(PauseMenu), "_restartLevelButton");

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PauseMenu __instance)
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active ||
                __instance == null) return;
            __instance.gameObject.name = "UP Editor Pause Menu";
            Configure(QuickSaveField?.GetValue(__instance) as Button,
                "editor.map.save", ScenarioEditorStorageMenu.ShowSaveFromPause);
            Configure(QuickLoadField?.GetValue(__instance) as Button,
                "editor.map.open", ScenarioEditorStorageMenu.ShowLoadFromPause);
            Configure(RestartField?.GetValue(__instance) as Button,
                "editor.map.empty", ScenarioEditorStorageMenu.ShowEmptyMapFromPause);
        }

        private static void Configure(Button button, string key, Action action)
        {
            if (button == null) return;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = EditorLocalization.Get(key);
                return;
            }
            var ordinary = button.GetComponentInChildren<Text>(true);
            if (ordinary != null) ordinary.text = EditorLocalization.Get(key);
        }
    }

    // El primer Escape cierra el cuadro propio del editor. El siguiente vuelve
    // a pasar al juego y abre su menú de pausa normal.
    [HarmonyPatch(typeof(Level), "HandlePauseLogic")]
    internal static class ScenarioEditorModalEscapePatch
    {
        private static bool Prefix(Level __instance)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning ||
                !Input.GetKeyDown(KeyCode.Escape)) return true;
            if (ScenarioCharacterViewer.IsOpen)
            {
                ScenarioCharacterViewer.Close();
                return false;
            }
            if (ScenarioFogViewer.IsOpen)
            {
                ScenarioFogViewer.Close();
                return false;
            }
            if (ScenarioEditorStorageMenu.CloseOpenDialog()) return false;
            if (ScenarioEditorTerrainPainter.CloseDialogIfOpen()) return false;
            var cursor = __instance?.CursorManager;
            if (cursor != null && cursor.IsModeActive<CursorEditHospitalItem>())
            {
                cursor.PopMode<CursorEditHospitalItem>();
                return false;
            }
            if (cursor != null && cursor.IsModeActive<CursorEditHospitalBuild>())
            {
                cursor.PopMode<CursorEditHospitalBuild>();
                return false;
            }
            if (cursor != null && cursor.IsModeActive<CursorEditHospitalMovePlot>())
            {
                cursor.PopMode<CursorEditHospitalMovePlot>();
                return false;
            }
            return true;
        }
    }
}
// ===== END ORIGINAL: ScenarioEditorStorage.cs =====
