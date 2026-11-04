// Updated: 2026-10-31 21:12 - Catálogo UI: referencias alineadas, controles grandes e interactivos.
using System;
using System.Collections.Generic;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal static class ScenarioUiReferenceCatalogRefs
    {
        // Paneles.
        internal const string REF_PANEL_NATIVE_PREFERENCES = "REF_PANEL_NATIVE_PREFERENCES";
        internal const string REF_PANEL_EDITOR_BLUE = "REF_PANEL_EDITOR_BLUE";
        internal const string REF_PANEL_HEADER_BLUE = "REF_PANEL_HEADER_BLUE";
        internal const string REF_PANEL_VIEWER_DARK = "REF_PANEL_VIEWER_DARK";
        internal const string REF_PANEL_HISTORY = "REF_PANEL_HISTORY";
        internal const string REF_PANEL_HISTORY_VIEWPORT = "REF_PANEL_HISTORY_VIEWPORT";
        internal const string REF_PANEL_DIALOG = "REF_PANEL_DIALOG";
        internal const string REF_PANEL_OBJECT_COLUMN = "REF_PANEL_OBJECT_COLUMN";
        internal const string REF_PANEL_DISEASE_SECTION = "REF_PANEL_DISEASE_SECTION";
        internal const string REF_PANEL_DISEASE_HEADER = "REF_PANEL_DISEASE_HEADER";
        internal const string REF_PANEL_DISEASE_ROW = "REF_PANEL_DISEASE_ROW";
        internal const string REF_PANEL_SELECTED_HOSPITAL_NATIVE = "REF_PANEL_SELECTED_HOSPITAL_NATIVE";

        // Botones. Los dos BUTTON_VIEWER se eliminan expresamente del catálogo.
        internal const string REF_BUTTON_EDITOR = "REF_BUTTON_EDITOR";
        internal const string REF_BUTTON_EDITOR_SELECTED = "REF_BUTTON_EDITOR_SELECTED";
        internal const string REF_BUTTON_PAUSE = "REF_BUTTON_PAUSE";
        internal const string REF_BUTTON_TERRAIN = "REF_BUTTON_TERRAIN";
        internal const string REF_BUTTON_DISEASE_GREEN = "REF_BUTTON_DISEASE_GREEN";
        internal const string REF_BUTTON_DISEASE_RED = "REF_BUTTON_DISEASE_RED";
        internal const string REF_BUTTON_LAYER_TRIO = "REF_BUTTON_LAYER_TRIO";
        internal const string REF_BUTTON_NATIVE_MAIN_MENU = "REF_BUTTON_NATIVE_MAIN_MENU";
        internal const string REF_BUTTON_NATIVE_SETTINGS_TAB = "REF_BUTTON_NATIVE_SETTINGS_TAB";
        internal const string REF_BUTTON_NATIVE_CLOSE = "REF_BUTTON_NATIVE_CLOSE";
        internal const string REF_BUTTON_CAREER_NATIVE_BLUE = "REF_BUTTON_CAREER_NATIVE_BLUE";

        // Textos.
        internal const string REF_TEXT_LEGACY_WHITE = "REF_TEXT_LEGACY_WHITE";
        internal const string REF_TEXT_TMP_DISEASE = "REF_TEXT_TMP_DISEASE";
        internal const string REF_TEXT_MAP_SHADOW = "REF_TEXT_MAP_SHADOW";

        // Campos y controles.
        internal const string REF_INPUT_SEARCH = "REF_INPUT_SEARCH";
        internal const string REF_INPUT_DISEASE = "REF_INPUT_DISEASE";
        internal const string REF_INPUT_WORKSHOP_SINGLE = "REF_INPUT_WORKSHOP_SINGLE";
        internal const string REF_INPUT_WORKSHOP_MULTI = "REF_INPUT_WORKSHOP_MULTI";
        internal const string REF_INPUT_MAP_NAME = "REF_INPUT_MAP_NAME";
        internal const string REF_DROPDOWN_DISEASE = "REF_DROPDOWN_DISEASE";
        internal const string REF_DROPDOWN_NATIVE = "REF_DROPDOWN_NATIVE";
        internal const string REF_TOGGLE_NATIVE = "REF_TOGGLE_NATIVE";
        internal const string REF_TOGGLE_LANDSCAPE = "REF_TOGGLE_LANDSCAPE";
        internal const string REF_SLIDER_EDITOR = "REF_SLIDER_EDITOR";
        internal const string REF_SLIDER_NATIVE = "REF_SLIDER_NATIVE";
        internal const string REF_SCROLLBAR_VERTICAL_NATIVE = "REF_SCROLLBAR_VERTICAL_NATIVE";
        internal const string REF_SCROLLBAR_HORIZONTAL_OBJECTS = "REF_SCROLLBAR_HORIZONTAL_OBJECTS";
        internal const string REF_HISTORY_ROWS = "REF_HISTORY_ROWS";
        internal const string REF_DIVIDER_EDITOR = "REF_DIVIDER_EDITOR";
        internal const string REF_TEXTURE_TILE = "REF_TEXTURE_TILE";
        internal const string REF_ROW_LANDSCAPE = "REF_ROW_LANDSCAPE";
    }

    /// <summary>
    /// Catálogo de referencia totalmente autónomo.
    /// No clona, busca ni lee ningún objeto de la UI original del juego.
    /// Cada REF se construye únicamente con GameObjects/componentes nuevos y
    /// valores visuales fijados en este mismo archivo.
    /// </summary>
    internal sealed class ScenarioUiReferenceCatalog : MonoBehaviour
    {
        private const int Columns = 5;
        private const float OuterMargin = 6f;
        private const float ColumnGap = 6f;
        private const float RowGap = 0f;
        private const float CellHeight = 116f;
        private const float RefLabelHeight = 20f;
        private const float SampleScale = 1f;

        // Fondo exclusivo del catálogo; no se reutiliza en ningún REF.
        private static readonly Color CatalogBackground = new Color32(83, 75, 71, 255);

        // Colores reales ya presentes en el código del proyecto.
        private static readonly Color EditorPanelBlue = new Color32(10, 110, 170, 255);
        private static readonly Color HeaderBlue = new Color(.045f, .38f, .64f, 1f);
        private static readonly Color ViewerPanelDark = new Color(.135f, .165f, .195f, .98f);
        private static readonly Color HistoryPanel = new Color(.045f, .070f, .095f, .94f);
        private static readonly Color HistoryViewport = new Color(.03f, .045f, .065f, .70f);
        private static readonly Color DialogPanel = new Color(.18f, .21f, .25f, .98f);
        private static readonly Color ObjectColumn = new Color32(82, 82, 82, 255);
        private static readonly Color DiseaseSection = new Color32(5, 132, 204, 255);
        private static readonly Color DiseaseRow = new Color32(22, 154, 221, 255);
        private static readonly Color EditorButton = new Color(.14f, .62f, .89f, 1f);
        private static readonly Color EditorSelectedButton = new Color32(10, 110, 170, 255);
        private static readonly Color TerrainButton = new Color(.58f, .68f, .74f, 1f);
        private static readonly Color DiseaseGreen = new Color32(15, 177, 77, 255);
        private static readonly Color DiseaseRed = new Color32(232, 38, 46, 255);
        private static readonly Color LegacyText = new Color(.96f, .97f, .98f, 1f);
        private static readonly Color SearchBackground = new Color(.86f, .89f, .92f, 1f);
        private static readonly Color SearchText = new Color(.09f, .13f, .16f, 1f);
        private static readonly Color SearchHint = new Color(.37f, .43f, .47f, 1f);
        private static readonly Color SliderTrack = new Color(.18f, .27f, .33f, 1f);
        private static readonly Color SliderFill = new Color(.23f, .74f, .98f, 1f);
        private static readonly Color SliderHandle = new Color(.97f, .97f, .91f, 1f);

        // Colores copiados del aspecto nativo que ya se estaba reutilizando.
        // Quedan fijados aquí: no se consulta ningún control nativo en runtime.
        private static readonly Color NativePanelGrey = new Color32(112, 112, 112, 255);
        private static readonly Color NativeDarkPanel = new Color32(50, 57, 63, 255);
        private static readonly Color NativeMainCyan = new Color32(13, 202, 242, 255);
        private static readonly Color NativeSettingsBlue = new Color32(38, 148, 205, 255);
        private static readonly Color NativeSettingsLightBlue = new Color32(58, 179, 226, 255);
        private static readonly Color NativeFieldGrey = new Color32(205, 205, 205, 255);
        private static readonly Color NativeDarkText = new Color32(50, 50, 50, 255);
        private static readonly Color NativeWorkshopBlue = new Color32(78, 129, 167, 255);
        private static readonly Color NativeWorkshopInput = new Color32(211, 215, 220, 255);
        private static readonly Color NativeScrollbarTrack = new Color32(62, 53, 50, 255);
        private static readonly Color NativeScrollbarHandle = new Color32(195, 196, 195, 255);
        private static readonly Color NativeSliderTrack = new Color32(99, 101, 99, 255);
        private static readonly Color NativeSliderFill = new Color32(132, 203, 225, 255);
        private static readonly Color NativeSliderHandle = new Color32(79, 190, 228, 255);

        private static readonly Color[] LayerColors =
        {
            new Color(240f / 255f, 90f / 255f, 140f / 255f, 1f),
            new Color(1f, .58f, .08f, 1f),
            new Color(.48f, .72f, .25f, 1f)
        };

        private GameObject _root;
        private RectTransform _content;
        private Sprite _roundedSprite;
        private Sprite _circleSprite;
        private Texture2D _roundedTexture;
        private Texture2D _circleTexture;
        private Font _legacyFont;
        private TMP_FontAsset _tmpFont;
        private int _referenceIndex;

        internal void Initialise()
        {
            if (_legacyFont == null)
                _legacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (_tmpFont == null)
                _tmpFont = TMP_Settings.defaultFontAsset;
        }

        private void OnDestroy()
        {
            CloseCatalog();
            DestroyGeneratedAssets();
        }

        private void DestroyGeneratedAssets()
        {
            if (_roundedSprite != null) Destroy(_roundedSprite);
            if (_circleSprite != null) Destroy(_circleSprite);
            if (_roundedTexture != null) Destroy(_roundedTexture);
            if (_circleTexture != null) Destroy(_circleTexture);

            _roundedSprite = null;
            _circleSprite = null;
            _roundedTexture = null;
            _circleTexture = null;
        }

        private void Update()
        {
            var control =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);

            var alt =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);

            if (control && alt && Input.GetKeyDown(KeyCode.F10))
            {
                if (_root == null)
                    OpenCatalog();
                else
                    CloseCatalog();

                return;
            }

            if (_root != null && Input.GetKeyDown(KeyCode.Escape))
                CloseCatalog();
        }

        private void OpenCatalog()
        {
            CloseCatalog();
            Initialise();

            if (_roundedSprite == null)
                _roundedSprite = CreateRoundedSprite(out _roundedTexture);

            if (_circleSprite == null)
                _circleSprite = CreateCircleSprite(out _circleTexture);

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _root = NewUi(
                "CATALOG_ROOT",
                canvas.transform,
                typeof(Image),
                typeof(Canvas),
                typeof(GraphicRaycaster));

            Stretch((RectTransform)_root.transform);

            var background = _root.GetComponent<Image>();
            background.color = CatalogBackground;
            background.raycastTarget = true;

            var nestedCanvas = _root.GetComponent<Canvas>();
            nestedCanvas.overrideSorting = true;
            nestedCanvas.sortingOrder = 32760;

            BuildContent();
            _referenceIndex = 0;
            BuildReferences();

            _root.transform.SetAsLastSibling();
        }

        private void CloseCatalog()
        {
            if (_root != null)
                Destroy(_root);

            _root = null;
            _content = null;
        }

        private void BuildContent()
        {
            var content = NewUi("CATALOG_CONTENT", _root.transform);
            _content = (RectTransform)content.transform;
            Stretch(_content);
            _content.offsetMin = new Vector2(OuterMargin, OuterMargin);
            _content.offsetMax = new Vector2(-OuterMargin, -OuterMargin);
        }

        private void BuildReferences()
        {
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_NATIVE_PREFERENCES, Build_REF_PANEL_NATIVE_PREFERENCES);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_EDITOR_BLUE, Build_REF_PANEL_EDITOR_BLUE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_HEADER_BLUE, Build_REF_PANEL_HEADER_BLUE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_VIEWER_DARK, Build_REF_PANEL_VIEWER_DARK);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_HISTORY, Build_REF_PANEL_HISTORY);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_HISTORY_VIEWPORT, Build_REF_PANEL_HISTORY_VIEWPORT);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_DIALOG, Build_REF_PANEL_DIALOG);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_OBJECT_COLUMN, Build_REF_PANEL_OBJECT_COLUMN);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_DISEASE_SECTION, Build_REF_PANEL_DISEASE_SECTION);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_DISEASE_HEADER, Build_REF_PANEL_DISEASE_HEADER);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_DISEASE_ROW, Build_REF_PANEL_DISEASE_ROW);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_PANEL_SELECTED_HOSPITAL_NATIVE, Build_REF_PANEL_SELECTED_HOSPITAL_NATIVE);

            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_EDITOR, Build_REF_BUTTON_EDITOR);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_EDITOR_SELECTED, Build_REF_BUTTON_EDITOR_SELECTED);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_PAUSE, Build_REF_BUTTON_PAUSE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_TERRAIN, Build_REF_BUTTON_TERRAIN);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_DISEASE_GREEN, Build_REF_BUTTON_DISEASE_GREEN);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_DISEASE_RED, Build_REF_BUTTON_DISEASE_RED);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_LAYER_TRIO, Build_REF_BUTTON_LAYER_TRIO);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_NATIVE_MAIN_MENU, Build_REF_BUTTON_NATIVE_MAIN_MENU);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_NATIVE_SETTINGS_TAB, Build_REF_BUTTON_NATIVE_SETTINGS_TAB);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_NATIVE_CLOSE, Build_REF_BUTTON_NATIVE_CLOSE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_BUTTON_CAREER_NATIVE_BLUE, Build_REF_BUTTON_CAREER_NATIVE_BLUE);

            AddReference(ScenarioUiReferenceCatalogRefs.REF_TEXT_LEGACY_WHITE, Build_REF_TEXT_LEGACY_WHITE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_TEXT_TMP_DISEASE, Build_REF_TEXT_TMP_DISEASE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_TEXT_MAP_SHADOW, Build_REF_TEXT_MAP_SHADOW);

            AddReference(ScenarioUiReferenceCatalogRefs.REF_INPUT_SEARCH, Build_REF_INPUT_SEARCH);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_INPUT_DISEASE, Build_REF_INPUT_DISEASE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_INPUT_WORKSHOP_SINGLE, Build_REF_INPUT_WORKSHOP_SINGLE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_INPUT_WORKSHOP_MULTI, Build_REF_INPUT_WORKSHOP_MULTI);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_INPUT_MAP_NAME, Build_REF_INPUT_MAP_NAME);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_DROPDOWN_DISEASE, Build_REF_DROPDOWN_DISEASE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_DROPDOWN_NATIVE, Build_REF_DROPDOWN_NATIVE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_TOGGLE_NATIVE, Build_REF_TOGGLE_NATIVE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_TOGGLE_LANDSCAPE, Build_REF_TOGGLE_LANDSCAPE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_SLIDER_EDITOR, Build_REF_SLIDER_EDITOR);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_SLIDER_NATIVE, Build_REF_SLIDER_NATIVE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_SCROLLBAR_VERTICAL_NATIVE, Build_REF_SCROLLBAR_VERTICAL_NATIVE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_SCROLLBAR_HORIZONTAL_OBJECTS, Build_REF_SCROLLBAR_HORIZONTAL_OBJECTS);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_HISTORY_ROWS, Build_REF_HISTORY_ROWS);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_DIVIDER_EDITOR, Build_REF_DIVIDER_EDITOR);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_TEXTURE_TILE, Build_REF_TEXTURE_TILE);
            AddReference(ScenarioUiReferenceCatalogRefs.REF_ROW_LANDSCAPE, Build_REF_ROW_LANDSCAPE);
        }

        private void AddReference(string reference, Action<RectTransform> build)
        {
            var index = _referenceIndex++;
            var column = index % Columns;
            var row = index / Columns;

            var left = (float)column / Columns;
            var right = (float)(column + 1) / Columns;

            var cell = NewUi("CATALOG_CELL_" + reference, _content);
            var rect = (RectTransform)cell.transform;
            rect.anchorMin = new Vector2(left, 1f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -row * (CellHeight + RowGap));
            rect.sizeDelta = new Vector2(-ColumnGap, CellHeight);

            var label = CreateReferenceLabel(cell.transform, reference);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(.5f, 1f);
            labelRect.offsetMin = new Vector2(4f, -RefLabelHeight);
            labelRect.offsetMax = new Vector2(-4f, 0f);

            var host = NewUi("CATALOG_SAMPLE_" + reference, cell.transform);
            var hostRect = (RectTransform)host.transform;
            hostRect.anchorMin = Vector2.zero;
            hostRect.anchorMax = Vector2.one;
            hostRect.offsetMin = new Vector2(4f, 0f);
            hostRect.offsetMax = new Vector2(-4f, -RefLabelHeight);

            build?.Invoke(hostRect);
        }

        private TextMeshProUGUI CreateReferenceLabel(Transform parent, string reference)
        {
            var root = NewUi("CATALOG_REF_" + reference, parent, typeof(TextMeshProUGUI));
            var text = root.GetComponent<TextMeshProUGUI>();

            if (_tmpFont != null)
                text.font = _tmpFont;

            text.text = ShortReferenceName(reference);
            text.fontSize = 13f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 13f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static string ShortReferenceName(string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return "";

            var name = reference.StartsWith("REF_", StringComparison.Ordinal)
                ? reference.Substring(4)
                : reference;

            switch (name)
            {
                case "PANEL_NATIVE_PREFERENCES": return "PANEL_PREFERENCES";
                case "PANEL_HISTORY_VIEWPORT": return "PANEL_HISTORY_VIEW";
                case "PANEL_OBJECT_COLUMN": return "PANEL_OBJECTS";
                case "PANEL_DISEASE_SECTION": return "PANEL_DISEASE";
                case "PANEL_SELECTED_HOSPITAL_NATIVE": return "PANEL_HOSPITAL";
                case "BUTTON_EDITOR_SELECTED": return "BUTTON_EDITOR_ACTIVE";
                case "BUTTON_DISEASE_GREEN": return "BUTTON_ADD";
                case "BUTTON_DISEASE_RED": return "BUTTON_REMOVE";
                case "BUTTON_LAYER_TRIO": return "BUTTON_LAYERS";
                case "BUTTON_NATIVE_MAIN_MENU": return "BUTTON_MAIN_MENU";
                case "BUTTON_NATIVE_SETTINGS_TAB": return "BUTTON_SETTINGS";
                case "BUTTON_NATIVE_CLOSE": return "BUTTON_CLOSE";
                case "BUTTON_CAREER_NATIVE_BLUE": return "BUTTON_CAREER";
                case "TEXT_LEGACY_WHITE": return "TEXT_LEGACY";
                case "TEXT_TMP_DISEASE": return "TEXT_DISEASE";
                case "INPUT_WORKSHOP_SINGLE": return "INPUT_WORKSHOP";
                case "INPUT_WORKSHOP_MULTI": return "INPUT_DESCRIPTION";
                case "SCROLLBAR_VERTICAL_NATIVE": return "SCROLLBAR_VERTICAL";
                case "SCROLLBAR_HORIZONTAL_OBJECTS": return "SCROLLBAR_OBJECTS";
                default:
                    return name.Replace("_NATIVE", "");
            }
        }

        // -----------------------------------------------------------------
        // PANELES
        // -----------------------------------------------------------------

        private void Build_REF_PANEL_NATIVE_PREFERENCES(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_NATIVE_PREFERENCES,
                NativePanelGrey, true, true, 150f, 34f);
        }

        private void Build_REF_PANEL_EDITOR_BLUE(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_EDITOR_BLUE,
                EditorPanelBlue, false, false, 150f, 34f);
        }

        private void Build_REF_PANEL_HEADER_BLUE(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_HEADER_BLUE,
                HeaderBlue, false, false, 150f, 10f);
        }

        private void Build_REF_PANEL_VIEWER_DARK(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_VIEWER_DARK,
                ViewerPanelDark, true, false, 150f, 34f);
        }

        private void Build_REF_PANEL_HISTORY(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_HISTORY,
                HistoryPanel, true, false, 150f, 34f);
        }

        private void Build_REF_PANEL_HISTORY_VIEWPORT(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_HISTORY_VIEWPORT,
                HistoryViewport, true, false, 150f, 34f);
        }

        private void Build_REF_PANEL_DIALOG(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_DIALOG,
                DialogPanel, true, false, 150f, 34f);
        }

        private void Build_REF_PANEL_OBJECT_COLUMN(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_OBJECT_COLUMN,
                ObjectColumn, true, false, 150f, 34f);
        }

        private void Build_REF_PANEL_DISEASE_SECTION(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_DISEASE_SECTION,
                DiseaseSection, false, true, 150f, 34f);
        }

        private void Build_REF_PANEL_DISEASE_HEADER(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_DISEASE_HEADER,
                HeaderBlue, false, true, 150f, 34f);
        }

        private void Build_REF_PANEL_DISEASE_ROW(RectTransform host)
        {
            CreatePanel(host, ScenarioUiReferenceCatalogRefs.REF_PANEL_DISEASE_ROW,
                DiseaseRow, false, true, 150f, 34f);
        }

        private void Build_REF_PANEL_SELECTED_HOSPITAL_NATIVE(RectTransform host)
        {
            var panel = CreatePanel(host,
                ScenarioUiReferenceCatalogRefs.REF_PANEL_SELECTED_HOSPITAL_NATIVE,
                NativeDarkPanel, true, true, 150f, 34f);

            var shadow = panel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, .55f);
            shadow.effectDistance = new Vector2(3f, -3f);
            shadow.useGraphicAlpha = true;
        }

        private GameObject CreatePanel(
            RectTransform host,
            string name,
            Color color,
            bool outline,
            bool rounded,
            float width,
            float height)
        {
            var root = NewUi(name, host, typeof(Image));
            PlaceCentered((RectTransform)root.transform, width, height);

            var image = root.GetComponent<Image>();
            image.sprite = rounded ? _roundedSprite : null;
            image.type = rounded ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;

            if (outline)
                AddPanelOutline(root);

            return root;
        }

        private static void AddPanelOutline(GameObject root)
        {
            var outline = root.GetComponent<Outline>() ?? root.AddComponent<Outline>();
            outline.effectColor = new Color(.88f, .92f, .90f, .90f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }

        // -----------------------------------------------------------------
        // BOTONES
        // -----------------------------------------------------------------

        private void Build_REF_BUTTON_EDITOR(RectTransform host)
        {
            CreateEditorButton(host, ScenarioUiReferenceCatalogRefs.REF_BUTTON_EDITOR,
                "Botón", EditorButton, false);
        }

        private void Build_REF_BUTTON_EDITOR_SELECTED(RectTransform host)
        {
            CreateEditorButton(host, ScenarioUiReferenceCatalogRefs.REF_BUTTON_EDITOR_SELECTED,
                "Activo", EditorSelectedButton, false);
        }

        private void Build_REF_BUTTON_PAUSE(RectTransform host)
        {
            CreateEditorButton(host, ScenarioUiReferenceCatalogRefs.REF_BUTTON_PAUSE,
                "Pausa", EditorButton, true);
        }

        private void CreateEditorButton(
            RectTransform host,
            string name,
            string label,
            Color color,
            bool pauseOutline)
        {
            var root = NewUi(name, host, typeof(Image), typeof(Button));
            PlaceCentered((RectTransform)root.transform, 190f, 38f);

            var image = root.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = true;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = InteractiveColorBlock();

            var outline = root.AddComponent<Outline>();
            outline.enabled = true;
            outline.effectColor = pauseOutline
                ? new Color(1f, 1f, 1f, .98f)
                : Color.white;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            var text = CreateTmpText(root.transform, label, 10f, Color.white,
                TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform);
        }

        private void Build_REF_BUTTON_TERRAIN(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_BUTTON_TERRAIN,
                host, typeof(Image), typeof(Button));
            PlaceCentered((RectTransform)root.transform, 165f, 36f);

            var image = root.GetComponent<Image>();
            image.color = TerrainButton;
            image.raycastTarget = true;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = InteractiveColorBlock();

            var text = CreateLegacyText(root.transform, "Terrain", 10,
                Color.white, TextAnchor.MiddleCenter, true);
            Stretch(text.rectTransform);
        }

        private void Build_REF_BUTTON_DISEASE_GREEN(RectTransform host)
        {
            CreateDiseaseButton(host, ScenarioUiReferenceCatalogRefs.REF_BUTTON_DISEASE_GREEN,
                "Añadir", DiseaseGreen);
        }

        private void Build_REF_BUTTON_DISEASE_RED(RectTransform host)
        {
            CreateDiseaseButton(host, ScenarioUiReferenceCatalogRefs.REF_BUTTON_DISEASE_RED,
                "Eliminar", DiseaseRed);
        }

        private void CreateDiseaseButton(RectTransform host, string name, string label, Color color)
        {
            var root = NewUi(name, host, typeof(Image), typeof(Button));
            PlaceCentered((RectTransform)root.transform, 175f, 38f);

            var image = root.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = true;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = InteractiveColorBlock();

            var text = CreateTmpText(root.transform, label, 10f, Color.white,
                TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform);
        }

        private void Build_REF_BUTTON_LAYER_TRIO(RectTransform host)
        {
            var names = new[] { "Base", "Build", "Unbuild" };

            for (var index = 0; index < names.Length; ++index)
            {
                var root = NewUi(
                    ScenarioUiReferenceCatalogRefs.REF_BUTTON_LAYER_TRIO + "_" + names[index],
                    host, typeof(Image), typeof(Button));

                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(86f, 38f);
                rect.anchoredPosition = new Vector2(index * 91f, -3f);

                var image = root.GetComponent<Image>();
                image.color = LayerColors[index];
                image.raycastTarget = true;

                var button = root.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.ColorTint;
                button.colors = InteractiveColorBlock();

                var outline = root.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = false;
                outline.enabled = index == 1;

                var label = CreateTmpText(
                    root.transform, names[index], 14f,
                    Color.white, TextAlignmentOptions.Center, true);
                Stretch(label.rectTransform);
            }
        }

        private void Build_REF_BUTTON_NATIVE_MAIN_MENU(RectTransform host)
        {
            CreateNativeRoundedButton(host,
                ScenarioUiReferenceCatalogRefs.REF_BUTTON_NATIVE_MAIN_MENU,
                "Menú", NativeMainCyan, 190f, 38f, true, false);
        }

        private void Build_REF_BUTTON_NATIVE_SETTINGS_TAB(RectTransform host)
        {
            var root = CreateNativeRoundedButton(host,
                ScenarioUiReferenceCatalogRefs.REF_BUTTON_NATIVE_SETTINGS_TAB,
                "Ajustes", NativeSettingsBlue, 190f, 38f, true, true);

            var highlight = NewUi("Highlight", root.transform, typeof(Image));
            var rect = (RectTransform)highlight.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -2f);
            rect.sizeDelta = new Vector2(-8f, 3f);
            var image = highlight.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = NativeSettingsLightBlue;
            image.raycastTarget = false;
        }

        private void Build_REF_BUTTON_NATIVE_CLOSE(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_BUTTON_NATIVE_CLOSE,
                host, typeof(Image), typeof(Button));
            PlaceCentered((RectTransform)root.transform, 52f, 52f);

            var image = root.GetComponent<Image>();
            image.sprite = _circleSprite;
            image.type = Image.Type.Simple;
            image.color = new Color32(86, 212, 252, 255);
            image.raycastTarget = false;

            var outline = root.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = InteractiveColorBlock();

            var text = CreateTmpText(root.transform, "×", 18f, Color.white,
                TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform);
            text.rectTransform.anchoredPosition = new Vector2(0f, 1f);
        }

        private void Build_REF_BUTTON_CAREER_NATIVE_BLUE(RectTransform host)
        {
            CreateNativeRoundedButton(host,
                ScenarioUiReferenceCatalogRefs.REF_BUTTON_CAREER_NATIVE_BLUE,
                "Mapa", NativeSettingsBlue, 180f, 38f, true, false);
        }

        private GameObject CreateNativeRoundedButton(
            RectTransform host,
            string name,
            string label,
            Color color,
            float width,
            float height,
            bool outline,
            bool shadow)
        {
            var root = NewUi(name, host, typeof(Image), typeof(Button));
            PlaceCentered((RectTransform)root.transform, width, height);

            var image = root.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = true;

            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = InteractiveColorBlock();

            if (outline)
            {
                var border = root.AddComponent<Outline>();
                border.effectColor = Color.white;
                border.effectDistance = new Vector2(2f, -2f);
                border.useGraphicAlpha = true;
            }

            if (shadow)
            {
                var drop = root.AddComponent<Shadow>();
                drop.effectColor = new Color(0f, 0f, 0f, .55f);
                drop.effectDistance = new Vector2(3f, -3f);
                drop.useGraphicAlpha = true;
            }

            var text = CreateTmpText(root.transform, label, 10f, Color.white,
                TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform);
            return root;
        }

        // -----------------------------------------------------------------
        // TEXTOS
        // -----------------------------------------------------------------

        private void Build_REF_TEXT_LEGACY_WHITE(RectTransform host)
        {
            var text = CreateLegacyText(host, "Texto", 13, LegacyText,
                TextAnchor.MiddleCenter, false);
            PlaceCentered(text.rectTransform, 130f, 24f);
        }

        private void Build_REF_TEXT_TMP_DISEASE(RectTransform host)
        {
            var text = CreateTmpText(host, "Texto", 13f, Color.white,
                TextAlignmentOptions.Center, true);
            PlaceCentered(text.rectTransform, 130f, 24f);
        }

        private void Build_REF_TEXT_MAP_SHADOW(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_TEXT_MAP_SHADOW,
                host, typeof(Image), typeof(Button));
            PlaceCentered((RectTransform)root.transform, 130f, 24f);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, .001f);
            root.GetComponent<Image>().raycastTarget = true;
            var mapButton = root.GetComponent<Button>();
            mapButton.targetGraphic = root.GetComponent<Image>();
            mapButton.transition = Selectable.Transition.ColorTint;
            mapButton.colors = InteractiveColorBlock();

            var text = CreateLegacyText(root.transform, "Mapa", 12, Color.white,
                TextAnchor.MiddleRight, true);
            Stretch(text.rectTransform);

            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, .85f);
            shadow.effectDistance = new Vector2(1f, -1f);
            shadow.useGraphicAlpha = true;
        }

        // -----------------------------------------------------------------
        // INPUTS Y DROPDOWNS
        // -----------------------------------------------------------------

        private void Build_REF_INPUT_SEARCH(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_INPUT_SEARCH,
                host, typeof(Image), typeof(InputField));
            PlaceCentered((RectTransform)root.transform, 245f, 38f);

            var image = root.GetComponent<Image>();
            image.color = SearchBackground;
            image.raycastTarget = true;

            var value = CreateLegacyText(root.transform, "", 11, SearchText,
                TextAnchor.MiddleLeft, false);
            Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(7f, 0f);
            value.rectTransform.offsetMax = new Vector2(-7f, 0f);

            var hint = CreateLegacyText(root.transform, "Buscar...", 10, SearchHint,
                TextAnchor.MiddleLeft, false);
            Stretch(hint.rectTransform);
            hint.rectTransform.offsetMin = new Vector2(7f, 0f);
            hint.rectTransform.offsetMax = new Vector2(-7f, 0f);

            var input = root.GetComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = value;
            input.placeholder = hint;
            input.transition = Selectable.Transition.None;
        }

        private void Build_REF_INPUT_DISEASE(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_INPUT_DISEASE,
                host, typeof(Image), typeof(TMP_InputField));
            PlaceCentered((RectTransform)root.transform, 210f, 38f);

            var image = root.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color32(218, 222, 226, 255);
            image.raycastTarget = true;

            var viewport = NewUi("Text Area", root.transform, typeof(RectMask2D));
            Stretch((RectTransform)viewport.transform);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.offsetMin = new Vector2(6f, 0f);
            viewportRect.offsetMax = new Vector2(-6f, 0f);

            var text = CreateTmpText(viewport.transform, "0", 11f, Color.black,
                TextAlignmentOptions.MidlineLeft, false);
            Stretch(text.rectTransform);

            var input = root.GetComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.None;
            input.SetTextWithoutNotify("0");
        }

        private void Build_REF_INPUT_WORKSHOP_SINGLE(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_INPUT_WORKSHOP_SINGLE,
                host, typeof(Image));

            PlaceCentered((RectTransform)root.transform, 290f, 42f);

            var background = root.GetComponent<Image>();
            background.sprite = _roundedSprite;
            background.type = Image.Type.Sliced;
            background.color = NativeWorkshopBlue;
            background.raycastTarget = false;

            var label = CreateTmpText(
                root.transform, "Nombre", 13f, Color.white,
                TextAlignmentOptions.MidlineLeft, true);

            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(.32f, 1f);
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);

            var field = NewUi("Input", root.transform, typeof(Image), typeof(TMP_InputField));
            var fieldRect = (RectTransform)field.transform;
            fieldRect.anchorMin = new Vector2(.32f, .12f);
            fieldRect.anchorMax = new Vector2(.97f, .88f);
            fieldRect.offsetMin = Vector2.zero;
            fieldRect.offsetMax = Vector2.zero;

            ConfigureTmpInput(
                field,
                "Escribir nombre…",
                false,
                NativeWorkshopInput,
                NativeDarkText);
        }

        private void Build_REF_INPUT_WORKSHOP_MULTI(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_INPUT_WORKSHOP_MULTI,
                host, typeof(Image));

            PlaceCentered((RectTransform)root.transform, 290f, 74f);

            var background = root.GetComponent<Image>();
            background.sprite = _roundedSprite;
            background.type = Image.Type.Sliced;
            background.color = NativeWorkshopBlue;
            background.raycastTarget = false;

            var label = CreateTmpText(
                root.transform, "Descripción", 13f, Color.white,
                TextAlignmentOptions.TopLeft, true);

            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(.32f, 1f);
            labelRect.offsetMin = new Vector2(10f, 8f);
            labelRect.offsetMax = new Vector2(-4f, -8f);

            var field = NewUi(
                "Input multilinea",
                root.transform,
                typeof(Image),
                typeof(TMP_InputField));

            var fieldRect = (RectTransform)field.transform;
            fieldRect.anchorMin = new Vector2(.32f, .10f);
            fieldRect.anchorMax = new Vector2(.97f, .90f);
            fieldRect.offsetMin = Vector2.zero;
            fieldRect.offsetMax = Vector2.zero;

            ConfigureTmpInput(
                field,
                "Escribir descripción…",
                true,
                NativeWorkshopInput,
                NativeDarkText);
        }

        private void Build_REF_INPUT_MAP_NAME(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_INPUT_MAP_NAME,
                host, typeof(Image), typeof(InputField));

            PlaceCentered((RectTransform)root.transform, 290f, 40f);

            var background = root.GetComponent<Image>();
            background.color = new Color(.96f, .96f, .96f, 1f);
            background.raycastTarget = true;

            var value = CreateLegacyText(
                root.transform, "", 12,
                new Color(.07f, .10f, .12f, 1f),
                TextAnchor.MiddleLeft, false);

            Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(10f, 0f);
            value.rectTransform.offsetMax = new Vector2(-10f, 0f);

            var placeholder = CreateLegacyText(
                root.transform, "Escribir nombre…", 11,
                new Color(.40f, .40f, .40f, 1f),
                TextAnchor.MiddleLeft, false);

            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(10f, 0f);
            placeholder.rectTransform.offsetMax = new Vector2(-10f, 0f);

            var input = root.GetComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.transition = Selectable.Transition.None;
        }

        private void Build_REF_DROPDOWN_DISEASE(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_DROPDOWN_DISEASE,
                host, typeof(Image), typeof(TMP_Dropdown));

            PlaceCentered((RectTransform)root.transform, 230f, 40f);

            var image = root.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color32(218, 222, 226, 255);
            image.raycastTarget = true;

            var caption = CreateTmpText(
                root.transform, "Opción", 13f, Color.black,
                TextAlignmentOptions.MidlineLeft, true);

            Stretch(caption.rectTransform);
            caption.rectTransform.offsetMin = new Vector2(10f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-34f, 0f);

            CreateDropdownArrow(root.transform, Color.black);

            var dropdown = root.GetComponent<TMP_Dropdown>();
            dropdown.targetGraphic = image;
            dropdown.captionText = caption;
            dropdown.transition = Selectable.Transition.ColorTint;
            dropdown.colors = InteractiveColorBlock();
            dropdown.options.Clear();
            dropdown.options.Add(new TMP_Dropdown.OptionData("Opción"));
            dropdown.options.Add(new TMP_Dropdown.OptionData("Segunda"));
            dropdown.options.Add(new TMP_Dropdown.OptionData("Tercera"));

            ConfigureDropdownTemplate(
                dropdown,
                root.transform,
                new Color32(225, 230, 235, 255),
                new Color32(218, 222, 226, 255),
                Color.black,
                230f);
        }

        private void Build_REF_DROPDOWN_NATIVE(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_DROPDOWN_NATIVE,
                host, typeof(Image), typeof(TMP_Dropdown));

            PlaceCentered((RectTransform)root.transform, 230f, 40f);

            var outer = root.GetComponent<Image>();
            outer.sprite = _roundedSprite;
            outer.type = Image.Type.Sliced;
            outer.color = Color.white;
            outer.raycastTarget = true;

            var inner = NewUi("Inner", root.transform, typeof(Image));
            var innerRect = (RectTransform)inner.transform;
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(3f, 3f);
            innerRect.offsetMax = new Vector2(-3f, -3f);

            var innerImage = inner.GetComponent<Image>();
            innerImage.sprite = _roundedSprite;
            innerImage.type = Image.Type.Sliced;
            innerImage.color = NativeFieldGrey;
            innerImage.raycastTarget = false;

            var caption = CreateTmpText(
                root.transform, "1920 × 1080", 13f,
                NativeDarkText, TextAlignmentOptions.MidlineLeft, true);

            Stretch(caption.rectTransform);
            caption.rectTransform.offsetMin = new Vector2(10f, 0f);
            caption.rectTransform.offsetMax = new Vector2(-34f, 0f);

            CreateDropdownArrow(root.transform, Color.black);

            var dropdown = root.GetComponent<TMP_Dropdown>();
            dropdown.targetGraphic = outer;
            dropdown.captionText = caption;
            dropdown.transition = Selectable.Transition.ColorTint;
            dropdown.colors = InteractiveColorBlock();
            dropdown.options.Clear();
            dropdown.options.Add(new TMP_Dropdown.OptionData("1920 × 1080"));
            dropdown.options.Add(new TMP_Dropdown.OptionData("1600 × 900"));
            dropdown.options.Add(new TMP_Dropdown.OptionData("1280 × 720"));

            ConfigureDropdownTemplate(
                dropdown,
                root.transform,
                new Color(.15f, .21f, .26f, 1f),
                NativeFieldGrey,
                NativeDarkText,
                230f);
        }

        private void ConfigureTmpInput(
            GameObject field,
            string placeholderText,
            bool multiline,
            Color backgroundColor,
            Color textColor)
        {
            var image = field.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = backgroundColor;
            image.raycastTarget = true;

            var viewport = NewUi("Text Area", field.transform, typeof(RectMask2D));
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(8f, 4f);
            viewportRect.offsetMax = new Vector2(-8f, -4f);

            var value = CreateTmpText(
                viewport.transform, "", 13f, textColor,
                multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft,
                false);
            Stretch(value.rectTransform);
            value.enableWordWrapping = multiline;

            var placeholder = CreateTmpText(
                viewport.transform, placeholderText, 12f,
                new Color(textColor.r, textColor.g, textColor.b, .55f),
                multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft,
                false);
            Stretch(placeholder.rectTransform);
            placeholder.enableWordWrapping = multiline;

            var input = field.GetComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.textViewport = viewportRect;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.ColorTint;
            input.colors = InteractiveColorBlock();
            input.SetTextWithoutNotify("");
        }

        private void ConfigureDropdownTemplate(
            TMP_Dropdown dropdown,
            Transform owner,
            Color listBackground,
            Color itemBackground,
            Color textColor,
            float width)
        {
            var template = NewUi(
                "Template",
                owner,
                typeof(Image),
                typeof(ScrollRect));

            var templateRect = (RectTransform)template.transform;
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(0f, 0f);
            templateRect.pivot = new Vector2(0f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -3f);
            templateRect.sizeDelta = new Vector2(width, 108f);

            var templateImage = template.GetComponent<Image>();
            templateImage.sprite = _roundedSprite;
            templateImage.type = Image.Type.Sliced;
            templateImage.color = listBackground;
            templateImage.raycastTarget = true;

            var viewport = NewUi(
                "Viewport",
                template.transform,
                typeof(Image),
                typeof(Mask));

            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(3f, 3f);
            viewportRect.offsetMax = new Vector2(-3f, -3f);

            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, .01f);
            viewportImage.raycastTarget = true;

            var mask = viewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = NewUi("Content", viewport.transform);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 34f);

            var item = NewUi(
                "Item",
                content.transform,
                typeof(Image),
                typeof(Toggle));

            var itemRect = (RectTransform)item.transform;
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(.5f, 1f);
            itemRect.anchoredPosition = Vector2.zero;
            itemRect.sizeDelta = new Vector2(0f, 34f);

            var itemImage = item.GetComponent<Image>();
            itemImage.sprite = _roundedSprite;
            itemImage.type = Image.Type.Sliced;
            itemImage.color = itemBackground;
            itemImage.raycastTarget = true;

            var itemText = CreateTmpText(
                item.transform, "Opción", 13f, textColor,
                TextAlignmentOptions.MidlineLeft, false);

            Stretch(itemText.rectTransform);
            itemText.rectTransform.offsetMin = new Vector2(10f, 0f);
            itemText.rectTransform.offsetMax = new Vector2(-8f, 0f);

            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemImage;
            itemToggle.transition = Selectable.Transition.ColorTint;
            itemToggle.colors = InteractiveColorBlock();

            var scroll = template.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            dropdown.template = templateRect;
            dropdown.itemText = itemText;
            dropdown.itemImage = null;

            template.SetActive(false);
        }

        private void CreateDropdownArrow(Transform parent, Color color)
        {
            var arrow = CreateTmpText(parent, "▼", 11f, color,
                TextAlignmentOptions.Center, true);
            var rect = arrow.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, .5f);
            rect.sizeDelta = new Vector2(23f, 0f);
            rect.anchoredPosition = Vector2.zero;
        }

        // -----------------------------------------------------------------
        // TOGGLES
        // -----------------------------------------------------------------

        private void Build_REF_TOGGLE_NATIVE(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_TOGGLE_NATIVE,
                host, typeof(Image), typeof(Toggle));

            PlaceCentered((RectTransform)root.transform, 38f, 38f);

            var outer = root.GetComponent<Image>();
            outer.sprite = _roundedSprite;
            outer.type = Image.Type.Sliced;
            outer.color = Color.white;
            outer.raycastTarget = true;

            var inner = NewUi("Inner", root.transform, typeof(Image));
            var rect = (RectTransform)inner.transform;
            Stretch(rect);
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);

            var innerImage = inner.GetComponent<Image>();
            innerImage.sprite = _roundedSprite;
            innerImage.type = Image.Type.Sliced;
            innerImage.color = NativeFieldGrey;
            innerImage.raycastTarget = false;

            var mark = CreateTmpText(
                root.transform, "✓", 20f, NativeSettingsBlue,
                TextAlignmentOptions.Center, true);
            Stretch(mark.rectTransform);

            var toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = outer;
            toggle.graphic = mark;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.colors = InteractiveColorBlock();
            toggle.isOn = true;
        }

        private void Build_REF_TOGGLE_LANDSCAPE(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_TOGGLE_LANDSCAPE,
                host, typeof(Image), typeof(Toggle));

            PlaceCentered((RectTransform)root.transform, 38f, 38f);

            var outer = root.GetComponent<Image>();
            outer.color = Color.white;
            outer.raycastTarget = true;

            var inner = NewUi("Inner", root.transform, typeof(Image));
            var innerRect = (RectTransform)inner.transform;
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(4f, 4f);
            innerRect.offsetMax = new Vector2(-4f, -4f);

            var innerImage = inner.GetComponent<Image>();
            innerImage.color = new Color(.18f, .22f, .26f, 1f);
            innerImage.raycastTarget = false;

            var mark = CreateLegacyText(
                root.transform, "✓", 20, Color.white,
                TextAnchor.MiddleCenter, true);
            Stretch(mark.rectTransform);

            var toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = outer;
            toggle.graphic = mark;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.colors = InteractiveColorBlock();
            toggle.isOn = true;
        }

        // -----------------------------------------------------------------
        // SLIDERS Y SCROLLBARS
        // -----------------------------------------------------------------

        private void Build_REF_SLIDER_EDITOR(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_SLIDER_EDITOR,
                host, typeof(Image), typeof(Slider));
            PlaceCentered((RectTransform)root.transform, 275f, 20f);

            var track = root.GetComponent<Image>();
            track.color = SliderTrack;
            track.raycastTarget = true;

            var fill = NewUi("Fill", root.transform, typeof(Image));
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(.58f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(0f, -2f);
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = SliderFill;
            fillImage.raycastTarget = false;

            var handle = NewUi("Handle", root.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(.58f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.sizeDelta = new Vector2(10f, 26f);
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = SliderHandle;
            handleImage.raycastTarget = true;

            var slider = root.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.handleRect = handleRect;
            slider.fillRect = fillRect;
            slider.transition = Selectable.Transition.ColorTint;
            slider.colors = InteractiveColorBlock();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = .58f;
        }

        private void Build_REF_SLIDER_NATIVE(RectTransform host)
        {
            var root = NewUi(
                ScenarioUiReferenceCatalogRefs.REF_SLIDER_NATIVE,
                host, typeof(Image), typeof(Slider));

            PlaceCentered((RectTransform)root.transform, 275f, 20f);

            var track = root.GetComponent<Image>();
            track.sprite = null;
            track.type = Image.Type.Simple;
            track.color = NativeSliderTrack;
            track.raycastTarget = true;

            var fill = NewUi("Fill", root.transform, typeof(Image));
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(.52f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(0f, -2f);

            var fillImage = fill.GetComponent<Image>();
            fillImage.sprite = null;
            fillImage.type = Image.Type.Simple;
            fillImage.color = NativeSliderFill;
            fillImage.raycastTarget = false;

            var handle = NewUi("Handle", root.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(.52f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.sizeDelta = new Vector2(10f, 26f);

            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = null;
            handleImage.type = Image.Type.Simple;
            handleImage.color = Color.white;
            handleImage.raycastTarget = true;

            var slider = root.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.handleRect = handleRect;
            slider.fillRect = fillRect;
            slider.transition = Selectable.Transition.ColorTint;
            slider.colors = InteractiveColorBlock();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = .52f;
        }

        private void Build_REF_SCROLLBAR_VERTICAL_NATIVE(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_SCROLLBAR_VERTICAL_NATIVE,
                host, typeof(Image), typeof(Scrollbar));
            PlaceCentered((RectTransform)root.transform, 30f, 82f);

            var track = root.GetComponent<Image>();
            track.sprite = _roundedSprite;
            track.type = Image.Type.Sliced;
            track.color = NativeScrollbarTrack;
            track.raycastTarget = true;

            var area = NewUi("Sliding Area", root.transform);
            var areaRect = (RectTransform)area.transform;
            Stretch(areaRect);
            areaRect.offsetMin = new Vector2(3f, 3f);
            areaRect.offsetMax = new Vector2(-3f, -3f);

            var handle = NewUi("Handle", area.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, .58f);
            handleRect.anchorMax = new Vector2(1f, 1f);
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = _roundedSprite;
            handleImage.type = Image.Type.Sliced;
            handleImage.color = NativeScrollbarHandle;
            handleImage.raycastTarget = true;

            var scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.transition = Selectable.Transition.ColorTint;
            scrollbar.colors = InteractiveColorBlock();
            scrollbar.value = 1f;
            scrollbar.size = .42f;
        }

        private void Build_REF_SCROLLBAR_HORIZONTAL_OBJECTS(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_SCROLLBAR_HORIZONTAL_OBJECTS,
                host, typeof(Image), typeof(Scrollbar));
            PlaceCentered((RectTransform)root.transform, 275f, 14f);

            var track = root.GetComponent<Image>();
            track.color = new Color(.09f, .15f, .19f, .90f);
            track.raycastTarget = true;

            var area = NewUi("Sliding Area", root.transform);
            var areaRect = (RectTransform)area.transform;
            Stretch(areaRect);
            areaRect.offsetMin = new Vector2(2f, 1f);
            areaRect.offsetMax = new Vector2(-2f, -1f);

            var handle = NewUi("Handle", area.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(.33f, 0f);
            handleRect.anchorMax = new Vector2(.67f, 1f);
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(.43f, .80f, .95f, 1f);
            handleImage.raycastTarget = true;

            var scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scrollbar.transition = Selectable.Transition.ColorTint;
            scrollbar.colors = InteractiveColorBlock();
            scrollbar.value = .5f;
            scrollbar.size = .34f;
        }

        // -----------------------------------------------------------------
        // RESTO DE MUESTRAS
        // -----------------------------------------------------------------

        private void Build_REF_HISTORY_ROWS(RectTransform host)
        {
            var colors = new[]
            {
                new Color(.12f, .16f, .21f, 1f),
                new Color(.15f, .40f, .57f, 1f),
                new Color(.46f, .52f, .56f, 1f)
            };

            var labels = new[] { "Pasado", "Actual", "Futuro" };

            for (var index = 0; index < 3; ++index)
            {
                var root = NewUi(
                    ScenarioUiReferenceCatalogRefs.REF_HISTORY_ROWS + "_" + index,
                    host, typeof(Image));

                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(86f, 38f);
                rect.anchoredPosition = new Vector2(index * 91f, -3f);

                var image = root.GetComponent<Image>();
                image.color = colors[index];
                image.raycastTarget = false;

                var text = CreateLegacyText(root.transform, labels[index], 8,
                    index == 2 ? new Color(.72f, .78f, .82f, 1f) : Color.white,
                    TextAnchor.MiddleCenter, false);
                Stretch(text.rectTransform);
            }
        }

        private void Build_REF_DIVIDER_EDITOR(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_DIVIDER_EDITOR,
                host, typeof(Image));
            PlaceCentered((RectTransform)root.transform, 245f, 14f);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, .01f);
            root.GetComponent<Image>().raycastTarget = false;

            var handle = NewUi("Tirador", root.transform, typeof(Image));
            var rect = (RectTransform)handle.transform;
            rect.anchorMin = new Vector2(0f, .5f);
            rect.anchorMax = new Vector2(1f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-6f, 4f);

            var image = handle.GetComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(.18f, .72f, .96f, 1f);
            image.raycastTarget = false;
        }

        private void Build_REF_TEXTURE_TILE(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_TEXTURE_TILE,
                host, typeof(Image));
            PlaceCentered((RectTransform)root.transform, 52f, 52f);

            var border = root.GetComponent<Image>();
            border.color = new Color(.16f, .34f, .42f, 1f);
            border.raycastTarget = false;

            var preview = NewUi("Preview", root.transform, typeof(RawImage));
            var rect = (RectTransform)preview.transform;
            Stretch(rect);
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);

            var raw = preview.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
        }

        private void Build_REF_ROW_LANDSCAPE(RectTransform host)
        {
            var root = NewUi(ScenarioUiReferenceCatalogRefs.REF_ROW_LANDSCAPE,
                host, typeof(Image), typeof(Outline));
            PlaceCentered((RectTransform)root.transform, 245f, 40f);

            var image = root.GetComponent<Image>();
            image.color = new Color(.29f, .34f, .38f, .96f);
            image.raycastTarget = false;

            var outline = root.GetComponent<Outline>();
            outline.effectColor = new Color(.07f, .72f, 1f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            var text = CreateLegacyText(root.transform, "  Landscape", 9,
                Color.white, TextAnchor.MiddleLeft, true);
            Stretch(text.rectTransform);

            var check = NewUi("Visibility", root.transform, typeof(Image), typeof(Toggle));
            var checkRect = (RectTransform)check.transform;
            checkRect.anchorMin = checkRect.anchorMax = new Vector2(1f, .5f);
            checkRect.pivot = new Vector2(1f, .5f);
            checkRect.anchoredPosition = new Vector2(-4f, 0f);
            checkRect.sizeDelta = new Vector2(24f, 24f);

            var checkImage = check.GetComponent<Image>();
            checkImage.color = Color.white;
            checkImage.raycastTarget = true;

            var inner = NewUi("Inner", check.transform, typeof(Image));
            var innerRect = (RectTransform)inner.transform;
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(2f, 2f);
            innerRect.offsetMax = new Vector2(-2f, -2f);
            var innerImage = inner.GetComponent<Image>();
            innerImage.color = new Color(.18f, .22f, .26f, 1f);
            innerImage.raycastTarget = false;

            var mark = CreateLegacyText(check.transform, "✓", 11,
                Color.white, TextAnchor.MiddleCenter, true);
            Stretch(mark.rectTransform);

            var toggle = check.GetComponent<Toggle>();
            toggle.targetGraphic = checkImage;
            toggle.graphic = mark;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.colors = InteractiveColorBlock();
            toggle.isOn = true;
        }

        // -----------------------------------------------------------------
        // HELPERS AUTÓNOMOS
        // -----------------------------------------------------------------

        private Sprite CreateRoundedSprite(out Texture2D texture)
        {
            const int size = 32;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "UP_UI_REF_ROUNDED_TEXTURE";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; ++y)
            {
                for (var x = 0; x < size; ++x)
                {
                    var dx = Mathf.Max(Mathf.Abs(x - 15.5f) - 9.5f, 0f);
                    var dy = Mathf.Max(Mathf.Abs(y - 15.5f) - 9.5f, 0f);
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = (byte)Mathf.RoundToInt(
                        255f * Mathf.Clamp01(6.5f - distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(8f, 8f, 8f, 8f));

            sprite.name = "UP_UI_REF_ROUNDED_SPRITE";
            return sprite;
        }

        private Sprite CreateCircleSprite(out Texture2D texture)
        {
            const int size = 32;
            const float center = 15.5f;
            const float radius = 15f;

            texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "UP_UI_REF_CIRCLE_TEXTURE";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; ++y)
            {
                for (var x = 0; x < size; ++x)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = (byte)Mathf.RoundToInt(
                        255f * Mathf.Clamp01(radius + .75f - distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(.5f, .5f),
                100f);

            sprite.name = "UP_UI_REF_CIRCLE_SPRITE";
            return sprite;
        }

        private Text CreateLegacyText(
            Transform parent,
            string value,
            int size,
            Color color,
            TextAnchor alignment,
            bool bold)
        {
            var root = NewUi("Text", parent, typeof(Text));
            var text = root.GetComponent<Text>();
            text.font = _legacyFont;
            text.text = value ?? "";
            text.fontSize = Mathf.Max(12, Mathf.RoundToInt(size * SampleScale));
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private TextMeshProUGUI CreateTmpText(
            Transform parent,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment,
            bool bold)
        {
            var root = NewUi("Texto", parent, typeof(TextMeshProUGUI));
            var text = root.GetComponent<TextMeshProUGUI>();

            if (_tmpFont != null)
                text.font = _tmpFont;

            text.text = value ?? "";
            text.fontSize = Mathf.Max(12f, size * SampleScale);
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private static ColorBlock InteractiveColorBlock()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.96f, .96f, .96f, 1f);
            colors.pressedColor = new Color(.78f, .78f, .78f, 1f);
            colors.selectedColor = new Color(.90f, .90f, .90f, 1f);
            colors.disabledColor = new Color(.72f, .72f, .72f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .06f;
            return colors;
        }

        private static ColorBlock WhiteColorBlock()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .08f;
            return colors;
        }

        private static GameObject NewUi(
            string name,
            Transform parent,
            params Type[] extraComponents)
        {
            var components = new List<Type>
            {
                typeof(RectTransform),
                typeof(CanvasRenderer)
            };

            if (extraComponents != null)
            {
                foreach (var component in extraComponents)
                {
                    if (component != null && !components.Contains(component))
                        components.Add(component);
                }
            }

            var root = new GameObject(name, components.ToArray());
            root.transform.SetParent(parent, false);

            if (parent != null)
                root.layer = parent.gameObject.layer;

            return root;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void PlaceCentered(RectTransform rect, float width, float height)
        {
            if (rect == null) return;

            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -3f);
            rect.sizeDelta = new Vector2(width * SampleScale, height * SampleScale);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
    }

    [HarmonyPatch(typeof(OpeningScreenMenu), "Start")]
    internal static class ScenarioUiReferenceCatalogOpeningMenuPatch
    {
        private static void Postfix(OpeningScreenMenu __instance)
        {
            if (__instance == null) return;

            var catalog =
                __instance.GetComponent<ScenarioUiReferenceCatalog>() ??
                __instance.gameObject.AddComponent<ScenarioUiReferenceCatalog>();

            catalog.Initialise();
        }
    }
}
