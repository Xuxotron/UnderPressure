// Updated: 2026-10-13
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
        private Vector2 _buttonLayoutOriginalPosition;
        private bool _buttonLayoutCaptured;
        private Coroutine _pending;

        internal void ApplyWhenReady(OpeningScreen screen)
        {
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
            FitMainBackdropToLogo(screen);
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

        private static void FitMainBackdropToLogo(OpeningScreen screen)
        {
            var menu = screen.GetComponentInChildren<OpeningScreenMenu>(true);
            var root = menu == null ? null : menu.transform;
            var backdrop = root?.Find("BackPanel_FrostedGlass") as RectTransform;
            var logo = root?.Find("GameLogo") as RectTransform;
            var parent = backdrop == null ? null : backdrop.parent as RectTransform;
            if (backdrop == null || logo == null || parent == null) return;

            Canvas.ForceUpdateCanvases();
            var backdropCorners = new Vector3[4];
            var logoCorners = new Vector3[4];
            backdrop.GetWorldCorners(backdropCorners);
            logo.GetWorldCorners(logoCorners);
            var left = parent.InverseTransformPoint(backdropCorners[0]).x;
            var logoRight = parent.InverseTransformPoint(logoCorners[2]).x;
            var width = Mathf.Max(1f, logoRight - left);
            backdrop.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

            Canvas.ForceUpdateCanvases();
            backdrop.GetWorldCorners(backdropCorners);
            var currentRight = parent.InverseTransformPoint(backdropCorners[2]).x;
            backdrop.anchoredPosition += new Vector2(logoRight - currentRight, 0f);
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
