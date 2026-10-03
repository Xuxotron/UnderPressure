using System;
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

        private void Awake()
        {
            Log = Logger;
            UnderPressureAssetBundle.Reloaded += LoadUiAssets;
            LoadUiAssets();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void OnDestroy()
        {
            UnderPressureAssetBundle.Reloaded -= LoadUiAssets;
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

            if (BatterySprite == null)
                BatterySprite = UnderPressureAssetBundle.LoadAsset<Sprite>("Assets/UI/bateria.png");
            if (BatterySprite == null)
                Logger.LogError("El AssetBundle de interfaz no contiene el sprite bateria.");
            PowerPanelPrefab = UnderPressureAssetBundle.LoadAsset<GameObject>(
                ElectricalPanelPrefabParameters.PrefabAsset);
            if (PowerPanelPrefab == null)
                Logger.LogError("El AssetBundle no contiene el prefab del cuadro electrico.");
            else
                ConfigurePanelGraphics();

            var panelIconTexture = UnderPressureAssetBundle.LoadAsset<Texture2D>(
                ElectricalPanelPrefabParameters.IconTextureAsset);
            if (panelIconTexture != null)
            {
                PowerPanelSprite = Sprite.Create(panelIconTexture,
                    new Rect(0f, 0f, panelIconTexture.width, panelIconTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                PowerPanelSprite.name = "powerpanel icon";
                PowerPanelSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            else Logger.LogError("El AssetBundle no contiene el icono del cuadro electrico.");
            EnergyRoomItems.RefreshBundleAssetReferences(PowerPanelPrefab, PowerPanelSprite);
        }

        private void ConfigurePanelGraphics()
        {
            var material = UnderPressureAssetBundle.LoadAsset<Material>(
                ElectricalPanelPrefabParameters.MaterialAsset);
            var texture = UnderPressureAssetBundle.LoadAsset<Texture2D>(
                ElectricalPanelPrefabParameters.TextureAsset);
            var meshes = UnderPressureAssetBundle.GetAllAssets<Mesh>();

            if (material == null)
                Logger.LogError("No se encontro el material grafico del cuadro electrico.");
            if (texture == null)
                Logger.LogError("No se encontro la textura grafica del cuadro electrico.");
            if (material != null && texture != null)
                material.mainTexture = texture;

            ConfigurePanelMesh(meshes, ElectricalPanelPrefabParameters.BaseMesh, material);
            ConfigurePanelMesh(meshes, ElectricalPanelPrefabParameters.DoorMesh, material);
        }

        private void ConfigurePanelMesh(Mesh[] meshes, string meshName, Material material)
        {
            var target = FindChild(PowerPanelPrefab.transform, meshName);
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
