// Updated: 2026-10-15 - Evita iniciar SaveSystem antes de Steam y registra los guardados tras iniciarse el juego.
// Mantiene intacto MapRuntime.cs y sus recursos nativos de parcelas.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioCareerSaveBridge
    {
        private const string MarkerVersion = "SCENARIO_CONFIG_SAVE_V1";
        private static readonly FieldInfo MapPathField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "_path");
        private static readonly FieldInfo WorldConfigAppliedField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "_worldConfigApplied");
        private static readonly FieldInfo SceneAppliedField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "_sceneApplied");
        private static readonly FieldInfo TerrainAppliedField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "_terrainApplied");
        private static readonly FieldInfo RetryField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "_retry");
        private static readonly FieldInfo RendererMaterialsField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "OriginalRendererMaterials");
        private static readonly FieldInfo TemporaryMaterialsField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "TemporaryMaterials");
        private static readonly FieldInfo TemporaryTexturesField =
            AccessTools.Field(typeof(ScenarioCareerMapLoader), "TemporaryTextures");
        private static readonly MethodInfo RestoreLandscapeMethod =
            AccessTools.Method(typeof(ScenarioCareerMapLoader), "RestoreOriginalLandscape");
        private static readonly MethodInfo RestoreTerrainMethod =
            AccessTools.Method(typeof(ScenarioCareerMapLoader), "RestoreTerrainMeshes");

        private static SaveFileHeader _explicitHeader;
        private static SaveFileHeader _callerLevelHeader;
        private static bool _callerIgnoreSave;
        private static bool _sessionLoaded;
        private static string _cachedPath;
        private static string _cachedFingerprint;
        private static DateTime _levelReadyUtc;
        private static DateTime _nextSaveCheckUtc;
        private static string _lastRecordedSave;
        private static long _lastRecordedWriteTicks;

        private static string MapPath => MapPathField?.GetValue(null) as string;
        private static string MarkerPath(SaveFileHeader header) =>
            string.IsNullOrEmpty(header?.FilePath) ? null : header.FilePath + ".configref";

        private static string Fingerprint(string mapPath)
        {
            if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath)) return null;
            if (string.Equals(mapPath, _cachedPath, StringComparison.OrdinalIgnoreCase))
                return _cachedFingerprint;
            using (var stream = File.OpenRead(mapPath))
            using (var sha = SHA256.Create())
            {
                _cachedPath = mapPath;
                _cachedFingerprint = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                return _cachedFingerprint;
            }
        }

        private static bool HasMarker(SaveFileHeader header)
        {
            var marker = MarkerPath(header);
            return marker != null && File.Exists(marker);
        }

        private static bool TryReadMarker(SaveFileHeader header, out string mapPath)
        {
            mapPath = null;
            var marker = MarkerPath(header);
            if (marker == null || !File.Exists(marker)) return false;
            try
            {
                var lines = File.ReadAllLines(marker);
                if (lines.Length != 4 || lines[0] != MarkerVersion ||
                    !File.Exists(lines[1]) ||
                    lines[2] != Fingerprint(lines[1]) ||
                    lines[3] != header.Date.Ticks.ToString())
                    return false;
                mapPath = Path.GetFullPath(lines[1]);
                return true;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Carrera: identificador de partida ilegible: " + ex.Message);
                return false;
            }
        }

        private static bool MatchesCurrentMap(SaveFileHeader header)
        {
            var path = MapPath;
            string markedMap;
            return path != null && TryReadMarker(header, out markedMap) &&
                string.Equals(Path.GetFullPath(path), markedMap,
                    StringComparison.OrdinalIgnoreCase);
        }

        // App.Load es el punto de entrada de Carga rapida y de la lista de guardados.
        // Se captura su SaveFileHeader ANTES del Prefix antiguo que lo borra.
        internal static bool BeforeAppLoad(App app, SaveFileHeader header)
        {
            _explicitHeader = header;
            if (app == null || header == null || ScenarioEditorFlow.IsRunning)
                return true;

            var marker = MarkerPath(header);
            if (marker == null || !File.Exists(marker)) return true;
            string savedMap;
            if (!TryReadMarker(header, out savedMap))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Carrera: el config.json asociado ha cambiado o no existe. " +
                    "No se cargara un escenario equivocado: " + marker);
                _explicitHeader = null;
                return false;
            }

            var active = MapPath;
            if (active != null && !string.Equals(active, savedMap,
                StringComparison.OrdinalIgnoreCase))
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Carrera: este guardado pertenece a otro config.json. " +
                    "Sal al menu principal antes de cambiar de escenario.");
                _explicitHeader = null;
                return false;
            }

            if (!ScenarioCareerMapLoader.IsCustomSession)
            {
                try
                {
                    var config = app.Metagame.LevelList.GetLevelConfigByID(header.LevelID);
                    ScenarioCareerMapLoader.Prepare(config, savedMap);
                    UnderPressureEditorPlugin.Log?.LogInfo(
                        "Carrera: config.json reconocido desde partida guardada: " + savedMap);
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Carrera: no se pudo preparar el config.json guardado: " + ex);
                    _explicitHeader = null;
                    return false;
                }
            }
            _explicitHeader = header; // Prepare ejecuta Cancel y limpia la captura.
            return true;
        }

        internal static void AfterAppLoad() => _explicitHeader = null;

        internal static void CaptureNativeLevelCall(SaveFileHeader header, bool ignoreSave)
        {
            _callerLevelHeader = header;
            _callerIgnoreSave = ignoreSave;
        }

        // Este Prefix se ejecuta DESPUES del Prefix de MapRuntime. Asi se restaura
        // la cabecera que la version anterior descartaba indiscriminadamente.
        internal static void AfterLegacyLevelPrefix(App app, LevelConfig config,
            ref SaveFileHeader chosenSave, ref bool ignoreSave)
        {
            if (app == null || app.IsLoading || ScenarioEditorFlow.IsRunning ||
                !ScenarioCareerMapLoader.IsCustomSession ||
                !ScenarioCareerMapLoader.MatchesConfig(config)) return;

            SaveFileHeader candidate = _explicitHeader ?? _callerLevelHeader;
            // Reiniciar hospital debe seguir creando un nivel nuevo, aunque
            // existan saves anteriores del escenario.
            var restore = !_callerIgnoreSave && candidate != null &&
                MatchesCurrentMap(candidate);
            if (!_callerIgnoreSave && !restore && candidate != null && _sessionLoaded &&
                !HasMarker(candidate) && candidate.LevelID == config.UniqueId)
            {
                // Guardados previos a esta correccion, dentro de la sesion actual.
                restore = true;
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Carrera: restaurando guardado anterior sin identificador de mapa.");
            }

            // El Prefix anterior transforma ignoreSave en true. Se utiliza
            // su valor ORIGINAL para distinguir Comenzar de Reiniciar.
            if (!_callerIgnoreSave && candidate == null && app.SaveSystem != null)
            {
                foreach (var header in app.SaveSystem.SaveFiles)
                {
                    if (header == null || header.IsBroken || header.LevelID != config.UniqueId ||
                        !MatchesCurrentMap(header)) continue;
                    candidate = header;
                    restore = true;
                    break;
                }
            }

            _callerLevelHeader = null;
            if (!restore)
            {
                // La nueva carga no debe reutilizar los visuales ni el flag
                // WorldState de una partida personalizada anterior.
                if (_sessionLoaded)
                {
                    ResetSceneVisualState();
                    WorldConfigAppliedField?.SetValue(null, false);
                    _sessionLoaded = false;
                }
                return;
            }

            ResetSceneVisualState();
            WorldConfigAppliedField?.SetValue(null, true);
            chosenSave = candidate;
            ignoreSave = false;
            _sessionLoaded = false;
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Carrera: RESTAURANDO guardado nativo del mapa: " + candidate.FilePath);
        }

        private static void ResetSceneVisualState()
        {
            var retry = RetryField?.GetValue(null) as MonoBehaviour;
            if (retry != null) UnityEngine.Object.Destroy(retry.gameObject);
            RetryField?.SetValue(null, null);
            RestoreLandscapeMethod?.Invoke(null, null);
            RestoreTerrainMethod?.Invoke(null, null);

            var originalMaterials = RendererMaterialsField?.GetValue(null)
                as Dictionary<Renderer, Material[]>;
            if (originalMaterials != null)
            {
                foreach (var item in originalMaterials)
                    if (item.Key != null) item.Key.sharedMaterials = item.Value;
                originalMaterials.Clear();
            }
            var tempMaterials = TemporaryMaterialsField?.GetValue(null) as List<Material>;
            if (tempMaterials != null)
            {
                foreach (var material in tempMaterials)
                    if (material != null) UnityEngine.Object.Destroy(material);
                tempMaterials.Clear();
            }
            var tempTextures = TemporaryTexturesField?.GetValue(null) as List<Texture2D>;
            if (tempTextures != null)
            {
                foreach (var texture in tempTextures)
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                tempTextures.Clear();
            }
            SceneAppliedField?.SetValue(null, false);
            TerrainAppliedField?.SetValue(null, false);
        }

        internal static void LevelReady(Level level)
        {
            if (!ScenarioCareerMapLoader.Match(level)) return;
            _sessionLoaded = true;
            _levelReadyUtc = DateTime.UtcNow;
            _nextSaveCheckUtc = _levelReadyUtc;
            _lastRecordedSave = null;
            _lastRecordedWriteTicks = 0L;
        }

        internal static void ResetSession()
        {
            _sessionLoaded = false;
            _explicitHeader = null;
            _callerLevelHeader = null;
            _callerIgnoreSave = false;
            _cachedPath = null;
            _cachedFingerprint = null;
            _levelReadyUtc = DateTime.MinValue;
            _nextSaveCheckUtc = DateTime.MinValue;
            _lastRecordedSave = null;
            _lastRecordedWriteTicks = 0L;
        }

        // Nunca parchear SaveSystem desde PatchAll: forzaria a Unity a
        // calcular Cloud/0 antes de que Steam haya inicializado el perfil.
        // App.Update solo llega aqui cuando hay un nivel cargado y un
        // guardado nativo realmente escrito DESPUES de iniciar el nivel.
        internal static void ObserveNativeSave(App app)
        {
            if (!_sessionLoaded || app?.Level == null ||
                !ScenarioCareerMapLoader.Match(app.Level)) return;
            var now = DateTime.UtcNow;
            if (now < _nextSaveCheckUtc) return;
            _nextSaveCheckUtc = now.AddSeconds(1);
            NativeSaveCompleted(app.SaveSystem, app.Level);
        }

        internal static void NativeSaveCompleted(SaveSystem saveSystem, Level level)
        {
            if (!_sessionLoaded || saveSystem == null || level == null ||
                !ScenarioCareerMapLoader.Match(level)) return;
            var path = MapPath;
            if (path == null) return;
            try
            {
                var header = saveSystem.GetSaveForLevel(level.Config.UniqueId, false);
                var marker = MarkerPath(header);
                if (marker == null || !File.Exists(header.FilePath)) return;
                // No asociar un save anterior del hospital ORIGINAL a este mapa.
                var lastWrite = File.GetLastWriteTimeUtc(header.FilePath);
                if (lastWrite < _levelReadyUtc ||
                    header.Date.ToUniversalTime() < _levelReadyUtc.AddSeconds(-2)) return;
                if (string.Equals(_lastRecordedSave, header.FilePath,
                    StringComparison.OrdinalIgnoreCase) &&
                    _lastRecordedWriteTicks == lastWrite.Ticks) return;
                // Evitar reescribir marcadores que ya corresponden a este save.
                string previouslyMarked;
                if (TryReadMarker(header, out previouslyMarked) &&
                    string.Equals(previouslyMarked, Path.GetFullPath(path),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _lastRecordedSave = header.FilePath;
                    _lastRecordedWriteTicks = lastWrite.Ticks;
                    return;
                }
                File.WriteAllLines(marker, new[]
                {
                    MarkerVersion, Path.GetFullPath(path), Fingerprint(path),
                    header.Date.Ticks.ToString()
                });
                _lastRecordedSave = header.FilePath;
                _lastRecordedWriteTicks = lastWrite.Ticks;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Carrera: guardado nativo asociado al escenario " +
                    Path.GetFileName(path) + ": " + header.FilePath);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Carrera: no se pudo asociar la partida al config.json: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(App), "Load", new[] { typeof(SaveFileHeader) })]
    internal static class ScenarioCareerSavedGameOpenPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(App __instance, SaveFileHeader __0) =>
            ScenarioCareerSaveBridge.BeforeAppLoad(__instance, __0);

        private static void Postfix() => ScenarioCareerSaveBridge.AfterAppLoad();
    }

    [HarmonyPatch(typeof(App), "LoadLevel", new[]
    {
        typeof(LevelConfig), typeof(SaveFileHeader), typeof(bool), typeof(bool)
    })]
    internal static class ScenarioCareerSavedGameLoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(App __instance, LevelConfig __0,
            ref SaveFileHeader __1, ref bool __2) =>
            ScenarioCareerSaveBridge.AfterLegacyLevelPrefix(__instance, __0,
                ref __1, ref __2);
    }

    [HarmonyPatch(typeof(App), "LoadLevel", new[]
    {
        typeof(LevelConfig), typeof(SaveFileHeader), typeof(bool), typeof(bool)
    })]
    internal static class ScenarioCareerSavedGameCapturePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(SaveFileHeader __1, bool __2) =>
            ScenarioCareerSaveBridge.CaptureNativeLevelCall(__1, __2);
    }

    // App esta inicializado despues de Steam. No parchear SaveSystem en el
    // arranque: su inicializador estatico fija prematuramente la ruta Cloud/0.
    [HarmonyPatch(typeof(App), "Update")]
    internal static class ScenarioCareerSavedGameMarkerPatch
    {
        private static void Postfix(App __instance) =>
            ScenarioCareerSaveBridge.ObserveNativeSave(__instance);
    }

    [HarmonyPatch(typeof(Metagame), "OnLevelLoaded")]
    internal static class ScenarioCareerSavedGameReadyPatch
    {
        private static void Postfix(Level __0) => ScenarioCareerSaveBridge.LevelReady(__0);
    }

    [HarmonyPatch(typeof(ScenarioCareerMapLoader), "Prepare")]
    internal static class ScenarioCareerSavedGameNewMapPatch
    {
        private static void Postfix() => ScenarioCareerSaveBridge.ResetSession();
    }

    [HarmonyPatch(typeof(ScenarioCareerMapLoader), "Cancel")]
    internal static class ScenarioCareerSavedGameEndPatch
    {
        private static void Postfix() => ScenarioCareerSaveBridge.ResetSession();
    }
}
