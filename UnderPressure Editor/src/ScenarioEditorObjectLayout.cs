// Updated: 2026-10-12
// Distribuye el catalogo exterior en dos filas y anade la etiqueta del buscador.
// Destino: UnderPressure Editor/src/ScenarioEditorObjectLayout.cs
using System;
using HarmonyLib;
using TMPro;
using TH20;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Setup")]
    internal static class ScenarioEditorLandscapeLayoutPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(LandscapeObjectsMenu __instance)
        {
            if (!ScenarioEditorFlow.IsRunning || __instance == null) return;
            try
            {
                var content = AccessTools.Field(typeof(LandscapeObjectsMenu), "_listContent")
                    ?.GetValue(__instance) as GameObject;
                var input = AccessTools.Field(typeof(LandscapeObjectsMenu), "_searchBox")
                    ?.GetValue(__instance) as TMP_InputField;
                if (content == null) return;
                var rect = content.transform as RectTransform;
                if (rect == null) return;

                var horizontal = content.GetComponent<HorizontalLayoutGroup>();
                if (horizontal != null) horizontal.enabled = false;
                var vertical = content.GetComponent<VerticalLayoutGroup>();
                if (vertical != null) vertical.enabled = false;

                var grid = content.GetComponent<GridLayoutGroup>() ?? content.AddComponent<GridLayoutGroup>();
                grid.enabled = true;
                grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                grid.constraintCount = 2;
                grid.startAxis = GridLayoutGroup.Axis.Vertical;
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.childAlignment = TextAnchor.UpperLeft;
                grid.padding = new RectOffset(8, 8, 8, 8);
                grid.cellSize = new Vector2(105f, 88f);
                grid.spacing = new Vector2(7f, 5f);

                var fitter = content.GetComponent<ContentSizeFitter>() ?? content.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = content.GetComponentInParent<ScrollRect>();
                if (scroll != null)
                {
                    scroll.horizontal = true;
                    scroll.vertical = false;
                    scroll.scrollSensitivity = 34f;
                    scroll.content = rect;
                    var scrollRect = scroll.transform as RectTransform;
                    if (scrollRect != null)
                    {
                        // Amplia el panel en altura sin crear una segunda barra de objetos.
                        var size = scrollRect.sizeDelta;
                        size.y = Mathf.Max(size.y, 190f);
                        scrollRect.sizeDelta = size;
                    }
                }

                if (input == null || input.transform.parent == null) return;
                var parent = input.transform.parent;
                if (parent.Find("UP_SearchLabel") != null) return;
                var inputRect = input.transform as RectTransform;
                if (inputRect == null) return;
                var originalPosition = inputRect.anchoredPosition;
                var originalSize = inputRect.sizeDelta;
                inputRect.anchoredPosition = originalPosition + new Vector2(82f, 0f);
                inputRect.sizeDelta = new Vector2(originalSize.x - 82f, originalSize.y);

                var labelGo = new GameObject("UP_SearchLabel", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(parent, false);
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = inputRect.anchorMin;
                labelRect.anchorMax = inputRect.anchorMax;
                labelRect.pivot = inputRect.pivot;
                labelRect.anchoredPosition = originalPosition;
                labelRect.sizeDelta = new Vector2(77f, Mathf.Max(30f, originalSize.y));
                var label = labelGo.GetComponent<TextMeshProUGUI>();
                if (input.textComponent != null)
                {
                    label.font = input.textComponent.font;
                    label.fontSize = input.textComponent.fontSize;
                }
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.raycastTarget = false;
                var translated = EditorLocalization.Get("editor.objects.search");
                label.text = translated == "editor.objects.search" ? "Buscar:" : translated;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Catalogo dos filas: " + ex);
            }
        }
    }
}
