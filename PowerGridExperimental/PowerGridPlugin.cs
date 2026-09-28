using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

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
        private Harmony _harmony;
        private AssetBundle _uiAssetBundle;

        private void Awake()
        {
            Log = Logger;
            LoadUiAssets();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private void LoadUiAssets()
        {
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || !string.Equals(sprite.name, "bateria", StringComparison.OrdinalIgnoreCase))
                    continue;
                BatterySprite = sprite;
                break;
            }

            var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var assetsDirectory = Path.Combine(pluginDirectory ?? string.Empty, "Assets");
            var bundlePath = Path.Combine(assetsDirectory, "underpressure_powerpanel");
            _uiAssetBundle = AssetBundle.LoadFromFile(bundlePath);
            if (_uiAssetBundle == null)
            {
                Logger.LogError("No se pudo cargar el AssetBundle de interfaz: " + bundlePath);
                return;
            }

            if (BatterySprite == null)
                BatterySprite = _uiAssetBundle.LoadAsset<Sprite>("assets/underpressure/ui/bateria.png");
            if (BatterySprite == null)
                Logger.LogError("El AssetBundle de interfaz no contiene el sprite bateria.");
            PowerPanelPrefab = _uiAssetBundle.LoadAsset<GameObject>("assets/powerpanel/powerpanel.prefab");
            if (PowerPanelPrefab == null)
                Logger.LogError("El AssetBundle no contiene el prefab del cuadro electrico.");
            else
            {
                // The source FBX uses centimetres. Its Unity prefab was saved with the
                // scene position and a 0.3937008 root scale; compare against the native
                // nurse locker hierarchy (the animation reference) to restore metres.
                PowerPanelPrefab.transform.localPosition = Vector3.zero;
                PowerPanelPrefab.transform.localRotation = Quaternion.identity;
                PowerPanelPrefab.transform.localScale = Vector3.one * 39.37008f;
            }

            var panelIconPath = Path.Combine(assetsDirectory, "panelenergy.png");
            if (File.Exists(panelIconPath))
            {
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false)
                {
                    name = "panelenergy",
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(panelIconPath), false))
                {
                    PowerPanelSprite = Sprite.Create(texture,
                        new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    PowerPanelSprite.name = "panelenergy";
                    PowerPanelSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                else Logger.LogError("No se pudo decodificar el icono del cuadro electrico.");
            }
            else Logger.LogError("No se encontro el icono del cuadro electrico: " + panelIconPath);
        }
    }
}
