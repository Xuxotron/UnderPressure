// Updated: 2026-10-13 - Crear parcelas nuevas sin prefabs perimetrales
// Fix: cerrar dialogo siempre y guardar identidad de prefab.
// Crear parcelas desde subcursores y guardar capas JSON vacias sin perder objetos.
// Alta de parcelas no destructiva: mantiene originales y registra controles nativos.
// Mapas .upmap JSON + archivos plot_XX.json y plot_XX.png por parcela.
// Sin lectura de los formatos antiguos de prueba.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnderPressure.LoaderRuntime;
using FullInspector;
using HarmonyLib;
using TMPro;
using TH20;
using UnityEngine;
using UnityEngine.UI;

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
                ? _namedMap : "Nuevo mapa";
        }

        internal static string NameForSave(Level level)
        {
            return ReferenceEquals(_namedMapLevel, level) ? _namedMap ?? "" : "";
        }

        private static void MarkCurrentMap(Level level, string name)
        {
            _namedMapLevel = level;
            _namedMap = name;
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
        private static readonly System.Reflection.FieldInfo ItemsField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "Items");
        private static readonly System.Reflection.FieldInfo BuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "BuiltItems");
        private static readonly System.Reflection.FieldInfo UnbuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "UnbuiltItems");
        private static readonly System.Reflection.FieldInfo WorldConfigField =
            AccessTools.Field(typeof(WorldState), "_config");

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
            var folder = Path.Combine(Folder, SafeName(baseId), SafeName(name));
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
                        ? PlotNames[i] : "Parcela " + (i + 1),
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
            var header = new ScenarioMapFormat.Map
            {
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
                    item?.Definition?.Instance?.ItemType == RoomItemDefinition.Type.Landscape))
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
            var previousStyles = CurrentStyles(level);
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
                        ? "Parcela " + (entry.Data.index + 1) : entry.Data.name);
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
                MarkCurrentMap(level, map.Header.name);
            }
            catch
            {
                if (!ReferenceEquals(_currentCustomPlots, prepared)) prepared.DestroyAssets();
                throw;
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
                    // HospitalMap captura PerimeterPrefabs en su constructor. Una
                    // lista vacia crea la parcela sin instanciar barreras y queda
                    // asociada solo a esta parcela nueva.
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
                    PlotNames.Add("Parcela " + (PlotNames.Count + 1));
                PlotNames.Add("Parcela " + (originalPlots.Length + 1));

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
        private GameObject _dialog;
        private InputField _nameInput;
        private Text _statusText;
        private Text _mapIdentityLabel;
        private Font _font;
        private HospitalPlot _selectedPlot;
        private TMP_Dropdown _layerDropdown;
        private RectTransform _plotListViewport;
        private Button _addPlotButton;
        private Button _deletePlotButton;

        private static string T(string key, string fallback)
        {
            var value = EditorLocalization.Get(key);
            return string.IsNullOrEmpty(value) || value == key ? fallback : value;
        }

        internal static bool IsDialogOpen => _active != null && _active._dialog != null;

        internal void Initialise(EditHospitalMenu menu, Level level)
        {
            if (_menu != null) return;
            _active = this;
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
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            AddToolbarButton(T("editor.map.save", "Guardar mapa"), 0, ShowSave);
            AddToolbarButton(T("editor.map.open", "Abrir mapa"), 1, ShowLoad);
            AddStylesToolbarButton();
            InitialisePlotActions();
            _selectedPlot = level.WorldState.HospitalPlots.Count > 0
                ? level.WorldState.HospitalPlots[0] : null;
            level.HospitalEditEvents.OnSelectHospitalPlot += OnPlotSelected;
            AddMapIdentity();
            RefreshMapIdentity();
        }

        // Dos lineas a la izquierda de Parcelas; no tapa los controles nativos.
        private void AddMapIdentity()
        {
            var root = AddUi("UP Hospital and Map Name", transform);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-381f, -10f);
            rect.sizeDelta = new Vector2(360f, 48f);
            _mapIdentityLabel = root.AddComponent<Text>();
            _mapIdentityLabel.font = _font;
            _mapIdentityLabel.fontSize = 17;
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
            if (_mapIdentityLabel == null || _level == null) return;
            var hospital = _level.Config.GetLocalisedDisplayName();
            _mapIdentityLabel.text = "Hospital: " + hospital + "\nMapa: " +
                                     ScenarioEditorStorage.CurrentMapName(_level);
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
            var button = CloneButton(_template, transform,
                T("editor.map.styles", "Estilos del mapa"), ShowStyles);
            var rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-18f, -58f);
                rect.sizeDelta = new Vector2(168f, 34f);
            }
            _buttons.Add(button.gameObject);
        }

        private void OnPlotSelected(HospitalPlot plot)
        {
            _selectedPlot = plot;
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
            // La lista tiene un ScrollRect; los botones van justo debajo de su
            // viewport, no dentro del contenido que se desplaza.
            var scroll = root.GetComponentInParent<ScrollRect>();
            _plotListViewport = scroll != null
                ? scroll.GetComponent<RectTransform>()
                : root.parent as RectTransform;
            _addPlotButton = CloneButton(_template, transform, "Añadir", () =>
            {
                try { ScenarioEditorStorage.AddNewPlot(_level); }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                    ShowError("Añadir parcela", exception.Message);
                }
            });
            _deletePlotButton = CloneButton(_template, transform, "Eliminar", ShowDeleteConfirmation);
            _buttons.Add(_addPlotButton.gameObject);
            _buttons.Add(_deletePlotButton.gameObject);
            PositionPlotActions();
        }

        // Atajos limitados a la sesion del Editor. InputManager.GetKeyDown
        // ignora las teclas cuando el jugador esta editando un campo de texto.
        private void Update()
        {
            if (_level == null || _menu == null || !ScenarioEditorSession.Active ||
                !ReferenceEquals(ScenarioEditorSession.Level, _level) || IsDialogOpen)
                return;

            var input = _level.InputManager;
            if (input == null) return;

            for (int i = 0; i < ScenarioEditorHotkeyMap.PlotKeys.Length; i++)
            {
                if (!input.GetKeyDown(ScenarioEditorHotkeyMap.PlotKeys[i])) continue;
                var plots = _level.WorldState?.HospitalPlots;
                if (plots != null && i < plots.Count)
                    _level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plots[i]);
                return;
            }

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

        private void LateUpdate()
        {
            // Los tamaños del editor nativo se calculan por LayoutGroup.
            // Anclar los botones a la base real de la lista evita que se muevan
            // cuando hay muchas parcelas o se modifica la resolucion.
            PositionPlotActions();
        }

        private void PositionPlotActions()
        {
            if (_plotListViewport == null || _addPlotButton == null || _deletePlotButton == null)
                return;
            var parentRect = transform as RectTransform;
            if (parentRect == null) return;
            var corners = new Vector3[4];
            _plotListViewport.GetWorldCorners(corners);
            var width = Mathf.Abs((corners[3] - corners[0]).magnitude /
                                  Mathf.Max(parentRect.lossyScale.x, 0.0001f));
            var half = Mathf.Max(70f, (width - 6f) * 0.5f);
            var add = _addPlotButton.transform as RectTransform;
            var remove = _deletePlotButton.transform as RectTransform;
            if (add == null || remove == null) return;
            foreach (var rect in new[] { add, remove })
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(half - 3f, 31f);
            }
            // El origen de cada boton es su esquina superior izquierda.
            var offset = parentRect.TransformVector(new Vector3(2f, -4f, 0f));
            add.position = corners[0] + offset;
            remove.position = corners[0] + offset +
                              parentRect.TransformVector(new Vector3(half, 0f, 0f));
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
                ShowError("Eliminar parcela", "Selecciona primero una parcela.");
                return;
            }
            if (_level.WorldState.HospitalPlots.Count <= 1)
            {
                ShowError("Eliminar parcela", "Debe quedar al menos una parcela.");
                return;
            }
            var index = _level.WorldState.HospitalPlots.IndexOf(selected);
            BuildDialog("Eliminar parcela");
            if (_dialog == null) return;
            AddText(_dialogPanel,
                "¿Eliminar la parcela " + (index + 1) + "? Se perderan su suelo y sus objetos. " +
                "Esta accion no se puede deshacer.",
                25, 88, 482, 135, 19, TextAnchor.MiddleCenter);
            var close = _dialogPanel.Find("UP Map - Cerrar");
            if (close != null)
            {
                var cancel = close.GetComponentInChildren<TMP_Text>(true);
                if (cancel != null) cancel.text = "Cancelar";
                else
                {
                    var ordinary = close.GetComponentInChildren<Text>(true);
                    if (ordinary != null) ordinary.text = "Cancelar";
                }
            }
            AddDialogButton("Eliminar", new Vector2(-85f, -289f), () =>
            {
                try
                {
                    ScenarioEditorStorage.DeletePlot(_level, selected);
                    CloseDialog();
                }
                catch (Exception exception)
                {
                    ShowStatus(exception.Message, true);
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                }
            });
        }

        private void ShowSave()
        {
            BuildDialog(T("editor.map.save", "Guardar mapa"));
            if (_dialog == null) return;
            AddText(_dialogPanel, T("editor.map.name", "Nombre del mapa"), 16, 62, 500, 28, 20, TextAnchor.MiddleLeft);
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
                ? "Nuevo Mapa" : ScenarioEditorStorage.NameForSave(_level);
            AddDialogButton("Guardar", new Vector2(-85f, -289f), () =>
            {
                try
                {
                    var path = ScenarioEditorStorage.Save(_level, _nameInput.text);
                    RefreshMapIdentity();
                    ShowStatus("Guardado: " + System.IO.Path.GetFileName(path), false);
                }
                catch (Exception exception)
                {
                    ShowStatus(exception.Message, true);
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                }
            });
        }

        private void ShowStyles()
        {
            BuildDialog(T("editor.map.styles", "Estilos del mapa"));
            if (_dialog == null) return;
            var catalog = ScenarioHospitalStyles.Build(_level,
                T("editor.map.style.original", "Original"),
                T("editor.map.style.none", "Sin perímetro"));

            AddStyleRow(T("editor.map.style.perimeter", "Perímetro exterior"), 66f,
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
            AddRoomStyleRow(T("editor.map.style.built", "Hospital construido"), 111f,
                catalog.Built, map => map.builtRoomStyle, (map, value) => map.builtRoomStyle = value);
            AddRoomStyleRow(T("editor.map.style.building", "Durante construcción"), 156f,
                catalog.Building, map => map.buildingRoomStyle, (map, value) => map.buildingRoomStyle = value);
            AddRoomStyleRow(T("editor.map.style.unbuilt", "Parcela sin construir"), 201f,
                catalog.Unbuilt, map => map.unbuiltRoomStyle, (map, value) => map.unbuiltRoomStyle = value);
            AddText(_dialogPanel, T("editor.map.style.help",
                    "Los estilos se guardan en el mapa y se aplican al volver a abrirlo."),
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
            BuildDialog(T("editor.map.open", "Abrir mapa") + " .upmap");
            if (_dialog == null) return;
            var list = ScenarioEditorStorage.ListSaved();
            if (list.Count == 0)
            {
                AddText(_dialogPanel, T("editor.map.none", "Ningún mapa guardado en la carpeta maps."),
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
                if (!map.Valid) name += " (archivo invalido)";
                else if (!matches) name += " (otro hospital)";
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
                        ShowStatus(exception.Message, true);
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
                label.text = T("editor.map.close", "Cerrar");
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
                label.text = T("editor.map.close", "Cerrar");
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
            _dialogPanel = null;
            _nameInput = null;
            _statusText = null;
        }

        private void OnDestroy()
        {
            if (_level != null)
                _level.HospitalEditEvents.OnSelectHospitalPlot -= OnPlotSelected;
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
}
