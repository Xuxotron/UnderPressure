using System.Reflection;
using HarmonyLib;
using UnderPressure.LoaderRuntime;
using UnderPressure.LoaderRuntime.Logging;

namespace UnderPressure.Storey
{
    [ModPlugin(PluginGuid, PluginName, PluginVersion)]
    [ModDependency("artef.twopointhospital.underpressure",
        ModDependencyAttribute.DependencyFlags.HardDependency)]
    [ModDependency("artef.twopointhospital.underpressure.editor",
        ModDependencyAttribute.DependencyFlags.HardDependency)]
    public sealed class StoreyPlugin : BaseModComponent
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure.storey";
        public const string PluginName = "Bajo Presión - Segunda planta";
        public const string PluginVersion = "1.2";
        internal static ModLogSource Log { get; private set; }
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            StoreyRuntime.Register();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(PluginName + " " + PluginVersion + " cargado.");
        }

        private void OnDestroy()
        {
            StoreyRuntime.Destroy(true);
            StoreyRuntime.Unregister();
            UnderPressure.StoreyMapRegistry.Clear();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
