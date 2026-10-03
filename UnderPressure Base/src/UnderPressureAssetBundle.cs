using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnderPressure
{
    public static class UnderPressureAssetBundle
    {
        public const string FileName = "underpressure";

        private static readonly Dictionary<string, Object> MainAssets =
            new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);
        private static ManualLogSource _log;
        private static string _bundlePath;
        private static AssetBundle _bundle;
        private static Object[] _allAssets = Array.Empty<Object>();
        private static bool _reloading;

        public static event Action Reloaded;

        public static string BundlePath => _bundlePath;
        public static IReadOnlyList<Object> AllAssets => _allAssets;

        internal static void Initialise(ManualLogSource log, string pluginDirectory)
        {
            _log = log;
            _bundlePath = Path.Combine(pluginDirectory ?? string.Empty, "Assets", FileName);
            Reload();
        }

        internal static void UpdateHotkey()
        {
            if (_reloading || !Input.GetKeyDown(KeyCode.F1) ||
                (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))) return;
            Reload();
        }

        public static bool Reload()
        {
            if (_reloading) return false;
            _reloading = true;
            try
            {
                if (string.IsNullOrEmpty(_bundlePath) || !File.Exists(_bundlePath))
                {
                    _log?.LogError("No existe el AssetBundle global de UnderPressure: " + _bundlePath);
                    return false;
                }

                if (_bundle != null)
                {
                    _bundle.Unload(false);
                    _bundle = null;
                }

                var reloadedBundle = AssetBundle.LoadFromFile(_bundlePath);
                if (reloadedBundle == null)
                {
                    _log?.LogError("No se pudo recargar el AssetBundle global de UnderPressure: " + _bundlePath);
                    return false;
                }

                var assetNames = reloadedBundle.GetAllAssetNames();
                var allAssets = reloadedBundle.LoadAllAssets();
                var mainAssets = new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);
                foreach (var assetName in assetNames)
                {
                    var asset = reloadedBundle.LoadAsset(assetName);
                    if (asset != null) mainAssets[assetName] = asset;
                }

                _bundle = reloadedBundle;
                _allAssets = allAssets ?? Array.Empty<Object>();
                MainAssets.Clear();
                foreach (var pair in mainAssets) MainAssets.Add(pair.Key, pair.Value);

                NotifyReloaded();
                _log?.LogInfo("AssetBundle global de UnderPressure recargado completo: " +
                              assetNames.Length + " entradas, " + _allAssets.Length + " assets cargados desde " +
                              _bundlePath + ".");
                return true;
            }
            finally
            {
                _reloading = false;
            }
        }

        public static T LoadAsset<T>(string assetPath) where T : Object
        {
            if (string.IsNullOrEmpty(assetPath) || _bundle == null) return null;
            return _bundle.LoadAsset<T>(assetPath);
        }

        public static T[] GetAllAssets<T>() where T : Object
        {
            var assets = new List<T>();
            foreach (var asset in _allAssets)
                if (asset is T typedAsset) assets.Add(typedAsset);
            return assets.ToArray();
        }

        private static void NotifyReloaded()
        {
            var handlers = Reloaded;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    _log?.LogError("Un consumidor no pudo aplicar los assets globales recargados: " + exception);
                }
        }
    }
}
