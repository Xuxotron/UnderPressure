using System;
using System.Linq;
using HarmonyLib;
using TH20;
using UnityEngine;
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

    // Visor de solo lectura. La edición de la máscara se añadirá por separado.
    internal static class ScenarioFogViewer
    {
        private static readonly Color PanelColor = new Color(0.135f, 0.165f, 0.195f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.24f, 0.31f, 0.36f, 1f);
        private static readonly Color HighlightColor = new Color(0.24f, 0.46f, 0.49f, 1f);
        private static GameObject _toggleRoot;
        private static GameObject _overlay;
        private static Text _toggleText;
        private static Font _font;

        private static string T(string key) => EditorLocalization.Get(key);

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            Detach();
            if (menu == null || level == null) return;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
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

                CreateText(panel.transform, T("editor.fog.title"), 28,
                    new Vector2(0f, -18f), new Vector2(560f, 42f), TextAnchor.MiddleCenter);

                var previewBack = Ui("PreviewBackground", panel.transform, typeof(Image));
                var backRect = (RectTransform)previewBack.transform;
                backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 1f);
                backRect.pivot = new Vector2(0.5f, 1f);
                backRect.anchoredPosition = new Vector2(0f, -75f);
                backRect.sizeDelta = new Vector2(520f, 520f);
                previewBack.GetComponent<Image>().color = new Color(0.82f, 0.84f, 0.86f, 1f);

                if (definition?.FogOfWarTexture != null)
                {
                    var preview = Ui("FogMask", previewBack.transform, typeof(RawImage));
                    Stretch((RectTransform)preview.transform);
                    var raw = preview.GetComponent<RawImage>();
                    raw.texture = definition.FogOfWarTexture;
                    raw.color = Color.white;
                    raw.raycastTarget = false;
                    CreateText(panel.transform,
                        EditorLocalization.Format("editor.fog.details",
                            definition.TileWidth, definition.TileHeight,
                            definition.MaxDistance, definition.Anchor.x, definition.Anchor.y),
                        17, new Vector2(0f, -607f), new Vector2(560f, 31f),
                        TextAnchor.MiddleCenter);
                }
                else
                {
                    CreateText(previewBack.transform, T("editor.fog.none"), 22,
                        Vector2.zero, new Vector2(460f, 80f), TextAnchor.MiddleCenter);
                }

                var close = CreateButton(panel.transform, "Close", T("editor.fog.close"),
                    new Vector2(220f, 42f), ButtonColor, Close);
                SetTop(close.transform, 0f, -648f);
                _toggleRoot.transform.SetAsLastSibling();
                if (_toggleText != null) _toggleText.text = T("editor.fog.close");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Fog of War: no se pudo abrir el visor: " + exception);
                Close();
            }
        }

        internal static void Close()
        {
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            if (_toggleText != null) _toggleText.text = T("editor.fog.button");
        }

        internal static void Detach()
        {
            Close();
            if (_toggleRoot != null) UnityEngine.Object.Destroy(_toggleRoot);
            _toggleRoot = null;
            _toggleText = null;
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
}
