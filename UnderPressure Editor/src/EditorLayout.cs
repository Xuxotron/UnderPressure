// Updated: 2026-10-20 - Distribucion acoplada del editor con cuatro paneles laterales de 220 px.
using System;
using System.Linq;
using HarmonyLib;
using TH20;
using TH20.ExtContent;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    // Un unico coordinador calcula toda la geometria. Los paneles inferiores
    // nunca deciden su altura: reciben exactamente el espacio que dejan los
    // paneles superiores y las barras de 50 px.
    internal sealed class ScenarioEditorDockLayout : MonoBehaviour
    {
        internal const float PanelWidth = 220f;
        internal const float BarHeight = 50f;
        private const float DividerHeight = 20f;
        private const float MinimumUpperHeight = 150f;
        private const float MinimumLowerHeight = 150f;

        private static ScenarioEditorDockLayout _active;
        private EditHospitalMenu _menu;
        private Level _level;
        private ScenarioEditorStorageMenu _storage;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _root;
        private RectTransform _plotPanel;
        private RectTransform _plotViewport;
        private Button _objectsToggle;
        private Button _historyToggle;
        private Button _terrainToggle;
        private RectTransform _leftDivider;
        private RectTransform _rightDivider;
        private GameObject _topBackground;
        private GameObject _leftTitle;
        private float _leftUpperHeight;
        private float _rightUpperHeight;
        private bool _leftHeightChosen;
        private bool _rightHeightChosen;
        private bool _historyOpen = true;
        private bool _terrainOpen = true;
        private float _lastCanvasWidth = -1f;
        private float _lastCanvasHeight = -1f;
        private WorkshopStyle _style;

        internal static void Attach(EditHospitalMenu menu, Level level,
            ScenarioEditorStorageMenu storage, Button template, RectTransform plotViewport)
        {
            if (menu == null || level == null || storage == null || template == null) return;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var component = canvas.GetComponent<ScenarioEditorDockLayout>() ??
                canvas.gameObject.AddComponent<ScenarioEditorDockLayout>();
            component.Initialise(menu, level, storage, template, plotViewport);
        }

        internal static void RegisterObjectsMenu(LandscapeObjectsMenu menu)
        {
            if (_active != null) _active.ApplyObjectsPanel(menu);
        }

        private void Initialise(EditHospitalMenu menu, Level level,
            ScenarioEditorStorageMenu storage, Button template, RectTransform plotViewport)
        {
            _active = this;
            _menu = menu;
            _level = level;
            _storage = storage;
            _canvas = menu.GetComponentInParent<Canvas>();
            _canvasRect = _canvas.transform as RectTransform;
            _plotViewport = plotViewport;
            _plotPanel = FindPlotPanel(menu.transform as RectTransform, plotViewport,
                storage.PlotLayerRoot as RectTransform);
            _style = WorkshopStyle.Read(level, template);

            if (_root == null)
            {
                var rootObject = new GameObject("UP Editor Dock Layout", typeof(RectTransform));
                rootObject.transform.SetParent(_canvas.transform, false);
                _root = (RectTransform)rootObject.transform;
                Stretch(_root);
                var overlay = rootObject.AddComponent<Canvas>();
                overlay.overrideSorting = true;
                overlay.sortingOrder = 3000;
                rootObject.AddComponent<GraphicRaycaster>();
                _root.SetAsLastSibling();
                BuildChrome(template);
            }

            _objectsToggle = storage.ObjectsButton;
            if (_objectsToggle != null) _objectsToggle.gameObject.name = "UP Objects Dock Toggle";
            ApplyLayout(true);
        }

        private void BuildChrome(Button template)
        {
            _topBackground = NewImage("UP Top Bar", _root, _style.PanelSprite,
                _style.HeaderColor, false);
            SetRect((RectTransform)_topBackground.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(.5f, 1f), Vector2.zero,
                new Vector2(0f, BarHeight));
            _topBackground.SetActive(false);

            _leftTitle = NewImage("UP Editor Title", _root, _style.PanelSprite,
                _style.HeaderColor, false);
            var titleRect = (RectTransform)_leftTitle.transform;
            SetTopLeft(titleRect, 0f, 0f, PanelWidth, BarHeight);
            CreateLabel(_leftTitle.transform, EditorLocalization.Get("editor.layout.title"), 18f);

            _historyToggle = CloneButton(template, _root, "UP History Dock Toggle",
                EditorLocalization.Get("editor.history.button"), ToggleHistory);
            _terrainToggle = CloneButton(template, _root, "UP Terrain Dock Toggle",
                EditorLocalization.Get("editor.terrain.toggle"), ToggleTerrain);

            _leftDivider = CreateDivider("UP Left Panel Divider", _root, SetLeftHeight);
            _rightDivider = CreateDivider("UP Right Panel Divider", _root, SetRightHeight);
        }

        private RectTransform CreateDivider(string name, Transform parent, Action<float> setter)
        {
            var go = NewImage(name, parent, null, new Color(0f, 0f, 0f, .01f), true);
            var handle = NewImage("Tirador", go.transform, _style.ButtonSprite,
                new Color(.18f, .72f, .96f, 1f), false);
            var handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, .5f);
            handleRect.anchorMax = new Vector2(1f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(-8f, 6f);
            var divider = go.AddComponent<ScenarioEditorDockDivider>();
            divider.Initialise(_root, setter);
            return (RectTransform)go.transform;
        }

        private void ToggleHistory()
        {
            _historyOpen = !_historyOpen;
            ScenarioEditorHistory.SetDockVisible(_historyOpen);
            ApplyLayout(true);
        }

        private void ToggleTerrain()
        {
            _terrainOpen = !_terrainOpen;
            ScenarioEditorTerrainPainter.SetDockVisible(_terrainOpen);
            ApplyLayout(true);
        }

        private void SetLeftHeight(float screenY)
        {
            if (_canvasRect == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, new Vector2(0f, screenY), _canvas.worldCamera, out local)) return;
            var top = _canvasRect.rect.yMax;
            _leftUpperHeight = top - local.y - BarHeight;
            _leftHeightChosen = true;
            ApplyLayout(true);
        }

        private void SetRightHeight(float screenY)
        {
            if (_canvasRect == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, new Vector2(0f, screenY), _canvas.worldCamera, out local)) return;
            var top = _canvasRect.rect.yMax;
            _rightUpperHeight = top - local.y - BarHeight;
            _rightHeightChosen = true;
            ApplyLayout(true);
        }

        private void LateUpdate()
        {
            if (_menu == null || _canvasRect == null || !ScenarioEditorFlow.IsRunning)
            {
                Destroy(this);
                return;
            }
            var size = _canvasRect.rect.size;
            var changed = !Mathf.Approximately(size.x, _lastCanvasWidth) ||
                          !Mathf.Approximately(size.y, _lastCanvasHeight);
            ApplyLayout(changed);
        }

        private void ApplyLayout(bool force)
        {
            if (_canvasRect == null || _plotPanel == null) return;
            var width = _canvasRect.rect.width;
            var height = _canvasRect.rect.height;
            if (width <= PanelWidth * 2f || height <= BarHeight * 3f) return;
            if (!force && Mathf.Approximately(width, _lastCanvasWidth) &&
                Mathf.Approximately(height, _lastCanvasHeight))
            {
                _objectsToggle = _storage?.ObjectsButton;
                PlaceCornerButton(_objectsToggle, false, false);
                PlaceCornerButton(_historyToggle, true, true);
                PlaceCornerButton(_terrainToggle, true, false);
                PlaceTopButtons(width);
                PlaceMapIdentity();
                ApplyDockStyles();
                ApplyObjectsPanel(FindActiveObjectsMenu());
                return;
            }
            _lastCanvasWidth = width;
            _lastCanvasHeight = height;

            var available = height - BarHeight * 2f;
            if (!_leftHeightChosen)
            {
                var count = Mathf.Max(1, _level?.WorldState?.HospitalPlots?.Count ?? 1);
                _leftUpperHeight = 145f + 39f * (count + 1);
            }
            if (!_rightHeightChosen) _rightUpperHeight = Mathf.Min(430f, available * .48f);
            _leftUpperHeight = Mathf.Clamp(_leftUpperHeight, MinimumUpperHeight,
                Mathf.Max(MinimumUpperHeight, available - MinimumLowerHeight));
            _rightUpperHeight = Mathf.Clamp(_rightUpperHeight, MinimumUpperHeight,
                Mathf.Max(MinimumUpperHeight, available - MinimumLowerHeight));

            SetTopLeft(_plotPanel, 0f, BarHeight, PanelWidth, _leftUpperHeight);
            FitPlotPanel();

            PlaceCornerButton(_objectsToggle, false, false);
            PlaceCornerButton(_historyToggle, true, true);
            PlaceCornerButton(_terrainToggle, true, false);
            PlaceTopButtons(width);
            PlaceMapIdentity();

            var objects = FindActiveObjectsMenu();
            ApplyObjectsPanel(objects);
            if (_leftDivider != null)
            {
                SetTopLeft(_leftDivider, 0f, BarHeight + _leftUpperHeight - DividerHeight * .5f,
                    PanelWidth, DividerHeight);
                _leftDivider.gameObject.SetActive(true);
                _leftDivider.SetAsLastSibling();
            }

            var rightContentHeight = height - BarHeight * 2f;
            float historyHeight;
            float terrainHeight;
            if (_historyOpen && _terrainOpen)
            {
                historyHeight = _rightUpperHeight;
                terrainHeight = Mathf.Max(0f, rightContentHeight - historyHeight);
            }
            else if (_historyOpen)
            {
                historyHeight = rightContentHeight;
                terrainHeight = 0f;
            }
            else
            {
                historyHeight = 0f;
                terrainHeight = _terrainOpen ? rightContentHeight : 0f;
            }
            ScenarioEditorHistory.SetDockRect(PanelWidth, historyHeight, BarHeight);
            ScenarioEditorHistory.SetDockVisible(_historyOpen);
            ScenarioEditorTerrainPainter.SetDockRect(PanelWidth, terrainHeight,
                BarHeight + historyHeight, BarHeight);
            ScenarioEditorTerrainPainter.SetDockVisible(_terrainOpen);
            ApplyDockStyles();

            if (_rightDivider != null)
            {
                SetTopRight(_rightDivider, 0f,
                    BarHeight + historyHeight - DividerHeight * .5f,
                    PanelWidth, DividerHeight);
                _rightDivider.gameObject.SetActive(_historyOpen && _terrainOpen);
                _rightDivider.SetAsLastSibling();
            }
            SetSelected(_historyToggle, _historyOpen);
            SetSelected(_terrainToggle, _terrainOpen);
        }

        private void ApplyDockStyles()
        {
            ScenarioEditorHistory.SetDockStyle(_style.PanelSprite, _style.PanelColor);
            ScenarioEditorTerrainPainter.SetDockStyle(_style.PanelSprite, _style.PanelColor);
        }

        private void FitPlotPanel()
        {
            if (_plotPanel == null) return;
            ApplyPanelStyle(_plotPanel.gameObject);
            EnsureSortingCanvas(_plotPanel.gameObject, 2850);
            _plotPanel.SetAsLastSibling();
            if (_plotViewport != null)
            {
                _plotViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    PanelWidth - 8f);
                var maximumListHeight = Mathf.Max(58f, _leftUpperHeight - 142f);
                _plotViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    maximumListHeight);
            }
            var layerRoot = _storage?.PlotLayerRoot as RectTransform;
            if (layerRoot != null)
                layerRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    PanelWidth - 10f);
            foreach (var button in _storage?.PlotChromeButtons ?? new Button[0])
            {
                if (button == null) continue;
                if (button.transform.parent == _menu.transform)
                    button.transform.SetParent(_root, false);
                ApplyButtonStyle(button);
                button.transform.SetAsLastSibling();
            }
            var landscape = _plotPanel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "UP Landscape - Row");
            var landscapeImage = landscape?.GetComponent<Image>();
            if (landscapeImage != null) landscapeImage.color = _style.ButtonColor;
            foreach (var row in _plotPanel.GetComponentsInChildren<PlotSelectionItem>(true))
            {
                var rect = row.transform as RectTransform;
                if (rect != null) rect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal, PanelWidth - 10f);
                CompactPlotRow(row, _style.ButtonSprite, _style.ButtonColor,
                    _style.SelectedColor);
            }
        }

        private static RectTransform FindPlotPanel(RectTransform menuRoot,
            RectTransform viewport, RectTransform layerRoot)
        {
            if (menuRoot == null || viewport == null) return viewport;
            var shared = layerRoot == null ? null : LowestCommonAncestor(viewport, layerRoot);
            if (shared != null && shared != menuRoot) return shared;

            RectTransform best = null;
            for (var current = viewport; current != null && current != menuRoot;
                current = current.parent as RectTransform)
            {
                var image = current.GetComponent<Image>();
                var width = Mathf.Abs(current.rect.width);
                if (image != null && width >= 160f && width <= 360f)
                    best = current;
            }
            return best ?? (viewport.parent as RectTransform) ?? viewport;
        }

        private static RectTransform LowestCommonAncestor(RectTransform first,
            RectTransform second)
        {
            if (first == null || second == null) return null;
            for (var left = first; left != null; left = left.parent as RectTransform)
                for (var right = second; right != null; right = right.parent as RectTransform)
                    if (ReferenceEquals(left, right)) return left;
            return null;
        }

        private static void CompactPlotRow(PlotSelectionItem row, Sprite sprite,
            Color normal, Color selected)
        {
            if (row == null) return;
            var text = AccessTools.Field(typeof(PlotSelectionItem), "_text")?
                .GetValue(row) as TMP_Text;
            if (text != null)
            {
                var textRect = text.transform as RectTransform;
                if (textRect != null)
                {
                    textRect.anchorMin = new Vector2(0f, 0f);
                    textRect.anchorMax = new Vector2(1f, 1f);
                    textRect.offsetMin = new Vector2(7f, 0f);
                    textRect.offsetMax = new Vector2(-126f, 0f);
                }
                text.enableAutoSizing = true;
                text.fontSizeMin = 9f;
                text.fontSizeMax = 14f;
                text.alignment = TextAlignmentOptions.MidlineLeft;
            }

            var names = new[]
            {
                "_toggleState", "_toggleVisible", "_toggleLayerBase",
                "_toggleLayerBuilt", "_toggleLayerUnbuilt"
            };
            for (var index = 0; index < names.Length; ++index)
            {
                var toggle = AccessTools.Field(typeof(PlotSelectionItem), names[index])?
                    .GetValue(row) as Toggle;
                var rect = toggle?.transform as RectTransform;
                if (rect == null) continue;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(-115f + index * 25f, 0f);
                rect.sizeDelta = new Vector2(22f, 22f);
            }
            var skin = row.GetComponent<ScenarioEditorPlotRowSkin>() ??
                row.gameObject.AddComponent<ScenarioEditorPlotRowSkin>();
            skin.Initialise(row, sprite, normal, selected);
        }

        private void PlaceTopButtons(float canvasWidth)
        {
            var buttons = new[]
            {
                _storage?.BuildButton,
                _storage?.CameraButton,
                ScenarioCharacterViewer.ToggleButton,
                ScenarioFogViewer.ToggleButton,
                _storage?.StylesButton
            }.Where(button => button != null).ToArray();
            if (buttons.Length == 0) return;
            var centralWidth = Mathf.Max(1f, canvasWidth - PanelWidth * 2f);
            var gap = 8f;
            var buttonWidth = Mathf.Min(208f,
                (centralWidth - gap * (buttons.Length + 1)) / buttons.Length);
            var total = buttonWidth * buttons.Length + gap * (buttons.Length - 1);
            var x = PanelWidth + (centralWidth - total) * .5f;
            foreach (var button in buttons)
            {
                var rect = button.transform as RectTransform;
                if (rect == null) continue;
                rect.SetParent(_root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, -5f);
                rect.sizeDelta = new Vector2(buttonWidth, 40f);
                ApplyButtonStyle(button);
                button.transform.SetAsLastSibling();
                x += buttonWidth + gap;
            }
        }

        private void PlaceCornerButton(Button button, bool right, bool top)
        {
            if (button == null) return;
            var rect = button.transform as RectTransform;
            if (rect == null) return;
            rect.SetParent(_root, false);
            if (right)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
                rect.pivot = new Vector2(1f, top ? 1f : 0f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, top ? 1f : 0f);
                rect.pivot = new Vector2(0f, top ? 1f : 0f);
            }
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(PanelWidth, BarHeight);
            ApplyButtonStyle(button);
            button.transform.SetAsLastSibling();
        }

        private void PlaceMapIdentity()
        {
            var identity = _storage?.MapIdentityRoot;
            var rect = identity?.transform as RectTransform;
            if (rect == null) return;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-PanelWidth - 8f, -5f);
            rect.sizeDelta = new Vector2(184f, 40f);
            rect.SetAsLastSibling();
        }

        private void ApplyObjectsPanel(LandscapeObjectsMenu menu)
        {
            if (menu == null || _canvasRect == null) return;
            var rect = menu.transform as RectTransform;
            if (rect == null) return;
            var height = Mathf.Max(MinimumLowerHeight,
                _canvasRect.rect.height - BarHeight * 2f - _leftUpperHeight);
            rect.SetParent(_canvas.transform, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(0f, BarHeight);
            rect.sizeDelta = new Vector2(PanelWidth, height);
            ApplyPanelStyle(menu.gameObject);
            EnsureSortingCanvas(menu.gameObject, 2850);
            ScenarioEditorObjectDock.Configure(menu, PanelWidth, height);
        }

        private LandscapeObjectsMenu FindActiveObjectsMenu()
        {
            LandscapeObjectsMenu fallback = null;
            foreach (var menu in Resources.FindObjectsOfTypeAll<LandscapeObjectsMenu>())
            {
                if (menu == null || !menu.gameObject.scene.IsValid()) continue;
                if (menu.gameObject.activeInHierarchy) return menu;
                if (fallback == null) fallback = menu;
            }
            return fallback != null && fallback.gameObject.activeSelf ? fallback : null;
        }

        private void ApplyPanelStyle(GameObject panel)
        {
            if (panel == null) return;
            var image = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            if (_style.PanelSprite != null)
            {
                image.sprite = _style.PanelSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = _style.PanelColor;
            image.raycastTarget = true;
        }

        private static void EnsureSortingCanvas(GameObject root, int order)
        {
            if (root == null) return;
            var canvas = root.GetComponent<Canvas>() ?? root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            if (root.GetComponent<GraphicRaycaster>() == null)
                root.AddComponent<GraphicRaycaster>();
        }

        private void ApplyButtonStyle(Button button)
        {
            if (button == null) return;
            if (button.image != null)
            {
                if (_style.ButtonSprite != null)
                {
                    button.image.sprite = _style.ButtonSprite;
                    button.image.type = Image.Type.Sliced;
                }
                button.image.color = _style.ButtonColor;
            }
            button.colors = _style.ButtonColors;
        }

        private void SetSelected(Button button, bool selected)
        {
            if (button?.image == null) return;
            button.image.color = selected ? _style.SelectedColor : _style.ButtonColor;
        }

        private Button CloneButton(Button source, Transform parent, string name,
            string text, Action action)
        {
            var button = Instantiate(source, parent, false);
            button.gameObject.name = name;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            button.gameObject.SetActive(true);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
            ApplyButtonStyle(button);
            return button;
        }

        private static GameObject NewImage(string name, Transform parent, Sprite sprite,
            Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return go;
        }

        private static void CreateLabel(Transform parent, string value, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void SetTopLeft(RectTransform rect, float x, float y,
            float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopRight(RectTransform rect, float x, float y,
            float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private sealed class WorkshopStyle
        {
            internal Sprite PanelSprite;
            internal Sprite ButtonSprite;
            internal Color PanelColor = new Color(.018f, .25f, .43f, 1f);
            internal Color HeaderColor = new Color(.018f, .25f, .43f, 1f);
            internal Color ButtonColor = new Color(.035f, .43f, .70f, 1f);
            internal Color SelectedColor = new Color(.02f, .58f, .88f, 1f);
            internal ColorBlock ButtonColors = ColorBlock.defaultColorBlock;

            internal static WorkshopStyle Read(Level level, Button fallback)
            {
                var result = new WorkshopStyle();
                if (fallback != null)
                {
                    result.ButtonColors = fallback.colors;
                    if (fallback.image != null)
                    {
                        result.ButtonSprite = fallback.image.sprite;
                        result.ButtonColor = fallback.image.color;
                    }
                }
                try
                {
                    var prefab = level?.App?.ExtContentManager?.ExtContentUIManager?
                        .Config?.WorkshopPublishUIScreenPrefab;
                    if (prefab == null) return result;
                    var nativeButton = prefab.GetComponentsInChildren<DynamicButton>(true)
                        .FirstOrDefault(button => button.targetGraphic is Image);
                    var buttonImage = nativeButton?.targetGraphic as Image;
                    if (buttonImage != null)
                    {
                        result.ButtonSprite = buttonImage.sprite;
                        result.ButtonColor = buttonImage.color;
                        result.ButtonColors = nativeButton.colors;
                    }
                    var panel = prefab.GetComponentsInChildren<Image>(true)
                        .Where(image => image.sprite != null &&
                            image.GetComponentInParent<DynamicButton>() == null)
                        .OrderByDescending(image =>
                        {
                            var rect = image.transform as RectTransform;
                            return rect == null ? 0f : Mathf.Abs(rect.rect.width * rect.rect.height);
                        }).FirstOrDefault();
                    if (panel != null)
                    {
                        result.PanelSprite = panel.sprite;
                    }
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Editor UI: no se pudo leer el estilo Workshop: " + exception.Message);
                }
                return result;
            }
        }
    }

    internal sealed class ScenarioEditorDockDivider : MonoBehaviour,
        IBeginDragHandler, IDragHandler
    {
        private RectTransform _root;
        private Action<float> _setter;

        internal void Initialise(RectTransform root, Action<float> setter)
        {
            _root = root;
            _setter = setter;
        }

        public void OnBeginDrag(PointerEventData eventData) => Apply(eventData);
        public void OnDrag(PointerEventData eventData) => Apply(eventData);

        private void Apply(PointerEventData eventData)
        {
            if (_root == null || _setter == null || eventData == null) return;
            _setter(eventData.position.y);
        }
    }

    internal sealed class ScenarioEditorPlotRowSkin : MonoBehaviour
    {
        private Button _button;
        private Toggle[] _toggles;
        private Sprite _sprite;
        private Color _normal;
        private Color _selectedColor;
        private bool _selected;
        private bool _initialised;

        internal void Initialise(PlotSelectionItem row, Sprite sprite,
            Color normal, Color selected)
        {
            _sprite = sprite;
            _normal = normal;
            _selectedColor = selected;
            if (!_initialised)
            {
                _button = AccessTools.Field(typeof(PlotSelectionItem), "_button")?
                    .GetValue(row) as Button;
                var nativeSelected = AccessTools.Field(typeof(PlotSelectionItem), "_selectedColor")?
                    .GetValue(row);
                if (_button?.targetGraphic != null && nativeSelected is Color)
                    _selected = ColorDistance(_button.targetGraphic.color,
                        (Color)nativeSelected) < .01f;
                var names = new[]
                {
                    "_toggleState", "_toggleVisible", "_toggleLayerBase",
                    "_toggleLayerBuilt", "_toggleLayerUnbuilt"
                };
                _toggles = names.Select(name =>
                    AccessTools.Field(typeof(PlotSelectionItem), name)?.GetValue(row) as Toggle)
                    .Where(toggle => toggle != null).ToArray();
                _initialised = true;
            }
            Apply();
        }

        internal void SetSelected(bool selected)
        {
            _selected = selected;
            Apply();
        }

        private void Apply()
        {
            if (_button?.targetGraphic is Image buttonImage)
            {
                if (_sprite != null)
                {
                    buttonImage.sprite = _sprite;
                    buttonImage.type = Image.Type.Sliced;
                }
                var baseColor = _selected ? _selectedColor : _normal;
                buttonImage.color = baseColor;
                var colors = _button.colors;
                colors.normalColor = baseColor;
                colors.highlightedColor = Color.Lerp(baseColor, Color.white, .16f);
                colors.pressedColor = Color.Lerp(baseColor, Color.black, .18f);
                colors.selectedColor = colors.highlightedColor;
                _button.colors = colors;
            }
            if (_toggles == null) return;
            foreach (var toggle in _toggles)
            {
                if (toggle?.targetGraphic is Image background)
                {
                    if (_sprite != null)
                    {
                        background.sprite = _sprite;
                        background.type = Image.Type.Sliced;
                    }
                    background.color = _normal;
                }
                if (toggle?.graphic is Graphic mark) mark.color = Color.white;
            }
        }

        private static float ColorDistance(Color first, Color second) =>
            Mathf.Abs(first.r - second.r) + Mathf.Abs(first.g - second.g) +
            Mathf.Abs(first.b - second.b) + Mathf.Abs(first.a - second.a);
    }

    [HarmonyPatch(typeof(PlotSelectionItem), "OnSelectHospitalPlot")]
    internal static class ScenarioEditorPlotRowSelectionPatch
    {
        private static readonly System.Reflection.FieldInfo PlotField =
            AccessTools.Field(typeof(PlotSelectionItem), "_plot");

        private static void Postfix(PlotSelectionItem __instance, HospitalPlot hospitalPlot)
        {
            if (!ScenarioEditorFlow.IsRunning || __instance == null) return;
            var skin = __instance.GetComponent<ScenarioEditorPlotRowSkin>();
            if (skin != null)
                skin.SetSelected(ReferenceEquals(PlotField?.GetValue(__instance), hospitalPlot));
        }
    }

    internal static class ScenarioEditorObjectDock
    {
        private static readonly System.Reflection.FieldInfo ContentField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_listContent");
        private static readonly System.Reflection.FieldInfo SearchField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_searchBox");

        internal static void Configure(LandscapeObjectsMenu menu, float width, float height)
        {
            if (menu == null) return;
            var contentObject = ContentField?.GetValue(menu) as GameObject;
            var search = SearchField?.GetValue(menu) as TMP_InputField;
            if (contentObject == null) return;
            var content = contentObject.transform as RectTransform;
            var scroll = content?.GetComponentInParent<ScrollRect>();
            var scrollRect = scroll?.transform as RectTransform;
            if (content == null || scroll == null || scrollRect == null) return;

            var oldSizer = content.GetComponent<ScenarioCatalogVisualBoundsSizer>();
            if (oldSizer != null) oldSizer.enabled = false;
            var nativeLayout = content.GetComponent<DynamicLayoutGroup>();
            if (nativeLayout != null) nativeLayout.enabled = false;
            var horizontalLayout = content.GetComponent<HorizontalLayoutGroup>();
            if (horizontalLayout != null) horizontalLayout.enabled = false;
            var verticalLayout = content.GetComponent<VerticalLayoutGroup>();
            if (verticalLayout != null) verticalLayout.enabled = false;
            var searchFollower = search?.GetComponent<ScenarioEditorSearchBelowPlots>();
            if (searchFollower != null) searchFollower.enabled = false;
            var oldBar = menu.transform.Find("UP_Catalog_HorizontalScrollbar");
            if (oldBar != null) oldBar.gameObject.SetActive(false);
            var oldBackdrop = menu.transform.Find("UP_Backdrop_Extension");
            if (oldBackdrop != null) oldBackdrop.gameObject.SetActive(false);

            var grid = content.GetComponent<GridLayoutGroup>() ??
                content.gameObject.AddComponent<GridLayoutGroup>();
            grid.enabled = true;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(5, 9, 6, 8);
            grid.spacing = new Vector2(6f, 6f);
            grid.cellSize = new Vector2(97f, 88f);

            var fitter = content.GetComponent<ContentSizeFitter>() ??
                content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.enabled = true;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 38f;
            scroll.content = content;
            if (scroll.viewport == null) scroll.viewport = content.parent as RectTransform;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.pivot = new Vector2(.5f, .5f);
            scrollRect.offsetMin = new Vector2(6f, 6f);
            scrollRect.offsetMax = new Vector2(-6f, -43f);
            if (scroll.viewport != null)
            {
                scroll.viewport.anchorMin = Vector2.zero;
                scroll.viewport.anchorMax = Vector2.one;
                scroll.viewport.offsetMin = Vector2.zero;
                scroll.viewport.offsetMax = new Vector2(-12f, 0f);
                var viewportImage = scroll.viewport.GetComponent<Image>() ??
                    scroll.viewport.gameObject.AddComponent<Image>();
                if (viewportImage.sprite == null)
                    viewportImage.color = new Color(0f, 0f, 0f, .001f);
                viewportImage.raycastTarget = true;
            }
            EnsureVerticalScrollbar(menu.transform as RectTransform, scroll);

            if (search != null)
            {
                var searchRect = search.transform as RectTransform;
                searchRect.anchorMin = searchRect.anchorMax = new Vector2(0f, 1f);
                searchRect.pivot = new Vector2(0f, 1f);
                searchRect.anchoredPosition = new Vector2(69f, -8f);
                searchRect.sizeDelta = new Vector2(91f, 27f);
                var label = menu.transform.Find("UP_SearchLabel") as RectTransform;
                if (label != null)
                {
                    label.anchorMin = label.anchorMax = new Vector2(0f, 1f);
                    label.pivot = new Vector2(0f, 1f);
                    label.anchoredPosition = new Vector2(7f, -8f);
                    label.sizeDelta = new Vector2(60f, 27f);
                }
                var show = menu.transform.Find("UP Objects - Show All") as RectTransform;
                if (show != null)
                {
                    show.anchorMin = show.anchorMax = new Vector2(0f, 1f);
                    show.pivot = new Vector2(0f, 1f);
                    show.anchoredPosition = new Vector2(164f, -8f);
                    show.sizeDelta = new Vector2(49f, 27f);
                }
            }
        }

        private static void EnsureVerticalScrollbar(RectTransform host, ScrollRect scroll)
        {
            if (host == null || scroll == null) return;
            var existing = host.Find("UP_Catalog_VerticalScrollbar") as RectTransform;
            if (existing == null)
            {
                var track = new GameObject("UP_Catalog_VerticalScrollbar", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
                track.transform.SetParent(host, false);
                existing = (RectTransform)track.transform;
                track.GetComponent<Image>().color = new Color(.035f, .20f, .32f, .95f);
                var area = new GameObject("Sliding Area", typeof(RectTransform));
                area.transform.SetParent(track.transform, false);
                var areaRect = (RectTransform)area.transform;
                areaRect.anchorMin = Vector2.zero;
                areaRect.anchorMax = Vector2.one;
                areaRect.offsetMin = new Vector2(2f, 3f);
                areaRect.offsetMax = new Vector2(-2f, -3f);
                var handle = new GameObject("Handle", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                handle.transform.SetParent(area.transform, false);
                var handleRect = (RectTransform)handle.transform;
                handleRect.anchorMin = Vector2.zero;
                handleRect.anchorMax = Vector2.one;
                handleRect.offsetMin = Vector2.zero;
                handleRect.offsetMax = Vector2.zero;
                var handleImage = handle.GetComponent<Image>();
                handleImage.color = new Color(.34f, .78f, .96f, 1f);
                var bar = track.GetComponent<Scrollbar>();
                bar.direction = Scrollbar.Direction.BottomToTop;
                bar.targetGraphic = handleImage;
                bar.handleRect = handleRect;
            }
            existing.anchorMin = new Vector2(1f, 0f);
            existing.anchorMax = new Vector2(1f, 1f);
            existing.pivot = new Vector2(1f, .5f);
            existing.anchoredPosition = Vector2.zero;
            existing.offsetMin = new Vector2(-12f, 6f);
            existing.offsetMax = new Vector2(0f, -43f);
            existing.gameObject.SetActive(true);
            existing.SetAsLastSibling();
            scroll.verticalScrollbar = existing.GetComponent<Scrollbar>();
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 2f;
        }
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Setup")]
    internal static class ScenarioEditorObjectDockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(LandscapeObjectsMenu __instance)
        {
            if (ScenarioEditorFlow.IsRunning)
                ScenarioEditorDockLayout.RegisterObjectsMenu(__instance);
        }
    }
}
