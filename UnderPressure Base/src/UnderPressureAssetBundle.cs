// Updated: 2026-10-08
// Purpose: Loads, globally converts shaders in, and hot-reloads the shared Under Pressure AssetBundle for all mod assemblies.
using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using UnderPressure.PowerGrid;
using System.Collections.Generic;

namespace UnderPressure
{
    public static class UnderPressureAssetBundle
    {
        public const string FileName = "underpressure";

        private static ManualLogSource _log;
        private static string _bundlePath;
        private static AssetBundle _bundle;
        private static UnityEngine.Object[] _allAssets = Array.Empty<UnityEngine.Object>();
        private static string[] _overlayAssetNames = Array.Empty<string>();
        private static string _overlayExtraSavePath;
        private static bool _overlayExtraSaveFound;
        private static float _overlayVisibleUntil;
        private static bool _reloading;

        public static event Action Reloading;
        public static event Action Reloaded;
        public static event Action ReloadCompleted;

        public static string BundlePath => _bundlePath;
        public static UnityEngine.Object[] AllAssets => _allAssets;

        internal static void Initialise(ManualLogSource log, string pluginDirectory)
        {
            _log = log;
            _bundlePath = Path.Combine(pluginDirectory ?? string.Empty, "Assets", FileName);
            Reload(false);
        }

        internal static void UpdateHotkey()
        {
            if (_reloading || !Input.GetKeyDown(KeyCode.F1)) return;
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
            Reload(true);
        }

        public static bool Reload()
        {
            return Reload(false);
        }

        internal static bool Reload(bool showOverlay)
        {
            if (_reloading) return false;
            _reloading = true;

            if (showOverlay)
            {
                _overlayAssetNames = Array.Empty<string>();
                _overlayExtraSavePath = null;
                _overlayExtraSaveFound = false;
                _overlayVisibleUntil = Time.unscaledTime + 8f;
            }

            try
            {
                if (string.IsNullOrEmpty(_bundlePath) || !File.Exists(_bundlePath))
                {
                    _log?.LogError("No existe el AssetBundle global de UnderPressure: " + _bundlePath);
                    return false;
                }

                // Leer primero a memoria permite descargar el bundle anterior y volver a cargar
                // exactamente el mismo fichero sin mantenerlo bloqueado.
                var bytes = File.ReadAllBytes(_bundlePath);

                NotifyReloading();

                _allAssets = Array.Empty<UnityEngine.Object>();

                if (_bundle != null)
                {
                    // false: descarga el contenedor pero mantiene vivos los objetos existentes
                    // hasta que los consumidores sustituyan sus referencias durante Reloaded.
                    _bundle.Unload(false);
                    _bundle = null;
                }

                var newBundle = AssetBundle.LoadFromMemory(bytes);
                if (newBundle == null)
                {
                    _log?.LogError("No se pudo recargar el AssetBundle global de UnderPressure: " + _bundlePath);
                    return false;
                }

                _bundle = newBundle;

                // GLOBAL Y ANTES DE LOS CONSUMIDORES:
                // carga todos los materiales contenidos en el AssetBundle y sustituye sus
                // shaders proxy por los shaders nativos del juego. Cualquier prefab, sala u
                // objeto que se cargue después recibe ya su material convertido, sin llamadas
                // específicas por asset.
                var bundleMaterials = _bundle.LoadAllAssets<Material>() ?? Array.Empty<Material>();
                var convertedMaterials = ShaderConversion.ConvertBundleMaterials(bundleMaterials);

                _log?.LogInfo(
                    "Conversión global de shaders del AssetBundle: " +
                    convertedMaterials + " de " + bundleMaterials.Length +
                    " materiales convertidos antes de cargar consumidores.");

                // Solo se enumeran nombres cuando Ctrl+F1 necesita mostrarlos en pantalla.
                if (showOverlay)
                    _overlayAssetNames = _bundle.GetAllAssetNames() ?? Array.Empty<string>();

                NotifyReloaded();
                NotifyReloadCompleted();

                _log?.LogInfo("AssetBundle global de UnderPressure recargado desde " + _bundlePath + ".");
                return true;
            }
            catch (Exception exception)
            {
                _log?.LogError("Error al recargar el AssetBundle global de UnderPressure: " + exception);
                return false;
            }
            finally
            {
                _reloading = false;
            }
        }

        public static T LoadAsset<T>(string assetName) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(assetName) || _bundle == null) return default(T);
            return _bundle.LoadAsset<T>(assetName);
        }

        public static T[] GetAllAssets<T>() where T : UnityEngine.Object
        {
            return _bundle != null ? _bundle.LoadAllAssets<T>() : Array.Empty<T>();
        }

        public static void ReportExtraSaveLoad(string path, bool saveExists)
        {
            _overlayExtraSavePath = path;
            _overlayExtraSaveFound = saveExists;
        }

        internal static void DrawReloadOverlay()
        {
            if (Time.unscaledTime >= _overlayVisibleUntil) return;

            var width = Mathf.Min(1200f, Screen.width - 28f);
            var assetLines = Math.Max(1, _overlayAssetNames.Length);
            var hasExtraSave = !string.IsNullOrEmpty(_overlayExtraSavePath);
            var totalLines = assetLines + (hasExtraSave ? 1 : 0);
            var height = 48f + totalLines * 21f;

            GUI.Box(new Rect(14f, 14f, width, height), GUIContent.none);

            var headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
            headingStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(26f, 22f, width - 24f, 28f), "LOADING ASSETS", headingStyle);

            var itemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14
            };
            itemStyle.normal.textColor = new Color(0.82f, 0.95f, 1f, 1f);

            if (_overlayAssetNames.Length == 0)
            {
                GUI.Label(new Rect(26f, 52f, width - 24f, 21f), "ASSETS NOT FOUND", itemStyle);
            }
            else
            {
                for (var index = 0; index < _overlayAssetNames.Length; ++index)
                {
                    GUI.Label(
                        new Rect(26f, 52f + index * 21f, width - 24f, 21f),
                        _overlayAssetNames[index],
                        itemStyle);
                }
            }

            if (hasExtraSave)
            {
                var prefix = _overlayExtraSaveFound ? "LOADING UPSAV:" : "UPSAV NOT FOUND:";
                GUI.Label(
                    new Rect(26f, 52f + assetLines * 21f, width - 24f, 21f),
                    prefix + " " + _overlayExtraSavePath,
                    itemStyle);
            }
        }

        private static void NotifyReloaded()
        {
            InvokeSubscribers(Reloaded,
                "Un consumidor no pudo aplicar los assets globales recargados: ");
        }

        private static void NotifyReloading()
        {
            InvokeSubscribers(Reloading,
                "Un consumidor no pudo preparar la recarga de assets globales: ");
        }

        private static void NotifyReloadCompleted()
        {
            InvokeSubscribers(ReloadCompleted,
                "Un consumidor no pudo completar la recarga global de assets: ");
        }

        private static void InvokeSubscribers(Action handlers, string errorPrefix)
        {
            if (handlers == null) return;

            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    _log?.LogError(errorPrefix + exception);
                }
            }
        }
    }
}

// Updated: 2026-10-08
// Purpose: Replaces proxy shaders from the global Under Pressure AssetBundle with the real shaders already loaded by Two Point Hospital.
namespace UnderPressure.PowerGrid
{
    public static class ShaderConversion
    {
        // Add future proxy -> native shader mappings here.
        // The proxy and native name may be identical; they are still different Shader instances at runtime.
        private static readonly Dictionary<string, string> ShaderMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "TH20 Standard", "TH20 Standard" }
            };

        /// <summary>
        /// Converts every known proxy shader used by every material contained in the
        /// Under Pressure AssetBundle. This is deliberately global: consumers do not
        /// need to remember to convert individual rooms, prefabs or materials.
        /// </summary>
        public static int ConvertBundleMaterials(Material[] bundleMaterials)
        {
            if (bundleMaterials == null || bundleMaterials.Length == 0)
                return 0;

            var proxyShaders = new HashSet<Shader>();

            foreach (var material in bundleMaterials)
            {
                if (material == null || material.shader == null)
                    continue;

                if (ShaderMap.ContainsKey(material.shader.name))
                    proxyShaders.Add(material.shader);
            }

            if (proxyShaders.Count == 0)
                return 0;

            var totalConverted = 0;

            foreach (var proxyShader in proxyShaders)
            {
                if (proxyShader == null)
                    continue;

                string nativeShaderName;
                if (!ShaderMap.TryGetValue(proxyShader.name, out nativeShaderName))
                    continue;

                var nativeShader = ResolveNativeShader(
                    proxyShader,
                    nativeShaderName,
                    proxyShaders);

                if (nativeShader == null)
                {
                    Debug.LogWarning(
                        "ShaderConversion: no se encontro el shader nativo '" +
                        nativeShaderName + "' para sustituir el proxy '" +
                        proxyShader.name + "'.");
                    continue;
                }

                var convertedForShader = 0;

                foreach (var material in bundleMaterials)
                {
                    if (material == null || material.shader != proxyShader)
                        continue;

                    ConvertMaterial(material, nativeShader);
                    ++convertedForShader;
                    ++totalConverted;
                }

                Debug.Log(
                    "ShaderConversion GLOBAL: '" + proxyShader.name + "' -> '" +
                    nativeShader.name + "': " + convertedForShader +
                    " materiales del AssetBundle convertidos.");
            }

            return totalConverted;
        }

        /// <summary>
        /// Compatibility entry point for older callers. New code should use
        /// ConvertBundleMaterials with all materials from the shared AssetBundle.
        /// </summary>
        public static int ConvertKnownShaders(params Material[] materials)
        {
            return ConvertBundleMaterials(materials);
        }

        private static Shader ResolveNativeShader(
            Shader proxyShader,
            string nativeShaderName,
            HashSet<Shader> bundleProxyShaders)
        {
            // First try Unity's global shader lookup. It is valid only if it does not
            // resolve to any proxy Shader instance that came from our AssetBundle.
            var found = Shader.Find(nativeShaderName);
            if (IsValidNativeShader(found, nativeShaderName, bundleProxyShaders))
                return found;

            // If Shader.Find resolves to our proxy because both shaders have the same
            // name, inspect materials already loaded by the game and choose the native
            // Shader instance with that name that is referenced by the most materials.
            var referenceCounts = new Dictionary<Shader, int>();

            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material == null || material.shader == null)
                    continue;

                var candidate = material.shader;
                if (!IsValidNativeShader(candidate, nativeShaderName, bundleProxyShaders))
                    continue;

                int count;
                referenceCounts.TryGetValue(candidate, out count);
                referenceCounts[candidate] = count + 1;
            }

            Shader best = null;
            var bestCount = -1;

            foreach (var pair in referenceCounts)
            {
                if (pair.Value <= bestCount)
                    continue;

                best = pair.Key;
                bestCount = pair.Value;
            }

            // Last fallback: the native shader may already be loaded even when no
            // currently loaded material references it.
            if (best == null)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Shader>())
                {
                    if (!IsValidNativeShader(candidate, nativeShaderName, bundleProxyShaders))
                        continue;

                    best = candidate;
                    break;
                }
            }

            return best;
        }

        private static bool IsValidNativeShader(
            Shader candidate,
            string nativeShaderName,
            HashSet<Shader> bundleProxyShaders)
        {
            return candidate != null &&
                   !bundleProxyShaders.Contains(candidate) &&
                   string.Equals(candidate.name, nativeShaderName, StringComparison.Ordinal);
        }

        private static void ConvertMaterial(Material material, Shader nativeShader)
        {
            if (material == null || nativeShader == null || material.shader == nativeShader)
                return;

            var originalKeywords = material.shaderKeywords;
            var originalRenderQueue = material.renderQueue;
            var originalGiFlags = material.globalIlluminationFlags;
            var originalEnableInstancing = material.enableInstancing;
            var originalDoubleSidedGi = material.doubleSidedGI;
            var snapshot = new Material(material);

            try
            {
                material.shader = nativeShader;
                material.CopyPropertiesFromMaterial(snapshot);

                // Preserve material-level state explicitly across the shader swap.
                material.shaderKeywords = originalKeywords;
                material.renderQueue = originalRenderQueue;
                material.globalIlluminationFlags = originalGiFlags;
                material.enableInstancing = originalEnableInstancing;
                material.doubleSidedGI = originalDoubleSidedGi;

                SynchronizeConfirmedKeywords(material);
            }
            finally
            {
                UnityEngine.Object.Destroy(snapshot);
            }
        }

        private static void SynchronizeConfirmedKeywords(Material material)
        {
            if (material == null || material.shader == null)
                return;

            if (!string.Equals(material.shader.name, "TH20 Standard", StringComparison.Ordinal))
                return;

            // Confirmed from native TH20 Standard materials: a populated normal-map
            // slot requires the _NORMALMAP shader variant.
            if (material.HasProperty("_BumpMap"))
            {
                if (material.GetTexture("_BumpMap") != null)
                    material.EnableKeyword("_NORMALMAP");
                else
                    material.DisableKeyword("_NORMALMAP");
            }

            // Keep the room-lighting keyword consistent with the material property.
            if (material.HasProperty("_ApplyRoomLighting"))
            {
                if (material.GetFloat("_ApplyRoomLighting") > 0.5f)
                    material.DisableKeyword("_APPLYROOMLIGHTING_OFF");
                else
                    material.EnableKeyword("_APPLYROOMLIGHTING_OFF");
            }
        }
    }
}
