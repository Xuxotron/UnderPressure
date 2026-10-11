// Inicia el módulo independiente del editor de escenarios y registra sus parches.
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace UnderPressure.Editor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class UnderPressureEditorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure.editor";
        public const string PluginName = "Bajo Presión - Editor de escenarios";
        public const string PluginVersion = "0.1.1";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
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
