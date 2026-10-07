// Updated: 2026-10-07
// Purpose: Energy-room-specific runtime logic: requirements resolution, unlock/save integration and janitor jobs.
using System;
using System.Collections.Generic;
using System.Reflection;
using FullInspector;
using FullInspector.Generated.SharedInstance;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnderPressure;

namespace UnderPressure.PowerGrid
{
    internal static class PowerPlantRoomRegistry
    {
        internal static int RoomTypeValue => RoomCatalog.Energy.TypeValue;

        private static readonly MethodInfo MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");

        private static RequiredItem _doorRequirement;
        private static RequiredItem _deskRequirement;
        private static RequiredItem _batteryRequirement;
        private static RequiredItem _panelRequirement;
        private static RequiredItem _transformerRequirement;
        private static RequiredItem _cellRequirement;
        private static StaffRequired _janitorRequirement;
        private static RoomDefinition _marketingRoom;

        internal static RoomDefinition Definition { get; private set; }
        internal static StaffRequired JanitorRequirement => _janitorRequirement;
        internal static bool FirstLevelMarketingEnabled { get; private set; }

        internal static bool IsPowerPlant(RoomDefinition definition)
        {
            return RoomCatalog.Is(definition, RoomCatalog.Energy);
        }

        internal static bool IsMarketingRoom(ISilverUnlockable unlockable) =>
            unlockable is RoomDefinition room &&
            _marketingRoom != null &&
            ReferenceEquals(room, _marketingRoom);

        internal static JobRoomDescription CreateEnergyJobDescription()
        {
            return new JobRoomDescription
            {
                Room = Definition,
                StaffRequired = JanitorRequirement
            };
        }

        internal static bool IsEnergyJobDescription(JobDescription description)
        {
            var roomJob = description as JobRoomDescription;
            return roomJob != null && IsPowerPlant(roomJob.Room);
        }

        internal static Dictionary<Staff, List<JobDescription>> StripEnergyJobExclusions(Level level)
        {
            var removed = new Dictionary<Staff, List<JobDescription>>();
            var staffMembers = level?.CharacterManager?.StaffMembers;
            if (staffMembers == null) return removed;

            foreach (var staff in staffMembers)
            {
                var exclusions = staff?.JobExclusions;
                if (exclusions == null) continue;

                for (var index = exclusions.Count - 1; index >= 0; --index)
                {
                    var description = exclusions[index];
                    if (!IsEnergyJobDescription(description)) continue;

                    if (!removed.TryGetValue(staff, out var list))
                    {
                        list = new List<JobDescription>();
                        removed.Add(staff, list);
                    }

                    list.Add(description);
                    exclusions.RemoveAt(index);
                }
            }

            return removed;
        }

        internal static void RestoreEnergyJobExclusions(Dictionary<Staff, List<JobDescription>> removed)
        {
            if (removed == null) return;

            foreach (var pair in removed)
            {
                var exclusions = pair.Key?.JobExclusions;
                if (exclusions == null) continue;

                foreach (var description in pair.Value)
                    if (description != null && !exclusions.Contains(description))
                        exclusions.Add(description);
            }
        }

        internal static void EnsureDefinition(Metagame metagame)
        {
            if (metagame?.RoomDatabase?.Instance == null) return;

            var rooms = metagame.RoomDatabase.Instance.Rooms;
            if (rooms == null) return;

            RoomDefinition marketingRoom = null;
            foreach (var shared in rooms)
            {
                if (shared?.Instance == null) continue;
                if (shared.Instance._type == RoomDefinition.Type.Marketing)
                {
                    marketingRoom = shared.Instance;
                    break;
                }
            }

            _marketingRoom = marketingRoom;

            _doorRequirement = RoomCatalog.FindDoorRequirement(rooms);
            _deskRequirement = DisabledFeatures.EnergyRoomDesk
                ? null
                : FindMarketingDeskRequirement(marketingRoom);

            EnergyRoomItems.Ensure(metagame, marketingRoom, rooms);

            _batteryRequirement = EnergyRoomItems.Requirement(
                "UnderPressure Energy Battery", EnergyRoomItems.Battery, 9112101);
            _panelRequirement = EnergyRoomItems.Requirement(
                "UnderPressure Electrical Panel", EnergyRoomItems.Panel, 9112102);
            _transformerRequirement = EnergyRoomItems.Requirement(
                "UnderPressure Transformer", EnergyRoomItems.Transformer, 9112103);
            _cellRequirement = EnergyRoomItems.Requirement(
                "UnderPressure Electrical Cell", EnergyRoomItems.Cell, 9112104);

            _janitorRequirement = CreateJanitorRequirement(marketingRoom);

            Definition = RoomCatalog.EnsureDefinition(
                metagame,
                RoomCatalog.Energy,
                EnergyLocalization.Create,
                BuildRequirementMap(),
                BuildStaffMap());

            if (Definition == null)
            {
                PowerGridPlugin.Log.LogError(
                    "No se pudo registrar la sala de energia desde RoomCatalog.");
                return;
            }

            RegisterSaveAssets(metagame);
        }

        private static void RegisterSaveAssets(Metagame metagame)
        {
            var saveSystem = metagame?.App?.SaveSystem;

            var mapping = saveSystem == null
                ? null
                : AccessTools.Field(typeof(SaveSystem), "_assetIDs")
                    ?.GetValue(saveSystem) as BiDictionary<int, object>;

            if (mapping == null) return;

            var added = 0;

            added += RegisterSaveAsset(
                mapping,
                EnergyRoomItems.BatteryShared?.ID ?? 0,
                EnergyRoomItems.BatteryShared,
                EnergyRoomItems.Battery);

            added += RegisterSaveAsset(
                mapping,
                EnergyRoomItems.PanelShared?.ID ?? 0,
                EnergyRoomItems.PanelShared,
                EnergyRoomItems.Panel);

            added += RegisterSaveAsset(
                mapping,
                EnergyRoomItems.TransformerShared?.ID ?? 0,
                EnergyRoomItems.TransformerShared,
                EnergyRoomItems.Transformer);

            added += RegisterSaveAsset(
                mapping,
                EnergyRoomItems.CellShared?.ID ?? 0,
                EnergyRoomItems.CellShared,
                EnergyRoomItems.Cell);

            if (added <= 0) return;

            RefreshSerializerMappings(saveSystem, mapping);

            PowerGridPlugin.Log.LogInfo(
                $"Registradas {added} referencias externas de UnderPressure para guardado.");
        }

        private static void RefreshSerializerMappings(
            SaveSystem saveSystem,
            BiDictionary<int, object> mapping)
        {
            var serializerFields = new[]
            {
                "_serializerLevel",
                "_serializerMetagame",
                "_serializerRoomTemplates"
            };

            foreach (var fieldName in serializerFields)
            {
                var serializer =
                    AccessTools.Field(typeof(SaveSystem), fieldName)
                        ?.GetValue(saveSystem) as FullSerializerSave.fsSerializer;

                serializer?.SetIDObjectMapping(
                    mapping.FirstToSecond,
                    mapping.SecondToFirst);
            }
        }

        private static int RegisterSaveAsset(
            BiDictionary<int, object> mapping,
            int wrapperId,
            object wrapper,
            object instance)
        {
            if (wrapperId == 0 || wrapper == null || instance == null)
                return 0;

            var added = 0;
            added += RegisterSaveObject(mapping, wrapperId, wrapper);
            added += RegisterSaveObject(mapping, wrapperId - 1, instance);
            return added;
        }

        private static int RegisterSaveObject(
            BiDictionary<int, object> mapping,
            int id,
            object value)
        {
            if (mapping.ContainsValue(value))
                return 0;

            if (mapping.ContainsKey(id))
            {
                PowerGridPlugin.Log.LogError(
                    $"ID externo {id} ocupado; no se puede registrar {value}.");
                return 0;
            }

            mapping.Add(id, value);
            return 1;
        }


        private static Dictionary<string, RequiredItem> BuildRequirementMap()
        {
            return new Dictionary<string, RequiredItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["Door"] = _doorRequirement,
                ["MarketingDesk"] = _deskRequirement,
                ["Battery"] = _batteryRequirement,
                ["ElectricalPanel"] = _panelRequirement,
                ["Transformer"] = _transformerRequirement,
                ["ElectricalCell"] = _cellRequirement
            };
        }

        private static Dictionary<string, StaffRequired> BuildStaffMap()
        {
            return new Dictionary<string, StaffRequired>(StringComparer.OrdinalIgnoreCase)
            {
                ["Janitor"] = _janitorRequirement
            };
        }

        private static RequiredItem FindMarketingDeskRequirement(
            RoomDefinition marketingRoom)
        {
            var requiredItems = marketingRoom?.GetRequiredItems();

            if (requiredItems == null)
            {
                PowerGridPlugin.Log.LogWarning(
                    "No se encontro Marketing para copiar su escritorio obligatorio.");
                return null;
            }

            foreach (var requirement in requiredItems)
            {
                if (requirement == null ||
                    requirement.Items == null ||
                    requirement.ContainsType(RoomItemDefinition.Type.Door))
                    continue;

                if ((ContainsWord(requirement.GroupName, "marketeer") ||
                     ContainsWord(requirement.GroupName, "marketing")) &&
                    (ContainsWord(requirement.GroupName, "desk") ||
                     ContainsWord(requirement.GroupName, "escritorio")))
                {
                    return LogMarketingDeskRequirement(requirement);
                }

                foreach (var shared in requirement.Items)
                {
                    var item = shared?.Instance;
                    if (item == null) continue;

                    var prefab = item.GetPrefab(0);

                    var identity =
                        (item.DebugTag ?? string.Empty) + " " +
                        (item.GetSanitizedName() ?? string.Empty) + " " +
                        (prefab == null ? string.Empty : prefab.name);

                    if ((ContainsWord(identity, "marketeer") ||
                         ContainsWord(identity, "marketing")) &&
                        (ContainsWord(identity, "desk") ||
                         ContainsWord(identity, "escritorio")))
                    {
                        return LogMarketingDeskRequirement(requirement);
                    }
                }
            }

            PowerGridPlugin.Log.LogWarning(
                "No se identifico el grupo del escritorio obligatorio de Marketing.");

            return null;
        }

        private static RequiredItem LogMarketingDeskRequirement(
            RequiredItem requirement)
        {
            PowerGridPlugin.Log.LogInfo(
                "Requisito de escritorio copiado de Marketing: " +
                (requirement.GroupName ?? "(sin nombre)"));

            return requirement;
        }

        private static StaffRequired CreateJanitorRequirement(
            RoomDefinition marketingRoom)
        {
            StaffRequired behaviourTemplate = null;

            var marketingStaff =
                AccessTools.Field(typeof(RoomDefinition), "_requiresStaff")
                    ?.GetValue(marketingRoom) as StaffRequired[];

            if (marketingStaff != null && marketingStaff.Length > 0)
                behaviourTemplate = marketingStaff[0];

            if (behaviourTemplate == null)
            {
                PowerGridPlugin.Log.LogWarning(
                    "No se encontro el comportamiento de trabajo de Marketing para el bedel.");
                return null;
            }

            SharedInstance_TH20TH20_StaffDefinition janitor = null;

            foreach (var shared in
                     Resources.FindObjectsOfTypeAll<SharedInstance_TH20TH20_StaffDefinition>())
            {
                if (shared?.Instance == null ||
                    shared.Instance._type != StaffDefinition.Type.Janitor)
                    continue;

                janitor = shared;
                break;
            }

            if (janitor == null)
            {
                PowerGridPlugin.Log.LogWarning(
                    "No se encontro el SharedInstance nativo del bedel.");
                return null;
            }

            var requirement =
                (StaffRequired)MemberwiseCloneMethod.Invoke(
                    behaviourTemplate, null);

            AccessTools.Field(typeof(StaffRequired), "Type")
                ?.SetValue(requirement, janitor);

            AccessTools.Field(typeof(StaffRequired), "AlternativeType")
                ?.SetValue(requirement, null);

            AccessTools.Field(typeof(StaffRequired), "Qualification")
                ?.SetValue(requirement, null);

            return requirement;
        }

        private static bool ContainsWord(string value, string word)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(
                       word,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }


        internal static void AddToLevel(WorldState worldState)
        {
            if (worldState == null) return;

            var levelField =
                AccessTools.Field(typeof(WorldState), "_level");

            var metagameField =
                AccessTools.Field(typeof(WorldState), "_metagame");

            var level =
                levelField?.GetValue(worldState) as Level;

            var metagame =
                metagameField?.GetValue(worldState) as Metagame;

            EnsureDefinition(metagame);

            FirstLevelMarketingEnabled =
                level != null &&
                level.UniqueID == "901";

            if (Definition == null) return;

            RoomCatalog.EnsureRoomTemplateBucket(level, RoomCatalog.Energy);
            RoomCatalog.EnsureAvailableItems(
                worldState,
                RoomCatalog.Energy,
                BuildRequirementMap());

            // Never call Metagame.UnlockItem for runtime definitions.
            PurgePersistentUnlocks(metagame);

            for (var index =
                     worldState.AvailableRooms.Count - 1;
                 index >= 0;
                 --index)
            {
                var available =
                    worldState.AvailableRooms[index];

                if (IsPowerPlant(available) &&
                    !ReferenceEquals(
                        available,
                        Definition))
                {
                    worldState.AvailableRooms.RemoveAt(index);
                }
            }

            if (!worldState.AvailableRooms.Contains(Definition))
                worldState.AvailableRooms.Add(Definition);

            if (level != null &&
                level.UniqueID == "901" &&
                _marketingRoom != null &&
                !worldState.AvailableRooms.Contains(_marketingRoom))
            {
                worldState.AvailableRooms.Add(_marketingRoom);
            }

            if (level != null &&
                level.UniqueID == "901" &&
                _marketingRoom != null)
            {
                foreach (var requirement in
                         _marketingRoom.GetRequiredItems() ??
                         Array.Empty<RequiredItem>())
                {
                    RoomCatalog.EnsureRequirementItemsAvailable(worldState, requirement);
                }
            }
        }


        internal static void PurgePersistentUnlocks(Metagame metagame)
        {
            var unlocks = metagame?.SilverUnlockables;
            if (unlocks == null) return;

            var removed = 0;

            for (var index = unlocks.Count - 1;
                 index >= 0;
                 --index)
            {
                var token = unlocks[index];
                var room = token as RoomDefinition;
                var item = token as IRoomItemDefinition;

                if (!IsPowerPlant(room) &&
                    !EnergyRoomItems.IsCustomDefinition(item))
                    continue;

                unlocks.RemoveAt(index);
                ++removed;
            }

            if (removed == 0) return;

            var dirty =
                AccessTools.Field(
                    typeof(Metagame),
                    "_hasUnsavedChangesHighImportance");

            dirty?.SetValue(metagame, true);

            PowerGridPlugin.Log.LogWarning(
                $"Eliminadas {removed} referencias persistentes antiguas de UnderPressure antes de guardar.");
        }


    }

    [HarmonyPatch(
        typeof(Metagame),
        "HasUnlocked",
        new[] { typeof(ISilverUnlockable) })]
    internal static class FirstLevelMarketingUnlockPatch
    {
        private static void Postfix(
            ISilverUnlockable __0,
            ref bool __result)
        {
            if (!__result &&
                PowerPlantRoomRegistry.FirstLevelMarketingEnabled &&
                PowerPlantRoomRegistry.IsMarketingRoom(__0))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(Metagame),
        "VerifyDatabases")]
    internal static class PowerPlantDatabasePatch
    {
        private static void Postfix(
            Metagame __instance)
        {
            PowerPlantRoomRegistry.EnsureDefinition(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(Metagame),
        "HasUnlocked")]
    internal static class PowerPlantRuntimeUnlockPatch
    {
        private static bool Prefix(
            ISilverUnlockable __0,
            ref bool __result)
        {
            var room =
                __0 as RoomDefinition;

            var item =
                __0 as IRoomItemDefinition;

            if (!PowerPlantRoomRegistry.IsPowerPlant(room) &&
                !EnergyRoomItems.IsCustomDefinition(item))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(Metagame),
        "UnlockItem")]
    internal static class PowerPlantBlockPersistentUnlockPatch
    {
        private static bool Prefix(
            ISilverUnlockable __0)
        {
            var room =
                __0 as RoomDefinition;

            var item =
                __0 as IRoomItemDefinition;

            return
                !PowerPlantRoomRegistry.IsPowerPlant(room) &&
                !EnergyRoomItems.IsCustomDefinition(item);
        }
    }

    [HarmonyPatch(
        typeof(RoomAlgorithms),
        "GetAllJobs")]
    internal static class PowerPlantJanitorJobColumnPatch
    {
        private static void Postfix(
            Metagame __0,
            WorldState __1,
            StaffDefinition.Type __2,
            ref List<JobDescription> __result)
        {
            if (__2 != StaffDefinition.Type.Janitor ||
                __result == null ||
                PowerPlantRoomRegistry.Definition == null ||
                PowerPlantRoomRegistry.JanitorRequirement == null ||
                __1?.AvailableRooms == null ||
                !__1.AvailableRooms.Contains(
                    PowerPlantRoomRegistry.Definition))
                return;

            var description =
                PowerPlantRoomRegistry.CreateEnergyJobDescription();

            foreach (var existing in __result)
                if (existing != null &&
                    existing.Equals(description))
                    return;

            __result.Add(description);
        }
    }

    [HarmonyPatch(
        typeof(Metagame),
        "RestoreFromSave")]
    internal static class PowerPlantMetagameRestorePatch
    {
        private static void Postfix(
            Metagame __instance)
        {
            PowerPlantRoomRegistry.EnsureDefinition(
                __instance);

            PowerPlantRoomRegistry.PurgePersistentUnlocks(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(App),
        "CreateMetagameSaveData")]
    internal static class PowerPlantMetagameSavePatch
    {
        private static void Prefix(
            App __instance)
        {
            PowerPlantRoomRegistry.PurgePersistentUnlocks(
                __instance?.Metagame);
        }
    }

    [HarmonyPatch(
        typeof(App),
        "CreateSaveData")]
    internal static class PowerPlantLevelSavePatch
    {
        private static void Prefix(
            App __instance)
        {
            PowerPlantRoomRegistry.PurgePersistentUnlocks(
                __instance?.Metagame);
        }
    }

    [HarmonyPatch(
        typeof(WorldState),
        MethodType.Constructor,
        new[]
        {
            typeof(WorldState.Config),
            typeof(Level),
            typeof(Metagame),
            typeof(VisualManager),
            typeof(Material),
            typeof(RoomItemVisualEdit.Config)
        })]
    internal static class PowerPlantWorldStateConstructorPatch
    {
        private static void Postfix(
            WorldState __instance)
        {
            PowerPlantRoomRegistry.AddToLevel(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(WorldState),
        "RestoreFromSave")]
    internal static class PowerPlantWorldStateRestorePatch
    {
        private static void Postfix(
            WorldState __instance)
        {
            PowerPlantRoomRegistry.AddToLevel(
                __instance);
        }
    }
}
