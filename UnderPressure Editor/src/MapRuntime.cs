// Updated: 2026-10-14 - Agrupacion estructural del Editor desde GitHub beb96a79.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using FullInspector;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TH20;
using UnityEngine;

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
