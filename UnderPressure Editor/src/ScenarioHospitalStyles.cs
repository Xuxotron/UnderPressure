// Catalogo de estilos originales de parcelas y aplicacion de su configuracion .upmap.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FullInspector;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioHospitalStyles
    {
        internal sealed class RoomChoice
        {
            internal string Id;
            internal string Label;
            internal SharedInstance<RoomDefinition> Definition;
        }

        internal sealed class PerimeterChoice
        {
            internal string Mode;
            internal string Label;
            internal string[] PrefabNames;
            internal GameObject[] Prefabs;
        }

        internal sealed class Catalog
        {
            internal readonly List<RoomChoice> Built = new List<RoomChoice>();
            internal readonly List<RoomChoice> Building = new List<RoomChoice>();
            internal readonly List<RoomChoice> Unbuilt = new List<RoomChoice>();
            internal readonly List<PerimeterChoice> Perimeters = new List<PerimeterChoice>();
        }

        internal static Catalog Build(Level level, string originalLabel, string noneLabel)
        {
            if (level?.App?.SandboxSettingsConfig?.LevelOptions == null)
                throw new InvalidOperationException("No esta disponible el catalogo original de hospitales.");

            var result = new Catalog();
            AddOriginalRoom(result.Built, originalLabel);
            AddOriginalRoom(result.Building, originalLabel);
            AddOriginalRoom(result.Unbuilt, originalLabel);
            result.Perimeters.Add(new PerimeterChoice
            {
                Mode = ScenarioMapFormat.PerimeterOriginal,
                Label = originalLabel,
                PrefabNames = new string[0]
            });
            result.Perimeters.Add(new PerimeterChoice
            {
                Mode = ScenarioMapFormat.PerimeterNone,
                Label = noneLabel,
                PrefabNames = new string[0],
                Prefabs = new GameObject[0]
            });

            var seenLevels = new HashSet<LevelConfig>();
            var seenPerimeters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in level.App.SandboxSettingsConfig.LevelOptions)
            {
                var source = option?.Level?.Instance;
                if (source == null || !seenLevels.Add(source) ||
                    !DLCUtils.IsDLCInstalled(source.GetRequiredDlcPack())) continue;
                var config = source.GetWorldStateConfig();
                if (config == null) continue;
                AddRoom(result.Built, config._roomBuiltDefinition);
                AddRoom(result.Building, config._roomBuildingDefinition);
                AddRoom(result.Unbuilt, config._roomUnbuiltDefinition);

                var prefabs = config.PerimeterPrefabs;
                if (prefabs == null || prefabs.Length == 0 || prefabs.Any(prefab => prefab == null))
                    continue;
                var names = prefabs.Select(prefab => prefab.name).ToArray();
                var key = string.Join("|", names);
                if (!seenPerimeters.Add(key)) continue;
                result.Perimeters.Add(new PerimeterChoice
                {
                    Mode = ScenarioMapFormat.PerimeterCustom,
                    Label = source.GetLocalisedDisplayName(),
                    PrefabNames = names,
                    Prefabs = (GameObject[])prefabs.Clone()
                });
            }
            return result;
        }

        private static void AddOriginalRoom(ICollection<RoomChoice> target, string label)
        {
            target.Add(new RoomChoice { Id = "", Label = label });
        }

        private static void AddRoom(ICollection<RoomChoice> target,
            SharedInstance<RoomDefinition> definition)
        {
            if (definition?.Instance == null || string.IsNullOrEmpty(definition.name) ||
                target.Any(item => string.Equals(item.Id, definition.name, StringComparison.Ordinal)))
                return;
            var label = definition.name.Replace("Room_Hospital_", "").Replace("Room_Hospital ", "");
            target.Add(new RoomChoice { Id = definition.name, Label = label, Definition = definition });
        }

        internal static void Apply(Level level, ScenarioMapFormat.Map map, WorldState.Config config,
            IList<HospitalPlotDefinition> definitions)
        {
            if (level == null || map == null || config == null)
                throw new ArgumentNullException();
            var catalog = Build(level, "Original", "None");
            ApplyRoom(map.builtRoomStyle, catalog.Built, value => config._roomBuiltDefinition = value,
                definitions, (definition, value) => definition.BuiltRoomDefinition = value);
            ApplyRoom(map.buildingRoomStyle, catalog.Building, value => config._roomBuildingDefinition = value,
                definitions, (definition, value) => definition.BuildingRoomDefinition = value);
            ApplyRoom(map.unbuiltRoomStyle, catalog.Unbuilt, value => config._roomUnbuiltDefinition = value,
                definitions, (definition, value) => definition.UnbuiltRoomDefinition = value);

            var mode = ScenarioMapFormat.NormalizePerimeter(map.plotPerimeter);
            if (mode == ScenarioMapFormat.PerimeterNone)
                config.PerimeterPrefabs = new GameObject[0];
            else if (mode == ScenarioMapFormat.PerimeterCustom)
            {
                var names = map.perimeterPrefabs ?? new string[0];
                var choice = catalog.Perimeters.FirstOrDefault(item =>
                    item.Mode == ScenarioMapFormat.PerimeterCustom &&
                    item.PrefabNames.SequenceEqual(names));
                if (choice == null)
                    throw new InvalidDataException("El estilo de perimetro del mapa no esta disponible.");
                config.PerimeterPrefabs = (GameObject[])choice.Prefabs.Clone();
            }
        }

        private static void ApplyRoom(string id, IList<RoomChoice> choices,
            Action<SharedInstance<RoomDefinition>> setConfig,
            IList<HospitalPlotDefinition> definitions,
            Action<HospitalPlotDefinition, SharedInstance<RoomDefinition>> setDefinition)
        {
            if (string.IsNullOrEmpty(id)) return;
            var choice = choices.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (choice?.Definition == null)
                throw new InvalidDataException("El estilo de hospital no esta disponible: " + id);
            setConfig(choice.Definition);
            if (definitions == null) return;
            foreach (var definition in definitions)
                if (definition != null) setDefinition(definition, choice.Definition);
        }
    }
}
