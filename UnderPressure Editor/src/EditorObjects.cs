// Updated: 2026-10-20 - Desactivar DynamicLayoutGroup nativo que duplica el ancho del catalogo de dos filas.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using FullInspector;
using HarmonyLib;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using TH20;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioEditorObjects.cs =====
// Updated: 2026-10-12
// Editor de escenarios: transparencia del suelo, filtro DLC y ocultacion reversible
// de la geometria fija combinada de las escenas mientras se editan sus objetos.
// Destino: UnderPressure Editor/src/ScenarioEditorObjects.cs
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
        private static readonly HashSet<RoomItemDefinition> SceneProvenanceChecked =
            new HashSet<RoomItemDefinition>(DefinitionReferenceComparer.Instance);
        private static readonly Dictionary<string, DLCItemDefinition> PacksByScene =
            new Dictionary<string, DLCItemDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> BaseScenes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly FieldInfo PrefabField =
            AccessTools.Field(typeof(RoomItemDefinition), "_prefab");
        private static Dictionary<string, HashSet<string>> _scenesByPrefab;

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
            SceneProvenanceChecked.Clear();
            PacksByScene.Clear();
            BaseScenes.Clear();
            if (options == null) return;

            var levels = new HashSet<LevelConfig>();
            var plots = 0;
            var references = 0;
            foreach (var option in options)
            {
                var level = option?.Level?.Instance;
                if (level == null || !levels.Add(level)) continue;
                var pack = level.GetRequiredDlcPack();
                var sceneName = NormalizeSceneName(level.SceneName);
                if (!string.IsNullOrEmpty(sceneName))
                {
                    if (pack == null) BaseScenes.Add(sceneName);
                    else PacksByScene[sceneName] = pack;
                }
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

        private static string NormalizeSceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return string.Empty;
            sceneName = sceneName.Replace('\\', '/');
            var slash = sceneName.LastIndexOf('/');
            if (slash >= 0) sceneName = sceneName.Substring(slash + 1);
            if (sceneName.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                sceneName = sceneName.Substring(0, sceneName.Length - 6);
            return sceneName;
        }

        private static Dictionary<string, HashSet<string>> LoadSceneProvenance()
        {
            if (_scenesByPrefab != null) return _scenesByPrefab;
            _scenesByPrefab = new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);
            const string resourceName =
                "UnderPressure.Editor.Data.LandscapeSceneProvenance.tsv";
            try
            {
                using (var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException(
                            "No se encontro el indice " + resourceName + ".");
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0 || line[0] == '#') continue;
                            var separator = line.IndexOf('\t');
                            if (separator <= 0 || separator >= line.Length - 1) continue;
                            var prefabName = line.Substring(0, separator);
                            var sceneName = NormalizeSceneName(
                                line.Substring(separator + 1));
                            if (!_scenesByPrefab.TryGetValue(prefabName, out var scenes))
                            {
                                scenes = new HashSet<string>(
                                    StringComparer.OrdinalIgnoreCase);
                                _scenesByPrefab.Add(prefabName, scenes);
                            }
                            scenes.Add(sceneName);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo cargar la procedencia de objetos exteriores: " +
                    exception);
            }
            return _scenesByPrefab;
        }

        private static bool RegisterSceneProvenance(RoomItemDefinition definition)
        {
            if (definition == null || !SceneProvenanceChecked.Add(definition))
                return Provenance.ContainsKey(definition);
            var prefab = PrefabField?.GetValue(definition) as GameObject;
            if (prefab == null ||
                !LoadSceneProvenance().TryGetValue(prefab.name, out var scenes))
                return false;

            foreach (var scene in scenes)
            {
                if (BaseScenes.Contains(scene))
                    RegisterProvenance(definition, null);
                else if (PacksByScene.TryGetValue(scene, out var pack))
                    RegisterProvenance(definition, pack);
            }
            return Provenance.ContainsKey(definition);
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
                RegisterSceneProvenance(definition);
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
// ===== END ORIGINAL: ScenarioEditorObjects.cs =====

// ===== BEGIN ORIGINAL: ScenarioEditorObjectLayout.cs =====
// Updated: 2026-10-13 - Iconos sin mover, fondo extendido arriba y scrollbar visible junto al borde de la paleta.
// Destino: UnderPressure Editor/src/ScenarioEditorObjectLayout.cs
namespace UnderPressure.Editor
{
    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Setup")]
    internal static class ScenarioEditorLandscapeLayoutPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(LandscapeObjectsMenu __instance)
        {
            if (!ScenarioEditorFlow.IsRunning || __instance == null) return;
            try
            {
                var content = AccessTools.Field(typeof(LandscapeObjectsMenu), "_listContent")
                    ?.GetValue(__instance) as GameObject;
                var input = AccessTools.Field(typeof(LandscapeObjectsMenu), "_searchBox")
                    ?.GetValue(__instance) as TMP_InputField;
                if (content == null) return;
                var rect = content.transform as RectTransform;
                if (rect == null) return;

                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;

                // El prefab conserva el layout nativo de una fila.
                // Sus 113952 unidades de ancho pisan las 51521 del Grid de dos filas.
                // Desactivarlo evita que ambos controladores escriban sobre el mismo Content.
                var nativeLayout = content.GetComponent<TH20.UI.DynamicLayoutGroup>();
                if (nativeLayout != null) nativeLayout.enabled = false;

                var horizontal = content.GetComponent<HorizontalLayoutGroup>();
                if (horizontal != null) horizontal.enabled = false;
                var vertical = content.GetComponent<VerticalLayoutGroup>();
                if (vertical != null) vertical.enabled = false;
                var grid = content.GetComponent<GridLayoutGroup>() ?? content.AddComponent<GridLayoutGroup>();
                grid.enabled = true;
                grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                grid.constraintCount = 2;
                grid.startAxis = GridLayoutGroup.Axis.Vertical;
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.childAlignment = TextAnchor.UpperLeft;
                grid.padding = new RectOffset(8, 0, 8, 8);
                grid.cellSize = new Vector2(105f, 88f);
                grid.spacing = new Vector2(7f, 5f);
                var fitter = content.GetComponent<ContentSizeFitter>() ?? content.AddComponent<ContentSizeFitter>();
                // En el commit 3007ecb el ancho lo controla VisualBoundsSizer.
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.enabled = true;

                var scroll = content.GetComponentInParent<ScrollRect>();
                if (scroll != null)
                {
                    scroll.horizontal = true;
                    scroll.vertical = false;
                    scroll.scrollSensitivity = 34f;
                    scroll.content = rect;
                    var scrollRect = scroll.transform as RectTransform;
                    if (scrollRect != null)
                    {
                        var layoutState = scrollRect.GetComponent<ScenarioCatalogLayoutState>() ??
                            scrollRect.gameObject.AddComponent<ScenarioCatalogLayoutState>();
                        // Aplicar el margen lateral una unica vez por instancia.
                        if (!layoutState.HorizontalInsetApplied)
                        {
                            var size = scrollRect.sizeDelta;
                            if (scrollRect.anchorMin.x == scrollRect.anchorMax.x)
                            {
                                size.x = Mathf.Max(260f, size.x - 219f);
                                scrollRect.sizeDelta = size;
                            }
                            else
                            {
                                var offset = scrollRect.offsetMax;
                                offset.x -= 219f;
                                scrollRect.offsetMax = offset;
                            }
                            layoutState.HorizontalInsetApplied = true;
                        }
                        SetHeightPreservingTop(scrollRect, 197f);
                        var viewport = scroll.viewport ?? rect.parent as RectTransform;
                        if (viewport != null && viewport != scrollRect)
                            SetHeightPreservingTop(viewport, 197f);
                        GrowBackdropOnly(__instance, viewport ?? scrollRect);
                        var sizer = content.GetComponent<ScenarioCatalogVisualBoundsSizer>() ??
                            content.AddComponent<ScenarioCatalogVisualBoundsSizer>();
                        sizer.Initialize(__instance, rect, grid, viewport ?? scrollRect);
                    }
                    RestoreScrollbar(scroll, __instance);
                }
                if (input != null)
                {
                    ScenarioEditorCatalogLoadGate.Initialise(__instance, input);
                    MoveSearchBelowPlots(input, __instance);
                }
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Catalogo exterior: " + ex);
            }
        }

        // No redimensionar el LandscapeObjectsMenu: altera tambien la
        // ubicacion nativa de los iconos. Se sube una fila completa y se
        // conserva un unico fondo extendido hasta el borde inferior.
        private static void GrowBackdropOnly(LandscapeObjectsMenu menu, RectTransform viewport)
        {
            var root = menu.transform as RectTransform;
            if (root == null) return;
            var extra = root.Find("UP_Backdrop_Extension") as RectTransform;
            if (extra == null)
            {
                root.anchoredPosition += new Vector2(0f, 93f);
                var background = new GameObject("UP_Backdrop_Extension",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                background.transform.SetParent(root, false);
                extra = (RectTransform)background.transform;
                var source = root.GetComponent<Image>();
                var target = background.GetComponent<Image>();
                if (source != null)
                {
                    target.sprite = source.sprite;
                    target.type = source.type;
                    target.color = source.color;
                    var sourceColor = source.color;
                    sourceColor.a = 0f;
                    source.color = sourceColor;
                }
                else target.color = new Color(.93f, .93f, .93f, .75f);
                target.raycastTarget = false;
                extra.SetAsFirstSibling();
            }
            var follower = extra.GetComponent<ScenarioCatalogBackdropFollower>() ??
                extra.gameObject.AddComponent<ScenarioCatalogBackdropFollower>();
            follower.Initialize(viewport, root);
            follower.Place();
        }

        private static void SetHeightPreservingTop(RectTransform rect, float height)
        {
            if (rect == null) return;
            var delta = height - rect.rect.height;
            if (delta <= .01f) return;
            if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                var size = rect.sizeDelta;
                size.y += delta;
                rect.sizeDelta = size;
                var position = rect.anchoredPosition;
                position.y -= delta * (1f - rect.pivot.y);
                rect.anchoredPosition = position;
                return;
            }
            var offset = rect.offsetMin;
            offset.y -= delta;
            rect.offsetMin = offset;
        }

        private static void RestoreScrollbar(ScrollRect scroll, LandscapeObjectsMenu menu)
        {
            var host = menu.transform as RectTransform;
            if (host == null) return;
            // Fuera del ScrollRect, NO dentro del Viewport ni de su Mask.
            // Cuando el contenido tiene varias páginas, ScrollRect actualiza
            // el handle automáticamente.
            var trackTransform = host.Find("UP_Catalog_HorizontalScrollbar") as RectTransform;
            if (trackTransform == null)
            {
                var track = new GameObject("UP_Catalog_HorizontalScrollbar",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
                track.transform.SetParent(host, false);
                trackTransform = (RectTransform)track.transform;
                track.GetComponent<Image>().color = new Color(.09f,.15f,.19f,.90f);
                var area = new GameObject("Sliding Area", typeof(RectTransform));
                area.transform.SetParent(track.transform, false);
                var areaRect = (RectTransform)area.transform;
                areaRect.anchorMin = Vector2.zero;
                areaRect.anchorMax = Vector2.one;
                areaRect.offsetMin = new Vector2(3f, 2f);
                areaRect.offsetMax = new Vector2(-3f,-2f);
                var handle = new GameObject("Handle",typeof(RectTransform),
                    typeof(CanvasRenderer),typeof(Image));
                handle.transform.SetParent(area.transform, false);
                var handleRect=(RectTransform)handle.transform;
                handleRect.anchorMin=Vector2.zero;
                handleRect.anchorMax=Vector2.one;
                handleRect.offsetMin=Vector2.zero;
                handleRect.offsetMax=Vector2.zero;
                var handleImage=handle.GetComponent<Image>();
                handleImage.color=new Color(.43f,.80f,.95f,1f);
                var bar=track.GetComponent<Scrollbar>();
                bar.direction=Scrollbar.Direction.LeftToRight;
                bar.targetGraphic=handleImage;
                bar.handleRect=handleRect;
            }
            // La ubicación NO depende del origen del LandscapeObjectsMenu:
            // se sigue la parte inferior REAL del viewport cada fotograma.
            trackTransform.anchorMin = trackTransform.anchorMax = new Vector2(.5f,.5f);
            trackTransform.pivot = new Vector2(.5f,.5f);
            trackTransform.SetAsLastSibling();
            trackTransform.gameObject.SetActive(true);
            var follower = trackTransform.GetComponent<ScenarioCatalogScrollbarFollower>() ??
                trackTransform.gameObject.AddComponent<ScenarioCatalogScrollbarFollower>();
            follower.Initialize(scroll, host);
            // Si el prefab ya tenía una barra oculta en su ScrollRect,
            // no dejarla solapada con la nueva.
            if (scroll.horizontalScrollbar != null &&
                scroll.horizontalScrollbar.transform != trackTransform)
                scroll.horizontalScrollbar.gameObject.SetActive(false);
            scroll.horizontalScrollbar=trackTransform.GetComponent<Scrollbar>();
            scroll.horizontalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalScrollbarSpacing=2f;
            if (scroll.viewport == null)
                scroll.viewport = scroll.content != null ?
                    scroll.content.parent as RectTransform : null;
            follower.Place();
        }

        private static void MoveSearchBelowPlots(TMP_InputField input, LandscapeObjectsMenu menu)
        {
            var inputRect = input.transform as RectTransform;
            var menuRect = menu.transform as RectTransform;
            if (inputRect == null || menuRect == null) return;
            var follower = input.GetComponent<ScenarioEditorSearchBelowPlots>();
            if (follower != null) return;
            var editMenu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            var plotRoot = AccessTools.Field(typeof(EditHospitalMenu), "_plotSelectionRoot")
                ?.GetValue(editMenu) as Transform;
            if (plotRoot == null) return;
            var plotScroll = plotRoot.GetComponentInParent<ScrollRect>();
            var viewport = plotScroll != null ? plotScroll.transform as RectTransform : plotRoot as RectTransform;
            if (viewport == null) return;
            // Mover el input real, no una copia: el filtro sigue conectado a la lista nativa.
            // Conservar el input bajo su menu nativo: si se mueve directamente
            // al Canvas quedaria visible incluso cuando se cerrara la paleta.
            inputRect.SetParent(menuRect, false);
            inputRect.anchorMin = inputRect.anchorMax = new Vector2(0f, 1f);
            inputRect.pivot = new Vector2(0f, 1f);
            inputRect.sizeDelta = new Vector2(88f, 27f);
            var labelObject = new GameObject("UP_SearchLabel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(inputRect.parent, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            if (input.textComponent != null)
            {
                label.font = input.textComponent.font;
                label.fontSize = Mathf.Min(input.textComponent.fontSize, 17f);
            }
            label.text = EditorLocalization.Get("editor.objects.search");
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            var showAll = CreateShowAllButton(menu, inputRect.parent);
            follower = input.gameObject.AddComponent<ScenarioEditorSearchBelowPlots>();
            follower.Configure(viewport, labelObject.transform as RectTransform, showAll);
            follower.Place();
        }

        private static RectTransform CreateShowAllButton(LandscapeObjectsMenu menu,
            Transform parent)
        {
            var editMenu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            var template = AccessTools.Field(typeof(EditHospitalMenu), "_objectsButton")
                ?.GetValue(editMenu) as Button;
            if (template == null) return null;
            var button = UnityEngine.Object.Instantiate(template, parent, false);
            button.gameObject.name = "UP Objects - Show All";
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(ScenarioEditorDockLayout.ToggleObjectsExpanded);
            button.interactable = true;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                // Este control será la entrada de un menú desplegable.
                text.text = EditorLocalization.Get("editor.objects.dropdown_arrow");
                text.enableAutoSizing = true;
                text.fontSizeMin = 14f;
                text.fontSizeMax = 18f;
            }
            var rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(82f, 27f);
            }
            button.gameObject.SetActive(true);
            return rect;
        }
    }

    internal sealed class ScenarioCatalogLayoutState : MonoBehaviour
    {
        internal bool HorizontalInsetApplied;
    }

    // La barra acompaña al viewport sin quedar dentro de su máscara.
    // Unity actualiza el tamaño del handle a través del ScrollRect nativo.
    internal sealed class ScenarioCatalogScrollbarFollower : MonoBehaviour
    {
        private ScrollRect _scroll;
        private RectTransform _host;
        private RectTransform _rect;
        private readonly Vector3[] _corners = new Vector3[4];
        internal void Initialize(ScrollRect scroll, RectTransform host)
        {
            _scroll = scroll;
            _host = host;
            _rect = transform as RectTransform;
        }
        internal void Place()
        {
            if (_scroll == null || _host == null || _rect == null) return;
            var viewport = _scroll.viewport ?? (_scroll.transform as RectTransform);
            if (viewport == null) return;
            viewport.GetWorldCorners(_corners);
            var left = _host.InverseTransformPoint(_corners[0]);
            var right = _host.InverseTransformPoint(_corners[3]);
            var width = Mathf.Max(80f, right.x - left.x - 20f);
            _rect.sizeDelta = new Vector2(width, 14f);
            var mid = (left.x + right.x) * .5f;
            _rect.localPosition = new Vector3(mid, left.y - 12f, 0f);
            // La visibilidad la controla el padre del catálogo; no se
            // desactiva este hijo desde su propio LateUpdate.
        }
        private void LateUpdate() { Place(); }
    }

    // El fondo acompaña únicamente al catálogo, sin invadir el panel de terreno.
    internal sealed class ScenarioCatalogBackdropFollower : MonoBehaviour
    {
        private RectTransform _viewport;
        private RectTransform _host;
        private RectTransform _rect;
        private readonly Vector3[] _corners = new Vector3[4];

        internal void Initialize(RectTransform viewport, RectTransform host)
        {
            _viewport = viewport;
            _host = host;
            _rect = transform as RectTransform;
        }

        internal void Place()
        {
            if (_viewport == null || _host == null || _rect == null) return;
            _viewport.GetWorldCorners(_corners);
            var bottomLeft = _host.InverseTransformPoint(_corners[0]);
            var topLeft = _host.InverseTransformPoint(_corners[1]);
            var topRight = _host.InverseTransformPoint(_corners[2]);
            const float lowerExtension = 117f;
            var bottom = bottomLeft.y - lowerExtension;
            _rect.anchorMin = _rect.anchorMax = new Vector2(.5f, .5f);
            _rect.pivot = new Vector2(.5f, .5f);
            _rect.sizeDelta = new Vector2(topRight.x - topLeft.x, topLeft.y - bottom);
            _rect.localPosition = new Vector3((topLeft.x + topRight.x) * .5f,
                (topLeft.y + bottom) * .5f, 0f);
        }

        private void LateUpdate() { Place(); }
    }

    // Conserva el GridLayoutGroup original de 2 filas. Este componente mide
    // el contenido real y registra las coordenadas que emplea ScrollRect.
    // NO cambia celdas, miniaturas, pivotes ni la escala del catalogo.
    internal sealed class ScenarioCatalogVisualBoundsSizer : MonoBehaviour
    {
        private static readonly FieldInfo PendingItemsField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_itemsToCreate");
        private LandscapeObjectsMenu _menu;
        private RectTransform _content;
        private RectTransform _viewport;
        private GridLayoutGroup _grid;
        private ScrollRect _scroll;
        private bool _dirty = true;
        private int _lastLoggedBucket = -1;
        private int _lastLoggedChildren = -1;
        private bool _initialLogPending;
        private readonly Vector3[] _corners = new Vector3[4];

        internal void Initialize(LandscapeObjectsMenu menu, RectTransform content,
            GridLayoutGroup grid, RectTransform viewport)
        {
            _menu = menu;
            _content = content;
            _grid = grid;
            _viewport = viewport;
            _scroll = content != null ? content.GetComponentInParent<ScrollRect>() : null;
            _dirty = true;
            _lastLoggedBucket = -1;
            _lastLoggedChildren = -1;
            _initialLogPending = false;
        }

        internal void MarkDirty() => _dirty = true;

        private void OnEnable()
        {
            _dirty = true;
            Canvas.willRenderCanvases += FitToVisibleChildren;
        }

        private void OnDisable() => Canvas.willRenderCanvases -= FitToVisibleChildren;
        private void OnDestroy() => Canvas.willRenderCanvases -= FitToVisibleChildren;
        private void OnTransformChildrenChanged() => _dirty = true;

        private void FitToVisibleChildren()
        {
            if (_content == null || _viewport == null || _grid == null ||
                !_content.gameObject.activeInHierarchy) return;

            // Mecanica existente del commit 3007ecb: conservada exactamente.
            if (_dirty)
            {
                _dirty = false;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                var maximumX = 0f;
                for (var i = 0; i < _content.childCount; ++i)
                {
                    var child = _content.GetChild(i) as RectTransform;
                    if (child == null || !child.gameObject.activeSelf) continue;
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                        _content, child);
                    maximumX = Mathf.Max(maximumX, bounds.max.x);
                }
                var targetWidth = Mathf.Max(_viewport.rect.width,
                    maximumX + _grid.padding.right);
                if (!Mathf.Approximately(_content.rect.width, targetWidth))
                    _content.SetSizeWithCurrentAnchors(
                        RectTransform.Axis.Horizontal, targetWidth);
            }

            // Esperar al render completado; el mismo Canvas puede reconstruir
            // el layout DESPUES de nuestra medicion. Solo 5 muestras por barrido.
            if (_scroll == null || _menu == null) return;
            var pending = PendingItemsField?.GetValue(_menu) as IList<RoomItemDefinition>;
            if (pending == null || pending.Count != 0 || _content.childCount == 0) return;
            var position = _scroll.horizontalNormalizedPosition;
            var bucket = Mathf.Clamp(Mathf.RoundToInt(position * 4f), 0, 4);
            if (_initialLogPending ||
                (bucket == _lastLoggedBucket && _lastLoggedChildren == _content.childCount))
                return;
            _lastLoggedBucket = bucket;
            _lastLoggedChildren = _content.childCount;
            _initialLogPending = true;
            StartCoroutine(LogAfterRender(bucket));
        }

        private System.Collections.IEnumerator LogAfterRender(int bucket)
        {
            yield return new WaitForEndOfFrame();
            _initialLogPending = false;
            if (_content == null || _viewport == null || _scroll == null ||
                !ScenarioEditorFlow.IsRunning) yield break;
            try
            {
                RectTransform first = null, last = null;
                int count = 0;
                for (int i = 0; i < _content.childCount; i++)
                {
                    var child = _content.GetChild(i) as RectTransform;
                    if (child == null || !child.gameObject.activeSelf) continue;
                    if (first == null) first = child;
                    last = child;
                    count++;
                }

                var actualContent = _scroll.content;
                var realViewport = _scroll.viewport != null ? _scroll.viewport :
                    _scroll.transform as RectTransform;
                var sb = new System.Text.StringBuilder(1800);
                sb.Append("CatalogoScroll DIAGNOSTICO [").Append(bucket)
                    .Append("/4], activos=").Append(count)
                    .Append(", normalizado=").Append(_scroll.horizontalNormalizedPosition.ToString("F4"))
                    .Append(", scroll.content==_listContent=")
                    .Append(ReferenceEquals(actualContent, _content)).AppendLine();
                AppendRect(sb, "_listContent", _content);
                AppendRect(sb, "scroll.content", actualContent);
                AppendRect(sb, "viewport pasado", _viewport);
                AppendRect(sb, "scroll.viewport real", realViewport);
                if (realViewport != null)
                {
                    sb.Append("bounds en viewport real: content=")
                        .Append(WorldXBounds(actualContent, realViewport))
                        .Append(", primero=").Append(WorldXBounds(first, realViewport))
                        .Append(", ultimo=").Append(WorldXBounds(last, realViewport))
                        .AppendLine();
                    if (actualContent != null)
                    {
                        var contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                            realViewport, actualContent);
                        sb.Append("ScrollRect contentBounds.size.x=")
                            .Append(contentBounds.size.x.ToString("F2"))
                            .Append(", viewport.rect.width=")
                            .Append(realViewport.rect.width.ToString("F2"))
                            .AppendLine();
                    }
                }
                sb.Append("Primero x=").Append(first != null ? first.anchoredPosition.x.ToString("F2") : "n/a")
                    .Append(", ultimo x=")
                    .Append(last != null ? last.anchoredPosition.x.ToString("F2") : "n/a")
                    .Append(", preferidoGrid=").Append(LayoutUtility.GetPreferredWidth(_content).ToString("F2"))
                    .AppendLine();
                sb.Append("Elementos de layout del contenido: ");
                foreach (var component in _content.GetComponents<Component>())
                {
                    var layout = component as ILayoutElement;
                    if (layout == null) continue;
                    sb.Append(component.GetType().Name)
                        .Append("{prio=").Append(layout.layoutPriority)
                        .Append(",min=").Append(layout.minWidth.ToString("F1"))
                        .Append(",pref=").Append(layout.preferredWidth.ToString("F1"))
                        .Append("} ");
                }
                UnderPressureEditorPlugin.Log?.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("CatalogoScroll diagnostico: " + ex);
            }
        }

        private static void AppendRect(System.Text.StringBuilder sb, string label,
            RectTransform rect)
        {
            if (rect == null) { sb.Append(label).Append("=NULL").AppendLine(); return; }
            sb.Append(label).Append(" path=").Append(rect.name)
                .Append(", rect.width=").Append(rect.rect.width.ToString("F2"))
                .Append(", sizeDelta=").Append(rect.sizeDelta)
                .Append(", localScale=").Append(rect.localScale)
                .Append(", lossyScale=").Append(rect.lossyScale)
                .Append(", anchors=").Append(rect.anchorMin).Append("->").Append(rect.anchorMax)
                .Append(", pivot=").Append(rect.pivot)
                .Append(", parent=").Append(rect.parent != null ? rect.parent.name : "none")
                .AppendLine();
        }

        // Proyeccion equivalente a los bounds utilizados por ScrollRect,
        // incluyendo escalas y posibles contenedores intermedios.
        private static string WorldXBounds(RectTransform rect, RectTransform relativeTo)
        {
            if (rect == null || relativeTo == null) return "NULL";
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            foreach (var point in corners)
            {
                var x = relativeTo.InverseTransformPoint(point).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }
            return "[" + min.ToString("F1") + "," + max.ToString("F1") + "]";
        }
    }

    [HarmonyPatch(typeof(LandscapeObjectListItem), "SetDefinition")]
    internal static class ScenarioEditorObjectPreviewAnglePatch
    {
        private static readonly FieldInfo CameraPitchField =
            AccessTools.Field(typeof(LandscapeObjectListItem), "_cameraPitch");

        private static void Prefix(LandscapeObjectListItem __instance)
        {
            if (ScenarioEditorFlow.IsRunning && __instance != null)
                CameraPitchField?.SetValue(__instance, -45f);
        }
    }

    [HarmonyPatch(typeof(LandscapeObjectListItem), "FrameCamera")]
    internal static class ScenarioEditorObjectPreviewYawPatch
    {
        private static readonly FieldInfo CameraRotationField =
            AccessTools.Field(typeof(LandscapeObjectListItem), "_rotation");

        private static void Prefix(LandscapeObjectListItem __instance, out float __state)
        {
            __state = float.NaN;
            if (!ScenarioEditorFlow.IsRunning || __instance == null || CameraRotationField == null)
                return;
            __state = (float)CameraRotationField.GetValue(__instance);
            CameraRotationField.SetValue(__instance, __state - 20f);
        }

        private static void Postfix(LandscapeObjectListItem __instance, float __state)
        {
            if (!float.IsNaN(__state) && __instance != null)
                CameraRotationField?.SetValue(__instance, __state);
        }
    }

    internal sealed class ScenarioEditorSearchBelowPlots : MonoBehaviour
    {
        private RectTransform _plotViewport;
        private RectTransform _label;
        private RectTransform _showAll;
        private RectTransform _input;
        private readonly Vector3[] _corners = new Vector3[4];

        internal void Configure(RectTransform plotViewport, RectTransform label,
            RectTransform showAll)
        {
            _plotViewport = plotViewport;
            _label = label;
            _showAll = showAll;
            _input = transform as RectTransform;
        }

        internal void Place()
        {
            if (_plotViewport == null || _input == null || _label == null) return;
            var parent = _input.parent as RectTransform;
            if (parent == null) return;
            _plotViewport.GetWorldCorners(_corners);
            // La banda de acciones mide 31 px y comienza 4 px bajo la lista.
            // Situar Buscar 39 px debajo permite que no solape Añadir/Eliminar.
            var origin = _corners[0] + parent.TransformVector(new Vector3(4f, -39f, 0f));
            _label.anchorMin = _label.anchorMax = new Vector2(0f, 1f);
            _label.pivot = new Vector2(0f, 1f);
            _label.sizeDelta = new Vector2(64f, 28f);
            _label.position = origin;
            _input.position = origin + parent.TransformVector(new Vector3(65f, 0f, 0f));
            if (_showAll != null)
                _showAll.position = origin + parent.TransformVector(new Vector3(158f, 0f, 0f));
        }

        private void LateUpdate() { Place(); }
        private void OnDestroy()
        {
            if (_label != null) UnityEngine.Object.Destroy(_label.gameObject);
            if (_showAll != null) UnityEngine.Object.Destroy(_showAll.gameObject);
        }
    }

    internal sealed class ScenarioEditorCatalogLoadGate : MonoBehaviour
    {
        private static readonly FieldInfo PendingItemsField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_itemsToCreate");
        private static readonly FieldInfo ItemsField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_items");
        private static readonly FieldInfo FilterField =
            AccessTools.Field(typeof(LandscapeObjectsMenu), "_filter");
        private readonly List<RoomItemDefinition> _emptyPending =
            new List<RoomItemDefinition>();
        private TMP_InputField _search;
        private bool _showAll;
        private int _lastFilteredCount = -1;
        private string _lastAppliedFilter;

        internal static void Initialise(LandscapeObjectsMenu menu, TMP_InputField search)
        {
            if (menu == null) return;
            var gate = menu.GetComponent<ScenarioEditorCatalogLoadGate>() ??
                       menu.gameObject.AddComponent<ScenarioEditorCatalogLoadGate>();
            gate._search = search;
            gate._showAll = true;
        }

        internal static void ShowAll(LandscapeObjectsMenu menu)
        {
            var gate = menu == null ? null : menu.GetComponent<ScenarioEditorCatalogLoadGate>();
            if (gate != null) gate._showAll = true;
        }

        internal bool CanLoad => _showAll ||
            (_search != null && !string.IsNullOrWhiteSpace(_search.text));

        internal static bool ShouldFilter(LandscapeObjectsMenu menu)
        {
            var gate = menu == null ? null : menu.GetComponent<ScenarioEditorCatalogLoadGate>();
            if (gate == null || ItemsField == null || FilterField == null) return true;
            var items = ItemsField.GetValue(menu) as IList<RoomItemDefinition>;
            var count = items?.Count ?? 0;
            var filter = FilterField.GetValue(menu) as string ?? string.Empty;
            if (count == gate._lastFilteredCount &&
                string.Equals(filter, gate._lastAppliedFilter, StringComparison.Ordinal)) return false;
            gate._lastFilteredCount = count;
            gate._lastAppliedFilter = filter;
            menu.GetComponent<ScenarioCatalogVisualBoundsSizer>()?.MarkDirty();
            return true;
        }

        internal static List<RoomItemDefinition> Suspend(LandscapeObjectsMenu menu)
        {
            var gate = menu == null ? null : menu.GetComponent<ScenarioEditorCatalogLoadGate>();
            if (gate == null || gate.CanLoad || PendingItemsField == null) return null;
            var pending = PendingItemsField.GetValue(menu) as List<RoomItemDefinition>;
            if (pending == null || pending.Count == 0) return null;
            PendingItemsField.SetValue(menu, gate._emptyPending);
            return pending;
        }

        internal static void Restore(LandscapeObjectsMenu menu,
            List<RoomItemDefinition> pending)
        {
            if (menu != null && pending != null && PendingItemsField != null)
                PendingItemsField.SetValue(menu, pending);
        }
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Filter")]
    internal static class ScenarioEditorCatalogFilterPatch
    {
        private static bool Prefix(LandscapeObjectsMenu __instance) =>
            !ScenarioEditorFlow.IsRunning || ScenarioEditorCatalogLoadGate.ShouldFilter(__instance);
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Update")]
    internal static class ScenarioEditorCatalogLazyLoadPatch
    {
        private static void Prefix(LandscapeObjectsMenu __instance,
            ref List<RoomItemDefinition> __state)
        {
            if (ScenarioEditorFlow.IsRunning)
                __state = ScenarioEditorCatalogLoadGate.Suspend(__instance);
        }

        private static void Postfix(LandscapeObjectsMenu __instance,
            List<RoomItemDefinition> __state) =>
            ScenarioEditorCatalogLoadGate.Restore(__instance, __state);
    }
}
// ===== END ORIGINAL: ScenarioEditorObjectLayout.cs =====

// ===== BEGIN ORIGINAL: ScenarioEditorObjectInteraction.cs =====
// Updated: 2026-10-14 - Ctrl: movimiento multiple nativo, cancela y restaura objetos sin modificar parcelas.
namespace UnderPressure.Editor
{
    internal sealed class ScenarioEditorObjectInteraction : MonoBehaviour
    {
        private static ScenarioEditorObjectInteraction _active;
        private static string _pendingSearch;
        private readonly Dictionary<RoomItemDefinition, string> _names =
            new Dictionary<RoomItemDefinition, string>();
        private static readonly MethodInfo NativeHighlightMultiple =
            AccessTools.Method(typeof(HighlightManager), "HighlightObjects", new[] { typeof(List<Renderer>) });
        private Level _level;
        private HospitalPlot _selection;
        private Vector2 _rightDown;
        private bool _rightClickPending;
        private static readonly FieldInfo CursorPlotField =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_hospitalPlot");
        private static readonly FieldInfo CursorLayerField =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_hospitalPlotLayer");
        private static readonly MethodInfo SetCursorFloorPlan =
            AccessTools.Method(typeof(CursorRoomItem), "SetFloorPlanEditing",
                new[] { typeof(FloorPlan), typeof(RoomFloorPlanVisual) });
        private static readonly FieldInfo CursorItemEditModeField =
            AccessTools.Field(typeof(CursorRoomItem), "_editMode");
        private static readonly FieldInfo LandscapeLayerField =
            AccessTools.Field(typeof(LandscapeRoomItem), "_layer");

        // Ctrl selecciona por el rectangulo horizontal, sin extraer objetos
        // de sus FloorPlans. La relacion instancia/parcela queda intacta.
        private readonly Dictionary<LandscapeRoomItem, HospitalPlot> _ctrlSelected =
            new Dictionary<LandscapeRoomItem, HospitalPlot>();
        private readonly HashSet<ScenarioEditorScene.SceneObject> _ctrlSceneSelected =
            new HashSet<ScenarioEditorScene.SceneObject>();
        private bool _ctrlDragging;
        private bool _ctrlHoverLocked;
        private Vector3 _ctrlStart;
        private Vector3 _ctrlEnd;
        private GameObject _ctrlRectangle;
        private LineRenderer _ctrlLine;
        private Material _ctrlMaterial;
        private HospitalPlotLayer _ctrlLayer;
        private bool _ctrlSceneLayer;

        // Identidad permanente del objeto durante el arrastre NATIVO. El juego
        // usa un solo BlueprintFloorPlan para mover, pero cada item retorna
        // a su FloorPlan original al confirmar o cancelar.
        private sealed class NativeMultiMember
        {
            internal HospitalPlot Owner;
            internal HospitalPlotLayer Layer;
            internal Vector3 OriginalWorld;
            internal Vector3 OriginalLocal;
            internal float OriginalRotation;
            internal HospitalPlotItem SourceRecord;
            internal int SourceRecordIndex;
        }

        private static readonly FieldInfo NativeDragFloor =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_dragFloorPlan");
        private static readonly FieldInfo NativeDragVisual =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_dragFloorPlanVisualisation");
        private static readonly FieldInfo NativeMultiItems =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_multiSelectItems");
        private readonly Dictionary<LandscapeRoomItem, NativeMultiMember> _nativeMulti =
            new Dictionary<LandscapeRoomItem, NativeMultiMember>();
        private CursorEditHospitalItem _nativeOwnerCursor;
        private BlueprintFloorPlan _nativeFloor;
        private bool _nativeMoveCancelled;
        private static readonly FieldInfo NativeMoveRotateControl =
            AccessTools.Field(typeof(CursorRoomMove), "_rotateControl");

        // El objeto original no pasa por StartItemEdit: solo se inicia la
        // colocacion NATIVA de otro objeto nuevo, en la parcela elegida.
        private LandscapeRoomItem _cloneSource;
        private HospitalPlot _cloneDestination;

        private sealed class NativeAltTransfer
        {
            internal CursorRoomItem Cursor;
            internal LandscapeRoomItem Item;
            internal HospitalPlot Source;
            internal HospitalPlot Destination;
            internal HospitalPlotLayer SourceLayer;
            internal HospitalPlotLayer DestinationLayer;
            internal Vector3 SourceLocalPosition;
            internal float SourceRotation;
            internal HospitalPlotItem SourceRecord;
            internal int SourceRecordIndex;
        }

        private NativeAltTransfer _pendingAlt;
        private NativeAltTransfer _activeAlt;

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            if (menu == null || level == null || !ScenarioEditorFlow.IsRunning) return;
            var component = menu.GetComponent<ScenarioEditorObjectInteraction>() ??
                menu.gameObject.AddComponent<ScenarioEditorObjectInteraction>();
            component.Configure(level);
        }

        private void Configure(Level level)
        {
            if (ReferenceEquals(_level, level)) return;
            Detach();
            _level = level;
            _active = this;
            _selection = level.WorldState?.HospitalPlots?.FirstOrDefault();
            level.HospitalEditEvents.OnSelectHospitalPlot += PlotSelected;
        }

        private void PlotSelected(HospitalPlot plot) { _selection = plot; }

        private void Detach()
        {
            if (_level != null) _level.HospitalEditEvents.OnSelectHospitalPlot -= PlotSelected;
            if (_nativeMulti.Count > 0) CompleteNativeMultiple(_nativeOwnerCursor, false, Vector3.zero, true);
            if (_activeAlt != null)
            {
                var transfer = _activeAlt;
                _activeAlt = null;
                RestoreAltTransfer(transfer, false);
            }
            ClearCtrlSelection();
            _cloneSource = null;
            _cloneDestination = null;
            _pendingAlt = null;
            _activeAlt = null;
            _level = null;
            _rightClickPending = false;
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private void OnEnable()
        {
            if (_level == null && ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Level != null)
                Configure(ScenarioEditorSession.Level);
        }
        private void OnDisable() { Detach(); }
        private void OnDestroy() { Detach(); }

        private bool Ready => _level != null && ScenarioEditorSession.Active &&
            ScenarioEditorFlow.IsRunning && ReferenceEquals(ScenarioEditorSession.Level, _level) &&
            !ScenarioEditorStorageMenu.IsDialogOpen;

        private static bool OverUi() => EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();

        private static bool CtrlDown => Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        internal static bool CtrlRectangleDragging => _active != null && _active._ctrlDragging;

        // El cursor original hace la seleccion, el highlight y StartItemEdit.
        // _hospitalPlot solo se sustituye durante SU CursorUpdate; el evento
        // OnSelectHospitalPlot NO se emite al mantener Mayus o Alt.
        internal static bool BeforeNativeCursor(CursorEditHospitalItem cursor,
            out HospitalPlot previousPlot)
        {
            previousPlot = null;
            var self = _active;
            if (ScenarioLandscape.IsActive) return false;
            if (self == null || !self.Ready || cursor == null) return true;
            if (self._nativeMulti.Count > 0) return false;
            if (self._rightClickPending &&
                Vector2.Distance(self._rightDown, Input.mousePosition) <= 5f) return false;
            if (CtrlDown)
            {
                // NO crear el BlueprintFloorPlan del juego: agruparia objetos
                // de varias parcelas bajo un solo propietario.
                self.UpdateCtrlRectangle();
                return false;
            }
            if (OverUi()) return true;
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (ScenarioEditorScene.IsSelected) return false;
            if (shift && Input.GetMouseButtonDown(0))
            {
                HospitalPlot pickedPlot;
                LandscapeRoomItem pickedItem;
                // Consumir SIEMPRE el clic Mayus: ni siquiera cuando no haya
                // objeto se debe coger accidentalmente el original.
                if (self.TryPickNative(Input.mousePosition, out pickedPlot, out pickedItem))
                    self.QueueNativeClone(pickedItem, self._selection ?? pickedPlot);
                return false;
            }
            if (!Input.GetMouseButtonDown(0)) return true;
            HospitalPlot owner;
            LandscapeRoomItem item;
            if (!self.TryPickNative(Input.mousePosition, out owner, out item) ||
                owner == null || CursorPlotField == null) return !alt;
            // El clic izquierdo no cambia de parcela. La parcela activa sólo
            // cambia mediante el clic derecho gestionado más abajo.
            if (!alt && !ReferenceEquals(owner, self._selection)) return false;

            var sourceLayer = ReadLayer(item);
            var records = owner.Definition?.GetItems(sourceLayer);
            var recordIndex = records == null ? -1 :
                records.FindIndex(record => record != null && record.Equals(item));
            if (recordIndex < 0)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Mover: no se encontro el registro exacto del objeto de origen.");
                return false;
            }
            self._pendingAlt = new NativeAltTransfer
            {
                Item = item,
                Source = owner,
                SourceLayer = sourceLayer,
                SourceLocalPosition = item.LocalPosition,
                SourceRotation = item.Rotation,
                SourceRecord = records[recordIndex],
                SourceRecordIndex = recordIndex
            };

            if (!alt || ReferenceEquals(owner, self._selection)) return true;
            previousPlot = CursorPlotField.GetValue(cursor) as HospitalPlot;
            CursorPlotField.SetValue(cursor, owner);
            return true;
        }

        internal static void AfterNativeCursor(CursorEditHospitalItem cursor,
            HospitalPlot previousPlot)
        {
            if (cursor != null && previousPlot != null && CursorPlotField != null)
                CursorPlotField.SetValue(cursor, previousPlot);
            _active?.ActivatePendingAltTransfer();
        }

        private void ActivatePendingAltTransfer()
        {
            var transfer = _pendingAlt;
            _pendingAlt = null;
            if (transfer == null || transfer.Item == null ||
                _level?.CursorManager == null ||
                !_level.CursorManager.TryGetActiveMode<CursorRoomItem>(out var roomCursor) ||
                !ReferenceEquals(roomCursor.RoomItem, transfer.Item)) return;
            try
            {
                var sourceRecords = transfer.Source.Definition.GetItems(transfer.SourceLayer);
                if (sourceRecords.Contains(transfer.SourceRecord))
                    sourceRecords.Remove(transfer.SourceRecord);
                SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(transfer.Source.Definition);
                transfer.Cursor = roomCursor;
                _activeAlt = transfer;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Mover: no se pudo iniciar el traslado del objeto: " + ex);
                RestoreAltTransfer(transfer, true);
            }
        }

        internal static bool BeforeAltPlace(CursorRoomItem cursor)
        {
            var self = _active;
            var transfer = self?._activeAlt;
            if (transfer == null || !ReferenceEquals(transfer.Cursor, cursor)) return true;
            var destination = self._selection;
            var destinationLayer = CursorEditHospital.HospitalPlotLayer;
            if (destination?.HospitalMap?.FloorPlan == null ||
                destination.HospitalMap.RoomVisual == null || SetCursorFloorPlan == null ||
                LandscapeLayerField == null) return false;
            try
            {
                transfer.Destination = destination;
                transfer.DestinationLayer = destinationLayer;
                SetCursorFloorPlan.Invoke(cursor, new object[]
                {
                    destination.HospitalMap.FloorPlan,
                    destination.HospitalMap.RoomVisual
                });
                LandscapeLayerField.SetValue(transfer.Item, destinationLayer);
                return true;
            }
            catch (Exception ex)
            {
                transfer.Destination = null;
                UnderPressureEditorPlugin.Log?.LogError(
                    "Mover: no se pudo asignar la parcela activa al soltar: " + ex);
                return false;
            }
        }

        internal static bool BeforeAltCancel(CursorRoomItem cursor, bool requestedByUser)
        {
            var self = _active;
            if (!requestedByUser || self == null) return true;
            if (self._activeAlt != null && ReferenceEquals(self._activeAlt.Cursor, cursor))
            {
                var transfer = self._activeAlt;
                self._activeAlt = null;
                self.RestoreAltTransfer(transfer, true);
                return false;
            }

            // Al cancelar con el boton derecho un objeto existente, el juego
            // destruye solo la instancia del cursor. No emite OnRoomItemDestroy
            // y por eso la definicion de la parcela conserva el objeto. En el
            // editor debe recorrer exactamente la misma eliminacion que Suprimir.
            if (!self.Ready || cursor?.RoomItem is not LandscapeRoomItem item ||
                CursorItemEditModeField == null ||
                Convert.ToInt32(CursorItemEditModeField.GetValue(cursor)) == 0)
                return true;

            var owner = item.FloorPlan?.HospitalMap?.Plot;
            var layer = ReadLayer(item);
            self._level.BuildEvents.OnRoomItemCancel?.Invoke(item, true);
            self._level.BuildEvents.OnRoomItemDestroy?.Invoke(item);
            if (owner != null)
            {
                ScenarioEditorScene.MarkPlotLayerModified(owner, layer);
                self._level.HospitalEditEvents.OnHospitalPlotUpdated?.Invoke(owner);
            }
            return false;
        }

        internal static void AfterAltPlace(CursorRoomItem cursor, bool placed)
        {
            var self = _active;
            var transfer = self?._activeAlt;
            if (transfer == null || !ReferenceEquals(transfer.Cursor, cursor) || !placed ||
                transfer.Destination == null) return;
            self._activeAlt = null;

            var item = transfer.Item;
            var sourceFloor = transfer.Source?.HospitalMap?.FloorPlan;
            var destinationFloor = transfer.Destination?.HospitalMap?.FloorPlan;
            if (item != null && destinationFloor != null)
            {
                // PlaceItem ya ha añadido la misma instancia al destino. Estas
                // comprobaciones cierran cualquier incoherencia residual sin
                // reconstruir ninguna parcela ni recorrer objetos ajenos.
                item.FloorPlan = destinationFloor;
                LandscapeLayerField.SetValue(item, transfer.DestinationLayer);
                if (!destinationFloor.LandscapeItems.Contains(item))
                    destinationFloor.AddItem(item);
                if (!ReferenceEquals(sourceFloor, destinationFloor) &&
                    sourceFloor != null && sourceFloor.LandscapeItems.Contains(item))
                    sourceFloor.LandscapeItems.Remove(item);

                var sourceRecords = transfer.Source.Definition.GetItems(transfer.SourceLayer);
                sourceRecords.Remove(transfer.SourceRecord);
                if (!transfer.Destination.Definition.Contains(item, destinationFloor,
                    transfer.DestinationLayer, default(Vector3)))
                {
                    transfer.Destination.Definition.AddItem(new HospitalPlotItem
                    {
                        Definition = SharedInstanceUtils.GetSharedInstance<RoomItemDefinition>(
                            item.Definition as RoomItemDefinition),
                        Position = item.LocalPosition,
                        Rotation = item.Rotation
                    }, transfer.DestinationLayer);
                }
                SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(transfer.Source.Definition);
                if (!ReferenceEquals(transfer.Source.Definition, transfer.Destination.Definition))
                    SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(
                        transfer.Destination.Definition);
            }
            ScenarioEditorScene.MarkPlotLayerModified(transfer.Source, transfer.SourceLayer);
            ScenarioEditorScene.MarkPlotLayerModified(transfer.Destination,
                transfer.DestinationLayer);
            var visual = item?.Visual?.GameObject;
            if (visual != null)
                visual.SetActive(transfer.Destination.IsVisible() &&
                    transfer.Destination.IsLayerVisible(transfer.DestinationLayer));
        }

        private void RestoreAltTransfer(NativeAltTransfer transfer, bool notifyCancel)
        {
            if (transfer?.Item == null || transfer.Source?.HospitalMap?.FloorPlan == null) return;
            try
            {
                var item = transfer.Item;
                item.FloorPlan = transfer.Source.HospitalMap.FloorPlan;
                item.LocalPosition = transfer.SourceLocalPosition;
                item.Rotation = transfer.SourceRotation;
                LandscapeLayerField.SetValue(item, transfer.SourceLayer);
                if (!transfer.Source.HospitalMap.FloorPlan.LandscapeItems.Contains(item))
                    transfer.Source.HospitalMap.FloorPlan.AddItem(item);

                var records = transfer.Source.Definition.GetItems(transfer.SourceLayer);
                if (!records.Contains(transfer.SourceRecord))
                    records.Insert(Mathf.Clamp(transfer.SourceRecordIndex, 0, records.Count),
                        transfer.SourceRecord);
                SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(transfer.Source.Definition);

                if (item.Visual != null)
                {
                    item.Visual.DisableAndDestroyEditingVisuals();
                    item.Visual.UpdateFrom(item, true, true, true, default(Vector3), 0f);
                    if (item.Visual.GameObject != null)
                        item.Visual.GameObject.SetActive(transfer.Source.IsVisible() &&
                            transfer.Source.IsLayerVisible(transfer.SourceLayer));
                }
                _level.BuildingLogic.StopEditingRoomObject(item);
                ScenarioEditorScene.MarkPlotLayerModified(transfer.Source, transfer.SourceLayer);
                if (notifyCancel) _level.BuildEvents.OnRoomItemCancel?.Invoke(item, true);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Mover: no se pudo restaurar el objeto cancelado: " + ex);
            }
        }

        private bool HasMultipleSelection =>
            _ctrlSelected.Count + _ctrlSceneSelected.Count >= 2;

        private void QueueNativeClone(LandscapeRoomItem original, HospitalPlot destination)
        {
            if (original?.Definition == null || destination?.HospitalMap?.FloorPlan == null ||
                destination.HospitalMap.RoomVisual == null) return;
            _cloneSource = original;
            _cloneDestination = destination;
            // No llamar a BuildEvents.StartItemEdit(original, ...): esa ruta
            // retira el item del FloorPlan y es la causa de que desaparezca.
            _level.HighlightManager.HighlightObject(original);
        }

        private void LateUpdate()
        {
            var original = _cloneSource;
            var destination = _cloneDestination;
            _cloneSource = null;
            _cloneDestination = null;
            if (original == null || destination == null || !Ready ||
                _level?.CursorManager == null ||
                !_level.CursorManager.IsModeActive<CursorEditHospitalItem>()) return;
            try
            {
                var config = _level.BuildingLogic.Configuration.RoomItemEditConfig;
                var next = new CursorRoomItem(_level.CursorManager, _level, config,
                    original.Definition, original.Rotation,
                    destination.HospitalMap.FloorPlan, destination.HospitalMap.RoomVisual,
                    null, false);
                _level.CursorManager.PushMode(next);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("Mayus: no se pudo iniciar la copia: " + ex);
            }
        }

        // El cursor del juego levanta y visualiza todos los objetos a la vez,
        // tal y como hace StartMultiSelectEdit() del juego original. Solo
        // sustituimos su lista de origen y el retorno a cada parcela.
        private bool BeginNativeMultiple()
        {
            if (!Ready || _ctrlSelected.Count == 0 ||
                _level.CursorManager == null ||
                !_level.CursorManager.TryGetActiveMode<CursorEditHospitalItem>(out var cursor))
                return false;
            var floor = NativeDragFloor?.GetValue(cursor) as BlueprintFloorPlan;
            var visual = NativeDragVisual?.GetValue(cursor) as BlueprintFloorPlanVisual;
            var nativeItems = NativeMultiItems?.GetValue(cursor) as List<LandscapeRoomItem>;
            if (floor == null || visual == null || nativeItems == null ||
                nativeItems.Count != 0 || floor.LandscapeItems.Count != 0)
                return false;

            var start = _ctrlStart.ToGridCoord();
            var end = _ctrlEnd.ToGridCoord();
            floor.Anchor = new GridCoord(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
            floor.Tiles = new bool[Mathf.Abs(end.X - start.X) + 1,
                Mathf.Abs(end.Y - start.Y) + 1];
            ArrayUtils.Populate<bool>(floor.Tiles, true);
            floor.RecalculateWalls();
            floor.ValidRoomSize = false;
            visual.CompletelyInvalid = true;
            _nativeFloor = floor;
            _nativeOwnerCursor = cursor;
            _nativeMoveCancelled = false;
            _nativeMulti.Clear();
            try
            {
                foreach (var pair in _ctrlSelected)
                {
                    var item = pair.Key;
                    var owner = pair.Value;
                    if (item == null || owner?.HospitalMap?.FloorPlan == null) continue;
                    var layer = ReadLayer(item);
                    var records = owner.Definition?.GetItems(layer);
                    var recordIndex = records == null ? -1 :
                        records.FindIndex(record => record != null && record.Equals(item));
                    if (recordIndex < 0) continue;
                    var world = item.WorldPosition;
                    _nativeMulti.Add(item, new NativeMultiMember
                    {
                        Owner = owner, Layer = layer,
                        OriginalWorld = world,
                        OriginalLocal = item.LocalPosition,
                        OriginalRotation = item.Rotation,
                        SourceRecord = records[recordIndex],
                        SourceRecordIndex = recordIndex
                    });

                    // El método nativo MoveItemToFloorPlan ejecuta AddToWorld
                    // sobre un BlueprintFloorPlan sin HospitalMap y falla para
                    // LandscapeRoomItem. En el plano temporal solo se necesita
                    // conservar la misma instancia y su posición mundial.
                    owner.HospitalMap.FloorPlan.RemoveItem(item);
                    item.FloorPlan = floor;
                    item.WorldPosition = world;
                    floor.LandscapeItems.Add(item);
                    if (item.Visual != null)
                    {
                        item.Visual.Destroy();
                        item.Visual = null;
                    }
                    nativeItems.Add(item);
                }
                if (_nativeMulti.Count == 0) return false;
                visual.UpdateFromRoom(floor, Vector3.zero, 0f);
                visual.SetWallsFloorVisible(false);
                visual.SetVisible(true);
                _level.CursorManager.PushMode(new CursorRoomMove(
                    _level.CursorManager, _level, _level.WorldState,
                    _level.BuildEvents, floor, visual, true));
                _ctrlSelected.Clear();
                _ctrlSceneSelected.Clear();
                return true;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ctrl: fallo al iniciar seleccion multiple nativa: " + ex);
                CompleteNativeMultiple(cursor, false, Vector3.zero, true);
                return false;
            }
        }

        // CursorRoomMove usa OnMoveRoomEnd(false, offset) tanto al COLOCAR
        // como al CANCELAR. Guardar la opcion Cancel del control nativo
        // permite restaurar todos los originales sin dar por hecho un guardado.
        internal static void TrackNativeMultiCancel(CursorControlRotatePlace control)
        {
            var self = _active;
            if (self == null || self._nativeMulti.Count == 0 ||
                control == null || self._level?.CursorManager == null ||
                !self._level.CursorManager.TryGetActiveMode<CursorRoomMove>(out var move) ||
                !ReferenceEquals(NativeMoveRotateControl?.GetValue(move), control)) return;
            self._nativeMoveCancelled = control.Cancel;
        }

        // Se llama desde el OnMoveRoomEnd original, antes de que el juego
        // intente devolver todos los objetos a UNA sola parcela.
        internal static bool InterceptNativeMultiple(CursorEditHospitalItem cursor,
            bool deleted, Vector3 cellOffset)
        {
            var self = _active;
            if (self == null || !ReferenceEquals(cursor, self._nativeOwnerCursor) ||
                self._nativeMulti.Count == 0) return false;
            self.CompleteNativeMultiple(cursor, deleted, cellOffset,
                self._nativeMoveCancelled);
            return true;
        }

        private void CompleteNativeMultiple(CursorEditHospitalItem cursor,
            bool deleted, Vector3 cellOffset, bool cancel)
        {
            var floor = _nativeFloor;
            var members = _nativeMulti.ToArray();
            // Liberar el modo antes de emitir eventos para impedir reentradas.
            _nativeMulti.Clear();
            _nativeOwnerCursor = null;
            _nativeFloor = null;
            _nativeMoveCancelled = false;
            cellOffset.y = 0f;
            var affected = new HashSet<HospitalPlot>();
            var nativeItems = cursor == null ? null :
                NativeMultiItems?.GetValue(cursor) as List<LandscapeRoomItem>;
            var destinationPlot = (!cancel && !deleted) ? _selection : null;
            var destinationLayer = CursorEditHospital.HospitalPlotLayer;
            try
            {
                foreach (var pair in members)
                {
                    var item = pair.Key;
                    var member = pair.Value;
                    var owner = member.Owner;
                    if (item == null || owner?.HospitalMap?.FloorPlan == null) continue;
                    var targetPlot = destinationPlot ?? owner;
                    var targetFloor = targetPlot?.HospitalMap?.FloorPlan;
                    if (targetFloor == null) targetFloor = owner.HospitalMap.FloorPlan;
                    var destination = (cancel || deleted) ? member.OriginalWorld :
                        item.WorldPosition + cellOffset;
                    floor?.LandscapeItems.Remove(item);
                    if (item.Visual != null)
                    {
                        item.Visual.Destroy();
                        item.Visual = null;
                    }
                    item.FloorPlan = targetFloor;
                    item.WorldPosition = destination;
                    LandscapeLayerField.SetValue(item,
                        (cancel || deleted) ? member.Layer : destinationLayer);
                    if (cancel || deleted)
                        item.Rotation = member.OriginalRotation;
                    targetFloor.AddItem(item);
                    if (!cancel && !deleted)
                    {
                        var sourceRecords = owner.Definition.GetItems(member.Layer);
                        sourceRecords.Remove(member.SourceRecord);
                        targetPlot.Definition.AddItem(new HospitalPlotItem
                        {
                            Definition = SharedInstanceUtils.GetSharedInstance<RoomItemDefinition>(
                                item.Definition as RoomItemDefinition),
                            Position = item.LocalPosition,
                            Rotation = item.Rotation
                        }, destinationLayer);
                        SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(owner.Definition);
                        if (!ReferenceEquals(owner.Definition, targetPlot.Definition))
                            SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(
                                targetPlot.Definition);
                        ScenarioEditorScene.MarkPlotLayerModified(owner, member.Layer);
                        ScenarioEditorScene.MarkPlotLayerModified(targetPlot, destinationLayer);
                    }
                    affected.Add(owner);
                    affected.Add(targetPlot);
                }
                nativeItems?.Clear();
                foreach (var owner in affected)
                {
                    owner?.HospitalMap?.RoomVisual?.CreateRoomItems(
                        Vector3.zero, 0f, null);
                }
                if (!cancel && !deleted && destinationPlot != null)
                    foreach (var pair in members)
                    {
                        var visualObject = pair.Key?.Visual?.GameObject;
                        if (visualObject != null)
                            visualObject.SetActive(destinationPlot.IsVisible() &&
                                destinationPlot.IsLayerVisible(destinationLayer));
                    }
                if (floor != null)
                {
                    // Vaciar y ocultar siempre el plano temporal: al cancelar
                    // tambien hay que eliminar las visuales de previsualizacion.
                    var visual = cursor == null ? null :
                        NativeDragVisual?.GetValue(cursor) as BlueprintFloorPlanVisual;
                    visual?.UpdateFromRoom(floor, Vector3.zero, 0f);
                    visual?.SetVisible(false);
                }
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ctrl: fallo al devolver objetos a sus parcelas: " + ex);
            }
        }

        // Ctrl conserva la parcela y la capa activas. Solo mantiene el
        // resaltado del objeto situado bajo el cursor.
        private void UpdateCtrlObjectHover()
        {
            if (!CtrlDown || _ctrlDragging || _nativeMulti.Count > 0 || _ctrlHoverLocked ||
                Input.GetMouseButton(0) || OverUi() ||
                _level?.CursorManager == null ||
                !_level.CursorManager.IsModeActive<CursorEditHospitalItem>() ||
                _level.CursorManager.IsModeActive<CursorRoomItem>()) return;
            HospitalPlot owner;
            LandscapeRoomItem item;
            if (!TryPickNative(Input.mousePosition, out owner, out item) || owner == null) return;
            if (_ctrlSelected.Count == 0 && _ctrlSceneSelected.Count == 0)
                _level.HighlightManager.HighlightObject(item);
        }

        private void UpdateCtrlRectangle()
        {
            if (_nativeMulti.Count > 0) return;
            if (_ctrlDragging && !CtrlDown)
            {
                // El usuario puede soltar Ctrl antes que el boton izquierdo.
                // Finalizar la seleccion con el rectangulo que estaba dibujando.
                if (ScenarioEditorScene.GetGroundPoint(Input.mousePosition, out var end))
                    _ctrlEnd = end;
                CollectCtrlItems();
                FinishCtrlRectangle();
                return;
            }
            if (_level?.CursorManager == null ||
                !_level.CursorManager.IsModeActive<CursorEditHospitalItem>() ||
                _level.CursorManager.IsModeActive<CursorRoomItem>()) return;
            if (!_ctrlDragging)
            {
                if (!Input.GetMouseButtonDown(0) || OverUi()) return;
                if (!ScenarioEditorScene.GetGroundPoint(Input.mousePosition, out _ctrlStart)) return;
                _ctrlEnd = _ctrlStart;
                _ctrlSceneLayer = ScenarioEditorScene.IsSelected;
                var menu = GetComponent<EditHospitalMenu>();
                var dropdown = menu == null ? null :
                    AccessTools.Field(typeof(EditHospitalMenu), "_plotLayerDropdown")?.GetValue(menu)
                    as TMP_Dropdown;
                _ctrlLayer = (HospitalPlotLayer)Mathf.Clamp(dropdown?.value ?? 0, 0, 2);
                // Cada rectangulo define una seleccion nueva, sin acumular
                // objetos fuera de sus bordes ni mezclar capas.
                _ctrlSelected.Clear();
                _ctrlSceneSelected.Clear();
                _ctrlHoverLocked = true;
                _ctrlDragging = true;
                BeginCtrlRectangle();
            }
            if (!_ctrlDragging) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ClearCtrlSelection();
                return;
            }
            Vector3 ground;
            if (ScenarioEditorScene.GetGroundPoint(Input.mousePosition, out ground))
                _ctrlEnd = ground;
            UpdateCtrlRectangleVisual();
            CollectCtrlItems();
            HighlightCtrlSelection();
            if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                FinishCtrlRectangle();
        }

        private void FinishCtrlRectangle()
        {
            _ctrlDragging = false;
            DestroyCtrlRectangle();
            if (_ctrlSelected.Count + _ctrlSceneSelected.Count == 0)
            {
                ClearCtrlSelection();
                return;
            }
            // Soltar el rectangulo inicia DIRECTAMENTE CursorRoomMove nativo.
            // La seleccion de Escenario (objetos externos a FloorPlan) aun no
            // participa en ese cursor; se conserva su resaltado sin moverlos.
            if (!_ctrlSceneLayer && !BeginNativeMultiple())
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Ctrl: no se pudo iniciar CursorRoomMove sobre la seleccion.");
        }

        private void BeginCtrlRectangle()
        {
            DestroyCtrlRectangle();
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            _ctrlMaterial = new Material(shader);
            _ctrlRectangle = new GameObject("UP Ctrl - rectangulo sobre el terreno");
            _ctrlLine = _ctrlRectangle.AddComponent<LineRenderer>();
            _ctrlLine.material = _ctrlMaterial;
            _ctrlLine.useWorldSpace = true;
            _ctrlLine.loop = true;
            _ctrlLine.positionCount = 4;
            _ctrlLine.widthMultiplier = 0.065f;
            _ctrlLine.startColor = _ctrlLine.endColor =
                new Color(0.22f, 0.7f, 1f, 0.95f);
        }

        private void UpdateCtrlRectangleVisual()
        {
            if (_ctrlLine == null) return;
            var y = Mathf.Max(_ctrlStart.y, _ctrlEnd.y) + 0.12f;
            _ctrlLine.SetPosition(0, new Vector3(_ctrlStart.x, y, _ctrlStart.z));
            _ctrlLine.SetPosition(1, new Vector3(_ctrlEnd.x, y, _ctrlStart.z));
            _ctrlLine.SetPosition(2, new Vector3(_ctrlEnd.x, y, _ctrlEnd.z));
            _ctrlLine.SetPosition(3, new Vector3(_ctrlStart.x, y, _ctrlEnd.z));
        }

        private void CollectCtrlItems()
        {
            // No acumular: un objeto que sale del rectangulo DEJA de estar
            // seleccionado en ese mismo fotograma.
            _ctrlSelected.Clear();
            _ctrlSceneSelected.Clear();
            var minX = Mathf.Min(_ctrlStart.x, _ctrlEnd.x);
            var maxX = Mathf.Max(_ctrlStart.x, _ctrlEnd.x);
            var minZ = Mathf.Min(_ctrlStart.z, _ctrlEnd.z);
            var maxZ = Mathf.Max(_ctrlStart.z, _ctrlEnd.z);
            if (_ctrlSceneLayer)
            {
                if (!ScenarioEditorScene.Visible) return;
                foreach (var scene in ScenarioEditorScene.SceneObjects)
                    if (scene?.Visual != null && scene.Visual.activeInHierarchy &&
                        IntersectsXZ(scene.Visual, minX, maxX, minZ, maxZ))
                        _ctrlSceneSelected.Add(scene);
                return;
            }
            var plots = _level?.WorldState?.HospitalPlots;
            if (plots == null) return;
            foreach (var plot in plots)
            {
                if (plot == null || !plot.IsVisible() || !plot.IsLayerVisible(_ctrlLayer)) continue;
                var items = plot.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (item == null || ReadLayer(item) != _ctrlLayer) continue;
                    var visual = item.Visual?.GameObject;
                    if (visual == null || !visual.activeInHierarchy)
                        continue;
                    if (IntersectsXZ(visual, minX, maxX, minZ, maxZ))
                        _ctrlSelected.Add(item, plot);
                }
            }
        }

        private static bool IntersectsXZ(GameObject visual, float minX, float maxX,
            float minZ, float maxZ)
        {
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                if (b.max.x >= minX && b.min.x <= maxX &&
                    b.max.z >= minZ && b.min.z <= maxZ) return true;
            }
            // Un prefab sin renderer tambien puede tener un pivote valido.
            var p = visual.transform.position;
            return p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;
        }

        private void HighlightCtrlSelection()
        {
            if (_level?.HighlightManager == null || NativeHighlightMultiple == null) return;
            var renderers = new List<Renderer>();
            foreach (var pair in _ctrlSelected)
            {
                if (pair.Value == null || !pair.Value.IsVisible() ||
                    pair.Key?.Visual?.GameObject == null ||
                    !pair.Key.Visual.GameObject.activeInHierarchy) continue;
                foreach (var renderer in pair.Key.Visual.GameObject.GetComponentsInChildren<Renderer>())
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        renderers.Add(renderer);
            }
            foreach (var scene in _ctrlSceneSelected)
                if (scene?.Visual != null)
                    foreach (var renderer in scene.Visual.GetComponentsInChildren<Renderer>())
                        if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                            renderers.Add(renderer);
            if (renderers.Count > 0)
                NativeHighlightMultiple.Invoke(_level.HighlightManager,
                    new object[] { renderers });
            else if (_ctrlDragging)
                HighlightRendererProxy.Instance.Clear();
        }

        private void DestroyCtrlRectangle()
        {
            if (_ctrlRectangle != null) UnityEngine.Object.Destroy(_ctrlRectangle);
            if (_ctrlMaterial != null) UnityEngine.Object.Destroy(_ctrlMaterial);
            _ctrlRectangle = null;
            _ctrlLine = null;
            _ctrlMaterial = null;
        }

        internal static void ClearCurrentSelection()
        {
            if (_active != null) _active.ClearCtrlSelection();
        }

        private void ClearCtrlSelection()
        {
            _ctrlDragging = false;
            _ctrlHoverLocked = false;
            _ctrlSelected.Clear();
            _ctrlSceneSelected.Clear();
            DestroyCtrlRectangle();
        }

        private bool TryPickNative(Vector2 mouse, out HospitalPlot owner,
            out LandscapeRoomItem selected)
        {
            owner = null;
            selected = null;
            var camera = Camera.main;
            var plots = _level?.WorldState?.HospitalPlots;
            if (camera == null || plots == null) return false;
            var ray = camera.ScreenPointToRay(mouse);
            var nearest = float.PositiveInfinity;
            foreach (var plot in plots)
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (!plot.IsVisible() || item?.Visual == null || item.Visual.GameObject == null ||
                        !item.Visual.GameObject.activeInHierarchy) continue;
                    float distance;
                    if (!item.Visual.RayCast(ray, out distance) || distance >= nearest) continue;
                    owner = plot;
                    selected = item;
                    nearest = distance;
                }
            }
            return selected != null;
        }

        private void Update()
        {
            if (_level == null && ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Level != null)
                Configure(ScenarioEditorSession.Level);
            if (!Ready) return;
            RefreshSearch();
            if (ScenarioLandscape.IsActive) return;

            if (!CtrlDown && !_ctrlDragging) _ctrlHoverLocked = false;
            UpdateCtrlObjectHover();
            if (_ctrlDragging || (CtrlDown && !OverUi())) UpdateCtrlRectangle();
            // Mayus clona y Alt roba, pero ambos deben mostrar siempre el
            // contorno del objeto alcanzado aunque pertenezca a otra parcela o capa.
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                 Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                !CtrlDown && !OverUi() &&
                _level.CursorManager.IsModeActive<CursorEditHospitalItem>())
            {
                HospitalPlot owner;
                LandscapeRoomItem item;
                if (TryPickNative(Input.mousePosition, out owner, out item))
                    _level.HighlightManager.HighlightObject(item);
            }
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) &&
                (_ctrlSelected.Count != 0 || _ctrlSceneSelected.Count != 0))
                HighlightCtrlSelection();
            // Un nuevo clic fuera de Ctrl deja paso a la seleccion nativa.
            if (!CtrlDown && !_ctrlDragging && _nativeMulti.Count == 0 &&
                !HasMultipleSelection && Input.GetMouseButtonDown(0) && !OverUi())
                ClearCtrlSelection();
            if (!CtrlDown && !_ctrlDragging && _nativeMulti.Count == 0 &&
                HasMultipleSelection && Input.GetMouseButtonDown(0) && !OverUi() &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                // Cuando se pincha fuera del grupo, dejar que el cursor nativo
                // seleccione el objeto por separado.
                HospitalPlot owner;
                LandscapeRoomItem hit;
                var onGroup = TryPickNative(Input.mousePosition, out owner, out hit) &&
                    _ctrlSelected.ContainsKey(hit);
                if (!onGroup && _ctrlSceneLayer && Camera.main != null)
                {
                    var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    foreach (var scene in _ctrlSceneSelected)
                        if (scene?.Visual != null &&
                            RayPickVisual(scene.Visual, ray, out var distance))
                        {
                            onGroup = true;
                            break;
                        }
                }
                if (!onGroup) ClearCtrlSelection();
            }
            // No se crean highlights. Se llama al postproceso NATIVO, incluso
            // para la decoracion global, cuyos prefabs no son RoomItem.
            if (ScenarioEditorScene.IsSelected && !CtrlDown && !_ctrlDragging && !OverUi())
            {
                ScenarioEditorScene.SceneObject scene;
                HospitalPlot owner;
                object item;
                RoomItemDefinition definition;
                if (TryPickForScene(out scene, out owner, out item, out definition))
                {
                    if (item is ICursorSelectable selectable)
                        _level.HighlightManager.HighlightObject(selectable);
                    else if (scene?.Visual != null)
                    {
                        var renderers = scene.Visual.GetComponentsInChildren<Renderer>()
                            .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToList();
                        if (renderers.Count > 0 && NativeHighlightMultiple != null)
                            NativeHighlightMultiple.Invoke(_level.HighlightManager,
                                new object[] { renderers });
                    }
                }
            }
            if (Input.GetMouseButtonDown(1))
            {
                _rightDown = Input.mousePosition;
                _rightClickPending = true;
            }
            if (_rightClickPending && Input.GetMouseButton(1) &&
                Vector2.Distance(_rightDown, Input.mousePosition) > 5f)
                _rightClickPending = false;
            if (_rightClickPending && Input.GetMouseButtonUp(1))
            {
                if (Vector2.Distance(_rightDown, Input.mousePosition) <= 5f &&
                    TryPickForScene(out var scene, out var owner, out var item, out var definition) &&
                    definition != null)
                {
                    if (owner != null && item != null)
                    {
                        if (!ReferenceEquals(owner, _selection))
                            _level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(owner);
                        _level.HospitalEditEvents.OnSelectHospitalPlotLayer?.Invoke(ReadLayer(item));
                    }
                    SetSearch(GetObjectName(definition));
                }
                _rightClickPending = false;
            }
        }

        private static IList Items(HospitalPlot plot) =>
            plot?.HospitalMap?.FloorPlan?.LandscapeItems as IList;

        private static object ReadMember(object obj, string name)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var property = t.GetProperty(name, flags);
                if (property?.GetGetMethod(true) != null)
                    try { return property.GetValue(obj, null); } catch { }
                foreach (var candidate in new[] { name,
                    "_" + char.ToLowerInvariant(name[0]) + name.Substring(1),
                    "<" + name + ">k__BackingField" })
                {
                    var field = t.GetField(candidate, flags);
                    if (field != null) try { return field.GetValue(obj); } catch { }
                }
            }
            return null;
        }

        private static HospitalPlotLayer ReadLayer(object item)
        {
            var value = ReadMember(item, "Layer");
            return value is HospitalPlotLayer layer ? layer : HospitalPlotLayer.Base;
        }

        // Conserva el formato de retorno que utiliza ScenarioEditorScene.
        // Seleccion EXACTAMENTE igual que el cursor nativo para las parcelas.
        internal static bool TryPickForScene(out ScenarioEditorScene.SceneObject scene,
            out HospitalPlot owner, out object item, out RoomItemDefinition definition)
        {
            scene = null;
            owner = null;
            item = null;
            definition = null;
            var self = _active;
            if (self == null || !self.Ready || Camera.main == null) return false;
            LandscapeRoomItem picked;
            var native = self.TryPickNative(Input.mousePosition, out owner, out picked);
            float nativeDistance = float.PositiveInfinity;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (native && picked != null)
                picked.Visual.RayCast(ray, out nativeDistance);
            float sceneDistance = float.PositiveInfinity;
            foreach (var candidate in ScenarioEditorScene.SceneObjects)
            {
                float distance;
                if (candidate?.Visual == null ||
                    !RayPickVisual(candidate.Visual, ray, out distance) ||
                    distance >= sceneDistance) continue;
                scene = candidate;
                sceneDistance = distance;
            }
            if (scene != null && sceneDistance < nativeDistance)
            {
                owner = null;
                item = null;
                definition = scene.Definition;
                return true;
            }
            scene = null;
            if (!native || picked == null) return false;
            item = picked;
            definition = picked.Definition as RoomItemDefinition;
            return definition != null;
        }

        internal static bool TryPickNativeForScene(out HospitalPlot owner, out object item,
            out RoomItemDefinition definition)
        {
            ScenarioEditorScene.SceneObject scene;
            return TryPickForScene(out scene, out owner, out item, out definition) &&
                owner != null;
        }

        internal static bool RayPickVisual(GameObject go, Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            if (go == null || !go.activeInHierarchy) return false;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || !filter.gameObject.activeInHierarchy) continue;
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var local = new Ray(filter.transform.InverseTransformPoint(ray.origin),
                    filter.transform.InverseTransformVector(ray.direction));
                float boundDistance;
                if (!mesh.bounds.IntersectRay(local, out boundDistance)) continue;
                try
                {
                    var vertices = mesh.vertices;
                    var triangles = mesh.triangles;
                    for (var i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        float at;
                        if (!RayIntersectsTriangle(local, vertices[triangles[i]],
                            vertices[triangles[i + 1]], vertices[triangles[i + 2]], out at)) continue;
                        if (at >= 0f && at < distance) distance = at;
                    }
                }
                catch (UnityException) { }
            }
            return distance < float.PositiveInfinity;
        }

        private static bool RayIntersectsTriangle(Ray ray, Vector3 a, Vector3 b,
            Vector3 c, out float distance)
        {
            distance = 0f;
            var edge1 = b - a;
            var edge2 = c - a;
            var p = Vector3.Cross(ray.direction, edge2);
            var det = Vector3.Dot(edge1, p);
            if (Mathf.Abs(det) < 0.000001f) return false;
            var inverse = 1f / det;
            var tvec = ray.origin - a;
            var u = Vector3.Dot(tvec, p) * inverse;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(tvec, edge1);
            var v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }

        internal static GameObject ReadNativeVisual(object item)
        {
            var visual = ReadMember(item, "Visual");
            return ReadMember(visual, "GameObject") as GameObject;
        }

        internal static Vector3? ReadNativePosition(object item)
        {
            var value = ReadMember(item, "LocalPosition");
            return value is Vector3 position ? position : (Vector3?)null;
        }

        internal static bool RemoveNativeAfterSceneTransfer(HospitalPlot owner, object item)
        {
            var objects = Items(owner);
            if (objects == null || !objects.Contains(item)) return false;
            var visual = ReadNativeVisual(item);
            var layer = ReadLayer(item);
            var position = ReadNativePosition(item);
            var definition = ReadMember(item, "Definition") as RoomItemDefinition;
            // La definición y el FloorPlan deben reflejar la misma transferencia.
            var saved = owner.Definition?.GetItems(layer);
            if (saved != null && definition != null && position.HasValue)
            {
                var matches = -1;
                for (var i = saved.Count - 1; i >= 0; --i)
                {
                    var placement = saved[i];
                    if (placement?.Definition?.Instance == null ||
                        !ReferenceEquals(placement.Definition.Instance, definition) ||
                        (placement.Position - position.Value).sqrMagnitude > 0.0001f)
                        continue;
                    matches = i;
                    break;
                }
                // Si la definicion conserva una posicion distinta, detener el
                // traslado antes de eliminar la instancia visible.
                if (matches >= 0) saved.RemoveAt(matches);
                else if (saved.Any(placement => placement?.Definition != null &&
                            ReferenceEquals(placement.Definition.Instance, definition)))
                    return false;
            }
            objects.Remove(item);
            ScenarioEditorScene.MarkPlotLayerModified(owner, layer);
            if (visual != null) UnityEngine.Object.Destroy(visual);
            var level = _active?._level;
            level?.HospitalEditEvents.OnHospitalPlotUpdated?.Invoke(owner);
            return true;
        }

        private static string SafePrefabName(RoomItemDefinition definition)
        {
            try { return definition?.GetPrefab(0)?.name ?? ""; }
            catch { return ""; }
        }

        private string GetObjectName(RoomItemDefinition definition)
        {
            if (definition == null) return "";
            if (_names.TryGetValue(definition, out var cached)) return cached;
            // Obtener el texto traducido que tambien usa la paleta. El objeto
            // NameLocalised puede tener diferentes implementaciones segun DLC.
            var value = ReadMember(definition, "NameLocalised");
            var text = ReadMember(value, "Translation") as string ??
                ReadMember(value, "Text") as string ??
                ReadMember(value, "Value") as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                foreach (var methodName in new[] { "GetLocalised", "GetLocalized",
                    "GetLocalisedText", "GetLocalizedText", "GetTranslation" })
                {
                    try
                    {
                        var method = value == null ? null : AccessTools.Method(value.GetType(), methodName,
                            Type.EmptyTypes);
                        if (method == null || method.ReturnType != typeof(string)) continue;
                        text = method.Invoke(value, null) as string;
                        if (!string.IsNullOrWhiteSpace(text)) break;
                    }
                    catch { }
                }
            }
            if (string.IsNullOrWhiteSpace(text))
                text = ReadMember(definition, "DisplayName") as string ??
                    ReadMember(definition, "Name") as string;
            if (string.IsNullOrWhiteSpace(text))
                text = !string.IsNullOrWhiteSpace(definition.DebugTag)
                    ? definition.DebugTag : SafePrefabName(definition);
            return _names[definition] = (text ?? "").Trim();
        }

        private static TMP_InputField FindSearch()
        {
            TMP_InputField fallback = null;
            foreach (var menu in Resources.FindObjectsOfTypeAll<LandscapeObjectsMenu>())
            {
                if (menu == null || !menu.gameObject.scene.IsValid()) continue;
                var field = AccessTools.Field(typeof(LandscapeObjectsMenu), "_searchBox");
                var input = field?.GetValue(menu) as TMP_InputField ??
                    menu.GetComponentInChildren<TMP_InputField>(true);
                if (input == null) continue;
                if (menu.gameObject.activeInHierarchy) return input;
                if (fallback == null) fallback = input;
            }
            return fallback;
        }

        private static void SetSearch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _pendingSearch = name;
            var input = FindSearch();
            if (input == null || !input.gameObject.activeInHierarchy)
            {
                // Abrir la paleta real; escribir en un input desactivado no
                // actualiza los resultados y parecia que el clic no funcionaba.
                var menu = _active == null ? null : _active.GetComponent<EditHospitalMenu>();
                var button = menu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                    "_objectsButton")?.GetValue(menu) as Button;
                if (button != null && button.interactable)
                    button.onClick.Invoke();
            }
            RefreshSearch();
        }

        internal static void RefreshSearch()
        {
            if (string.IsNullOrWhiteSpace(_pendingSearch) || !ScenarioEditorFlow.IsRunning) return;
            var input = FindSearch();
            if (input == null || !input.gameObject.activeInHierarchy) return;
            input.text = _pendingSearch;
            input.onValueChanged.Invoke(input.text);
            input.ForceLabelUpdate();
            _pendingSearch = null;
        }
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioEditorObjectInteractionSetupPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level __0) =>
            ScenarioEditorObjectInteraction.Attach(__instance, __0);
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "StartEdit")]
    internal static class ScenarioEditorObjectInteractionBeginPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active) return;
            var menu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            if (menu != null)
                ScenarioEditorObjectInteraction.Attach(menu, ScenarioEditorSession.Level);
        }
    }

    [HarmonyPatch(typeof(CursorEditHospitalItem), "CursorUpdate")]
    internal static class ScenarioEditorCrossPlotPickupPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorEditHospitalItem __instance,
            out HospitalPlot __state) =>
            ScenarioEditorObjectInteraction.BeforeNativeCursor(__instance, out __state);

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CursorEditHospitalItem __instance,
            HospitalPlot __state) =>
            ScenarioEditorObjectInteraction.AfterNativeCursor(__instance, __state);
    }

    [HarmonyPatch(typeof(CursorRoomItem), "CancelItem")]
    internal static class ScenarioEditorSingleObjectAltCancelPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorRoomItem __instance, bool __0) =>
            ScenarioEditorObjectInteraction.BeforeAltCancel(__instance, __0);
    }

    [HarmonyPatch(typeof(CursorRoomItem), "PlaceItem")]
    internal static class ScenarioEditorSingleObjectAltPlacedPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorRoomItem __instance, ref bool __result)
        {
            if (ScenarioEditorObjectInteraction.BeforeAltPlace(__instance)) return true;
            __result = false;
            return false;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CursorRoomItem __instance, bool __result) =>
            ScenarioEditorObjectInteraction.AfterAltPlace(__instance, __result);
    }

    // Detectar la cancelacion en el propio control nativo de colocar/rotar.
    // Se ejecuta dentro de CursorRoomMove.CursorUpdate, antes de OnMoveRoomEnd.
    [HarmonyPatch(typeof(CursorControlRotatePlace), "Update")]
    internal static class ScenarioEditorNativeMultiCancelPatch
    {
        private static void Postfix(CursorControlRotatePlace __instance) =>
            ScenarioEditorObjectInteraction.TrackNativeMultiCancel(__instance);
    }

    // Intercepta SOLO el fin de nuestro CursorRoomMove multiple y evita que
    // el juego entregue todos los objetos a la parcela actualmente elegida.
    [HarmonyPatch(typeof(CursorEditHospitalItem), "OnMoveRoomEnd")]
    internal static class ScenarioEditorNativeMultiReturnPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorEditHospitalItem __instance,
            bool deleted, Vector3 cellOffset) =>
            !ScenarioEditorObjectInteraction.InterceptNativeMultiple(
                __instance, deleted, cellOffset);
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Setup")]
    internal static class ScenarioEditorPickSearchSetupPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorObjectInteraction.RefreshSearch();
    }

    // El juego aplica su contorno en HighlightRenderer.Render mediante _Color.
    // Cambiamos SOLO el RGB del color nativo, sin crear geometria ni materiales.
    [HarmonyPatch(typeof(HighlightRenderer), "Render")]
    internal static class ScenarioEditorNativeOutlineColorPatch
    {
        private static readonly ConstructorInfo ColorConstructor =
            AccessTools.Constructor(typeof(Color),
                new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo TintMethod =
            AccessTools.Method(typeof(ScenarioEditorNativeOutlineColorPatch), nameof(Tint));
        private static readonly ConstructorInfo VectorConstructor =
            AccessTools.Constructor(typeof(Vector3),
                new[] { typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo FullRangeMethod =
            AccessTools.Method(typeof(ScenarioEditorNativeOutlineColorPatch), nameof(FullRange));

        private static Color Tint(Color native)
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active) return native;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                return new Color(1f, 0.1f, 0.85f, native.a);
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return new Color(0.12f, 0.46f, 1f, native.a);
            return native;
        }

        // _MousePosInvRange.z es el multiplicador radial del resaltado nativo.
        // Cero elimina el degradado sin alterar el shader ni generar siluetas.
        private static Vector3 FullRange(Vector3 native)
        {
            if (ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Active)
                native.z = 0f;
            return native;
        }

        // El radio que usa el juego para RECORTAR los renderers se obtiene al
        // multiplicar el tamano de la pantalla por HighlightKeyholeSize.
        // Modificamos ese valor en el propio metodo nativo, sin acceder a
        // PostProcessEffectRenderer<T>.settings (DLL opcional del proyecto).
        private static readonly MethodInfo FullSizeMethod =
            AccessTools.Method(typeof(ScenarioEditorNativeOutlineColorPatch), nameof(FullSize));

        private static float FullSize(float native)
        {
            return ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Active
                ? native * 40f : native;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool waitingForKeyholeMultiply = false;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is FieldInfo field &&
                    field.Name == "HighlightKeyholeSize")
                    waitingForKeyholeMultiply = true;

                yield return instruction;

                // Ensanchar tambien el recorte nativo de la camara: quitar
                // solo el degradado del shader dejaba extremos sin contorno.
                if (waitingForKeyholeMultiply && instruction.opcode == OpCodes.Mul)
                {
                    waitingForKeyholeMultiply = false;
                    if (FullSizeMethod != null)
                        yield return new CodeInstruction(OpCodes.Call, FullSizeMethod);
                }
                if (ColorConstructor != null && TintMethod != null &&
                    instruction.opcode == OpCodes.Newobj &&
                    Equals(instruction.operand, ColorConstructor))
                    yield return new CodeInstruction(OpCodes.Call, TintMethod);
                if (VectorConstructor != null && FullRangeMethod != null &&
                    instruction.opcode == OpCodes.Newobj &&
                    Equals(instruction.operand, VectorConstructor))
                    yield return new CodeInstruction(OpCodes.Call, FullRangeMethod);
            }
        }
    }
}
// ===== END ORIGINAL: ScenarioEditorObjectInteraction.cs =====
