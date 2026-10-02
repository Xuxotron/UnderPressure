using System;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    public static class RoomItemDefinitionCatalog
    {
        // ============================================================================
        // PLANTILLA GENÉRICA TEST — CAMPOS NATIVOS DE ROOMITEMDEFINITION
        // ============================================================================
        // Todo este bloque está comentado: documenta el juego original y no se compila.
        // No describe ningún objeto concreto ni contiene reglas propias de UnderPressure.
        //
        // var item = new RoomItemDefinition();
        //
        // IDENTIDAD Y TEXTOS
        // Set(item, "_guid", new Guid("00000000-0000-0000-0000-000000000000"));
        //     GUID único y persistente. No puede repetirse entre definiciones.
        // Set(item, "_type", RoomItemDefinition.Type.Default);
        //     Tipo general. Opciones: Default=0, Door=1, Window=2,
        //     DEPRECATED_WholeWallDoor=3, ServingHatch=4, Research=5, Landscape=6,
        //     Machine=7, Special=8, OtherSingleItems=9, SideDoor=10, PlotObject=11,
        //     Ambulance=12.
        // Set(item, "_localisedName", nombreLocalizado);
        //     Nombre mostrado por el juego; su tipo es LocalisedString.
        // Set(item, "_localisedDescription", descripcionLocalizada);
        //     Descripción principal del catálogo y los menús.
        // Set(item, "_functionalDescription", descripcionFuncionalLocalizada);
        //     Texto que explica la función o el efecto del objeto.
        // Set(item, "_debugTag", "TEST");
        //     Nombre interno legible utilizado para identificar y depurar la definición.
        // Set(item, "_itemDeprecated", false);
        //     True marca la definición como obsoleta.
        //
        // ICONOS
        // Set(item, "_icon", icono);
        //     Sprite principal del catálogo.
        // Set(item, "_iconWithoutBacking", iconoSinFondo);
        //     Variante del icono sin el fondo del catálogo.
        // Set(item, "_jobAssignmentIcon", iconoDeTrabajo);
        //     Sprite empleado en la asignación o representación de trabajos.
        //
        // PRECIO, DISPONIBILIDAD Y PRESTIGIO
        // Set(item, "_cost", 0);
        //     Precio de compra en dinero normal.
        // Set(item, "_energyCost", 0);
        //     Coste energético mensual nativo añadido a la factura al instalarlo.
        // Set(item, "_initiallyAvailable", true);
        //     Disponible sin recibir previamente un desbloqueo.
        // Set(item, "_mustBeWhiteListed", false);
        //     Exige que otro sistema lo incluya en la lista de objetos permitidos.
        // Set(item, "_saveInRoomLayout", true);
        //     Permite conservarlo dentro de plantillas o diseños de sala.
        // Set(item, "_silverCost", 0);
        //     Precio alternativo en Kudosh.
        // Set(item, "_lockedFreeItem", false);
        //     Marca un objeto gratuito que permanece bloqueado hasta obtenerlo.
        // Set(item, "_unlockedMessage", default(LocalisedString));
        //     Mensaje localizado mostrado al desbloquearlo.
        // Set(item, "_prestige", 0f);
        //     Prestigio aportado a la sala.
        // Set(item, "_hospitalLevelPoints", 0f);
        //     Puntos aportados al nivel general del hospital.
        //
        // TAMAÑO Y COLOCACIÓN
        // Set(item, "_size", RoomItemDefinition.Size.Small);
        //     Clasificación de tamaño. Opciones: Small=0, Medium=1, Large=2.
        // Set(item, "_playPlacmentSFX", true);
        //     Reproduce el sonido nativo de colocación.
        // Set(item, "_placeOnWall", false);
        //     Activa la colocación ajustada a paredes.
        // Set(item, "_occupyWallOnly", false);
        //     Restringe su posición al espacio propio de la pared.
        // Set(item, "_allowOnCorner", true);
        //     Permite colocarlo junto a esquinas.
        // Set(item, "_gridSnap", 1f);
        //     Incremento de posición sobre la cuadrícula.
        // Set(item, "_rotationSnap", 90f);
        //     Incremento de rotación en grados.
        // Set(item, "_defaultRotation", 0f);
        //     Rotación inicial en grados.
        // Set(item, "_wallMagnetism", false);
        //     Activa la atracción automática hacia paredes cercanas.
        // Set(item, "_wallMagnetismRotation", 0f);
        //     Rotación aplicada por el magnetismo de pared.
        // Set(item, "_wallMagnetismDistance", 2f);
        //     Distancia máxima de actuación del magnetismo de pared.
        // Set(item, "_singlePlace", false);
        //     True limita su colocación a una única unidad.
        //
        // COLISIÓN, NAVEGACIÓN Y SELECCIÓN
        // Set(item, "_hasCollision", true);
        //     Activa las comprobaciones de colisión.
        // Set(item, "_useVerticalCollision", false);
        //     Incluye la dimensión vertical en la colisión especial.
        // Set(item, "_collideWithSameType", false);
        //     Hace que colisione expresamente con objetos del mismo tipo.
        // Set(item, "_collideWithRugs", false);
        //     Hace que las alfombras cuenten como colisión.
        // Set(item, "_collisionType", RoomItemDefinition.CollisionType.Default);
        //     Tipo de colisión. Opciones: Default=0, Rug=1.
        // Set(item, "_moveOutOfWay", false);
        //     Activa el comportamiento nativo que permite apartarlo.
        // Set(item, "_ignoreValidation", false);
        //     True omite las reglas normales de validación de colocación.
        // Set(item, "_isSelectable", true);
        //     Permite seleccionarlo con el cursor.
        // Set(item, "_hasTooltip", true);
        //     Permite mostrar información al pasar el cursor.
        // Set(item, "_showQueuePositions", false);
        //     Muestra las posiciones de cola de sus interacciones.
        // Set(item, "_showStatusIcon", true);
        //     Permite mostrar iconos de estado sobre el objeto.
        // Set(item, "_affectsNavigation", true);
        //     Hace que sus límites modifiquen la navegación de personajes.
        // Set(item, "_removeWalls", false);
        //     Elimina los segmentos de pared afectados por su colocación.
        // Set(item, "_disableParticlesOnEdit", false);
        //     Desactiva sus partículas mientras se edita o mueve.
        //
        // PREFABS Y RESTRICCIONES DE SALA
        // Set(item, "_prefab", prefab);
        //     GameObject utilizado como representación construida.
        // Set(item, "_blueprintPrefab", prefabDePlano);
        //     GameObject mostrado durante la colocación o construcción.
        // Set(item, "_canBePlacedIn", Array.Empty<RoomDefinition.Type>());
        //     Tipos de sala permitidos. Una lista vacía no aplica una lista positiva.
        // Set(item, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>());
        //     Tipos de sala prohibidos.
        //     Opciones completas de RoomDefinition.Type para ambas listas:
        //     Invalid=-1, Hospital=0, GPOffice=1, Pharmacy=2, XRay=3,
        //     GeneralDiagnosis=4, Cardiography=5, MRIScanner=6, Cafe=7, Ward=8,
        //     StaffRoom=9, Toilets=10, Training=11, Research=12, Psychiatry=13,
        //     OperatingTheater=14, ClinicCubism=15, FluidAnalysis=16, DNAAnalysis=17,
        //     InjectionRoom=18, Marketing=19, Chromatherapy=20, LightHeaded=21,
        //     MummyClinic=22, ElectricShockClinic=23, ClownClinic=24, PandemicClinic=25,
        //     AnimalMagnetismClinic=26, TurtleHeadClinic=27, ClinicVI10=28,
        //     FractureWard=29, Reception=30, HospitalUnbuilt=31, EightBitClinic=32,
        //     FrankensteinClinic=33, DogClinic=34, RobotMonsterClinic=35,
        //     BlankLooksClinic=36, EightBallClinic=37, ExplorerClinic=38,
        //     CardboardClinic=39, FrogClinic=40, AstroClinic=41, PinocchioClinic=42,
        //     ScarecrowClinic=43, TechClinic=44, PlantWardClinic=45, StuntmanClinic=46,
        //     MudPersonClinic=47, ToySoldierClinic=48, TimeTunnel=49,
        //     SnowballedClinic=50, HivesClinic=51, UnderTheWeatherClinic=52,
        //     AmbulanceBay=53, NoDataRoom=54.
        //
        // ATRIBUTOS Y MANTENIMIENTO
        // Set(item, "_attributes", Array.Empty<ObjectAttributes.Definition>());
        //     Atributos creados en cada instancia. Opciones completas de ObjectAttributes.Type:
        //     None = -1: no representa un atributo válido.
        //     Maintenance = 0: deterioro del objeto, normalmente limitado entre 0 y 100.
        //     Cada ObjectAttributes.Definition contiene también _initialValue, su valor inicial.
        // Set(item, "_maintenanceModifer", 0f);
        //     Variación NATIVA continua de Maintenance por unidad de tiempo de simulación.
        //     Positivo aumenta el deterioro, cero no lo altera y negativo lo reduce.
        // Set(item, "_maintenanceFunctionalLevel", 100f);
        //     Nivel de deterioro a partir del cual RoomItem.IsFunctional devuelve false.
        // Set(item, "_maintenanceIconOverride", iconoDeMantenimiento);
        //     Sprite que sustituye al icono de la categoría de mantenimiento.
        // Set(item, "_janitorPriority", 1f);
        //     Prioridad relativa utilizada para ordenar trabajos de conserje.
        // Set(item, "_janitorRepairRate", 10f);
        //     Velocidad base con la que el conserje reduce Maintenance al reparar.
        // Set(item, "_ignoredByJanitors", false);
        //     True impide que el planificador cree trabajos automáticos de conserje.
        // Set(item, "_maintenanceDescription", JobMaintenance.JobDescription.None);
        //     Categoría de mantenimiento. Opciones completas: None=0, BrokenMachine=1,
        //     BlockedToilet=2, OutOfStock=3, WiltedPlant=4, Litter=5, MedicalWaste=6,
        //     Ghost=7, Vehicular=8, Max=9.
        // Set(item, "_serviceDescription", JobService.JobDescription.None);
        //     Categoría de servicio. Opciones completas: None=0, ReceptionCheckIn=1,
        //     KioskCustomer=2.
        //
        // EFECTOS, VISTAS Y MENÚS
        // Set(item, "_roomModifiers", Array.Empty<RoomModifier>());
        //     Efectos aplicados al mapa o a la sala, como atractivo o temperatura.
        // Set(item, "_interactionAttributeModifiers", Array.Empty<InteractionAttributeModifier>());
        //     Cambios de atributos o finanzas aplicados por una interacción.
        //     Opciones de InteractionAttributeModifier.Type: Use=0, Maintain=1, Serve=2,
        //     Special=3, Upgrade=4.
        // Set(item, "_dataViewMode", DataViewManager.Mode.None);
        //     Vista de datos asociada. Opciones completas: None=0,
        //     HospitalTemperature=100, HospitalAttractiveness=101, HospitalHygiene=102,
        //     CharacterHunger=200, CharacterThirst=201, CharacterToilet=202,
        //     CharacterBoredom=203, CharacterLitter=204, CharacterHappiness=205,
        //     PatientHealth=300, PatientHappiness=301, StaffEnergy=400, StaffXp=401,
        //     StaffType=402, StaffQualifications=403, StaffHappiness=404,
        //     ObjectMaintenance=500.
        // Set(item, "_hoverMenuPrefab", menuAlPasar);
        //     Prefab del menú mostrado al pasar el cursor.
        // Set(item, "_selectMenuPrefab", menuAlSeleccionar);
        //     Prefab del menú mostrado al seleccionar el objeto.
        //
        // REQUISITOS, MEJORAS, INTERACCIONES Y COMPONENTES
        // Set(item, "_dlcPackRequired", dlcRequerido);
        //     DLC necesario; null significa ninguno.
        // Set(item, "_collaborativeResearchRequired", investigacionRequerida);
        //     Proyecto colaborativo necesario; null significa ninguno.
        // Set(item, "_superBugVictoryRequired", superbugRequerido);
        //     Objetivo Superbug necesario; null significa ninguno.
        // Set(item, "_upgrades", Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>());
        //     Definiciones de mejora disponibles.
        // Set(item, "_singleInteractor", false);
        //     True limita el uso simultáneo a un personaje.
        // Set(item, "_interactionsAlwayAnimate", false);
        //     True fuerza la reproducción de animaciones de interacción.
        // Set(item, "_minValidInteractions", 0);
        //     Número mínimo de interacciones que deben resultar válidas.
        // Set(item, "_interactions", Array.Empty<InteractionDefinition>());
        //     Interacciones ofrecidas. Tipos: Use=0, Maintain=1, Serve=2, Special=3,
        //     Upgrade=4. Opciones de InteractionDefinition.Socket: None=0, LeftHand=1,
        //     RightHand=2.
        // Set(item, "_filters", Array.Empty<RoomItemFilter>());
        //     Filtros de personajes o condiciones válidas para las interacciones.
        // Set(item, "_placementEffect", efectoDeColocacion);
        //     GameObject instanciado como efecto al colocarlo.
        // Set(item, "_spawnLimitCategory", categoriaDeLimite);
        //     Categoría compartida que limita el número de unidades.
        // Set(item, "_minimumQueuePositionAllowedToSatisyNeed", 0);
        //     Posición mínima de cola desde la que puede satisfacerse una necesidad.
        // Set(item, "_components", Array.Empty<EntityComponent>());
        //     Componentes adicionales: fuego, materiales, stock u otros comportamientos.
        //
        // MANIPULACIÓN, ELECTRICIDAD NATIVA Y AMBULANCIAS
        // Set(item, "_canBePickedUp", true);
        //     Permite recogerlo y recolocarlo.
        // Set(item, "_canDragHoldSelect", true);
        //     Permite selección mantenida y arrastre.
        // Set(item, "_canBeSold", true);
        //     Permite venderlo.
        // Set(item, "_generatesElectricity", false);
        //     Marca utilizada por el sistema eléctrico NATIVO del juego.
        // Set(item, "_ecoRatingModifier", 0f);
        //     Modificación aplicada a la valoración ecológica.
        // Set(item, "_ambulanceConfig", configuracionDeAmbulancia);
        //     Configuración de ambulancia; null para objetos normales.
        // Set(item, "_fixedWallPlacement", RoomItemDefinition.FixedWallPlacementOption.None);
        //     Colocación fija de pared. Opciones: None=0, AmbulanceBayEntrance=1.
        // Set(item, "_primeEntitlementRequired", 0);
        //     Identificador de recompensa promocional necesaria; cero significa ninguna.
        // Set(item, "_isAnAmbulance", false);
        //     Identifica expresamente la definición como ambulancia.

        // ============================================================================
        // DEFINICIÓN NATIVA COMPLETA: CUADRO ELÉCTRICO
        // ============================================================================

        public const string ElectricalPanelDebugTag = "under pressure electrical panel";
        public const int ElectricalPanelSharedId = 9112111;
        public static readonly Guid ElectricalPanelGuid =
            new Guid("5c382c42-4eb7-4c8a-8aca-a5f902481102");

        public static RoomItemDefinition CreateElectricalPanel(ElectricalPanelRuntimeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Prefab == null) throw new ArgumentNullException(nameof(context.Prefab));
            if (context.Icon == null) throw new ArgumentNullException(nameof(context.Icon));
            if (context.MaintenanceInteraction == null)
                throw new ArgumentNullException(nameof(context.MaintenanceInteraction));

            var item = new RoomItemDefinition();
            Set(item, "_guid", ElectricalPanelGuid);
            Set(item, "_type", RoomItemDefinition.Type.Default);
            Set(item, "_localisedName", context.Name);
            Set(item, "_localisedDescription", context.Description);
            Set(item, "_functionalDescription", context.Description);
            Set(item, "_debugTag", ElectricalPanelDebugTag);
            Set(item, "_itemDeprecated", false);
            Set(item, "_icon", context.Icon);
            Set(item, "_iconWithoutBacking", context.Icon);
            Set(item, "_jobAssignmentIcon", null);
            Set(item, "_cost", 200);
            Set(item, "_energyCost", 0);
            Set(item, "_initiallyAvailable", true);
            Set(item, "_mustBeWhiteListed", false);
            Set(item, "_saveInRoomLayout", true);
            Set(item, "_silverCost", 0);
            Set(item, "_lockedFreeItem", false);
            Set(item, "_unlockedMessage", default(LocalisedString));
            Set(item, "_prestige", 0f);
            Set(item, "_hospitalLevelPoints", 0f);
            Set(item, "_size", RoomItemDefinition.Size.Medium);
            Set(item, "_playPlacmentSFX", true);
            Set(item, "_placeOnWall", true);
            Set(item, "_occupyWallOnly", false);
            Set(item, "_allowOnCorner", true);
            Set(item, "_gridSnap", 0.25f);
            Set(item, "_rotationSnap", 90f);
            Set(item, "_defaultRotation", 180f);
            Set(item, "_wallMagnetism", false);
            Set(item, "_wallMagnetismRotation", 0f);
            Set(item, "_wallMagnetismDistance", 2f);
            Set(item, "_singlePlace", false);
            Set(item, "_hasCollision", true);
            Set(item, "_useVerticalCollision", false);
            Set(item, "_collideWithSameType", false);
            Set(item, "_collideWithRugs", false);
            Set(item, "_collisionType", RoomItemDefinition.CollisionType.Default);
            Set(item, "_moveOutOfWay", false);
            Set(item, "_ignoreValidation", false);
            Set(item, "_isSelectable", true);
            Set(item, "_hasTooltip", true);
            Set(item, "_showQueuePositions", false);
            Set(item, "_showStatusIcon", true);
            Set(item, "_affectsNavigation", false);
            Set(item, "_removeWalls", false);
            Set(item, "_disableParticlesOnEdit", false);
            Set(item, "_prefab", context.Prefab);
            Set(item, "_blueprintPrefab", context.Prefab);
            Set(item, "_canBePlacedIn", Array.Empty<RoomDefinition.Type>());
            Set(item, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>());
            Set(item, "_attributes", new[]
            {
                new ObjectAttributes.Definition
                {
                    _type = ObjectAttributes.Type.Maintenance,
                    _initialValue = 0f
                }
            });
            Set(item, "_maintenanceModifer", 0f);
            Set(item, "_maintenanceFunctionalLevel", 100f);
            Set(item, "_maintenanceIconOverride", null);
            Set(item, "_janitorPriority", 10f);
            Set(item, "_janitorRepairRate", 1f);
            Set(item, "_ignoredByJanitors", false);
            Set(item, "_maintenanceDescription", JobMaintenance.JobDescription.BrokenMachine);
            Set(item, "_serviceDescription", JobService.JobDescription.None);
            Set(item, "_roomModifiers", Array.Empty<RoomModifier>());
            Set(item, "_interactionAttributeModifiers",
                context.MaintenanceAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>());
            Set(item, "_dataViewMode", DataViewManager.Mode.None);
            Set(item, "_hoverMenuPrefab", null);
            Set(item, "_selectMenuPrefab", null);
            Set(item, "_dlcPackRequired", null);
            Set(item, "_collaborativeResearchRequired", null);
            Set(item, "_superBugVictoryRequired", null);
            Set(item, "_upgrades", Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>());
            Set(item, "_singleInteractor", true);
            Set(item, "_interactionsAlwayAnimate", false);
            Set(item, "_minValidInteractions", 1);
            Set(item, "_interactions", new[] { context.MaintenanceInteraction });
            Set(item, "_filters", Array.Empty<RoomItemFilter>());
            Set(item, "_placementEffect", null);
            Set(item, "_spawnLimitCategory", null);
            Set(item, "_minimumQueuePositionAllowedToSatisyNeed", 0);
            Set(item, "_components", Array.Empty<EntityComponent>());
            Set(item, "_canBePickedUp", true);
            Set(item, "_canDragHoldSelect", true);
            Set(item, "_canBeSold", true);
            Set(item, "_generatesElectricity", false);
            Set(item, "_ecoRatingModifier", 0f);
            Set(item, "_ambulanceConfig", null);
            Set(item, "_fixedWallPlacement", RoomItemDefinition.FixedWallPlacementOption.None);
            Set(item, "_primeEntitlementRequired", 0);
            Set(item, "_isAnAmbulance", false);
            return item;
        }

        private static void Set(RoomItemDefinition item, string fieldName, object value)
        {
            var field = AccessTools.Field(typeof(RoomItemDefinition), fieldName);
            if (field == null)
                throw new MissingFieldException(typeof(RoomItemDefinition).FullName, fieldName);
            field.SetValue(item, value);
        }
    }

    public sealed class ElectricalPanelRuntimeContext
    {
        public LocalisedString Name;
        public LocalisedString Description;
        public GameObject Prefab;
        public Sprite Icon;
        public InteractionDefinition MaintenanceInteraction;
        public InteractionAttributeModifier[] MaintenanceAttributeModifiers;
    }

    // ================================================================================
    // ÚLTIMO BLOQUE: PARÁMETROS ESPECÍFICOS DEL CUADRO — NO SON CAMPOS NATIVOS
    // ================================================================================
    public static class ElectricalPanelParameters
    {
        // Red eléctrica añadida por UnderPressure.
        public const int MaximumConnectedTiles = 15;
        public const int MaximumLoad = 20;
        public const float WearPerConnectedTile = 0.01f;

        // Recursos y estructura gráfica añadidos por UnderPressure.
        public const string PrefabAsset = "assets/powerpanel/powerpanel.prefab";
        public const string ModelSourceAsset = "Assets/PowerPanel/powerpanel.FBX";
        public const string MaterialAsset = "Assets/PowerPanel/Material #25.mat";
        public const string TextureAsset = "Assets/PowerPanel/powerpanel tex.jpg";
        public const string IconTextureAsset = "Assets/PowerPanel/powerpanel icon.png";
        public const string BaseMesh = "CuadroEnergyBase";
        public const string DoorMesh = "CuadroEnergyDoor";
        public const string MaterialName = "Material #25";
        public const string MaintenanceSocket = "NURSE_LOCKER_BASE_RIG:START_FRONT";
        public static readonly Vector3 BuildBoundsCenter = new Vector3(0f, 0.5f, 0.5f);
        public static readonly Vector3 BuildBoundsSize = Vector3.one;
        public const bool BuildBoundsSolid = true;
    }
}
