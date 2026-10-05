// Purpose: Loads and hot-reloads the shared Under Pressure AssetBundle for all mod assemblies.
using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

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

                // En el arranque NO se hace LoadAllAssets ni se recorre asset por asset.
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
