// Purpose: Defines the editable constants and material parameters used by hospital lighting.
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderPressure
{
    internal static class LightingParameters
    {
        // PRUEBA DEL CRISTAL: elimina cualquier otra sombra estructural y deja únicamente
        // la sombra del cristal de las ventanas. false devuelve el comportamiento normal.
        internal static bool IsolateWindowGlassShadow = false;

        // Brillo de las zonas oscuras de todo el hospital. 0 = negro; 1 = muy iluminado.
        internal static float AmbientIntensity = 0.15f;
        // Color que tendrán las zonas iluminadas por el brillo anterior.
        internal static Color AmbientColor = Color.white;

        // Fuerza de la gran mancha brillante que cruza suelos, paredes y objetos.
        // Ponlo en 0 para eliminar esa mancha.
        internal static float DirectionalIntensity = 0.20f;
        // Color de esa gran mancha brillante.
        internal static Color DirectionalColor = Color.white;

        // 1 hace que objetos, personas, suelos y paredes usen la iluminación de la sala.
        // 0 hace que la ignoren y conserven la iluminación exterior.
        internal static float MaterialRoomLighting = 1.00f;

        // Valor enviado directamente a las luces automáticas del techo. 0 las apaga; 1 las enciende.
        internal static float NativeCeilingLights = 0.00f;
        // Altura usada por las luces automáticas del techo.
        internal static float NativeCeilingLightHeight = 2.50f;
        // Brillo de las luces automáticas del techo.
        internal static float NativeCeilingLightIntensity = 0.20f;
        // Separación lateral de las filas de luces automáticas del techo.
        internal static float NativeCeilingLightOffset = 4.00f;

        // Oscuridad de las sombras interiores. 0 las elimina; 1 las deja completamente oscuras.0.70f
        internal static float RoomShadowIntensity = 0.70f;
        // Fuerza con la que la iluminación se difumina al llegar a sus bordes.0.40f;
        internal static float RoomLightFalloffStrength = 0.40f;
        // Anchura de la zona difuminada en los bordes de la iluminación.1.00f;
        internal static float RoomLightFalloffThickness = 1.00f;
        // Intensidad de la pérdida de luz cerca del techo.0.00f;
        internal static float RoomCeilingFalloffAmplitude = 1.00f;

        // Fuerza de la sombra del cristal cuando la prueba anterior está activa.
        internal static float WindowGlassTestShadowStrength = 1.00f;

        // Sombras de la luz direccional interior: None, Hard o Soft.
        internal static LightShadows InteriorDirectionalShadows = LightShadows.Hard;
        // Intensidad de las sombras de la luz direccional interior. 0 las quita; 1 usa toda su fuerza.1.00f;
        internal static float InteriorDirectionalShadowStrength = 0.00f;
        // Separación de la sombra respecto a la superficie que la produce.
        internal static float InteriorDirectionalShadowBias = 0.05f;
        // Separación adicional de la sombra siguiendo la inclinación de la superficie.
        internal static float InteriorDirectionalShadowNormalBias = 0.40f;
        // Distancia mínima desde la que la luz direccional empieza a dibujar sombras.
        internal static float InteriorDirectionalShadowNearPlane = 0.20f;
        // Modo de cálculo de la luz direccional interior: Auto, Important o NotImportant.
        internal static LightRenderMode InteriorDirectionalRenderMode = LightRenderMode.Auto;
        // Capas exactas que recibe la luz direccional interior. -1 incluye todas las capas.
        internal static int InteriorDirectionalCullingMask = -1;
        // true usa la dirección del sol exterior; false conserva la dirección interior original del juego.true
        internal static bool CopyExteriorDirectionToInterior = true;

        // Altura del techo que tapa el sol. 2.00 coincide con la parte superior del muro.
        internal static float ShadowCeilingHeight = 2.00f;
        // Estado inicial del techo: On lo muestra; ShadowsOnly lo oculta pero conserva su sombra; Off lo desactiva.
        internal static ShadowCastingMode ShadowCeilingModeAtStart = ShadowCastingMode.ShadowsOnly;
        // Valor exacto que recibe el techo cuando el botón lo muestra.
        internal static ShadowCastingMode ShadowCeilingVisibleMode = ShadowCastingMode.On;
        // Valor exacto que recibe el techo cuando el botón lo oculta.
        internal static ShadowCastingMode ShadowCeilingHiddenMode = ShadowCastingMode.ShadowsOnly;

        // true añade el antiguo foco artificial de ventana; false no lo crea.
        internal static bool UseWindowSpotFallback = false;
        // Tipo real de la luz artificial de ventana: Spot, Point, Directional o Area.
        internal static LightType WindowLightType = LightType.Spot;
        // Color del foco artificial de ventana.
        internal static Color WindowSpotColor = new Color(1.00f, 0.91f, 0.73f, 1.00f);
        // Distancia hasta la que llega el foco artificial de ventana.
        internal static float WindowSpotRange = 12.00f;
        // Brillo del foco artificial de ventana.
        internal static float WindowSpotIntensity = 0.85f;
        // Anchura del foco artificial de ventana. 170 crea un cono muy abierto.
        internal static float WindowSpotAngle = 170.00f;
        // Anchura de la zona central sin difuminar del foco.
        internal static float WindowSpotInnerAngle = 0.00f;
        // Distancia del foco hacia el exterior respecto a la ventana.
        internal static float WindowSpotExteriorOffset = 1.50f;
        // Inclinación del foco hacia el suelo.
        internal static float WindowSpotDownwardAim = 0.18f;
        // Sombras del foco artificial: None, Hard o Soft.
        internal static LightShadows WindowSpotShadows = LightShadows.None;
        // Intensidad de las sombras del foco. 0 las quita; 1 usa toda su fuerza.
        internal static float WindowSpotShadowStrength = 1.00f;
        // Separación de la sombra del foco respecto al objeto.
        internal static float WindowSpotShadowBias = 0.05f;
        // Separación adicional de la sombra del foco en superficies inclinadas.
        internal static float WindowSpotShadowNormalBias = 0.40f;
        // Distancia mínima desde la que el foco empieza a dibujar sombras.
        internal static float WindowSpotShadowNearPlane = 0.20f;
        // Modo de cálculo del foco: Auto, Important o NotImportant.
        internal static LightRenderMode WindowSpotRenderMode = LightRenderMode.Auto;
        // Multiplicador de la luz indirecta producida por el foco.
        internal static float WindowSpotBounceIntensity = 1.00f;
        // Máscara exacta de capas iluminadas por el foco. -1 incluye todas.
        internal static int WindowSpotCullingMask = -1;

        // Color de la luz de la lámpara del escritorio de Consulta.
        internal static Color GpLampColor = new Color(1.00f, 0.78f, 0.48f, 1.00f);
        // Distancia hasta la que ilumina esa lámpara.
        internal static float GpLampRange = 5.50f;
        // Brillo de esa lámpara.
        internal static float GpLampIntensity = 1.25f;
        // Altura de esa luz sobre el suelo.
        internal static float GpLampHeight = 0.55f;

        // Color de la luz del paciente Cabeza Bombilla.
        internal static Color LightHeadedColor = new Color(1.00f, 0.84f, 0.35f, 1.00f);
        // Distancia hasta la que ilumina su cabeza.
        internal static float LightHeadedRange = 5.00f;
        // Brillo de su cabeza.
        internal static float LightHeadedIntensity = 1.40f;
        // Altura de la luz respecto al centro de su cabeza.
        internal static float LightHeadedHeight = 0.08f;

        // Color reflejado por metales, suelos pulidos y otros objetos brillantes.
        internal static Color NeutralReflectionColor = Color.black;
        // Color ambiental enviado directamente a los objetos de las salas.
        internal static Color RoomItemAmbientColor = Color.white;
        // Color direccional enviado directamente a los objetos de las salas.
        internal static Color RoomItemDirectionalColor = Color.white;
    }
}
