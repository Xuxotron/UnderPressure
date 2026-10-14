// Updated: 2026-10-12
// Inicia el editor de escenarios y guarda la opción Mostrar nuevos mapas.
using System.Reflection;
using UnderPressure.LoaderRuntime;
using UnderPressure.LoaderRuntime.Configuration;
using UnderPressure.LoaderRuntime.Logging;
using HarmonyLib;

namespace UnderPressure.Editor
{
    [ModPlugin(PluginGuid, PluginName, PluginVersion)]
    [ModDependency("artef.twopointhospital.underpressure", ModDependencyAttribute.DependencyFlags.HardDependency)]
    public sealed class UnderPressureEditorPlugin : BaseModComponent
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure.editor";
        public const string PluginName = "Bajo Presión - Editor de escenarios";
        public const string PluginVersion = "0.1.1";

        internal static ModLogSource Log { get; private set; }
        internal static ConfigEntry<bool> ShowNewMapsSetting { get; private set; }
        internal static bool ShowNewMaps => ShowNewMapsSetting == null || ShowNewMapsSetting.Value;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ShowNewMapsSetting = Config.Bind(
                EditorLocalization.Get("editor.settings.career"),
                EditorLocalization.Get("editor.settings.show_new_maps"), true,
                EditorLocalization.Get("editor.settings.show_new_maps.description"));
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
