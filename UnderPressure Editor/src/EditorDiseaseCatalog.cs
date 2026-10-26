using System;
using System.Collections.Generic;
using System.Linq;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioDiseaseCatalog
    {
        internal sealed class Disease
        {
            internal string Id;
            internal SharedInstance<IllnessDefinition> Definition;
            internal WeightedIllness Defaults;
            internal DLCItemDefinition Dlc;
            internal readonly List<RoomDefinition> Rooms = new List<RoomDefinition>();

            internal string Name
            {
                get
                {
                    try { return IllnessName(Definition?.Instance); }
                    catch { return ""; }
                }
            }
        }

        internal sealed class Room
        {
            internal string Id;
            internal RoomDefinition Definition;
            internal Sprite Icon;
            internal DLCItemDefinition Dlc;
            internal int DlcOrder = int.MaxValue;
            internal readonly List<Disease> Diseases = new List<Disease>();

            internal string Name => RoomName(Definition);
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
            var dlcOrder = new Dictionary<uint, int>();
            var allDlc = AllDlcItems(level);
            if (allDlc != null)
            {
                var index = 0;
                foreach (var shared in allDlc)
                {
                    var dlc = shared?.Instance;
                    if (dlc != null && !dlcOrder.ContainsKey(dlc.AppID))
                        dlcOrder.Add(dlc.AppID, index++);
                }
            }
            var weightedRead = 0;
            var liveCharacters = level?.CharacterManager?.GetConfig();
            weightedRead += AddWeighted(snapshot, liveCharacters?._weightedIllnesses);
            weightedRead += AddWeighted(snapshot, liveCharacters?._weightedIllnessesTimePortal);
            try
            {
                var configuredCharacters = level?.Config?.GetCharacterManagerConfig();
                if (!ReferenceEquals(configuredCharacters, liveCharacters))
                {
                    weightedRead += AddWeighted(snapshot,
                        configuredCharacters?._weightedIllnesses);
                    weightedRead += AddWeighted(snapshot,
                        configuredCharacters?._weightedIllnessesTimePortal);
                }
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Enfermedades: no se pudo examinar la configuracion actual: " +
                    exception.Message);
            }

            // LevelOptions enumera los hospitales originales de todo el juego,
            // incluidos los DLC. El hospital abierto no contiene por si solo
            // todas sus enfermedades ni, por tanto, todas las salas de tratamiento.
            var examinedLevels = new HashSet<LevelConfig>();
            if (level?.Config != null) examinedLevels.Add(level.Config);
            var options = level?.App?.SandboxSettingsConfig?.LevelOptions;
            if (options != null)
                foreach (var option in options)
                {
                    LevelConfig levelConfig;
                    try { levelConfig = option?.Level?.Instance; }
                    catch { continue; }
                    if (levelConfig == null || !examinedLevels.Add(levelConfig)) continue;
                    try
                    {
                        var characters = levelConfig.GetCharacterManagerConfig();
                        weightedRead += AddWeighted(snapshot, characters?._weightedIllnesses);
                        weightedRead += AddWeighted(snapshot,
                            characters?._weightedIllnessesTimePortal);
                    }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Enfermedades: no se pudo examinar " + levelConfig.UniqueId +
                            ": " + exception.Message);
                    }
                }

            foreach (var list in Resources.FindObjectsOfTypeAll<SharedInstance<WeightedIllnessList>>())
            {
                var illnesses = list?.Instance?.Illnesses;
                if (illnesses != null) weightedRead += AddWeighted(snapshot, illnesses.ToArray());
            }

            var sharedRead = 0;
            foreach (var shared in Resources.FindObjectsOfTypeAll<SharedInstance<IllnessDefinition>>())
            {
                ++sharedRead;
                AddDefinition(snapshot, shared, null);
            }

            var rooms = new Dictionary<RoomDefinition, Room>();
            var treatmentDiseases = 0;
            foreach (var disease in snapshot.Diseases)
            {
                var treatments = disease.Definition?.Instance?._treatmentTypes;
                if (treatments == null) continue;
                if (treatments.Any(treatment => treatment?._room?.Instance != null))
                    ++treatmentDiseases;
                foreach (var treatment in treatments)
                {
                    var definition = treatment?._room?.Instance;
                    if (definition == null || disease.Rooms.Contains(definition)) continue;
                    disease.Rooms.Add(definition);
                    if (!rooms.TryGetValue(definition, out var room))
                    {
                        var dlc = definition.DlcPackRequired?.Instance;
                        room = new Room
                        {
                            Definition = definition,
                            Id = RoomId(treatment._room),
                            Icon = definition._icon,
                            Dlc = dlc,
                            DlcOrder = dlc != null && dlcOrder.TryGetValue(dlc.AppID,
                                out var order) ? order : int.MaxValue
                        };
                        rooms.Add(definition, room);
                        snapshot.Rooms.Add(room);
                    }
                    room.Diseases.Add(disease);
                }
            }

            // Muchas RoomDefinition de los DLC no declaran DlcPackRequired. En ese
            // caso la procedencia se deduce solo si TODAS las enfermedades que usan
            // la sala pertenecen al mismo paquete. Las salas base compartidas nunca
            // se etiquetan como DLC por este procedimiento.
            foreach (var room in snapshot.Rooms)
            {
                if (room.Dlc == null && room.Diseases.Count > 0 &&
                    room.Diseases.All(disease => disease.Dlc != null))
                {
                    var packs = room.Diseases.Select(disease => disease.Dlc)
                        .GroupBy(dlc => dlc.AppID).ToList();
                    if (packs.Count == 1) room.Dlc = packs[0].First();
                }
                room.DlcOrder = room.Dlc != null &&
                    dlcOrder.TryGetValue(room.Dlc.AppID, out var order)
                        ? order : int.MaxValue;
            }

            SortLocalized(snapshot, dlcOrder);
            var dlcRooms = snapshot.Rooms.Count(room => room.Dlc != null);
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Enfermedades: " + snapshot.Diseases.Count + " definiciones, " +
                treatmentDiseases + " con tratamiento y " + snapshot.Rooms.Count +
                " salas (" + dlcRooms + " DLC); " + weightedRead +
                " entradas ponderadas y " + sharedRead + " SharedInstance examinados.");
            return snapshot;
        }

        internal static void SortLocalized(Snapshot snapshot)
        {
            if (snapshot == null) return;
            var dlcOrder = snapshot.Rooms.Where(room => room.Dlc != null)
                .GroupBy(room => room.Dlc.AppID)
                .ToDictionary(group => group.Key, group => group.Min(room => room.DlcOrder));
            SortLocalized(snapshot, dlcOrder);
        }

        private static void SortLocalized(Snapshot snapshot,
            IDictionary<uint, int> dlcOrder)
        {
            snapshot.Diseases.Sort((left, right) => string.Compare(left.Name, right.Name,
                StringComparison.CurrentCultureIgnoreCase));
            snapshot.Rooms.Sort((left, right) =>
            {
                var leftDlc = left.Dlc != null;
                var rightDlc = right.Dlc != null;
                if (leftDlc != rightDlc) return leftDlc ? 1 : -1;
                if (leftDlc)
                {
                    var leftOrder = dlcOrder != null && dlcOrder.TryGetValue(left.Dlc.AppID,
                        out var resolvedLeft) ? resolvedLeft : left.DlcOrder;
                    var rightOrder = dlcOrder != null && dlcOrder.TryGetValue(right.Dlc.AppID,
                        out var resolvedRight) ? resolvedRight : right.DlcOrder;
                    var order = leftOrder.CompareTo(rightOrder);
                    if (order != 0) return order;
                    order = left.Dlc.AppID.CompareTo(right.Dlc.AppID);
                    if (order != 0) return order;
                }
                return string.Compare(left.Name, right.Name,
                    StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (var room in snapshot.Rooms)
                room.Diseases.Sort((left, right) => string.Compare(left.Name, right.Name,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        private static int AddWeighted(Snapshot snapshot, WeightedIllness[] values)
        {
            if (values == null) return 0;
            var read = 0;
            foreach (var value in values)
            {
                ++read;
                var shared = value?.Definition;
                AddDefinition(snapshot, shared, value);
            }
            return read;
        }

        private static void AddDefinition(Snapshot snapshot,
            SharedInstance<IllnessDefinition> shared, WeightedIllness defaults)
        {
            IllnessDefinition illness;
            try { illness = shared?.Instance; }
            catch { return; }
            if (illness == null) return;
            var id = DefinitionId(shared);
            if (string.IsNullOrEmpty(id) || snapshot.ById.ContainsKey(id)) return;
            var disease = new Disease
            {
                Id = id,
                Definition = shared,
                Dlc = illness.DLCPackRequired?.Instance,
                Defaults = defaults == null ? new WeightedIllness
                {
                    Definition = shared
                } : Clone(defaults)
            };
            snapshot.ById.Add(id, disease);
            snapshot.Diseases.Add(disease);
        }

        private static IEnumerable<SharedInstance<DLCItemDefinition>> AllDlcItems(Level level)
        {
            var manager = level?.App?.DLCManager;
            if (manager == null) return null;
            try
            {
                var config = AccessTools.Field(typeof(DLCManager), "_config")
                    ?.GetValue(manager);
                var items = config == null ? null : AccessTools.Field(config.GetType(),
                    "DLCItems")?.GetValue(config) as
                    IEnumerable<SharedInstance<DLCItemDefinition>>;
                if (items != null) return items;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Enfermedades: no se pudo leer el orden completo de DLC: " +
                    exception.Message);
            }
            return manager.AvailableItems;
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
            return value ?? "";
        }

        private static string RoomName(RoomDefinition room)
        {
            if (room == null) return "";
            var value = room.GetLocalisedName();
            return value ?? "";
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
