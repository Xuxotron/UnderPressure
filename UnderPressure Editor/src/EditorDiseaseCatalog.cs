using System;
using System.Collections.Generic;
using System.Linq;
using FullInspector;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioDiseaseCatalog
    {
        internal sealed class Disease
        {
            internal string Id;
            internal string Name;
            internal SharedInstance<IllnessDefinition> Definition;
            internal WeightedIllness Defaults;
            internal readonly List<RoomDefinition> Rooms = new List<RoomDefinition>();
        }

        internal sealed class Room
        {
            internal string Id;
            internal RoomDefinition Definition;
            internal string Name;
            internal Sprite Icon;
            internal readonly List<Disease> Diseases = new List<Disease>();
        }

        internal sealed class Snapshot
        {
            internal readonly List<Disease> Diseases = new List<Disease>();
            internal readonly List<Room> Rooms = new List<Room>();
            internal readonly Dictionary<string, Disease> ById =
                new Dictionary<string, Disease>(StringComparer.Ordinal);
        }

        internal static Snapshot Build(Level level)
        {
            var snapshot = new Snapshot();
            var levels = new List<LevelConfig>();
            if (level?.Config != null) levels.Add(level.Config);
            var options = level?.App?.SandboxSettingsConfig?.LevelOptions;
            if (options != null)
                foreach (var option in options)
                {
                    var config = option?.Level?.Instance;
                    if (config != null && !levels.Contains(config)) levels.Add(config);
                }

            foreach (var config in levels)
            {
                try
                {
                    var characters = config.GetCharacterManagerConfig();
                    AddWeighted(snapshot, characters?._weightedIllnesses);
                    AddWeighted(snapshot, characters?._weightedIllnessesTimePortal);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Enfermedades: no se pudo examinar " + config.UniqueId + ": " +
                        exception.Message);
                }
            }

            var rooms = new Dictionary<RoomDefinition, Room>();
            foreach (var disease in snapshot.Diseases)
            {
                var treatments = disease.Definition?.Instance?._treatmentTypes;
                if (treatments == null) continue;
                foreach (var treatment in treatments)
                {
                    var definition = treatment?._room?.Instance;
                    if (definition == null || disease.Rooms.Contains(definition)) continue;
                    disease.Rooms.Add(definition);
                    if (!rooms.TryGetValue(definition, out var room))
                    {
                        room = new Room
                        {
                            Definition = definition,
                            Id = RoomId(treatment._room),
                            Name = RoomName(definition),
                            Icon = definition._icon
                        };
                        rooms.Add(definition, room);
                        snapshot.Rooms.Add(room);
                    }
                    room.Diseases.Add(disease);
                }
            }

            snapshot.Diseases.Sort((left, right) => string.Compare(left.Name, right.Name,
                StringComparison.CurrentCultureIgnoreCase));
            snapshot.Rooms.Sort((left, right) => string.Compare(left.Name, right.Name,
                StringComparison.CurrentCultureIgnoreCase));
            foreach (var room in snapshot.Rooms)
                room.Diseases.Sort((left, right) => string.Compare(left.Name, right.Name,
                    StringComparison.CurrentCultureIgnoreCase));
            return snapshot;
        }

        private static void AddWeighted(Snapshot snapshot, WeightedIllness[] values)
        {
            if (values == null) return;
            foreach (var value in values)
            {
                var shared = value?.Definition;
                var illness = shared?.Instance;
                if (illness == null || !illness.DLCIsValid()) continue;
                var id = DefinitionId(shared);
                if (string.IsNullOrEmpty(id) || snapshot.ById.ContainsKey(id)) continue;
                var disease = new Disease
                {
                    Id = id,
                    Name = IllnessName(illness),
                    Definition = shared,
                    Defaults = Clone(value)
                };
                snapshot.ById.Add(id, disease);
                snapshot.Diseases.Add(disease);
            }
        }

        internal static ScenarioMapFormat.WeightedIllnessData[] Capture(WeightedIllness[] values)
        {
            if (values == null) return new ScenarioMapFormat.WeightedIllnessData[0];
            return values.Where(value => value?.Definition?.Instance != null)
                .Select(value => FromNative(value)).ToArray();
        }

        internal static ScenarioMapFormat.WeightedIllnessData FromNative(WeightedIllness value)
        {
            return new ScenarioMapFormat.WeightedIllnessData
            {
                definition = DefinitionId(value.Definition),
                room = "",
                deprecated = value.Deprecated,
                unlocked = value.Unlocked,
                minWeight = value.MinWeight,
                maxWeight = value.MaxWeight,
                minStarRating = value.MinStarRating,
                minPatientsSpawned = value.MinPatientsSpawned
            };
        }

        internal static ScenarioMapFormat.WeightedIllnessData FromCatalog(Disease disease)
        {
            var value = disease?.Defaults ?? new WeightedIllness();
            var result = FromNative(value);
            if (disease != null) result.definition = disease.Id;
            return result;
        }

        internal static ScenarioMapFormat.WeightedIllnessData Clone(
            ScenarioMapFormat.WeightedIllnessData value)
        {
            if (value == null) return null;
            return new ScenarioMapFormat.WeightedIllnessData
            {
                definition = value.definition ?? "",
                room = value.room ?? "",
                deprecated = value.deprecated,
                unlocked = value.unlocked,
                minWeight = value.minWeight,
                maxWeight = value.maxWeight,
                minStarRating = value.minStarRating,
                minPatientsSpawned = value.minPatientsSpawned
            };
        }

        internal static SharedInstance<IllnessDefinition> Resolve(LevelConfig level, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var config = level?.GetCharacterManagerConfig();
            var result = Resolve(config?._weightedIllnesses, id) ??
                         Resolve(config?._weightedIllnessesTimePortal, id);
            if (result != null) return result;
            foreach (var shared in Resources.FindObjectsOfTypeAll<SharedInstance<IllnessDefinition>>())
                if (shared != null && Matches(shared, id)) return shared;
            return null;
        }

        private static SharedInstance<IllnessDefinition> Resolve(WeightedIllness[] values, string id)
        {
            if (values == null) return null;
            foreach (var value in values)
                if (value?.Definition != null && Matches(value.Definition, id))
                    return value.Definition;
            return null;
        }

        private static bool Matches(SharedInstance<IllnessDefinition> shared, string id) =>
            string.Equals(DefinitionId(shared), id, StringComparison.Ordinal);

        internal static string DefinitionId(SharedInstance<IllnessDefinition> shared)
        {
            if (shared == null) return "";
            if (!string.IsNullOrEmpty(shared.name)) return shared.name;
            return shared.Instance == null ? "" : shared.Instance.Name.Term ?? "";
        }

        private static string RoomId(SharedInstance<RoomDefinition> shared)
        {
            if (shared == null) return "";
            if (!string.IsNullOrEmpty(shared.name)) return shared.name;
            return shared.Instance == null ? "" : shared.Instance._type.ToString();
        }

        internal static string IllnessName(IllnessDefinition illness)
        {
            if (illness == null) return "";
            var value = illness.Name.Translation;
            return string.IsNullOrEmpty(value) ? illness.ToString() : value;
        }

        private static string RoomName(RoomDefinition room)
        {
            if (room == null) return "";
            var value = room.GetLocalisedName();
            return string.IsNullOrEmpty(value) ? room.ToString() : value;
        }

        private static WeightedIllness Clone(WeightedIllness value)
        {
            return new WeightedIllness
            {
                Definition = value.Definition,
                Deprecated = value.Deprecated,
                Unlocked = value.Unlocked,
                MinWeight = value.MinWeight,
                MaxWeight = value.MaxWeight,
                MinStarRating = value.MinStarRating,
                MinPatientsSpawned = value.MinPatientsSpawned
            };
        }
    }
}
