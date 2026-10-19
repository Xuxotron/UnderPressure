using System;
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

    // Editor de la mascara Fog of War. La copia editable sustituye a la activa
    // solo al realizar la primera pincelada; nunca modifica el recurso nativo.
    internal static class ScenarioFogViewer
    {
        private enum PaintMode { Add, Remove }

        private static readonly Color PanelColor = new Color(0.135f, 0.165f, 0.195f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.24f, 0.31f, 0.36f, 1f);
        private static readonly Color HighlightColor = new Color(0.24f, 0.46f, 0.49f, 1f);
        private static GameObject _toggleRoot;
        private static GameObject _overlay;
        private static Text _toggleText;
        private static Font _font;
        private static Texture2D _workingTexture;
        private static Texture2D _previewTexture;
        private static RawImage _preview;
        private static Button _addButton;
        private static Button _removeButton;
        private static Text _alphaText;
        private static Text _mapText;
        private static Text _opacityText;
        private static Text _sizeText;
        private static Text _pressureText;
        private static Level _level;
        private static GameObject _mapLayerRoot;
        private static RectTransform _brushCursor;
        private static Texture2D _brushCursorTexture;
        private static PaintMode _paintMode = PaintMode.Add;
        private static int _brushSize = 10;
        private static int _brushPressure = 100;
        private static float _previewOpacity = 1f;
        private static bool _alphaEnabled = true;
        private static bool _mapEnabled;
        private static bool _workingTextureInstalled;

        internal static bool IsOpen => _overlay != null;

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

        private static void Open()
        {
            if (_toggleRoot == null || _overlay != null) return;
            var definition = ScenarioEditorFogOfWar.CurrentDefinition;
            var canvas = _toggleRoot.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            ScenarioCharacterViewer.Close();
            try
            {
                _paintMode = PaintMode.Add;
                _brushSize = 10;
                _brushPressure = 100;
                _previewOpacity = 1f;
                _alphaEnabled = true;
                _mapEnabled = false;
                _workingTextureInstalled = false;
                if (definition?.FogOfWarTexture != null)
                    _workingTexture = ScenarioEditorFogOfWar.CloneForEditing(
                        definition.FogOfWarTexture, "UP_FogOfWarWorking");

                _overlay = Ui("UP_FogViewerOverlay", canvas.transform, typeof(Image));
                Stretch((RectTransform)_overlay.transform);
                _overlay.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.035f, 0.64f);
                _overlay.transform.SetAsLastSibling();

                var panel = Ui("UP_FogViewerPanel", _overlay.transform, typeof(Image));
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.anchoredPosition = Vector2.zero;
                panelRect.sizeDelta = new Vector2(620f, 700f);
                panel.GetComponent<Image>().color = PanelColor;

                _addButton = CreateButton(panel.transform, "Add", T("editor.fog.add"),
                    new Vector2(112f, 42f), HighlightColor,
                    () => SelectPaintMode(PaintMode.Add)).GetComponent<Button>();
                SetTop(_addButton.transform, -245f, -25f);

                _sizeText = CreateText(panel.transform, "", 14,
                    new Vector2(-78f, -10f), new Vector2(145f, 20f), TextAnchor.MiddleCenter);
                var size = CreateSlider(panel.transform, "BrushSize", new Vector2(145f, 20f),
                    1f, 50f, _brushSize, Slider.Direction.LeftToRight,
                    value =>
                    {
                        _brushSize = Mathf.RoundToInt(value);
                        RefreshControlTexts();
                        RefreshBrushCursorSize();
                    });
                SetTop(size.transform, -78f, -38f);

                _pressureText = CreateText(panel.transform, "", 14,
                    new Vector2(78f, -10f), new Vector2(145f, 20f), TextAnchor.MiddleCenter);
                var pressure = CreateSlider(panel.transform, "BrushPressure", new Vector2(145f, 20f),
                    0f, 100f, _brushPressure, Slider.Direction.LeftToRight,
                    value =>
                    {
                        _brushPressure = Mathf.RoundToInt(value);
                        RefreshControlTexts();
                    });
                SetTop(pressure.transform, 78f, -38f);

                _removeButton = CreateButton(panel.transform, "Remove", T("editor.fog.remove"),
                    new Vector2(112f, 42f), ButtonColor,
                    () => SelectPaintMode(PaintMode.Remove)).GetComponent<Button>();
                SetTop(_removeButton.transform, 245f, -25f);

                var previewBack = Ui("PreviewBackground", panel.transform, typeof(Image));
                var backRect = (RectTransform)previewBack.transform;
                backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 1f);
                backRect.pivot = new Vector2(0.5f, 1f);
                backRect.anchoredPosition = new Vector2(0f, -74f);
                backRect.sizeDelta = new Vector2(520f, 520f);
                previewBack.GetComponent<Image>().color = Color.black;

                CreateMapLayers(previewBack.transform);

                if (_workingTexture != null)
                {
                    var previewRoot = Ui("FogMask", previewBack.transform, typeof(RawImage));
                    Stretch((RectTransform)previewRoot.transform);
                    _preview = previewRoot.GetComponent<RawImage>();
                    _preview.raycastTarget = true;
                    var paintSurface = previewRoot.AddComponent<FogMaskPaintSurface>();
                    paintSurface.Initialise(PaintAt, ShowBrushCursor, HideBrushCursor);
                    CreateBrushCursor(previewRoot.transform);
                    RebuildPreview();
                }

                var alpha = CreateButton(panel.transform, "Alpha", "",
                    new Vector2(132f, 42f), HighlightColor, ToggleAlpha);
                SetTop(alpha.transform, -220f, -626f);
                _alphaText = alpha.GetComponentInChildren<Text>();

                _opacityText = CreateText(panel.transform, "", 14,
                    new Vector2(0f, -602f), new Vector2(250f, 20f), TextAnchor.MiddleCenter);
                var opacity = CreateSlider(panel.transform, "Opacity", new Vector2(250f, 22f),
                    0f, 100f, 100f, Slider.Direction.RightToLeft, value =>
                    {
                        _previewOpacity = value / 100f;
                        RefreshControlTexts();
                        RebuildPreview();
                    });
                SetTop(opacity.transform, 0f, -636f);

                var map = CreateButton(panel.transform, "Map", "",
                    new Vector2(132f, 42f), ButtonColor, ToggleMap);
                SetTop(map.transform, 220f, -626f);
                _mapText = map.GetComponentInChildren<Text>();
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

        private static void SelectPaintMode(PaintMode mode)
        {
            _paintMode = mode;
            if (_addButton != null)
                _addButton.GetComponent<Image>().color = mode == PaintMode.Add
                    ? HighlightColor : ButtonColor;
            if (_removeButton != null)
                _removeButton.GetComponent<Image>().color = mode == PaintMode.Remove
                    ? HighlightColor : ButtonColor;
        }

        private static void ToggleAlpha()
        {
            _alphaEnabled = !_alphaEnabled;
            RefreshControlTexts();
            RebuildPreview();
        }

        private static void ToggleMap()
        {
            _mapEnabled = !_mapEnabled;
            if (_mapLayerRoot != null) _mapLayerRoot.SetActive(_mapEnabled);
            RefreshControlTexts();
        }

        private static void RefreshControlTexts()
        {
            if (_alphaText != null)
                _alphaText.text = T(_alphaEnabled ? "editor.fog.alpha.on" : "editor.fog.alpha.off");
            if (_mapText != null)
                _mapText.text = T(_mapEnabled ? "editor.fog.map.on" : "editor.fog.map.off");
            if (_opacityText != null)
                _opacityText.text = EditorLocalization.Format("editor.fog.opacity",
                    Mathf.RoundToInt(_previewOpacity * 100f));
            if (_sizeText != null)
                _sizeText.text = EditorLocalization.Format("editor.fog.size", _brushSize);
            if (_pressureText != null)
                _pressureText.text = EditorLocalization.Format("editor.fog.pressure", _brushPressure);
        }

        private static void PaintAt(Vector2 normalized)
        {
            if (_workingTexture == null) return;
            var width = _workingTexture.width;
            var height = _workingTexture.height;
            var centerX = Mathf.RoundToInt(normalized.x * (width - 1));
            var centerY = Mathf.RoundToInt(normalized.y * (height - 1));
            var radius = Mathf.Max(0.5f, Mathf.Min(width, height) * (_brushSize / 100f) * 0.5f);
            var minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radius));
            var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centerX + radius));
            var minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radius));
            var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centerY + radius));
            var pixels = _workingTexture.GetPixels32();
            var pressure = _brushPressure / 100f;
            if (pressure <= 0f) return;
            for (var y = minY; y <= maxY; ++y)
            {
                for (var x = minX; x <= maxX; ++x)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var normalizedDistance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    if (normalizedDistance > 1f) continue;
                    var softness = Mathf.SmoothStep(0f, 1f, 1f - normalizedDistance);
                    var index = y * width + x;
                    var delta = 255f * pressure * softness;
                    var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp(
                        pixels[index].a + (_paintMode == PaintMode.Add ? -delta : delta),
                        0f, 255f));
                    pixels[index] = new Color32(0, 0, 0, alpha);
                }
            }
            _workingTexture.SetPixels32(pixels);
            _workingTexture.Apply(false, false);
            if (!_workingTextureInstalled)
            {
                ScenarioEditorFogOfWar.ApplyEditedTexture(_workingTexture);
                _workingTextureInstalled = true;
            }
            RebuildPreview();
        }

        private static void RebuildPreview()
        {
            if (_workingTexture == null || _preview == null) return;
            if (_previewTexture == null || _previewTexture.width != _workingTexture.width ||
                _previewTexture.height != _workingTexture.height)
            {
                if (_previewTexture != null) UnityEngine.Object.Destroy(_previewTexture);
                _previewTexture = new Texture2D(_workingTexture.width, _workingTexture.height,
                    TextureFormat.RGBA32, false, true)
                {
                    name = "UP_FogOfWarPreview",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _preview.texture = _previewTexture;
                _preview.color = Color.white;
            }

            var source = _workingTexture.GetPixels32();
            var preview = new Color32[source.Length];
            var globalAlpha = (byte)Mathf.RoundToInt(_previewOpacity * 255f);
            for (var i = 0; i < source.Length; ++i)
            {
                // En el editor blanco representa zona añadida (sin niebla) y
                // negro zona eliminada (niebla completa).
                var value = (byte)(255 - source[i].a);
                var alpha = _alphaEnabled
                    ? (byte)Mathf.RoundToInt(source[i].a * _previewOpacity)
                    : globalAlpha;
                preview[i] = new Color32(value, value, value, alpha);
            }
            _previewTexture.SetPixels32(preview);
            _previewTexture.Apply(false, false);
        }

        private static void CreateMapLayers(Transform parent)
        {
            _mapLayerRoot = Ui("MapLayers", parent);
            Stretch((RectTransform)_mapLayerRoot.transform);
            _mapLayerRoot.SetActive(_mapEnabled);
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
                var layer = Ui("Plot_" + count++, _mapLayerRoot.transform, typeof(RawImage));
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
                    var alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(1.5f - edge) * 255f);
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
            var sourceSize = _workingTexture == null ? 1f :
                Mathf.Min(_workingTexture.width, _workingTexture.height);
            var radius = Mathf.Max(0.5f, sourceSize * (_brushSize / 100f) * 0.5f);
            var diameter = Mathf.Min(parent.rect.width, parent.rect.height) *
                           (radius * 2f / sourceSize);
            _brushCursor.sizeDelta = new Vector2(diameter, diameter);
        }

        internal static void Close()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            if (_previewTexture != null) UnityEngine.Object.Destroy(_previewTexture);
            _previewTexture = null;
            if (_brushCursorTexture != null) UnityEngine.Object.Destroy(_brushCursorTexture);
            _brushCursorTexture = null;
            _brushCursor = null;
            _mapLayerRoot = null;
            _preview = null;
            if (_workingTexture != null && !_workingTextureInstalled)
                UnityEngine.Object.Destroy(_workingTexture);
            _workingTexture = null;
            _workingTextureInstalled = false;
            _addButton = null;
            _removeButton = null;
            _alphaText = null;
            _mapText = null;
            _opacityText = null;
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
        private Vector2 _lastPoint;
        private bool _hasLastPoint;
        private bool _pointerInside;
        private Camera _eventCamera;

        internal void Initialise(Action<Vector2> paint, Action<Vector2> hover, Action exit)
        {
            _paint = paint;
            _hover = hover;
            _exit = exit;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _hasLastPoint = false;
            Paint(eventData);
        }

        public void OnDrag(PointerEventData eventData) => Paint(eventData);

        public void OnPointerUp(PointerEventData eventData) => _hasLastPoint = false;

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
                var steps = Mathf.Max(1, Mathf.CeilToInt(distance * 256f));
                for (var i = 1; i <= steps; ++i)
                    _paint?.Invoke(Vector2.Lerp(_lastPoint, point, i / (float)steps));
            }
            else
            {
                _paint?.Invoke(point);
            }
            _lastPoint = point;
            _hasLastPoint = true;
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
