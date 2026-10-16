using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using I2.Loc;
using TH20;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    // Portal independiente para mapas compartidos, agrupados por creador.
    internal sealed class ScenarioConfluxMenu : MonoBehaviour
    {
        private sealed class CreatorChoice
        {
            internal string Name;
            internal string Folder;
            internal readonly List<MapChoice> Maps = new List<MapChoice>();
        }

        private sealed class MapChoice
        {
            internal string Path;
            internal string Name;
            internal string Description;
            internal LevelConfig BaseHospital;
        }

        private static readonly FieldInfo NameField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_hospitalNameLabel");
        private static readonly FieldInfo DescriptionField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_hospitalDescriptionText");
        private static readonly FieldInfo DescriptionPanelField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_hospitalDescriptionPanel");
        private static readonly FieldInfo StarsPanelField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_starsPanel");
        private static readonly FieldInfo CurrentSavePanelField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_currentSavePanel");
        private static readonly FieldInfo StartNewPanelField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_startNewButtonPanel");
        private static readonly FieldInfo FriendsPanelField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_friendsPanel");
        private static readonly FieldInfo MainButtonField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_button");
        private static readonly FieldInfo MainTextField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_buttonText");
        private static readonly FieldInfo RemixTabsField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_remixModeTabs");
        private static readonly FieldInfo NativeBackgroundField =
            AccessTools.Field(typeof(SelectedHospitalMenu), "_defaultBackground");

        private static MapPinHospital _pendingPin;
        private static ScenarioConfluxMenu _configuringMenu;

        private SelectedHospitalMenu _native;
        private MetagameMap _map;
        private RectTransform _nativeRect;
        private GameObject _creatorWindow;
        private GameObject _mapWindow;
        private RectTransform _creatorFrame;
        private RectTransform _mapFrame;
        private RectTransform _creatorContent;
        private RectTransform _mapContent;
        private readonly List<CreatorChoice> _creators = new List<CreatorChoice>();
        private readonly List<Button> _creatorButtons = new List<Button>();
        private readonly List<Button> _mapButtons = new List<Button>();
        private CreatorChoice _selectedCreator;
        private MapChoice _selectedMap;
        private TMP_Text _nativeName;
        private TMP_Text _nativeDescription;
        private Localize _nativeMainText;
        private DynamicButton _nativeMainButton;
        private Sprite _selectedSprite;
        private bool _open;
        private bool _preparing;

        internal bool IsOpen => _open;

        private static string T(string key) => EditorLocalization.Get(key);

        internal static void BeginPinSelection(MapPinHospital pin)
        {
            _pendingPin = ScenarioWorldMapAuthoring.TryGetConflux(pin, out _) ? pin : null;
        }

        internal static void EndPinSelection()
        {
            _pendingPin = null;
        }

        internal static bool HandleNativeSetup(SelectedHospitalMenu menu)
        {
            if (menu == null) return false;
            if (_configuringMenu != null && ReferenceEquals(_configuringMenu._native, menu))
                return true;
            if (_pendingPin == null ||
                !ScenarioWorldMapAuthoring.TryGetConflux(_pendingPin, out _))
                return false;
            var component = menu.GetComponent<ScenarioConfluxMenu>() ??
                menu.gameObject.AddComponent<ScenarioConfluxMenu>();
            component.Open(menu);
            return true;
        }

        private void Open(SelectedHospitalMenu menu)
        {
            Close();
            _native = menu;
            _nativeRect = menu.transform as RectTransform;
            _map = AccessTools.Field(typeof(SelectedHospitalMenu), "_metagameMap")
                ?.GetValue(menu) as MetagameMap;
            if (_nativeRect == null || _map?.Metagame == null) return;
            CaptureNativeReferences();
            LoadCreators();
            Build();
            _open = _creatorWindow != null && _mapWindow != null;
            RebuildCreators();
            RebuildMaps();
            ApplyEmptyDetails();
        }

        private void CaptureNativeReferences()
        {
            _nativeName = (NameField?.GetValue(_native) as Component)?.GetComponent<TMP_Text>();
            if (_nativeName == null)
                _nativeName = (NameField?.GetValue(_native) as Component)
                    ?.GetComponentInChildren<TMP_Text>(true);
            _nativeDescription = DescriptionField?.GetValue(_native) as TMP_Text;
            _nativeMainText = MainTextField?.GetValue(_native) as Localize;
            _nativeMainButton = MainButtonField?.GetValue(_native) as DynamicButton;
        }

        private void LoadCreators()
        {
            _creators.Clear();
            var root = Path.GetFullPath(ScenarioEditorStorage.Folder);
            if (!Directory.Exists(root)) return;
            foreach (var creatorFolder in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var creatorName = Path.GetFileName(creatorFolder);
                if (string.IsNullOrWhiteSpace(creatorName) ||
                    creatorName.StartsWith("_", StringComparison.Ordinal))
                    continue;
                var creator = new CreatorChoice
                {
                    Name = creatorName,
                    Folder = creatorFolder
                };
                foreach (var path in Directory.GetFiles(creatorFolder, "*.upmap",
                             SearchOption.AllDirectories))
                {
                    try
                    {
                        var header = ScenarioMapFormat.ReadHeader(path);
                        var hospital = _map.Metagame.LevelList
                            .GetLevelConfigByID(header.baseHospitalId);
                        if (hospital == null)
                        {
                            UnderPressureEditorPlugin.Log?.LogWarning(
                                "Conflux ignora un mapa sin hospital base: " + path);
                            continue;
                        }
                        var description = string.IsNullOrWhiteSpace(header.description)
                            ? T("career.maps.custom.description") : header.description;
                        if (description.Length > 1400)
                            description = description.Substring(0, 1400);
                        creator.Maps.Add(new MapChoice
                        {
                            Path = path,
                            Name = header.name,
                            Description = description,
                            BaseHospital = hospital
                        });
                    }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Conflux ignora un mapa incorrecto: " + path + ": " + exception.Message);
                    }
                }
                creator.Maps.Sort((left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
                if (creator.Maps.Count > 0) _creators.Add(creator);
            }
            _creators.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        }

        private void Build()
        {
            var parent = _nativeRect.parent as RectTransform;
            if (parent == null) return;
            _creatorWindow = BuildWindow("UP_ConfluxCreators", parent,
                T("conflux.creators"), out _creatorFrame, out _creatorContent);
            _mapWindow = BuildWindow("UP_ConfluxMaps", parent,
                T("conflux.maps"), out _mapFrame, out _mapContent);
            CaptureSelectedSprite();
            SyncGeometry();
        }

        private GameObject BuildWindow(string name, RectTransform parent, string title,
            out RectTransform frame, out RectTransform content)
        {
            content = null;
            var root = Ui(name, parent);
            frame = (RectTransform)root.transform;
            Place(frame, Vector2.zero, new Vector2(282f, 420f));
            root.AddComponent<LayoutElement>().ignoreLayout = true;
            if (!CopyNativeFrame(frame))
            {
                Destroy(root);
                frame = null;
                return null;
            }
            var titleObject = Ui("Title", frame);
            Place((RectTransform)titleObject.transform, new Vector2(20f, -15f),
                new Vector2(242f, 44f));
            AddLabel(titleObject.transform, title, 21f);

            var scrollObject = Ui("Scroll", frame, typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollObject.transform;
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(15f, 20f);
            scrollRect.offsetMax = new Vector2(-15f, -70f);
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var viewport = Ui("Viewport", scrollRect, typeof(Image), typeof(Mask));
            Fill((RectTransform)viewport.transform);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.002f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            var contentObject = Ui("Buttons", viewport.transform,
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 2, 2);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = content;
            return root;
        }

        private bool CopyNativeFrame(RectTransform target)
        {
            var background = NativeBackgroundField?.GetValue(_native) as LayoutElement;
            if (background == null) return false;
            var clone = Instantiate(background.gameObject, target, false);
            clone.name = "TPH_NativeConfluxBackground";
            clone.SetActive(true);
            var rect = clone.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
            }
            foreach (var button in clone.GetComponentsInChildren<Button>(true))
                button.interactable = false;
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
                text.gameObject.SetActive(false);
            foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            return clone.GetComponentsInChildren<Image>(true).Length > 0;
        }

        private void CaptureSelectedSprite()
        {
            _selectedSprite = null;
            var tabs = RemixTabsField?.GetValue(_native) as DynamicButton[];
            if (tabs == null) return;
            foreach (var tab in tabs)
            {
                var animator = tab == null ? null : tab.GetComponent<ButtonAnimator>();
                var preset = animator == null ? null :
                    AccessTools.Field(typeof(ButtonAnimator), "_preset")?.GetValue(animator)
                        as ButtonAnimatorPreset;
                if (preset?.SelectedBackgroundSprite == null) continue;
                _selectedSprite = preset.SelectedBackgroundSprite;
                return;
            }
        }

        private void RebuildCreators()
        {
            ClearButtons(_creatorButtons, _creatorContent);
            if (_creatorContent == null) return;
            if (_creators.Count == 0)
            {
                AddRow(_creatorContent, T("conflux.no_creators"), null, _creatorButtons);
                return;
            }
            foreach (var creator in _creators)
            {
                var selected = creator;
                AddRow(_creatorContent, creator.Name, () => SelectCreator(selected),
                    _creatorButtons);
            }
        }

        private void RebuildMaps()
        {
            ClearButtons(_mapButtons, _mapContent);
            if (_mapContent == null) return;
            if (_selectedCreator == null || _selectedCreator.Maps.Count == 0)
            {
                AddRow(_mapContent, _selectedCreator == null
                    ? T("conflux.select_creator") : T("conflux.no_maps"), null, _mapButtons);
                return;
            }
            foreach (var map in _selectedCreator.Maps)
            {
                var selected = map;
                AddRow(_mapContent, map.Name, () => SelectMap(selected), _mapButtons);
            }
        }

        private Button AddRow(Transform parent, string text, Action action, List<Button> buttons)
        {
            var row = Ui("Choice_" + text, parent, typeof(Image), typeof(Button),
                typeof(LayoutElement));
            StyleBlue(row.GetComponent<Image>());
            var button = row.GetComponent<Button>();
            button.interactable = action != null;
            if (action != null) button.onClick.AddListener(() => action());
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 52f;
            element.preferredHeight = 52f;
            AddLabel(row.transform, text, 20f);
            buttons.Add(button);
            return button;
        }

        private static void ClearButtons(List<Button> buttons, RectTransform content)
        {
            foreach (var button in buttons)
                if (button != null) Destroy(button.gameObject);
            buttons.Clear();
            if (content == null) return;
            for (var index = content.childCount - 1; index >= 0; --index)
                Destroy(content.GetChild(index).gameObject);
        }

        private void SelectCreator(CreatorChoice creator)
        {
            _selectedCreator = creator;
            _selectedMap = null;
            Highlight(_creatorButtons, _creators.IndexOf(creator));
            RebuildMaps();
            ApplyEmptyDetails();
        }

        private void SelectMap(MapChoice choice)
        {
            if (choice?.BaseHospital == null) return;
            var record = _map.Metagame.GetHospitalRecord(choice.BaseHospital, false);
            if (record == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Conflux no encontro el registro del hospital base: " +
                    choice.BaseHospital.UniqueId);
                return;
            }
            _configuringMenu = this;
            try
            {
                _native.Setup(choice.BaseHospital, record,
                    _map.Metagame.OnlineMetadataManager, _map, _map.SaveSystem);
            }
            finally
            {
                _configuringMenu = null;
            }
            CaptureNativeReferences();
            _selectedMap = choice;
            Highlight(_mapButtons, _selectedCreator.Maps.IndexOf(choice));
            ApplySelectedDetails();
        }

        private void Highlight(List<Button> buttons, int selectedIndex)
        {
            for (var index = 0; index < buttons.Count; ++index)
            {
                var image = buttons[index] == null ? null : buttons[index].GetComponent<Image>();
                if (image == null) continue;
                image.overrideSprite = index == selectedIndex ? _selectedSprite : null;
                image.color = Color.white;
            }
        }

        private void ApplyEmptyDetails()
        {
            if (_nativeName != null) _nativeName.text = T("conflux.title");
            if (_nativeDescription != null)
                _nativeDescription.text = _selectedCreator == null
                    ? T("conflux.select_creator") : T("conflux.select_map");
            SetNativePanels(false);
            if (_nativeMainButton != null) _nativeMainButton.interactable = false;
        }

        private void ApplySelectedDetails()
        {
            if (_selectedMap == null) return;
            if (_nativeName != null) _nativeName.text = _selectedMap.Name;
            if (_nativeDescription != null) _nativeDescription.text = _selectedMap.Description;
            var descriptionPanel = DescriptionPanelField?.GetValue(_native) as GameObject;
            if (descriptionPanel != null) descriptionPanel.SetActive(true);
            SetNativePanels(false);
            if (_nativeMainText != null && _nativeMainText.Term != "Misc/Start_CS")
                _nativeMainText.SetTerm("Misc/Start_CS");
            if (_nativeMainButton != null) _nativeMainButton.interactable = true;
        }

        private void SetNativePanels(bool active)
        {
            foreach (var field in new[]
                     {
                         StarsPanelField, CurrentSavePanelField, StartNewPanelField, FriendsPanelField
                     })
            {
                var panel = field?.GetValue(_native) as GameObject;
                if (panel != null) panel.SetActive(active);
            }
        }

        internal bool BeforeNativeStart()
        {
            if (!_open || _preparing) return true;
            if (_selectedMap == null)
            {
                ApplyEmptyDetails();
                return false;
            }
            _preparing = true;
            try
            {
                ScenarioCareerMapLoader.Prepare(_selectedMap.BaseHospital, _selectedMap.Path);
                return true;
            }
            catch (Exception exception)
            {
                ScenarioCareerMapLoader.Cancel();
                UnderPressureEditorPlugin.Log?.LogError(
                    "Conflux no pudo iniciar el mapa: " + exception);
                if (_nativeDescription != null)
                    _nativeDescription.text = T("career.maps.open.error");
                return false;
            }
            finally
            {
                _preparing = false;
            }
        }

        private void LateUpdate()
        {
            if (!_open || _native == null || !_native.gameObject.activeInHierarchy) return;
            if (_native.IsClosing()) return;
            SyncGeometry();
            if (_selectedMap == null) ApplyEmptyDetails();
            else ApplySelectedDetails();
        }

        private void SyncGeometry()
        {
            if (_nativeRect == null || _creatorFrame == null || _mapFrame == null) return;
            var parent = _nativeRect.parent as RectTransform;
            if (parent == null) return;
            var corners = new Vector3[4];
            _nativeRect.GetWorldCorners(corners);
            var bottomLeft = parent.InverseTransformPoint(corners[0]);
            var topRight = parent.InverseTransformPoint(corners[2]);
            var height = topRight.y - bottomLeft.y;
            if (height <= 0f) return;
            var parentTopLeft = new Vector2(parent.rect.xMin, parent.rect.yMax);
            var first = new Vector2(topRight.x - 18f - parentTopLeft.x,
                topRight.y - parentTopLeft.y);
            _creatorFrame.anchoredPosition = first;
            _creatorFrame.sizeDelta = new Vector2(282f, height);
            _mapFrame.anchoredPosition = first + new Vector2(264f, 0f);
            _mapFrame.sizeDelta = new Vector2(282f, height);
            var originalIndex = _nativeRect.GetSiblingIndex();
            _creatorFrame.SetSiblingIndex(originalIndex);
            _mapFrame.SetSiblingIndex(originalIndex + 1);
        }

        private void StyleBlue(Image image)
        {
            if (image == null) return;
            var tabs = RemixTabsField?.GetValue(_native) as DynamicButton[];
            if (tabs == null || tabs.Length < 2 || tabs[1] == null) return;
            var source = tabs[1].GetComponent<Image>();
            if (source == null) return;
            image.sprite = source.sprite;
            image.type = source.type;
            image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            image.material = source.material;
            image.color = source.color;
        }

        private void AddLabel(Transform parent, string text, float size)
        {
            var root = Ui("Text", parent, typeof(TextMeshProUGUI));
            Fill((RectTransform)root.transform);
            var label = root.GetComponent<TextMeshProUGUI>();
            var original = (MainTextField?.GetValue(_native) as Component)
                ?.GetComponent<TMP_Text>();
            if (original?.font != null) label.font = original.font;
            label.text = text;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.margin = new Vector4(8f, 0f, 8f, 0f);
            label.raycastTarget = false;
        }

        internal void Close()
        {
            if (_creatorWindow != null) Destroy(_creatorWindow);
            if (_mapWindow != null) Destroy(_mapWindow);
            _creatorWindow = null;
            _mapWindow = null;
            _creatorFrame = null;
            _mapFrame = null;
            _creatorContent = null;
            _mapContent = null;
            _creatorButtons.Clear();
            _mapButtons.Clear();
            _creators.Clear();
            _selectedCreator = null;
            _selectedMap = null;
            _open = false;
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            Close();
        }

        private static GameObject Ui(string name, Transform parent, params Type[] components)
        {
            var types = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
            types.AddRange(components);
            var result = new GameObject(name, types.ToArray());
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
