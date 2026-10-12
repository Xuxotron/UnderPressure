// Updated: 2026-10-12
// Editor de escenarios: transparencia del suelo, filtro DLC y ocultacion reversible
// de la geometria fija combinada de las escenas mientras se editan sus objetos.
// Destino: UnderPressure Editor/src/ScenarioEditorObjects.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioEditorObjects
    {
        // Solo modifica el color con que se PINTA una celda nueva. Su RGB conserva
        // exactamente el tipo original (Floor/Window/Pillar/etc.). HospitalMap
        // clasifica las superficies por RGB, no por alfa.
        internal static void MakePaintTransparent(ref Color color)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning) return;
            if (color.a > 0f) color.a = 0.5f;
        }

        private sealed class DlcProvenance
        {
            internal bool UsedByBase;
            internal readonly Dictionary<uint, DLCItemDefinition> Packs =
                new Dictionary<uint, DLCItemDefinition>();
        }

        private sealed class DefinitionReferenceComparer :
            IEqualityComparer<RoomItemDefinition>
        {
            internal static readonly DefinitionReferenceComparer Instance =
                new DefinitionReferenceComparer();

            public bool Equals(RoomItemDefinition left, RoomItemDefinition right) =>
                ReferenceEquals(left, right);

            public int GetHashCode(RoomItemDefinition value) =>
                value == null ? 0 : RuntimeHelpers.GetHashCode(value);
        }

        // Las definiciones exteriores no garantizan un GUID exclusivo: varias
        // pueden compartir Guid.Empty. La instancia cargada por SharedInstance
        // es la identidad real que tambien entrega LandscapeItemDatabase.
        private static readonly Dictionary<RoomItemDefinition, DlcProvenance> Provenance =
            new Dictionary<RoomItemDefinition, DlcProvenance>(
                DefinitionReferenceComparer.Instance);

        // Bajo Presion Base es otra DLL. No referenciarla directamente permite
        // que el Editor funcione de manera independiente. La busqueda es perezosa
        // y se repite si Base aun no estaba cargado en el primer intento.
        private static MethodInfo _baseDlcDisabled;

        private static System.Reflection.MethodInfo FindBaseDlcDisabled()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("UnderPressure.DLCControl", false);
                if (type == null) continue;
                var method = type.GetMethod("IsDisabled",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    null, new[] { typeof(uint) }, null);
                if (method != null) return method;
            }
            return null;
        }

        private static bool DisabledFromBase(uint appId)
        {
            if (appId == 0) return false;
            if (_baseDlcDisabled == null) _baseDlcDisabled = FindBaseDlcDisabled();
            if (_baseDlcDisabled == null) return false;
            try { return (bool)_baseDlcDisabled.Invoke(null, new object[] { appId }); }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Filtro DLC: no pudo consultar Base: " + exception.Message);
                return false;
            }
        }

        internal static void BuildDlcProvenance(IEnumerable<SandboxLevelOption> options)
        {
            Provenance.Clear();
            if (options == null) return;

            var levels = new HashSet<LevelConfig>();
            var plots = 0;
            var references = 0;
            foreach (var option in options)
            {
                var level = option?.Level?.Instance;
                if (level == null || !levels.Add(level)) continue;
                var pack = level.GetRequiredDlcPack();
                var config = level.GetWorldStateConfig();
                if (config?.HospitalPlots == null) continue;

                foreach (var plotWrapper in config.HospitalPlots)
                {
                    var plot = plotWrapper?.Instance;
                    if (plot == null) continue;
                    ++plots;
                    foreach (HospitalPlotLayer layer in Enum.GetValues(typeof(HospitalPlotLayer)))
                    {
                        var items = plot.GetItems(layer);
                        if (items == null) continue;
                        foreach (var item in items)
                        {
                            var definition = item?.Definition?.Instance;
                            if (definition == null) continue;
                            RegisterProvenance(definition, pack);
                            ++references;
                        }
                    }
                }
            }

            var baseItems = 0;
            var dlcOnlyItems = 0;
            foreach (var value in Provenance.Values)
            {
                if (value.UsedByBase) ++baseItems;
                else if (value.Packs.Count > 0) ++dlcOnlyItems;
            }
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Procedencia del catalogo exterior: " + levels.Count + " niveles, " +
                plots + " parcelas y " + references + " referencias; " +
                baseItems + " objetos usados por Base y " + dlcOnlyItems +
                " usados exclusivamente por niveles DLC.");
        }

        private static void RegisterProvenance(
            RoomItemDefinition definition, DLCItemDefinition levelPack)
        {
            if (!Provenance.TryGetValue(definition, out var provenance))
            {
                provenance = new DlcProvenance();
                Provenance.Add(definition, provenance);
            }

            if (levelPack == null)
            {
                provenance.UsedByBase = true;
                return;
            }
            if (levelPack.AppID != 0)
                provenance.Packs[levelPack.AppID] = levelPack;
        }

        private static bool IsPackAvailable(DLCItemDefinition pack)
        {
            return pack != null && DLCUtils.IsDLCInstalled(pack) &&
                   !DisabledFromBase(pack.AppID);
        }

        private static bool IsUnavailable(RoomItemDefinition definition)
        {
            var required = definition?.DlcPackRequired;
            var dlc = required?.Instance;
            if (dlc != null) return !IsPackAvailable(dlc);

            if (definition == null ||
                !Provenance.TryGetValue(definition, out var provenance) ||
                provenance.UsedByBase || provenance.Packs.Count == 0)
                return false;

            // Un objeto compartido por varios DLC se conserva si al menos uno
            // de esos paquetes sigue disponible.
            foreach (var pack in provenance.Packs.Values)
                if (IsPackAvailable(pack)) return false;
            return true;
        }

        internal static void FilterUnavailableDlc(ref List<RoomItemDefinition> items)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning || items == null)
                return;

            if (Provenance.Count == 0)
            {
                var options = ScenarioEditorSession.Level?.App?
                    .SandboxSettingsConfig?.LevelOptions;
                BuildDlcProvenance(options);
            }
            if (_baseDlcDisabled == null) _baseDlcDisabled = FindBaseDlcDisabled();

            var removed = 0;
            var inferred = 0;
            var matched = 0;
            var untagged = 0;
            var tagged = 0;
            for (var i = items.Count - 1; i >= 0; --i)
            {
                var definition = items[i];
                if (definition == null)
                {
                    items.RemoveAt(i);
                    ++removed;
                    continue;
                }

                if (definition.DlcPackRequired?.Instance == null) ++untagged;
                else ++tagged;
                if (Provenance.ContainsKey(definition)) ++matched;
                if (!IsUnavailable(definition)) continue;
                if (definition.DlcPackRequired?.Instance == null) ++inferred;
                items.RemoveAt(i);
                ++removed;
            }

            UnderPressureEditorPlugin.Log?.LogInfo(
                "Catalogo exterior: " + items.Count + " disponibles; " + removed +
                " excluidos por DLC (" + inferred + " por procedencia de nivel); " +
                matched + " vinculados a parcelas; " +
                tagged + " con DLC declarado, " +
                untagged + " sin DLC declarado. " +
                "Comprobacion independiente de Base: " +
                (_baseDlcDisabled != null ? "activa" : "no encontrada") + ".");
        }
    }

    [HarmonyPatch(typeof(HospitalMapTile), nameof(HospitalMapTile.GetColor))]
    internal static class ScenarioEditorPaintAlphaPatch
    {
        private static void Postfix(ref Color __result) =>
            ScenarioEditorObjects.MakePaintTransparent(ref __result);
    }

    [HarmonyPatch(typeof(WorldState), nameof(WorldState.GetLandscapeItems))]
    internal static class ScenarioEditorLandscapeDlcPatch
    {
        // Argumento 0 del metodo original: out List<RoomItemDefinition> items.
        private static void Postfix(ref List<RoomItemDefinition> __0) =>
            ScenarioEditorObjects.FilterUnavailableDlc(ref __0);
    }

    internal static class ScenarioEditorFixedGeometry
    {
        // Los meshes "Combined Mesh (root scene) N" son resultados del static batching
        // de Unity. El .unity original conserva MeshRenderer individuales bajo
        // LandscapeItems_Base: no hay que modificar ni descombinar esos assets.
        // En el modo editor el juego crea ademas LandscapeRoomItem editables.
        // Solo ocultamos los renderizadores ESTATICOS de la escena original.
        private const string OriginalLandscapeRootName = "LandscapeItems_Base";

        private static readonly Dictionary<Renderer, bool> OriginalRenderers =
            new Dictionary<Renderer, bool>();
        private static readonly Dictionary<Collider, bool> OriginalColliders =
            new Dictionary<Collider, bool>();

        private static Transform _sceneRoot;
        private static bool _scanned;
        private static bool _scanning;
        private static bool _reportedMissing;

        internal static void Scan(Level level, bool force = false)
        {
            if (_scanning || !ScenarioEditorSession.Active ||
                !ReferenceEquals(ScenarioEditorSession.Level, level) ||
                level?.WorldState?.HospitalPlots == null) return;
            if (_scanned && !force) return;

            _scanning = true;
            try
            {
                if (_sceneRoot == null)
                {
                    var originalRoot = GameObject.Find(OriginalLandscapeRootName);
                    if (originalRoot == null)
                    {
                        if (!_reportedMissing)
                        {
                            _reportedMissing = true;
                            UnderPressureEditorPlugin.Log?.LogWarning(
                                "Editor: no se encontro la jerarquia ORIGINAL " +
                                OriginalLandscapeRootName +
                                ". No se ocultara geometria ajena al escenario.");
                        }
                        return;
                    }
                    _sceneRoot = originalRoot.transform;
                }

                // Evita ocultar los clones editables del HospitalMap incluso en el
                // caso de que el juego los coloque dentro de la misma jerarquia.
                var editable = new HashSet<Renderer>();
                foreach (var plot in level.WorldState.HospitalPlots)
                {
                    var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                    if (items == null) continue;
                    foreach (var item in items)
                    {
                        var visual = item?.Visual?.GameObject;
                        if (visual == null) continue;
                        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                            if (renderer != null) editable.Add(renderer);
                    }
                }

                var hiddenRenderers = 0;
                var hiddenColliders = 0;
                var bakedRenderers = 0;
                var candidates = _sceneRoot.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var renderer in candidates)
                {
                    if (renderer == null || editable.Contains(renderer)) continue;
                    // Evita scripts, personajes, previews y decoracion instanciada
                    // durante la sesion: solo los renderizadores estaticos originales.
                    if (!renderer.gameObject.isStatic && !renderer.isPartOfStaticBatch)
                        continue;

                    if (renderer.isPartOfStaticBatch) ++bakedRenderers;
                    if (!OriginalRenderers.ContainsKey(renderer))
                        OriginalRenderers.Add(renderer, renderer.enabled);
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        ++hiddenRenderers;
                    }

                    // Una malla invisible con un collider activo puede seguir
                    // interceptando el cursor. Solo tocamos los colliders del
                    // mismo GameObject que el renderer original, no el terreno.
                    foreach (var collider in renderer.GetComponents<Collider>())
                    {
                        if (collider == null) continue;
                        if (!OriginalColliders.ContainsKey(collider))
                            OriginalColliders.Add(collider, collider.enabled);
                        if (!collider.enabled) continue;
                        collider.enabled = false;
                        ++hiddenColliders;
                    }
                }

                _scanned = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Editor: decoracion original ocultada en " + OriginalLandscapeRootName +
                    ": " + hiddenRenderers + " renderizadores, " + hiddenColliders +
                    " colliders; " + bakedRenderers + " renderizadores con static batching. " +
                    "Los LandscapeRoomItem editables siguen visibles. " +
                    "La malla combinada original NO se ha modificado.");

                if (hiddenRenderers == 0 && OriginalRenderers.Count == 0)
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Editor: se encontro " + OriginalLandscapeRootName +
                        " pero ningun renderer estatico. Revisar la escena cargada.");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Error al ocultar la geometria fija del escenario: " + exception);
            }
            finally
            {
                _scanning = false;
            }
        }

        internal static void Restore()
        {
            foreach (var state in OriginalRenderers)
                if (state.Key != null) state.Key.enabled = state.Value;
            foreach (var state in OriginalColliders)
                if (state.Key != null) state.Key.enabled = state.Value;
            OriginalRenderers.Clear();
            OriginalColliders.Clear();
            _sceneRoot = null;
            _scanned = false;
            _scanning = false;
            _reportedMissing = false;
        }
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "StartEdit")]
    internal static class ScenarioEditorFixedGeometryStartPatch
    {
        // El cursor nativo ha creado los LandscapeRoomItem editables.
        // Ocultamos ahora la decoracion fija de la escena de Unity.
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HospitalEditEvents __instance)
        {
            if (!ScenarioEditorSession.Active) return;
            ScenarioEditorFixedGeometry.Scan(ScenarioEditorSession.Level, true);
        }
    }

    [HarmonyPatch(typeof(HospitalMap), nameof(HospitalMap.Build))]
    internal static class ScenarioEditorFixedGeometryRebuildPatch
    {
        // Si la escena original aun no estaba disponible al comenzar,
        // intentamos localizarla al reconstruir una parcela. No se vuelve
        // a recorrer la geometria despues de una ocultacion satisfactoria.
        private static void Postfix()
        {
            if (!ScenarioEditorSession.Active) return;
            ScenarioEditorFixedGeometry.Scan(ScenarioEditorSession.Level);
        }
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "EndEdit")]
    internal static class ScenarioEditorFixedGeometryExitPatch
    {
        // Antes de que el editor restituya las definiciones originales.
        [HarmonyPriority(Priority.First)]
        private static void Prefix() => ScenarioEditorFixedGeometry.Restore();
    }
}
