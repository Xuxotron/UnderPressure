// Updated: 2026-10-07
// Purpose: Catalogo y mecanica comun de todas las salas personalizadas de UnderPressure.
// Aqui viven los parametros compartidos de RoomDefinition: identidad, precio, tamano,
// objetos disponibles/obligatorios, personal, geometria, materiales, iluminacion y registro.
// La logica funcional propia de cada sala u objeto permanece en su modulo especifico.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FullInspector;
using FullInspector.Generated.SharedInstance;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    public static class RoomCatalog
    {
        // =====================================================================
        // PLANTILLAS DE ESTILOS DE PARED
        // =====================================================================

        public const int NativeStyle = 0;
        public const int WallStyle1 = 1;

        // Cada plantilla contiene las 11 piezas minimas de un estilo completo:
        // cuatro paredes, dos esquinas, una puerta y cuatro ventanas.
        public static readonly WallStyleDefinition Style1 = new WallStyleDefinition(WallStyle1)
        {
            Geometry = new WallGeometry
            {
                CornerInner = NativePrefab(
                    "Wall_Corner_A_V1",
                    "D_Outer_Wall_Corner_A_V1"),
                CornerOuter = NativePrefab(
                    "Wall_Corner_B_V1",
                    "D_Outer_Wall_Corner_B_V1"),

                Wall = NativePrefab(
                    "Wall_Blank_A_V1",
                    "D_Outer_Wall_Blank_A_V1"),
                WallCornerLeft = NativePrefab(
                    "Wall_Blank_SL_A_V1",
                    "D_Outer_Wall_Blank_SL_A_V1"),
                WallCornerRight = NativePrefab(
                    "Wall_Blank_SR_A_V1",
                    "D_Outer_Wall_Blank_SR_A_V1"),
                WallCornerBoth = NativePrefab(
                    "Wall_Blank_Short_A_V1",
                    "D_Outer_Wall_Blank_Short_A_V1"),

                Door = NativeMesh("Wall_DoorFrame_A_V1"),

                Window = NativePrefab(
                    "Wall_Winframe_A_V1",
                    "D_Outer_Wall_Winframe_A_V1"),
                WindowCornerLeft = NativePrefab(
                    "Wall_Winframe_SL_A_V1",
                    "D_Outer_Wall_WinFrame_SL_A_1"),
                WindowCornerRight = NativePrefab(
                    "Wall_Winframe_SR_A_V1",
                    "D_Outer_Wall_Winframe_SR_A_V1"),
                WindowCornerBoth = NativePrefab(
                    "Wall_Winframe_Short_A_V1",
                    "D_Outer_Wall_Winframe_Short_A_V1")
            }
        };

        public static readonly WallStyleDefinition[] WallStyles =
        {
            Style1
        };

        // =====================================================================
        // SALAS
        // =====================================================================

        public static readonly RoomEntry Energy = new RoomEntry("Energy")
        {
            Enabled = true,
            TypeValue = 1000,
            SharedDefinitionId = 9112001,
            WrapperName = "UnderPressure Energy Room",
            NameTerm = "energy.room.name",
            DescriptionTerm = "energy.room.description",
            IconSpriteName = "shock_icon",
            Cost = 25000,
            SilverCost = 0,
            MinSizeX = 3,
            MinSizeY = 3,
            MaxCapacity = 0,
            HasQueue = false,
            CanManageQueue = false,
            AllowQueueWarningStatusIcon = false,
            MinimumStaffCount = 0,
            MustBeWhiteListed = false,

            // El catalogo decide QUE necesita/admite la sala. El modulo de Energia
            // solo enlaza estas claves con sus referencias runtime concretas.
            AvailableItemKeys = new[]
            {
                "Door",
                "MarketingDesk",
                "Battery",
                "ElectricalPanel",
                "Transformer",
                "ElectricalCell"
            },
            RequiredItemKeys = new[]
            {
                "Door",
                "MarketingDesk",
                "Transformer",
                "ElectricalCell"
            },
            RequiredWorkingItemKeys = new[]
            {
                "Transformer"
            },
            RequiredStaffKeys = new[]
            {
                "Janitor"
            },

            Visuals = new RoomVisuals
            {
                UseHospitalFloorTile = true,
                WallThickness = null,
                Floor = null,
                InteriorWallStyle = NativeStyle,
                ExteriorWallStyle = WallStyle1,

                FloorMaterialAsset = "Assets/EnergyRoom/EnergyFloor.mat",
                InteriorMaterialAsset = "Assets/EnergyRoom/EnergyInterior.mat",
                ExteriorMaterialAsset = "Assets/EnergyRoom/EnergyExterior.mat",
                DoorMaterialAsset = "Assets/EnergyRoom/EnergyDoor.mat"
            }
        };

        public static readonly RoomEntry Maintenance = new RoomEntry("Maintenance")
        {
            Enabled = false,
            Visuals = new RoomVisuals
            {
                UseHospitalFloorTile = true,
                InteriorWallStyle = NativeStyle,
                ExteriorWallStyle = NativeStyle
            }
        };

        public static readonly RoomEntry Morgue = new RoomEntry("Morgue")
        {
            Enabled = false,
            Visuals = new RoomVisuals
            {
                UseHospitalFloorTile = true,
                InteriorWallStyle = NativeStyle,
                ExteriorWallStyle = NativeStyle
            }
        };

        public static readonly RoomEntry DirectorOffice = new RoomEntry("DirectorOffice")
        {
            Enabled = false,
            Visuals = new RoomVisuals
            {
                UseHospitalFloorTile = true,
                InteriorWallStyle = NativeStyle,
                ExteriorWallStyle = NativeStyle
            }
        };

        public static readonly RoomEntry[] All =
        {
            Energy,
            Maintenance,
            Morgue,
            DirectorOffice
        };

        // =====================================================================
        // DATOS
        // =====================================================================

        public sealed class RoomEntry
        {
            public RoomEntry(string key)
            {
                Key = key;
            }

            public string Key;
            public bool Enabled;
            public int TypeValue = -1;
            public int SharedDefinitionId;
            public string WrapperName;

            public string NameTerm;
            public string DescriptionTerm;
            public string IconSpriteName;

            public int Cost;
            public int SilverCost;
            public int MinSizeX = 3;
            public int MinSizeY = 3;
            public int MaxCapacity;
            public bool HasQueue;
            public bool CanManageQueue;
            public bool AllowQueueWarningStatusIcon;
            public int MinimumStaffCount;
            public bool MustBeWhiteListed;
            public bool ShowUnitsProcessedInGUI;
            public bool ShowTotalRevenueInGUI;

            public string[] AvailableItemKeys = Array.Empty<string>();
            public string[] RequiredItemKeys = Array.Empty<string>();
            public string[] RequiredWorkingItemKeys = Array.Empty<string>();
            public string[] RequiredStaffKeys = Array.Empty<string>();

            public RoomVisuals Visuals = new RoomVisuals();
        }

        public sealed class RoomVisuals
        {
            public bool UseHospitalFloorTile;
            public float? WallThickness;

            public PrefabReference Floor;
            public int InteriorWallStyle;
            public int ExteriorWallStyle;

            public string FloorMaterialAsset;
            public string InteriorMaterialAsset;
            public string ExteriorMaterialAsset;
            public string DoorMaterialAsset;

        }

        public sealed class PrefabReference
        {
            public string NativePrefabName;
            public string NativeMeshName;
            public string BundlePrefabAsset;
        }

        public sealed class WallStyleDefinition
        {
            public WallStyleDefinition(int number)
            {
                Number = number;
            }

            public int Number;
            public WallGeometry Geometry = new WallGeometry();
        }

        public sealed class WallGeometry
        {


            public PrefabReference Wall;
            public PrefabReference WallCornerLeft;
            public PrefabReference WallCornerRight;
            public PrefabReference WallCornerBoth;

            public PrefabReference CornerInner;
            public PrefabReference CornerOuter;

            public PrefabReference Door;


            public PrefabReference Window;
            public PrefabReference WindowCornerLeft;
            public PrefabReference WindowCornerRight;
            public PrefabReference WindowCornerBoth;

        }

        public sealed class SurfaceMaterials
        {
            public Material Floor;
            public Material Interior;
            public Material Exterior;
            public Material Door;
        }

        // =====================================================================
        // DEFINICION COMUN DE SALA
        // =====================================================================

        public static RoomDefinition EnsureDefinition(
            Metagame metagame,
            RoomEntry entry,
            Func<string, LocalisedString> localize,
            IDictionary<string, RequiredItem> requirements,
            IDictionary<string, StaffRequired> staff)
        {
            if (metagame?.RoomDatabase?.Instance == null ||
                entry == null ||
                !entry.Enabled ||
                entry.TypeValue < 0 ||
                entry.SharedDefinitionId <= 0)
                return null;

            var database = metagame.RoomDatabase.Instance;
            var rooms = database.Rooms;
            if (rooms == null)
                return null;

            RoomDefinition definition = null;
            SharedInstance_TH20TH20_RoomDefinition wrapper = null;

            foreach (var shared in rooms)
            {
                if (shared?.Instance == null || !Is(shared.Instance, entry))
                    continue;

                definition = shared.Instance;
                wrapper = shared as SharedInstance_TH20TH20_RoomDefinition;
                break;
            }

            var isNew = definition == null;
            if (isNew)
            {
                definition = new RoomDefinition();
                SetField(definition, "_components", Array.Empty<EntityComponent>());
            }

            ResolveNativeWallDefaults(rooms);
            ApplyCommonDefinition(definition, entry, localize, requirements, staff);

            if (isNew)
            {
                wrapper = ScriptableObject.CreateInstance<SharedInstance_TH20TH20_RoomDefinition>();
                wrapper.name = entry.WrapperName ?? ("UnderPressure " + entry.Key + " Room");
                wrapper.hideFlags = HideFlags.DontUnloadUnusedAsset;
                wrapper.ID = entry.SharedDefinitionId;
                wrapper.Instance = definition;

                var expanded = new SharedInstance<RoomDefinition>[rooms.Length + 1];
                Array.Copy(rooms, expanded, rooms.Length);
                expanded[rooms.Length] = wrapper;
                database.Rooms = expanded;
            }

            RuntimeDefinitions[entry] = definition;
            RuntimeWrappers[entry] = wrapper;
            RegisterRoomSaveAsset(metagame, entry, wrapper, definition);
            return definition;
        }

        private static void ApplyCommonDefinition(
            RoomDefinition definition,
            RoomEntry entry,
            Func<string, LocalisedString> localize,
            IDictionary<string, RequiredItem> requirements,
            IDictionary<string, StaffRequired> staff)
        {
            SetField(definition, "_type", (RoomDefinition.Type)entry.TypeValue);

            if (localize != null)
            {
                if (!string.IsNullOrEmpty(entry.NameTerm))
                    SetField(definition, "Name", localize(entry.NameTerm));

                if (!string.IsNullOrEmpty(entry.DescriptionTerm))
                {
                    var description = localize(entry.DescriptionTerm);
                    SetField(definition, "Description", description);
                    SetField(definition, "LongDescription", description);
                    SetField(definition, "UnlockedMessage", description);
                }
            }

            SetField(definition, "_cost", entry.Cost);
            SetField(definition, "_silverCost", entry.SilverCost);
            SetField(definition, "_minSizeX", entry.MinSizeX);
            SetField(definition, "_minSizeY", entry.MinSizeY);
            SetField(definition, "_maxCapacity", entry.MaxCapacity);
            SetField(definition, "_hasQueue", entry.HasQueue);
            SetField(definition, "_canManageQueue", entry.CanManageQueue);
            SetField(definition, "_allowQueueWarningStatusIcon", entry.AllowQueueWarningStatusIcon);
            SetField(definition, "MinimumStaffCount", entry.MinimumStaffCount);
            SetField(definition, "MustBeWhiteListed", entry.MustBeWhiteListed);
            SetField(definition, "DlcPackRequired", null);
            SetField(definition, "_showUnitsProcessedInGUI", entry.ShowUnitsProcessedInGUI);
            SetField(definition, "_showTotalRevenueInGUI", entry.ShowTotalRevenueInGUI);

            SetField(definition, "_requiredItemsNew",
                ResolveRequiredItems(entry.RequiredItemKeys, requirements));
            SetField(definition, "_requiredWorkingItems",
                ResolveWorkingItems(entry.RequiredWorkingItemKeys, requirements));
            SetField(definition, "_requiresStaff",
                ResolveStaff(entry.RequiredStaffKeys, staff));

            SetField(definition, "_singlePlaceItems", Array.Empty<RoomItemDefinition.Type>());
            SetField(definition, "_staffPatientInteractions", Array.Empty<StaffPatientInteraction>());
            SetField(definition, "WhoCanUseRoom", Array.Empty<WhoCanUseRoom.GroupDefinition>());
            SetField(definition, "_itemToLeaveOnCursor", null);

            ApplyGeometry(definition, entry);
            ApplyIcon(definition, entry.IconSpriteName);
            AllowAvailableItemsInRoom(entry, requirements);
        }

        public static bool Is(RoomDefinition definition, RoomEntry entry)
        {
            return definition != null &&
                   entry != null &&
                   entry.TypeValue >= 0 &&
                   Convert.ToInt32(definition._type) == entry.TypeValue;
        }

        public static RoomEntry Find(RoomDefinition definition)
        {
            if (definition == null)
                return null;

            foreach (var entry in All)
                if (entry.Enabled && Is(definition, entry))
                    return entry;

            return null;
        }

        public static RoomDefinition GetDefinition(RoomEntry entry)
        {
            RuntimeDefinitions.TryGetValue(entry, out var definition);
            return definition;
        }

        public static SharedInstance_TH20TH20_RoomDefinition GetSharedDefinition(RoomEntry entry)
        {
            RuntimeWrappers.TryGetValue(entry, out var wrapper);
            return wrapper;
        }

        // =====================================================================
        // REQUISITOS / OBJETOS / PERSONAL
        // =====================================================================

        public static RequiredItem FindDoorRequirement(SharedInstance<RoomDefinition>[] rooms)
        {
            if (rooms == null)
                return null;

            foreach (var shared in rooms)
            {
                var room = shared?.Instance;
                if (room == null || room._type != RoomDefinition.Type.GPOffice)
                    continue;

                foreach (var requirement in room.GetRequiredItems() ?? Array.Empty<RequiredItem>())
                    if (requirement != null && requirement.ContainsType(RoomItemDefinition.Type.Door))
                        return requirement;
            }

            return null;
        }

        public static void EnsureAvailableItems(
            WorldState worldState,
            RoomEntry entry,
            IDictionary<string, RequiredItem> requirements)
        {
            if (worldState?.AvailableRoomItems == null || entry == null || requirements == null)
                return;

            foreach (var key in entry.AvailableItemKeys ?? Array.Empty<string>())
            {
                if (!requirements.TryGetValue(key, out var requirement) || requirement?.Items == null)
                    continue;

                foreach (var shared in requirement.Items)
                {
                    var item = shared?.Instance;
                    if (item != null && !worldState.AvailableRoomItems.Contains(item))
                        worldState.AvailableRoomItems.Add(item);
                }
            }
        }

        public static void EnsureRequirementItemsAvailable(WorldState worldState, RequiredItem requirement)
        {
            if (worldState?.AvailableRoomItems == null || requirement?.Items == null)
                return;

            foreach (var shared in requirement.Items)
            {
                var item = shared?.Instance;
                if (item != null && !worldState.AvailableRoomItems.Contains(item))
                    worldState.AvailableRoomItems.Add(item);
            }
        }

        public static void EnsureRoomTemplateBucket(Level level, RoomEntry entry)
        {
            if (level == null || entry == null || entry.TypeValue < 0)
                return;

            var templates = level.App?.RoomTemplatesManager?.RoomTemplates;
            var roomType = (RoomDefinition.Type)entry.TypeValue;
            if (templates == null || templates.ContainsKey(roomType))
                return;

            templates.Add(roomType, new Dictionary<string, RoomTemplate>());
        }

        private static RequiredItem[] ResolveRequiredItems(
            IEnumerable<string> keys,
            IDictionary<string, RequiredItem> requirements)
        {
            var result = new List<RequiredItem>();
            if (requirements == null)
                return result.ToArray();

            foreach (var key in keys ?? Array.Empty<string>())
            {
                if (!requirements.TryGetValue(key, out var requirement) || requirement == null)
                    continue;
                if (!result.Contains(requirement))
                    result.Add(requirement);
            }

            return result.ToArray();
        }

        private static SharedInstance<RoomItemDefinition>[] ResolveWorkingItems(
            IEnumerable<string> keys,
            IDictionary<string, RequiredItem> requirements)
        {
            var result = new List<SharedInstance<RoomItemDefinition>>();
            if (requirements == null)
                return result.ToArray();

            foreach (var key in keys ?? Array.Empty<string>())
            {
                if (!requirements.TryGetValue(key, out var requirement) || requirement?.Items == null)
                    continue;

                foreach (var item in requirement.Items)
                    if (item != null && !result.Contains(item))
                        result.Add(item);
            }

            return result.ToArray();
        }

        private static StaffRequired[] ResolveStaff(
            IEnumerable<string> keys,
            IDictionary<string, StaffRequired> staff)
        {
            var result = new List<StaffRequired>();
            if (staff == null)
                return result.ToArray();

            foreach (var key in keys ?? Array.Empty<string>())
            {
                if (!staff.TryGetValue(key, out var requirement) || requirement == null)
                    continue;
                if (!result.Contains(requirement))
                    result.Add(requirement);
            }

            return result.ToArray();
        }

        private static void AllowAvailableItemsInRoom(
            RoomEntry entry,
            IDictionary<string, RequiredItem> requirements)
        {
            if (entry == null || requirements == null || entry.TypeValue < 0)
                return;

            var roomType = (RoomDefinition.Type)entry.TypeValue;
            var canField = AccessTools.Field(typeof(RoomItemDefinition), "_canBePlacedIn");
            var cantField = AccessTools.Field(typeof(RoomItemDefinition), "_cantBePlacedIn");

            foreach (var key in entry.AvailableItemKeys ?? Array.Empty<string>())
            {
                if (!requirements.TryGetValue(key, out var requirement) || requirement?.Items == null)
                    continue;

                foreach (var shared in requirement.Items)
                {
                    var item = shared?.Instance;
                    if (item == null)
                        continue;

                    var allowed = canField?.GetValue(item) as RoomDefinition.Type[];
                    if (allowed != null && allowed.Length > 0 && Array.IndexOf(allowed, roomType) < 0)
                    {
                        var expanded = new RoomDefinition.Type[allowed.Length + 1];
                        Array.Copy(allowed, expanded, allowed.Length);
                        expanded[allowed.Length] = roomType;
                        canField.SetValue(item, expanded);
                    }

                    var forbidden = cantField?.GetValue(item) as RoomDefinition.Type[];
                    if (forbidden == null || Array.IndexOf(forbidden, roomType) < 0)
                        continue;

                    var reduced = new List<RoomDefinition.Type>();
                    foreach (var type in forbidden)
                        if (type != roomType)
                            reduced.Add(type);

                    cantField.SetValue(item, reduced.ToArray());
                }
            }
        }

        // =====================================================================
        // GEOMETRIA
        // =====================================================================

        public static PrefabReference NativePrefab(string prefabName)
        {
            return new PrefabReference { NativePrefabName = prefabName };
        }

        public static PrefabReference NativePrefab(string prefabName, string meshName)
        {
            return new PrefabReference
            {
                NativePrefabName = prefabName,
                NativeMeshName = meshName
            };
        }

        public static PrefabReference NativeMesh(string meshName)
        {
            return new PrefabReference { NativeMeshName = meshName };
        }

        public static PrefabReference BundlePrefab(string assetPath)
        {
            return new PrefabReference { BundlePrefabAsset = assetPath };
        }

        private sealed class BuiltGeometry
        {
            public GameObject Floor;
            public RoomWallDefinition Interior;
            public RoomWallDefinition Exterior;
            public SharedInstance<RoomWallDefinition> InteriorShared;
        }

        private static void ApplyGeometry(RoomDefinition definition, RoomEntry entry)
        {
            if (!GeometryCache.TryGetValue(entry, out var geometry))
            {
                geometry = BuildGeometry(entry);
                GeometryCache[entry] = geometry;
            }

            var visuals = entry.Visuals ?? new RoomVisuals();
            SetField(definition, "UseHospitalFloorTile", visuals.UseHospitalFloorTile);
            SetField(definition, "_roomFloorTile", geometry.Floor);
            SetField(definition, "_wallsInterior", geometry.Interior);
            SetField(definition, "_wallsExterior", geometry.Exterior);
            SetField(definition, "_blueprintWallDefinition", geometry.InteriorShared);
            SetField(definition, "_dragAddWallDefinition", geometry.InteriorShared);
            SetField(definition, "_dragSubWallDefinition", geometry.InteriorShared);

            if (visuals.WallThickness.HasValue)
                SetField(definition, "WallThickness", visuals.WallThickness.Value);

            // Todas las salas personalizadas usan por defecto la iluminacion global del hospital.
            SetField(definition, "_roomLightMaterial", null);
            SetField(definition, "_roomReflectionCubemap", null);
            SetField(definition, "_roomClosedLightMaterial", null);
            SetField(definition, "_roomClosedReflectionCubemap", null);
            SetField(definition, "_roomOperationalLightMaterial", null);
            SetField(definition, "_roomOperationalReflectionCubemap", null);
        }

        private static BuiltGeometry BuildGeometry(RoomEntry entry)
        {
            var visuals = entry.Visuals ?? new RoomVisuals();
            var interior = visuals.InteriorWallStyle == NativeStyle ? NativeInteriorWalls : BuildWallDefinitionForStyle(visuals.InteriorWallStyle, entry.Key + " Interior");
            var exterior = visuals.ExteriorWallStyle == NativeStyle ? NativeExteriorWalls : BuildWallDefinitionForStyle(visuals.ExteriorWallStyle, entry.Key + " Exterior");

            var interiorShared = NativeBlueprintWalls;

            return new BuiltGeometry
            {
                Floor = ResolvePrefab(visuals.Floor, entry.Key + " Floor"),
                Interior = interior,
                Exterior = exterior,
                InteriorShared = interiorShared
            };
        }

        private static void ResolveNativeWallDefaults(SharedInstance<RoomDefinition>[] rooms)
        {
            var interiorCounts = new Dictionary<WallsDefinition, int>();
            var exteriorCounts = new Dictionary<WallsDefinition, int>();
            var blueprintCounts = new Dictionary<WallsDefinition, int>();
            var interiorValues = new Dictionary<WallsDefinition, RoomWallDefinition>();
            var exteriorValues = new Dictionary<WallsDefinition, RoomWallDefinition>();
            var blueprintValues =
                new Dictionary<WallsDefinition, SharedInstance<RoomWallDefinition>>();

            foreach (var shared in rooms)
            {
                var definition = shared?.Instance;
                if (definition == null || Find(definition) != null)
                    continue;

                CountWallDefinition(definition._wallsInterior, interiorCounts, interiorValues);
                CountWallDefinition(definition._wallsExterior, exteriorCounts, exteriorValues);

                var blueprint = BlueprintWallDefinitionField?.GetValue(definition)
                    as SharedInstance<RoomWallDefinition>;
                var blueprintWalls = blueprint?.Instance?.GetWallsDefinition();
                if (blueprintWalls == null || !HasAnyWallPiece(blueprintWalls))
                    continue;

                blueprintCounts[blueprintWalls] = blueprintCounts.TryGetValue(blueprintWalls, out var count)
                    ? count + 1
                    : 1;
                if (!blueprintValues.ContainsKey(blueprintWalls))
                    blueprintValues[blueprintWalls] = blueprint;
            }

            NativeInteriorWalls = FindMostCommonWall(interiorCounts, interiorValues);
            NativeExteriorWalls = FindMostCommonWall(exteriorCounts, exteriorValues);
            NativeBlueprintWalls = FindMostCommonBlueprint(blueprintCounts, blueprintValues);
        }

        private static void CountWallDefinition(
            RoomWallDefinition definition,
            IDictionary<WallsDefinition, int> counts,
            IDictionary<WallsDefinition, RoomWallDefinition> values)
        {
            var walls = definition?.GetWallsDefinition();
            if (walls == null || !HasAnyWallPiece(walls))
                return;

            counts[walls] = counts.TryGetValue(walls, out var count) ? count + 1 : 1;
            if (!values.ContainsKey(walls))
                values[walls] = definition;
        }

        private static RoomWallDefinition FindMostCommonWall(
            IDictionary<WallsDefinition, int> counts,
            IDictionary<WallsDefinition, RoomWallDefinition> values)
        {
            var walls = FindMostCommonWalls(counts);
            return walls != null && values.TryGetValue(walls, out var value) ? value : null;
        }

        private static SharedInstance<RoomWallDefinition> FindMostCommonBlueprint(
            IDictionary<WallsDefinition, int> counts,
            IDictionary<WallsDefinition, SharedInstance<RoomWallDefinition>> values)
        {
            var walls = FindMostCommonWalls(counts);
            return walls != null && values.TryGetValue(walls, out var value) ? value : null;
        }

        private static WallsDefinition FindMostCommonWalls(
            IDictionary<WallsDefinition, int> counts)
        {
            WallsDefinition selected = null;
            var highestCount = 0;
            foreach (var pair in counts)
            {
                if (pair.Value <= highestCount)
                    continue;

                selected = pair.Key;
                highestCount = pair.Value;
            }

            return selected;
        }

        public static WallStyleDefinition FindWallStyle(int number)
        {
            if (number <= NativeStyle)
                return null;

            foreach (var style in WallStyles)
                if (style != null && style.Number == number)
                    return style;

            return null;
        }

        private static RoomWallDefinition BuildWallDefinitionForStyle(
            int styleNumber,
            string usageName)
        {
            if (styleNumber == NativeStyle)
                return BuildWallDefinition(usageName, null);

            if (BuiltWallStyles.TryGetValue(styleNumber, out var cached))
                return cached;

            var style = FindWallStyle(styleNumber);
            if (style == null)
            {
                UnityEngine.Debug.LogError(
                    "[UnderPressure] Estilo de pared no registrado: " + styleNumber);
                return BuildWallDefinition(usageName, null);
            }

            var built = BuildWallDefinition("Style " + styleNumber, style.Geometry);
            BuiltWallStyles[styleNumber] = built;
            return built;
        }

        private static RoomWallDefinition BuildWallDefinition(string name, WallGeometry source)
        {
            source = source ?? new WallGeometry();

            var walls = new WallsDefinition
            {

                Wall = ResolvePrefab(source.Wall, name + " Wall"),
                WallCornerLeft = ResolvePrefab(source.WallCornerLeft, name + " WallCornerLeft"),
                WallCornerRight = ResolvePrefab(source.WallCornerRight, name + " WallCornerRight"),
                WallCornerBoth = ResolvePrefab(source.WallCornerBoth, name + " WallCornerBoth"),
                CornerInner = ResolvePrefab(source.CornerInner, name + " CornerInner"),
                CornerOuter = ResolvePrefab(source.CornerOuter, name + " CornerOuter"),
                Door = ResolvePrefab(source.Door, name + " Door"),

                Window = ResolvePrefab(source.Window, name + " Window"),
                WindowCornerLeft = ResolvePrefab(source.WindowCornerLeft, name + " WindowCornerLeft"),
                WindowCornerRight = ResolvePrefab(source.WindowCornerRight, name + " WindowCornerRight"),
                WindowCornerBoth = ResolvePrefab(source.WindowCornerBoth, name + " WindowCornerBoth"),

            };

            var wallsShared = ScriptableObject.CreateInstance<SharedInstance_TH20TH20_WallsDefinition>();
            wallsShared.name = "UnderPressure " + name;
            wallsShared.hideFlags = HideFlags.HideAndDontSave;
            wallsShared.Instance = walls;

            var roomWall = new RoomWallDefinition();
            RoomWallWallsField?.SetValue(roomWall, wallsShared);
            return roomWall;
        }

        private static GameObject ResolvePrefab(PrefabReference reference, string generatedName)
        {
            if (reference == null)
                return null;

            if (!string.IsNullOrEmpty(reference.BundlePrefabAsset))
            {
                var bundlePrefab = UnderPressureAssetBundle.LoadAsset<GameObject>(reference.BundlePrefabAsset);
                if (bundlePrefab != null)
                    return bundlePrefab;
            }

            if (!string.IsNullOrEmpty(reference.NativePrefabName))
            {
                var nativePrefab = ResolveNativePrefab(reference.NativePrefabName, reference.NativeMeshName);
                if (nativePrefab != null)
                    return nativePrefab;
            }

            // Fallback determinista: si el prefab nativo no esta cargado pero si la malla,
            // construimos el contenedor minimo que MeshUtils.SetStaticMeshFromPrefab espera.
            if (!string.IsNullOrEmpty(reference.NativeMeshName))
                return CreateMeshPrefab(generatedName, ResolveNative<Mesh>(reference.NativeMeshName));

            return null;
        }

        private static GameObject ResolveNativePrefab(string prefabName, string expectedMeshName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return null;

            GameObject firstNamedPrefab = null;
            GameObject firstNamedAsset = null;
            GameObject firstMeshMatch = null;

            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null ||
                    !string.Equals(candidate.name, prefabName, StringComparison.Ordinal))
                    continue;

                // MeshUtils.SetStaticMeshFromPrefab() exige que el prefab tenga al menos
                // un MeshFilter y un MeshRenderer en su jerarquia.
                if (candidate.GetComponentInChildren<MeshFilter>(true) == null ||
                    candidate.GetComponentInChildren<MeshRenderer>(true) == null)
                    continue;

                if (firstNamedPrefab == null)
                    firstNamedPrefab = candidate;

                // Los assets/prefabs nativos no pertenecen a una Scene. Los preferimos
                // frente a posibles instancias runtime con el mismo nombre.
                if (!candidate.scene.IsValid() && firstNamedAsset == null)
                    firstNamedAsset = candidate;

                if (string.IsNullOrEmpty(expectedMeshName) ||
                    !PrefabContainsMesh(candidate, expectedMeshName))
                    continue;

                if (!candidate.scene.IsValid())
                    return candidate;

                if (firstMeshMatch == null)
                    firstMeshMatch = candidate;
            }

            if (firstMeshMatch != null)
                return firstMeshMatch;

            var resolved = firstNamedAsset ?? firstNamedPrefab;
            if (resolved != null)
            {
                // IMPORTANTE: el juego puede obtener la malla real desde MeshRandomizer.
                // El nombre de prefab es la referencia autoritativa; el nombre de malla
                // solo sirve para verificar/seleccionar cuando sea posible.
                if (!string.IsNullOrEmpty(expectedMeshName))
                {
                    UnityEngine.Debug.LogWarning(
                        "[UnderPressure] Prefab nativo encontrado por nombre '" + prefabName +
                        "', pero no se pudo confirmar la malla '" + expectedMeshName +
                        "'. Se usara el prefab exacto, igual que hace el juego nativo.");
                }

                return resolved;
            }

            UnityEngine.Debug.LogError(
                "[UnderPressure] No se encontro un prefab nativo util: " + prefabName);
            return null;
        }

        private static bool PrefabContainsMesh(GameObject prefab, string meshName)
        {
            if (prefab == null || string.IsNullOrEmpty(meshName))
                return false;

            foreach (var meshFilter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = meshFilter?.sharedMesh;
                if (mesh != null && string.Equals(mesh.name, meshName, StringComparison.Ordinal))
                    return true;
            }

            // MeshUtils.SetStaticMeshFromPrefab() usa MeshRandomizer.GetMesh() cuando
            // existe. Por tanto una malla valida puede estar en _meshes y NO ser la
            // sharedMesh actual del MeshFilter. El resolver anterior ignoraba esto y
            // podia devolver null para un prefab perfectamente valido.
            var meshesField = AccessTools.Field(typeof(MeshRandomizer), "_meshes");
            if (meshesField == null)
                return false;

            foreach (var randomizer in prefab.GetComponentsInChildren<MeshRandomizer>(true))
            {
                var meshes = meshesField.GetValue(randomizer) as Mesh[];
                if (meshes == null)
                    continue;

                foreach (var mesh in meshes)
                    if (mesh != null && string.Equals(mesh.name, meshName, StringComparison.Ordinal))
                        return true;
            }

            return false;
        }

        private static GameObject CreateMeshPrefab(string name, Mesh mesh)
        {
            if (mesh == null)
                return null;

            if (MeshPrefabCache.TryGetValue(mesh, out var cached) && cached != null)
                return cached;

            var prefab = new GameObject("UnderPressure Geometry - " + name);
            prefab.hideFlags = HideFlags.HideAndDontSave;
            prefab.AddComponent<MeshFilter>().sharedMesh = mesh;
            prefab.AddComponent<MeshRenderer>().sharedMaterials = Array.Empty<Material>();
            MeshPrefabCache[mesh] = prefab;
            return prefab;
        }

        private static T ResolveNative<T>(string name) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (var asset in Resources.FindObjectsOfTypeAll<T>())
                if (asset != null && string.Equals(asset.name, name, StringComparison.Ordinal))
                    return asset;

            UnityEngine.Debug.LogWarning("[UnderPressure] Recurso nativo de sala no encontrado: " + name);
            return null;
        }

        internal static bool TryGetConfiguredExteriorWallDefinition(
            Room room,
            out RoomWallDefinition wallDefinition)
        {
            wallDefinition = null;

            if (room?.Definition == null || Find(room.Definition) == null)
                return false;

            var candidate = room.Definition._wallsExterior;
            var walls = candidate?.GetWallsDefinition();
            if (walls == null || !HasAnyWallPiece(walls))
                return false;

            wallDefinition = candidate;
            return true;
        }

        internal static bool TryGetDirectExteriorPiece(
            RoomWallDefinition wallDefinition,
            RoomWallDefinition.Type type,
            out GameObject piece)
        {
            piece = null;
            if (wallDefinition == null || !IsCatalogExteriorWallDefinition(wallDefinition))
                return false;

            var walls = wallDefinition.GetWallsDefinition();
            if (walls == null)
                return false;

            // El enum RoomWallDefinition.Type usa los mismos nombres que las piezas
            // de WallsDefinition. Asi evitamos limitar el override a Wall/Window y
            // dejamos pasar tambien CornerOuter, WallBack y cualquier otra pieza
            // exterior configurada por la sala. Si esa pieza concreta es null,
            // dejamos que el juego use su comportamiento/fallback normal.
            var pieceField = AccessTools.Field(typeof(WallsDefinition), type.ToString());
            if (pieceField == null || !typeof(GameObject).IsAssignableFrom(pieceField.FieldType))
                return false;

            piece = pieceField.GetValue(walls) as GameObject;
            return piece != null;
        }

        private static bool IsCatalogExteriorWallDefinition(RoomWallDefinition wallDefinition)
        {
            if (wallDefinition == null)
                return false;

            var walls = wallDefinition.GetWallsDefinition();
            if (walls == null)
                return false;

            foreach (var pair in GeometryCache)
            {
                var geometry = pair.Value;
                if (geometry?.Exterior == null)
                    continue;

                if (ReferenceEquals(geometry.Exterior, wallDefinition))
                    return true;

                var configuredWalls = geometry.Exterior.GetWallsDefinition();
                if (configuredWalls == null)
                    continue;

                // Algunas rutas del juego pueden conservar la WallsDefinition pero
                // trabajar con otra instancia de RoomWallDefinition. Aceptamos tambien
                // esa identidad y, como ultimo respaldo, las mismas piezas principales.
                if (ReferenceEquals(configuredWalls, walls))
                    return true;

                if (configuredWalls.Wall != null && ReferenceEquals(configuredWalls.Wall, walls.Wall))
                    return true;

                if (configuredWalls.CornerOuter != null &&
                    ReferenceEquals(configuredWalls.CornerOuter, walls.CornerOuter))
                    return true;
            }

            return false;
        }

        private static bool HasAnyWallPiece(WallsDefinition walls)
        {
            return walls.WallBack != null ||
                   walls.WindowBack != null ||
                   walls.Wall != null ||
                   walls.WallCornerLeft != null ||
                   walls.WallCornerRight != null ||
                   walls.WallCornerBoth != null ||
                   walls.CornerInner != null ||
                   walls.CornerOuter != null ||
                   walls.Door != null ||
                   walls.DoorCornerLeft != null ||
                   walls.DoorCornerRight != null ||
                   walls.DoorCornerBoth != null ||
                   walls.Window != null ||
                   walls.WindowCornerLeft != null ||
                   walls.WindowCornerRight != null ||
                   walls.WindowCornerBoth != null ||
                   walls.Pillar != null ||
                   walls.PillarCornerLeft != null ||
                   walls.PillarCornerRight != null ||
                   walls.PillarCornerBoth != null ||
                   walls.FillerLeft != null ||
                   walls.FillerRight != null;
        }

        // =====================================================================
        // MATERIALES DE SUPERFICIE (COMUN PARA TODAS LAS SALAS)
        // =====================================================================

        public static SurfaceMaterials LoadSurfaceMaterials(RoomEntry entry)
        {
            if (entry == null)
                return null;

            var visuals = entry.Visuals ?? new RoomVisuals();
            return new SurfaceMaterials
            {
                Floor = LoadMaterial(visuals.FloorMaterialAsset),
                Interior = LoadMaterial(visuals.InteriorMaterialAsset),
                Exterior = LoadMaterial(visuals.ExteriorMaterialAsset),
                Door = LoadMaterial(visuals.DoorMaterialAsset)
            };
        }

        public static void SetSurfaceMaterials(RoomEntry entry, SurfaceMaterials materials)
        {
            if (entry == null || materials == null)
                return;

            SurfaceMaterialSets[entry] = materials;
            RefreshTrackedVisuals();
        }

        private static Material LoadMaterial(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var material = UnderPressureAssetBundle.LoadAsset<Material>(assetPath);
            if (material == null)
                UnityEngine.Debug.LogError("[UnderPressure] Material de sala no encontrado: " + assetPath);
            return material;
        }

        private static bool TryGetSurfaceMaterials(RoomDefinition definition, out SurfaceMaterials materials)
        {
            materials = null;
            var entry = Find(definition);
            return entry != null && SurfaceMaterialSets.TryGetValue(entry, out materials);
        }

        internal static void PrepareVisual(RoomFloorPlanVisual visual, FloorPlan floorPlan)
        {
            if (visual == null ||
                visual is BlueprintFloorPlanVisual ||
                floorPlan?.Definition == null ||
                !TryGetSurfaceMaterials(floorPlan.Definition, out _))
                return;

            FloorOverrideField?.SetValue(visual, null);
            WallOverrideField?.SetValue(visual, null);
        }

        internal static void PrepareRestoredVisual(RoomFloorPlanVisual visual)
        {
            if (visual == null)
                return;

            PrepareVisual(visual, FloorPlanField?.GetValue(visual) as FloorPlan);
        }

        internal static void ApplyVisual(RoomFloorPlanVisual visual)
        {
            if (visual == null || visual is BlueprintFloorPlanVisual)
                return;

            var floorPlan = FloorPlanField?.GetValue(visual) as FloorPlan;
            if (floorPlan?.Definition == null ||
                !TryGetSurfaceMaterials(floorPlan.Definition, out var materials))
                return;

            Track(TrackedRoomVisuals, visual);

            var floorRenderers = FloorRenderersField?.GetValue(visual) as List<Renderer>;
            if (floorRenderers != null)
                foreach (var renderer in floorRenderers)
                    ApplyFirstMaterial(renderer, materials.Floor);

            var wallObjects = WallObjectsField?.GetValue(visual)
                as List<KeyValuePair<Transform, Transform>>;
            if (wallObjects != null)
                foreach (var wall in wallObjects)
                {
                    ApplyFirstMaterial(wall.Key, materials.Interior);
                    ApplyFirstMaterial(wall.Value, materials.Exterior);
                }

            if (floorPlan.Items != null)
                foreach (var item in floorPlan.Items)
                    ApplyDoorMaterial(item, materials.Door);
        }

        internal static void ApplyVisual(CorridorWallsVisual visual)
        {
            if (visual == null)
                return;

            Track(CorridorVisuals, visual);
            var activeWalls = CorridorWallsField?.GetValue(visual) as IEnumerable;
            if (activeWalls == null || CorridorWallTransformField == null || CorridorWallRoomField == null)
                return;

            foreach (var entry in activeWalls)
            {
                var room = CorridorWallRoomField.GetValue(entry) as Room;
                if (room?.Definition == null ||
                    !TryGetSurfaceMaterials(room.Definition, out var materials))
                    continue;

                ApplyFirstMaterial(
                    CorridorWallTransformField.GetValue(entry) as Transform,
                    materials.Exterior,
                    true);
            }
        }

        private static void ApplyDoorMaterial(RoomItem item, Material replacement)
        {
            if (replacement == null ||
                item?.Definition == null ||
                item.Definition.ItemType != RoomItemDefinition.Type.Door ||
                item.OwningRoom?.Definition == null ||
                !TryGetSurfaceMaterials(item.OwningRoom.Definition, out _))
                return;

            var root = item.Visual?.GameObject?.transform;
            if (root == null)
                return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                var changed = false;
                for (var index = 0; index < materials.Length; ++index)
                {
                    var material = materials[index];
                    if (material == null || string.IsNullOrEmpty(material.name))
                        continue;

                    var name = material.name;
                    if (name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) < 0 ||
                        name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Trim", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    materials[index] = replacement;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        private static void ApplyFirstMaterial(
            Transform root,
            Material material,
            bool replaceAllSlots = false)
        {
            if (root == null || material == null)
                return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!replaceAllSlots)
                {
                    ApplyFirstMaterial(renderer, material);
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = material;
                    continue;
                }

                var changed = false;
                for (var index = 0; index < materials.Length; ++index)
                {
                    if (ReferenceEquals(materials[index], material))
                        continue;

                    materials[index] = material;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        private static void ApplyFirstMaterial(Renderer renderer, Material material)
        {
            if (renderer == null || material == null)
                return;

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = material;
                return;
            }

            if (ReferenceEquals(materials[0], material))
                return;

            materials[0] = material;
            renderer.sharedMaterials = materials;
        }

        private static void RefreshTrackedVisuals()
        {
            Refresh(TrackedRoomVisuals, target => ApplyVisual(target as RoomFloorPlanVisual));
            Refresh(CorridorVisuals, target => ApplyVisual(target as CorridorWallsVisual));
        }

        private static void Refresh(List<WeakReference> references, Action<object> apply)
        {
            for (var index = references.Count - 1; index >= 0; --index)
            {
                var target = references[index].Target;
                if (target == null)
                {
                    references.RemoveAt(index);
                    continue;
                }

                apply(target);
            }
        }

        private static void Track(List<WeakReference> references, object target)
        {
            foreach (var reference in references)
                if (ReferenceEquals(reference.Target, target))
                    return;

            references.Add(new WeakReference(target));
        }

        // =====================================================================
        // ICONO / SAVE / HELPERS
        // =====================================================================

        private static void ApplyIcon(RoomDefinition definition, string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return;

            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null ||
                    !string.Equals(sprite.name, spriteName, StringComparison.OrdinalIgnoreCase))
                    continue;

                SetField(definition, "_icon", sprite);
                SetField(definition, "_jobAssignmentIcon", sprite);
                return;
            }
        }

        private static void RegisterRoomSaveAsset(
            Metagame metagame,
            RoomEntry entry,
            SharedInstance_TH20TH20_RoomDefinition wrapper,
            RoomDefinition definition)
        {
            var saveSystem = metagame?.App?.SaveSystem;
            if (saveSystem == null || wrapper == null || definition == null)
                return;

            var mapping = AccessTools.Field(typeof(SaveSystem), "_assetIDs")
                ?.GetValue(saveSystem) as BiDictionary<int, object>;
            if (mapping == null)
                return;

            var added = 0;
            added += RegisterSaveObject(mapping, entry.SharedDefinitionId, wrapper);
            added += RegisterSaveObject(mapping, entry.SharedDefinitionId - 1, definition);
            if (added > 0)
                RefreshSerializerMappings(saveSystem, mapping);
        }

        private static int RegisterSaveObject(BiDictionary<int, object> mapping, int id, object value)
        {
            if (id == 0 || value == null || mapping.ContainsValue(value))
                return 0;

            if (mapping.ContainsKey(id))
            {
                UnityEngine.Debug.LogError("[UnderPressure] ID de sala ocupado: " + id);
                return 0;
            }

            mapping.Add(id, value);
            return 1;
        }

        private static void RefreshSerializerMappings(SaveSystem saveSystem, BiDictionary<int, object> mapping)
        {
            var serializerFields = new[]
            {
                "_serializerLevel",
                "_serializerMetagame",
                "_serializerRoomTemplates"
            };

            foreach (var fieldName in serializerFields)
            {
                var serializer = AccessTools.Field(typeof(SaveSystem), fieldName)
                    ?.GetValue(saveSystem) as FullSerializerSave.fsSerializer;
                serializer?.SetIDObjectMapping(mapping.FirstToSecond, mapping.SecondToFirst);
            }
        }

        private static void SetField(RoomDefinition definition, string fieldName, object value)
        {
            var field = AccessTools.Field(typeof(RoomDefinition), fieldName);
            if (field == null)
            {
                UnityEngine.Debug.LogWarning("[UnderPressure] Campo RoomDefinition no encontrado: " + fieldName);
                return;
            }

            field.SetValue(definition, value);
        }

        private static RoomWallDefinition NativeInteriorWalls;
        private static RoomWallDefinition NativeExteriorWalls;
        private static SharedInstance<RoomWallDefinition> NativeBlueprintWalls;

        private static readonly FieldInfo BlueprintWallDefinitionField =
            AccessTools.Field(typeof(RoomDefinition), "_blueprintWallDefinition");

        private static readonly Dictionary<RoomEntry, RoomDefinition> RuntimeDefinitions =
            new Dictionary<RoomEntry, RoomDefinition>();

        private static readonly Dictionary<RoomEntry, SharedInstance_TH20TH20_RoomDefinition> RuntimeWrappers =
            new Dictionary<RoomEntry, SharedInstance_TH20TH20_RoomDefinition>();

        private static readonly Dictionary<RoomEntry, BuiltGeometry> GeometryCache =
            new Dictionary<RoomEntry, BuiltGeometry>();

        private static readonly Dictionary<int, RoomWallDefinition> BuiltWallStyles =
            new Dictionary<int, RoomWallDefinition>();

        private static readonly Dictionary<Mesh, GameObject> MeshPrefabCache =
            new Dictionary<Mesh, GameObject>();

        private static readonly Dictionary<RoomEntry, SurfaceMaterials> SurfaceMaterialSets =
            new Dictionary<RoomEntry, SurfaceMaterials>();

        private static readonly List<WeakReference> TrackedRoomVisuals = new List<WeakReference>();
        private static readonly List<WeakReference> CorridorVisuals = new List<WeakReference>();

        private static readonly FieldInfo RoomWallWallsField =
            AccessTools.Field(typeof(RoomWallDefinition), "WallsDefinition");

        private static readonly FieldInfo FloorPlanField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_floorPlan");
        private static readonly FieldInfo FloorRenderersField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_floorTileRenderers");
        private static readonly FieldInfo WallObjectsField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_wallObjects");
        private static readonly FieldInfo FloorOverrideField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_floorVisualOverride");
        private static readonly FieldInfo WallOverrideField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_wallVisualOverride");
        private static readonly FieldInfo CorridorWallsField =
            AccessTools.Field(typeof(CorridorWallsVisual), "_activeWalls");

        private static readonly Type CorridorWallVisualType =
            typeof(CorridorWallsVisual).GetNestedType("WallVisual", BindingFlags.NonPublic);
        private static readonly FieldInfo CorridorWallTransformField =
            CorridorWallVisualType?.GetField("Transform",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo CorridorWallRoomField =
            CorridorWallVisualType?.GetField("Room",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    // CorridorWallsVisual sustituye _wallsExterior por la definicion global del
    // pasillo cuando existe ese reemplazo. Por eso cambiar el estilo exterior
    // de RoomCatalog podria no tener efecto visible.
    // Las salas con estilo exterior explicito deben evitar ese reemplazo global.
    [HarmonyPatch(typeof(CorridorWallsVisual), "GetRoomExteriorWallDefinition")]
    internal static class RoomCatalogCorridorDefinitionPatch
    {
        private static bool Prefix(Room __0, ref RoomWallDefinition __result)
        {
            if (!RoomCatalog.TryGetConfiguredExteriorWallDefinition(__0, out var wallDefinition))
                return true;

            __result = wallDefinition;
            return false;
        }
    }

    // RoomWallDefinition.GetPiece can globally force the game's default wall
    // prefabs. Bypass that switch for every configured RoomCatalog exterior piece.
    [HarmonyPatch(typeof(RoomWallDefinition), "GetPiece")]
    internal static class RoomCatalogExteriorPiecePatch
    {
        private static bool Prefix(
            RoomWallDefinition __instance,
            RoomWallDefinition.Type __0,
            ref GameObject __result)
        {
            if (!RoomCatalog.TryGetDirectExteriorPiece(__instance, __0, out var piece))
                return true;

            __result = piece;
            return false;
        }
    }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), "UpdateFromRoom")]
    internal static class RoomCatalogSurfaceUpdatePatch
    {
        private static void Prefix(RoomFloorPlanVisual __instance, FloorPlan __0)
        {
            RoomCatalog.PrepareVisual(__instance, __0);
        }

        private static void Postfix(RoomFloorPlanVisual __instance)
        {
            RoomCatalog.ApplyVisual(__instance);
        }
    }

    [HarmonyPatch(typeof(RoomFloorPlanVisual), "RestoreFromSave")]
    internal static class RoomCatalogSurfaceRestorePatch
    {
        private static void Prefix(RoomFloorPlanVisual __instance)
        {
            RoomCatalog.PrepareRestoredVisual(__instance);
        }

        private static void Postfix(RoomFloorPlanVisual __instance)
        {
            RoomCatalog.ApplyVisual(__instance);
        }
    }

    [HarmonyPatch(typeof(CorridorWallsVisual), "CreateWallObjects")]
    internal static class RoomCatalogCorridorSurfacePatch
    {
        private static void Postfix(CorridorWallsVisual __instance)
        {
            RoomCatalog.ApplyVisual(__instance);
        }
    }
}
