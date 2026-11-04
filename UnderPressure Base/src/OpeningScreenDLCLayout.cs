// Updated: 2026-10-19 - Panel principal con ancho y alto manuales.
// UnderPressure Base: ordena los iconos DLC y compacta el menu principal.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    [HarmonyPatch(typeof(OpeningScreen), "ShowNormalContent")]
    internal static class OpeningScreenDLCLayoutPatch
    {
        private static void Postfix(OpeningScreen __instance)
        {
            if (__instance == null || !UnderPressurePlugin.IsModEnabled) return;
            var controller = __instance.GetComponent<OpeningScreenDLCLayoutController>();
            if (controller == null)
                controller = __instance.gameObject.AddComponent<OpeningScreenDLCLayoutController>();
            controller.ApplyWhenReady(__instance);
        }
    }

    internal sealed class OpeningScreenDLCLayoutController : MonoBehaviour
    {
        // PANEL DEL MENU PRINCIPAL: modifica solo estos dos valores.
        // Son unidades de interfaz (equivalentes a px con Canvas a escala 1).
        private const float PanelAncho = 400f;
        private const float PanelAlto = 150f;

        private const float MenuButtonsDown = 27f;
        private const float RowPadding = 7f;
        private const float IconGap = 5f;

        private static readonly FieldInfo DlcButtonsField =
            AccessTools.Field(typeof(OpeningScreen), "_dlcButtons");
        private static readonly FieldInfo DlcDefinitionField =
            AccessTools.Field(typeof(DLCBuyButton), "_dlcDefinition");
        private static readonly FieldInfo SettingsButtonField =
            AccessTools.Field(typeof(OpeningScreenMenu), "_settingsButton");

        private RectTransform _backdrop;
        private RectTransform _bottomRow;
        private RectTransform _buttonLayout;
        private OpeningScreen _screen;
        private RectTransform _mainBackdrop;
        private Vector2 _buttonLayoutOriginalPosition;
        private bool _buttonLayoutCaptured;
        private Coroutine _pending;
        private bool _reportedMissing;
        private RectTransform[] _panelGraphics;
        private bool _panelInitialized;

        internal void ApplyWhenReady(OpeningScreen screen)
        {
            _screen = screen;
            if (_pending != null) StopCoroutine(_pending);
            _pending = StartCoroutine(ApplyAfterLayout(screen));
        }

        private IEnumerator ApplyAfterLayout(OpeningScreen screen)
        {
            // Espera a que Unity haya construido el layout original del menu.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            _pending = null;
            if (screen == null || !screen.IsVisible) yield break;

            var nativeButtons = DlcButtonsField == null
                ? null
                : DlcButtonsField.GetValue(screen) as DLCBuyButton[];
            if (nativeButtons == null || nativeButtons.Length < 3) yield break;

            // Hospital Pass no forma parte de OpeningScreen._dlcButtons aunque
            // si es un DLCBuyButton de la escena. Recogemos los once botones.
            var rects = screen.GetComponentsInChildren<DLCBuyButton>(true)
                .Where(b => b != null && b.gameObject.activeInHierarchy)
                .OrderBy(DlcGroup)
                .ThenBy(b => NativeIndex(nativeButtons, b))
                .Select(b => b.GetComponent<RectTransform>())
                .Where(r => r != null && r.gameObject.activeInHierarchy)
                .Distinct().ToList();
            if (rects.Count < 3) yield break;

            Canvas.ForceUpdateCanvases();
            if (_bottomRow == null)
            {
                var centers = rects.Select(r => new IconInfo(r, Center(r))).ToList();
                _backdrop = FindBackdrop(screen, centers);
                var canvas = AccessTools.Field(typeof(OpeningScreen), "_rootCanvas")
                    ?.GetValue(screen) as Canvas;
                if (canvas == null) yield break;
                var row = new GameObject("UP DLC Bottom Row", typeof(RectTransform));
                row.transform.SetParent(canvas.transform, false);
                _bottomRow = (RectTransform)row.transform;
                _bottomRow.anchorMin = _bottomRow.anchorMax = new Vector2(0f, 0f);
                _bottomRow.pivot = new Vector2(0f, 0f);
                _bottomRow.anchoredPosition = Vector2.zero;
                var originalImage = _backdrop == null ? null : _backdrop.GetComponent<Image>();
                if (originalImage != null) originalImage.enabled = false;
            }

            ArrangeBottomRow(rects, screen);
            MoveButtonLayoutDown(screen);
            FindMainBackdrop(screen);
            FitMainBackdropToLogo();
        }

        private void ArrangeBottomRow(List<RectTransform> rects, OpeningScreen screen)
        {
            if (_bottomRow == null || rects == null || rects.Count == 0) return;
            var canvas = AccessTools.Field(typeof(OpeningScreen), "_rootCanvas")
                ?.GetValue(screen) as Canvas;
            var canvasRect = canvas == null ? null : canvas.transform as RectTransform;
            if (canvasRect == null) return;

            var widths = new List<float>(rects.Count);
            var heights = new List<float>(rects.Count);
            foreach (var rect in rects)
            {
                widths.Add(Mathf.Max(1f, rect.rect.width));
                heights.Add(Mathf.Max(1f, rect.rect.height));
            }
            var iconsWidth = widths.Sum();
            var minimumWidth = iconsWidth + IconGap * (rects.Count - 1) + RowPadding * 2f;
            var rowWidth = minimumWidth;
            var rowHeight = heights.Max() + RowPadding * 2f;
            _bottomRow.sizeDelta = new Vector2(rowWidth, rowHeight);

            var x = RowPadding;
            for (var i = 0; i < rects.Count; ++i)
            {
                var rect = rects[i];
                if (rect.parent != _bottomRow) rect.SetParent(_bottomRow, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                rect.sizeDelta = new Vector2(widths[i], heights[i]);
                rect.anchoredPosition = new Vector2(x + widths[i] * .5f, 0f);
                x += widths[i] + IconGap;
            }
        }

        private void MoveButtonLayoutDown(OpeningScreen screen)
        {
            var menu = screen.GetComponentInChildren<OpeningScreenMenu>(true);
            var settings = menu == null ? null : SettingsButtonField?.GetValue(menu) as Component;
            var layout = settings?.transform?.parent?.parent as RectTransform;
            if (layout == null) return;
            if (!_buttonLayoutCaptured || _buttonLayout != layout)
            {
                _buttonLayout = layout;
                _buttonLayoutOriginalPosition = layout.anchoredPosition;
                _buttonLayoutCaptured = true;
            }
            _buttonLayout.anchoredPosition = _buttonLayoutOriginalPosition +
                new Vector2(0f, -MenuButtonsDown);
        }

        private static DLCItemDefinition Definition(DLCBuyButton button)
        {
            if (button == null || DlcDefinitionField == null) return null;
            var wrapper = DlcDefinitionField.GetValue(button) as SharedInstance<DLCItemDefinition>;
            return wrapper == null ? null : wrapper.Instance;
        }

        private static int DlcGroup(DLCBuyButton button)
        {
            var definition = Definition(button);
            if (definition == null) return 1;
            if (definition.IsHospitalPassSignup) return 2;
            return definition.Name.Term != null &&
                   definition.Name.Term.StartsWith("Levels/DLC_", StringComparison.Ordinal)
                ? 0 : 1;
        }

        private static int NativeIndex(DLCBuyButton[] nativeButtons, DLCBuyButton button)
        {
            var index = Array.IndexOf(nativeButtons, button);
            return index >= 0 ? index : int.MaxValue;
        }

        private static RectTransform FindNestedRect(Transform root, string name)
        {
            if (root == null) return null;
            var direct = root.Find(name) as RectTransform;
            if (direct != null) return direct;
            var candidates = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < candidates.Length; ++i)
                if (candidates[i] != null &&
                    string.Equals(candidates[i].name, name, StringComparison.Ordinal))
                    return candidates[i];
            return null;
        }

        private void FindMainBackdrop(OpeningScreen screen)
        {
            var menu = screen.GetComponentInChildren<OpeningScreenMenu>(true);
            var canvas = AccessTools.Field(typeof(OpeningScreen), "_rootCanvas")
                ?.GetValue(screen) as Canvas;
            var primary = menu == null ? null : menu.transform;
            var fullRoot = canvas == null ? screen.transform : canvas.transform;
            var backdrop = FindNestedRect(primary, "BackPanel_FrostedGlass") ??
                FindNestedRect(fullRoot, "BackPanel_FrostedGlass");
            if (backdrop != _mainBackdrop)
            {
                _mainBackdrop = backdrop;
                _panelGraphics = null;
                _panelInitialized = false;
            }
            if (_mainBackdrop == null && !_reportedMissing)
            {
                _reportedMissing = true;
                UnityEngine.Debug.LogWarning(
                    "Menu UnderPressure: BackPanel_FrostedGlass no encontrado.");
            }
        }

        // Solo ajustamos el grafico del fondo, no los botones ni el logo.
        private void FitMainBackdropToLogo()
        {
            if (_mainBackdrop == null) return;
            if (!_panelInitialized)
            {
                var graphics = _mainBackdrop.GetComponentsInChildren<Graphic>(true);
                var panels = new List<RectTransform>();
                foreach (var graphic in graphics)
                {
                    if (graphic == null || graphic is Text) continue;
                    var rect = graphic.rectTransform;
                    if (rect != null && rect.rect.width >= 150f)
                        panels.Add(rect);
                }
                _panelGraphics = panels.ToArray();
                _panelInitialized = true;
            }
            if (_panelGraphics == null) return;
            foreach (var rect in _panelGraphics)
            {
                if (rect == null) continue;
                var oldWidth = rect.rect.width;
                var oldHeight = rect.rect.height;
                if (Mathf.Abs(oldWidth - PanelAncho) < .01f &&
                    Mathf.Abs(oldHeight - PanelAlto) < .01f) continue;

                // Mantener fijas la esquina izquierda y la superior.
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PanelAncho);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, PanelAlto);
                rect.localPosition += new Vector3(
                    (PanelAncho - oldWidth) * rect.pivot.x,
                    (oldHeight - PanelAlto) * (1f - rect.pivot.y), 0f);
            }
        }

        private void LateUpdate()
        {
            if (_screen == null || !_screen.IsVisible) return;
            if (_mainBackdrop == null) FindMainBackdrop(_screen);
            FitMainBackdropToLogo();
        }

        private sealed class IconInfo
        {
            internal readonly RectTransform Rect;
            internal readonly Vector2 Position;
            internal IconInfo(RectTransform rect, Vector2 position)
            {
                Rect = rect;
                Position = position;
            }
        }

        private static Vector2 Center(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Vector2((corners[0].x + corners[2].x) * 0.5f,
                (corners[0].y + corners[2].y) * 0.5f);
        }

        private static float WorldHeight(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Abs(corners[1].y - corners[0].y);
        }

        private static RectTransform FindBackdrop(OpeningScreen screen, List<IconInfo> icons)
        {
            // Evita seleccionar el panel completo del menu: el fondo DLC debe
            // envolver JUSTO la rejilla de iconos, no toda la pantalla.
            var rootCanvasField = AccessTools.Field(typeof(OpeningScreen), "_rootCanvas");
            var canvas = rootCanvasField == null
                ? screen.GetComponentInChildren<Canvas>(true)
                : rootCanvasField.GetValue(screen) as Canvas;
            if (canvas == null) return null;
            var graphics = canvas.GetComponentsInChildren<Graphic>(true);
            float minX = icons.Min(c => c.Position.x);
            float maxX = icons.Max(c => c.Position.x);
            float minY = icons.Min(c => c.Position.y);
            float maxY = icons.Max(c => c.Position.y);
            float iconsWidth = Mathf.Max(1f, maxX - minX);
            float iconsHeight = Mathf.Max(1f, maxY - minY);
            RectTransform best = null;
            float bestScore = float.MaxValue;
            foreach (var graphic in graphics)
            {
                if (graphic == null || graphic.color.r > 0.55f ||
                    graphic.color.g > 0.55f || graphic.color.b > 0.55f) continue;
                var candidate = graphic.rectTransform;
                if (candidate == null) continue;
                var corners = new Vector3[4];
                candidate.GetWorldCorners(corners);
                float left = Mathf.Min(corners[0].x, corners[2].x);
                float right = Mathf.Max(corners[0].x, corners[2].x);
                float bottom = Mathf.Min(corners[0].y, corners[2].y);
                float top = Mathf.Max(corners[0].y, corners[2].y);
                if (left > minX || right < maxX || bottom > minY || top < maxY) continue;
                float width = right - left;
                float height = top - bottom;
                if (width > iconsWidth * 1.9f || height > iconsHeight * 3.2f)
                    continue;
                float score = width * height;
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }
    }
}
