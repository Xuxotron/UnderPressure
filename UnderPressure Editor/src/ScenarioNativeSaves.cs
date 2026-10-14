// Updated: 2026-10-13 - Guardado nativo por mapa, implementado desde el cargador funcional.
// No sustituye perfiles, campanas ni ranuras del SaveSystem; cada UPMap tiene nombre de .sav propio.
// No cambia el formato .sav; aparta tambien los .sav experimentales viejos sin eliminarlos.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using FullInspector;
using HarmonyLib;
using TH20;

namespace UnderPressure.Editor
{
    internal static class ScenarioNativeSaves
    {
        private const string Prefix = "UPMAP_";
        private static readonly FieldInfo NativeIdTable =
            AccessTools.Field(typeof(SaveSystem), "_assetIDs");
        private static readonly string[] NativeSerializers =
        {
            "_serializerLevel", "_serializerMetagame", "_serializerRoomTemplates"
        };
        private static SaveSystem _attachedSystem;
        private static readonly Dictionary<int, object> AttachedAssets =
            new Dictionary<int, object>();

        // Identidad estable y exclusiva basada en la ruta RELATIVA a Mods/maps.
        // Nunca usar LevelID: muchos mapas comparten el mismo hospital original.
        private static ulong HashPath(string mapPath)
        {
            var root = Path.GetFullPath(ScenarioEditorStorage.Folder)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(mapPath);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".upmap", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El mapa esta fuera de Mods/maps.");
            var relative = full.Substring(root.Length).Replace('\\', '/').ToUpperInvariant();
            unchecked
            {
                var hash = 14695981039346656037UL;
                foreach (var c in relative)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }

        private static string Key(string mapPath) =>
            Prefix + HashPath(mapPath).ToString("X16", CultureInfo.InvariantCulture);

        private static string NameFromSave(SaveFileHeader save) =>
            save == null || string.IsNullOrEmpty(save.FilePath)
                ? string.Empty : Path.GetFileNameWithoutExtension(save.FilePath);

        // Tambien aislar los guardados experimentales anteriores de Codex: nunca
        // presentarlos al cargar un hospital original. No se borran sus archivos.
        internal static bool IsCustomSave(SaveFileHeader save)
        {
            var name = NameFromSave(save);
            if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (!name.StartsWith("UP_", StringComparison.OrdinalIgnoreCase) ||
                name.Length < 20 || name[name.Length - 17] != '_') return false;
            for (var i = name.Length - 16; i < name.Length; ++i)
                if (!Uri.IsHexDigit(name[i])) return false;
            return true;
        }

        internal static bool TryFind(
            SaveSystem system, string originalLevelId, string mapPath, out SaveFileHeader found)
        {
            found = null;
            if (system == null || system.SaveFiles == null ||
                string.IsNullOrEmpty(originalLevelId) || string.IsNullOrEmpty(mapPath)) return false;
            var key = Key(mapPath);
            foreach (var save in system.SaveFiles)
            {
                if (save == null || save.IsBroken ||
                    !string.Equals(save.LevelID, originalLevelId, StringComparison.Ordinal) ||
                    !string.Equals(NameFromSave(save), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found == null || save.Date > found.Date) found = save;
            }
            return found != null;
        }

        internal static bool TryFindCurrent(SaveSystem system, string levelId, out SaveFileHeader found) =>
            TryFind(system, levelId, ScenarioCareerMapLoader.CurrentMapPath, out found);

        // Permite autocargar un .sav personalizado sin confundirlo con el hospital original.
        internal static bool TryResolveMap(SaveFileHeader save, string originalLevelId, out string path)
        {
            path = null;
            if (!NameFromSave(save).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(save.LevelID, originalLevelId,
                    StringComparison.Ordinal)) return false;
            var key = NameFromSave(save);
            var folder = ScenarioEditorStorage.Folder;
            if (!Directory.Exists(folder)) return false;
            foreach (var candidate in Directory.GetFiles(folder, "*.upmap", SearchOption.AllDirectories))
            {
                if (!string.Equals(Key(candidate), key, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (string.Equals(ScenarioMapFormat.ReadHeader(candidate).baseHospitalId,
                        originalLevelId, StringComparison.Ordinal))
                    {
                        path = candidate;
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "No se pudo verificar un mapa para este .sav: " + exception.Message);
                }
            }
            UnderPressureEditorPlugin.Log?.LogError(
                "No se encontro el UPMap correspondiente al guardado " + key + ".");
            return false;
        }

        // Registrar las referencias creadas en memoria EN el serializador nativo.
        // Desregistrarlas al salir evita contaminar otros hospitales o perfiles.
        internal static void Attach(ScenarioPlotRuntime.Collection plots, SaveSystem system, string mapPath)
        {
            if (plots == null || system == null || string.IsNullOrEmpty(mapPath))
                throw new InvalidOperationException("No se pueden preparar las parcelas del guardado.");
            Detach();
            var table = NativeIdTable?.GetValue(system) as BiDictionary<int, object>;
            if (table == null)
                throw new InvalidOperationException("El SaveSystem nativo no expone el indice de assets.");
            _attachedSystem = system;
            var start = 1200000000 + (int)(HashPath(mapPath) & 0x0FFFFFFFUL);
            try
            {
                for (var i = 0; i < plots.Wrappers.Count; ++i)
                {
                    var definitionId = start + i * 2;
                    var wrapperId = definitionId + 1;
                    var definition = plots.Definitions[i];
                    var wrapper = plots.Wrappers[i];
                    object existing;
                    if (table.TryGetValue(definitionId, out existing) ||
                        table.TryGetValue(wrapperId, out existing))
                        throw new InvalidOperationException("Conflicto con un ID de asset nativo: " + definitionId);
                    table.Add(definitionId, definition);
                    AttachedAssets.Add(definitionId, definition);
                    table.Add(wrapperId, wrapper);
                    AttachedAssets.Add(wrapperId, wrapper);
                    wrapper.ID = wrapperId;
                }
                UpdateSerializers(system, table);
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Sav nativo: " + plots.Wrappers.Count + " parcelas registradas sin cambiar el perfil.");
            }
            catch
            {
                Detach();
                throw;
            }
        }

        internal static void Detach()
        {
            var system = _attachedSystem;
            _attachedSystem = null;
            if (system != null)
            {
                var table = NativeIdTable?.GetValue(system) as BiDictionary<int, object>;
                if (table != null)
                {
                    foreach (var pair in AttachedAssets)
                    {
                        object actual;
                        if (table.TryGetValue(pair.Key, out actual) && ReferenceEquals(actual, pair.Value))
                            table.Remove(pair.Key);
                    }
                    UpdateSerializers(system, table);
                }
            }
            AttachedAssets.Clear();
        }

        private static void UpdateSerializers(SaveSystem system, BiDictionary<int, object> table)
        {
            foreach (var field in NativeSerializers)
            {
                var serializer = AccessTools.Field(typeof(SaveSystem), field)
                    ?.GetValue(system) as FullSerializerSave.fsSerializer;
                serializer?.SetIDObjectMapping(table.FirstToSecond, table.SecondToFirst);
            }
        }

        // Se permite que el juego continue usando su SaveSystem y la ranura de campana ACTUAL.
        // Solo se cambia el nombre solicitado, para no sobrescribir el hospital original.
        internal static string GetNativeFileName(SaveData data, string nativeName)
        {
            if (!ScenarioCareerMapLoader.IsCustomSession || ScenarioEditorFlow.IsRunning ||
                data?.Level == null || !ScenarioCareerMapLoader.Match(data.Level))
                return nativeName;
            var path = ScenarioCareerMapLoader.CurrentMapPath;
            return string.IsNullOrEmpty(path) ? nativeName : Key(path);
        }

        internal static bool GetOriginalSave(SaveSystem system, string id, bool includeBroken,
            out SaveFileHeader result)
        {
            result = null;
            if (system?.SaveFiles == null) return false;
            foreach (var save in system.SaveFiles)
            {
                if (save == null || (save.IsBroken && !includeBroken) ||
                    !string.Equals(save.LevelID, id, StringComparison.Ordinal) || IsCustomSave(save))
                    continue;
                if (result == null || save.Date > result.Date) result = save;
            }
            return result != null;
        }
    }

    // Solo interceptar cuando se ha seleccionado un mapa personalizado.
    // Para el juego original no sustituir GetSaveForLevel: conservar su criterio nativo.
    [HarmonyPatch(typeof(SaveSystem), "GetSaveForLevel")]
    internal static class ScenarioNativeSaveLookupPatch
    {
        private static bool Prefix(SaveSystem __instance, string __0,
            ref SaveFileHeader __result)
        {
            if (ScenarioEditorFlow.IsRunning || !ScenarioCareerMapLoader.IsCustomSession ||
                !ScenarioCareerMapLoader.MatchesLevelId(__0)) return true;
            ScenarioNativeSaves.TryFindCurrent(__instance, __0, out __result);
            return false;
        }

        private static void Postfix(SaveSystem __instance, string __0, bool __1,
            ref SaveFileHeader __result)
        {
            if (ScenarioEditorFlow.IsRunning || ScenarioCareerMapLoader.IsCustomSession ||
                !ScenarioNativeSaves.IsCustomSave(__result)) return;
            ScenarioNativeSaves.GetOriginalSave(__instance, __0, __1, out __result);
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "SaveImplementation")]
    internal static class ScenarioNativeSaveNamePatch
    {
        private static void Prefix(SaveData __1, ref string __2)
        {
            __2 = ScenarioNativeSaves.GetNativeFileName(__1, __2);
        }
    }
}
