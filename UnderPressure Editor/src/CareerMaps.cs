// Updated: 2026-10-14 - CareerMaps contiene exclusivamente la seleccion y los controles del mapa de carrera.
// Cargador ingame y representacion de parcelas trasladados a MapRuntime.cs sin cambiar mecanicas.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using I2.Loc;
using TH20;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ===== BEGIN ORIGINAL: ScenarioCareerMapMenu.cs =====
// Updated: 2026-10-13
// Selector de mapas CARRERA: marco y botonera copiados de la UI nativa de TPH.
// Nombre, descripcion, informacion y Comenzar siguen dentro de SelectedHospitalMenu (UI nativa).
// No reutiliza la interfaz del editor ni modifica el cargador .upmap.
namespace UnderPressure.Editor
{
    internal sealed class ScenarioCareerMapMenu : MonoBehaviour
    {
        private sealed class Choice
        {
            internal string Path;
            internal string Name;
            internal string Description;
        }

        private static readonly FieldInfo ConfigField = AccessTools.Field(typeof(SelectedHospitalMenu), "_levelConfig");
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(SelectedHospitalMenu), "_metagameMap");
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(SelectedHospitalMenu), "_hospitalNameLabel");
        private static readonly FieldInfo DescField = AccessTools.Field(typeof(SelectedHospitalMenu), "_hospitalDescriptionText");
        private static readonly FieldInfo DescPanelField = AccessTools.Field(typeof(SelectedHospitalMenu), "_hospitalDescriptionPanel");
        private static readonly FieldInfo MainTextField = AccessTools.Field(typeof(SelectedHospitalMenu), "_buttonText");
        private static readonly FieldInfo SavePanelField = AccessTools.Field(typeof(SelectedHospitalMenu), "_currentSavePanel");
        private static readonly FieldInfo RestartPanelField = AccessTools.Field(typeof(SelectedHospitalMenu), "_startNewButtonPanel");
        private static readonly FieldInfo RemixTabsField = AccessTools.Field(typeof(SelectedHospitalMenu), "_remixModeTabs");
        private static readonly FieldInfo NativeBackgroundField = AccessTools.Field(typeof(SelectedHospitalMenu), "_defaultBackground");

        private SelectedHospitalMenu _native;
        private LevelConfig _hospital;
        private GameObject _listWindow;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Choice _selected;
        private readonly List<Choice> _choices = new List<Choice>();
        private readonly List<Button> _buttons = new List<Button>();
        private TMP_Text _nativeName;
        private TMP_Text _nativeDescription;
        private Localize _nativeMainText;
        private bool _preparing;
        private bool _hadNativeFrame;
        private bool _buildFailed;
        private RectTransform _nativeRect;
        private RectTransform _frameRect;
        private Sprite _nativeSelectedSprite;
        private RectTransform _viewportRect;
        private Scrollbar _scrollbar;
        private CanvasGroup _selectorFade;
        private CanvasGroup _nativeFade;
        private Graphic _nativeBackgroundGraphic;
        private float _nativeFadeBaseline = 1f;
        private float _graphicFadeBaseline = 1f;
        private float _closingElapsed;
        private bool _closing;

        private static string T(string key) => EditorLocalization.Get(key);

        internal void Open(SelectedHospitalMenu menu)
        {
            if (menu == null || ScenarioEditorFlow.IsRunning) return;
            if (!UnderPressureEditorPlugin.ShowNewMaps)
            {
                if (_selected != null) menu.Refresh();
                Close();
                return;
            }
            _native = menu;
            var config = ConfigField?.GetValue(menu) as LevelConfig;
            if (config == null) return;

            var map = MapField?.GetValue(menu) as MetagameMap;
            if (map?.Metagame == null || !config.IsPlayable(map.Metagame) ||
                !DLCUtils.IsDLCInstalled(config.GetRequiredDlcPack()))
            {
                Close();
                return;
            }

            _nativeName = (NameField?.GetValue(menu) as Component)?.GetComponent<TMP_Text>();
            if (_nativeName == null)
                _nativeName = (NameField?.GetValue(menu) as Component)?.GetComponentInChildren<TMP_Text>(true);
            _nativeDescription = DescField?.GetValue(menu) as TMP_Text;
            _nativeMainText = MainTextField?.GetValue(menu) as Localize;

            if (_listWindow == null && !_buildFailed) Build();
            if (_listWindow == null) return;
            SyncFrameGeometry();
            if (_hospital != config)
            {
                _hospital = config;
                _selected = null; // Vanilla hospital remains selected until a custom map is clicked.
                LoadChoices();
                RebuildButtons();
            }
        }

        private void LoadChoices()
        {
            _choices.Clear();
            var root = ScenarioEditorStorage.Folder;
            if (!Directory.Exists(root)) return;
            foreach (var path in Directory.GetFiles(root, "*.upmap", SearchOption.AllDirectories))
            {
                try
                {
                    var header = ScenarioMapFormat.ReadHeader(path);
                    if (!string.Equals(header.baseHospitalId, _hospital.UniqueId, StringComparison.Ordinal))
                        continue;
                    var description = string.IsNullOrEmpty(header.description)
                        ? T("career.maps.custom.description")
                        : header.description;
                    if (description.Length > 1400) description = description.Substring(0, 1400);
                    _choices.Add(new Choice { Name = header.name, Description = description, Path = path });
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning("Mapa ignorado: " + path + ": " + ex.Message);
                }
            }
            _choices.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }

        private void Build()
        {
            // The original SelectedHospitalMenu defines the frame: never generate a rectangle,
            // shadow or rounded outline by hand. Copy its real sprite layers and dimensions.
            var original = _native.transform as RectTransform;
            var parent = original == null ? null : original.parent as RectTransform;
            if (parent == null) return;
            _nativeRect = original;
            _listWindow = Ui("UP_CareerMapNativeFrame", parent);
            var rect = (RectTransform)_listWindow.transform;
            _frameRect = rect;
            // First geometry is only provisional: Unity resizes SelectedHospitalMenu
            // AFTER Setup, depending on existing save data and Remix tabs.
            Place(rect, Vector2.zero, new Vector2(300f, 420f));
            _listWindow.AddComponent<LayoutElement>().ignoreLayout = true;
            _selectorFade = _listWindow.AddComponent<CanvasGroup>();
            _selectorFade.alpha = 1f;
            CaptureNativeFadeReferences();
            SyncFrameGeometry();

            _hadNativeFrame = CopyNativeFrame(original, rect);
            if (!_hadNativeFrame)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se ha encontrado el marco original de SelectedHospitalMenu; selector ocultado para no dibujar un panel inventado.");
                Destroy(_listWindow);
                _listWindow = null;
                _buildFailed = true;
                return;
            }

            // Header is text, NOT a tab or clickable button. Native Remix tabs
            // belong exclusively to the game's own SelectedHospitalMenu.
            var title = Ui("EditorLevelsTitle", rect);
            Place((RectTransform)title.transform, new Vector2(36f, -16f), new Vector2(221f, 46f));
            AddLabel(title.transform, T("career.maps.title"), 22f, Color.white);

            var scrollGo = Ui("MapScroll", rect, typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(15f, 20f);
            scrollRect.offsetMax = new Vector2(-18f, -78f);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 45f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = Ui("Viewport", scrollGo.transform, typeof(Image), typeof(Mask));
            var viewRect = (RectTransform)viewport.transform;
            Fill(viewRect);
            _viewportRect = viewRect;
            // No scrollbar space is reserved until the map list actually overflows.
            _viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.002f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = Ui("MapButtons", viewport.transform,
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 2, 2);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.viewport = viewRect;
            _scroll.content = _content;
            CaptureNativeSelectedSprite();

            var scrollbarSource = _native.GetComponentsInChildren<Scrollbar>(true)
                .FirstOrDefault(s => s != null);
            if (scrollbarSource != null)
            {
                // Native Scrollbar clone: the existing game's track, handle and sprites.
                var scrollbar = Instantiate(scrollbarSource, scrollGo.transform, false);
                scrollbar.name = "NativeMapScrollbar";
                var barRect = (RectTransform)scrollbar.transform;
                barRect.anchorMin = new Vector2(1f, 0f);
                barRect.anchorMax = new Vector2(1f, 1f);
                barRect.pivot = new Vector2(1f, 0.5f);
                barRect.offsetMin = new Vector2(-15f, 0f);
                barRect.offsetMax = Vector2.zero;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                _scroll.verticalScrollbar = scrollbar;
                _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                _scrollbar = scrollbar;
                _scrollbar.gameObject.SetActive(false);
            }
            RefreshListGeometry();
        }

        private float PreferredContentHeight()
        {
            // Exactly the sizes used by RebuildButtons and the native-styled layout.
            // VerticalLayoutGroup: 2+2 padding, 52 px per button, 10 px between rows.
            var count = _choices.Count;
            return 4f + 52f * count + 10f * Mathf.Max(0, count - 1);
        }

        private void RefreshListGeometry()
        {
            if (_frameRect == null || _viewportRect == null || _scroll == null) return;
            var visibleHeight = Mathf.Max(0f, _frameRect.rect.height - 98f);
            var needsScrollbar = PreferredContentHeight() > visibleHeight + 0.5f;
            if (_scrollbar != null && _scrollbar.gameObject.activeSelf != needsScrollbar)
                _scrollbar.gameObject.SetActive(needsScrollbar);
            _viewportRect.offsetMax = new Vector2(needsScrollbar ? -19f : 0f, 0f);
            _scroll.vertical = needsScrollbar;
            if (!needsScrollbar) _scroll.verticalNormalizedPosition = 1f;
        }

        private void CaptureNativeFadeReferences()
        {
            // Sample the original menu's own fade. Our clone is a sibling of it,
            // so it would otherwise ignore the native menu's animation.
            _nativeFade = _native == null ? null : _native.GetComponent<CanvasGroup>();
            _nativeFadeBaseline = _nativeFade == null ? 1f : Mathf.Max(0.001f, _nativeFade.alpha);
            var background = NativeBackgroundField?.GetValue(_native) as Component;
            _nativeBackgroundGraphic = background == null ? null : background.GetComponentInChildren<Graphic>(true);
            _graphicFadeBaseline = _nativeBackgroundGraphic == null ? 1f :
                Mathf.Max(0.001f, _nativeBackgroundGraphic.canvasRenderer.GetAlpha());
        }

        private void FadeAlongsideNative()
        {
            _closingElapsed += Time.unscaledDeltaTime;
            if (_selectorFade == null) return;
            // When the native animated menu changes its alpha, mirror that exactly.
            // Other animations may only move the panel: use a short fade then.
            var measured = 1f;
            if (_nativeFade != null)
                measured = Mathf.Min(measured, _nativeFade.alpha / _nativeFadeBaseline);
            if (_nativeBackgroundGraphic != null)
                measured = Mathf.Min(measured,
                    _nativeBackgroundGraphic.canvasRenderer.GetAlpha() / _graphicFadeBaseline);
            measured = Mathf.Clamp01(measured);
            var fallback = Mathf.Clamp01(1f - _closingElapsed / 0.28f);
            _selectorFade.alpha = measured < 0.98f ? measured : fallback;
        }

        private void SyncFrameGeometry()
        {
            if (_nativeRect == null || _frameRect == null || _native == null ||
                _native.IsClosing()) return;
            var parent = _nativeRect.parent as RectTransform;
            if (parent == null) return;

            // World corners are read every frame AFTER the native layout has been
            // rebuilt, not frozen in SelectedHospitalMenu.Setup's Postfix.
            var corners = new Vector3[4];
            _nativeRect.GetWorldCorners(corners);
            var bottomLeft = parent.InverseTransformPoint(corners[0]);
            var topRight = parent.InverseTransformPoint(corners[2]);
            var width = topRight.x - bottomLeft.x;
            var height = topRight.y - bottomLeft.y;
            if (width <= 0f || height <= 0f) return;

            var parentTopLeft = new Vector2(parent.rect.xMin, parent.rect.yMax);
            var pos = new Vector2(topRight.x - 18f - parentTopLeft.x,
                topRight.y - parentTopLeft.y);
            if ((_frameRect.anchoredPosition - pos).sqrMagnitude > 0.01f)
                _frameRect.anchoredPosition = pos;
            // The original hospital panel is the HEIGHT LIMIT, not a mandatory height.
            // Expand only far enough to fit the existing map buttons and padding.
            var desiredHeight = Mathf.Max(115f, 98f + PreferredContentHeight());
            var finalHeight = Mathf.Min(height, desiredHeight);
            if (Mathf.Abs(_frameRect.sizeDelta.y - finalHeight) > 0.1f)
                _frameRect.sizeDelta = new Vector2(300f, finalHeight);
            RefreshListGeometry();

            // This entire frame always stays behind the real hospital panel.
            // Neither the panel's width nor its height is fixed: existing saves
            // and native Remix modes both affect its final layout.
            var originalIndex = _nativeRect.GetSiblingIndex();
            if (_frameRect.GetSiblingIndex() > originalIndex)
                _frameRect.SetSiblingIndex(originalIndex);
        }

        private void CaptureNativeSelectedSprite()
        {
            _nativeSelectedSprite = null;
            var tabs = RemixTabsField?.GetValue(_native) as DynamicButton[];
            if (tabs == null) return;
            foreach (var tab in tabs)
            {
                if (tab == null) continue;
                var animator = tab.GetComponent<ButtonAnimator>();
                var preset = animator == null ? null :
                    AccessTools.Field(typeof(ButtonAnimator), "_preset")?.GetValue(animator)
                    as ButtonAnimatorPreset;
                if (preset != null && preset.SelectedBackgroundSprite != null)
                {
                    _nativeSelectedSprite = preset.SelectedBackgroundSprite;
                    return;
                }
            }
            // In case a native tab has no preset, reuse its selected graphic.
            foreach (var tab in tabs)
            {
                if (tab == null) continue;
                var image = tab.GetComponent<Image>();
                if (image != null && image.overrideSprite != null)
                {
                    _nativeSelectedSprite = image.overrideSprite;
                    return;
                }
            }
        }

        private bool CopyNativeFrame(RectTransform original, RectTransform target)
        {
            // Copy the game's COMPLETE default background hierarchy, not a lookalike
            // Image or an Outline: this preserves its original borders and rounding.
            var background = NativeBackgroundField?.GetValue(_native) as LayoutElement;
            if (background != null)
            {
                var clone = Instantiate(background.gameObject, target, false);
                clone.name = "TPH_NativeHospitalBackground";
                clone.SetActive(true);
                var copyRect = clone.transform as RectTransform;
                if (copyRect != null)
                {
                    copyRect.anchorMin = Vector2.zero;
                    copyRect.anchorMax = Vector2.one;
                    copyRect.pivot = new Vector2(0.5f, 0.5f);
                    copyRect.offsetMin = Vector2.zero;
                    copyRect.offsetMax = Vector2.zero;
                    copyRect.localScale = Vector3.one;
                }
                foreach (var b in clone.GetComponentsInChildren<Button>(true)) b.interactable = false;
                foreach (var t in clone.GetComponentsInChildren<TMP_Text>(true)) t.gameObject.SetActive(false);
                foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
                    graphic.raycastTarget = false;
                if (clone.GetComponentsInChildren<Image>(true).Length > 0)
                    return true;
                Destroy(clone);
            }

            // Fallback for variations of the prefab in which the native graphic
            // layers are siblings of _defaultBackground.
            var allImages = _native.GetComponentsInChildren<Image>(true);
            var bounds = new Vector3[4];
            original.GetWorldCorners(bounds);
            var originalWidth = original.rect.width;
            var originalHeight = original.rect.height;
            var count = 0;
            foreach (var source in allImages)
            {
                if (source == null || !source.gameObject.activeInHierarchy ||
                    (source.sprite == null && source.color.a < 0.35f)) continue;
                var r = source.rectTransform;
                if (r == null) continue;
                var sourceCorners = new Vector3[4];
                r.GetWorldCorners(sourceCorners);
                var left = original.InverseTransformPoint(sourceCorners[0]);
                var right = original.InverseTransformPoint(sourceCorners[2]);
                var w = right.x - left.x;
                var h = right.y - left.y;
                // Only outer structural layers, not the description box or action buttons.
                if (w < originalWidth * .86f || h < originalHeight * .85f) continue;
                if (left.x < -originalWidth * .20f || right.x > originalWidth * 1.20f) continue;
                var part = Ui("NativeFrame_" + count, target, typeof(Image));
                var image = part.GetComponent<Image>();
                image.sprite = source.sprite;
                image.overrideSprite = source.overrideSprite;
                image.type = source.type;
                image.color = source.color;
                image.material = source.material;
                image.preserveAspect = source.preserveAspect;
                image.fillCenter = source.fillCenter;
                image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                image.raycastTarget = false;
                var originalOutline = source.GetComponent<Outline>();
                if (originalOutline != null)
                {
                    var cloned = part.AddComponent<Outline>();
                    cloned.effectColor = originalOutline.effectColor;
                    cloned.effectDistance = originalOutline.effectDistance;
                    cloned.useGraphicAlpha = originalOutline.useGraphicAlpha;
                }
                else
                {
                    var originalShadow = source.GetComponent<Shadow>();
                    if (originalShadow != null)
                    {
                        var cloned = part.AddComponent<Shadow>();
                        cloned.effectColor = originalShadow.effectColor;
                        cloned.effectDistance = originalShadow.effectDistance;
                        cloned.useGraphicAlpha = originalShadow.useGraphicAlpha;
                    }
                }
                var dest = (RectTransform)part.transform;
                Place(dest,
                    new Vector2((left.x - original.rect.xMin) * target.rect.width / originalWidth,
                                -(original.rect.yMax - right.y) * target.rect.height / originalHeight),
                    new Vector2(w * target.rect.width / originalWidth,
                                h * target.rect.height / originalHeight));
                count++;
            }
            return count > 0;
        }

        private void RebuildButtons()
        {
            if (_content == null) return;
            foreach (var button in _buttons)
                if (button != null) Destroy(button.gameObject);
            _buttons.Clear();
            foreach (var item in _choices)
            {
                var selection = item;
                var go = Ui("Map_" + item.Name, _content, typeof(Image), typeof(Button), typeof(LayoutElement));
                StyleBlue(go.GetComponent<Image>());
                var button = go.GetComponent<Button>();
                button.onClick.AddListener(() => Select(selection));
                var element = go.GetComponent<LayoutElement>();
                element.minHeight = 52f;
                element.preferredHeight = 52f;
                AddLabel(go.transform, item.Name, 22f, Color.white);
                _buttons.Add(button);
            }
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
            SyncFrameGeometry();
        }

        private void Select(Choice item)
        {
            if (item == null) return;
            _selected = item;
            for (var i = 0; i < _buttons.Count && i < _choices.Count; i++)
            {
                var button = _buttons[i];
                if (button == null) continue;
                var image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.overrideSprite = _choices[i] == item ? _nativeSelectedSprite : null;
                    image.color = Color.white;
                }
            }
            ApplyToOriginalPanel();
        }

        private void ApplyToOriginalPanel()
        {
            if (_native == null || _selected == null) return;
            if (_nativeName != null) _nativeName.text = _selected.Name;
            if (_nativeDescription != null) _nativeDescription.text = _selected.Description;
            var descPanel = DescPanelField?.GetValue(_native) as GameObject;
            if (descPanel != null && !descPanel.activeSelf) descPanel.SetActive(true);
            var savePanel = SavePanelField?.GetValue(_native) as GameObject;
            if (savePanel != null && savePanel.activeSelf) savePanel.SetActive(false);
            var restartPanel = RestartPanelField?.GetValue(_native) as GameObject;
            if (restartPanel != null && restartPanel.activeSelf) restartPanel.SetActive(false);
            if (_nativeMainText != null && _nativeMainText.Term != "Misc/Start_CS")
                _nativeMainText.SetTerm("Misc/Start_CS");
        }

        private void LateUpdate()
        {
            if (!UnderPressureEditorPlugin.ShowNewMaps)
            {
                if (_selected != null && _native != null) _native.Refresh();
                Close();
                return;
            }
            if (_native != null && _listWindow == null && !_buildFailed && _native.isActiveAndEnabled)
            {
                Open(_native);
                return;
            }
            if (_native == null || _listWindow == null || !_native.gameObject.activeInHierarchy) return;
            if (_native.IsClosing())
            {
                // Do NOT chase the original when its closing animation slides it left.
                // Preserve the last open position while matching its fade-out.
                _closing = true;
                FadeAlongsideNative();
                return;
            }
            if (_closing)
            {
                _closing = false;
                _closingElapsed = 0f;
                if (_selectorFade != null) _selectorFade.alpha = 1f;
                CaptureNativeFadeReferences();
            }
            SyncFrameGeometry();
            var newHospital = ConfigField?.GetValue(_native) as LevelConfig;
            if (newHospital != null && newHospital != _hospital)
            {
                _hospital = newHospital;
                LoadChoices();
                RebuildButtons();
                _selected = null; // Let native Hogsport/Remix be the default.
                _native.Refresh();
            }
            ApplyToOriginalPanel(); // Vanilla Update rewrites its description every frame.
        }

        // Invoked ONLY from the game's real "Comenzar" button, never from the map list.
        internal bool BeforeNativeStart()
        {
            if (!UnderPressureEditorPlugin.ShowNewMaps || _preparing || _selected == null)
                return true;
            var map = MapField?.GetValue(_native) as MetagameMap;
            if (map?.Level?.Config == _hospital)
            {
                if (_nativeDescription != null)
                    _nativeDescription.text = T("career.maps.active.error");
                return false;
            }
            _preparing = true;
            try
            {
                ScenarioCareerMapLoader.Prepare(_hospital, _selected.Path);
                return true; // Let the original game start the hospital normally.
            }
            catch (Exception ex)
            {
                ScenarioCareerMapLoader.Cancel();
                UnderPressureEditorPlugin.Log?.LogError("No se pudo empezar el mapa: " + ex);
                if (_nativeDescription != null)
                    _nativeDescription.text = T("career.maps.open.error");
                return false;
            }
            finally { _preparing = false; }
        }

        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }

        private void Close()
        {
            if (_listWindow != null) Destroy(_listWindow);
            _listWindow = null;
            _content = null;
            _scroll = null;
            _buttons.Clear();
            _choices.Clear();
            _selected = null;
            _hospital = null;
            _buildFailed = false;
            _nativeRect = null;
            _frameRect = null;
            _nativeSelectedSprite = null;
            _viewportRect = null;
            _scrollbar = null;
            _selectorFade = null;
            _nativeFade = null;
            _nativeBackgroundGraphic = null;
            _closing = false;
            _closingElapsed = 0f;
        }


        private void StyleBlue(Image image)
        {
            if (image == null) return;
            var tabs = RemixTabsField?.GetValue(_native) as DynamicButton[];
            if (tabs == null || tabs.Length < 2 || tabs[1] == null) return;
            var originalImage = tabs[1].GetComponent<Image>();
            if (originalImage == null) return;
            image.sprite = originalImage.sprite;
            image.type = originalImage.type;
            image.pixelsPerUnitMultiplier = originalImage.pixelsPerUnitMultiplier;
            image.material = originalImage.material;
            image.color = originalImage.color;
            image.preserveAspect = originalImage.preserveAspect;
        }

        private void AddLabel(Transform parent, string value, float size, Color color)
        {
            var go = Ui("Text", parent, typeof(TextMeshProUGUI));
            Fill((RectTransform)go.transform);
            var label = go.GetComponent<TextMeshProUGUI>();
            var original = (MainTextField?.GetValue(_native) as Component)?.GetComponent<TMP_Text>();
            if (original != null && original.font != null) label.font = original.font;
            label.text = value;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = 13f;
            label.fontSizeMax = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = color;
            label.margin = new Vector4(8f, 0f, 8f, 0f);
            label.raycastTarget = false;
        }

        private static GameObject Ui(string name, Transform parent, params Type[] components)
        {
            var list = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
            list.AddRange(components);
            var go = new GameObject(name, list.ToArray());
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }

    [HarmonyPatch(typeof(SelectedHospitalMenu), "Setup")]
    internal static class ScenarioCareerMapSelectorPatch
    {
        private static void Postfix(SelectedHospitalMenu __instance)
        {
            if (ScenarioEditorFlow.IsRunning || __instance == null) return;
            var component = __instance.GetComponent<ScenarioCareerMapMenu>() ??
                __instance.gameObject.AddComponent<ScenarioCareerMapMenu>();
            component.Open(__instance);
        }
    }

    [HarmonyPatch(typeof(SelectedHospitalMenu), "OnButtonPressed")]
    internal static class ScenarioCareerMapNativeStartPatch
    {
        private static bool Prefix(SelectedHospitalMenu __instance)
        {
            var component = __instance != null ? __instance.GetComponent<ScenarioCareerMapMenu>() : null;
            return component == null || component.BeforeNativeStart();
        }
    }
}


namespace UnderPressure.Editor
{
    // Adds the check to the existing Under Pressure MOD options (real, cloned native toggle).
    [HarmonyPatch]
    internal static class ScenarioCareerMapSettingsPatch
    {
        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("UnderPressure.ModSettingsTab");
            return type == null ? null : AccessTools.Method(type, "BuildContents");
        }

        private static void Postfix(object __instance)
        {
            try
            {
                if (UnderPressureEditorPlugin.ShowNewMapsSetting == null) return;
                var field = AccessTools.Field(__instance.GetType(), "_contents");
                var root = field?.GetValue(__instance) as Transform;
                var content = root?.Find("Scroll View/Viewport/Content");
                var left = content?.Find("Startup Settings");
                var source = left?.Find("Show Electricity");
                if (source == null || left.Find("Show New Maps") != null) return;

                var row = UnityEngine.Object.Instantiate(source, left, false);
                row.name = "Show New Maps";
                var rect = row as RectTransform;
                if (rect != null)
                {
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -327f);
                    rect.SetAsLastSibling();
                }
                foreach (var component in row.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    var type = component.GetType().FullName ?? string.Empty;
                    if (type.StartsWith("I2.Loc.", StringComparison.Ordinal) ||
                        type.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0)
                        UnityEngine.Object.Destroy(component);
                }
                var label = row.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                    label.text = EditorLocalization.Get("editor.settings.show_new_maps");
                var toggle = row.GetComponentInChildren<Toggle>(true);
                if (toggle == null) return;
                toggle.onValueChanged.RemoveAllListeners();
                toggle.isOn = UnderPressureEditorPlugin.ShowNewMaps;
                toggle.onValueChanged.AddListener(value =>
                {
                    UnderPressureEditorPlugin.ShowNewMapsSetting.Value = value;
                });
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudo añadir Mostrar nuevos mapas: " + ex);
            }
        }
    }
}
// ===== END ORIGINAL: ScenarioCareerMapMenu.cs =====

// ===== BEGIN ORIGINAL: ScenarioCareerMapControls.cs =====
// Updated: 2026-10-13 - Ubicacion correcta de botones nativos arriba; extras 5 px sobre el panel.
// Conserva la jerarquia y los eventos nativos; no mueve el menu animado.
// Los botones nuevos sólo alteran la cámara y la representación de hospitales;
// no conceden estrellas, desbloqueos ni licencias DLC.
namespace UnderPressure.Editor
{
    internal sealed class ScenarioCareerMapControls : MonoBehaviour
    {
        private static readonly FieldInfo MenuButtonField =
            AccessTools.Field(typeof(MetagameButtonsMenu), "_optionsButton");
        private static readonly FieldInfo CareerButtonField =
            AccessTools.Field(typeof(MetagameButtonsMenu), "_careerGoalsButton");
        private static readonly FieldInfo LeaderboardButtonField =
            AccessTools.Field(typeof(MetagameButtonsMenu), "_leaderboardButton");
        private static readonly MethodInfo UpdateBoundsMethod =
            AccessTools.Method(typeof(MetagameMapUI), "SetCameraBoundsToMapPinBounds");
        private static readonly FieldInfo MeshField =
            AccessTools.Field(typeof(MapPinHospital), "_meshIcon");
        private static readonly FieldInfo HoverField =
            AccessTools.Field(typeof(MapPinHospital), "_cursorOver");
        private static readonly FieldInfo LockedField =
            AccessTools.Field(typeof(MapPinHospital), "_materialLocked");
        private static readonly FieldInfo LockedHoverField =
            AccessTools.Field(typeof(MapPinHospital), "_materialLockedHighlighted");
        private static readonly FieldInfo StarsField =
            AccessTools.Field(typeof(MapPinHospital), "_stars");
        private static readonly FieldInfo RecordField =
            AccessTools.Field(typeof(MapPinHospital), "_hospitalRecord");
        private static readonly FieldInfo MetagameField =
            AccessTools.Field(typeof(MapPinHospital), "_metagame");
        private static readonly FieldInfo CameraConfigField =
            AccessTools.Field(typeof(TopDownCameraLogic), "_config");
        private static readonly FieldInfo VisibilityMetagameField =
            AccessTools.Field(typeof(MapVisualsActivation), "_metagame");
        private static readonly FieldInfo VisibilityRecordField =
            AccessTools.Field(typeof(MapVisualsActivation), "_hospitalRecord");
        private static readonly FieldInfo VisibilityLevelField =
            AccessTools.Field(typeof(MapVisualsActivation), "_levelConfig");

        private static ScenarioCareerMapControls _current;
        private MetagameMap _map;
        private MetagameButtonsMenu _nativeMenu;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private DynamicButton[] _nativeButtons;
        private DynamicButton _zoomButton;
        private DynamicButton _hospitalsButton;
        private bool _moreZoom;
        private bool _allHospitals;
        private float _originalFarClip;
        private bool _initialised;
        // Distancias fijas: el hover nativo no reordena los botones.
        private readonly float[] _nativeWidths = new float[3];
        private readonly float[] _nativeHeights = new float[3];
        private bool _nativeMeasuresReady;
        private bool _canvasCallbackRegistered;

        internal static bool ShowingAllHospitals => _current != null && _current._allHospitals;

        internal static bool ExtraZoomFor(TopDownCameraLogic camera, out float scalar)
        {
            scalar = 0f;
            if (_current == null || !_current._moreZoom || _current._map == null ||
                _current._map.CameraLogic != camera)
                return false;
            var config = CameraConfigField?.GetValue(camera) as TopDownCameraLogic.Config;
            var max = config == null ? 1f : Mathf.Max(0.001f, config.MaxZoomScalar);
            scalar = Mathf.Clamp01(camera.CurrentZoomScalar / max);
            return true;
        }

        internal void Initialise(MetagameMap map, MetagameButtonsMenu menu)
        {
            if (_initialised || map == null || menu == null) return;
            _map = map;
            _nativeMenu = menu;
            _canvas = menu.GetComponentInParent<Canvas>();
            if (_canvas == null) return;
            _canvas = _canvas.rootCanvas;
            _canvasRect = _canvas.transform as RectTransform;
            if (_canvasRect == null) return;

            var options = MenuButtonField?.GetValue(menu) as DynamicButton;
            var career = CareerButtonField?.GetValue(menu) as DynamicButton;
            var leaderboard = LeaderboardButtonField?.GetValue(menu) as DynamicButton;
            if (options == null || career == null || leaderboard == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Minimapa: no se localizaron los tres botones nativos; no se recolocan.");
                return;
            }
            _nativeButtons = new[] { options, career, leaderboard };
            // Los tres botones se mantienen dentro del MetagameButtonsMenu original.
            // No mover su raiz: el Animator del juego restaura su transform.
            _originalFarClip = map.CameraLogic?.CameraComponent != null
                ? map.CameraLogic.CameraComponent.farClipPlane : 0f;
            _current = this;
            _initialised = true;
            try
            {
                // El prefab existente sirve como origen visual: no se inventan sprites ni shaders.
                _zoomButton = CreateNativeButton(options, "UP_MapMoreZoom",
                    EditorLocalization.Get("career.map.more_zoom"), ZoomPressed);
                _hospitalsButton = CreateNativeButton(options, "UP_MapAllHospitals",
                    EditorLocalization.Get("career.map.all_hospitals"), HospitalsPressed);
                ArrangeExtraButtons();
                Canvas.willRenderCanvases += PositionNativeButtonsBeforeRender;
                _canvasCallbackRegistered = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Minimapa: posicionamiento visual nativo, sin reparentar ni alterar callbacks.");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("Minimapa: error al crear botones: " + exception);
            }
        }

        private DynamicButton CreateNativeButton(DynamicButton original, string name, string text, Action callback)
        {
            var clone = Instantiate(original.gameObject, _canvasRect, false);
            clone.name = name;
            clone.SetActive(true);
            foreach (var l in clone.GetComponentsInChildren<Localize>(true))
                l.enabled = false;
            var button = clone.GetComponent<DynamicButton>();
            if (button == null)
            {
                Destroy(clone);
                return null;
            }
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => callback());
            button.interactable = true;
            button.SetTMPText(text);
            var r = clone.transform as RectTransform;
            if (r != null)
            {
                // Sólo las COPIAS tienen nueva ubicación; la medida original se conserva.
                var source = original.transform as RectTransform;
                if (source != null)
                    r.sizeDelta = source.rect.size;
                r.anchorMin = new Vector2(1f, 0f);
                r.anchorMax = new Vector2(1f, 0f);
                r.pivot = new Vector2(1f, 0f);
                r.localRotation = Quaternion.identity;
                r.localScale = Vector3.one;
            }
            // Si la plantilla incluía listeners de herramientas, se sustituyen arriba.
            var tip = clone.GetComponentInChildren<TooltipSpawner>(true);
            if (tip != null) tip.enabled = false;
            return button;
        }

        private void ArrangeExtraButtons()
        {
            if (_canvasRect == null) return;
            // Los botones adicionales van 5 px por encima del panel de fundacion.
            // Si la raiz del panel nativo incluye margen invisible, se usa una
            // distancia segura al borde inferior como alternativa.
            const float right = 14f;
            const float gap = 8f;
            float bottom = 164f;
            var foundation = _map?.HUD?.FindMenu<FoundationStatusMenu>(false);
            var frame = foundation == null ? null : foundation.transform as RectTransform;
            if (frame != null && frame.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                frame.GetWorldCorners(corners);
                float top = float.NegativeInfinity;
                float minX = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    var point = _canvasRect.InverseTransformPoint(corners[i]);
                    top = Mathf.Max(top, point.y);
                    minX = Mathf.Min(minX, point.x);
                }
                var bounds = _canvasRect.rect;
                if (top > bounds.yMin + 60f &&
                    top < bounds.yMin + bounds.height * 0.48f &&
                    minX > bounds.xMin + bounds.width * 0.45f)
                    bottom = top - bounds.yMin + 5f;
            }
            if (_hospitalsButton != null)
            {
                var r = _hospitalsButton.transform as RectTransform;
                if (r != null) r.anchoredPosition = new Vector2(-right, bottom);
            }
            if (_zoomButton != null)
            {
                var r = _zoomButton.transform as RectTransform;
                var rightButton = _hospitalsButton?.transform as RectTransform;
                if (r != null)
                {
                    float width = rightButton == null ? r.rect.width : rightButton.rect.width;
                    r.anchoredPosition = new Vector2(-right - width - gap, bottom);
                }
            }
        }

        private void LateUpdate()
        {
            if (!_initialised || _canvasRect == null || _nativeMenu == null) return;
            // SOLO los dos botones nuevos desaparecen con el menu de pausa.
            // Los tres nativos conservan el control de visibilidad del juego.
            var hud = _map == null ? null : _map.HUD;
            bool pauseOpen = hud != null && hud.IsOptionsMenuOpen;
            bool visible = !pauseOpen && _nativeMenu.gameObject.activeInHierarchy;
            if (_zoomButton != null && _zoomButton.gameObject.activeSelf != visible)
                _zoomButton.gameObject.SetActive(visible);
            if (_hospitalsButton != null && _hospitalsButton.gameObject.activeSelf != visible)
                _hospitalsButton.gameObject.SetActive(visible);
        }

        private void PositionNativeButtonsBeforeRender()
        {
            if (!_initialised || _canvasRect == null || _nativeMenu == null ||
                !_nativeMenu.gameObject.activeInHierarchy ||
                _nativeMenu.IsClosed() || _nativeMenu.IsClosing()) return;

            // El menu tiene animacion nativa: mover su RectTransform no funciona
            // porque el Animator lo devuelve al borde inferior. Se desplazan
            // exclusivamente los botones, manteniendo cada uno su padre original,
            // listeners, escala, layout y comportamientos de apertura/cierre.
            var corners = new Vector3[4];
            if (!_nativeMeasuresReady)
            {
                for (int i = 0; i < _nativeButtons.Length; i++)
                {
                    var rect = _nativeButtons[i]?.transform as RectTransform;
                    if (rect == null) return;
                    rect.GetWorldCorners(corners);
                    var a = _canvasRect.InverseTransformPoint(corners[0]);
                    var b = _canvasRect.InverseTransformPoint(corners[2]);
                    _nativeWidths[i] = Mathf.Abs(b.x - a.x);
                    _nativeHeights[i] = Mathf.Abs(b.y - a.y);
                    if (_nativeWidths[i] < 24f || _nativeHeights[i] < 12f) return;
                }
                _nativeMeasuresReady = true;
            }

            // Orden original visual: Menu, Carrera, Marcadores; el ultimo queda
            // a la izquierda del boton nativo de incidencias.
            float right = _canvasRect.rect.xMax - 96f;
            float top = _canvasRect.rect.yMax - 17f;
            const float gap = 8f;
            for (int i = _nativeButtons.Length - 1; i >= 0; i--)
            {
                var rect = _nativeButtons[i]?.transform as RectTransform;
                if (rect == null) continue;
                float width = _nativeWidths[i];
                float height = _nativeHeights[i];
                float centerX = right - width * 0.5f;
                float centerY = top - height * 0.5f;
                right -= width + gap;
                if (!rect.gameObject.activeInHierarchy) continue;

                rect.GetWorldCorners(corners);
                var a = _canvasRect.InverseTransformPoint(corners[0]);
                var b = _canvasRect.InverseTransformPoint(corners[2]);
                var displacement = new Vector3(centerX - (a.x + b.x) * 0.5f,
                    centerY - (a.y + b.y) * 0.5f, 0f);
                if (displacement.sqrMagnitude > 0.01f)
                    rect.position += _canvasRect.TransformVector(displacement);
            }
            // No se usa SetParent, SetActive ni se modifica onPrimaryDown de
            // ninguno de los botones nativos.
        }

        private void ZoomPressed()
        {
            _moreZoom = !_moreZoom;
            if (_map?.CameraLogic?.CameraComponent == null) return;
            // Evita recortar la escena al permitir alejar tres veces más la cámara.
            _map.CameraLogic.CameraComponent.farClipPlane = _moreZoom
                ? Mathf.Max(_originalFarClip, 1500f)
                : _originalFarClip;
        }

        private void HospitalsPressed()
        {
            _allHospitals = !_allHospitals;
            RefreshHospitals();
        }

        private void RefreshHospitals()
        {
            var scene = _map?.App?.MetagameMapScene;
            if (scene == null) return;
            foreach (var pin in scene.MapPins)
            {
                if (pin is MapPinHospital hospital)
                    hospital.Refresh(true);
            }
            foreach (var visual in scene.MapVisuals)
            {
                if (visual != null) visual.Refresh();
            }
            try { UpdateBoundsMethod?.Invoke(_map.MapUI, null); }
            catch (Exception e)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Minimapa: límites de cámara: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (_canvasCallbackRegistered)
            {
                Canvas.willRenderCanvases -= PositionNativeButtonsBeforeRender;
                _canvasCallbackRegistered = false;
            }
            if (_current != this) return;
            _allHospitals = false;
            _moreZoom = false;
            if (_map?.CameraLogic?.CameraComponent != null && _originalFarClip > 0f)
                _map.CameraLogic.CameraComponent.farClipPlane = _originalFarClip;
            _current = null;
        }

        internal static void RevealHospital(MapPinHospital pin)
        {
            if (!ShowingAllHospitals || pin == null || pin.LevelConfig == null) return;
            var mesh = MeshField?.GetValue(pin) as MeshRenderer;
            if (mesh == null) return;
            mesh.gameObject.SetActive(true);

            // La apariencia bloqueada se calcula con IsPlayable, que NO se modifica.
            var level = pin.LevelConfig;
            var metagame = MetagameField?.GetValue(pin) as Metagame;
            var record = RecordField?.GetValue(pin) as MetagameHospitalRecord;
            bool playable = metagame != null && DLCUtils.IsDLCInstalled(level.GetRequiredDlcPack()) &&
                (level.IsPlayable(metagame) || (record != null && record.IsPlayable()));
            if (!playable)
            {
                // Los hospitales revelados mantienen el icono gris nativo,
                // sin estrellas, premios ni marcas de hospital jugable.
                var stars = StarsField?.GetValue(pin) as GameObject[];
                if (stars != null)
                    foreach (var star in stars)
                        if (star != null && star.activeSelf) star.SetActive(false);
                // Desactivar también la insignia Remix azul en hospitales bloqueados.
                var remixBadge = AccessTools.Field(typeof(MapPinHospital), "_remixBadgeIcon")
                    ?.GetValue(pin) as Component;
                if (remixBadge != null) remixBadge.gameObject.SetActive(false);
                var hovered = HoverField?.GetValue(pin) is bool value && value;
                var material = (hovered ? LockedHoverField : LockedField)?.GetValue(pin) as Material;
                if (material != null) mesh.material = material;
            }
        }

        internal static void RevealVisuals(MapVisualsActivation visuals)
        {
            if (!ShowingAllHospitals || visuals == null) return;
            // Muestra el hospital en su estado NATIVO bloqueado o jugable.
            // No altera estrellas, progreso, dinero, DLC o prerequisitos.
            var metagame = VisibilityMetagameField?.GetValue(visuals) as Metagame;
            var record = VisibilityRecordField?.GetValue(visuals) as MetagameHospitalRecord;
            var shared = VisibilityLevelField?.GetValue(visuals);
            LevelConfig level = null;
            if (shared != null)
            {
                var instance = AccessTools.Property(shared.GetType(), "Instance");
                if (instance != null) level = instance.GetValue(shared, null) as LevelConfig;
            }
            if (level == null || metagame == null) return;
            bool playable = DLCUtils.IsDLCInstalled(level.GetRequiredDlcPack()) &&
                (level.IsPlayable(metagame) || (record != null && record.IsPlayable()));
            visuals.SetLevelPlayable(playable);
        }
    }

    [HarmonyPatch(typeof(MetagameMapCareerUI), "Setup")]
    internal static class ScenarioCareerMapControlsSetupPatch
    {
        private static void Postfix(MetagameMapCareerUI __instance, MetagameMap metagameMap)
        {
            if (__instance == null || __instance.ButtonsMenu == null) return;
            var controls = __instance.GetComponent<ScenarioCareerMapControls>() ??
                __instance.gameObject.AddComponent<ScenarioCareerMapControls>();
            controls.Initialise(metagameMap, __instance.ButtonsMenu);
        }
    }

    [HarmonyPatch(typeof(TopDownCameraLogic), "GetOffset")]
    internal static class ScenarioCareerMapControlsZoomPatch
    {
        private static void Postfix(TopDownCameraLogic __instance, ref Vector3 __result)
        {
            float zoom;
            if (!ScenarioCareerMapControls.ExtraZoomFor(__instance, out zoom)) return;
            // Zoom 0 conserva exactamente la distancia minima nativa.
            // Sólo aumenta la distancia del tramo de alejamiento, máximo ~1.60x.
            // No se modifica la velocidad ni el incremento de la rueda.
            // No altera el acercamiento: empieza a ampliar sólo desde el
            // 45% del recorrido hacia el extremo lejano. Máximo ~1,60x.
            var farFactor = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.45f, 1f, zoom));
            __result *= Mathf.Lerp(1f, 1.60f, farFactor);
        }
    }

    [HarmonyPatch(typeof(MapPinHospital), "Refresh")]
    internal static class ScenarioCareerMapControlsPinPatch
    {
        private static void Postfix(MapPinHospital __instance) =>
            ScenarioCareerMapControls.RevealHospital(__instance);
    }

    [HarmonyPatch(typeof(MapPinHospital), "OnCursorOver")]
    internal static class ScenarioCareerMapControlsHoverPatch
    {
        private static void Postfix(MapPinHospital __instance) =>
            ScenarioCareerMapControls.RevealHospital(__instance);
    }

    [HarmonyPatch(typeof(MapPinHospital), "IsPinUnlocked")]
    internal static class ScenarioCareerMapControlsAvailabilityPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ScenarioCareerMapControls.ShowingAllHospitals) __result = true;
        }
    }

    [HarmonyPatch(typeof(MapVisualsActivation), "Refresh")]
    internal static class ScenarioCareerMapControlsVisualPatch
    {
        private static void Postfix(MapVisualsActivation __instance) =>
            ScenarioCareerMapControls.RevealVisuals(__instance);
    }
}

namespace UnderPressure.Editor
{
    // Deja una franja libre para los tres botones de la cabecera sin alterar
    // el tamaño ni el estilo del panel de Marcadores.
    [HarmonyPatch(typeof(MetagameLeaderboardsMenu), "Setup")]
    internal static class ScenarioCareerLowerLeaderboardsPatch
    {
        private static void Postfix(MetagameLeaderboardsMenu __instance)
        {
            if (__instance == null || __instance.GetComponent<ScenarioLeaderboardsMoved>() != null)
                return;
            var rect = __instance.transform as RectTransform;
            if (rect == null) return;
            rect.anchoredPosition += new Vector2(0f, -78f);
            __instance.gameObject.AddComponent<ScenarioLeaderboardsMoved>();
        }
    }

    internal sealed class ScenarioLeaderboardsMoved : MonoBehaviour { }
}
// ===== END ORIGINAL: ScenarioCareerMapControls.cs =====
