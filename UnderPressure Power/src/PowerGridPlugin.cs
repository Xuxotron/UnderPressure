// Updated: 2026-10-07
// Purpose: Bootstraps the electrical plugin, shared assets, Harmony patches, and extra-save reloads.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.UI;
using UnderPressure;

namespace UnderPressure.PowerGrid
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("artef.twopointhospital.underpressure", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class PowerGridPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure.powergrid.experimental";
        public const string PluginName = "Bajo Presion - Red electrica experimental";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }
        internal static Sprite BatterySprite { get; private set; }
        internal static Sprite PowerPanelSprite { get; private set; }
        internal static GameObject PowerPanelPrefab { get; private set; }
        internal static GameObject TransformerPrefab { get; private set; }
        internal static GameObject ElectricalCellPrefab { get; private set; }

        private readonly List<UiSpriteReference> _uiSpriteReferences =
            new List<UiSpriteReference>();

        private Harmony _harmony;

        private enum UiSpriteKind
        {
            Battery,
            Panel
        }

        private sealed class UiSpriteReference
        {
            internal Image Image;
            internal UiSpriteKind Kind;
            internal bool Sprite;
            internal bool OverrideSprite;
        }

        private void Awake()
        {
            Log = Logger;

            UnderPressureAssetBundle.Reloading += CaptureUiSpriteReferences;
            UnderPressureAssetBundle.Reloaded += LoadUiAssets;
            UnderPressureAssetBundle.ReloadCompleted += ReloadExtraState;

            LoadUiAssets();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void OnDestroy()
        {
            UnderPressureAssetBundle.Reloading -= CaptureUiSpriteReferences;
            UnderPressureAssetBundle.Reloaded -= LoadUiAssets;
            UnderPressureAssetBundle.ReloadCompleted -= ReloadExtraState;

            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private void LoadUiAssets()
        {
            var energyRoomMaterials =
                RoomCatalog.LoadSurfaceMaterials(RoomCatalog.Energy);

            if (energyRoomMaterials != null)
            {
                ShaderConversion.ConvertKnownShaders(
                    energyRoomMaterials.Floor,
                    energyRoomMaterials.Interior,
                    energyRoomMaterials.Exterior,
                    energyRoomMaterials.Door);

                RoomCatalog.SetSurfaceMaterials(
                    RoomCatalog.Energy,
                    energyRoomMaterials);
            }

            BatterySprite =
                UnderPressureAssetBundle.LoadAsset<Sprite>(
                    "Assets/UI/bateria.png");

            if (BatterySprite == null)
            {
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (sprite == null ||
                        !string.Equals(
                            sprite.name,
                            "bateria",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    BatterySprite = sprite;
                    break;
                }
            }

            if (BatterySprite == null)
                Logger.LogError(
                    "El AssetBundle de interfaz no contiene el sprite bateria.");

            PowerPanelPrefab =
                UnderPressureAssetBundle.LoadAsset<GameObject>(
                    ElectricalPanelPrefabParameters.PrefabAsset);

            if (PowerPanelPrefab == null)
            {
                Logger.LogError(
                    "El AssetBundle no contiene el prefab del cuadro electrico.");
            }


            if (PowerPanelPrefab != null)
                EnableNativeBuildEffect(PowerPanelPrefab);

            TransformerPrefab =
                UnderPressureAssetBundle.LoadAsset<GameObject>(
                    "Assets/Transformador/Transformador.prefab");
            ElectricalCellPrefab =
                UnderPressureAssetBundle.LoadAsset<GameObject>(
                    "Assets/CeldaElectrica/Celda.prefab");

            if (TransformerPrefab == null)
                Logger.LogError("El AssetBundle no contiene el prefab del transformador.");
            if (ElectricalCellPrefab == null)
                Logger.LogError("El AssetBundle no contiene el prefab de la celda electrica.");

            var panelIconTexture =
                UnderPressureAssetBundle.LoadAsset<Texture2D>(
                    ElectricalPanelPrefabParameters.IconTextureAsset);

            if (panelIconTexture != null)
            {
                PowerPanelSprite =
                    Sprite.Create(
                        panelIconTexture,
                        new Rect(
                            0f,
                            0f,
                            panelIconTexture.width,
                            panelIconTexture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);

                PowerPanelSprite.name =
                    "powerpanel icon";

                PowerPanelSprite.hideFlags =
                    HideFlags.DontUnloadUnusedAsset;
            }
            else
            {
                Logger.LogError(
                    "El AssetBundle no contiene el icono del cuadro electrico.");
            }

            EnergyRoomItems.RefreshBundleAssetReferences(
                PowerPanelPrefab,
                PowerPanelSprite,
                TransformerPrefab,
                ElectricalCellPrefab);

            RestoreUiSpriteReferences();
        }

        private void EnableNativeBuildEffect(GameObject prefab)
        {
            if (prefab == null)
                return;

            var compatibleMaterials = 0;

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null ||
                        !material.HasProperty("_BuildEffect"))
                        continue;

                    material.SetFloat("_BuildEffect", 1f);
                    ++compatibleMaterials;
                }
            }

            if (compatibleMaterials == 0)
            {
                Logger.LogWarning(
                    "Los materiales del cuadro electrico no exponen _BuildEffect; " +
                    "su shader no puede reproducir la animacion nativa de construccion.");
            }
        }


        private static void ReloadExtraState()
        {
            PowerGridPrototype.ReloadActiveExtraState();
        }

        private void CaptureUiSpriteReferences()
        {
            _uiSpriteReferences.Clear();

            foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image == null)
                    continue;

                CaptureUiSpriteReference(
                    image,
                    BatterySprite,
                    UiSpriteKind.Battery);

                CaptureUiSpriteReference(
                    image,
                    PowerPanelSprite,
                    UiSpriteKind.Panel);
            }
        }

        private void CaptureUiSpriteReference(
            Image image,
            Sprite sprite,
            UiSpriteKind kind)
        {
            if (sprite == null)
                return;

            var usesSprite =
                image.sprite == sprite;

            var usesOverride =
                image.overrideSprite == sprite;

            if (!usesSprite &&
                !usesOverride)
                return;

            _uiSpriteReferences.Add(
                new UiSpriteReference
                {
                    Image = image,
                    Kind = kind,
                    Sprite = usesSprite,
                    OverrideSprite = usesOverride
                });
        }

        private void RestoreUiSpriteReferences()
        {
            foreach (var reference in _uiSpriteReferences)
            {
                if (reference.Image == null)
                    continue;

                var sprite =
                    reference.Kind == UiSpriteKind.Battery
                        ? BatterySprite
                        : PowerPanelSprite;

                if (sprite == null)
                    continue;

                if (reference.Sprite)
                    reference.Image.sprite = sprite;

                if (reference.OverrideSprite)
                    reference.Image.overrideSprite = sprite;
            }

            _uiSpriteReferences.Clear();
        }
    }
}
