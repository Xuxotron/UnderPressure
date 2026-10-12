// Updated: 2026-10-12
// Purpose: Enable/disable installed DLC from the original main-menu icons without uninstalling it.
// Changes apply to newly loaded content; existing save objects are never deleted.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnderPressure.LoaderRuntime.Configuration;
using FullInspector;
using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    internal static class DLCControl
    {
        private static readonly Dictionary<uint, ConfigEntry<bool>> DisabledEntries =
            new Dictionary<uint, ConfigEntry<bool>>();

        private static readonly System.Reflection.FieldInfo DefinitionField =
            AccessTools.Field(typeof(DLCBuyButton), "_dlcDefinition");
        private static readonly System.Reflection.FieldInfo ButtonField =
            AccessTools.Field(typeof(DLCBuyButton), "_button");
        private static readonly System.Reflection.FieldInfo ImageField =
            AccessTools.Field(typeof(DLCBuyButton), "_dlcImage");
        private static readonly System.Reflection.FieldInfo InstalledField =
            AccessTools.Field(typeof(DLCBuyButton), "_isInstalled");

        private static ConfigEntry<bool> Entry(uint appId)
        {
            if (appId == 0 || UnderPressurePlugin.ModConfig == null) return null;
            if (DisabledEntries.TryGetValue(appId, out var entry)) return entry;
            entry = UnderPressurePlugin.ModConfig.Bind(
                "DLC deshabilitados",
                appId.ToString(CultureInfo.InvariantCulture),
                false,
                new ConfigDescription("Deshabilitar el DLC instalado con AppID " + appId +
                                      " para probar Under Pressure sin su contenido."));
            DisabledEntries.Add(appId, entry);
            return entry;
        }

        internal static bool IsDisabled(uint appId)
        {
            var entry = Entry(appId);
            return UnderPressurePlugin.IsModEnabled && entry != null && entry.Value;
        }

        internal static bool IsDisabled(DLCItemDefinition definition) =>
            definition != null && IsDisabled(definition.AppID);

        private static DLCItemDefinition Definition(DLCBuyButton owner)
        {
            if (owner == null || DefinitionField == null) return null;
            // The generated wrapper inherits SharedInstance<DLCItemDefinition>.
            // Instance is a public FIELD, not a property.
            var wrapper = DefinitionField.GetValue(owner) as SharedInstance<DLCItemDefinition>;
            return wrapper == null ? null : wrapper.Instance;
        }

        private static bool IsPhysicallyInstalled(DLCItemDefinition definition)
        {
            return definition != null && OSManager.IsInitialised() &&
                   OSManager.IsDlcInstalled(definition.AppID);
        }

        internal static void RefreshButton(DLCBuyButton owner)
        {
            var definition = Definition(owner);
            if (definition == null) return;

            var installed = IsPhysicallyInstalled(definition);
            var disabled = installed && IsDisabled(definition);
            var button = ButtonField?.GetValue(owner) as DynamicButton;
            var image = ImageField?.GetValue(owner) as Image;

            // The native Setup disables click handling on purchased DLC icons.
            // Enable it only for physically installed DLCs so they become switches.
            if (button != null) button.enabled = true;

            if (image != null)
            {
                image.overrideSprite = installed ? definition.Icon : definition.NotOwnedIcon;
                image.color = disabled
                    ? new Color(0.40f, 0.40f, 0.40f, 1f)
                    : Color.white;
            }

            // Keep the native installed-state tooltip rather than a purchase prompt.
            InstalledField?.SetValue(owner, installed);
        }

        internal static bool HandleClick(DLCBuyButton owner)
        {
            var definition = Definition(owner);
            if (!IsPhysicallyInstalled(definition)) return true;

            var entry = Entry(definition.AppID);
            if (entry == null) return false;
            entry.Value = !entry.Value;
            RefreshButton(owner);
            // Update the game's cached installed-DLC list without touching Steam.
            var screen = UnityEngine.Object.FindObjectOfType<OpeningScreen>();
            var app = screen == null ? null :
                AccessTools.Field(typeof(OpeningScreen), "_app")?.GetValue(screen) as App;
            app?.DLCManager?.RevalidatePurchasedDLC();
            UnderPressurePlugin.Log?.LogInfo(
                "DLC " + definition.Name.Translation + " (" + definition.AppID + "): " +
                (entry.Value ? "deshabilitado" : "habilitado") +
                ". Se aplicara en las proximas cargas de contenido.");
            return false; // Never open the store for an already installed DLC.
        }

        internal static void UpdateTooltip(DLCBuyButton owner, Tooltip tooltip)
        {
            if (tooltip == null) return;
            var definition = Definition(owner);
            if (!IsPhysicallyInstalled(definition) || !IsDisabled(definition)) return;

            tooltip.Text = "<size=125%>" + definition.Name.Translation + "</size>\n" +
                           "<color=#A7A7A7><smallcaps><size=115%>Deshabilitado</size></smallcaps></color>\n" +
                           "DLC instalado, pero su contenido esta desactivado por Under Pressure. " +
                           "Pulsa el icono para volver a habilitarlo.";
        }

        internal static bool IsContentDisabled(RoomItemDefinition definition)
        {
            var dependency = definition?.DlcPackRequired;
            return dependency != null && IsDisabled(dependency.Instance);
        }

        internal static bool IsContentDisabled(RoomDefinition definition)
        {
            var dependency = definition?.DlcPackRequired;
            return dependency != null && IsDisabled(dependency.Instance);
        }
    }

    // DLCUtils is the game's central DLC-availability check. The Steam state stays untouched.
    [HarmonyPatch(typeof(DLCUtils), nameof(DLCUtils.IsDLCInstalled),
        new[] { typeof(DLCItemDefinition) })]
    internal static class DLCControlInstalledDefinitionPatch
    {
        private static void Postfix(DLCItemDefinition __0, ref bool __result)
        {
            if (__result && DLCControl.IsDisabled(__0)) __result = false;
        }
    }

    [HarmonyPatch(typeof(DLCUtils), nameof(DLCUtils.IsDLCInstalled),
        new[] { typeof(uint) })]
    internal static class DLCControlInstalledIdPatch
    {
        private static void Postfix(uint __0, ref bool __result)
        {
            if (__result && DLCControl.IsDisabled(__0)) __result = false;
        }
    }

    [HarmonyPatch(typeof(DLCUtils), nameof(DLCUtils.IsDLCOwned))]
    internal static class DLCControlOwnershipPatch
    {
        private static void Postfix(DLCItemDefinition __0, ref bool __result)
        {
            if (__result && DLCControl.IsDisabled(__0)) __result = false;
        }
    }

    // Guard against catalogue paths which add definitions after SetupAvailableItems.
    [HarmonyPatch(typeof(WorldState), "OnAddRoomItemDefinition")]
    internal static class DLCControlAddItemPatch
    {
        private static bool Prefix(RoomItemDefinition definition) =>
            !DLCControl.IsContentDisabled(definition);
    }

    [HarmonyPatch(typeof(WorldState), "OnAddRoomDefinition")]
    internal static class DLCControlAddRoomPatch
    {
        private static bool Prefix(RoomDefinition definition) =>
            !DLCControl.IsContentDisabled(definition);
    }

    [HarmonyPatch(typeof(DLCBuyButton), nameof(DLCBuyButton.Setup))]
    internal static class DLCControlIconSetupPatch
    {
        private static void Postfix(DLCBuyButton __instance) => DLCControl.RefreshButton(__instance);
    }

    [HarmonyPatch(typeof(DLCBuyButton), "OnButtonPressed")]
    internal static class DLCControlIconClickPatch
    {
        private static bool Prefix(DLCBuyButton __instance) => DLCControl.HandleClick(__instance);
    }

    [HarmonyPatch(typeof(DLCBuyButton), "OnTooltip")]
    internal static class DLCControlIconTooltipPatch
    {
        private static void Postfix(DLCBuyButton __instance, Tooltip __0) =>
            DLCControl.UpdateTooltip(__instance, __0);
    }
}
