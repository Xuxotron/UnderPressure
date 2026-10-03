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
        private static string[] _overlayAssetNames = Array.Empty<string>();
        private static float _overlayVisibleUntil;
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
            Reload(true);
        }

        public static bool Reload() => Reload(false);

        private static bool Reload(bool showOverlay)
        {
            if (_reloading) return false;
            _reloading = true;
            if (showOverlay)
            {
                _overlayAssetNames = Array.Empty<string>();
                _overlayVisibleUntil = Time.unscaledTime + 8f;
            }
            try
            {
                if (string.IsNullOrEmpty(_bundlePath) || !File.Exists(_bundlePath))
                {
                    _log?.LogError("No existe el AssetBundle global de UnderPressure: " + _bundlePath);
                    return false;
                }

                var bundleBytes = File.ReadAllBytes(_bundlePath);
                MainAssets.Clear();
                _allAssets = Array.Empty<Object>();
                if (_bundle != null)
                {
                    _bundle.Unload(true);
                    _bundle = null;
                }

                var reloadedBundle = AssetBundle.LoadFromMemory(bundleBytes);
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
                if (showOverlay) _overlayAssetNames = assetNames ?? Array.Empty<string>();
                MainAssets.Clear();
                foreach (var pair in mainAssets) MainAssets.Add(pair.Key, pair.Value);

                NotifyReloaded();
                _log?.LogInfo("AssetBundle global de UnderPressure recargado completo: " +
                              assetNames.Length + " entradas, " + _allAssets.Length + " assets cargados desde " +
                              _bundlePath + ".");
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

        public static T LoadAsset<T>(string assetPath) where T : Object
        {
            if (string.IsNullOrEmpty(assetPath) || _bundle == null) return null;
            return _bundle.LoadAsset<T>(assetPath);
        }

        public static T[] GetAllAssets<T>() where T : Object
        {
            return _bundle == null ? Array.Empty<T>() : _bundle.LoadAllAssets<T>();
        }

        internal static void DrawReloadOverlay()
        {
            if (Time.unscaledTime >= _overlayVisibleUntil) return;

            const float left = 14f;
            const float top = 14f;
            const float width = 720f;
            const float lineHeight = 21f;
            var lines = Math.Max(1, _overlayAssetNames.Length);
            var height = 48f + lines * lineHeight;
            GUI.Box(new Rect(left, top, width, height), GUIContent.none);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(left + 12f, top + 8f, width - 24f, 28f),
                ModLocalization.Get("assets.loading"), titleStyle);

            var assetStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.82f, 0.95f, 1f, 1f) }
            };
            if (_overlayAssetNames.Length == 0)
            {
                GUI.Label(new Rect(left + 12f, top + 38f, width - 24f, lineHeight),
                    ModLocalization.Get("assets.not_found"), assetStyle);
                return;
            }

            for (var index = 0; index < _overlayAssetNames.Length; ++index)
                GUI.Label(new Rect(left + 12f, top + 38f + index * lineHeight, width - 24f, lineHeight),
                    _overlayAssetNames[index], assetStyle);
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
