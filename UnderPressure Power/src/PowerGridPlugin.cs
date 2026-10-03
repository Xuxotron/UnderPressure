using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TH20;
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
        private bool _assetReloadInProgress;

        private void Awake()
        {
            Log = Logger;
            LoadUiAssets(false);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private void Update()
        {
            if (_assetReloadInProgress || !Input.GetKeyDown(KeyCode.F1) ||
                (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))) return;

            _assetReloadInProgress = true;
            try
            {
                LoadUiAssets(true);
            }
            finally
            {
                _assetReloadInProgress = false;
            }
        }

        private bool LoadUiAssets(bool reload)
        {
            if (!reload)
            {
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (sprite == null || !string.Equals(sprite.name, "bateria", StringComparison.OrdinalIgnoreCase))
                        continue;
                    BatterySprite = sprite;
                    break;
                }
            }

            var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var assetsDirectory = Path.Combine(pluginDirectory ?? string.Empty, "Assets");
            var bundlePath = Path.Combine(assetsDirectory, "underpressure");
            if (!File.Exists(bundlePath))
            {
                Logger.LogError("No existe el AssetBundle de UnderPressure: " + bundlePath);
                return false;
            }

            if (reload && !EnergyRoomItems.CanReloadPanelAssets(out var blockedReason))
            {
                Logger.LogWarning("Recarga de assets cancelada: " + blockedReason);
                return false;
            }

            if (reload && _uiAssetBundle != null)
            {
                _uiAssetBundle.Unload(false);
                _uiAssetBundle = null;
            }

            var newBundle = AssetBundle.LoadFromFile(bundlePath);
            if (newBundle == null)
            {
                Logger.LogError("No se pudo cargar el AssetBundle de UnderPressure: " + bundlePath);
                return false;
            }

            var newBatterySprite = BatterySprite ?? newBundle.LoadAsset<Sprite>("Assets/UI/bateria.png");
            if (newBatterySprite == null)
                Logger.LogError("El AssetBundle de UnderPressure no contiene el sprite bateria.");
            var newPanelPrefab = newBundle.LoadAsset<GameObject>(
                ElectricalPanelPrefabParameters.PrefabAsset);
            if (newPanelPrefab == null)
                Logger.LogError("El AssetBundle no contiene el prefab del cuadro electrico.");
            else
                ConfigurePanelGraphics(newBundle, newPanelPrefab);

            var panelIconTexture = newBundle.LoadAsset<Texture2D>(
                ElectricalPanelPrefabParameters.IconTextureAsset);
            Sprite newPanelSprite = null;
            if (panelIconTexture != null)
            {
                newPanelSprite = Sprite.Create(panelIconTexture,
                    new Rect(0f, 0f, panelIconTexture.width, panelIconTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                newPanelSprite.name = "powerpanel icon";
                newPanelSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            else Logger.LogError("El AssetBundle no contiene el icono del cuadro electrico.");

            _uiAssetBundle = newBundle;
            if (newPanelPrefab == null || newPanelSprite == null) return false;
            if (reload && !EnergyRoomItems.ApplyReloadedPanelAssets(newPanelPrefab, newPanelSprite)) return false;

            BatterySprite = newBatterySprite;
            PowerPanelPrefab = newPanelPrefab;
            PowerPanelSprite = newPanelSprite;
            if (reload)
                Logger.LogInfo("Assets de UnderPressure recargados con Ctrl+F1 desde: " + bundlePath);
            return true;
        }

        private void ConfigurePanelGraphics(AssetBundle bundle, GameObject panelPrefab)
        {
            var material = bundle.LoadAsset<Material>(
                ElectricalPanelPrefabParameters.MaterialAsset);
            var texture = bundle.LoadAsset<Texture2D>(
                ElectricalPanelPrefabParameters.TextureAsset);
            var meshes = bundle.LoadAllAssets<Mesh>();

            if (material == null)
                Logger.LogError("No se encontro el material grafico del cuadro electrico.");
            if (texture == null)
                Logger.LogError("No se encontro la textura grafica del cuadro electrico.");
            if (material != null && texture != null)
                material.mainTexture = texture;

            ConfigurePanelMesh(panelPrefab, meshes, ElectricalPanelPrefabParameters.BaseMesh, material);
            ConfigurePanelMesh(panelPrefab, meshes, ElectricalPanelPrefabParameters.DoorMesh, material);
        }

        private void ConfigurePanelMesh(GameObject panelPrefab, Mesh[] meshes, string meshName, Material material)
        {
            var target = FindChild(panelPrefab.transform, meshName);
            var targetFilter = target?.GetComponent<MeshFilter>();
            if (targetFilter == null)
            {
                Logger.LogError("El prefab del cuadro no contiene el objeto de malla '" + meshName + "'.");
                return;
            }
            Mesh selected = null;
            foreach (var mesh in meshes ?? Array.Empty<Mesh>())
                if (mesh != null && string.Equals(mesh.name, meshName, StringComparison.Ordinal))
                {
                    selected = mesh;
                    break;
                }
            if (selected == null)
                Logger.LogError("El AssetBundle no contiene la malla '" + meshName + "'.");
            else
                targetFilter.sharedMesh = selected;
            var renderer = target.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
        }

        private static Transform FindChild(Transform root, string objectName)
        {
            if (root == null) return null;
            if (string.Equals(root.name, objectName, StringComparison.Ordinal)) return root;
            for (var index = 0; index < root.childCount; ++index)
            {
                var found = FindChild(root.GetChild(index), objectName);
                if (found != null) return found;
            }
            return null;
        }

    }
}
