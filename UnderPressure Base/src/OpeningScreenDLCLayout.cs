// Updated: 2026-10-13
// UnderPressure Base: compacta exclusivamente los iconos DLC del menu principal.
// No cambia los botones, sus tamanos, sus padres ni su funcionamiento nativo.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        // Distancias pequenas: compactan las dos filas sin cambiar el tamano de ningun icono.
        private const float FirstRowDown = 7f;
        private const float HospitalPassUp = 7f;
        private const float BackdropHeightReduction = 11f;
        private const float BackdropWidthReductionPerSide = 5f;

        private static readonly FieldInfo DlcButtonsField =
            AccessTools.Field(typeof(OpeningScreen), "_dlcButtons");

        private readonly Dictionary<RectTransform, Vector2> _originalPositions =
            new Dictionary<RectTransform, Vector2>();

        private RectTransform _backdrop;
        private Vector2 _originalBackdropOffsetMax;
        private Vector2 _originalBackdropOffsetMin;
        private bool _captured;
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

            var buttons = DlcButtonsField == null
                ? null
                : DlcButtonsField.GetValue(screen) as DLCBuyButton[];
            if (buttons == null || buttons.Length < 3) yield break;

            var rects = buttons.Where(b => b != null)
                .Select(b => b.GetComponent<RectTransform>())
                .Where(r => r != null && r.gameObject.activeInHierarchy)
                .Distinct().ToList();
            if (rects.Count < 3) yield break;

            Canvas.ForceUpdateCanvases();
            // Solo clasificar las filas la primera vez. En aperturas sucesivas se
            // restauran las coordenadas capturadas, sin acumular desplazamientos.
            if (!_captured)
            {
                var centers = rects.Select(r => new IconInfo(r, Center(r))).ToList();
                float highest = centers.Max(c => c.Position.y);
                float lowest = centers.Min(c => c.Position.y);
                float typicalHeight = centers.Average(c => WorldHeight(c.Rect));
                if (highest - lowest < typicalHeight * 0.40f) yield break;

                float split = (highest + lowest) * 0.5f;
                var firstRow = centers.Where(c => c.Position.y > split).ToList();
                var lowerRow = centers.Where(c => c.Position.y <= split).ToList();
                if (firstRow.Count < 2 || lowerRow.Count == 0) yield break;

                // El emblema HP del extremo derecho ocupa la segunda fila.
                var hospitalPass = lowerRow.OrderByDescending(c => c.Position.x).First();
                var affected = new HashSet<RectTransform>(firstRow.Select(c => c.Rect));
                affected.Add(hospitalPass.Rect);
                foreach (var rect in affected)
                    _originalPositions[rect] = rect.anchoredPosition;

                _backdrop = FindBackdrop(screen, centers);
                if (_backdrop != null)
                {
                    _originalBackdropOffsetMax = _backdrop.offsetMax;
                    _originalBackdropOffsetMin = _backdrop.offsetMin;
                }

                foreach (var c in firstRow)
                    _originalPositions[c.Rect] += new Vector2(0f, -FirstRowDown);
                _originalPositions[hospitalPass.Rect] += new Vector2(0f, HospitalPassUp);
                _captured = true;
            }

            foreach (var pair in _originalPositions)
                if (pair.Key != null) pair.Key.anchoredPosition = pair.Value;

            // Reduce el borde superior (NO mueve el borde inferior del fondo).
            // Si no se reconoce el fondo original, lo deja intacto.
            if (_backdrop != null)
            {
                _backdrop.offsetMin = _originalBackdropOffsetMin +
                    new Vector2(BackdropWidthReductionPerSide, 0f);
                _backdrop.offsetMax = _originalBackdropOffsetMax +
                    new Vector2(-BackdropWidthReductionPerSide, -BackdropHeightReduction);
            }
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
