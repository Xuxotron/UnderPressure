// Updated: 2026-10-13 - Iconos sin mover, fondo extendido arriba y scrollbar visible junto al borde de la paleta.
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
                    GrowBackdropOnly(__instance);
                    scroll.horizontal = true;
                    scroll.vertical = false;
                    scroll.scrollSensitivity = 34f;
                    scroll.content = rect;
                    var scrollRect = scroll.transform as RectTransform;
                    if (scrollRect != null)
                    {
                        var size = scrollRect.sizeDelta;
                        size.y = Mathf.Max(size.y, 190f);
                        scrollRect.sizeDelta = size;
                    }
                    RestoreScrollbar(scroll, __instance);
                }
                if (input != null) MoveSearchBelowPlots(input, __instance);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Catalogo exterior: " + ex);
            }
        }

        // No redimensionar el LandscapeObjectsMenu: altera tambien la
        // ubicacion nativa de los iconos. Sólo extender su fondo visual.
        private static void GrowBackdropOnly(LandscapeObjectsMenu menu)
        {
            var root = menu.transform as RectTransform;
            if (root == null) return;
            var extra = root.Find("UP_Backdrop_Extension") as RectTransform;
            if (extra == null)
            {
                var background = new GameObject("UP_Backdrop_Extension",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                background.transform.SetParent(root, false);
                extra = (RectTransform)background.transform;
                var source = root.GetComponent<Image>();
                var target = background.GetComponent<Image>();
                if (source != null)
                {
                    target.sprite = source.sprite;
                    target.type = source.type;
                    target.color = source.color;
                }
                else target.color = new Color(.93f, .93f, .93f, .75f);
                target.raycastTarget = false;
                extra.SetAsFirstSibling();
            }
            extra.anchorMin = Vector2.zero;
            extra.anchorMax = Vector2.one;
            extra.offsetMin = new Vector2(0f, -24f);
            extra.offsetMax = new Vector2(0f, 38f);
        }

        private static void RestoreScrollbar(ScrollRect scroll, LandscapeObjectsMenu menu)
        {
            var host = menu.transform as RectTransform;
            if (host == null) return;
            // Fuera del ScrollRect, NO dentro del Viewport ni de su Mask.
            // Cuando el contenido tiene varias páginas, ScrollRect actualiza
            // el handle automáticamente.
            var trackTransform = host.Find("UP_Catalog_HorizontalScrollbar") as RectTransform;
            if (trackTransform == null)
            {
                var track = new GameObject("UP_Catalog_HorizontalScrollbar",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
                track.transform.SetParent(host, false);
                trackTransform = (RectTransform)track.transform;
                track.GetComponent<Image>().color = new Color(.09f,.15f,.19f,.90f);
                var area = new GameObject("Sliding Area", typeof(RectTransform));
                area.transform.SetParent(track.transform, false);
                var areaRect = (RectTransform)area.transform;
                areaRect.anchorMin = Vector2.zero;
                areaRect.anchorMax = Vector2.one;
                areaRect.offsetMin = new Vector2(3f, 2f);
                areaRect.offsetMax = new Vector2(-3f,-2f);
                var handle = new GameObject("Handle",typeof(RectTransform),
                    typeof(CanvasRenderer),typeof(Image));
                handle.transform.SetParent(area.transform, false);
                var handleRect=(RectTransform)handle.transform;
                handleRect.anchorMin=Vector2.zero;
                handleRect.anchorMax=Vector2.one;
                handleRect.offsetMin=Vector2.zero;
                handleRect.offsetMax=Vector2.zero;
                var handleImage=handle.GetComponent<Image>();
                handleImage.color=new Color(.43f,.80f,.95f,1f);
                var bar=track.GetComponent<Scrollbar>();
                bar.direction=Scrollbar.Direction.LeftToRight;
                bar.targetGraphic=handleImage;
                bar.handleRect=handleRect;
            }
            // La ubicación NO depende del origen del LandscapeObjectsMenu:
            // se sigue la parte inferior REAL del viewport cada fotograma.
            trackTransform.anchorMin = trackTransform.anchorMax = new Vector2(.5f,.5f);
            trackTransform.pivot = new Vector2(.5f,.5f);
            trackTransform.SetAsLastSibling();
            trackTransform.gameObject.SetActive(true);
            var follower = trackTransform.GetComponent<ScenarioCatalogScrollbarFollower>() ??
                trackTransform.gameObject.AddComponent<ScenarioCatalogScrollbarFollower>();
            follower.Initialize(scroll, host);
            // Si el prefab ya tenía una barra oculta en su ScrollRect,
            // no dejarla solapada con la nueva.
            if (scroll.horizontalScrollbar != null &&
                scroll.horizontalScrollbar.transform != trackTransform)
                scroll.horizontalScrollbar.gameObject.SetActive(false);
            scroll.horizontalScrollbar=trackTransform.GetComponent<Scrollbar>();
            scroll.horizontalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalScrollbarSpacing=2f;
            if (scroll.viewport == null)
                scroll.viewport = scroll.content != null ?
                    scroll.content.parent as RectTransform : null;
            follower.Place();
        }

        private static void MoveSearchBelowPlots(TMP_InputField input, LandscapeObjectsMenu menu)
        {
            var inputRect = input.transform as RectTransform;
            var menuRect = menu.transform as RectTransform;
            if (inputRect == null || menuRect == null) return;
            var follower = input.GetComponent<ScenarioEditorSearchBelowPlots>();
            if (follower != null) return;
            var editMenu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            var plotRoot = AccessTools.Field(typeof(EditHospitalMenu), "_plotSelectionRoot")
                ?.GetValue(editMenu) as Transform;
            if (plotRoot == null) return;
            var plotScroll = plotRoot.GetComponentInParent<ScrollRect>();
            var viewport = plotScroll != null ? plotScroll.transform as RectTransform : plotRoot as RectTransform;
            if (viewport == null) return;
            // Mover el input real, no una copia: el filtro sigue conectado a la lista nativa.
            // Conservar el input bajo su menu nativo: si se mueve directamente
            // al Canvas quedaria visible incluso cuando se cerrara la paleta.
            inputRect.SetParent(menuRect, false);
            inputRect.anchorMin = inputRect.anchorMax = new Vector2(0f, 1f);
            inputRect.pivot = new Vector2(0f, 1f);
            inputRect.sizeDelta = new Vector2(147f, 27f);
            var labelObject = new GameObject("UP_SearchLabel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(inputRect.parent, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            if (input.textComponent != null)
            {
                label.font = input.textComponent.font;
                label.fontSize = Mathf.Min(input.textComponent.fontSize, 17f);
            }
            var translated = EditorLocalization.Get("editor.objects.search");
            label.text = translated == "editor.objects.search" ? "Buscar:" : translated;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            follower = input.gameObject.AddComponent<ScenarioEditorSearchBelowPlots>();
            follower.Configure(viewport, labelObject.transform as RectTransform);
            follower.Place();
        }
    }

    // La barra acompaña al viewport sin quedar dentro de su máscara.
    // Unity actualiza el tamaño del handle a través del ScrollRect nativo.
    internal sealed class ScenarioCatalogScrollbarFollower : MonoBehaviour
    {
        private ScrollRect _scroll;
        private RectTransform _host;
        private RectTransform _rect;
        private readonly Vector3[] _corners = new Vector3[4];
        internal void Initialize(ScrollRect scroll, RectTransform host)
        {
            _scroll = scroll;
            _host = host;
            _rect = transform as RectTransform;
        }
        internal void Place()
        {
            if (_scroll == null || _host == null || _rect == null) return;
            var viewport = _scroll.viewport ?? (_scroll.transform as RectTransform);
            if (viewport == null) return;
            viewport.GetWorldCorners(_corners);
            var left = _host.InverseTransformPoint(_corners[0]);
            var right = _host.InverseTransformPoint(_corners[3]);
            var width = Mathf.Max(80f, right.x - left.x - 20f);
            _rect.sizeDelta = new Vector2(width, 14f);
            var mid = (left.x + right.x) * .5f;
            _rect.localPosition = new Vector3(mid, left.y - 12f, 0f);
            // La visibilidad la controla el padre del catálogo; no se
            // desactiva este hijo desde su propio LateUpdate.
        }
        private void LateUpdate() { Place(); }
    }

    internal sealed class ScenarioEditorSearchBelowPlots : MonoBehaviour
    {
        private RectTransform _plotViewport;
        private RectTransform _label;
        private RectTransform _input;
        private readonly Vector3[] _corners = new Vector3[4];

        internal void Configure(RectTransform plotViewport, RectTransform label)
        {
            _plotViewport = plotViewport;
            _label = label;
            _input = transform as RectTransform;
        }

        internal void Place()
        {
            if (_plotViewport == null || _input == null || _label == null) return;
            var parent = _input.parent as RectTransform;
            if (parent == null) return;
            _plotViewport.GetWorldCorners(_corners);
            // La banda de acciones mide 31 px y comienza 4 px bajo la lista.
            // Situar Buscar 39 px debajo permite que no solape Añadir/Eliminar.
            var origin = _corners[0] + parent.TransformVector(new Vector3(4f, -39f, 0f));
            _label.anchorMin = _label.anchorMax = new Vector2(0f, 1f);
            _label.pivot = new Vector2(0f, 1f);
            _label.sizeDelta = new Vector2(64f, 28f);
            _label.position = origin;
            _input.position = origin + parent.TransformVector(new Vector3(65f, 0f, 0f));
        }

        private void LateUpdate() { Place(); }
        private void OnDestroy()
        {
            if (_label != null) UnityEngine.Object.Destroy(_label.gameObject);
        }
    }
}
