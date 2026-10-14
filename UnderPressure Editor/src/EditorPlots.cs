// Updated: 2026-10-14 - Agrupacion estructural del Editor desde GitHub beb96a79.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using FullInspector;
using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using TH20;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioEditorSession.cs =====
// Updated: 2026-10-13 - Correccion CS1955 de Count() mediante System.Linq
// Aisla los recursos originales, fuerza la carga del nivel desde sus definiciones y protege la visibilidad de las parcelas.
// Destino: UnderPressure Editor/src/ScenarioEditorSession.cs
namespace UnderPressure.Editor
{
    internal static class ScenarioEditorSession
    {
        private sealed class OriginalPlot
        {
            internal HospitalPlot Plot;
            internal Texture2D Texture;
            internal List<HospitalPlotItem> BaseItems;
            internal List<HospitalPlotItem> BuiltItems;
            internal List<HospitalPlotItem> UnbuiltItems;
        }

        private static readonly System.Reflection.FieldInfo WorldConfigField =
            AccessTools.Field(typeof(WorldState), "_config");
        private static readonly System.Reflection.FieldInfo PlotBaseField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "Items");
        private static readonly System.Reflection.FieldInfo PlotBuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "BuiltItems");
        private static readonly System.Reflection.FieldInfo PlotUnbuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "UnbuiltItems");
        private static readonly List<OriginalPlot> Originals = new List<OriginalPlot>();
        private static Level _level;
        private static WorldState.Config _worldConfig;
        private static bool _previousBaseLandscape;

        internal static bool Active => _level != null;
        internal static Level Level => _level;

        internal static void Begin(Level level)
        {
            if (level == null || !ScenarioEditorFlow.IsRunning) return;
            if (ReferenceEquals(level, _level)) return;
            if (_level != null) Restore();
            _level = level;
            try
            {
                if (WorldConfigField != null)
                {
                    _worldConfig = WorldConfigField.GetValue(level.WorldState) as WorldState.Config;
                    if (_worldConfig != null)
                    {
                        _previousBaseLandscape = _worldConfig.createBaseLandscapeItems;
                        _worldConfig.createBaseLandscapeItems = true;
                    }
                }

                // Es esencial duplicar no solo la textura: el editor cambia tambien
                // las listas y las posiciones de los HospitalPlotItem originales.
                foreach (var plot in level.WorldState.HospitalPlots)
                {
                    if (plot?.Definition == null || plot.Definition.FloorImage == null) continue;
                    var definition = plot.Definition;
                    var original = new OriginalPlot
                    {
                        Plot = plot,
                        Texture = definition.FloorImage,
                        BaseItems = PlotBaseField?.GetValue(definition) as List<HospitalPlotItem>,
                        BuiltItems = PlotBuiltField?.GetValue(definition) as List<HospitalPlotItem>,
                        UnbuiltItems = PlotUnbuiltField?.GetValue(definition) as List<HospitalPlotItem>
                    };
                    Originals.Add(original);
                    var cloned = new Texture2D(original.Texture.width, original.Texture.height,
                        TextureFormat.RGBA32, false)
                    {
                        name = "UP_EDITOR_" + original.Texture.name,
                        hideFlags = HideFlags.DontSave,
                        filterMode = original.Texture.filterMode,
                        wrapMode = original.Texture.wrapMode,
                        anisoLevel = original.Texture.anisoLevel
                    };
                    cloned.SetPixels32(original.Texture.GetPixels32());
                    cloned.Apply(false, false);
                    definition.FloorImage = cloned;
                    PlotBaseField?.SetValue(definition, CloneItems(original.BaseItems));
                    PlotBuiltField?.SetValue(definition, CloneItems(original.BuiltItems));
                    PlotUnbuiltField?.SetValue(definition, CloneItems(original.UnbuiltItems));
                }
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Sesion aislada: " + Originals.Count + " parcelas duplicadas en memoria.");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo aislar el escenario original: " + exception);
                Restore();
                throw;
            }
        }

        private static List<HospitalPlotItem> CloneItems(List<HospitalPlotItem> source)
        {
            var copy = new List<HospitalPlotItem>();
            if (source == null) return copy;
            foreach (var item in source)
            {
                if (item == null) continue;
                copy.Add(new HospitalPlotItem
                {
                    Definition = item.Definition,
                    Position = item.Position,
                    Rotation = item.Rotation
                });
            }
            return copy;
        }

        internal static void Restore()
        {
            foreach (var original in Originals)
            {
                if (original.Plot?.Definition == null) continue;
                var definition = original.Plot.Definition;
                var cloned = definition.FloorImage;
                definition.FloorImage = original.Texture;
                PlotBaseField?.SetValue(definition, original.BaseItems);
                PlotBuiltField?.SetValue(definition, original.BuiltItems);
                PlotUnbuiltField?.SetValue(definition, original.UnbuiltItems);
                if (cloned != null && !ReferenceEquals(cloned, original.Texture))
                    UnityEngine.Object.Destroy(cloned);
            }
            Originals.Clear();
            if (_worldConfig != null)
                _worldConfig.createBaseLandscapeItems = _previousBaseLandscape;
            _worldConfig = null;
            _level = null;
        }

        // El movimiento nativo solo desplaza los pixeles de FloorImage. Es necesario
        // aplicar el mismo delta a la decoracion antes de que HospitalMap.Build la regenere.
        internal static void ShiftLandscape(HospitalPlot plot, int dx, int dy)
        {
            if (!Active || plot?.Definition == null || (dx == 0 && dy == 0)) return;
            var offset = new Vector3(dx * 2f, 0f, dy * 2f);
            foreach (HospitalPlotLayer layer in Enum.GetValues(typeof(HospitalPlotLayer)))
                foreach (var item in plot.Definition.GetItems(layer))
                    if (item != null) item.Position += offset;
        }

        internal static void EnableObjects(EditHospitalMenu menu)
        {
            if (!Active || menu == null) return;
            var field = AccessTools.Field(typeof(EditHospitalMenu), "_objectsButton");
            var button = field?.GetValue(menu) as Button;
            if (button == null) return;
            button.interactable = true;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = EditorLocalization.Get("editor.objects.title");
        }
    }

    // La metapartida temporal se crea con ignoreSave=true, pero el flujo nativo
    // de MetagameStateInHospital llama despues a App.LoadLevel con ignoreSave=false.
    // Eso hace que el editor abra EL ULTIMO SAVE de Hogsport en vez de su escenario base.
    // Forzamos la carga del nivel original. No se modifica ningun guardado.
    [HarmonyPatch(typeof(App), "LoadLevel", new[]
    {
        typeof(LevelConfig), typeof(SaveFileHeader), typeof(bool), typeof(bool)
    })]
    internal static class ScenarioEditorIgnoreExistingLevelSavePatch
    {
        private static void Prefix(LevelConfig __0, ref SaveFileHeader __1,
            ref bool __2, ref bool __3)
        {
            if (!ScenarioEditorFlow.IsRunning || ScenarioEditorSession.Active || __0 == null)
                return;
            __1 = null;  // No cargar un guardado seleccionado manualmente.
            __2 = true;  // ignoreSave: crear el nivel desde los assets de origen.
            __3 = false; // No guardar automaticamente el nivel anterior.
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Editor de escenarios: cargando definicion ORIGINAL de " + __0.UniqueId +
                " (guardados ignorados).");
        }
    }

    // Se conserva el control nativo de visibilidad general de la parcela.
    // En versiones anteriores bloqueabamos _toggleVisible; ya no lo hacemos.
    // Es distinto de los tres controles de capa Base/Built/Unbuilt.

    // El Prefix se ejecuta ANTES del Setup del menu y del constructor del cursor:
    // ambos leen/modifican las definiciones originales.
    [HarmonyPatch(typeof(HospitalEditEvents), "StartEdit")]
    internal static class ScenarioEditorBeginIsolationPatch
    {
        private static void Prefix(HospitalEditEvents __instance)
        {
            if (!ScenarioEditorFlow.IsRunning) return;
            var field = AccessTools.Field(typeof(HospitalEditEvents), "_level");
            ScenarioEditorSession.Begin(field?.GetValue(__instance) as Level);
        }
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "EndEdit")]
    internal static class ScenarioEditorEndIsolationPatch
    {
        private static void Postfix()
        {
            if (ScenarioEditorSession.Active) ScenarioEditorSession.Restore();
        }
    }

    // SetFloorImageAlpha reduce el alfa a 0.1/0.5 sin restaurarlo al cerrar.
    // Conservamos la opacidad original de los pixeles editables.
    [HarmonyPatch(typeof(CursorEditHospital), "SetFloorImageAlpha")]
    internal static class ScenarioEditorAlphaPatch
    {
        private static bool Prefix() => !ScenarioEditorSession.Active;
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioEditorObjectsPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance)
        {
            ScenarioEditorSession.EnableObjects(__instance);
        }
    }

    [HarmonyPatch(typeof(CursorEditHospitalMovePlot), "Place")]
    internal static class ScenarioEditorMoveLandscapePatch
    {
        private static void Prefix(CursorEditHospitalMovePlot __instance)
        {
            if (!ScenarioEditorSession.Active || __instance == null) return;
            var type = typeof(CursorEditHospitalMovePlot);
            var plot = AccessTools.Field(type, "_hospitalPlot")?.GetValue(__instance) as HospitalPlot;
            var floor = AccessTools.Field(type, "_floorPlan")?.GetValue(__instance) as FloorPlan;
            var original = AccessTools.Field(type, "_originalAnchor")?.GetValue(__instance);
            if (floor == null || !(original is GridCoord)) return;
            var start = (GridCoord)original;
            var current = floor.Anchor;
            ScenarioEditorSession.ShiftLandscape(plot, current.X - start.X, current.Y - start.Y);
        }
    }

    // Move Plot tambien llamaba a Build(false) despues de desplazar la mascara.
    // Esto destruia la visual y reconstruia paredes con variaciones aleatorias.
    // Conservar el refresco ligero ya utilizado para la pintura de parcelas.
    [HarmonyPatch(typeof(CursorEditHospitalMovePlot), "Place")]
    internal static class ScenarioEditorMovePreserveWallsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var build = AccessTools.Method(typeof(HospitalMap), nameof(HospitalMap.Build));
            var refresh = AccessTools.Method(typeof(ScenarioEditorPaintRefresh), nameof(ScenarioEditorPaintRefresh.Refresh));
            var light = AccessTools.Method(typeof(WorldState), nameof(WorldState.CalculateLighting));
            var safeLight = AccessTools.Method(typeof(ScenarioEditorPaintRefresh), nameof(ScenarioEditorPaintRefresh.CalculateLighting));
            if (refresh == null || safeLight == null ||
                codes.Count(c => Equals(c.operand, build)) != 1 ||
                codes.Count(c => Equals(c.operand, light)) != 1)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "MovePlot: firma no reconocida; se conserva método nativo.");
                return codes;
            }
            foreach (var code in codes)
            {
                if (Equals(code.operand, build))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = refresh;
                }
                else if (Equals(code.operand, light))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = safeLight;
                }
            }
            return codes;
        }
    }

    // La pintura nativa llama a Build(false), que demuele y vuelve a crear toda
    // la parcela aunque solo cambie una celda. RebuildWalls conserva HospitalMap,
    // FloorPlan, Room y las tres capas de paisaje. El editor repone los dos pasos
    // que ese metodo omite: entradas y tipos de pared asociados a pilares.
    internal static class ScenarioEditorPaintRefresh
    {
        private static readonly MethodInfo AddEntrancesMethod =
            AccessTools.Method(typeof(HospitalMap), "AddHospitalEntrancesFromImage",
                new[] { typeof(Texture2D) });
        private static readonly MethodInfo AddPillarsMethod =
            AccessTools.Method(typeof(HospitalMap), "AddPillars",
                new[] { typeof(Texture2D) });
        private static readonly PropertyInfo FloorImageProperty =
            AccessTools.Property(typeof(HospitalMap), "FloorImage");
        private static readonly FieldInfo FloorImageField =
            AccessTools.Field(typeof(HospitalMap), "_floorImage");

        private static HospitalMap _mapBeingRefreshed;
        private static bool _skipNextLighting;

        internal static void Refresh(HospitalMap map, bool playBuildAnimation)
        {
            _skipNextLighting = false;
            if (!ScenarioEditorSession.Active || map == null ||
                AddEntrancesMethod == null || AddPillarsMethod == null ||
                FloorImageProperty == null || FloorImageField == null)
            {
                map?.Build(playBuildAnimation);
                return;
            }

            var room = map.Room;
            var wasOpen = room?.IsOpen ?? false;
            var wasVisible = room?.IsVisible() ?? false;
            var previousWalls = map.ExternalWalls?.Count ?? 0;
            var timer = Stopwatch.StartNew();
            var previousFloorImage = FloorImageField.GetValue(map) as Texture2D;

            try
            {
                var floorImage = GetFloorImage(map);
                // RebuildWalls fue escrito para parcelas fusionadas y usa el
                // campo _floorImage directamente. En una parcela normal es null,
                // aunque la propiedad devuelve correctamente la textura de la
                // definicion. Se completa solo durante este refresco.
                FloorImageField.SetValue(map, floorImage);
                AddEntrancesMethod.Invoke(map, new object[] { floorImage });
                _mapBeingRefreshed = map;
                map.RebuildWalls();

                // RebuildWalls conserva la instancia, pero UpdateFromRoom puede
                // alterar su presentacion. Solo se corrige si realmente cambia.
                if (room != null && ReferenceEquals(room, map.Room))
                {
                    if (room.IsOpen != wasOpen)
                    {
                        if (wasOpen) room.Open();
                        else room.Close();
                    }
                    if (room.IsVisible() != wasVisible)
                        room.SetVisible(wasVisible);
                }

                _skipNextLighting = true;
                timer.Stop();
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Pintura del editor actualizada sin demoler la parcela: paredes " +
                    previousWalls + " -> " + (map.ExternalWalls?.Count ?? 0) +
                    ", " + timer.ElapsedMilliseconds + " ms.");
            }
            catch (Exception exception)
            {
                // Si una version del juego cambia esta ruta privada, se conserva
                // el comportamiento nativo antes que dejar una parcela parcial.
                _mapBeingRefreshed = null;
                UnderPressureEditorPlugin.Log?.LogError(
                    "Fallo en el refresco ligero de pintura; se usara Build(false): " +
                    exception);
                map.Build(playBuildAnimation);
                _skipNextLighting = true;
            }
            finally
            {
                _mapBeingRefreshed = null;
                FloorImageField.SetValue(map, previousFloorImage);
            }
        }

        internal static void AddPillarsBeforeVisualUpdate()
        {
            var map = _mapBeingRefreshed;
            if (map == null || AddPillarsMethod == null) return;
            AddPillarsMethod.Invoke(map, new object[] { GetFloorImage(map) });
        }

        private static Texture2D GetFloorImage(HospitalMap map) =>
            FloorImageProperty?.GetValue(map, null) as Texture2D;

        internal static void CalculateLighting(WorldState worldState)
        {
            if (_skipNextLighting)
            {
                _skipNextLighting = false;
                return;
            }
            worldState?.CalculateLighting();
        }
    }

    // Se cambia exclusivamente la reconstruccion iniciada por la herramienta de
    // pintura. Las cargas, aperturas de .upmap y movimientos conservan Build.
    [HarmonyPatch(typeof(CursorEditHospitalBuild), nameof(CursorEditHospitalBuild.CursorUpdate))]
    internal static class ScenarioEditorPaintRefreshPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var build = AccessTools.Method(typeof(HospitalMap), nameof(HospitalMap.Build));
            var lighting = AccessTools.Method(
                typeof(WorldState), nameof(WorldState.CalculateLighting));
            var refresh = AccessTools.Method(
                typeof(ScenarioEditorPaintRefresh), nameof(ScenarioEditorPaintRefresh.Refresh));
            var calculateLighting = AccessTools.Method(typeof(ScenarioEditorPaintRefresh),
                nameof(ScenarioEditorPaintRefresh.CalculateLighting));
            var replacedBuild = 0;
            var replacedLighting = 0;

            foreach (var code in codes)
            {
                if (Equals(code.operand, build)) ++replacedBuild;
                else if (Equals(code.operand, lighting)) ++replacedLighting;
            }

            if (replacedBuild != 1 || replacedLighting != 1)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo aislar la reconstruccion de pintura: Build=" +
                    replacedBuild + ", Lighting=" + replacedLighting +
                    ". Se conserva CursorUpdate sin modificar.");
                return codes;
            }

            foreach (var code in codes)
            {
                if (Equals(code.operand, build))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = refresh;
                }
                else if (Equals(code.operand, lighting))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = calculateLighting;
                }
            }
            return codes;
        }
    }

    // RebuildWalls actualiza los visuales despues de calcular las paredes. Este
    // punto permite aplicar los tipos de pilar en el mismo orden que Build.
    [HarmonyPatch(typeof(RoomFloorPlanVisual), nameof(RoomFloorPlanVisual.UpdateFromRoom))]
    internal static class ScenarioEditorPaintPillarsPatch
    {
        private static void Prefix() =>
            ScenarioEditorPaintRefresh.AddPillarsBeforeVisualUpdate();
    }

    // Ctrl mantiene la seleccion multiple nativa.
    // Solo en la confirmacion de colocacion la tecla de duplicado pasa a Mayus.
    // El objeto/seleccion permanece en el cursor y puede colocarse repetidamente.
    [HarmonyPatch(typeof(CursorEditHospitalItem), "OnMoveRoomEnd")]
    internal static class ScenarioEditorShiftClonePatch
    {
        private static readonly MethodInfo NativeGetKey =
            AccessTools.Method(typeof(InputManager), "GetKey", new[] { typeof(KeyCode) });
        private static readonly MethodInfo CloneGetKey =
            AccessTools.Method(typeof(ScenarioEditorShiftClonePatch), nameof(GetCloneModifier));

        private static bool GetCloneModifier(InputManager input, KeyCode unused)
        {
            // Solo afecta al Editor de escenarios: fuera de el, Ctrl nativo.
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active)
                return input != null && input.GetKey(KeyCode.LeftControl);
            return input != null &&
                (input.GetKey(KeyCode.LeftShift) || input.GetKey(KeyCode.RightShift));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // OnMoveRoomEnd contiene UN acceso a GetKey(LeftControl), usado
            // unicamente al confirmar si la seleccion se DUPLICA. Si el juego
            // cambia el metodo, no alterar ninguna otra comprobacion de Ctrl.
            var codes = new List<CodeInstruction>(instructions);
            var matches = NativeGetKey == null ? 0 :
                codes.FindAll(code => code.Calls(NativeGetKey)).Count;
            if (matches != 1 || CloneGetKey == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se cambio Ctrl por Mayus: el metodo nativo difiere (" + matches + ").");
                return codes;
            }
            foreach (var instruction in codes)
            {
                if (instruction.Calls(NativeGetKey))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CloneGetKey;
                }
            }
            return codes;
        }
    }
}

namespace UnderPressure.Editor
{
    // CursorRoomItem gestiona la colocacion de UN objeto. Al mover uno existente,
    // el cursor nativo finaliza despues de colocarlo; con Mayus abrimos otro
    // cursor nuevo del mismo modelo para continuar creando copias.
    internal static class ScenarioEditorContinuousObjectClone
    {
        private sealed class Pending
        {
            internal Level Level;
            internal IRoomItemDefinition Definition;
            internal FloorPlan Floor;
            internal RoomFloorPlanVisual Visual;
            internal float Rotation;
        }

        private static readonly Dictionary<CursorRoomItem, Pending> PendingByCursor =
            new Dictionary<CursorRoomItem, Pending>();
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(CursorRoomItem), "_level");
        private static readonly FieldInfo VisualField = AccessTools.Field(typeof(CursorRoomItem), "_roomFloorPlanVisual");

        internal static object BeforePlace(CursorRoomItem cursor)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning || cursor?.RoomItem == null)
                return null;
            var level = LevelField?.GetValue(cursor) as Level;
            if (level?.InputManager == null ||
                !(level.InputManager.GetKey(KeyCode.LeftShift) ||
                  level.InputManager.GetKey(KeyCode.RightShift))) return null;
            var item = cursor.RoomItem;
            var floor = item.FloorPlan;
            var visual = VisualField?.GetValue(cursor) as RoomFloorPlanVisual;
            if (floor == null || visual == null) return null;
            return new Pending
            {
                Level = level, Definition = item.Definition, Floor = floor,
                Visual = visual, Rotation = item.Rotation
            };
        }

        internal static void AfterPlace(CursorRoomItem cursor, bool ended, object state)
        {
            if (cursor == null) return;
            if (ended && state is Pending pending)
                PendingByCursor[cursor] = pending;
            else
                PendingByCursor.Remove(cursor);
        }

        internal static void AfterCursorUpdate(CursorRoomItem cursor)
        {
            if (cursor == null || !PendingByCursor.TryGetValue(cursor, out var pending)) return;
            PendingByCursor.Remove(cursor);
            try
            {
                if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning ||
                    pending.Level == null || !ReferenceEquals(pending.Level, ScenarioEditorSession.Level) ||
                    pending.Floor == null || pending.Definition == null ||
                    pending.Level.CursorManager == null)
                    return;
                // La siguiente copia utiliza el objeto como NUEVA colocacion.
                // Ctrl nunca interviene aqui: la pulsacion de Mayus se comprueba
                // en BeforePlace cuando se confirma la colocacion anterior.
                var configuration = pending.Level.BuildingLogic.Configuration.RoomItemEditConfig;
                var next = new CursorRoomItem(pending.Level.CursorManager, pending.Level,
                    configuration, pending.Definition, pending.Rotation,
                    pending.Floor, pending.Visual, null, false);
                pending.Level.CursorManager.PushMode(next);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "No se pudo continuar clonando el objeto con Mayus: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(CursorRoomItem), "PlaceItem")]
    internal static class ScenarioEditorSingleObjectShiftPatch
    {
        private static void Prefix(CursorRoomItem __instance, out object __state) =>
            __state = ScenarioEditorContinuousObjectClone.BeforePlace(__instance);

        private static void Postfix(CursorRoomItem __instance, bool __result, object __state) =>
            ScenarioEditorContinuousObjectClone.AfterPlace(__instance, __result, __state);
    }

    [HarmonyPatch(typeof(CursorRoomItem), "CursorUpdate")]
    internal static class ScenarioEditorContinueAfterSinglePlacementPatch
    {
        private static void Postfix(CursorRoomItem __instance) =>
            ScenarioEditorContinuousObjectClone.AfterCursorUpdate(__instance);
    }
}
// ===== END ORIGINAL: ScenarioEditorSession.cs =====

// ===== BEGIN ORIGINAL: ScenarioHospitalStyles.cs =====
// Catalogo de estilos originales de parcelas y aplicacion de su configuracion .upmap.
namespace UnderPressure.Editor
{
    internal static class ScenarioHospitalStyles
    {
        internal sealed class RoomChoice
        {
            internal string Id;
            internal string Label;
            internal SharedInstance<RoomDefinition> Definition;
        }

        internal sealed class PerimeterChoice
        {
            internal string Mode;
            internal string Label;
            internal string[] PrefabNames;
            internal GameObject[] Prefabs;
        }

        internal sealed class Catalog
        {
            internal readonly List<RoomChoice> Built = new List<RoomChoice>();
            internal readonly List<RoomChoice> Building = new List<RoomChoice>();
            internal readonly List<RoomChoice> Unbuilt = new List<RoomChoice>();
            internal readonly List<PerimeterChoice> Perimeters = new List<PerimeterChoice>();
        }

        internal static Catalog Build(Level level, string originalLabel, string noneLabel)
        {
            if (level?.App?.SandboxSettingsConfig?.LevelOptions == null)
                throw new InvalidOperationException("No esta disponible el catalogo original de hospitales.");

            var result = new Catalog();
            AddOriginalRoom(result.Built, originalLabel);
            AddOriginalRoom(result.Building, originalLabel);
            AddOriginalRoom(result.Unbuilt, originalLabel);
            result.Perimeters.Add(new PerimeterChoice
            {
                Mode = ScenarioMapFormat.PerimeterOriginal,
                Label = originalLabel,
                PrefabNames = new string[0]
            });
            result.Perimeters.Add(new PerimeterChoice
            {
                Mode = ScenarioMapFormat.PerimeterNone,
                Label = noneLabel,
                PrefabNames = new string[0],
                Prefabs = new GameObject[0]
            });

            var seenLevels = new HashSet<LevelConfig>();
            var seenPerimeters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in level.App.SandboxSettingsConfig.LevelOptions)
            {
                var source = option?.Level?.Instance;
                if (source == null || !seenLevels.Add(source) ||
                    !DLCUtils.IsDLCInstalled(source.GetRequiredDlcPack())) continue;
                var config = source.GetWorldStateConfig();
                if (config == null) continue;
                AddRoom(result.Built, config._roomBuiltDefinition);
                AddRoom(result.Building, config._roomBuildingDefinition);
                AddRoom(result.Unbuilt, config._roomUnbuiltDefinition);

                var prefabs = config.PerimeterPrefabs;
                if (prefabs == null || prefabs.Length == 0 || prefabs.Any(prefab => prefab == null))
                    continue;
                var names = prefabs.Select(prefab => prefab.name).ToArray();
                var key = string.Join("|", names);
                if (!seenPerimeters.Add(key)) continue;
                result.Perimeters.Add(new PerimeterChoice
                {
                    Mode = ScenarioMapFormat.PerimeterCustom,
                    Label = source.GetLocalisedDisplayName(),
                    PrefabNames = names,
                    Prefabs = (GameObject[])prefabs.Clone()
                });
            }
            return result;
        }

        private static void AddOriginalRoom(ICollection<RoomChoice> target, string label)
        {
            target.Add(new RoomChoice { Id = "", Label = label });
        }

        private static void AddRoom(ICollection<RoomChoice> target,
            SharedInstance<RoomDefinition> definition)
        {
            if (definition?.Instance == null || string.IsNullOrEmpty(definition.name) ||
                target.Any(item => string.Equals(item.Id, definition.name, StringComparison.Ordinal)))
                return;
            var label = definition.name.Replace("Room_Hospital_", "").Replace("Room_Hospital ", "");
            target.Add(new RoomChoice { Id = definition.name, Label = label, Definition = definition });
        }

        internal static void Apply(Level level, ScenarioMapFormat.Map map, WorldState.Config config,
            IList<HospitalPlotDefinition> definitions)
        {
            if (level == null || map == null || config == null)
                throw new ArgumentNullException();
            var catalog = Build(level, "Original", "None");
            ApplyRoom(map.builtRoomStyle, catalog.Built, value => config._roomBuiltDefinition = value,
                definitions, (definition, value) => definition.BuiltRoomDefinition = value);
            ApplyRoom(map.buildingRoomStyle, catalog.Building, value => config._roomBuildingDefinition = value,
                definitions, (definition, value) => definition.BuildingRoomDefinition = value);
            ApplyRoom(map.unbuiltRoomStyle, catalog.Unbuilt, value => config._roomUnbuiltDefinition = value,
                definitions, (definition, value) => definition.UnbuiltRoomDefinition = value);

            var mode = ScenarioMapFormat.NormalizePerimeter(map.plotPerimeter);
            if (mode == ScenarioMapFormat.PerimeterNone)
                config.PerimeterPrefabs = new GameObject[0];
            else if (mode == ScenarioMapFormat.PerimeterCustom)
            {
                var names = map.perimeterPrefabs ?? new string[0];
                var choice = catalog.Perimeters.FirstOrDefault(item =>
                    item.Mode == ScenarioMapFormat.PerimeterCustom &&
                    item.PrefabNames.SequenceEqual(names));
                if (choice == null)
                    throw new InvalidDataException("El estilo de perimetro del mapa no esta disponible.");
                config.PerimeterPrefabs = (GameObject[])choice.Prefabs.Clone();
            }
        }

        private static void ApplyRoom(string id, IList<RoomChoice> choices,
            Action<SharedInstance<RoomDefinition>> setConfig,
            IList<HospitalPlotDefinition> definitions,
            Action<HospitalPlotDefinition, SharedInstance<RoomDefinition>> setDefinition)
        {
            if (string.IsNullOrEmpty(id)) return;
            var choice = choices.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (choice?.Definition == null)
                throw new InvalidDataException("El estilo de hospital no esta disponible: " + id);
            setConfig(choice.Definition);
            if (definitions == null) return;
            foreach (var definition in definitions)
                if (definition != null) setDefinition(definition, choice.Definition);
        }
    }
}
// ===== END ORIGINAL: ScenarioHospitalStyles.cs =====
