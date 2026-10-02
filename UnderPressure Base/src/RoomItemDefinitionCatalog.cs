using System;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Definiciones completas de objetos creados por UnderPressure.
    /// Cada objeto debe declarar expresamente todos los campos nativos para no heredar
    /// valores invisibles de otro RoomItemDefinition.
    /// </summary>
    public static class RoomItemDefinitionCatalog
    {
        // -----------------------------------------------------------------------------
        // OBJETO: CUADRO ELÉCTRICO
        // -----------------------------------------------------------------------------

        public const string ElectricalPanelDebugTag = "under pressure electrical panel";
        public const int ElectricalPanelSharedId = 9112111;
        public static readonly Guid ElectricalPanelGuid =
            new Guid("5c382c42-4eb7-4c8a-8aca-a5f902481102");

        // PARÁMETROS PROPIOS DE UNDERPRESSURE
        public const int ElectricalPanelMaximumConnectedTiles = 15; // Máximo de celdas de baja tensión asignadas.
        public const int ElectricalPanelMaximumLoad = 20; // Carga máxima simultánea antes de sobrecargarse.
        public const float ElectricalPanelWearPerConnectedTile = 0.01f; // Desgaste por segundo y celda conectada.

        // RECURSOS GRÁFICOS DEL ASSETBUNDLE
        public const string ElectricalPanelPrefabAsset = "assets/powerpanel/powerpanel.prefab"; // Prefab completo.
        public const string ElectricalPanelModelSourceAsset = "Assets/PowerPanel/powerpanel.FBX"; // Fuente usada al regenerar el bundle.
        public const string ElectricalPanelMaterialAsset = "Assets/PowerPanel/Material #25.mat"; // Material del modelo.
        public const string ElectricalPanelTextureAsset = "Assets/PowerPanel/powerpanel tex.jpg"; // Textura principal.
        public const string ElectricalPanelIconTextureAsset = "Assets/PowerPanel/powerpanel icon.png"; // Icono del catálogo.
        public const string ElectricalPanelBaseMesh = "CuadroEnergyBase"; // Malla incrustada de la carcasa fija.
        public const string ElectricalPanelDoorMesh = "CuadroEnergyDoor"; // Malla incrustada de la puerta animable.
        public const string ElectricalPanelMaterialName = "Material #25"; // Material asignado a ambas mallas.
        public const string ElectricalPanelMaintenanceSocket =
            "NURSE_LOCKER_BASE_RIG:START_FRONT"; // Punto donde se coloca el conserje.

        public static readonly Vector3 ElectricalPanelBuildBoundsCenter = new Vector3(0f, 0.5f, 0.5f);
        public static readonly Vector3 ElectricalPanelBuildBoundsSize = Vector3.one;
        public const bool ElectricalPanelBuildBoundsSolid = true;

        /// <summary>
        /// Crea el cuadro desde cero. Los recursos cargados en ejecución se reciben en el contexto,
        /// pero todos los campos de RoomItemDefinition se asignan aquí de forma explícita.
        /// </summary>
        public static RoomItemDefinition CreateElectricalPanel(ElectricalPanelRuntimeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Prefab == null) throw new ArgumentNullException(nameof(context.Prefab));
            if (context.Icon == null) throw new ArgumentNullException(nameof(context.Icon));
            if (context.MaintenanceInteraction == null)
                throw new ArgumentNullException(nameof(context.MaintenanceInteraction));

            var item = new RoomItemDefinition();

            // IDENTIDAD Y TEXTOS NATIVOS
            Set(item, "_guid", ElectricalPanelGuid); // Identificador permanente usado por partidas guardadas.
            Set(item, "_type", RoomItemDefinition.Type.Default); // Categoría general del objeto.
            Set(item, "_localisedName", context.Name); // Nombre localizado visible en el catálogo.
            Set(item, "_localisedDescription", context.Description); // Descripción localizada principal.
            Set(item, "_functionalDescription", context.Description); // Texto localizado que explica su función.
            Set(item, "_debugTag", ElectricalPanelDebugTag); // Identidad legible usada por el mod y los registros.
            Set(item, "_itemDeprecated", false); // False mantiene el objeto disponible.

            // ICONOS NATIVOS
            Set(item, "_icon", context.Icon); // Icono principal del catálogo.
            Set(item, "_iconWithoutBacking", context.Icon); // Variante sin fondo; usa el mismo sprite.
            Set(item, "_jobAssignmentIcon", null); // Icono de asignación de trabajo; no necesita uno propio.

            // PRECIO, DESBLOQUEO Y PRESTIGIO
            Set(item, "_cost", 200); // Precio normal de compra.
            Set(item, "_energyCost", 0); // Coste energético nativo; el cuadro distribuye, no consume.
            Set(item, "_initiallyAvailable", true); // Disponible desde el inicio.
            Set(item, "_mustBeWhiteListed", false); // No exige una lista blanca de desbloqueo.
            Set(item, "_saveInRoomLayout", true); // Se conserva en plantillas y diseños de sala.
            Set(item, "_silverCost", 0); // Precio en Kudosh.
            Set(item, "_lockedFreeItem", false); // No es un objeto gratuito bloqueado.
            Set(item, "_unlockedMessage", default(LocalisedString)); // Mensaje especial de desbloqueo.
            Set(item, "_prestige", 0f); // Prestigio añadido a la sala.
            Set(item, "_hospitalLevelPoints", 0f); // Puntos aportados al nivel del hospital.

            // TAMAÑO Y COLOCACIÓN
            Set(item, "_size", RoomItemDefinition.Size.Medium); // Clasificación de tamaño usada por el catálogo.
            Set(item, "_playPlacmentSFX", true); // Reproduce el sonido nativo al colocarlo.
            Set(item, "_placeOnWall", true); // Se ajusta a paredes.
            Set(item, "_occupyWallOnly", false); // Puede conservar su base delante de la pared.
            Set(item, "_allowOnCorner", true); // Admite colocación junto a esquinas.
            Set(item, "_gridSnap", 0.25f); // Paso de posición sobre la cuadrícula.
            Set(item, "_rotationSnap", 90f); // Paso de rotación en grados.
            Set(item, "_defaultRotation", 180f); // Orientación inicial frente a la pared.
            Set(item, "_wallMagnetism", false); // No busca paredes desde lejos.
            Set(item, "_wallMagnetismRotation", 0f); // Giro extra aplicado por el magnetismo.
            Set(item, "_wallMagnetismDistance", 2f); // Distancia nativa de detección de pared.
            Set(item, "_singlePlace", false); // Permite colocar varios cuadros.

            // COLISIÓN, NAVEGACIÓN Y SELECCIÓN
            Set(item, "_hasCollision", true); // Reserva físicamente sus límites de construcción.
            Set(item, "_useVerticalCollision", false); // No usa la prueba vertical especial.
            Set(item, "_collideWithSameType", false); // No añade una regla exclusiva contra cuadros iguales.
            Set(item, "_collideWithRugs", false); // No colisiona especialmente con alfombras.
            Set(item, "_collisionType", RoomItemDefinition.CollisionType.Default); // Colisión nativa normal.
            Set(item, "_moveOutOfWay", false); // Los personajes no lo apartan.
            Set(item, "_ignoreValidation", false); // Respeta las comprobaciones de colocación.
            Set(item, "_isSelectable", true); // Puede seleccionarse con el cursor.
            Set(item, "_hasTooltip", true); // Muestra información al señalarlo.
            Set(item, "_showQueuePositions", false); // No dibuja posiciones de cola.
            Set(item, "_showStatusIcon", true); // Permite mostrar averías y otros estados.
            Set(item, "_affectsNavigation", false); // La navegación se controla mediante sus límites del prefab.
            Set(item, "_removeWalls", false); // No elimina la pared sobre la que se coloca.
            Set(item, "_disableParticlesOnEdit", false); // No necesita apagar partículas en edición.

            // PREFABS Y RESTRICCIONES DE SALA
            Set(item, "_prefab", context.Prefab); // Modelo completo cargado desde ElectricalPanelPrefabAsset.
            Set(item, "_blueprintPrefab", context.Prefab); // Representación usada durante la construcción.
            Set(item, "_canBePlacedIn", Array.Empty<RoomDefinition.Type>()); // Sin lista restrictiva de salas.
            Set(item, "_cantBePlacedIn", Array.Empty<RoomDefinition.Type>()); // Sin salas expresamente prohibidas.

            // ATRIBUTOS Y MANTENIMIENTO
            Set(item, "_attributes", new[]
            {
                new ObjectAttributes.Definition
                {
                    _type = ObjectAttributes.Type.Maintenance, // Contador nativo: 0 perfecto, 100 averiado.
                    _initialValue = 0f // Todo cuadro nuevo comienza sin desgaste.
                }
            });
            Set(item, "_maintenanceModifer", 0f); // Power calcula la tasa individual según sus celdas conectadas.
            Set(item, "_maintenanceFunctionalLevel", 100f); // Deja de funcionar al alcanzar 100 de desgaste.
            Set(item, "_maintenanceIconOverride", null); // Usa el icono nativo de máquina averiada.
            Set(item, "_janitorPriority", 10f); // Prioridad nativa equivalente a una máquina.
            Set(item, "_janitorRepairRate", 1f); // Velocidad nativa equivalente a las máquinas examinadas.
            Set(item, "_ignoredByJanitors", false); // Permite que el planificador envíe conserjes.
            Set(item, "_maintenanceDescription", JobMaintenance.JobDescription.BrokenMachine); // Trabajo de reparación.
            Set(item, "_serviceDescription", JobService.JobDescription.None); // No necesita un trabajo de servicio aparte.

            // EFECTOS DE SALA E INTERACCIONES
            Set(item, "_roomModifiers", Array.Empty<RoomModifier>()); // No altera atractivo, temperatura ni prestigio.
            Set(item, "_interactionAttributeModifiers",
                context.MaintenanceAttributeModifiers ?? Array.Empty<InteractionAttributeModifier>()); // XP nativa al reparar.
            Set(item, "_dataViewMode", DataViewManager.Mode.None); // No fuerza una vista de datos nativa.
            Set(item, "_hoverMenuPrefab", null); // Usa el menú genérico al pasar el cursor.
            Set(item, "_selectMenuPrefab", null); // Usa el menú genérico al seleccionarlo.

            // REQUISITOS, MEJORAS Y COLAS
            Set(item, "_dlcPackRequired", null); // No exige DLC.
            Set(item, "_collaborativeResearchRequired", null); // No exige investigación colaborativa.
            Set(item, "_superBugVictoryRequired", null); // No exige completar un Superbug.
            Set(item, "_upgrades", Array.Empty<SharedInstance<RoomItemUpgradeDefinition>>()); // Sin mejoras por niveles.
            Set(item, "_singleInteractor", true); // Solo un conserje puede repararlo simultáneamente.
            Set(item, "_interactionsAlwayAnimate", false); // Respeta la animación definida por la interacción.
            Set(item, "_minValidInteractions", 1); // La interacción Maintenance debe ser válida.
            Set(item, "_interactions", new[] { context.MaintenanceInteraction }); // Única interacción propia.
            Set(item, "_filters", Array.Empty<RoomItemFilter>()); // Sin filtros heredados de otros objetos.
            Set(item, "_placementEffect", null); // Sin efecto visual adicional al colocarlo.
            Set(item, "_spawnLimitCategory", null); // Sin límite global de aparición.
            Set(item, "_minimumQueuePositionAllowedToSatisyNeed", 0); // Valor nativo para colas; no usa necesidades.
            Set(item, "_components", Array.Empty<EntityComponent>()); // Sin componentes de fuego, stock o materiales.

            // VENTA, ELECTRICIDAD NATIVA Y AMBULANCIAS
            Set(item, "_canBePickedUp", true); // Puede moverse después de construirlo.
            Set(item, "_canDragHoldSelect", true); // Admite selección mantenida y arrastre.
            Set(item, "_canBeSold", true); // Puede venderse.
            Set(item, "_generatesElectricity", false); // No usa el generador eléctrico nativo del juego.
            Set(item, "_ecoRatingModifier", 0f); // No modifica la valoración ecológica.
            Set(item, "_ambulanceConfig", null); // No es una ambulancia.
            Set(item, "_fixedWallPlacement", RoomItemDefinition.FixedWallPlacementOption.None); // Sin anclaje fijo especial.
            Set(item, "_primeEntitlementRequired", 0); // No exige recompensa promocional.
            Set(item, "_isAnAmbulance", false); // Confirma que es un objeto de habitación normal.

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

    /// <summary>
    /// Referencias que solo existen después de cargar el juego y el AssetBundle.
    /// Sus campos nativos se asignan igualmente dentro del catálogo.
    /// </summary>
    public sealed class ElectricalPanelRuntimeContext
    {
        public LocalisedString Name;
        public LocalisedString Description;
        public GameObject Prefab;
        public Sprite Icon;
        public InteractionDefinition MaintenanceInteraction;
        public InteractionAttributeModifier[] MaintenanceAttributeModifiers;
    }
}
