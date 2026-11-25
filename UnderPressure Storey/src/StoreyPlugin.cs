using System.Reflection;
using HarmonyLib;
using UnderPressure.LoaderRuntime;

namespace UnderPressure.Storey
{
    [ModPlugin(PluginGuid, PluginName, PluginVersion)]
    [ModDependency("artef.twopointhospital.underpressure", ModDependencyAttribute.DependencyFlags.HardDependency)]
    public sealed class StoreyPlugin : BaseModComponent
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure.storey";
        public const string PluginName = "Bajo Presión - Segunda planta";
        public const string PluginVersion = "1.2";

        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            StoreyApi.Clear();
        }
    }
}
