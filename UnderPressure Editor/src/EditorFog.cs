// Updated: 2026-10-20 - Pincel universal unico con alfa + / - y misma difuminacion para ambos botones.
using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioFogViewerOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level level)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning) return;
            ScenarioFogViewer.Attach(__instance, level);
        }
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Destroy")]
    internal static class ScenarioFogViewerClosePatch
    {
        private static void Prefix() => ScenarioFogViewer.Detach();
    }

    internal static class ScenarioFogViewer
    {
        private const float PreviewSize = 768f;  // 6x128; 3x256 (unidades de interfaz)
        private const float PanelWidth = 864f;
        private const float PanelHeight = 910f;
        private static readonly Color PanelColor = new Color(0.135f, 0.165f, 0.195f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.24f, 0.31f, 0.36f, 1f);
        private static readonly Color HighlightColor = new Color(0.24f, 0.46f, 0.49f, 1f);
        private static GameObject _toggleRoot;
        private static GameObject _overlay;
        private static Text _toggleText;
        private static Font _font;
        private static Texture2D _workingTexture;
        private static Texture2D _resetTexture;
        private static RawImage _preview;
        private static Button _addButton;
        private static Button _removeButton;
        private static Text _plotText;
        private static Text _alphaText;
        private static Text _fogText;
        private static Text _generateMapText;
        private static Image _previewBackground;
        private static RawImage _checkerLayer;
        private static Text _sizeText;
        private static Text _pressureText;
        private static Level _level;
        private static GameObject _plotLayerRoot;
        private static RawImage _generatedMapLayer;
        private static Texture2D _generatedMapTexture;
        private static RectTransform _brushCursor;
        private static Texture2D _brushCursorTexture;
        private static Texture2D _checkerTexture;
        private static Texture2D _alphaPreviewTexture;
        private static int _paintDirection = 1; // +1 Anadir, -1 Eliminar
        private static int _brushSize = 10;
        private static int _brushMaxOpacity = 100;
        private static Color32[] _strokePixels;
        private static Color32[] _strokeOriginalPixels;
        private static float[] _strokeInfluence;
        private static bool _strokeDirty;
        private static bool _plotsEnabled;
        private static bool _alphaEnabled = true;
        private static bool _fogEnabled = true;
        private static bool _workingTextureInstalled;

        internal static bool IsOpen => _overlay != null;
        internal static int BrushSizeForStroke => _brushSize;
        internal static int BrushTextureWidth => _workingTexture != null ? _workingTexture.width : 128;
        internal static int BrushTextureHeight => _workingTexture != null ? _workingTexture.height : 128;
        private static string T(string key) => EditorLocalization.Get(key);

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            Detach();
            if (menu == null || level == null) return;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _level = level;
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var button = CreateButton(canvas.transform, "UP_FogViewerToggle",
                T("editor.fog.button"), new Vector2(208f, 40f), HighlightColor, Toggle);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(220f, -12f);
            _toggleRoot = button;
            _toggleText = button.GetComponentInChildren<Text>();
        }

        private static void Toggle()
        {
            if (_overlay != null) { Close(); return; }
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning) return;
            Open();
        }

        // Abrir el PNG directamente desde la carpeta del mapa actual.
        // No depender del lector completo (.upmap + parcelas) ni de que la
        // cabecera tenga fogOfWarFile: el archivo RGBA es la fuente de verdad.
        private static bool TryReadSavedFog(out ScenarioMapFormat.Map header, out byte[] png)
        {
            header = null;
            png = null;
            if (_level == null) return false;
            var name = ScenarioEditorStorage.NameForSave(_level);
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var safe = SafeName(name);
                var architectMethod = AccessTools.Method(typeof(ScenarioEditorStorage),
                    "CurrentArchitect");
                var architect = architectMethod == null ? null :
                    architectMethod.Invoke(null, new object[] { _level }) as string;
                if (string.IsNullOrWhiteSpace(architect))
                    architect = UnderPressureEditorPlugin.ArchitectName;
                var root = ScenarioEditorStorage.Folder;
                var folder = Path.Combine(root, SafeName(architect), safe);
                var pngPath = Path.Combine(folder, "fog_of_war.png");
                var main = Path.Combine(folder, safe + ".upmap");
                // Migraciones antiguas: buscar unicamente el mapa homonimo.
                if (!File.Exists(pngPath) && Directory.Exists(root))
                {
                    var matches = Directory.GetFiles(root, safe + ".upmap",
                        SearchOption.AllDirectories);
                    if (matches.Length == 1)
                    {
                        main = matches[0];
                        folder = Path.GetDirectoryName(main);
                        pngPath = Path.Combine(folder, "fog_of_war.png");
                    }
                }
                if (!File.Exists(pngPath))
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Fog de disco no encontrado: " + pngPath);
                    return false;
                }
                var info = new FileInfo(pngPath);
                if (info.Length < 24 || info.Length > 32 * 1024 * 1024)
                    return false;
                // La cabecera solo aporta parámetros para aplicar la textura
                // al mundo. Para mostrarla no es necesario cargar parcelas.
                ScenarioMapFormat.Map saved = null;
                if (File.Exists(main) && new FileInfo(main).Length <= 1024 * 1024)
                {
                    try { saved = JsonUtility.FromJson<ScenarioMapFormat.Map>(
                        File.ReadAllText(main)); }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Cabecera Fog incompleta; se muestra el PNG: " +
                            exception.Message);
                    }
                }
                png = File.ReadAllBytes(pngPath);
                if (saved == null) saved = new ScenarioMapFormat.Map();
                // La longitud del PNG no basta para determinar la resolución:
                // DecodePng comprobará la imagen real después de LoadImage.
                if (saved.fogTileWidth <= 0 || saved.fogTileHeight <= 0)
                {
                    saved.fogTileWidth = 0;
                    saved.fogTileHeight = 0;
                }
                if (string.IsNullOrEmpty(saved.fogOfWarFile))
                    saved.fogOfWarFile = "fog_of_war.png";
                header = saved;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Fog de disco: " + pngPath + " (" + png.Length + " bytes)");
                return true;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Fog: no se pudo leer el ultimo PNG guardado: " + exception);
                return false;
            }
        }

        private static string SafeName(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            value = value.Trim(' ', '.').Replace(' ', '_');
            if (value.Length == 0) throw new ArgumentException("Nombre de mapa vacio.");
            return value;
        }

        // Las texturas leidas con LoadImage son legibles: copiamos sus bytes
        // directamente, sin pasar por Graphics.Blit ni reinterpretar RGB/alfa.
        private static Texture2D CloneExact(Texture source, string name)
        {
            if (source == null) return null;
            var readable = source as Texture2D;
            if (readable != null)
            {
                try
                {
                    var pixels = readable.GetPixels32();
                    var copy = new Texture2D(readable.width, readable.height,
                        TextureFormat.RGBA32, false, true)
                    {
                        name = name,
                        hideFlags = HideFlags.DontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    copy.SetPixels32(pixels);
                    copy.Apply(false, false);
                    return copy;
                }
                catch (UnityException)
                {
                    // Las texturas nativas pueden ser no legibles.
                }
            }
            return ScenarioEditorFogOfWar.CloneForEditing(source, name);
        }

        private static Texture2D DecodePng(byte[] png, ScenarioMapFormat.Map header,
            string name)
        {
            if (png == null || png.Length < 24)
                throw new InvalidDataException("PNG Fog vacio.");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                if (!ImageConversion.LoadImage(tex, png, false))
                    throw new InvalidDataException("No se pudo decodificar el PNG Fog.");
                if (header.fogTileWidth > 0 && header.fogTileHeight > 0 &&
                    (tex.width != header.fogTileWidth ||
                     tex.height != header.fogTileHeight))
                    throw new InvalidDataException("PNG Fog y cabecera tienen tamanos diferentes.");
                if (header.fogTileWidth == 0) header.fogTileWidth = tex.width;
                if (header.fogTileHeight == 0) header.fogTileHeight = tex.height;
                return tex;
            }
            catch
            {
                UnityEngine.Object.Destroy(tex);
                throw;
            }
        }

        private static void Open()
        {
            if (_toggleRoot == null || _overlay != null) return;
            var canvas = _toggleRoot.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            ScenarioCharacterViewer.Close();
            ScenarioEditorTerrainPainter.DisableTools();
            try
            {
                _paintDirection = 1;
                _brushSize = 10;
                _brushMaxOpacity = 100;
                _plotsEnabled = false;
                _alphaEnabled = true;
                _fogEnabled = true;
                _workingTextureInstalled = false;

                // La visualizacion no debe depender de que el postprocesado
                // del juego acepte la definicion: el PNG es la fuente de verdad.
                ScenarioMapFormat.Map savedHeader;
                byte[] savedPng;
                if (TryReadSavedFog(out savedHeader, out savedPng))
                {
                    _workingTexture = DecodePng(savedPng, savedHeader,
                        "UP_FogOfWarWorking");
                    _resetTexture = DecodePng(savedPng, savedHeader,
                        "UP_FogOfWarReset");
                    try
                    {
                        if (savedHeader.fogMaxDistance > 0)
                            ScenarioEditorFogOfWar.ApplyLoaded(savedHeader, savedPng);
                    }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Fog of War: PNG visible pero no aplicado al renderer: " +
                            exception.Message);
                    }
                }
                else
                {
                    var definition = ScenarioEditorFogOfWar.CurrentDefinition;
                    if (definition?.FogOfWarTexture != null)
                    {
                        _workingTexture = CloneExact(definition.FogOfWarTexture,
                            "UP_FogOfWarWorking");
                        _resetTexture = CloneExact(definition.FogOfWarTexture,
                            "UP_FogOfWarReset");
                    }
                    else
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Fog of War: no se encontro PNG del mapa actual ni mascara nativa.");
                    }
                }

                _overlay = Ui("UP_FogViewerOverlay", canvas.transform, typeof(Image));
                Stretch((RectTransform)_overlay.transform);
                _overlay.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.035f, 0.64f);
                _overlay.GetComponent<Image>().raycastTarget = true;
                _overlay.transform.SetAsLastSibling();

                var panel = Ui("UP_FogViewerPanel", _overlay.transform, typeof(Image));
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.anchoredPosition = Vector2.zero;
                panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
                panel.GetComponent<Image>().color = PanelColor;

                _addButton = CreateButton(panel.transform, "Add", T("editor.fog.add"),
                    new Vector2(112f, 42f), HighlightColor,
                    () => SelectPaintMode(1)).GetComponent<Button>();
                SetTop(_addButton.transform, -358f, -16f);

                _sizeText = CreateText(panel.transform, "", 14,
                    new Vector2(-112f, -3f), new Vector2(145f, 20f), TextAnchor.MiddleCenter);
                var size = CreateSlider(panel.transform, "BrushSize", new Vector2(145f, 20f),
                    1f, 50f, _brushSize, Slider.Direction.LeftToRight, value =>
                    {
                        _brushSize = Mathf.RoundToInt(value);
                        RefreshControlTexts();
                        RefreshBrushCursorSize();
                    });
                SetTop(size.transform, -112f, -31f);

                _pressureText = CreateText(panel.transform, "", 14,
                    new Vector2(112f, -3f), new Vector2(145f, 20f), TextAnchor.MiddleCenter);
                var maxOpacity = CreateSlider(panel.transform, "BrushMaxOpacity", new Vector2(145f, 20f),
                    0f, 100f, _brushMaxOpacity, Slider.Direction.LeftToRight, value =>
                    {
                        _brushMaxOpacity = Mathf.RoundToInt(value);
                        RefreshControlTexts();
                    });
                SetTop(maxOpacity.transform, 112f, -31f);

                _removeButton = CreateButton(panel.transform, "Remove", T("editor.fog.remove"),
                    new Vector2(112f, 42f), ButtonColor,
                    () => SelectPaintMode(-1)).GetComponent<Button>();
                SetTop(_removeButton.transform, 358f, -16f);

                var previewBack = Ui("PreviewBackground", panel.transform, typeof(Image));
                var backRect = (RectTransform)previewBack.transform;
                backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 1f);
                backRect.pivot = new Vector2(0.5f, 1f);
                backRect.anchoredPosition = new Vector2(0f, -63f);
                backRect.sizeDelta = new Vector2(PreviewSize, PreviewSize);
                // El PNG tiene RGB negro y el alfa contiene la mascara.
                // Sobre blanco, el centro transparente parecia blanco: no es
                // el aspecto del PNG original. Fondo de cuadros OSCURO separado.
                _previewBackground = previewBack.GetComponent<Image>();
                _previewBackground.color = Color.black;
                _checkerTexture = BuildCheckerTexture();
                var checkerLayer = Ui("AlphaCheckerboard", previewBack.transform,
                    typeof(RawImage));
                Stretch((RectTransform)checkerLayer.transform);
                var checkerImage = checkerLayer.GetComponent<RawImage>();
                checkerImage.texture = _checkerTexture;
                _checkerLayer = checkerImage;
                checkerImage.raycastTarget = false;
                CreateGeneratedMapLayer(previewBack.transform);
                CreatePlotLayers(previewBack.transform);
                if (_workingTexture == null)
                    CreateText(previewBack.transform,
                        "No se ha encontrado fog_of_war.png para este mapa", 16,
                        new Vector2(0f, -20f), new Vector2(480f, 32f),
                        TextAnchor.MiddleCenter);

                if (_workingTexture != null)
                {
                    var previewRoot = Ui("FogMask", previewBack.transform, typeof(RawImage));
                    Stretch((RectTransform)previewRoot.transform);
                    _preview = previewRoot.GetComponent<RawImage>();
                    _preview.raycastTarget = true;
                    _preview.texture = _workingTexture;
                    _preview.color = Color.white;
                    var paintSurface = previewRoot.AddComponent<FogMaskPaintSurface>();
                    paintSurface.Initialise(PaintAt, ShowBrushCursor, HideBrushCursor,
                        BeginStroke, FlushStroke, EndStroke);
                    CreateBrushCursor(previewRoot.transform);
                }

                var reset = CreateButton(panel.transform, "Reset", T("editor.fog.reset"),
                    new Vector2(150f, 42f), HighlightColor, ResetToLastSaved);
                SetTop(reset.transform, -320f, -847f);

                var alpha = CreateButton(panel.transform, "Alpha", "",
                    new Vector2(150f, 42f), ButtonColor, ToggleAlpha);
                SetTop(alpha.transform, -160f, -847f);
                _alphaText = alpha.GetComponentInChildren<Text>();

                var fog = CreateButton(panel.transform, "Fog", "",
                    new Vector2(150f, 42f), ButtonColor, ToggleFog);
                SetTop(fog.transform, 0f, -847f);
                _fogText = fog.GetComponentInChildren<Text>();

                var plots = CreateButton(panel.transform, "Plots", "",
                    new Vector2(150f, 42f), ButtonColor, TogglePlots);
                SetTop(plots.transform, 160f, -847f);
                _plotText = plots.GetComponentInChildren<Text>();

                var generateMap = CreateButton(panel.transform, "GenerateMap",
                    T("editor.fog.generate_map"), new Vector2(150f, 42f),
                    HighlightColor, GenerateMap);
                SetTop(generateMap.transform, 320f, -847f);
                _generateMapText = generateMap.GetComponentInChildren<Text>();
                RefreshAlphaBackground();
                RefreshControlTexts();
                _toggleRoot.transform.SetAsLastSibling();
                if (_toggleText != null) _toggleText.text = T("editor.fog.close");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Fog of War: no se pudo abrir el editor: " + exception);
                Close();
            }
        }

        // Los botones no crean pinceles distintos: seleccionan solo el signo.
        private static void SelectPaintMode(int direction)
        {
            _paintDirection = direction;
            if (_addButton != null)
                _addButton.GetComponent<Image>().color = direction > 0
                    ? HighlightColor : ButtonColor;
            if (_removeButton != null)
                _removeButton.GetComponent<Image>().color = direction < 0
                    ? HighlightColor : ButtonColor;
        }

        private static void ResetToLastSaved()
        {
            if (_level == null) return;
            ScenarioMapFormat.Map header;
            byte[] png;
            try
            {
                Texture2D source = null;
                if (TryReadSavedFog(out header, out png))
                {
                    source = DecodePng(png, header, "UP_FogOfWarRestored");
                    try
                    {
                        if (header.fogMaxDistance > 0)
                            ScenarioEditorFogOfWar.ApplyLoaded(header, png);
                    }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Fog de disco restaurado en visor pero no en renderer: " +
                            exception.Message);
                    }
                }
                else if (_resetTexture != null)
                {
                    source = CloneExact(_resetTexture, "UP_FogOfWarRestored");
                    try
                    {
                        ScenarioEditorFogOfWar.ApplyEditedTexture(
                            CloneExact(source, "UP_FogOfWarApplied"));
                    }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Fog restaurado en visor pero no en renderer: " +
                            exception.Message);
                    }
                }
                if (source == null) return;
                if (_workingTexture != null && !_workingTextureInstalled)
                    UnityEngine.Object.Destroy(_workingTexture);
                _workingTexture = source;
                _workingTextureInstalled = false;
                _strokePixels = null;
                _strokeOriginalPixels = null;
                _strokeInfluence = null;
                _strokeDirty = false;
                RefreshBrushCursorSize();
                if (_resetTexture != null) UnityEngine.Object.Destroy(_resetTexture);
                _resetTexture = CloneExact(source, "UP_FogOfWarReset");
                RebuildPreview();
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Fog of War: error al reiniciar: " + exception.Message);
            }
        }

        private static void TogglePlots()
        {
            _plotsEnabled = !_plotsEnabled;
            if (_plotLayerRoot != null) _plotLayerRoot.SetActive(_plotsEnabled);
            RefreshControlTexts();
            RebuildPreview();
        }

        private static void GenerateMap()
        {
            if (_generateMapText != null)
                _generateMapText.text = T("editor.fog.generate_map.working");
            EditorMapCapture.Capture(path =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    if (_generateMapText != null)
                        _generateMapText.text = T("editor.fog.generate_map.error");
                    return;
                }
                LoadGeneratedMap(path);
                if (_generateMapText != null)
                    _generateMapText.text = T("editor.fog.generate_map.saved");
            });
        }

        private static void ToggleFog()
        {
            _fogEnabled = !_fogEnabled;
            if (_preview != null) _preview.gameObject.SetActive(_fogEnabled);
            if (!_fogEnabled) HideBrushCursor();
            RefreshControlTexts();
        }

        // Alpha ON muestra el canal alfa directamente en escala de grises.
        // Alpha OFF muestra el RGBA real sobre damero.
        // Ninguno de los modos modifica los pixeles de trabajo.
        private static void ToggleAlpha()
        {
            _alphaEnabled = !_alphaEnabled;
            RefreshAlphaBackground();
            RefreshControlTexts();
        }

        private static void RefreshAlphaBackground()
        {
            if (_previewBackground != null)
                _previewBackground.color = Color.black;
            if (_checkerLayer != null)
                _checkerLayer.gameObject.SetActive(!_alphaEnabled);
            RebuildPreview();
        }

        private static void RefreshControlTexts()
        {
            if (_alphaText != null)
                _alphaText.text = T(_alphaEnabled
                    ? "editor.fog.alpha.on" : "editor.fog.alpha.off");
            if (_fogText != null)
                _fogText.text = T(_fogEnabled
                    ? "editor.fog.fog.on" : "editor.fog.fog.off");
            if (_plotText != null)
                _plotText.text = T(_plotsEnabled
                    ? "editor.fog.map.on" : "editor.fog.map.off");
            if (_sizeText != null)
                _sizeText.text = EditorLocalization.Format("editor.fog.size", _brushSize);
            if (_pressureText != null)
                _pressureText.text = "Opacidad máxima: " + _brushMaxOpacity + "%";
        }

        // PINCEL UNIVERSAL: exactamente el mismo sello, perfil y
        // acumulador para ambos botones. Solo cambia _paintDirection.
        // La influencia maxima se memoriza por pixel durante cada trazo:
        // pasar varias veces por el mismo punto no acumula opacidad.
        private static void BeginStroke()
        {
            if (_workingTexture == null) return;
            _strokeOriginalPixels = _workingTexture.GetPixels32();
            _strokePixels = (Color32[])_strokeOriginalPixels.Clone();
            _strokeInfluence = new float[_strokePixels.Length];
            _strokeDirty = false;
        }

        private static void EndStroke()
        {
            FlushStroke();
            _strokeOriginalPixels = null;
            _strokePixels = null;
            _strokeInfluence = null;
            _strokeDirty = false;
        }

        private static void PaintAt(Vector2 normalized)
        {
            if (_workingTexture == null || _overlay == null ||
                !ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning ||
                !_overlay.activeInHierarchy || _brushMaxOpacity <= 0) return;
            if (_strokePixels == null) BeginStroke();
            if (_strokePixels == null || _strokeOriginalPixels == null ||
                _strokeInfluence == null) return;

            var width = _workingTexture.width;
            var height = _workingTexture.height;
            var centerX = Mathf.Clamp01(normalized.x) * width - 0.5f;
            var centerY = Mathf.Clamp01(normalized.y) * height - 0.5f;
            var radius = BrushRadiusPixels();
            var minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radius));
            var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centerX + radius));
            var minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radius));
            var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centerY + radius));
            var maximumStrength = _brushMaxOpacity / 100f;
            var invRadius = 1f / Mathf.Max(0.5f, radius);

            for (var y = minY; y <= maxY; ++y)
            {
                var dy = y - centerY;
                for (var x = minX; x <= maxX; ++x)
                {
                    var dx = x - centerX;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy) * invRadius;
                    if (distance >= 1f) continue;

                    // Todo el circulo pinta: 100 % en el centro y descenso
                    // continuo y uniforme hasta 0 % exactamente en el borde.
                    var influence = 1f - distance;
                    var index = y * width + x;
                    if (influence <= _strokeInfluence[index]) continue;
                    _strokeInfluence[index] = influence;

                    // Eliminar y Anadir aplican el mismo delta con signo opuesto.
                    // Asi recorrer de nuevo el mismo trazo lo restaura con el
                    // mismo grosor, incluido todo el borde difuminado.
                    var delta = 255f * maximumStrength * influence;
                    var originalAlpha = (int)_strokeOriginalPixels[index].a;
                    var alpha = Mathf.RoundToInt(Mathf.Clamp(
                        originalAlpha + _paintDirection * delta, 0f, 255f));
                    if (alpha == _strokePixels[index].a) continue;
                    _strokePixels[index].a = (byte)alpha;
                    _strokeDirty = true;
                }
            }
        }

        private static float BrushRadiusPixels()
        {
            if (_workingTexture == null) return 0.5f;
            return Mathf.Max(0.5f, Mathf.Min(_workingTexture.width,
                _workingTexture.height) * (_brushSize / 100f) * 0.5f);
        }

        // Una unica subida a GPU por evento del raton. Antes se aplicaba
        // una textura completa por cada sello interpolado del arrastre.
        private static void FlushStroke()
        {
            if (!_strokeDirty || _strokePixels == null || _workingTexture == null) return;
            _workingTexture.SetPixels32(_strokePixels);
            _workingTexture.Apply(false, false);
            _strokeDirty = false;
            if (!_workingTextureInstalled)
            {
                try
                {
                    ScenarioEditorFogOfWar.ApplyEditedTexture(_workingTexture);
                    _workingTextureInstalled = true;
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Fog of War: mascara visible, pendiente de renderer: " +
                        exception.Message);
                }
            }
            RebuildPreview();
        }

        private static void RebuildPreview()
        {
            if (_workingTexture == null || _preview == null) return;
            if (_alphaEnabled)
            {
                if (_alphaPreviewTexture == null ||
                    _alphaPreviewTexture.width != _workingTexture.width ||
                    _alphaPreviewTexture.height != _workingTexture.height)
                {
                    if (_alphaPreviewTexture != null)
                        UnityEngine.Object.Destroy(_alphaPreviewTexture);
                    _alphaPreviewTexture = new Texture2D(
                        _workingTexture.width, _workingTexture.height,
                        TextureFormat.RGBA32, false, false)
                    {
                        name = "UP_FogAlphaChannelPreview",
                        hideFlags = HideFlags.DontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                }

                var source = _workingTexture.GetPixels32();
                var previewPixels = new Color32[source.Length];
                for (var i = 0; i < source.Length; ++i)
                {
                    var alpha = source[i].a;
                    previewPixels[i] = new Color32(alpha, alpha, alpha, 255);
                }
                _alphaPreviewTexture.SetPixels32(previewPixels);
                _alphaPreviewTexture.Apply(false, false);
                _preview.texture = _alphaPreviewTexture;
            }
            else
            {
                _preview.texture = _workingTexture;
            }
            _preview.color = Color.white;
        }

        private static Texture2D BuildCheckerTexture()
        {
            const int size = 256;
            const int cell = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "UP_FogAlphaPreviewBackground",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var colors = new Color32[size * size];
            var dark = new Color32(38, 38, 38, 255);
            var light = new Color32(77, 77, 77, 255);
            for (var y = 0; y < size; ++y)
                for (var x = 0; x < size; ++x)
                    colors[y * size + x] = ((x / cell + y / cell) & 1) == 0
                        ? dark : light;
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            return texture;
        }

        private static void CreateGeneratedMapLayer(Transform parent)
        {
            var layer = Ui("GeneratedMap", parent, typeof(RawImage));
            Stretch((RectTransform)layer.transform);
            _generatedMapLayer = layer.GetComponent<RawImage>();
            _generatedMapLayer.color = Color.white;
            _generatedMapLayer.raycastTarget = false;
            LoadGeneratedMap(EditorMapCapture.GetMapImagePath(_level));
        }

        private static void LoadGeneratedMap(string path)
        {
            if (_generatedMapTexture != null)
                UnityEngine.Object.Destroy(_generatedMapTexture);
            _generatedMapTexture = null;
            if (_generatedMapLayer != null)
                _generatedMapLayer.texture = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            Texture2D texture = null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0 || bytes.Length > 32 * 1024 * 1024)
                    throw new InvalidDataException("JPG de mapa vacio o demasiado grande.");
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false, true)
                {
                    name = "UP_GeneratedMap",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(texture, bytes, false))
                    throw new InvalidDataException("No se pudo decodificar el JPG del mapa.");
                _generatedMapTexture = texture;
                if (_generatedMapLayer != null)
                    _generatedMapLayer.texture = texture;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Mapa cenital cargado en el editor de niebla: " + path);
            }
            catch (Exception exception)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "No se pudo cargar el mapa cenital: " + exception.Message);
            }
        }

        private static void CreatePlotLayers(Transform parent)
        {
            _plotLayerRoot = Ui("PlotLayers", parent);
            Stretch((RectTransform)_plotLayerRoot.transform);
            _plotLayerRoot.SetActive(_plotsEnabled);
            var plots = _level?.WorldState?.HospitalPlots;
            if (plots == null || _workingTexture == null) return;
            var count = 0;
            foreach (var plot in plots)
            {
                var texture = plot?.Definition?.FloorImage;
                if (texture == null) continue;
                if (texture.width != _workingTexture.width || texture.height != _workingTexture.height)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Fog of War: se omite una parcela con dimensiones " +
                        texture.width + "x" + texture.height + " frente a " +
                        _workingTexture.width + "x" + _workingTexture.height + ".");
                    continue;
                }
                var layer = Ui("Plot_" + count++, _plotLayerRoot.transform, typeof(RawImage));
                Stretch((RectTransform)layer.transform);
                var image = layer.GetComponent<RawImage>();
                image.texture = texture;
                image.color = Color.white;
                image.raycastTarget = false;
            }
        }

        private static void CreateBrushCursor(Transform parent)
        {
            _brushCursorTexture = BuildBrushCursorTexture();
            var cursor = Ui("BrushCursor", parent, typeof(RawImage));
            _brushCursor = (RectTransform)cursor.transform;
            _brushCursor.anchorMin = _brushCursor.anchorMax = new Vector2(0.5f, 0.5f);
            _brushCursor.pivot = new Vector2(0.5f, 0.5f);
            var image = cursor.GetComponent<RawImage>();
            image.texture = _brushCursorTexture;
            image.color = Color.white;
            image.raycastTarget = false;
            RefreshBrushCursorSize();
            cursor.SetActive(false);
        }

        private static Texture2D BuildBrushCursorTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "UP_FogBrushCursor",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; ++y)
            {
                for (var x = 0; x < size; ++x)
                {
                    var distance = Mathf.Sqrt((x - center) * (x - center) +
                                              (y - center) * (y - center));
                    var edge = Mathf.Abs(distance - (center - 0.75f));
                    var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(1.5f - edge) * 255f);
                    pixels[y * size + x] = new Color32(40, 225, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void ShowBrushCursor(Vector2 normalized)
        {
            if (_brushCursor == null) return;
            _brushCursor.gameObject.SetActive(true);
            RefreshBrushCursorSize();
            var parent = _brushCursor.parent as RectTransform;
            if (parent == null) return;
            _brushCursor.anchoredPosition = new Vector2(
                (normalized.x - 0.5f) * parent.rect.width,
                (normalized.y - 0.5f) * parent.rect.height);
        }

        private static void HideBrushCursor()
        {
            if (_brushCursor != null) _brushCursor.gameObject.SetActive(false);
        }

        private static void RefreshBrushCursorSize()
        {
            if (_brushCursor == null) return;
            var parent = _brushCursor.parent as RectTransform;
            if (parent == null) return;
            if (_workingTexture == null) return;
            var diameterInPixels = 2f * BrushRadiusPixels();
            // Las escalas horizontal y vertical son independientes: el mismo
            // círculo en pixeles debe cubrir el mismo área que PaintAt.
            var cursorWidth = diameterInPixels * parent.rect.width / _workingTexture.width;
            var cursorHeight = diameterInPixels * parent.rect.height / _workingTexture.height;
            var expectedSize = new Vector2(cursorWidth, cursorHeight);
            if ((_brushCursor.sizeDelta - expectedSize).sqrMagnitude > 0.0001f)
                _brushCursor.sizeDelta = expectedSize;
        }

        internal static void Close()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            if (_resetTexture != null) UnityEngine.Object.Destroy(_resetTexture);
            _resetTexture = null;
            if (_brushCursorTexture != null) UnityEngine.Object.Destroy(_brushCursorTexture);
            _brushCursorTexture = null;
            if (_checkerTexture != null) UnityEngine.Object.Destroy(_checkerTexture);
            _checkerTexture = null;
            if (_alphaPreviewTexture != null) UnityEngine.Object.Destroy(_alphaPreviewTexture);
            _alphaPreviewTexture = null;
            if (_generatedMapTexture != null) UnityEngine.Object.Destroy(_generatedMapTexture);
            _generatedMapTexture = null;
            _generatedMapLayer = null;
            _brushCursor = null;
            _plotLayerRoot = null;
            _preview = null;
            if (_workingTexture != null && !_workingTextureInstalled)
                UnityEngine.Object.Destroy(_workingTexture);
            _workingTexture = null;
            _workingTextureInstalled = false;
            _strokePixels = null;
            _strokeOriginalPixels = null;
            _strokeInfluence = null;
            _strokeDirty = false;
            _addButton = null;
            _removeButton = null;
            _plotText = null;
            _alphaText = null;
            _fogText = null;
            _generateMapText = null;
            _previewBackground = null;
            _checkerLayer = null;
            _sizeText = null;
            _pressureText = null;
            if (_toggleText != null) _toggleText.text = T("editor.fog.button");
        }

        internal static void Detach()
        {
            Close();
            if (_toggleRoot != null) UnityEngine.Object.Destroy(_toggleRoot);
            _toggleRoot = null;
            _toggleText = null;
            _level = null;
        }

        private static GameObject Ui(string name, Transform parent, params Type[] components)
        {
            var types = new[] { typeof(RectTransform) }.Concat(components).ToArray();
            var result = new GameObject(name, types);
            result.transform.SetParent(parent, false);
            return result;
        }

        private static GameObject CreateButton(Transform parent, string name, string label,
            Vector2 size, Color color, Action action)
        {
            var root = Ui(name, parent, typeof(Image), typeof(Button));
            ((RectTransform)root.transform).sizeDelta = size;
            root.GetComponent<Image>().color = color;
            var button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(() => action());
            CreateText(root.transform, label, 18, Vector2.zero, size, TextAnchor.MiddleCenter);
            return root;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 size,
            float min, float max, float initial, Slider.Direction direction, Action<float> onChange)
        {
            var root = Ui(name, parent, typeof(Image), typeof(Slider));
            ((RectTransform)root.transform).sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0.18f, 0.27f, 0.33f, 1f);
            var fill = Ui("Fill", root.transform, typeof(Image));
            var fillRect = (RectTransform)fill.transform;
            Stretch(fillRect);
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);
            fill.GetComponent<Image>().color = new Color(0.23f, 0.74f, 0.98f, 1f);
            fill.GetComponent<Image>().raycastTarget = false;
            var handle = Ui("Handle", root.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(15f, 30f);
            handle.GetComponent<Image>().color = new Color(0.97f, 0.97f, 0.91f, 1f);
            var slider = root.GetComponent<Slider>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = direction;
            slider.wholeNumbers = true;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            slider.onValueChanged.AddListener(value => onChange?.Invoke(value));
            return slider;
        }

        private static Text CreateText(Transform parent, string value, int size,
            Vector2 position, Vector2 dimensions, TextAnchor alignment)
        {
            var root = Ui("Text", parent, typeof(Text));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            var text = root.GetComponent<Text>();
            text.text = value;
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.96f, 0.97f, 0.98f, 1f);
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = size;
            if (parent.GetComponent<Button>() != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            return text;
        }

        private static void SetTop(Transform transform, float x, float y)
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    internal sealed class FogMaskPaintSurface : MonoBehaviour, IPointerDownHandler,
        IDragHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Action<Vector2> _paint;
        private Action<Vector2> _hover;
        private Action _exit;
        private Action _beginStroke;
        private Action _flushStroke;
        private Action _endStroke;
        private Vector2 _lastPoint;
        private bool _hasLastPoint;
        private bool _pointerInside;
        private Camera _eventCamera;

        internal void Initialise(Action<Vector2> paint, Action<Vector2> hover,
            Action exit, Action beginStroke, Action flushStroke, Action endStroke)
        {
            _paint = paint;
            _hover = hover;
            _exit = exit;
            _beginStroke = beginStroke;
            _flushStroke = flushStroke;
            _endStroke = endStroke;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _hasLastPoint = false;
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                _beginStroke?.Invoke();
                Paint(eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Paint(eventData);
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                _endStroke?.Invoke();
            _hasLastPoint = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            _eventCamera = eventData.enterEventCamera;
            Hover(eventData.position, _eventCamera);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            _eventCamera = null;
            _exit?.Invoke();
        }

        private void Update()
        {
            if (_pointerInside) Hover(Input.mousePosition, _eventCamera);
        }

        private void Paint(PointerEventData eventData)
        {
            var rect = transform as RectTransform;
            Vector2 local;
            if (rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, eventData.position, eventData.pressEventCamera, out local)) return;
            var area = rect.rect;
            var point = new Vector2(
                Mathf.Clamp01((local.x - area.xMin) / area.width),
                Mathf.Clamp01((local.y - area.yMin) / area.height));
            if (_hasLastPoint)
            {
                var distance = Vector2.Distance(_lastPoint, point);
                if (distance <= 0.0001f)
                {
                    _hover?.Invoke(point);
                    return;
                }
                var width = ScenarioFogViewer.BrushTextureWidth;
                var height = ScenarioFogViewer.BrushTextureHeight;
                var diameterPixels = Mathf.Max(1f,
                    Mathf.Min(width, height) * ScenarioFogViewer.BrushSizeForStroke / 100f);
                // Un sello cada medio pixel como maximo elimina los dientes
                // entre muestras incluso en curvas y pinceles pequenos.
                var spacingPixels = Mathf.Max(0.25f,
                    Mathf.Min(0.5f, diameterPixels * 0.05f));
                var distancePixels = Mathf.Sqrt(
                    Mathf.Pow((point.x - _lastPoint.x) * (width - 1f), 2f) +
                    Mathf.Pow((point.y - _lastPoint.y) * (height - 1f), 2f));
                var steps = Mathf.Max(1,
                    Mathf.CeilToInt(distancePixels / spacingPixels));
                for (var i = 1; i <= steps; ++i)
                    _paint?.Invoke(Vector2.Lerp(_lastPoint, point, i / (float)steps));
            }
            else
            {
                _paint?.Invoke(point);
            }
            _lastPoint = point;
            _hasLastPoint = true;
            _flushStroke?.Invoke();
            _hover?.Invoke(point);
        }

        private void Hover(Vector2 screenPosition, Camera eventCamera)
        {
            var rect = transform as RectTransform;
            Vector2 local;
            if (rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, screenPosition, eventCamera, out local)) return;
            var area = rect.rect;
            _hover?.Invoke(new Vector2(
                Mathf.Clamp01((local.x - area.xMin) / area.width),
                Mathf.Clamp01((local.y - area.yMin) / area.height)));
        }
    }
}
