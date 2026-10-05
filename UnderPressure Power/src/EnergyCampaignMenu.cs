// Purpose: Builds the transformer campaign interface, contracted-power selector, and progress controls.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnderPressure.PowerGrid
{
    /// <summary>
    /// Independent controller for a visual clone of the native campaign panel.
    /// The source prefab is read only as a template. No Marketing menu instance
    /// is created, registered, opened, updated or destroyed.
    /// </summary>
    internal sealed class EnergyCampaignMenu : MonoBehaviour
    {
        private static readonly LocalisedString[] Names =
        {
            EnergyLocalization.Create("energy.campaign.hack.name"),
            EnergyLocalization.Create("energy.campaign.debug.name"),
            EnergyLocalization.Create("energy.campaign.climate.name")
        };

        private static readonly LocalisedString[] Descriptions =
        {
            EnergyLocalization.Create("energy.campaign.hack.description"),
            EnergyLocalization.Create("energy.campaign.debug.description"),
            EnergyLocalization.Create("energy.campaign.climate.description")
        };

        private static readonly LocalisedString DurationText = EnergyLocalization.Create(
            "energy.campaign.duration");
        private static readonly LocalisedString MonthsText = EnergyLocalization.Create(
            "energy.campaign.months");
        private static readonly LocalisedString CapacityText = EnergyLocalization.Create(
            "energy.campaign.capacity");
        private static readonly LocalisedString StartText = EnergyLocalization.Create(
            "energy.campaign.start");
        private static readonly LocalisedString ActiveText = EnergyLocalization.Create(
            "energy.campaign.active_button");
        private static readonly LocalisedString HeaderTitle = EnergyLocalization.Create(
            "energy.campaign.header");
        private static readonly LocalisedString HeaderAction = EnergyLocalization.Create(
            "energy.campaign.start_action");
        private static readonly LocalisedString ContractedPowerText = EnergyLocalization.Create(
            "energy.contract.power");
        private static readonly LocalisedString MonthlyCostText = EnergyLocalization.Create(
            "energy.contract.monthly_cost");

        private static EnergyCampaignMenu _instance;
        private readonly List<Choice> _choices = new List<Choice>();
        private Room _room;
        private EnergyCampaignKind _selected;
        private TMP_Text _description;
        private TMP_Text _duration;
        private TMP_Text _costLabel;
        private TMP_Text _cost;
        private Slider _slider;
        private Slider _contractSlider;
        private TMP_Text _contractPower;
        private TMP_Text _contractCost;
        private DynamicButton _launch;
        private Image _capacityFill;
        private Image _earnedFill;
        private RectTransform _capacityMarker;
        private int _months = 3;

        private sealed class Choice
        {
            internal EnergyCampaignKind Kind;
            internal Button Button;
            internal TMP_Text Name;
        }

        internal static void Open(Room room)
        {
            if (room?.Level?.HUD == null) return;
            if (_instance != null) _instance.Close();

            var template = FindMarketingTemplate(room.Level.HUD);
            if (template == null)
            {
                PowerGridPlugin.Log.LogError("No se encontró el prefab nativo que sirve como plantilla visual del menú de energía.");
                return;
            }

            var native = template.GetComponent<MarketingCampaignMenu>();
            if (native == null) return;
            var parent = FindDrawParent(room.Level.HUD, native);
            if (parent == null) return;

            var rootObject = new GameObject("UnderPressure_EnergyCampaignMenu", typeof(RectTransform), typeof(CanvasGroup));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            Stretch(root);
            _instance = rootObject.AddComponent<EnergyCampaignMenu>();
            _instance.Build(room, native, root);
        }

        private void Build(Room room, MarketingCampaignMenu native, RectTransform root)
        {
            _room = room;
            _selected = EnergyCampaignKind.HackPowerCompany;

            var blocker = NewImage(root, "Input blocker", Color.clear);
            Stretch(blocker.rectTransform);
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(Close);

            var sourceDescription = Field<TMP_Text>(native, "_campaignDescriptionText");
            var sourceDuration = Field<TMP_Text>(native, "_durationText");
            var sourceCostLabel = Field<TMP_Text>(native, "_costTextLabel");
            var sourceCost = Field<TMP_Text>(native, "_costText");
            var sourceSlider = Field<Slider>(native, "_durationSlider");
            var sourceLaunch = Field<DynamicButton>(native, "_launchButton");
            var sourceName = Field<TMP_Text>(native, "_campaignNameText");
            var itemPrefab = Field<GameObject>(native, "_listItemPrefab");

            var sourceDetailsPanel = CommonAncestor(sourceDescription?.transform, sourceDuration?.transform,
                sourceCost?.transform, sourceSlider?.transform) as RectTransform;
            var sourceControlsPanel = CommonAncestor(sourceDuration?.transform, sourceSlider?.transform,
                sourceCostLabel?.transform, sourceCost?.transform) as RectTransform;
            var sourcePanel = CommonAncestor(sourceDetailsPanel, sourceLaunch?.transform) as RectTransform;
            if (sourcePanel == null)
            {
                PowerGridPlugin.Log.LogError("La plantilla de Marketing no contiene el panel de campaña esperado.");
                Close();
                return;
            }

            var panelObject = Object.Instantiate(sourcePanel.gameObject, root, false);
            panelObject.name = "UnderPressure_EnergyCampaignPanel_Clone";
            RemoveMarketingBehaviours(panelObject);
            var panel = panelObject.transform as RectTransform;
            // Keep the approved single-column composition, but give the native
            // rear panel enough room to contain the launch button completely.
            SetRect(panel, new Vector2(0f, -38f), new Vector2(620f, 830f));
            DisableAutomaticLayout(panelObject);

            var rearImage = panel.GetComponent<Image>();
            if (rearImage != null) rearImage.enabled = false;

            // Marketing does not tint one giant image. Its MainPanel is composed
            // from three serialized layers: frosted interior, coloured interior
            // and an independent white outer border. Clone that exact structure
            // and recolour only the coloured interior.
            var nativeRearPanel = CloneNativeRearPanel(native, panel);

            var detailsPanel = CloneTransform(sourcePanel, panel, sourceDetailsPanel) as RectTransform;
            if (detailsPanel != null)
            {
                // This is the transparent frame that surrounds every control except
                // the launch button. Its lower edge sits just above that button.
                SetRect(detailsPanel, new Vector2(-10f, 2f), new Vector2(450f, 620f));
                var transparentFrame = detailsPanel.GetComponent<Image>();
                if (transparentFrame != null)
                {
                    var color = transparentFrame.color;
                    transparentFrame.color = new Color(color.r, color.g, color.b, 1f);
                    transparentFrame.type = Image.Type.Sliced;
                    transparentFrame.fillCenter = true;
                    transparentFrame.enabled = true;
                }

            }

            // Temporarily hide the additional oversized native frame. It is kept
            // in the hierarchy so it can be restored once the final layout is set.
            foreach (var image in panel.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image == rearImage ||
                    (nativeRearPanel != null && image.transform.IsChildOf(nativeRearPanel)) ||
                    image.transform == detailsPanel) continue;
                var rect = image.rectTransform;
                if (rect != null && rect.rect.width > 460f && rect.rect.height > 400f) image.enabled = false;
            }

            _description = CloneComponent(sourcePanel, panel, sourceDescription);
            _duration = CloneComponent(sourcePanel, panel, sourceDuration);
            _costLabel = CloneComponent(sourcePanel, panel, sourceCostLabel);
            _cost = CloneComponent(sourcePanel, panel, sourceCost);
            _slider = CloneComponent(sourcePanel, panel, sourceSlider);
            _launch = CloneComponent(sourcePanel, panel, sourceLaunch);
            var clonedName = CloneComponent(sourcePanel, panel, sourceName);

            var nameGroup = clonedName != null ? clonedName.transform.parent : null;
            if (nameGroup != null) nameGroup.gameObject.SetActive(false);

            if (itemPrefab != null)
            {
                for (var i = 0; i < 3; ++i)
                {
                    var itemObject = Object.Instantiate(itemPrefab, detailsPanel != null ? detailsPanel : panel, false);
                    itemObject.name = "UnderPressure_EnergyCampaignChoice_" + i;
                    itemObject.SetActive(true);
                    var nativeItem = itemObject.GetComponent<MarketingCampaignListItem>();
                    if (nativeItem == null) continue;
                    var kind = (EnergyCampaignKind)i;
                    nativeItem.Name.text = Names[i].Translation;
                    nativeItem.Button.onClick.RemoveAllListeners();
                    var buttonSfx = itemObject.GetComponent<ButtonSFX>();
                    if (buttonSfx != null) buttonSfx.UpdateListeners();
                    nativeItem.Button.onClick.AddListener(() => Select(kind));
                    _choices.Add(new Choice { Kind = kind, Button = nativeItem.Button, Name = nativeItem.Name });
                    foreach (var canvasGroup in itemObject.GetComponentsInChildren<CanvasGroup>(true))
                        canvasGroup.alpha = 1f;
                    foreach (var image in itemObject.GetComponentsInChildren<Image>(true))
                    {
                        var color = image.color;
                        image.color = new Color(color.r, color.g, color.b, 1f);
                    }
                    var colours = nativeItem.Button.colors;
                    colours.normalColor = Opaque(colours.normalColor);
                    colours.highlightedColor = Opaque(colours.highlightedColor);
                    colours.pressedColor = Opaque(colours.pressedColor);
                    colours.selectedColor = Opaque(colours.selectedColor);
                    colours.disabledColor = Opaque(colours.disabledColor);
                    nativeItem.Button.colors = colours;
                    Object.DestroyImmediate(nativeItem);
                    SetRect(itemObject.transform as RectTransform, new Vector2(0f, 273f - i * 59f), new Vector2(394f, 49f));
                }
            }

            var descriptionGroup = CloneTransform(sourcePanel, panel, sourceDescription?.transform.parent) as RectTransform;
            var controlsPanel = CloneTransform(sourcePanel, panel, sourceControlsPanel) as RectTransform;
            var durationGroup = CloneTransform(sourcePanel, panel,
                CommonAncestor(sourceDuration?.transform, sourceSlider?.transform)) as RectTransform;
            var costGroup = CloneTransform(sourcePanel, panel,
                CommonAncestor(sourceCostLabel?.transform, sourceCost?.transform)) as RectTransform;
            if (controlsPanel != null && controlsPanel != detailsPanel)
            {
                SetRect(controlsPanel, new Vector2(0f, -163f), new Vector2(430f, 250f));
                DisablePanelImages(controlsPanel);
            }
            PlaceDirectly(panel, descriptionGroup, new Vector2(0f, 69f), new Vector2(394f, 86f));
            SetSlicedPanel(descriptionGroup);
            var contractGroup = BuildContractGroup(panel, durationGroup);
            PlaceDirectly(panel, contractGroup, new Vector2(0f, -48f), new Vector2(390f, 116f));
            PlaceDirectly(panel, durationGroup, new Vector2(0f, -159f), new Vector2(390f, 106f));
            PlaceDirectly(panel, costGroup, new Vector2(0f, -258f), new Vector2(390f, 82f));
            DisablePanelImages(durationGroup);
            DisablePanelImages(costGroup);
            if (_launch != null) PlaceDirectly(panel, _launch.transform as RectTransform,
                new Vector2(0f, -356f), new Vector2(408f, 54f));

            BuildProgressCapacityBar(costGroup);

            if (_slider != null)
            {
                _slider.transform.localPosition += new Vector3(16f, 0f, 0f);
                _slider.onValueChanged.RemoveAllListeners();
                _slider.minValue = 1f;
                _slider.maxValue = 6f;
                _slider.wholeNumbers = true;
                _slider.value = 3f;
                _slider.onValueChanged.AddListener(value =>
                {
                    _months = Mathf.RoundToInt(value);
                    Refresh();
                });
                ConfigureSliderPoints(_slider, 6);
            }

            if (_contractSlider != null)
            {
                _contractSlider.onValueChanged.RemoveAllListeners();
                _contractSlider.minValue = 0f;
                _contractSlider.maxValue = 10f;
                _contractSlider.wholeNumbers = true;
                _contractSlider.value = PowerGridPrototype.GetSelectedContractedEnergy(_room.Level) / 1000f;
                _contractSlider.onValueChanged.AddListener(value =>
                {
                    PowerGridPrototype.RequestContractedEnergy(_room.Level, Mathf.RoundToInt(value) * 1000);
                    RefreshContract();
                });
                ConfigureSliderPoints(_contractSlider, 11);
            }

            if (_launch != null)
            {
                _launch.onPrimaryDown.RemoveAllListeners();
                _launch.onPrimaryDown.AddListener(Launch);
            }

            CloneNativeHeader(native, root);
            Refresh();
        }

        private void CloneNativeHeader(MarketingCampaignMenu native, RectTransform root)
        {
            var source = AccessTools.Field(typeof(MenuBase), "_closeMenuButton")?.GetValue(native) as DynamicButton;
            if (source == null) return;
            var templateRoot = native.transform;
            var headerSource = DirectChild(templateRoot, source.transform);
            if (headerSource == null || headerSource == templateRoot) headerSource = source.transform.parent;
            var cloneObject = Object.Instantiate(headerSource.gameObject, root, false);
            cloneObject.name = "UnderPressure_EnergyCampaignHeader_Clone";
            RemoveMarketingBehaviours(cloneObject);
            DisableAutomaticLayout(cloneObject);
            var headerRect = cloneObject.transform as RectTransform;
            if (headerRect != null)
            {
                headerRect.anchorMin = headerRect.anchorMax = new Vector2(0f, 1f);
                headerRect.pivot = new Vector2(0f, 1f);
                headerRect.anchoredPosition = new Vector2(0f, -36f);
                headerRect.sizeDelta = new Vector2(1240f,
                    (headerSource as RectTransform)?.rect.height ?? headerRect.rect.height);
                headerRect.localScale = Vector3.one;
            }
            var close = cloneObject.GetComponentInChildren<DynamicButton>(true);
            if (close == null) return;
            close.onPrimaryDown.RemoveAllListeners();
            close.onPrimaryDown.AddListener(Close);
            var texts = cloneObject.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text != null && !text.transform.IsChildOf(close.transform))
                .OrderByDescending(text => text.fontSize).ToArray();
            if (texts.Length > 0)
            {
                texts[0].text = HeaderTitle.Translation;
                texts[0].rectTransform.anchoredPosition += new Vector2(-120f, 0f);
            }
            if (texts.Length > 1) texts[1].text = HeaderAction.Translation;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void Select(EnergyCampaignKind kind)
        {
            _selected = kind;
            Refresh();
        }

        private void Launch()
        {
            var component = EnergyCampaignSystem.GetController(_room);
            if (component == null)
            {
                PowerGridPlugin.Log.LogError("No se encontró el componente de campaña del transformador.");
                return;
            }
            component.StartCampaign(_selected, _months);
            Refresh();
            Close();
        }

        private void Refresh()
        {
            if (_room == null) return;
            var state = EnergyCampaignSystem.Get(_room);
            if (_description != null) _description.text = Descriptions[(int)_selected].Translation;
            if (_duration != null) _duration.text = DurationText.Translation + ": " + _months + " " + MonthsText.Translation;
            var capacity = EnergyCampaignSystem.GetMaximumProgress(_room, _selected);
            var progress = state.Progress[(int)_selected];
            if (_costLabel != null) _costLabel.text = CapacityText.Translation + ": " + Mathf.RoundToInt(capacity) + "%";
            if (_cost != null) _cost.gameObject.SetActive(false);
            SetBarAmount(_capacityFill, capacity / 100f);
            SetBarAmount(_earnedFill, Mathf.Min(progress, capacity) / 100f);
            if (_capacityMarker != null)
                _capacityMarker.anchorMin = _capacityMarker.anchorMax = new Vector2(Mathf.Clamp01(capacity / 100f), 0.5f);
            if (_launch != null) _launch.SetTMPText(state.Active == _selected ? ActiveText.Translation : StartText.Translation);
            RefreshContract();
            foreach (var choice in _choices)
            {
                var selected = choice.Kind == _selected;
                GameObjectUtils.SetInteractable(choice.Button, !selected);
                if (choice.Name != null) choice.Name.color = selected ? Color.black : Color.white;
            }
        }

        private RectTransform BuildContractGroup(RectTransform panel, RectTransform durationGroup)
        {
            if (panel == null || durationGroup == null) return null;
            var contractObject = Object.Instantiate(durationGroup.gameObject, panel, false);
            contractObject.name = "UnderPressure_ContractedPower";
            var contractGroup = contractObject.transform as RectTransform;
            _contractSlider = CloneComponent(durationGroup, contractGroup, _slider);
            _contractPower = CloneComponent(durationGroup, contractGroup, _duration);
            if (_contractPower != null)
            {
                _contractPower.gameObject.name = "Contracted power";
                SetRect(_contractPower.rectTransform, new Vector2(0f, 27f), new Vector2(390f, 34f));
            }
            if (_contractSlider != null)
            {
                _contractSlider.gameObject.name = "Contracted power slider";
                _contractSlider.transform.localPosition += new Vector3(16f, -10f, 0f);
            }
            if (_contractPower != null)
            {
                var costObject = Object.Instantiate(_contractPower.gameObject, contractGroup, false);
                costObject.name = "Monthly contracted power cost";
                _contractCost = costObject.GetComponent<TMP_Text>();
                if (_contractCost != null)
                {
                    _contractCost.fontSize = Mathf.Max(1f, _contractPower.fontSize * 0.82f);
                    SetRect(_contractCost.rectTransform, new Vector2(0f, 4f), new Vector2(390f, 28f));
                }
            }
            DisablePanelImages(contractGroup);
            return contractGroup;
        }

        private void RefreshContract()
        {
            if (_room?.Level == null) return;
            var power = _contractSlider == null
                ? PowerGridPrototype.GetSelectedContractedEnergy(_room.Level)
                : Mathf.RoundToInt(_contractSlider.value) * 1000;
            var cost = PowerGridPrototype.GetMonthlyCostForContractedEnergy(power);
            if (_contractPower != null)
                _contractPower.text = ContractedPowerText.Translation + ": " +
                                      power.ToString(CultureInfo.InvariantCulture);
            if (_contractCost != null)
                _contractCost.text = MonthlyCostText.Translation + ": " +
                                     cost.ToString(CultureInfo.InvariantCulture) + " $";
        }

        private static void ConfigureSliderPoints(Slider slider, int pointCount)
        {
            if (slider == null || pointCount < 2) return;
            var markers = slider.transform.parent.GetComponentsInChildren<Image>(true)
                .Where(image => image != null && image.sprite != null &&
                                image.rectTransform.rect.width <= 24f && image.rectTransform.rect.height <= 24f &&
                                (slider.handleRect == null || !image.transform.IsChildOf(slider.handleRect)))
                .GroupBy(image => new
                {
                    Parent = image.transform.parent,
                    Sprite = image.sprite,
                    Y = Mathf.RoundToInt(image.rectTransform.anchoredPosition.y)
                })
                .Where(group => group.Count() >= 5)
                .ToArray();
            foreach (var group in markers)
            {
                var points = group.OrderBy(image => image.rectTransform.anchoredPosition.x).ToList();
                var minimumX = points.First().rectTransform.anchoredPosition.x;
                var maximumX = points.Last().rectTransform.anchoredPosition.x;
                while (points.Count < pointCount)
                {
                    var clone = Object.Instantiate(points[0], points[0].transform.parent, false);
                    clone.name = points[0].name + " " + points.Count;
                    points.Add(clone);
                }
                for (var index = 0; index < points.Count; ++index)
                {
                    var visible = index < pointCount;
                    points[index].gameObject.SetActive(visible);
                    if (!visible) continue;
                    var position = points[index].rectTransform.anchoredPosition;
                    position.x = Mathf.Lerp(minimumX, maximumX, index / (float)(pointCount - 1));
                    points[index].rectTransform.anchoredPosition = position;
                }
            }
        }

        private void BuildProgressCapacityBar(RectTransform group)
        {
            if (group == null) return;
            foreach (var image in group.GetComponentsInChildren<Image>(true)) image.enabled = false;
            var track = NewImage(group, "Campaign capacity track", new Color(0.40f, 0.42f, 0.42f, 1f));
            SetRect(track.rectTransform, new Vector2(0f, -18f), new Vector2(330f, 32f));
            ApplyRoundedBarStyle(track, _slider);
            _capacityFill = NewImage(track.rectTransform, "Maximum capacity", new Color(0.95f, 0.48f, 0.10f, 1f));
            _earnedFill = NewImage(track.rectTransform, "Earned progress", new Color(0.20f, 0.72f, 0.30f, 1f));
            ApplyRoundedBarStyle(_capacityFill, _slider);
            ApplyRoundedBarStyle(_earnedFill, _slider);
            PrepareBarFill(_capacityFill.rectTransform);
            PrepareBarFill(_earnedFill.rectTransform);
            var marker = NewImage(track.rectTransform, "Capacity limit", new Color(1f, 0.96f, 0.82f, 1f));
            marker.raycastTarget = false;
            _capacityMarker = marker.rectTransform;
            _capacityMarker.pivot = new Vector2(0.5f, 0.5f);
            _capacityMarker.sizeDelta = new Vector2(4f, 30f);
            _capacityMarker.anchoredPosition = Vector2.zero;
        }

        private static void PrepareBarFill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetBarAmount(Image image, float amount)
        {
            if (image == null) return;
            image.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        }

        private static void ApplyRoundedBarStyle(Image target, Slider source)
        {
            if (target == null || source == null) return;
            var candidates = source.GetComponentsInChildren<Image>(true)
                .Where(image => image != null && image.sprite != null)
                .OrderByDescending(image => image.rectTransform.rect.width / Mathf.Max(1f, image.rectTransform.rect.height));
            var template = candidates.FirstOrDefault();
            if (template == null) return;
            target.sprite = template.sprite;
            target.type = Image.Type.Sliced;
            target.material = template.material;
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        private static RectTransform CloneNativeRearPanel(MarketingCampaignMenu native, RectTransform parent)
        {
            var sourceMainPanel = native.transform.Find("MainPanel") as RectTransform;
            if (sourceMainPanel == null) return null;

            var rearObject = new GameObject("Energy native rear panel", typeof(RectTransform));
            var rear = rearObject.GetComponent<RectTransform>();
            rear.SetParent(parent, false);
            SetRect(rear, new Vector2(0f, -40f), new Vector2(520f, 760f));
            rear.SetAsFirstSibling();

            var layerNames = new[] { "Background Inner Frosted", "Background Inner", "Background Outer" };
            foreach (var layerName in layerNames)
            {
                var sourceLayer = sourceMainPanel.Find(layerName);
                if (sourceLayer == null) continue;
                var sourceImage = sourceLayer.GetComponent<Image>();
                if (sourceImage == null || sourceImage.sprite == null) continue;
                var color = sourceImage.color;
                if (layerName == "Background Inner")
                    color = new Color(1f, 0.62f, 0.10f, color.a);
                var image = NewImage(rear, layerName, color);
                image.sprite = sourceImage.sprite;
                image.material = sourceImage.material;
                image.type = Image.Type.Sliced;
                image.fillCenter = true;
                image.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
                image.raycastTarget = false;
                Stretch(image.rectTransform);
            }
            return rear;
        }

        private static void SetSlicedPanel(RectTransform panel)
        {
            if (panel == null) return;
            var image = panel.GetComponent<Image>();
            if (image == null || image.sprite == null) return;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
        }

        private static void DisablePanelImages(RectTransform panel)
        {
            if (panel == null) return;
            var image = panel.GetComponent<Image>();
            if (image != null) image.enabled = false;
        }

        private void Close()
        {
            if (_instance == this) _instance = null;
            Object.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private static GameObject FindMarketingTemplate(HUD hud)
        {
            var method = typeof(HUD).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.Name == "FindMenuPrefab" && candidate.IsGenericMethodDefinition &&
                                             candidate.GetParameters().Length == 0);
            return method?.MakeGenericMethod(typeof(MarketingCampaignMenu)).Invoke(hud, null) as GameObject;
        }

        private static Transform FindDrawParent(HUD hud, MarketingCampaignMenu menu)
        {
            var field = AccessTools.Field(typeof(HUD), "_drawOrderedMenuTransforms");
            var transforms = field?.GetValue(hud) as List<Transform>;
            var index = (int)menu.DrawOrderSlot;
            return transforms != null && index >= 0 && index < transforms.Count ? transforms[index] : null;
        }

        private static T Field<T>(MarketingCampaignMenu menu, string name) where T : class =>
            AccessTools.Field(typeof(MarketingCampaignMenu), name)?.GetValue(menu) as T;

        private static T CloneComponent<T>(Transform sourceRoot, Transform cloneRoot, T source) where T : Component
        {
            if (source == null) return null;
            var relative = RelativePath(sourceRoot, source.transform);
            var target = string.IsNullOrEmpty(relative) ? cloneRoot : cloneRoot.Find(relative);
            return target != null ? target.GetComponent<T>() : null;
        }

        private static Transform CloneTransform(Transform sourceRoot, Transform cloneRoot, Transform source)
        {
            if (source == null) return null;
            var relative = RelativePath(sourceRoot, source);
            return string.IsNullOrEmpty(relative) ? cloneRoot : cloneRoot.Find(relative);
        }

        private static string RelativePath(Transform root, Transform child)
        {
            if (root == child) return string.Empty;
            var names = new Stack<string>();
            var current = child;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", names.ToArray()) : null;
        }

        private static void RemoveMarketingBehaviours(GameObject root)
        {
            foreach (var menu in root.GetComponentsInChildren<MarketingCampaignMenu>(true)) Object.DestroyImmediate(menu);
            foreach (var item in root.GetComponentsInChildren<MarketingCampaignListItem>(true)) Object.DestroyImmediate(item);
        }

        private static void DisableAutomaticLayout(GameObject root)
        {
            foreach (var layout in root.GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
            foreach (var fitter in root.GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
        }

        private static Transform CommonAncestor(params Transform[] transforms)
        {
            Transform candidate = null;
            foreach (var transform in transforms)
            {
                if (transform == null) continue;
                if (candidate == null) { candidate = transform; continue; }
                while (candidate != null && transform != candidate && !transform.IsChildOf(candidate)) candidate = candidate.parent;
            }
            return candidate;
        }

        private static Transform DirectChild(Transform root, Transform member)
        {
            if (root == null || member == null) return null;
            if (root == member) return member;
            var current = member;
            while (current.parent != null && current.parent != root) current = current.parent;
            return current.parent == root ? current : null;
        }

        private static void PlaceGroup(RectTransform root, Transform member, Vector2 position, Vector2 size)
        {
            var group = DirectChild(root, member) as RectTransform;
            if (group != null) SetRect(group, position, size);
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceDirectly(RectTransform parent, RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.SetParent(parent, false);
            rect.gameObject.SetActive(true);
            SetRect(rect, position, size);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
