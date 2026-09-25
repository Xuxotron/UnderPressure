using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.PowerGrid
{
    internal sealed class EnergyCampaignController
    {
        private readonly RoomItem _transformer;
        private readonly Level _level;
        internal event Action ProgressChanged;
        internal event Action<bool> CampaignEnded;
        internal EnergyCampaignState State => _transformer?.OwningRoom == null ? null : EnergyCampaignSystem.Get(_transformer.OwningRoom);
        internal int DurationInDays => State == null ? 1 : Mathf.Max(1, Mathf.RoundToInt(State.DurationMonths * 30.42f));
        internal int TimeRemainingDays => State?.TimeRemainingDays ?? 0;
        internal float ElapsedRatio => 1f - Mathf.Clamp01((float)TimeRemainingDays / DurationInDays);

        internal EnergyCampaignController(RoomItem transformer)
        {
            _transformer = transformer;
            _level = transformer?.Level;
            if (_level?.TimelineManager != null) _level.TimelineManager.OnTimelineUpdated += OnTimelineUpdated;
            if (State != null && State.Active != EnergyCampaignKind.None) ShowStatusIcon();
        }

        internal void Destroy()
        {
            if (_level?.TimelineManager != null) _level.TimelineManager.OnTimelineUpdated -= OnTimelineUpdated;
        }

        internal void StartCampaign(EnergyCampaignKind kind, int months)
        {
            var state = State;
            if (kind == EnergyCampaignKind.None || state == null) return;
            state.Active = kind;
            state.DurationMonths = Mathf.Clamp(months, 3, 12);
            state.TimeRemainingDays = Mathf.Max(1, Mathf.RoundToInt(state.DurationMonths * 30.42f));
            ShowStatusIcon();
            ProgressChanged?.Invoke();
        }

        internal void EndCampaign(bool cancelled)
        {
            var state = State;
            if (state == null || state.Active == EnergyCampaignKind.None) return;
            state.Active = EnergyCampaignKind.None;
            state.TimeRemainingDays = 0;
            CampaignEnded?.Invoke(cancelled);
        }

        private void OnTimelineUpdated(int hour, int day, int month)
        {
            var state = State;
            var room = _transformer?.OwningRoom;
            if (state == null || state.Active == EnergyCampaignKind.None || room?.StaffWorkingInRoom == null || room.StaffWorkingInRoom.Count == 0) return;
            var index = (int)state.Active;
            var maximum = EnergyCampaignSystem.GetMaximumProgress(room, state.Active);
            state.TimeRemainingDays = Mathf.Max(0, state.TimeRemainingDays - 1);
            state.Progress[index] = Mathf.Min(maximum, state.Progress[index] + 1f);
            if (state.TimeRemainingDays == 0 || state.Progress[index] >= maximum) EndCampaign(false);
            else ProgressChanged?.Invoke();
        }

        private void ShowStatusIcon()
        {
            if (_transformer != null && _level?.StatusIconManager != null)
                _level.StatusIconManager.ShowStatusIcon(_transformer, EnergyCampaignRuntime.StatusIconType);
        }
    }

    internal static class EnergyCampaignRuntime
    {
        internal static readonly StatusIcon.Type StatusIconType = (StatusIcon.Type)43;
        private static readonly Dictionary<RoomItem, EnergyCampaignController> Controllers = new Dictionary<RoomItem, EnergyCampaignController>();
        private static GameObject _hoverPrefab;
        private static GameObject _selectPrefab;
        internal static GameObject HoverPrefab => _hoverPrefab;
        internal static GameObject SelectPrefab => _selectPrefab;

        internal static EnergyCampaignController GetController(RoomItem transformer)
        {
            if (transformer == null || !EnergyRoomItems.IsTransformer(transformer)) return null;
            if (!Controllers.TryGetValue(transformer, out var controller))
            {
                controller = new EnergyCampaignController(transformer);
                Controllers.Add(transformer, controller);
            }
            return controller;
        }

        internal static void ReleaseController(RoomItem transformer)
        {
            if (transformer == null || !Controllers.TryGetValue(transformer, out var controller)) return;
            Controllers.Remove(transformer);
            controller.Destroy();
        }

        internal static void ConfigureTransformerDefinition(RoomItemDefinition transformer, RoomItemDefinition reference)
        {
            if (transformer == null || reference == null) return;
            if (_hoverPrefab == null) _hoverPrefab = BuildHoverPrefab(reference.HoverMenuPrefab);
            if (_selectPrefab == null) _selectPrefab = BuildSelectPrefab(reference.SelectMenuPrefab);
            if (_hoverPrefab != null) Set(transformer, "_hoverMenuPrefab", _hoverPrefab);
            if (_selectPrefab != null) Set(transformer, "_selectMenuPrefab", _selectPrefab);
            Set(transformer, "_showStatusIcon", true);
        }

        private static GameObject BuildHoverPrefab(GameObject source)
        {
            if (source == null || source.GetComponent<HoverMenuMarketing>() == null)
                throw new InvalidOperationException("La plantilla hover no contiene HoverMenuMarketing.");
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = "Under Pressure Energy Campaign Hover";
            clone.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(clone);
            var native = clone.GetComponent<HoverMenuMarketing>();
            if (native == null) return null;
            var replacement = clone.AddComponent<HoverMenuEnergyCampaign>();
            CopyBaseFields(native, replacement);
            replacement.Configure(Get<TMP_Text>(native, "_campaignName"), Get<ProgressBarMaskable>(native, "_progressBar"));
            UnityEngine.Object.DestroyImmediate(native);
            clone.SetActive(false);
            return clone;
        }

        private static GameObject BuildSelectPrefab(GameObject source)
        {
            if (source == null || source.GetComponent<SelectMenuMarketing>() == null)
                throw new InvalidOperationException("La plantilla select no contiene SelectMenuMarketing.");
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = "Under Pressure Energy Campaign Selection";
            clone.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(clone);
            var native = clone.GetComponent<SelectMenuMarketing>();
            if (native == null) return null;
            var replacement = clone.AddComponent<SelectMenuEnergyCampaign>();
            CopyBaseFields(native, replacement);
            replacement.Configure(Get<TMP_Text>(native, "_campaignName"), Get<ProgressBarMaskable>(native, "_progressBar"), Get<DynamicButton>(native, "_cancelButton"));
            UnityEngine.Object.DestroyImmediate(native);
            clone.SetActive(false);
            return clone;
        }

        internal static HoverMenuEnergyCampaign CreateHoverMenu(HUD hud)
        {
            if (hud == null || _hoverPrefab == null) return null;
            _hoverPrefab.SetActive(true);
            try
            {
                return hud.CreateMenu<HoverMenuEnergyCampaign>(_hoverPrefab);
            }
            finally
            {
                _hoverPrefab.SetActive(false);
            }
        }

        internal static SelectMenuEnergyCampaign CreateSelectMenu(HUD hud)
        {
            if (hud == null || _selectPrefab == null) return null;
            _selectPrefab.SetActive(true);
            try
            {
                return hud.CreateMenu<SelectMenuEnergyCampaign>(_selectPrefab);
            }
            finally
            {
                _selectPrefab.SetActive(false);
            }
        }

        internal static string CampaignName(EnergyCampaignKind kind)
        {
            switch (kind)
            {
                case EnergyCampaignKind.HackPowerCompany: return "Hackear compañía eléctrica";
                case EnergyCampaignKind.DebugCodeWithAi: return "Depurar código con IA";
                case EnergyCampaignKind.DenyClimateChange: return "Negar el cambio climático";
                default: return "Iniciar campaña de energía";
            }
        }

        internal static string ProgressText(EnergyCampaignController controller)
        {
            if (controller?.State == null) return string.Empty;
            var duration = controller.State.DurationMonths;
            var elapsedDays = Mathf.Max(0, controller.DurationInDays - controller.TimeRemainingDays);
            return Mathf.Clamp(Mathf.FloorToInt(elapsedDays / 30.42f), 0, duration) + " / " + duration + " meses";
        }

        internal static Sprite FindEnergyIcon()
        {
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && string.Equals(sprite.name, "T_UI_D7_ER_Map_Battery_Icon", StringComparison.OrdinalIgnoreCase)) return sprite;
            return null;
        }

        private static T Get<T>(object source, string name) where T : class => AccessTools.Field(source.GetType(), name)?.GetValue(source) as T;

        private static void CopyBaseFields(object source, object destination)
        {
            // Copy UI configuration only. Unity's native object pointer and runtime
            // menu state must belong to the replacement component itself.
            for (var type = source.GetType().BaseType; type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized ||
                        (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false))) continue;
                    field.SetValue(destination, field.GetValue(source));
                }
        }

        private static void Set(object target, string name, object value) => AccessTools.Field(target.GetType(), name)?.SetValue(target, value);
    }

    [HarmonyPatch(typeof(RoomItem), "Destroy")]
    internal static class EnergyCampaignControllerCleanupPatch
    {
        private static void Prefix(RoomItem __instance) => EnergyCampaignRuntime.ReleaseController(__instance);
    }

    [HarmonyPatch(typeof(BuildEvents), "CursorSelectObject")]
    internal static class EnergyTransformerSelectionPatch
    {
        private static readonly FieldInfo HudField = AccessTools.Field(typeof(BuildEvents), "_hud");
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(BuildEvents), "_level");

        private static bool Prefix(BuildEvents __instance, ICursorSelectable __0)
        {
            var transformer = __0 as RoomItem;
            if (!EnergyRoomItems.IsTransformer(transformer)) return true;

            var activeMenu = transformer.GetActiveMenu();
            if (activeMenu is SelectMenuEnergyCampaign) return false;
            if (activeMenu != null) activeMenu.CloseMenu();

            var controller = EnergyCampaignRuntime.GetController(transformer);
            if (controller?.State == null || controller.State.Active == EnergyCampaignKind.None)
            {
                EnergyCampaignMenu.Open(transformer.OwningRoom);
                return false;
            }

            var hud = HudField.GetValue(__instance) as HUD;
            var level = LevelField.GetValue(__instance) as Level;
            var menu = EnergyCampaignRuntime.CreateSelectMenu(hud);
            menu?.Setup(transformer, level);
            return false;
        }
    }

    [HarmonyPatch(typeof(BuildEvents), "CursorHoverStart")]
    internal static class EnergyTransformerHoverPatch
    {
        private static readonly FieldInfo HudField = AccessTools.Field(typeof(BuildEvents), "_hud");
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(BuildEvents), "_level");

        private static bool Prefix(BuildEvents __instance, ICursorSelectable __0)
        {
            var transformer = __0 as RoomItem;
            if (!EnergyRoomItems.IsTransformer(transformer)) return true;

            var activeMenu = transformer.GetActiveMenu();
            if (activeMenu is SelectMenuEnergyCampaign) return false;
            if (activeMenu != null) activeMenu.CloseMenu();

            var controller = EnergyCampaignRuntime.GetController(transformer);
            if (controller?.State == null || controller.State.Active == EnergyCampaignKind.None)
                return false;

            var hud = HudField.GetValue(__instance) as HUD;
            var level = LevelField.GetValue(__instance) as Level;
            var menu = EnergyCampaignRuntime.CreateHoverMenu(hud);
            menu?.Setup(transformer, level);
            return false;
        }
    }

    [HarmonyPatch(typeof(StatusIconMarketingCampaign), "Update")]
    internal static class EnergyCampaignStatusIconUpdatePatch
    {
        private static readonly FieldInfo Item = AccessTools.Field(typeof(StatusIconMarketingCampaign), "_item");
        private static readonly FieldInfo Image = AccessTools.Field(typeof(StatusIconMarketingCampaign), "_image");
        private static readonly FieldInfo Progress = AccessTools.Field(typeof(StatusIconMarketingCampaign), "_progressBar");
        private static bool Prefix(StatusIconMarketingCampaign __instance)
        {
            var transformer = Item?.GetValue(__instance) as RoomItem;
            if (!EnergyRoomItems.IsTransformer(transformer)) return true;
            var controller = EnergyCampaignRuntime.GetController(transformer);
            var image = Image?.GetValue(__instance) as Image;
            var progress = Progress?.GetValue(__instance) as ProgressBar;
            var icon = EnergyCampaignRuntime.FindEnergyIcon();
            if (image != null && icon != null) image.sprite = icon;
            if (progress != null && controller != null) progress.Progress = controller.ElapsedRatio;
            return false;
        }
    }

    [HarmonyPatch(typeof(StatusIconMarketingCampaign), "HasTimedOut")]
    internal static class EnergyCampaignStatusIconTimeoutPatch
    {
        private static readonly FieldInfo Item = AccessTools.Field(typeof(StatusIconMarketingCampaign), "_item");
        private static bool Prefix(StatusIconMarketingCampaign __instance, ref bool __result)
        {
            var transformer = Item?.GetValue(__instance) as RoomItem;
            if (!EnergyRoomItems.IsTransformer(transformer)) return true;
            var controller = EnergyCampaignRuntime.GetController(transformer);
            __result = controller == null || controller.State == null || controller.State.Active == EnergyCampaignKind.None;
            return false;
        }
    }

    internal sealed class HoverMenuEnergyCampaign : HoverMenuRoomItem
    {
        [SerializeField] private TMP_Text _campaignName;
        [SerializeField] private ProgressBarMaskable _progressBar;
        private EnergyCampaignController _campaign;
        internal void Configure(TMP_Text name, ProgressBarMaskable progress) { _campaignName = name; _progressBar = progress; }
        public override void Setup(RoomItem item, Level level)
        {
            base.Setup(item, level);
            _campaign = EnergyCampaignRuntime.GetController(item);
            if (_campaign == null || _campaign.State.Active == EnergyCampaignKind.None)
            {
                if (_campaignName != null) _campaignName.text = "Iniciar campaña de energía";
                if (_progressBar != null) _progressBar.gameObject.SetActive(false);
                return;
            }
            _campaign.ProgressChanged += Refresh;
            _campaign.CampaignEnded += OnEnded;
            Refresh();
        }
        public override void Destroy() { Unsubscribe(); base.Destroy(); }
        private void Refresh()
        {
            if (_campaignName != null) _campaignName.text = EnergyCampaignRuntime.CampaignName(_campaign.State.Active);
            if (_progressBar == null) return;
            _progressBar.gameObject.SetActive(true);
            _progressBar.LabelText = EnergyCampaignRuntime.ProgressText(_campaign);
            _progressBar.Progress = _campaign.ElapsedRatio;
        }
        private void OnEnded(bool cancelled) => CloseMenu();
        private void Unsubscribe() { if (_campaign == null) return; _campaign.ProgressChanged -= Refresh; _campaign.CampaignEnded -= OnEnded; }
    }

    internal sealed class SelectMenuEnergyCampaign : SelectMenuRoomItem
    {
        [SerializeField] private TMP_Text _campaignName;
        [SerializeField] private ProgressBarMaskable _progressBar;
        [SerializeField] private DynamicButton _cancelButton;
        private EnergyCampaignController _campaign;
        internal void Configure(TMP_Text name, ProgressBarMaskable progress, DynamicButton cancel) { _campaignName = name; _progressBar = progress; _cancelButton = cancel; }
        public override void Setup(RoomItem item, Level level)
        {
            base.Setup(item, level);
            _campaign = EnergyCampaignRuntime.GetController(item);
            if (_campaign == null || _campaign.State.Active == EnergyCampaignKind.None)
            {
                HUD.DestroyMenu(this);
                EnergyCampaignMenu.Open(item.OwningRoom);
                return;
            }
            if (_cancelButton != null) { _cancelButton.onPrimaryDown.RemoveAllListeners(); _cancelButton.onPrimaryDown.AddListener(Cancel); }
            _campaign.ProgressChanged += Refresh;
            _campaign.CampaignEnded += OnEnded;
            Refresh();
        }
        public override void Destroy() { Unsubscribe(); base.Destroy(); }
        private void Refresh()
        {
            if (_campaignName != null) _campaignName.text = EnergyCampaignRuntime.CampaignName(_campaign.State.Active);
            if (_progressBar == null) return;
            _progressBar.LabelText = EnergyCampaignRuntime.ProgressText(_campaign);
            _progressBar.Progress = _campaign.ElapsedRatio;
        }
        private void Cancel() => _campaign?.EndCampaign(true);
        private void OnEnded(bool cancelled) => CloseMenu();
        private void Unsubscribe() { if (_campaign == null) return; _campaign.ProgressChanged -= Refresh; _campaign.CampaignEnded -= OnEnded; }
    }
}
