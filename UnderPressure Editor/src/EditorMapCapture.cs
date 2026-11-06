// Updated: 2026-10-20 - Camara cenital limpia, sin postprocesado, y salida JPG opaca.
// Archivo INDEPENDIENTE. Anadirlo a "UnderPressure Editor/src" (no sustituye otros .cs).
using System;
using System.IO;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class EditorMapCaptureOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level level)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning) return;
            EditorMapCapture.Attach(__instance, level);
        }
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Destroy")]
    internal static class EditorMapCaptureClosePatch
    {
        private static void Prefix() => EditorMapCapture.Detach();
    }

    // Captura una imagen del MUNDO 3D; no del HUD ni de la mascara Fog.
    // El mapa de Fog puede tener 128x128 texeles, pero ese valor NO determina
    // el origen ni la extension espacial del terreno.
    internal static class EditorMapCapture
    {
        // ===== VALORES EDITABLES =====
        private const int Resolucion = 768;            // Coincide con el visor de niebla.
        private const int CalidadJpeg = 95;
        private const float Margen = 0.72f;             // 8% extra para que no se corte el terreno.
        private const float AlturaCamara = 100f;        // Por encima del punto mas alto del terreno.
        private const float DesplazamientoX = 10f;
        private const float DesplazamientoZ = 3f;
        // ===========================

        private static Level _level;
        private static bool _capturing;

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            Detach();
            if (menu == null || level == null) return;
            _level = level;
        }

        internal static void Detach()
        {
            _level = null;
        }

        internal static string GetMapImagePath(Level level)
        {
            if (level == null) return null;
            var carpeta = ScenarioEditorStorage.CurrentMapFolder(level);
            return string.IsNullOrEmpty(carpeta)
                ? null : Path.Combine(carpeta, "map.jpg");
        }

        internal static void Capture(Action<string> completed)
        {
            if (_capturing || _level == null ||
                !ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning)
            {
                completed?.Invoke(null);
                return;
            }

            _capturing = true;
            string archivoGuardado = null;
            try
            {
                Bounds terreno;
                if (!TryGetTerrainBounds(out terreno))
                    throw new InvalidOperationException(
                        "No se encontraron mallas Pass bajo un contenedor Terrain_*.");

                var original = Camera.main;
                if (original == null)
                    throw new InvalidOperationException("La camara del juego no esta disponible.");

                // Centrar el encuadre segun la union de las PARCELAS reales.
                // Mantener el terreno completo dentro de la imagen cuadrada.
                Bounds parcelas;
                var centro = TryGetPlotBounds(_level, out parcelas)
                    ? parcelas.center : terreno.center;
                var mitadHorizontal = Mathf.Max(
                    Mathf.Abs(terreno.min.x - centro.x),
                    Mathf.Abs(terreno.max.x - centro.x),
                    Mathf.Abs(terreno.min.z - centro.z),
                    Mathf.Abs(terreno.max.z - centro.z));
                var alturaVisible = mitadHorizontal * Margen;

                // Crear una camara limpia y copiar solo Camera. Clonar el objeto
                // completo copiaba tambien PostProcessLayer: FogOfWarRenderer
                // dibujaba su quad negro sobre casi toda la captura cenital.
                // NUNCA mover, girar ni modificar el objeto original.
                GameObject camaraObject = null;
                Camera camara = null;
                Light[] lucesEscena = null;
                bool[] lucesActivas = null;
                RenderTexture destino = null;
                Texture2D imagen = null;
                var anterior = RenderTexture.active;
                var sombrasAnteriores = QualitySettings.shadows;
                var distanciaSombrasAnterior = QualitySettings.shadowDistance;
                var lodAnterior = QualitySettings.lodBias;
                var ambienteModoAnterior = RenderSettings.ambientMode;
                var ambienteColorAnterior = RenderSettings.ambientLight;
                var ambienteIntensidadAnterior = RenderSettings.ambientIntensity;
                var reflejosAnteriores = RenderSettings.reflectionIntensity;
                var solAnterior = RenderSettings.sun;

                try
                {
                    camaraObject = new GameObject("UP_CamaraCenital", typeof(Camera));
                    camara = camaraObject.GetComponent<Camera>();
                    camara.CopyFrom(original);
                    camara.enabled = false;
                    // Los materiales opacos del juego quedan negros al renderizar
                    // esta camara auxiliar con la ruta diferida de la principal.
                    // Forward resuelve la iluminacion directamente para cada
                    // material sin depender de sus buffers diferidos externos.
                    camara.renderingPath = RenderingPath.Forward;
                    camara.allowHDR = false;
                    camara.allowMSAA = true;
                    camara.depthTextureMode = DepthTextureMode.None;

                    camara.transform.position = new Vector3(
                        centro.x + DesplazamientoX,
                        terreno.max.y + AlturaCamara,
                        centro.z + DesplazamientoZ);
                    camara.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    camara.orthographic = true;
                    camara.orthographicSize = Mathf.Max(1f, alturaVisible);
                    camara.aspect = 1f;
                    camara.rect = new Rect(0f, 0f, 1f, 1f);
                    camara.nearClipPlane = 0.01f;
                    camara.farClipPlane = Mathf.Max(2000f,
                        AlturaCamara + terreno.size.y + 100f);
                    camara.useOcclusionCulling = false;
                    camara.layerCullDistances = new float[32];
                    camara.layerCullSpherical = false;

                    // La captura debe mostrar las texturas sin focos, sombras ni
                    // reflejos. Se desactivan temporalmente todas las luces y se
                    // usa solo ambiente blanco plano; finally restaura la escena.
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.shadowDistance = camara.farClipPlane;
                    QualitySettings.lodBias = Mathf.Max(lodAnterior, 10f);
                    RenderSettings.ambientMode =
                        UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = Color.white;
                    RenderSettings.ambientIntensity = 1f;
                    RenderSettings.reflectionIntensity = 0f;
                    RenderSettings.sun = null;
                    lucesEscena = UnityEngine.Object.FindObjectsOfType<Light>(true);
                    lucesActivas = new bool[lucesEscena.Length];
                    for (var i = 0; i < lucesEscena.Length; ++i)
                    {
                        var luz = lucesEscena[i];
                        if (luz == null) continue;
                        lucesActivas[i] = luz.enabled;
                        luz.enabled = false;
                    }

                    // Una captura de la escena 3D, sin interfaz en Screen Space
                    // y sin elementos World Space de la capa UI.
                    var capaUi = LayerMask.NameToLayer("UI");
                    if (capaUi >= 0) camara.cullingMask &= ~(1 << capaUi);

                    destino = RenderTexture.GetTemporary(Resolucion, Resolucion, 24,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    destino.filterMode = FilterMode.Bilinear;
                    camara.targetTexture = destino;
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Captura cenital: ruta original=" +
                        original.actualRenderingPath + "; ruta captura=" +
                        camara.actualRenderingPath + "; mascara=0x" +
                        camara.cullingMask.ToString("X8") + ".");
                    camara.Render();

                    RenderTexture.active = destino;
                    imagen = new Texture2D(Resolucion, Resolucion, TextureFormat.RGB24, false);
                    imagen.ReadPixels(new Rect(0f, 0f, Resolucion, Resolucion),
                        0, 0, false);
                    imagen.Apply(false, false);

                    // El directorio de capturas es independiente de los recursos
                    // editables: el JPG acompana al mapa, pero no altera config.json,
                    // parcelas ni texturas de Fog.
                    var archivo = GetMapImagePath(_level);
                    if (string.IsNullOrEmpty(archivo))
                        throw new InvalidOperationException(
                            "Guarda primero el mapa antes de generar su imagen cenital.");
                    var jpg = ImageConversion.EncodeToJPG(imagen, CalidadJpeg);
                    if (jpg == null || jpg.Length == 0)
                        throw new IOException("No se ha podido codificar la captura JPG.");
                    archivoGuardado = ScenarioEditorStorage.CommitCurrentMapFile(
                        _level, "map.jpg", jpg);
                    if (string.IsNullOrEmpty(archivoGuardado))
                        throw new IOException("No se ha podido actualizar map.jpg dentro del paquete.");

                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Captura cenital guardada en: " + archivoGuardado +
                        "; centro real=(" + centro.x.ToString("F2") + ", " +
                        centro.z.ToString("F2") + "); tamano X/Z=(" +
                        terreno.size.x.ToString("F2") + ", " +
                        terreno.size.z.ToString("F2") + ").");
                }
                finally
                {
                    RenderTexture.active = anterior;
                    QualitySettings.shadows = sombrasAnteriores;
                    QualitySettings.shadowDistance = distanciaSombrasAnterior;
                    QualitySettings.lodBias = lodAnterior;
                    RenderSettings.ambientMode = ambienteModoAnterior;
                    RenderSettings.ambientLight = ambienteColorAnterior;
                    RenderSettings.ambientIntensity = ambienteIntensidadAnterior;
                    RenderSettings.reflectionIntensity = reflejosAnteriores;
                    RenderSettings.sun = solAnterior;
                    if (lucesEscena != null && lucesActivas != null)
                    {
                        for (var i = 0; i < lucesEscena.Length; ++i)
                        {
                            var luz = lucesEscena[i];
                            if (luz != null) luz.enabled = lucesActivas[i];
                        }
                    }
                    if (camara != null) camara.targetTexture = null;
                    if (destino != null) RenderTexture.ReleaseTemporary(destino);
                    if (imagen != null) UnityEngine.Object.Destroy(imagen);
                    if (camaraObject != null) UnityEngine.Object.Destroy(camaraObject);
                }
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("Captura cenital: " + ex);
            }
            finally
            {
                _capturing = false;
                completed?.Invoke(archivoGuardado);
            }
        }

        // WorldBounds esta en coordenadas de cuadrícula de TH20.
        // Una casilla equivale a DOS unidades de mundo en X y Z.
        private static bool TryGetPlotBounds(Level level, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;
            var plots = level?.WorldState?.HospitalPlots;
            if (plots == null) return false;
            foreach (var plot in plots)
            {
                var floor = plot?.HospitalMap?.FloorPlan;
                if (floor == null) continue;
                var grid = floor.WorldBounds;
                var minX = grid.Min.X * 2f;
                var minZ = grid.Min.Y * 2f;
                var maxX = grid.Max.X * 2f;
                var maxZ = grid.Max.Y * 2f;
                if (maxX <= minX || maxZ <= minZ) continue;
                var plotBounds = new Bounds(
                    new Vector3((minX + maxX) * .5f, 0f, (minZ + maxZ) * .5f),
                    new Vector3(maxX - minX, 0f, maxZ - minZ));
                if (!found) { bounds = plotBounds; found = true; }
                else bounds.Encapsulate(plotBounds);
            }
            return found;
        }

        private static bool TryGetTerrainBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            var encontrado = false;
            // Se utiliza el mismo criterio que el pintor de terreno del editor:
            // contenedor Terrain_* y renderers con nombre Pass*.
            foreach (var tr in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (tr == null ||
                    !tr.name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var renderer in tr.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null ||
                        !renderer.gameObject.name.StartsWith(
                            "Pass", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Se incluyen todos los pases del terreno, aun cuando alguno
                    // este oculto temporalmente para pintar.
                    if (!encontrado)
                    {
                        bounds = renderer.bounds;
                        encontrado = true;
                    }
                    else
                        bounds.Encapsulate(renderer.bounds);
                }
            }
            return encontrado && bounds.size.x > 0.1f && bounds.size.z > 0.1f;
        }

    }
}
