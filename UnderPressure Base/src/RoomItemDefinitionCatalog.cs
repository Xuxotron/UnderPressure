using System;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;
using static UnderPressure.RoomItemDefinitionAccess;

namespace UnderPressure
{
    internal static class RoomItemDefinitionAccess
    {
        internal static void Set(RoomItemDefinition item, string fieldName, object value) { var field = AccessTools.Field(typeof(RoomItemDefinition), fieldName); if (field == null) throw new MissingFieldException(typeof(RoomItemDefinition).FullName, fieldName); field.SetValue(item, value); }
    }

    public static class RoomItemDefinitionCatalog
    {
        public static bool ApplyNativeEnergyCost(RoomItemDefinition item)
        {
            var prefab = item?.GetPrefab(0)?.name;
            switch (prefab)
            {
                case ReceptionNativeParameters.Prefab: ReceptionNativeParameters.Apply(item); return true;
                case WardNurseStationNativeParameters.Prefab: WardNurseStationNativeParameters.Apply(item); return true;
                case OfficeDeskNativeParameters.Prefab: OfficeDeskNativeParameters.Apply(item); return true;
                case VendingMachineDrinksNativeParameters.Prefab: VendingMachineDrinksNativeParameters.Apply(item); return true;
                case VendingMachineEnergyDrinksNativeParameters.Prefab: VendingMachineEnergyDrinksNativeParameters.Apply(item); return true;
                case VendingMachineLuxuryDrinksNativeParameters.Prefab: VendingMachineLuxuryDrinksNativeParameters.Apply(item); return true;
                case VendingMachineGambleDrinksNativeParameters.Prefab: VendingMachineGambleDrinksNativeParameters.Apply(item); return true;
                case VendingMachineLaxativeDrinksNativeParameters.Prefab: VendingMachineLaxativeDrinksNativeParameters.Apply(item); return true;
                case VendingMachineSnacksNativeParameters.Prefab: VendingMachineSnacksNativeParameters.Apply(item); return true;
                case VendingMachineSaltySnacksNativeParameters.Prefab: VendingMachineSaltySnacksNativeParameters.Apply(item); return true;
                case VendingMachineLuxurySnacksNativeParameters.Prefab: VendingMachineLuxurySnacksNativeParameters.Apply(item); return true;
                case VendingMachineSpongeSnacksNativeParameters.Prefab: VendingMachineSpongeSnacksNativeParameters.Apply(item); return true;
                case VendingMachineToySnacksNativeParameters.Prefab: VendingMachineToySnacksNativeParameters.Apply(item); return true;
                case VendingMachineCaviarSnackNativeParameters.Prefab: VendingMachineCaviarSnackNativeParameters.Apply(item); return true;
                case VendingMachineLemonNativeParameters.Prefab: VendingMachineLemonNativeParameters.Apply(item); return true;
                case VendingMachineTomatoesNativeParameters.Prefab: VendingMachineTomatoesNativeParameters.Apply(item); return true;
                case VendingMachineCarrotNativeParameters.Prefab: VendingMachineCarrotNativeParameters.Apply(item); return true;
                case VendingMachineWaterNativeParameters.Prefab: VendingMachineWaterNativeParameters.Apply(item); return true;
                case VendingMachineRetroKebabNativeParameters.Prefab: VendingMachineRetroKebabNativeParameters.Apply(item); return true;
                case ToiletHandDryerNativeParameters.Prefab: ToiletHandDryerNativeParameters.Apply(item); return true;
                case ToiletHandDryerGoldNativeParameters.Prefab: ToiletHandDryerGoldNativeParameters.Apply(item); return true;
                case DiagnosisMachineNativeParameters.Prefab: DiagnosisMachineNativeParameters.Apply(item); return true;
                case LightHeadedMachineNativeParameters.Prefab: LightHeadedMachineNativeParameters.Apply(item); return true;
                case DrugDispenserNativeParameters.Prefab: DrugDispenserNativeParameters.Apply(item); return true;
                case MachineControlConsoleNativeParameters.Prefab: MachineControlConsoleNativeParameters.Apply(item); return true;
                default: return false;
            }
        }
    }

    // =================
    // PARÁMETROS NATIVOS
    // =================

    /* CUADRO ELÉCTRICO */ public static class ElectricalPanelNativeParameters {
        public const string ElectricalPanelDebugTag = "under pressure electrical panel"; // Identificador interno legible.
        public const int ElectricalPanelSharedId = 9112111; // Identificador del contenedor SharedInstance.
        public static readonly Guid ElectricalPanelGuid = // Identificador único y persistente de la definición.
            new Guid("5c382c42-4eb7-4c8a-8aca-a5f902481102");

        public sealed class ElectricalPanelRuntimeContext
        {
            public LocalisedString Name;
            public LocalisedString Description;
            public GameObject Prefab;
            public Sprite Icon;
            public InteractionDefinition MaintenanceInteraction;
            public InteractionAttributeModifier[] MaintenanceAttributeModifiers;
        }

        public static RoomItemDefinition CreateElectricalPanel(ElectricalPanelRuntimeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Prefab == null) throw new ArgumentNullException(nameof(context.Prefab));
            if (context.Icon == null) throw new ArgumentNullException(nameof(context.Icon));
            if (context.MaintenanceInteraction == null)
                throw new ArgumentNullException(nameof(context.MaintenanceInteraction));

            var item = new RoomItemDefinition();

            // IDENTIDAD Y TEXTOS
            Set(item, "_guid", ElectricalPanelGuid); // GUID único y persistente; no puede repetirse entre definiciones.
            Set(item, "_type", RoomItemDefinition.Type.Default); // Tipo general: Default, Door, Window, DEPRECATED_WholeWallDoor, ServingHatch, Research, Landscape, Machine, Special, OtherSingleItems, SideDoor, PlotObject, Ambulance.
            Set(item, "_localisedName", context.Name); // Nombre mostrado por el juego; su tipo es LocalisedString.
            Set(item, "_localisedDescription", context.Description); // Descripción principal del catálogo y los menús.
            Set(item, "_functionalDescription", context.Description); // Texto que explica la función o el efecto del objeto.
            Set(item, "_debugTag", ElectricalPanelDebugTag); // Nombre interno legible utilizado para identificar y depurar la definición.
            Set(item, "_itemDeprecated", false); // True marca la definición como obsoleta.

            // ICONOS
            Set(item, "_icon", context.Icon); // Sprite principal del catálogo.
            Set(item, "_iconWithoutBacking", context.Icon); // Variante del icono sin el fondo del catálogo.
            Set(item, "_jobAssignmentIcon", null); // Sprite empleado en la asignación o representación de trabajos.

            // PRECIO, DISPONIBILIDAD Y PRESTIGIO
            Set(item, "_cost", 200); // Precio de compra en dinero normal.
            Set(item, "_canBeSold", true); // Permite venderlo.
            Set(item, "_initiallyAvailable", true); // Disponible sin recibir previamente un desbloqueo.
            Set(item, "_mustBeWhiteListed", false); // Exige que otro sistema lo incluya en la lista de objetos permitidos.
            Set(item, "_saveInRoomLayout", true); // Permite conservarlo dentro de plantillas o diseños de sala.
            Set(item, "_silverCost", 0); // Precio alternativo en Kudosh.
            Set(item, "_lockedFreeItem", false); // Marca un objeto gratuito que permanece bloqueado hasta obtenerlo.
            Set(item, "_unlockedMessage", default(LocalisedString)); // Mensaje localizado mostrado al desbloquearlo.
            Set(item, "_prestige", 0f); // Prestigio aportado a la sala.
            Set(item, "_hospitalLevelPoints", 0f); // Puntos aportados al nivel general del hospital.

            // TAMAÑO Y COLOCACIÓN
            Set(item, "_size", RoomItemDefinition.Size.Medium); // Clasificación de tamaño: Small, Medium, Large.
            Set(item, "_playPlacmentSFX", true); // Reproduce el sonido nativo de colocación.
            Set(item, "_placeOnWall", true); // Activa la colocación ajustada a paredes.
            Set(item, "_occupyWallOnly", false); // Restringe su posición al espacio propio de la pared.
            Set(item, "_allowOnCorner", true); // Permite colocarlo junto a esquinas.
            Set(item, "_gridSnap", 0.25f); // Incremento de posición sobre la cuadrícula.
            Set(item, "_rotationSnap", 90f); // Incremento de rotación en grados.
            Set(item, "_defaultRotation", 0f); // Rotación inicial en grados.
            Set(item, "_wallMagnetism", false); // Activa la atracción automática hacia paredes cercanas.
            Set(item, "_wallMagnetismRotation", 0f); // Rotación aplicada por el magnetismo de pared.
            Set(item, "_wallMagnetismDistance", 2f); // Distancia máxima de actuación del magnetismo de pared.
            Set(item, "_singlePlace", false); // True limita su colocación a una única unidad.

            // COLISIÓN, NAVEGACIÓN Y SELECCIÓN
            Set(item, "_hasCollision", true); // Activa las comprobaciones de colisión.
            Set(item, "_useVerticalCollision", false); // Incluye la dimensión vertical en la colisión especial.
            Set(item, "_collideWithSameType", false); // Hace que colisione expresamente con objetos del mismo tipo.
            Set(item, "_collideWithRugs", false); // Hace que las alfombras cuenten como colisión.
            Set(item, "_collisionType", RoomItemDefinition.CollisionType.Default); // Tipo de colisión: Default, Rug.
            Set(item, "_moveOutOfWay", false); // Activa el comportamiento nativo que permite apartarlo.
            Set(item, "_ignoreValidation", false); // True omite las reglas normales de validación de colocación.
            Set(item, "_isSelectable", true); // Permite seleccionarlo con el cursor.
            Set(item, "_hasTooltip", true); // Permite mostrar información al pasar el cursor.
            Set(item, "_showQueuePositions", false); // Muestra las posiciones de cola de sus interacciones.
            Set(item, "_showStatusIcon", true); // Permite mostrar iconos de estado sobre el objeto.
            Set(item, "_affectsNavigation", false); // Hace que sus límites modifiquen la navegación de personajes.
            Set(item, "_removeWalls", false); // Elimina los segmentos de pared afectados por su colocación.
            Set(item, "_disableParticlesOnEdit", false); // Desactiva sus partículas mientras se edita o mueve.

            // PREFABS Y RESTRICCIONES DE SALA
            Set(item, "_prefab", context.Prefab); // GameObject utilizado como representación construida.
            Set(item, "_blueprintPrefab", context.Prefab); // GameObject mostrado durante la colocación o construcción.
            Set(item, "_canBePlacedIn", Array.Empty<RoomDefinition.Type>()); // Tipos de sala permitidos; una lista vacía no aplica una lista positiva.
            Set(item, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>()); // Tipos de sala prohibidos.
            // RoomDefinition.Type: Invalid, Hospital, GPOffice, Pharmacy, XRay, GeneralDiagnosis, Cardiography,
            // MRIScanner, Cafe, Ward, StaffRoom, Toilets, Training, Research, Psychiatry, OperatingTheater,
            // ClinicCubism, FluidAnalysis, DNAAnalysis, InjectionRoom, Marketing, Chromatherapy, LightHeaded,
            // MummyClinic, ElectricShockClinic, ClownClinic, PandemicClinic, AnimalMagnetismClinic,
            // TurtleHeadClinic, ClinicVI10, FractureWard, Reception, HospitalUnbuilt, EightBitClinic,
            // FrankensteinClinic, DogClinic, RobotMonsterClinic, BlankLooksClinic, EightBallClinic,
            // ExplorerClinic, CardboardClinic, FrogClinic, AstroClinic, PinocchioClinic, ScarecrowClinic,
            // TechClinic, PlantWardClinic, StuntmanClinic, MudPersonClinic, ToySoldierClinic, TimeTunnel,
            // SnowballedClinic, HivesClinic, UnderTheWeatherClinic, AmbulanceBay, NoDataRoom.

            // ATRIBUTOS Y MANTENIMIENTO
            Set(item, "_attributes", new[] // Atributos creados en cada instancia.
            {
                new ObjectAttributes.Definition
                {
                    _type = ObjectAttributes.Type.Maintenance, // Tipo de atributo: None=-1, Maintenance=0.
                    _initialValue = 0f // Valor inicial del atributo.
                }
            });
            Set(item, "_maintenanceModifer", 0f); // Variación nativa continua de Maintenance por unidad de tiempo; positivo deteriora y negativo repara.
            Set(item, "_maintenanceFunctionalLevel", 100f); // Nivel de deterioro a partir del cual RoomItem.IsFunctional devuelve false.
            Set(item, "_maintenanceIconOverride", null); // Sprite que sustituye al icono de la categoría de mantenimiento.
            Set(item, "_janitorPriority", 10f); // Prioridad relativa utilizada para ordenar trabajos de conserje.
            Set(item, "_janitorRepairRate", 1f); // Velocidad base con la que el conserje reduce Maintenance al reparar.
            Set(item, "_ignoredByJanitors", false); // True impide que el planificador cree trabajos automáticos de conserje.
            Set(item, "_maintenanceDescription", JobMaintenance.JobDescription.BrokenMachine); // Categoría: None, BrokenMachine, BlockedToilet, OutOfStock, WiltedPlant, Litter, MedicalWaste, Ghost, Vehicular, Max.
            Set(item, "_serviceDescription", JobService.JobDescription.None); // Categoría de servicio: None, ReceptionCheckIn, KioskCustomer.

            // EFECTOS, VISTAS Y MENÚS
            Set(item, "_roomModifiers", Array.Empty<RoomModifier>()); // Efectos aplicados al mapa o a la sala, como atractivo o temperatura.
            Set(item, "_interactionAttributeModifiers", // Cambios de atributos o finanzas aplicados por una interacción.
                context.MaintenanceAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>());
            // InteractionAttributeModifier.Type: Use, Maintain, Serve, Special, Upgrade.
            Set(item, "_dataViewMode", DataViewManager.Mode.None); // Vista asociada: None, HospitalTemperature, HospitalAttractiveness, HospitalHygiene, CharacterHunger, CharacterThirst, CharacterToilet, CharacterBoredom, CharacterLitter, CharacterHappiness, PatientHealth, PatientHappiness, StaffEnergy, StaffXp, StaffType, StaffQualifications, StaffHappiness, ObjectMaintenance.
            Set(item, "_hoverMenuPrefab", null); // Prefab del menú mostrado al pasar el cursor.
            Set(item, "_selectMenuPrefab", null); // Prefab del menú mostrado al seleccionar el objeto.

            // REQUISITOS, MEJORAS, INTERACCIONES Y COMPONENTES
            Set(item, "_dlcPackRequired", null); // DLC necesario; null significa ninguno.
            Set(item, "_collaborativeResearchRequired", null); // Proyecto colaborativo necesario; null significa ninguno.
            Set(item, "_superBugVictoryRequired", null); // Objetivo Superbug necesario; null significa ninguno.
            Set(item, "_upgrades", Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>()); // Definiciones de mejora disponibles.
            Set(item, "_singleInteractor", true); // True limita el uso simultáneo a un personaje.
            Set(item, "_interactionsAlwayAnimate", false); // True fuerza la reproducción de animaciones de interacción.
            Set(item, "_minValidInteractions", 1); // Número mínimo de interacciones que deben resultar válidas.
            Set(item, "_interactions", new[] { context.MaintenanceInteraction }); // Interacciones ofrecidas; tipos: Use, Maintain, Serve, Special, Upgrade.
            Set(item, "_filters", Array.Empty<RoomItemFilter>()); // Filtros de personajes o condiciones válidas para las interacciones.
            Set(item, "_placementEffect", null); // GameObject instanciado como efecto al colocarlo.
            Set(item, "_spawnLimitCategory", null); // Categoría compartida que limita el número de unidades.
            Set(item, "_minimumQueuePositionAllowedToSatisyNeed", 0); // Posición mínima de cola desde la que puede satisfacerse una necesidad.
            Set(item, "_components", Array.Empty<EntityComponent>()); // Componentes adicionales: fuego, materiales, stock u otros comportamientos.

            // MANIPULACIÓN, ELECTRICIDAD NATIVA Y AMBULANCIAS
            Set(item, "_canBePickedUp", true); // Permite recogerlo y recolocarlo.
            Set(item, "_canDragHoldSelect", true); // Permite selección mantenida y arrastre.
            Set(item, "_energyCost", 0); // Coste energético mensual nativo añadido a la factura al instalarlo.
            Set(item, "_generatesElectricity", false); // Marca utilizada por el sistema eléctrico nativo del juego.
            Set(item, "_ecoRatingModifier", 0f); // Modificación aplicada a la valoración ecológica.
            Set(item, "_ambulanceConfig", null); // Configuración de ambulancia; null para objetos normales.
            Set(item, "_fixedWallPlacement", RoomItemDefinition.FixedWallPlacementOption.None); // Colocación fija de pared: None, AmbulanceBayEntrance.
            Set(item, "_primeEntitlementRequired", 0); // Identificador de recompensa promocional necesaria; cero significa ninguna.
            Set(item, "_isAnAmbulance", false); // Identifica expresamente la definición como ambulancia.

            return item;
        }
    }

    /* RECEPCIÓN */ public static class ReceptionNativeParameters {
        public const string Prefab = "RI_Reception";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 3); }
    }

    /* PUESTO DE ENFERMERÍA */ public static class WardNurseStationNativeParameters {
        public const string Prefab = "RI_Ward_Nurse_Station";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 3); }
    }

    /* ESCRITORIO */ public static class OfficeDeskNativeParameters {
        public const string Prefab = "RI_OfficeDesk";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 3); }
    }

    /* EXPENDEDORA DE BEBIDAS */ public static class VendingMachineDrinksNativeParameters {
        public const string Prefab = "RI_VendingMachine_Drinks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE BEBIDAS ENERGÉTICAS */ public static class VendingMachineEnergyDrinksNativeParameters {
        public const string Prefab = "RI_VendingMachine_EnergyDrinks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE BEBIDAS DE LUJO */ public static class VendingMachineLuxuryDrinksNativeParameters {
        public const string Prefab = "RI_VendingMachine_LuxuryDrinks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE BEBIDAS DE APUESTAS */ public static class VendingMachineGambleDrinksNativeParameters {
        public const string Prefab = "RI_VendingMachine_GambleDrinks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE BEBIDAS LAXANTES */ public static class VendingMachineLaxativeDrinksNativeParameters {
        public const string Prefab = "RI_VendingMachine_Drinks_Laxative";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE APERITIVOS */ public static class VendingMachineSnacksNativeParameters {
        public const string Prefab = "RI_VendingMachine_Snacks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE APERITIVOS SALADOS */ public static class VendingMachineSaltySnacksNativeParameters {
        public const string Prefab = "RI_VendingMachine_SaltySnacks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE APERITIVOS DE LUJO */ public static class VendingMachineLuxurySnacksNativeParameters {
        public const string Prefab = "RI_VendingMachine_LuxurySnacks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE APERITIVOS ESPONJA */ public static class VendingMachineSpongeSnacksNativeParameters {
        public const string Prefab = "RI_VendingMachine_SpongeSnacks";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE APERITIVOS DE JUGUETE */ public static class VendingMachineToySnacksNativeParameters {
        public const string Prefab = "RI_VendingMachine_Snacks_Toy";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE CAVIAR */ public static class VendingMachineCaviarSnackNativeParameters {
        public const string Prefab = "RI_VendingMachine_CaviarSnack";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE LIMONES */ public static class VendingMachineLemonNativeParameters {
        public const string Prefab = "RI_Grid_Vending_Lemon_V1";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE TOMATES */ public static class VendingMachineTomatoesNativeParameters {
        public const string Prefab = "RI_Grid_Vending_Tomatoes_V1";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE ZANAHORIAS */ public static class VendingMachineCarrotNativeParameters {
        public const string Prefab = "RI_Grid_Vending_Carrot_V1";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE AGUA */ public static class VendingMachineWaterNativeParameters {
        public const string Prefab = "RI_Grid_Vending_Water_V1";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* EXPENDEDORA DE KEBAB RETRO */ public static class VendingMachineRetroKebabNativeParameters {
        public const string Prefab = "RI_IP_Retro_Kebab_Vending_Machine_V1";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 2); }
    }

    /* SECAMANOS */ public static class ToiletHandDryerNativeParameters {
        public const string Prefab = "RI_ToiletHandDryer";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 5); }
    }

    /* SECAMANOS DORADO */ public static class ToiletHandDryerGoldNativeParameters {
        public const string Prefab = "RI_ToiletHandDryer_Gold";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 5); }
    }

    /* MÁQUINA DE DIAGNÓSTICO */ public static class DiagnosisMachineNativeParameters {
        public const string Prefab = "RI_DiagnosisMachine";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 150); }
    }

    /* MÁQUINA DE CABEZAS HUECAS */ public static class LightHeadedMachineNativeParameters {
        public const string Prefab = "RI_Machine_LightHeaded";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 350); }
    }

    /* DISPENSADOR DE MEDICAMENTOS */ public static class DrugDispenserNativeParameters {
        public const string Prefab = "RI_DrugDispenser";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 200); }
    }

    /* CONSOLA DE CONTROL */ public static class MachineControlConsoleNativeParameters {
        public const string Prefab = "RI_Machine_Control_Console";
        public static void Apply(RoomItemDefinition item) { Set(item, "_energyCost", 10); }
    }

    // =================
    // PARÁMETROS NUEVOS
    // =================

    /* CUADRO ELÉCTRICO */ public static class ElectricalPanelNewParameters {
    }

    // =================
    // PARÁMETROS PREFABS
    // =================

    /* CUADRO ELÉCTRICO */ public static class ElectricalPanelPrefabParameters {
        public const string PrefabAsset = "assets/powerpanel/powerpanel.prefab"; // Prefab cargado desde el AssetBundle.
        public const string ModelSourceAsset = "Assets/PowerPanel/powerpanel.FBX"; // Modelo fuente del proyecto de Unity.
        public const string MaterialAsset = "Assets/PowerPanel/Material #25.mat"; // Material cargado desde el AssetBundle.
        public const string TextureAsset = "Assets/PowerPanel/powerpanel tex.jpg"; // Textura principal del modelo.
        public const string IconTextureAsset = "Assets/PowerPanel/powerpanel icon.png"; // Textura utilizada para crear el icono.
        public const string BaseMesh = "CuadroEnergyBase"; // Malla de la carcasa.
        public const string DoorMesh = "CuadroEnergyDoor"; // Malla de la puerta.
        public const string MaterialName = "Material #25"; // Nombre del material asignado a las mallas.
        public const string MaintenanceSocket = "NURSE_LOCKER_BASE_RIG:START_FRONT"; // Punto de interacción del conserje.
    }

    // =================
    // PARÁMETROS ESPECIALES
    // =================

    /* CUADRO ELÉCTRICO */ public static class ElectricalPanelSpecialParameters {
        public const int MaximumConnectedTiles = 15; // Máximo de tiles de baja tensión conectados.
        public const int MaximumLoad = 20; // Carga máxima soportada.
        public const float WearPerConnectedTile = 0.01f; // Desgaste por segundo aportado por cada tile conectado.
    }
}
