// Updated: 2026-10-20 - Camara cenital limpia, sin postprocesado, y salida JPG opaca.
// Archivo INDEPENDIENTE. Anadirlo a "UnderPressure Editor/src" (no sustituye otros .cs).
using System;
using System.IO;
using System.Text;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.UI;

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
        private const int Resolucion = 4096;           // JPG cuadrado, 4096x4096.
        private const int CalidadJpeg = 95;
        private const float Margen = 1.08f;             // 8% extra para que no se corte el terreno.
        private const float AlturaCamara = 120f;        // Por encima del punto mas alto del terreno.
        private const float BotonX = 435f;              // Posicion del boton respecto al centro superior.
        private const float BotonY = -60f;
        // ===========================

        private static GameObject _buttonRoot;
        private static Text _buttonLabel;
        private static Level _level;
        private static bool _capturing;
        private static string T(string key) => EditorLocalization.Get(key);

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            Detach();
            if (menu == null || level == null) return;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _level = level;
            _buttonRoot = new GameObject("UP_CapturaCenital",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            _buttonRoot.transform.SetParent(canvas.transform, false);

            var rect = (RectTransform)_buttonRoot.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(BotonX, BotonY);
            rect.sizeDelta = new Vector2(172f, 38f);

            var image = _buttonRoot.GetComponent<Image>();
            image.color = new Color(0.22f, 0.44f, 0.48f, 0.96f);

            var button = _buttonRoot.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(Capture);

            var caption = new GameObject("Texto", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            caption.transform.SetParent(_buttonRoot.transform, false);
            var captionRect = (RectTransform)caption.transform;
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = Vector2.zero;
            captionRect.offsetMax = Vector2.zero;

            _buttonLabel = caption.GetComponent<Text>();
            _buttonLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _buttonLabel.fontSize = 17;
            _buttonLabel.alignment = TextAnchor.MiddleCenter;
            _buttonLabel.color = Color.white;
            _buttonLabel.raycastTarget = false;
            _buttonLabel.text = T("editor.capture.button");
        }

        internal static void Detach()
        {
            if (_buttonRoot != null) UnityEngine.Object.Destroy(_buttonRoot);
            _buttonRoot = null;
            _buttonLabel = null;
            _level = null;
        }

        private static void Capture()
        {
            if (_capturing || _level == null ||
                !ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning)
                return;

            _capturing = true;
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
                RenderTexture destino = null;
                Texture2D imagen = null;
                var anterior = RenderTexture.active;

                try
                {
                    camaraObject = new GameObject("UP_CamaraCenital", typeof(Camera));
                    camara = camaraObject.GetComponent<Camera>();
                    camara.CopyFrom(original);
                    camara.enabled = false;

                    camara.transform.position = new Vector3(
                        centro.x, terreno.max.y + AlturaCamara, centro.z);
                    camara.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    camara.orthographic = true;
                    camara.orthographicSize = Mathf.Max(1f, alturaVisible);
                    camara.aspect = 1f;
                    camara.rect = new Rect(0f, 0f, 1f, 1f);
                    camara.nearClipPlane = 0.01f;
                    camara.farClipPlane = Mathf.Max(500f,
                        AlturaCamara + terreno.size.y + 100f);
                    camara.useOcclusionCulling = false;

                    // Una captura de la escena 3D, sin interfaz en Screen Space
                    // y sin elementos World Space de la capa UI.
                    var capaUi = LayerMask.NameToLayer("UI");
                    if (capaUi >= 0) camara.cullingMask &= ~(1 << capaUi);

                    destino = RenderTexture.GetTemporary(Resolucion, Resolucion, 24,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    destino.filterMode = FilterMode.Bilinear;
                    camara.targetTexture = destino;
                    camara.Render();

                    RenderTexture.active = destino;
                    imagen = new Texture2D(Resolucion, Resolucion, TextureFormat.RGB24, false);
                    imagen.ReadPixels(new Rect(0f, 0f, Resolucion, Resolucion),
                        0, 0, false);
                    imagen.Apply(false, false);

                    // El directorio de capturas es independiente de los recursos
                    // del mapa: no altera UPMap, plots ni texturas de Fog.
                    var carpeta = Path.Combine(ScenarioEditorStorage.Folder, "Capturas");
                    Directory.CreateDirectory(carpeta);
                    var nombreMapa = SafeFileName(
                        ScenarioEditorStorage.CurrentMapName(_level));
                    var nombre = "Cenital_" + nombreMapa + "_" +
                                 DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg";
                    var archivo = Path.Combine(carpeta, nombre);
                    var jpg = ImageConversion.EncodeToJPG(imagen, CalidadJpeg);
                    if (jpg == null || jpg.Length == 0)
                        throw new IOException("No se ha podido codificar la captura JPG.");
                    File.WriteAllBytes(archivo, jpg);

                    if (_buttonLabel != null)
                        _buttonLabel.text = T("editor.capture.saved");
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Captura cenital guardada en: " + archivo +
                        "; centro real=(" + centro.x.ToString("F2") + ", " +
                        centro.z.ToString("F2") + "); tamano X/Z=(" +
                        terreno.size.x.ToString("F2") + ", " +
                        terreno.size.z.ToString("F2") + ").");
                }
                finally
                {
                    RenderTexture.active = anterior;
                    if (camara != null) camara.targetTexture = null;
                    if (destino != null) RenderTexture.ReleaseTemporary(destino);
                    if (imagen != null) UnityEngine.Object.Destroy(imagen);
                    if (camaraObject != null) UnityEngine.Object.Destroy(camaraObject);
                }
            }
            catch (Exception ex)
            {
                if (_buttonLabel != null)
                    _buttonLabel.text = T("editor.capture.error");
                UnderPressureEditorPlugin.Log?.LogError("Captura cenital: " + ex);
            }
            finally
            {
                _capturing = false;
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

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Mapa";
            var invalidos = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
                sb.Append(Array.IndexOf(invalidos, ch) >= 0 ? '_' : ch);
            return sb.ToString().Trim();
        }
    }
}
