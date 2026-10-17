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

        internal bool SelectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var full = Path.GetFullPath(path);
            var choice = _choices.FirstOrDefault(item => string.Equals(
                Path.GetFullPath(item.Path), full, StringComparison.OrdinalIgnoreCase));
            if (choice == null) return false;
            Select(choice);
            return true;
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

        internal void Close()
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
            _native = null;
            _nativeName = null;
            _nativeDescription = null;
            _nativeMainText = null;
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
            if (ScenarioConfluxMenu.HandleNativeSetup(__instance))
            {
                __instance.GetComponent<ScenarioCareerMapMenu>()?.Close();
                return;
            }
            __instance.GetComponent<ScenarioConfluxMenu>()?.Close();
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
            var conflux = __instance != null ? __instance.GetComponent<ScenarioConfluxMenu>() : null;
            if (conflux != null && conflux.IsOpen)
                return conflux.BeforeNativeStart();
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
        private static readonly FieldInfo CollaborativeButtonField =
            AccessTools.Field(typeof(CollaborativeSidebarMenu), "_collaborativePortfolioButton");
        private static readonly MethodInfo UpdateBoundsMethod =
            AccessTools.Method(typeof(MetagameMapUI), "SetCameraBoundsToMapPinBounds");
        private static readonly MethodInfo CloseAllMenusMethod =
            AccessTools.Method(typeof(MetagameButtonsMenu), "CloseAllMenus");
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

        internal static void ClosePanelsForCollaborativeMenu()
        {
            if (_current?._nativeMenu == null) return;
            CloseAllMenusMethod?.Invoke(_current._nativeMenu, null);
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
            bool fullscreenOpen = hud != null && hud.IsFullscreenMenuOpen();
            bool visible = !pauseOpen && !fullscreenOpen &&
                _nativeMenu.gameObject.activeInHierarchy;
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

            // Orden original visual: Menu, Carrera, Marcadores. Se toma como
            // referencia el boton real de Superbug para compartir altura y
            // conservar una separacion estable aunque cambie la resolucion.
            float right = _canvasRect.rect.xMax - 106f;
            float centerY = _canvasRect.rect.yMax - 42f;
            const float gap = 14f;
            var collaborative = _map?.HUD?.FindMenu<CollaborativeSidebarMenu>(false);
            var collaborativeButton = collaborative == null ? null :
                CollaborativeButtonField?.GetValue(collaborative) as Component;
            var collaborativeRect = collaborativeButton?.transform as RectTransform;
            if (collaborativeRect != null && collaborativeRect.gameObject.activeInHierarchy)
            {
                collaborativeRect.GetWorldCorners(corners);
                var a = _canvasRect.InverseTransformPoint(corners[0]);
                var b = _canvasRect.InverseTransformPoint(corners[2]);
                right = Mathf.Min(a.x, b.x) - gap;
                centerY = (a.y + b.y) * 0.5f;
            }
            for (int i = _nativeButtons.Length - 1; i >= 0; i--)
            {
                var rect = _nativeButtons[i]?.transform as RectTransform;
                if (rect == null) continue;
                float width = _nativeWidths[i];
                float height = _nativeHeights[i];
                float centerX = right - width * 0.5f;
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

    // Herramienta privada de autor para añadir elementos al mapamundi sin
    // modificar ninguno de los objetos serializados por el juego.
    // Los elementos se definen en EditWorldMap.cs y F9 vuelve a aplicarlos.
    internal sealed class ScenarioWorldMapAuthoring : MonoBehaviour
    {
        private static readonly FieldInfo ScenePinsField =
            AccessTools.Field(typeof(MetagameMapScene), "_mapPins");
        private static readonly FieldInfo UiPinsField =
            AccessTools.Field(typeof(MetagameMapUI), "_mapPins");
        private static readonly MethodInfo RefreshCameraBoundsMethod =
            AccessTools.Method(typeof(MetagameMapUI), "SetCameraBoundsToMapPinBounds");
        private static readonly FieldInfo StarsField =
            AccessTools.Field(typeof(MapPinHospital), "_stars");
        private static readonly FieldInfo MeshIconField =
            AccessTools.Field(typeof(MapPinHospital), "_meshIcon");
        private static readonly FieldInfo HoverField =
            AccessTools.Field(typeof(MapPinHospital), "_cursorOver");
        private static readonly FieldInfo ActiveHospitalEffectField =
            AccessTools.Field(typeof(MapPinHospital), "_activeHospitalEffect");
        private static readonly FieldInfo[] PinMaterialFields =
        {
            AccessTools.Field(typeof(MapPinHospital), "_materialDefault"),
            AccessTools.Field(typeof(MapPinHospital), "_materialHighlighted"),
            AccessTools.Field(typeof(MapPinHospital), "_materialLocked"),
            AccessTools.Field(typeof(MapPinHospital), "_materialLockedHighlighted"),
            AccessTools.Field(typeof(MapPinHospital), "_materialDefaultRemix"),
            AccessTools.Field(typeof(MapPinHospital), "_materialHighlightedRemix"),
            AccessTools.Field(typeof(MapPinHospital), "_materialLockedRemix"),
            AccessTools.Field(typeof(MapPinHospital), "_materialLockedHighlightedRemix")
        };
        private static readonly Dictionary<MapPinHospital, string> PinMapPaths =
            new Dictionary<MapPinHospital, string>();
        private static readonly Dictionary<MapPinHospital, WorldMapConflux> ConfluxPins =
            new Dictionary<MapPinHospital, WorldMapConflux>();
        private static readonly Dictionary<MapPinHospital, PinIconMaterials> GeneratedPinIconMaterials =
            new Dictionary<MapPinHospital, PinIconMaterials>();

        private sealed class PinIconMaterials
        {
            internal Material Off;
            internal Material On;
        }

        private MetagameMap _map;
        private Transform _sceneRoot;
        private Transform _generatedRoot;
        private readonly List<MapPinHospital> _generatedHospitalPins =
            new List<MapPinHospital>();
        private bool _debugVisible;
        private Vector3 _cursorPosition;
        private bool _hasCursorPosition;
        private string _catalogPath;
        private readonly Dictionary<string, Texture2D> _pinIconTextures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        internal void Initialise(MetagameMap map)
        {
            if (map == null || map.App?.MetagameMapScene?.RootObject == null) return;
            if (_map != null && _map != map) ClearGenerated();
            _map = map;
            _sceneRoot = map.App.MetagameMapScene.RootObject.transform;
            var folder = Path.Combine(ScenarioEditorStorage.Folder, "_author");
            _catalogPath = Path.Combine(folder, "world_map_assets.txt");
            Reload();
        }

        internal void InitialiseOpeningScreen(App app, MetagameMapScene mapScene)
        {
            var map = app?.MetagameMap;
            if (map != null)
            {
                Initialise(map);
                return;
            }

            var root = mapScene?.RootObject;
            if (root == null) return;
            if (_map == null && _sceneRoot == root.transform && _generatedRoot != null) return;
            ClearGenerated();
            _map = null;
            _sceneRoot = root.transform;
            var folder = Path.Combine(ScenarioEditorStorage.Folder, "_author");
            _catalogPath = Path.Combine(folder, "world_map_assets.txt");
            Reload();
        }

        private void Update()
        {
            if (_map == null || !_map.IsVisible) return;
            var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (control && alt && Input.GetKeyDown(KeyCode.F10))
            {
                _debugVisible = !_debugVisible;
                if (_debugVisible)
                {
                    ExportAssetCatalog();
                    Reload();
                }
            }
            if (!_debugVisible) return;
            _hasCursorPosition = TryGetCursorPosition(out _cursorPosition);
            if (Input.GetKeyDown(KeyCode.F9)) Reload();
            if (control && alt && Input.GetKeyDown(KeyCode.C) && _hasCursorPosition)
            {
                GUIUtility.systemCopyBuffer = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "new Vector3({0:0.###}f, {1:0.###}f, {2:0.###}f)",
                    _cursorPosition.x, _cursorPosition.y, _cursorPosition.z);
            }
        }

        private void OnGUI()
        {
            if (!_debugVisible || _map == null || !_map.IsVisible) return;
            var width = Mathf.Min(670f, Screen.width - 24f);
            GUI.Box(new Rect(12f, 12f, width, 88f), GUIContent.none);
            GUI.Label(new Rect(24f, 18f, width - 24f, 24f),
                EditorLocalization.Get("world_map.author.title"));
            var position = _hasCursorPosition
                ? EditorLocalization.Format("world_map.author.position",
                    _cursorPosition.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    _cursorPosition.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    _cursorPosition.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                : EditorLocalization.Get("world_map.author.no_surface");
            GUI.Label(new Rect(24f, 42f, width - 24f, 22f), position);
            GUI.Label(new Rect(24f, 64f, width - 24f, 22f),
                EditorLocalization.Get("world_map.author.shortcuts"));
        }

        private void Reload()
        {
            ClearGenerated();
            if (_sceneRoot == null) return;
            try
            {
                EnsureGeneratedRoot();
                var meshes = EditWorldMap.Meshes ?? new WorldMapMesh[0];
                for (var index = 0; index < meshes.Length; ++index)
                    AddMesh(meshes[index], index);
                var hospitals = _map == null
                    ? new WorldMapHospital[0]
                    : EditWorldMap.Hospitals ?? new WorldMapHospital[0];
                for (var index = 0; index < hospitals.Length; ++index)
                    AddHospital(hospitals[index], index);
                var confluxes = _map == null
                    ? new WorldMapConflux[0]
                    : EditWorldMap.Confluxes ?? new WorldMapConflux[0];
                for (var index = 0; index < confluxes.Length; ++index)
                    AddConflux(confluxes[index], index);
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Mapamundi de autor cargado: " + meshes.Length + " mallas, " +
                    hospitals.Length + " hospitales y " + confluxes.Length + " Conflux.");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo cargar el mapamundi de autor: " + exception);
            }
        }

        private void AddMesh(WorldMapMesh placement, int index)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.Asset)) return;
            GameObject source;
            if (placement.Source == WorldMapSource.Scene)
                source = FindSceneObject(placement.Asset);
            else if (placement.Source == WorldMapSource.Bundle)
                source = FindBundlePrefab(placement.Asset);
            else
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Origen desconocido en EditWorldMap.cs: " + placement.Source);
                return;
            }
            if (source == null) return;
            var clone = Instantiate(source);
            clone.name = "UP_WorldMesh_" + SafeId(placement.Id, index);
            clone.transform.SetParent(_generatedRoot, true);
            clone.transform.position = placement.Position;
            clone.transform.localScale *= placement.ScaleMultiplier;
            clone.SetActive(true);
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
            }
            foreach (var particles in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.gameObject.SetActive(true);
                var main = particles.main;
                main.simulationSpeed *= placement.SpeedMultiplier;
                if (placement.TintMagenta) TintParticleSystem(particles);
                if (string.Equals(placement.Id, "conflux_swirl",
                    StringComparison.OrdinalIgnoreCase))
                    RestoreSwirlOpacity(particles);
                if (placement.CircularEmissionRadius > 0f)
                {
                    var shape = particles.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = placement.CircularEmissionRadius;
                    shape.radiusThickness = 1f;
                    shape.arc = 360f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                }
                particles.Clear(true);
                particles.Play(true);
            }
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Malla de autor creada: " + clone.name + " en " + clone.transform.position +
                ", renderers=" + clone.GetComponentsInChildren<Renderer>(true).Length + ".");
        }

        private void AddHospital(WorldMapHospital placement, int index)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.Template)) return;
            var source = FindSceneObject(placement.Template);
            if (source == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Plantilla de hospital no encontrada: " + placement.Template);
                return;
            }
            // La raiz completa conserva juntos PinMesh, PinPlane, estrellas,
            // materiales, escalas y LOD del hospital nativo elegido.
            var clone = Instantiate(source);
            clone.name = "UP_WorldHospital_" + SafeId(placement.Id, index);
            clone.transform.SetParent(_generatedRoot, true);
            clone.transform.position = placement.Position;
            clone.SetActive(true);
            var hospital = clone.GetComponentInChildren<MapPinHospital>(true);
            if (hospital == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "La plantilla no contiene un MapPinHospital: " + placement.Template);
                Destroy(clone);
                return;
            }

            // Registrar e inicializar exactamente como hace MetagameMapCareerUI.
            // El segundo pin comparte LevelConfig y progreso con el hospital nativo.
            hospital.enabled = true;
            hospital.Initialise(_map.Metagame, _map, _map.App.SaveSystem);
            AppendPin(ScenePinsField, _map.App.MetagameMapScene, hospital);
            AppendPin(UiPinsField, _map.MapUI, hospital);
            _generatedHospitalPins.Add(hospital);
            var mapPath = ResolveMapPath(placement.MapPath);
            if (mapPath != null) PinMapPaths[hospital] = mapPath;
            ApplyPinIcons(hospital, "UnderMapsOFF.png", "UnderMapsON.png",
                "UP_UnderMaps");
            RefreshCameraBoundsMethod?.Invoke(_map.MapUI, null);
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Pin de autor creado: " + clone.name + " en " + clone.transform.position +
                ", renderers=" + clone.GetComponentsInChildren<Renderer>(true).Length + ".");
        }

        private void AddConflux(WorldMapConflux placement, int index)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.Template)) return;
            var source = FindSceneObject(placement.Template);
            if (source == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Plantilla de Conflux no encontrada: " + placement.Template);
                return;
            }
            var clone = Instantiate(source);
            clone.name = "UP_WorldConflux_" + SafeId(placement.Id, index);
            clone.transform.SetParent(_generatedRoot, true);
            clone.transform.position = placement.Position;
            clone.SetActive(true);
            var pin = clone.GetComponentInChildren<MapPinHospital>(true);
            if (pin == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "La plantilla de Conflux no contiene un MapPinHospital: " + placement.Template);
                Destroy(clone);
                return;
            }
            pin.enabled = true;
            pin.Initialise(_map.Metagame, _map, _map.App.SaveSystem);
            AppendPin(ScenePinsField, _map.App.MetagameMapScene, pin);
            AppendPin(UiPinsField, _map.MapUI, pin);
            _generatedHospitalPins.Add(pin);
            ConfluxPins[pin] = placement;
            ApplyPinIcons(pin, "confluxOFF.png", "confluxON.png", "UP_Conflux");
            RefreshCameraBoundsMethod?.Invoke(_map.MapUI, null);
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Pin Conflux creado: " + clone.name + " en " + clone.transform.position + ".");
        }

        private static string ResolveMapPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var root = Path.GetFullPath(ScenarioEditorStorage.Folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".upmap", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(full))
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Mapa asignado al pin no encontrado: " + path);
                return null;
            }
            return full;
        }

        internal static bool TryGetMapPath(MapPinHospital pin, out string path)
        {
            path = null;
            return pin != null && PinMapPaths.TryGetValue(pin, out path);
        }

        internal static bool TryGetConflux(MapPinHospital pin, out WorldMapConflux conflux)
        {
            conflux = null;
            return pin != null && ConfluxPins.TryGetValue(pin, out conflux);
        }

        internal static void RefreshGeneratedPinVisual(MapPinHospital pin)
        {
            if (pin == null || !GeneratedPinIconMaterials.TryGetValue(pin, out var icons)) return;
            var stars = StarsField?.GetValue(pin) as GameObject[];
            if (stars != null)
                foreach (var star in stars)
                    if (star != null) star.SetActive(false);

            var hovered = HoverField != null && (bool)HoverField.GetValue(pin);
            var material = hovered ? icons.On : icons.Off;
            if (material != null)
            {
                var mesh = MeshIconField?.GetValue(pin) as MeshRenderer;
                if (mesh != null) mesh.sharedMaterial = material;
            }

            if (!ConfluxPins.ContainsKey(pin)) return;
            var activeEffect = ActiveHospitalEffectField?.GetValue(pin) as GameObject;
            if (activeEffect == null) return;
            activeEffect.SetActive(false);
            Destroy(activeEffect);
            ActiveHospitalEffectField.SetValue(pin, null);
        }

        private static void TintParticleSystem(ParticleSystem particles)
        {
            var magenta = new Color(1f, 0f, 0.72f, 1f);
            var main = particles.main;
            main.startColor = TintGradient(main.startColor, magenta);
            var lifetime = particles.colorOverLifetime;
            if (lifetime.enabled) lifetime.color = TintGradient(lifetime.color, magenta);
            var speed = particles.colorBySpeed;
            if (speed.enabled) speed.color = TintGradient(speed.color, magenta);
            var trails = particles.trails;
            if (trails.enabled)
            {
                trails.colorOverLifetime = TintGradient(trails.colorOverLifetime, magenta);
                trails.colorOverTrail = TintGradient(trails.colorOverTrail, magenta);
            }
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;
            foreach (var material in renderer.materials)
            {
                if (material == null) continue;
                SetMaterialMagenta(material, "_Color", magenta);
                SetMaterialMagenta(material, "_TintColor", magenta);
                SetMaterialMagenta(material, "_EmissionColor", magenta);
                SetMaterialMagenta(material, "_EmisColor", magenta);
                SetMaterialMagenta(material, "_BaseColor", magenta);
                SetMaterialMagenta(material, "_ParticleColor", magenta);
            }
            var trailMaterial = renderer.trailMaterial;
            if (trailMaterial == null) return;
            SetMaterialMagenta(trailMaterial, "_Color", magenta);
            SetMaterialMagenta(trailMaterial, "_TintColor", magenta);
            SetMaterialMagenta(trailMaterial, "_EmissionColor", magenta);
        }

        private static void RestoreSwirlOpacity(ParticleSystem particles)
        {
            var lifetime = particles.colorOverLifetime;
            var source = lifetime.color;
            var gradient = source.gradient;
            if (gradient == null) return;

            // Curva alfa serializada del Swirl original del DLC temporal.
            var restored = new Gradient { mode = gradient.mode };
            restored.SetKeys(gradient.colorKeys, new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.7254902f, 0.0500038f),
                new GradientAlphaKey(0.7333333f, 0.9473106f),
                new GradientAlphaKey(0f, 1f)
            });
            lifetime.enabled = true;
            lifetime.color = new ParticleSystem.MinMaxGradient(restored);
        }

        private static ParticleSystem.MinMaxGradient TintGradient(
            ParticleSystem.MinMaxGradient source, Color magenta)
        {
            switch (source.mode)
            {
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(
                        TintColor(source.colorMin, magenta), TintColor(source.colorMax, magenta));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(TintUnityGradient(source.gradient, magenta));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(
                        TintUnityGradient(source.gradientMin, magenta),
                        TintUnityGradient(source.gradientMax, magenta));
                case ParticleSystemGradientMode.RandomColor:
                    var random = new ParticleSystem.MinMaxGradient(
                        TintUnityGradient(source.gradient, magenta));
                    random.mode = ParticleSystemGradientMode.RandomColor;
                    return random;
                default:
                    return new ParticleSystem.MinMaxGradient(TintColor(source.color, magenta));
            }
        }

        private static Gradient TintUnityGradient(Gradient source, Color magenta)
        {
            if (source == null) return new Gradient();
            var colors = source.colorKeys;
            for (var index = 0; index < colors.Length; ++index)
                colors[index].color = TintColor(colors[index].color, magenta);
            var result = new Gradient { mode = source.mode };
            result.SetKeys(colors, source.alphaKeys);
            return result;
        }

        private static Color TintColor(Color source, Color magenta)
        {
            var intensity = Mathf.Max(1f, source.r, source.g, source.b);
            return new Color(magenta.r * intensity, magenta.g * intensity,
                magenta.b * intensity, source.a);
        }

        private static void SetMaterialMagenta(Material material, string property, Color magenta)
        {
            if (!material.HasProperty(property)) return;
            var original = material.GetColor(property);
            var intensity = Mathf.Max(1f, original.r, original.g, original.b);
            material.SetColor(property, new Color(
                magenta.r * intensity, magenta.g * intensity, magenta.b * intensity, original.a));
        }

        private void ApplyPinIcons(MapPinHospital pin, string offFile, string onFile,
            string materialName)
        {
            var mesh = MeshIconField?.GetValue(pin) as MeshRenderer;
            if (mesh == null) return;
            var off = CreatePinIconMaterial(mesh, offFile, materialName + "OFF");
            var on = CreatePinIconMaterial(mesh, onFile, materialName + "ON");
            if (off == null || on == null)
            {
                if (off != null) Destroy(off);
                if (on != null) Destroy(on);
                return;
            }

            for (var index = 0; index < PinMaterialFields.Length; ++index)
                PinMaterialFields[index]?.SetValue(pin, (index & 1) == 0 ? off : on);
            GeneratedPinIconMaterials[pin] = new PinIconMaterials { Off = off, On = on };
            RefreshGeneratedPinVisual(pin);
        }

        private Material CreatePinIconMaterial(MeshRenderer mesh, string fileName,
            string materialName)
        {
            var path = Path.Combine(UnderPressure.LoaderRuntime.Paths.PluginPath,
                "Assets", fileName);
            if (!File.Exists(path))
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Icono de pin no encontrado: " + path);
                return null;
            }
            if (!_pinIconTextures.TryGetValue(fileName, out var texture) || texture == null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                {
                    name = "UP_PinIcon_" + Path.GetFileNameWithoutExtension(fileName),
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                {
                    Destroy(texture);
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "No se pudo decodificar el icono de pin: " + path);
                    return null;
                }
                _pinIconTextures[fileName] = texture;
            }

            var shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            var original = mesh.sharedMaterial;
            if (shader == null && original == null) return null;
            var material = shader != null ? new Material(shader) : new Material(original);
            material.name = materialName;
            material.mainTexture = texture;
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            return material;
        }

        private static void AppendPin(FieldInfo field, object owner, MapPin pin)
        {
            if (field == null || owner == null || pin == null) return;
            var pins = field.GetValue(owner) as MapPin[] ?? new MapPin[0];
            if (pins.Any(candidate => ReferenceEquals(candidate, pin))) return;
            var expanded = new MapPin[pins.Length + 1];
            Array.Copy(pins, expanded, pins.Length);
            expanded[pins.Length] = pin;
            field.SetValue(owner, expanded);
        }

        private static void RemovePin(FieldInfo field, object owner, MapPin pin)
        {
            if (field == null || owner == null || pin == null) return;
            var pins = field.GetValue(owner) as MapPin[];
            if (pins == null) return;
            field.SetValue(owner, pins.Where(candidate =>
                candidate != null && !ReferenceEquals(candidate, pin)).ToArray());
        }

        private GameObject FindSceneObject(string selector)
        {
            if (_sceneRoot == null) return null;
            GameObject match = null;
            var transforms = _sceneRoot.GetComponentsInChildren<Transform>(true);
            foreach (var candidate in transforms)
            {
                var matches = selector.IndexOf('/') >= 0
                    ? string.Equals(ScenePath(candidate), selector, StringComparison.Ordinal)
                    : string.Equals(candidate.name, selector, StringComparison.Ordinal);
                if (!matches) continue;
                if (match != null)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Asset ambiguo; usa su ruta completa de world_map_assets.txt: " + selector);
                    return null;
                }
                match = candidate.gameObject;
            }
            if (match == null)
                UnderPressureEditorPlugin.Log?.LogWarning("Asset del mapamundi no encontrado: " + selector);
            return match;
        }

        private static GameObject FindBundlePrefab(string selector)
        {
            var direct = global::UnderPressure.UnderPressureAssetBundle.LoadAsset<GameObject>(selector);
            if (direct != null) return direct;
            GameObject match = null;
            foreach (var candidate in global::UnderPressure.UnderPressureAssetBundle.GetAllAssets<GameObject>())
            {
                if (candidate == null || !string.Equals(candidate.name, selector, StringComparison.Ordinal))
                    continue;
                if (match != null)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Prefab ambiguo en AssetBundle: " + selector);
                    return null;
                }
                match = candidate;
            }
            if (match == null)
                UnderPressureEditorPlugin.Log?.LogWarning("Prefab no encontrado en AssetBundle: " + selector);
            return match;
        }

        private void ExportAssetCatalog()
        {
            if (_sceneRoot == null || string.IsNullOrEmpty(_catalogPath)) return;
            try
            {
                var folder = Path.GetDirectoryName(_catalogPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                var lines = new List<string>();
                foreach (var candidate in _sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.GetComponent<Renderer>() != null)
                        lines.Add("scene\t" + ScenePath(candidate));
                }
                foreach (var prefab in global::UnderPressure.UnderPressureAssetBundle.GetAllAssets<GameObject>())
                {
                    if (prefab != null) lines.Add("bundle\t" + prefab.name);
                }
                lines.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(_catalogPath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "No se pudo exportar el catalogo del mapamundi: " + exception.Message);
            }
        }

        private string ScenePath(Transform target)
        {
            var parts = new List<string>();
            for (var node = target; node != null; node = node.parent)
            {
                parts.Add(node.name + "[" + node.GetSiblingIndex() + "]");
                if (node == _sceneRoot) break;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private bool TryGetCursorPosition(out Vector3 position)
        {
            position = Vector3.zero;
            var camera = _map?.CameraLogic?.CameraComponent;
            if (camera == null || _sceneRoot == null) return false;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 20000f);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var target = hit.collider == null ? null : hit.collider.transform;
                if (target == null || !target.IsChildOf(_sceneRoot) ||
                    (_generatedRoot != null && target.IsChildOf(_generatedRoot)) ||
                    target.GetComponentInParent<MapPin>() != null) continue;
                position = hit.point;
                return true;
            }
            return false;
        }

        private void EnsureGeneratedRoot()
        {
            if (_generatedRoot != null) return;
            var root = new GameObject("UP_WorldMapAuthoring");
            root.transform.SetParent(_sceneRoot, true);
            _generatedRoot = root.transform;
        }

        private void ClearGenerated()
        {
            foreach (var pin in _generatedHospitalPins)
            {
                if (pin == null) continue;
                RemovePin(ScenePinsField, _map?.App?.MetagameMapScene, pin);
                RemovePin(UiPinsField, _map?.MapUI, pin);
                PinMapPaths.Remove(pin);
                ConfluxPins.Remove(pin);
                if (GeneratedPinIconMaterials.TryGetValue(pin, out var icons))
                {
                    GeneratedPinIconMaterials.Remove(pin);
                    if (icons.Off != null) Destroy(icons.Off);
                    if (icons.On != null) Destroy(icons.On);
                }
                try { pin.PrepareForDestroy(); }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "No se pudo desregistrar el pin de autor: " + exception.Message);
                }
            }
            _generatedHospitalPins.Clear();
            foreach (var texture in _pinIconTextures.Values)
            {
                if (texture != null) Destroy(texture);
            }
            _pinIconTextures.Clear();
            if (_generatedRoot == null) return;
            _generatedRoot.gameObject.SetActive(false);
            Destroy(_generatedRoot.gameObject);
            _generatedRoot = null;
        }

        private static string SafeId(string id, int index) =>
            string.IsNullOrWhiteSpace(id) ? index.ToString() : id.Trim();

        private void OnDestroy()
        {
            ClearGenerated();
            _map = null;
            _sceneRoot = null;
        }
    }


    [HarmonyPatch(typeof(CollaborativeSidebarMenu), "ShowCollaborativeMenu")]
    internal static class ScenarioCollaborativeMenuClosePanelsPatch
    {
        private static void Prefix() =>
            ScenarioCareerMapControls.ClosePanelsForCollaborativeMenu();
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
            var root = metagameMap?.App?.MetagameMapScene?.RootObject;
            if (root == null) return;
            var authoring = root.GetComponent<ScenarioWorldMapAuthoring>() ??
                root.AddComponent<ScenarioWorldMapAuthoring>();
            authoring.Initialise(metagameMap);
        }
    }

    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioWorldMapOpeningScreenPatch
    {
        private static readonly FieldInfo AppField =
            AccessTools.Field(typeof(OpeningScreen), "_app");

        private static void Postfix(OpeningScreen __instance)
        {
            var app = AppField?.GetValue(__instance) as App;
            var root = app?.MetagameMapScene?.RootObject;
            if (root == null) return;
            var authoring = root.GetComponent<ScenarioWorldMapAuthoring>() ??
                root.AddComponent<ScenarioWorldMapAuthoring>();
            authoring.InitialiseOpeningScreen(app, app.MetagameMapScene);
        }
    }

    [HarmonyPatch(typeof(OpeningScreen), "Setup")]
    internal static class ScenarioWorldMapOpeningScreenSetupPatch
    {
        private static void Postfix(App __0, MetagameMapScene __2)
        {
            var root = __2?.RootObject;
            if (root == null) return;
            var authoring = root.GetComponent<ScenarioWorldMapAuthoring>() ??
                root.AddComponent<ScenarioWorldMapAuthoring>();
            authoring.InitialiseOpeningScreen(__0, __2);
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
        private static void Postfix(MapPinHospital __instance)
        {
            ScenarioCareerMapControls.RevealHospital(__instance);
            ScenarioWorldMapAuthoring.RefreshGeneratedPinVisual(__instance);
        }
    }

    [HarmonyPatch(typeof(MapPinHospital), "RefreshVisual")]
    internal static class ScenarioWorldMapConfluxVisualPatch
    {
        private static void Postfix(MapPinHospital __instance) =>
            ScenarioWorldMapAuthoring.RefreshGeneratedPinVisual(__instance);
    }

    [HarmonyPatch(typeof(MapPinHospital), "OnCursorOver")]
    internal static class ScenarioCareerMapControlsHoverPatch
    {
        private static void Postfix(MapPinHospital __instance)
        {
            ScenarioCareerMapControls.RevealHospital(__instance);
            ScenarioWorldMapAuthoring.RefreshGeneratedPinVisual(__instance);
        }
    }

    [HarmonyPatch(typeof(MapPinHospital), "OnSelected")]
    internal static class ScenarioWorldMapHospitalSelectionPatch
    {
        private static void Prefix(MapPinHospital __instance) =>
            ScenarioConfluxMenu.BeginPinSelection(__instance);

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MapPinHospital __instance)
        {
            var isConflux = ScenarioWorldMapAuthoring.TryGetConflux(__instance, out _);
            ScenarioConfluxMenu.EndPinSelection();
            if (isConflux) return;
            if (!ScenarioWorldMapAuthoring.TryGetMapPath(__instance, out var path)) return;
            var selector = UnityEngine.Object.FindObjectOfType<ScenarioCareerMapMenu>();
            if (selector != null && selector.SelectPath(path)) return;
            UnderPressureEditorPlugin.Log?.LogWarning(
                "El pin no pudo seleccionar su mapa asignado: " + path);
        }
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
