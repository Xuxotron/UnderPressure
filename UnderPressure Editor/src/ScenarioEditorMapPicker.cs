// Updated: 2026-10-12
// Selector de mapas del editor con dos columnas y estilo integrado en el menu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal static class ScenarioEditorMapPicker
    {
        private sealed class StoredMap
        {
            internal string FilePath;
            internal string DisplayName;
        }

        private sealed class HospitalChoice
        {
            internal LevelConfig Level;
            internal string DisplayName;
        }

        private sealed class UiStyle
        {
            internal Sprite ButtonSprite;
            internal Material ButtonMaterial;
            internal Image.Type ButtonType = Image.Type.Sliced;
            internal float PixelsPerUnitMultiplier = 1f;
            internal Font LegacyFont;
            internal TMP_FontAsset TmpFont;
            internal int FontSize = 18;
            internal Color TextColor = Color.white;
            internal Color PanelColor = new Color(0.19f, 0.24f, 0.29f, 0.96f);
            internal Color OverlayColor = new Color(0f, 0f, 0f, 0.45f);
            internal Color HeaderColor = Color.white;
            internal Color NewMapColor = new Color(0.28f, 0.86f, 0.24f, 1f);
            internal Color SelectedColor = new Color(0.93f, 0.73f, 0.17f, 1f);
        }

        private static readonly FieldInfo SelectorField =
            AccessTools.Field(typeof(ScenarioEditorFlow), "_selectorRoot");
        private static readonly MethodInfo OriginalStart =
            AccessTools.Method(typeof(ScenarioEditorFlow), "StartSelectedLevel", new[] { typeof(LevelConfig) });

        private static bool _bypass;
        private static string _pendingMap;
        private static GameObject _overlay;
        private static RectTransform _overlayRect;
        private static RectTransform _leftContent;
        private static RectTransform _rightContent;
        private static TMP_Text _rightTitleTmp;
        private static Text _rightTitleLegacy;
        private static ScrollRect _leftScroll;
        private static ScrollRect _rightScroll;
        private static LevelConfig _selectedLevel;
        private static readonly List<HospitalChoice> _hospitals = new List<HospitalChoice>();
        private static readonly List<Button> _hospitalButtons = new List<Button>();
        private static readonly List<Button> _mapButtons = new List<Button>();
        private static UiStyle _style;
        private static Transform _selectorRoot;

        internal static bool InterceptHospitalSelection(LevelConfig level)
        {
            if (_bypass || level == null || !ScenarioEditorFlow.IsRunning) return true;
            try
            {
                var selector = SelectorField?.GetValue(null) as GameObject;
                if (selector == null || OriginalStart == null)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Selector editor: faltan controles nativos; se abre mapa nuevo.");
                    return true;
                }

                _selectorRoot = selector.transform;
                EnsureOverlay(_selectorRoot);
                CollectHospitals(selector.transform, level);
                SelectHospital(level);
                return false;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("Selector editor: " + ex);
                CloseOverlay();
                return true;
            }
        }

        internal static void OnEditorOpened(Level level)
        {
            var path = _pendingMap;
            _pendingMap = null;
            if (string.IsNullOrEmpty(path) || level == null || !ScenarioEditorSession.Active) return;
            try
            {
                ScenarioEditorStorage.Load(level, path);
                UnderPressureEditorPlugin.Log?.LogInfo("Editor: mapa seleccionado cargado: " + path);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo abrir el mapa elegido (se conserva el hospital original): " + ex);
            }
        }

        internal static void Reset()
        {
            _pendingMap = null;
            _selectedLevel = null;
            _bypass = false;
            _hospitals.Clear();
            CloseOverlay();
        }

        private static void EnsureOverlay(Transform selector)
        {
            if (_overlay != null) return;
            _style = CaptureStyle(selector);
            BuildOverlay(selector);
        }

        private static void BuildOverlay(Transform selector)
        {
            _overlay = NewUi("UP_EditorMapPicker", selector, typeof(Image));
            _overlayRect = (RectTransform)_overlay.transform;
            Stretch(_overlayRect);
            _overlay.GetComponent<Image>().color = _style.OverlayColor;
            _overlay.transform.SetAsLastSibling();

            var backgroundCatcher = _overlay.GetComponent<Button>() ?? _overlay.AddComponent<Button>();
            backgroundCatcher.transition = Selectable.Transition.None;
            backgroundCatcher.onClick.AddListener(() => CloseOverlay());

            var panel = NewUi("Panel", _overlay.transform, typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.72f, 0.56f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 470f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = _style.PanelColor;
            panelImage.raycastTarget = true;

            var panelButton = panel.AddComponent<Button>();
            panelButton.transition = Selectable.Transition.None;
            panelButton.onClick = new Button.ButtonClickedEvent();

            CreateHeader(panel.transform, "Escenario base", new Vector2(15f, -12f), new Vector2(360f, 42f), out _);
            CreateHeader(panel.transform, "Hospital", new Vector2(390f, -12f), new Vector2(355f, 42f), out _rightTitleTmp, out _rightTitleLegacy);

            _leftScroll = CreateScrollColumn(panel.transform, "BaseLevels", new Vector2(15f, -58f), new Vector2(360f, 330f), out _leftContent);
            _rightScroll = CreateScrollColumn(panel.transform, "MapLevels", new Vector2(390f, -58f), new Vector2(355f, 330f), out _rightContent);

            CreateMainButton(panel.transform, "Volver al menú principal", new Vector2(15f, -402f), new Vector2(360f, 56f), CloseOverlay, false);
        }

        private static void CollectHospitals(Transform selector, LevelConfig fallback)
        {
            _hospitals.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var selectable in selector.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable == null || selectable.transform.IsChildOf(_overlayRect)) continue;
                var level = TryExtractLevel(selectable.gameObject);
                if (level == null || string.IsNullOrEmpty(level.UniqueId) || !seen.Add(level.UniqueId)) continue;
                _hospitals.Add(new HospitalChoice
                {
                    Level = level,
                    DisplayName = ExtractLabel(selectable.gameObject, level.GetLocalisedDisplayName())
                });
            }

            if (fallback != null && !seen.Contains(fallback.UniqueId))
            {
                _hospitals.Add(new HospitalChoice
                {
                    Level = fallback,
                    DisplayName = fallback.GetLocalisedDisplayName()
                });
            }

            _hospitals.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            RebuildHospitalButtons();
        }

        private static LevelConfig TryExtractLevel(GameObject go)
        {
            if (go == null) return null;
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var type = component.GetType();
                try
                {
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!typeof(LevelConfig).IsAssignableFrom(field.FieldType)) continue;
                        var value = field.GetValue(component) as LevelConfig;
                        if (value != null) return value;
                    }
                    foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                        if (!typeof(LevelConfig).IsAssignableFrom(prop.PropertyType)) continue;
                        var value = prop.GetValue(component, null) as LevelConfig;
                        if (value != null) return value;
                    }
                }
                catch { }
            }
            return null;
        }

        private static string ExtractLabel(GameObject go, string fallback)
        {
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text.Trim();
            var txt = go.GetComponentInChildren<Text>(true);
            return txt != null && !string.IsNullOrWhiteSpace(txt.text) ? txt.text.Trim() : fallback;
        }

        private static void RebuildHospitalButtons()
        {
            foreach (var button in _hospitalButtons)
                if (button != null) UnityEngine.Object.Destroy(button.gameObject);
            _hospitalButtons.Clear();

            foreach (var hospital in _hospitals)
            {
                var selected = hospital;
                var button = CreateMainButton(_leftContent, hospital.DisplayName, Vector2.zero, new Vector2(0f, 0f),
                    () => SelectHospital(selected.Level), false);
                AttachLayout(button.gameObject, 54f);
                _hospitalButtons.Add(button);
            }
            if (_leftScroll != null) _leftScroll.verticalNormalizedPosition = 1f;
            RefreshHospitalHighlight();
        }

        private static void SelectHospital(LevelConfig level)
        {
            if (level == null) return;
            _selectedLevel = level;
            if (_rightTitleTmp != null) _rightTitleTmp.text = ExtractHospitalName(level);
            if (_rightTitleLegacy != null) _rightTitleLegacy.text = ExtractHospitalName(level);
            RefreshHospitalHighlight();
            RebuildMapButtons();
        }

        private static string ExtractHospitalName(LevelConfig level)
        {
            if (level == null) return "Hospital";
            var name = level.GetLocalisedDisplayName();
            return string.IsNullOrWhiteSpace(name) ? level.UniqueId : name;
        }

        private static void RefreshHospitalHighlight()
        {
            for (var i = 0; i < _hospitalButtons.Count && i < _hospitals.Count; i++)
            {
                var image = _hospitalButtons[i] != null ? _hospitalButtons[i].GetComponent<Image>() : null;
                if (image == null) continue;
                image.color = _hospitals[i].Level == _selectedLevel ? _style.SelectedColor : Color.white;
            }
        }

        private static void RebuildMapButtons()
        {
            foreach (var button in _mapButtons)
                if (button != null) UnityEngine.Object.Destroy(button.gameObject);
            _mapButtons.Clear();
            if (_selectedLevel == null) return;

            var newMapButton = CreateMainButton(_rightContent, "New Map", Vector2.zero, new Vector2(0f, 0f),
                () => StartSelected(null), true);
            AttachLayout(newMapButton.gameObject, 50f);
            _mapButtons.Add(newMapButton);

            var maps = ReadMaps(_selectedLevel.UniqueId);
            foreach (var map in maps)
            {
                var selected = map;
                var button = CreateMainButton(_rightContent, selected.DisplayName, Vector2.zero, new Vector2(0f, 0f),
                    () => StartSelected(selected.FilePath), false);
                AttachLayout(button.gameObject, 50f);
                _mapButtons.Add(button);
            }
            if (_rightScroll != null) _rightScroll.verticalNormalizedPosition = 1f;
        }

        private static Button CreateMainButton(Transform parent, string label, Vector2 position, Vector2 size, Action onClick, bool green)
        {
            var go = NewUi("Button_" + label, parent, typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            if (size == Vector2.zero)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, 50f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }

            var image = go.GetComponent<Image>();
            image.sprite = _style.ButtonSprite;
            image.material = _style.ButtonMaterial;
            image.type = _style.ButtonType;
            image.pixelsPerUnitMultiplier = _style.PixelsPerUnitMultiplier;
            image.color = green ? _style.NewMapColor : Color.white;

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = green ? new Color(0.38f, 0.96f, 0.34f, 1f) : new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = green ? new Color(0.22f, 0.72f, 0.18f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            CreateButtonLabel(go.transform, label);
            return button;
        }

        private static void CreateHeader(Transform parent, string label, Vector2 position, Vector2 size, out TMP_Text tmp)
        {
            Text legacy;
            CreateHeader(parent, label, position, size, out tmp, out legacy);
        }

        private static void CreateHeader(Transform parent, string label, Vector2 position, Vector2 size, out TMP_Text tmp, out Text legacy)
        {
            var root = NewUi("Header_" + label, parent);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            tmp = null;
            legacy = null;
            if (_style.TmpFont != null)
            {
                var go = NewUi("Text", root.transform, typeof(TextMeshProUGUI));
                Stretch((RectTransform)go.transform);
                tmp = go.GetComponent<TextMeshProUGUI>();
                tmp.font = _style.TmpFont;
                tmp.text = label;
                tmp.fontSize = 20f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = _style.HeaderColor;
                tmp.raycastTarget = false;
            }
            else
            {
                var go = NewUi("Text", root.transform, typeof(Text));
                Stretch((RectTransform)go.transform);
                legacy = go.GetComponent<Text>();
                legacy.font = _style.LegacyFont ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                legacy.fontSize = 20;
                legacy.fontStyle = FontStyle.Bold;
                legacy.alignment = TextAnchor.MiddleCenter;
                legacy.color = _style.HeaderColor;
                legacy.text = label;
                legacy.raycastTarget = false;
            }
        }

        private static ScrollRect CreateScrollColumn(Transform parent, string name, Vector2 position, Vector2 size, out RectTransform contentRect)
        {
            var host = NewUi(name, parent, typeof(Image), typeof(ScrollRect));
            var hostRect = (RectTransform)host.transform;
            hostRect.anchorMin = hostRect.anchorMax = new Vector2(0f, 1f);
            hostRect.pivot = new Vector2(0f, 1f);
            hostRect.anchoredPosition = position;
            hostRect.sizeDelta = size;
            host.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            var scroll = host.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 34f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewUi("Viewport", host.transform, typeof(Image), typeof(Mask));
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.001f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = NewUi("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 5, 5);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            return scroll;
        }

        private static void AttachLayout(GameObject go, float height)
        {
            var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private static void CreateButtonLabel(Transform parent, string label)
        {
            if (_style.TmpFont != null)
            {
                var go = NewUi("Text", parent, typeof(TextMeshProUGUI));
                Stretch((RectTransform)go.transform);
                var text = go.GetComponent<TextMeshProUGUI>();
                text.font = _style.TmpFont;
                text.text = label;
                text.fontSize = _style.FontSize;
                text.enableAutoSizing = true;
                text.fontSizeMin = 12f;
                text.fontSizeMax = _style.FontSize;
                text.alignment = TextAlignmentOptions.Center;
                text.color = _style.TextColor;
                text.margin = new Vector4(10f, 0f, 10f, 0f);
                text.raycastTarget = false;
                return;
            }

            var labelGo = NewUi("Text", parent, typeof(Text));
            Stretch((RectTransform)labelGo.transform);
            var legacy = labelGo.GetComponent<Text>();
            legacy.font = _style.LegacyFont ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            legacy.fontSize = _style.FontSize;
            legacy.fontStyle = FontStyle.Bold;
            legacy.alignment = TextAnchor.MiddleCenter;
            legacy.color = _style.TextColor;
            legacy.text = label;
            legacy.raycastTarget = false;
        }

        private static UiStyle CaptureStyle(Transform selector)
        {
            var style = new UiStyle();
            var templateSelectable = selector.GetComponentsInChildren<Selectable>(true)
                .FirstOrDefault(s => s != null && s.GetComponent<Image>() != null);
            if (templateSelectable != null)
            {
                var image = templateSelectable.GetComponent<Image>();
                style.ButtonSprite = image.sprite;
                style.ButtonMaterial = image.material;
                style.ButtonType = image.type;
                style.PixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
                var tmp = templateSelectable.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null)
                {
                    style.TmpFont = tmp.font;
                    style.FontSize = Mathf.RoundToInt(tmp.fontSize > 0f ? tmp.fontSize : 18f);
                    style.TextColor = tmp.color;
                }
                else
                {
                    var text = templateSelectable.GetComponentInChildren<Text>(true);
                    if (text != null)
                    {
                        style.LegacyFont = text.font;
                        style.FontSize = text.fontSize > 0 ? text.fontSize : 18;
                        style.TextColor = text.color;
                    }
                }
            }
            else
            {
                style.LegacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            return style;
        }

        private static List<StoredMap> ReadMaps(string hospitalId)
        {
            var result = new List<StoredMap>();
            var folder = ScenarioEditorStorage.Folder;
            if (!Directory.Exists(folder)) return result;

            foreach (var file in Directory.GetFiles(folder, "*.upmap", SearchOption.AllDirectories))
            {
                try
                {
                    using (var reader = new StreamReader(file, Encoding.UTF8))
                    {
                        if (reader.ReadLine() != "UNDERPRESSURE_MAP_1") continue;
                        var header = (reader.ReadLine() ?? string.Empty).Split('|');
                        if (header.Length != 4 || header[0] != "M") continue;
                        var id = Encoding.UTF8.GetString(Convert.FromBase64String(header[1]));
                        if (!string.Equals(id, hospitalId, StringComparison.Ordinal)) continue;
                        var displayName = Encoding.UTF8.GetString(Convert.FromBase64String(header[3]));
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        result.Add(new StoredMap { FilePath = file, DisplayName = displayName });
                    }
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Selector editor: se ignoró archivo ilegible " + file + ": " + ex.Message);
                }
            }
            return result.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void StartSelected(string mapPath)
        {
            if (_selectedLevel == null || OriginalStart == null) return;
            var level = _selectedLevel;
            _pendingMap = mapPath;
            CloseOverlay();
            _bypass = true;
            try
            {
                OriginalStart.Invoke(null, new object[] { level });
            }
            catch (TargetInvocationException ex)
            {
                _pendingMap = null;
                UnderPressureEditorPlugin.Log?.LogError("No se pudo iniciar el editor: " + (ex.InnerException ?? ex));
            }
            catch (Exception ex)
            {
                _pendingMap = null;
                UnderPressureEditorPlugin.Log?.LogError("No se pudo iniciar el editor: " + ex);
            }
            finally
            {
                _bypass = false;
            }
        }

        private static void CloseOverlay()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            _overlayRect = null;
            _leftContent = null;
            _rightContent = null;
            _leftScroll = null;
            _rightScroll = null;
            _rightTitleTmp = null;
            _rightTitleLegacy = null;
            _hospitalButtons.Clear();
            _mapButtons.Clear();
        }

        private static GameObject NewUi(string name, Transform parent, params Type[] components)
        {
            var list = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
            list.AddRange(components);
            var go = new GameObject(name, list.ToArray());
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "StartSelectedLevel")]
    internal static class ScenarioEditorMapPickerSelectPatch
    {
        private static bool Prefix(LevelConfig level) => ScenarioEditorMapPicker.InterceptHospitalSelection(level);
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "OpenNativeEditor")]
    internal static class ScenarioEditorMapPickerOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Level level) => ScenarioEditorMapPicker.OnEditorOpened(level);
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "ResetAtOpeningScreen")]
    internal static class ScenarioEditorMapPickerResetPatch
    {
        private static void Prefix() => ScenarioEditorMapPicker.Reset();
    }
}
