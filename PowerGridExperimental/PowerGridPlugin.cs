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
            var bundlePath = Path.Combine(pluginDirectory ?? string.Empty, "Assets", "underpressure_powerpanel");
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
        }
    }
}
