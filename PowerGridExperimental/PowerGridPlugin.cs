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
                ConfigurePowerPanelVisual(PowerPanelPrefab);

                // Reserve the complete floor tile in front of the panel for its future
                // maintenance animation. Build bounds are expressed in item-space by the
                // game and therefore remain exactly one tile despite the imported FBX scale.
                var buildBounds = PowerPanelPrefab.GetComponent<ItemBuildBoundsComponent>() ??
                                  PowerPanelPrefab.AddComponent<ItemBuildBoundsComponent>();
                buildBounds.center = new Vector3(0f, 0.5f, 0.5f);
                buildBounds.size = Vector3.one;
                buildBounds.Solid = true;
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

        private static void ConfigurePowerPanelVisual(GameObject prefab)
        {
            var visualRoot = FindChild(prefab.transform, "A_Prop_Nurse_Locker_V1");
            if (visualRoot == null)
            {
                Log.LogError("El prefab del cuadro electrico no contiene su raiz visual.");
                return;
            }

            // Keep the model on the same side as its reserved access tile. One tested
            // position left it a full tile away and the opposite correction moved it a
            // full tile through the wall; their midpoint places its rear face on the wall.
            visualRoot.localPosition = new Vector3(0f, 0f, 0.012733f);

            // The imported faces point into the wall. Flip each mesh around its own centre
            // so the door remains in front of the box instead of exchanging their depths.
            foreach (var filter in visualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var meshTransform = filter.transform;
                var scaledCentre = Vector3.Scale(filter.sharedMesh.bounds.center, meshTransform.localScale);
                var centreCorrection = meshTransform.localRotation *
                                       new Vector3(scaledCentre.x * 2f, 0f, scaledCentre.z * 2f);
                meshTransform.localPosition += centreCorrection;
                meshTransform.localRotation *= Quaternion.Euler(0f, 180f, 0f);

                // The locker rig leaves the box base 4.5 cm farther out than the lid.
                // Mirror that separation so the base sits against the wall and the lid
                // remains in front, ready for its later opening animation.
                if (string.Equals(filter.name, "ChafCaja05", StringComparison.Ordinal))
                    meshTransform.localPosition += new Vector3(0f, 0f, 0.002318f);
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
