// Updated: 2026-11-02 - Panel Workshop con UI nativa de TPH (WorkshopPublishUIScreenPrefab) recolocada a 1400 px; conserva Autor/Título/Descripción editables, visibilidad y Actualizar/Crear nuevo.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using TH20;
using TH20.ExtContent;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    // Los mapas .upmap los instala Steam y los carga ScenarioWorkshopLibrary.
    // El gestor UGC nativo de TPH espera WorkshopMetaData.json y, al no hallarlo,
    // intenta descargar estos mismos artículos en cada arranque. Su callback
    // cierra todos los menús abiertos antes de mostrar el aviso de actualización.
    [HarmonyPatch(typeof(WorkshopItemDetail), "DoesItemNeedUpdating")]
    internal static class ScenarioWorkshopExcludeMapsFromNativeUpdaterPatch
    {
        private static void Postfix(WorkshopItemDetail __instance, ref bool __result)
        {
            if (!__result || __instance == null || !__instance.InstalledInfoValid)
                return;

            var folder = __instance.InstalledFolderPathSpec;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            try
            {
                if (Directory.EnumerateFiles(
                        folder, "*.upmap", SearchOption.AllDirectories).Any())
                    __result = false;
            }
            catch (IOException exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo identificar el artículo .upmap: " +
                    exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo identificar el artículo .upmap: " +
                    exception.Message);
            }
        }
    }

    internal enum ScenarioWorkshopVisibility
    {
        Public,
        FriendsOnly,
        Private
    }

    internal sealed class ScenarioWorkshopMetaData
    {
        internal const int CurrentVersion = 1;
        internal const string EntryName = "workshop.json";
        internal const string PreviewEntryName = "workshop_preview.jpg";
        internal const string SteamMetadataPrefix = "UnderPressure.UpMap.v1:";

        public int version = CurrentVersion;
        public string localId = "";
        public string publishedFileId = "";
        public string author = "";
        public string previewFile = PreviewEntryName;
        public string[] images = new string[0];
        public bool galleryManaged = false;

        internal const int MaxImages = 20;

        internal static ScenarioWorkshopMetaData NewDefault()
        {
            return new ScenarioWorkshopMetaData
            {
                version = CurrentVersion,
                localId = "",
                publishedFileId = "",
                author = UnderPressureEditorPlugin.ArchitectName ?? "",
                previewFile = PreviewEntryName,
                images = new string[0]
            };
        }
    }

    internal static class ScenarioWorkshopMetaStore
    {
        private static Level _level;
        private static string _path;
        private static ScenarioWorkshopMetaData _data;

        internal static void Reset(Level level = null)
        {
            _level = null;
            _path = null;
            _data = ScenarioWorkshopMetaData.NewDefault();
        }

        internal static ScenarioWorkshopMetaData Current(Level level)
        {
            var path = ScenarioEditorStorage.CurrentMapPath(level);

            if (!ReferenceEquals(_level, level))
            {
                _level = level;
                _path = path;
                _data = Read(path) ?? ScenarioWorkshopMetaData.NewDefault();
            }
            else if (!string.Equals(
                         _path ?? "", path ?? "",
                         StringComparison.OrdinalIgnoreCase))
            {
                // Al guardar por primera vez, conservar Autor/LocalId pendientes
                // y asociarlos al nuevo .upmap. Para cualquier otro cambio de
                // ruta se relee la metadata del mapa realmente cargado.
                if (string.IsNullOrEmpty(_path) &&
                    !string.IsNullOrEmpty(path) &&
                    _data != null)
                {
                    _path = path;
                }
                else
                {
                    _path = path;
                    _data = Read(path) ?? ScenarioWorkshopMetaData.NewDefault();
                }
            }

            if (_data == null) _data = ScenarioWorkshopMetaData.NewDefault();
            if (string.IsNullOrWhiteSpace(_data.author))
                _data.author = UnderPressureEditorPlugin.ArchitectName ?? "";
            if (!_data.galleryManaged && string.IsNullOrWhiteSpace(_data.previewFile))
                _data.previewFile = ScenarioWorkshopMetaData.PreviewEntryName;
            return _data;
        }

        internal static void SetAuthor(Level level, string author)
        {
            var data = Current(level);
            data.author = (author ?? "").Trim();
            PersistIfPossible(level);
        }

        internal static void EnsureSavedIdentity(Level level)
        {
            var data = Current(level);
            if (string.IsNullOrWhiteSpace(data.localId))
                data.localId = Guid.NewGuid().ToString("N");
            PersistIfPossible(level);
        }

        internal static void SetPublishedFileId(Level level, ulong id)
        {
            var data = Current(level);
            if (string.IsNullOrWhiteSpace(data.localId))
                data.localId = Guid.NewGuid().ToString("N");
            data.publishedFileId = id.ToString();
            PersistIfPossible(level);
        }

        internal static void SetPreview(Level level, byte[] jpg)
        {
            if (level == null || jpg == null || jpg.Length == 0)
                throw new ArgumentException("Vista previa vacía.");

            EnsureSavedIdentity(level);
            var committed = ScenarioEditorStorage.CommitCurrentMapFile(
                level, ScenarioWorkshopMetaData.PreviewEntryName, jpg);
            if (string.IsNullOrEmpty(committed))
                throw new IOException("No se pudo guardar la vista previa dentro del .upmap.");

            var data = Current(level);
            data.previewFile = ScenarioWorkshopMetaData.PreviewEntryName;
            PersistIfPossible(level);
        }

        // Todas las imágenes guardadas del mapa (rutas reales, en orden).
        internal static List<string> GalleryPaths(Level level)
        {
            var result = new List<string>();
            if (level == null) return result;

            var package = ScenarioEditorStorage.CurrentMapPath(level);
            if (string.IsNullOrEmpty(package) || !File.Exists(package)) return result;

            try
            {
                var data = Current(level);
                var folder = ScenarioMapPackage.Materialize(package);

                var names = new List<string>();
                if (data.images != null)
                    foreach (var name in data.images)
                        if (!string.IsNullOrWhiteSpace(name) &&
                            !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                            names.Add(name);

                // Compatibilidad: mapas con una sola vista previa antigua.
                if (!data.galleryManaged)
                {
                    var primary = string.IsNullOrWhiteSpace(data.previewFile)
                        ? ScenarioWorkshopMetaData.PreviewEntryName
                        : data.previewFile;
                    if (!names.Contains(primary, StringComparer.OrdinalIgnoreCase))
                        names.Insert(0, primary);
                }

                // La miniatura siempre ocupa el primer lugar de la galería.
                var thumbnail = Path.GetFileName(data.previewFile ?? "");
                var thumbnailIndex = names.FindIndex(name =>
                    string.Equals(name, thumbnail, StringComparison.OrdinalIgnoreCase));
                if (thumbnailIndex > 0)
                {
                    names.RemoveAt(thumbnailIndex);
                    names.Insert(0, thumbnail);
                }

                foreach (var name in names)
                {
                    var path = Path.Combine(folder, Path.GetFileName(name));
                    if (File.Exists(path)) result.Add(path);
                }
            }
            catch
            {
            }

            return result;
        }

        // Añade una imagen (NO sustituye a las existentes).
        internal static void AddImage(Level level, byte[] jpg)
        {
            if (level == null || jpg == null || jpg.Length == 0)
                throw new ArgumentException("Imagen vacía.");

            EnsureSavedIdentity(level);

            var existing = GalleryPaths(level)
                .Select(path => Path.GetFileName(path)).ToList();

            var entry = "workshop_image_" +
                        Guid.NewGuid().ToString("N").Substring(0, 8) + ".jpg";

            var committed = ScenarioEditorStorage.CommitCurrentMapFile(level, entry, jpg);
            if (string.IsNullOrEmpty(committed))
                throw new IOException("No se pudo guardar la imagen dentro del .upmap.");

            existing.Add(entry);

            var data = Current(level);
            data.images = existing.ToArray();
            data.galleryManaged = true;

            // La primera imagen es la miniatura de Steam hasta que se elija otra.
            if (string.IsNullOrEmpty(PreviewPath(level)))
                data.previewFile = entry;

            PersistIfPossible(level);
        }

        // Elimina una imagen de la lista del mapa.
        internal static void RemoveImage(Level level, string entry)
        {
            if (level == null || string.IsNullOrWhiteSpace(entry)) return;

            var names = GalleryPaths(level).Select(path => Path.GetFileName(path)).ToList();
            names.RemoveAll(name => string.Equals(name, entry, StringComparison.OrdinalIgnoreCase));

            var data = Current(level);
            data.images = names.ToArray();
            data.galleryManaged = true;

            // Si era la miniatura, pasa a ser la primera que quede (o ninguna).
            if (string.Equals(Path.GetFileName(data.previewFile ?? ""), entry,
                    StringComparison.OrdinalIgnoreCase))
                data.previewFile = names.Count > 0 ? names[0] : "";

            if (!TryDeleteMapFile(level, entry))
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Workshop: imagen retirada de la lista; el archivo '" + entry +
                    "' queda dentro del .upmap (no hay API de borrado).");

            PersistIfPossible(level);
        }

        // Marca una imagen como miniatura (la que se subirá a Steam en el futuro).
        internal static void SetThumbnail(Level level, string entry)
        {
            if (level == null || string.IsNullOrWhiteSpace(entry)) return;

            var data = Current(level);
            var thumbnail = Path.GetFileName(entry);
            var images = GalleryPaths(level)
                .Select(path => Path.GetFileName(path))
                .Where(name => !string.Equals(
                    name, thumbnail, StringComparison.OrdinalIgnoreCase))
                .ToList();
            images.Insert(0, thumbnail);
            data.images = images.ToArray();
            data.galleryManaged = true;
            data.previewFile = thumbnail;
            PersistIfPossible(level);
        }

        private static bool TryDeleteMapFile(Level level, string entry)
        {
            try
            {
                foreach (var method in typeof(ScenarioEditorStorage).GetMethods(
                             BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var name = method.Name;
                    if (name.IndexOf("CurrentMapFile", StringComparison.Ordinal) < 0) continue;
                    if (name.IndexOf("Delete", StringComparison.Ordinal) < 0 &&
                        name.IndexOf("Remove", StringComparison.Ordinal) < 0)
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != 2 ||
                        parameters[0].ParameterType != typeof(Level) ||
                        parameters[1].ParameterType != typeof(string))
                        continue;

                    method.Invoke(null, new object[] { level, entry });
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal static string PreviewPath(Level level)
        {
            var package = ScenarioEditorStorage.CurrentMapPath(level);
            if (string.IsNullOrEmpty(package) || !File.Exists(package)) return null;

            var data = Current(level);
            if (data.galleryManaged && string.IsNullOrWhiteSpace(data.previewFile))
                return null;

            try
            {
                var folder = ScenarioMapPackage.Materialize(package);
                var name = string.IsNullOrWhiteSpace(data.previewFile)
                    ? ScenarioWorkshopMetaData.PreviewEntryName
                    : Path.GetFileName(data.previewFile);
                var path = Path.Combine(folder, name);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryGetPublishedId(Level level, out ulong id)
        {
            id = 0UL;
            var value = Current(level).publishedFileId;
            ulong raw;
            if (!ulong.TryParse(value, out raw) || raw == 0UL) return false;
            id = raw;
            return true;
        }

        internal static ScenarioWorkshopMetaData ReadFromMap(string package)
        {
            return Read(package) ?? ScenarioWorkshopMetaData.NewDefault();
        }

        internal static bool TryReadFromMap(
            string package, out ScenarioWorkshopMetaData data)
        {
            data = Read(package);
            return data != null;
        }

        private static ScenarioWorkshopMetaData Read(string package)
        {
            if (string.IsNullOrEmpty(package) || !File.Exists(package)) return null;
            try
            {
                var json = ScenarioMapPackage.ReadText(
                    package, ScenarioWorkshopMetaData.EntryName, 64 * 1024);
                var data = JsonUtility.FromJson<ScenarioWorkshopMetaData>(json);
                if (data == null) return null;
                if (data.version <= 0) data.version = ScenarioWorkshopMetaData.CurrentVersion;
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static void PersistIfPossible(Level level)
        {
            var package = ScenarioEditorStorage.CurrentMapPath(level);
            if (string.IsNullOrEmpty(package) || !File.Exists(package)) return;

            var data = Current(level);
            if (string.IsNullOrWhiteSpace(data.localId))
                data.localId = Guid.NewGuid().ToString("N");

            var json = JsonUtility.ToJson(data, true);
            var bytes = new UTF8Encoding(false).GetBytes(json);
            var committed = ScenarioEditorStorage.CommitCurrentMapFile(
                level, ScenarioWorkshopMetaData.EntryName, bytes);
            if (string.IsNullOrEmpty(committed))
                throw new IOException("No se pudo actualizar workshop.json dentro del .upmap.");

            _path = ScenarioEditorStorage.CurrentMapPath(level);
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "BeginNewMap")]
    internal static class ScenarioWorkshopNewMapPatch
    {
        private static void Postfix(Level level)
        {
            ScenarioWorkshopMetaStore.Reset(level);
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "Save")]
    internal static class ScenarioWorkshopSavePatch
    {
        private static void Postfix(Level level, string __result)
        {
            if (level == null || string.IsNullOrEmpty(__result)) return;
            try
            {
                ScenarioWorkshopMetaStore.EnsureSavedIdentity(level);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo guardar la identidad local: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "Load")]
    internal static class ScenarioWorkshopLoadMetaPatch
    {
        private static void Postfix(Level level, string path)
        {
            ScenarioWorkshopMetaStore.Reset(level);

            if (!ScenarioWorkshopLibrary.IsWorkshopMapPath(path)) return;
            try
            {
                var data = ScenarioWorkshopMetaStore.Current(level);
                var architect = !string.IsNullOrWhiteSpace(data.author)
                    ? data.author
                    : UnderPressureEditorPlugin.ArchitectName;

                AccessTools.Field(typeof(ScenarioEditorStorage), "_architectLevel")
                    ?.SetValue(null, level);
                AccessTools.Field(typeof(ScenarioEditorStorage), "_architectName")
                    ?.SetValue(null, architect);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo restaurar el autor del mapa suscrito: " + exception);
            }
        }
    }

    // ================================================================
    // MAPAS SUSCRITOS
    // Se añaden al mismo listado actual. Por ahora NO se filtran por metadata:
    // cualquier .upmap instalado en un artículo suscrito aparece.
    // ================================================================
    internal static class ScenarioWorkshopLibrary
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> WorkshopMaps =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastRefreshUtc = DateTime.MinValue;

        internal static List<string> EnumerateSubscribedMaps(bool force = false)
        {
            lock (Sync)
            {
                if (!force && (DateTime.UtcNow - _lastRefreshUtc).TotalSeconds < 3)
                    return WorkshopMaps.ToList();

                WorkshopMaps.Clear();
                _lastRefreshUtc = DateTime.UtcNow;

                try
                {
                    if (!WorkshopUtils.AreSteamWorkshopFeaturesAvailable())
                        return WorkshopMaps.ToList();

                    var ids = SteamWorkshopBridge.GetSubscribedItems();
                    if (ids.Length == 0) return WorkshopMaps.ToList();

                    for (var index = 0; index < ids.Length; ++index)
                    {
                        var id = ids[index];
                        var state = SteamWorkshopBridge.GetItemState(id);
                        var installed = SteamWorkshopBridge.IsInstalled(state);
                        var needsUpdate = SteamWorkshopBridge.NeedsUpdate(state);

                        // El gestor nativo de TPH ya descarga y confirma las
                        // actualizaciones. Aquí solo se enumeran instalaciones
                        // completas para no provocar avisos repetidos.
                        if (!installed || needsUpdate) continue;

                        string folder;
                        if (!SteamWorkshopBridge.TryGetInstallFolder(id, out folder) ||
                            string.IsNullOrWhiteSpace(folder) ||
                            !Directory.Exists(folder))
                            continue;

                        foreach (var map in Directory.GetFiles(
                                     folder, "*" + ScenarioMapPackage.Extension,
                                     SearchOption.AllDirectories))
                        {
                            try
                            {
                                var full = Path.GetFullPath(map);
                                ScenarioMapFormat.ReadHeader(full);
                                WorkshopMaps.Add(full);
                            }
                            catch (Exception exception)
                            {
                                UnderPressureEditorPlugin.Log?.LogWarning(
                                    "Workshop: .upmap ignorado " + map + ": " +
                                    exception.Message);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Workshop: no se pudieron enumerar los artículos suscritos: " +
                        exception);
                }

                return WorkshopMaps.ToList();
            }
        }

        internal static bool IsWorkshopMapPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return false; }

            lock (Sync)
                if (WorkshopMaps.Contains(full)) return true;

            return EnumerateSubscribedMaps(true).Any(candidate =>
                string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool AllowedStartsWith(
            string instance, string value, StringComparison comparison)
        {
            return instance.StartsWith(value, comparison) || IsWorkshopMapPath(instance);
        }

        internal static bool AllowedDirectPackage(string path, string root)
        {
            return ScenarioMapPackage.IsDirectPackage(path, root) ||
                   IsWorkshopMapPath(path);
        }
    }

    internal static class ScenarioMapSources
    {
        internal static bool LocalEnabled { get; private set; } = true;
        internal static bool SteamEnabled { get; private set; } = true;

        internal static void ToggleLocal()
        {
            LocalEnabled = !LocalEnabled;
        }

        internal static void ToggleSteam()
        {
            SteamEnabled = !SteamEnabled;
        }

        internal static List<string> EnumerateEnabledMaps(bool forceSteam = false)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (LocalEnabled && Directory.Exists(ScenarioEditorStorage.Folder))
                foreach (var path in ScenarioMapPackage.Enumerate(
                             ScenarioEditorStorage.Folder))
                    result.Add(Path.GetFullPath(path));

            if (SteamEnabled)
                foreach (var path in ScenarioWorkshopLibrary.EnumerateSubscribedMaps(forceSteam))
                    result.Add(Path.GetFullPath(path));

            return result.ToList();
        }

        internal static bool IsLocalMapPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var root = Path.GetFullPath(ScenarioEditorStorage.Folder).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(
                    root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetAuthor(string path, out string author)
        {
            author = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            if (IsLocalMapPath(path))
            {
                var root = Path.GetFullPath(ScenarioEditorStorage.Folder).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var relative = Path.GetFullPath(path).Substring(root.Length);
                var separator = relative.IndexOfAny(new[]
                {
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar
                });
                author = separator > 0
                    ? relative.Substring(0, separator)
                    : UnderPressureEditorPlugin.ArchitectName;
                return !string.IsNullOrWhiteSpace(author) &&
                       !author.StartsWith("_", StringComparison.Ordinal);
            }

            ScenarioWorkshopMetaData metadata;
            if (!ScenarioWorkshopMetaStore.TryReadFromMap(path, out metadata) ||
                metadata == null || string.IsNullOrWhiteSpace(metadata.author))
                return false;

            author = metadata.author.Trim();
            return author.Length > 0;
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "ListSaved")]
    internal static class ScenarioWorkshopListSavedPatch
    {
        private static void Postfix(ref List<ScenarioEditorStorage.MapInfo> __result)
        {
            if (__result == null)
                __result = new List<ScenarioEditorStorage.MapInfo>();

            if (!ScenarioMapSources.LocalEnabled)
                __result.RemoveAll(item => item != null &&
                    ScenarioMapSources.IsLocalMapPath(item.Path));

            if (!ScenarioMapSources.SteamEnabled)
                return;

            var existing = new HashSet<string>(
                __result.Where(item => item != null)
                    .Select(item => Path.GetFullPath(item.Path)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var path in ScenarioWorkshopLibrary.EnumerateSubscribedMaps(true))
            {
                if (existing.Contains(path)) continue;

                try
                {
                    var header = ScenarioMapFormat.ReadHeader(path);
                    __result.Add(new ScenarioEditorStorage.MapInfo
                    {
                        Path = path,
                        Name = string.IsNullOrWhiteSpace(header.name)
                            ? Path.GetFileNameWithoutExtension(path)
                            : header.name,
                        BaseId = header.baseHospitalId ?? "",
                        BaseName = header.baseHospitalName ?? "Desconocido",
                        Valid = true,
                        ModifiedUtc = File.GetLastWriteTimeUtc(path)
                    });
                    existing.Add(path);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Workshop: no se pudo añadir el mapa " + path + ": " +
                        exception.Message);
                }
            }

            __result.Sort((left, right) =>
            {
                var cmp = right.ModifiedUtc.CompareTo(left.ModifiedUtc);
                return cmp != 0
                    ? cmp
                    : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });
        }
    }

    // ScenarioEditorStorage.Load protege normalmente Mods/maps.
    // Para los elementos del Workshop se mantienen TODAS sus validaciones de
    // formato, pero se permite la ruta instalada por Steam sin copiar el archivo.
    [HarmonyPatch(typeof(ScenarioEditorStorage), "Load")]
    internal static class ScenarioWorkshopDirectLoadPatch
    {
        private static readonly MethodInfo StartsWithMethod = AccessTools.Method(
            typeof(string), "StartsWith",
            new[] { typeof(string), typeof(StringComparison) });

        private static readonly MethodInfo OriginalDirectPackageMethod =
            AccessTools.Method(typeof(ScenarioMapPackage), "IsDirectPackage",
                new[] { typeof(string), typeof(string) });

        private static readonly MethodInfo AllowedStartsWithMethod =
            AccessTools.Method(typeof(ScenarioWorkshopLibrary), "AllowedStartsWith");

        private static readonly MethodInfo AllowedDirectPackageMethod =
            AccessTools.Method(typeof(ScenarioWorkshopLibrary), "AllowedDirectPackage");

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method &&
                    method == StartsWithMethod)
                {
                    yield return new CodeInstruction(
                        OpCodes.Call, AllowedStartsWithMethod)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };
                    continue;
                }

                if (instruction.operand is MethodInfo direct &&
                    direct == OriginalDirectPackageMethod)
                {
                    yield return new CodeInstruction(
                        OpCodes.Call, AllowedDirectPackageMethod)
                    {
                        labels = instruction.labels,
                        blocks = instruction.blocks
                    };
                    continue;
                }

                yield return instruction;
            }
        }
    }

    // ================================================================
    // BOTÓN EN ESC
    // ================================================================


    // ================================================================
    // PANEL WORKSHOP
    // - Usa directamente el WorkshopPublishUIScreenPrefab nativo de TPH
    //   (fondo, paneles, botones, tipografías y sprites NATIVOS).
    // - Se recoloca a 1400 px de ancho con la estructura:
    //
    //      [Steam Workshop]            [Compartir en Steam Workshop]  (X)
    //      [Autor ...............]  [Título ...........................]
    //      [Imagen previa ........]  [Descripción .......................]
    //      [Capturar imagen]         [Público][Solo amigos][Oculto]
    //                                [Actualizar][Crear como nuevo]
    //
    // - Parámetros propios conservados: Autor, Título y Descripción
    //   editables, visibilidad Público / Solo amigos / Oculto,
    //   Actualizar artículo / Crear como artículo nuevo.
    // - Si el recolocado falla, el panel nativo queda tal cual (log de error).
    // ================================================================
    internal sealed class ScenarioWorkshopPanel : MonoBehaviour
    {
        private static ScenarioWorkshopPanel _active;

        private static readonly FieldInfo UiParentField =
            AccessTools.Field(typeof(ExtContentUIManager), "_uiParentTransform");
        private static readonly MethodInfo SetBeforeMessageBoxMethod =
            AccessTools.Method(
                typeof(ExtContentUIManager),
                "SetUIGameObjectBeforeMessageBox");

        // ---------------- GEOMETRÍA (px, origen arriba-izquierda) ----------------
        private const float PanelWidth = 1400f;
        private const float PanelHeight = 900f;

        private const float LeftX = 36f;
        private const float LeftW = 494f;
        private const float RightX = 544f;
        private const float RightW = 820f;

        private const float TopY = 44f;
        private const float TopH = 80f;
        private const float RowY = 138f;
        private const float RowH = 78f;
        private const float MainY = 230f;
        private const float MainH = 410f;
        private const float CaptureY = 652f;
        private const float CaptureH = 54f;
        private const float VisibilityY = 652f;
        private const float VisibilityH = 86f;
        private const float ActionsY = 750f;
        private const float ActionsH = 86f;

        private const float RowLabelWidth = 150f;

        private Level _level;
        private WorkshopPublishUIScreen _nativeScreen;

        private DynamicButton _buttonCloseMenu;
        private DynamicButton _buttonCreateUpdate;
        private DynamicButton _buttonUpdateOther;
        private DynamicButton _buttonCreateAsNew;
        private DynamicButton _buttonChoosePreviewImage;
        private DynamicButton _buttonChoosePreviewImageLarge;
        private DynamicButton _buttonResetPreviewImage;
        private DynamicButton _buttonVisibilityPrivate;
        private DynamicButton _buttonVisibilityFriends;
        private DynamicButton _buttonVisibilityPublic;
        private DynamicButton _buttonSteamWorkshop;
        private DynamicButton _buttonSteamWorkshopRefresh;
        private DynamicButton _buttonUserAgreementLink;
        private DynamicButton _buttonCapture;

        private InputField _inputTitle;
        private InputField _inputDescription;
        private InputField _inputAuthor;
        private Image _imagePreviewImage;
        private Image _imageWorkshopBusyIcon;
        private Image _imageBusyModeInputBlocker;
        private TMP_Text _textScrollPanelLabel;
        private TMP_Text _textScrollPanelContent;
        private ScrollRect _scrollerPackContents;

        private float _busyIconAngularVelocity;
        private Texture2D _previewTexture;
        private bool _publishing;
        private ScenarioWorkshopVisibility _visibility = ScenarioWorkshopVisibility.Private;

        internal static bool IsOpen =>
            _active != null &&
            _active.gameObject != null &&
            _active.gameObject.activeInHierarchy;

        internal static void Open(Level level)
        {
            if (level == null ||
                !ScenarioEditorFlow.IsRunning ||
                !ScenarioEditorSession.Active)
                return;

            if (_active != null)
            {
                _active._level = level;
                _active.gameObject.SetActive(true);
                PlaceBelowMessageBoxes(_active.gameObject);
                _active.LoadFieldsFromModel();
                _active.Refresh();
                return;
            }

            var uiManager =
                ExtContentUtils.ExtContentManager?.ExtContentUIManager;
            var prefab =
                uiManager?.Config?.WorkshopPublishUIScreenPrefab;

            if (uiManager == null || prefab == null)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: no existe el WorkshopPublishUIScreenPrefab nativo de TPH.");
                return;
            }

            var parent = UiParentField?.GetValue(uiManager) as Transform;
            if (parent == null)
                parent = FindEditorUiHost();

            if (parent == null)
            {
                UnityEngine.Debug.LogError("Workshop: sin contenedor de UI.");
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: no se encontró el contenedor nativo de UI.");
                return;
            }

            // MISMO PREFAB que usa ExtContentUIManager.GetWorkshopPublishUIScreen().
            var root = UnityEngine.Object.Instantiate(prefab, parent, false);
            root.name = "UP Workshop Map Publisher - Native";

            if (SetBeforeMessageBoxMethod != null)
            {
                try
                {
                    SetBeforeMessageBoxMethod.Invoke(
                        uiManager, new object[] { root });
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Workshop: no se pudo aplicar el orden nativo del prefab: " +
                        exception.Message);
                }
            }

            var nativeScreen = root.GetComponent<WorkshopPublishUIScreen>();
            if (nativeScreen == null)
            {
                UnityEngine.Object.Destroy(root);
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: el prefab nativo no contiene WorkshopPublishUIScreen.");
                return;
            }

            // Se conserva TODO el prefab visual y se desactiva únicamente
            // la lógica de publicación nativa: nuestro contenido es un .upmap.
            nativeScreen.enabled = false;

            _active = root.AddComponent<ScenarioWorkshopPanel>();
            _active._level = level;
            _active._nativeScreen = nativeScreen;

            if (!_active.BindNativeControls())
            {
                UnityEngine.Object.Destroy(root);
                _active = null;
                return;
            }

            root.SetActive(true);

            try
            {
                _active.ApplyCompactLayout();
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: no se pudo recolocar el panel nativo (se deja el layout original): " +
                    exception);
            }

            // El panel va POR DEBAJO de los avisos del juego (si no, no se leen).
            PlaceBelowMessageBoxes(_active.gameObject);
            _active.LoadFieldsFromModel();
            _active.Refresh();
        }

        // Orden nativo: el panel queda justo debajo de los cuadros de mensaje del juego.
        private static void PlaceBelowMessageBoxes(GameObject root)
        {
            if (root == null || SetBeforeMessageBoxMethod == null) return;

            var uiManager = ExtContentUtils.ExtContentManager?.ExtContentUIManager;
            if (uiManager == null) return;

            try
            {
                SetBeforeMessageBoxMethod.Invoke(uiManager, new object[] { root });
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo ordenar el panel bajo los avisos: " +
                    exception.Message);
            }
        }

        internal static void CloseActive()
        {
            if (_active != null)
                _active.Close();
        }

        private static Transform FindEditorUiHost()
        {
            var menus = Resources.FindObjectsOfTypeAll<ScenarioEditorStorageMenu>();
            foreach (var menu in menus)
            {
                if (menu == null || !menu.gameObject.scene.IsValid())
                    continue;

                var canvas = menu.GetComponentInParent<Canvas>();
                if (canvas != null)
                    return canvas.transform;

                return menu.transform.parent != null
                    ? menu.transform.parent
                    : menu.transform;
            }

            return Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(canvas => canvas != null && canvas.gameObject.scene.IsValid())
                .OrderByDescending(canvas => canvas.sortingOrder)
                .Select(canvas => canvas.transform)
                .FirstOrDefault();
        }

        private void Awake()
        {
            if (_active == null)
                _active = this;
        }

        private void Update()
        {
            if (_publishing &&
                _imageWorkshopBusyIcon != null &&
                _imageWorkshopBusyIcon.gameObject.activeSelf)
            {
                ExtContentUIUtils.ProcessBusyIndicatorAnimation(
                    _imageWorkshopBusyIcon,
                    _busyIconAngularVelocity);
            }

            if (Input.GetKeyDown(KeyCode.Escape) &&
                !_publishing &&
                !ScenarioWorkshopCapture.IsActive)
                Close();
        }

        // ================================================================
        // ENLACE CON LOS CONTROLES NATIVOS
        // ================================================================
        private bool BindNativeControls()
        {
            if (_nativeScreen == null)
                return false;

            _buttonCloseMenu = NativeField<DynamicButton>("_buttonCloseMenu");
            _buttonCreateUpdate = NativeField<DynamicButton>("_buttonCreateUpdate");
            _buttonUpdateOther = NativeField<DynamicButton>("_buttonUpdateOther");
            _buttonCreateAsNew = NativeField<DynamicButton>("_buttonCreateAsNew");
            _buttonChoosePreviewImage = NativeField<DynamicButton>("_buttonChoosePreviewImage");
            _buttonChoosePreviewImageLarge = NativeField<DynamicButton>("_buttonChoosePreviewImageLarge");
            _buttonResetPreviewImage = NativeField<DynamicButton>("_buttonResetPreviewImage");
            _buttonVisibilityPrivate = NativeField<DynamicButton>("_buttonVisibilityPrivate");
            _buttonVisibilityFriends = NativeField<DynamicButton>("_buttonVisibilityFriends");
            _buttonVisibilityPublic = NativeField<DynamicButton>("_buttonVisibilityPublic");
            _buttonSteamWorkshop = NativeField<DynamicButton>("_buttonSteamWorkshop");
            _buttonSteamWorkshopRefresh = NativeField<DynamicButton>("_buttonSteamWorkshopRefresh");
            _buttonUserAgreementLink = NativeField<DynamicButton>("_buttonUserAgreementLink");

            _inputTitle = NativeField<InputField>("_inputTitle");
            _inputDescription = NativeField<InputField>("_inputDescription");
            _imagePreviewImage = NativeField<Image>("_imagePreviewImage");
            _imageWorkshopBusyIcon = NativeField<Image>("_imageWorkshopBusyIcon");
            _imageBusyModeInputBlocker = NativeField<Image>("_imageBusyModeInputBlocker");
            _textScrollPanelLabel = NativeField<TMP_Text>("_textScollPanelLabel");
            _textScrollPanelContent = NativeField<TMP_Text>("_textScollPanelContent");
            _scrollerPackContents = NativeField<ScrollRect>("_scrollerPackContents");

            var busyVelocityField = AccessTools.Field(
                typeof(WorkshopPublishUIScreen), "_busyIconAngularVelocity");
            if (busyVelocityField != null)
                _busyIconAngularVelocity = Convert.ToSingle(
                    busyVelocityField.GetValue(_nativeScreen));

            var required =
                _buttonCloseMenu != null &&
                _buttonCreateUpdate != null &&
                _buttonCreateAsNew != null &&
                _inputTitle != null &&
                _inputDescription != null &&
                _imagePreviewImage != null &&
                _buttonVisibilityPrivate != null &&
                _buttonVisibilityFriends != null &&
                _buttonVisibilityPublic != null;

            if (!required)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: la jerarquía/campos del prefab nativo no coincide con TPH.");
                return false;
            }

            // Título y Descripción editables (se guardan en los ajustes del escenario).
            _inputTitle.readOnly = false;
            _inputDescription.readOnly = false;
            _inputDescription.lineType = InputField.LineType.MultiLineNewline;
            _inputTitle.onValueChanged.AddListener(_ => RefreshActionButtons());

            Bind(_buttonCloseMenu, Close);
            Bind(_buttonCreateUpdate, () => BeginPublish(false));
            Bind(_buttonCreateAsNew, () => BeginPublish(true));

            Bind(_buttonVisibilityPublic,
                () => SetVisibility(ScenarioWorkshopVisibility.Public));
            Bind(_buttonVisibilityFriends,
                () => SetVisibility(ScenarioWorkshopVisibility.FriendsOnly));
            Bind(_buttonVisibilityPrivate,
                () => SetVisibility(ScenarioWorkshopVisibility.Private));

            Bind(_buttonSteamWorkshop, OpenSteamWorkshop);

            // Resto de controles nativos, como en el BUENO.
            Bind(_buttonChoosePreviewImage, BeginCapture);

            Bind(_buttonChoosePreviewImageLarge, BeginCapture);
            Bind(_buttonSteamWorkshopRefresh,
                () => ScenarioWorkshopLibrary.EnumerateSubscribedMaps(true));
            Bind(_buttonUserAgreementLink, OpenUserAgreement);

            if (_buttonResetPreviewImage != null)
            {
                _buttonResetPreviewImage.onPrimaryDown.RemoveAllListeners();
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonResetPreviewImage, false);
            }

            if (_buttonUpdateOther != null)
            {
                _buttonUpdateOther.onPrimaryDown.RemoveAllListeners();
                ExtContentUIUtils.SetSelectableInteractable(_buttonUpdateOther, false);
            }

            SetNativeButtonText(_buttonSteamWorkshop,
                EditorLocalization.Get("editor.workshop.view"));
            SetNativeButtonText(_buttonCreateUpdate,
                EditorLocalization.Get("editor.workshop.update_item"));
            SetNativeButtonText(_buttonCreateAsNew,
                EditorLocalization.Get("editor.workshop.create_new_item"));

            SetBusy(false);
            return true;
        }

        private T NativeField<T>(string name) where T : class
        {
            var field = AccessTools.Field(typeof(WorkshopPublishUIScreen), name);
            return field?.GetValue(_nativeScreen) as T;
        }

        private static void Bind(DynamicButton button, Action action)
        {
            if (button == null)
                return;

            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => action?.Invoke());
        }

        private static void UnbindAndHide(DynamicButton button)
        {
            if (button == null)
                return;

            button.onPrimaryDown.RemoveAllListeners();
            button.gameObject.SetActive(false);
        }

        private static void DisableLocalisers(Transform root)
        {
            if (root == null)
                return;

            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null ||
                    string.IsNullOrEmpty(component.GetType().FullName))
                    continue;

                if (component.GetType().FullName.StartsWith(
                        "I2.Loc.", StringComparison.Ordinal))
                    component.enabled = false;
            }
        }

        private static void SetNativeButtonText(DynamicButton button, string value)
        {
            if (button == null)
                return;

            DisableLocalisers(button.transform);

            var tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = value ?? "";
                return;
            }

            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
                text.text = value ?? "";
        }

        // ================================================================
        // RECOLOCADO A 1400 px
        // Solo cambia geometría. Todo el aspecto visual sigue siendo el nativo.
        // ================================================================
        private void ApplyCompactLayout()
        {
            var root = (RectTransform)transform;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);

            var known = new List<Transform>();
            AddKnown(known, _inputTitle);
            AddKnown(known, _inputDescription);
            AddKnown(known, _imagePreviewImage);
            AddKnown(known, _buttonChoosePreviewImage);
            AddKnown(known, _buttonChoosePreviewImageLarge);
            AddKnown(known, _buttonResetPreviewImage);
            AddKnown(known, _buttonVisibilityPublic);
            AddKnown(known, _buttonVisibilityFriends);
            AddKnown(known, _buttonVisibilityPrivate);
            AddKnown(known, _buttonSteamWorkshop);
            AddKnown(known, _buttonSteamWorkshopRefresh);
            AddKnown(known, _buttonUserAgreementLink);
            AddKnown(known, _buttonCreateUpdate);
            AddKnown(known, _buttonCreateAsNew);
            AddKnown(known, _buttonUpdateOther);
            AddKnown(known, _buttonCloseMenu);
            AddKnown(known, _scrollerPackContents);

            var titleSec = FindSection(root, known, _inputTitle);
            var descSec = FindSection(root, known, _inputDescription);
            var previewSec = FindSection(root, known, _imagePreviewImage,
                _buttonChoosePreviewImage, _buttonChoosePreviewImageLarge,
                _buttonResetPreviewImage);
            var brandSec = FindSection(root, known, _buttonSteamWorkshop,
                _buttonSteamWorkshopRefresh, _buttonUserAgreementLink);
            var visSec = FindSection(root, known, _buttonVisibilityPublic,
                _buttonVisibilityFriends, _buttonVisibilityPrivate);
            var actSec = FindSection(root, known, _buttonCreateUpdate,
                _buttonCreateAsNew, _buttonUpdateOther);
            var scrollSec = FindSection(root, known, _scrollerPackContents);

            UnderPressureEditorPlugin.Log?.LogInfo(
                "Workshop layout nativo: título=" + PathOf(titleSec) +
                " | descripción=" + PathOf(descSec) +
                " | preview=" + PathOf(previewSec) +
                " | marca=" + PathOf(brandSec) +
                " | visibilidad=" + PathOf(visSec) +
                " | acciones=" + PathOf(actSec) +
                " | paquete=" + PathOf(scrollSec));

            // ---- ancho nativo REAL del contenido (en unidades de canvas) ----
            var measured = new[] { brandSec, titleSec, descSec, previewSec, visSec, actSec }
                .Where(x => x != null).ToArray();
            var parentCanvas = root.GetComponentInParent<Canvas>();
            var rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            var canvasUnit = rootCanvas != null
                ? Mathf.Abs(rootCanvas.transform.lossyScale.x)
                : 1f;
            if (canvasUnit < .0001f) canvasUnit = 1f;

            var nativeWidth = MeasureCanvasWidth(measured, canvasUnit);

            // ---- textos sin la palabra "Workshop" (antes de clonar/mover) ----
            Transform labelDesc = null;

            if (titleSec != null)
                SetLabelText(titleSec, known, _inputTitle.transform,
                    EditorLocalization.Get("editor.workshop.name"));
            if (descSec != null)
                labelDesc = SetLabelText(
                    descSec, known, _inputDescription.transform,
                    EditorLocalization.Get("editor.workshop.description"));
            if (previewSec != null)
            {
                var obsoletePreviewLabel = SetLabelText(previewSec, known, null, "");
                if (obsoletePreviewLabel != null)
                    obsoletePreviewLabel.gameObject.SetActive(false);
            }

            var headerText = SetHeaderText(
                root, known, brandSec,
                EditorLocalization.Get("editor.workshop.share_header"));
            var banner = FindHeaderBannerRect(headerText);

            // ---- logo + botón (se hace antes de medir nada más) ----
            var logo = brandSec != null ? FindBrandLogo(brandSec, known) : null;

            // ================================================================
            // Se mueven PANELES COMPLETOS (fondo + contenido), nunca controles sueltos.
            // ================================================================
            if (titleSec != null && descSec != null && previewSec != null &&
                previewSec.parent != null)
            {
                var rt = WorldRect(titleSec);
                var rd = WorldRect(descSec);
                var rp = WorldRect(previewSec);
                var ra = actSec != null ? WorldRect(actSec) : rd;

                _canvasUnit = canvasUnit;
                _previewPanelRoot = (RectTransform)previewSec;

                // Medidas sobre el DIBUJO real de cada panel (no sobre su rectángulo).
                var visTitle = VisibleRect(titleSec);
                var visDesc = VisibleRect(descSec);
                var visPreview = VisibleRect(previewSec);

                var gap = visTitle.yMin - visDesc.yMax;
                if (gap < 0f || gap > rt.height) gap = rt.height * .3f;
                var rowHeight = rt.height;

                // Hueco entre columnas = hueco entre paneles verticales.
                var widen = Mathf.Max(0f, (visTitle.xMin - visPreview.xMax) - gap);

                // La barra nativa se clona ANTES de ocultar nada: será la horizontal.
                if (scrollSec != null)
                    _galleryBarClone = CloneNativeScrollbar(scrollSec);

                // Restos nativos de "Este paquete contiene" (cuadro oscuro, barra vertical).
                if (scrollSec != null)
                    HidePackLeftovers(root, scrollSec, _galleryBarClone);

                // Cuánto suben las filas: hasta quedar justo bajo la cabecera.
                var up = 0f;
                if (banner.HasValue)
                    up = Mathf.Max(0f, (banner.Value.yMin - gap) - rt.yMax);

                // ---- AUTOR: copia del panel de Nombre, a la izquierda ----
                var authorSec = UnityEngine.Object.Instantiate(
                    titleSec.gameObject, previewSec.parent, false).transform;
                authorSec.name = "UP Autor";

                var authorRect = (RectTransform)authorSec;
                DisableAutoLayout(authorRect);
                authorSec.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

                var parentRect = (RectTransform)previewSec.parent;
                var previewTopLeft = parentRect.InverseTransformPoint(
                    new Vector3(rp.xMin, rt.yMax + up, previewSec.position.z));
                var previewTopRight = parentRect.InverseTransformPoint(
                    new Vector3(rp.xMax + widen, rt.yMax + up, previewSec.position.z));

                authorRect.anchorMin = authorRect.anchorMax = new Vector2(0f, 1f);
                authorRect.pivot = new Vector2(0f, 1f);
                authorRect.localScale = Vector3.one;
                authorRect.anchoredPosition = new Vector2(
                    previewTopLeft.x - parentRect.rect.xMin,
                    previewTopLeft.y - parentRect.rect.yMax);
                authorRect.sizeDelta = new Vector2(
                    Mathf.Abs(previewTopRight.x - previewTopLeft.x),
                    Mathf.Abs(LocalDeltaY(authorRect, rowHeight)));

                _inputAuthor = authorSec.GetComponentInChildren<InputField>(true);
                if (_inputAuthor != null)
                {
                    _inputAuthor.onValueChanged.RemoveAllListeners();
                    _inputAuthor.onEndEdit.RemoveAllListeners();
                    _inputAuthor.readOnly = false;
                    _inputAuthor.characterLimit = 120;
                    _inputAuthor.lineType = InputField.LineType.SingleLine;
                }

                SetLabelText(authorSec, known,
                    _inputAuthor != null ? _inputAuthor.transform : null,
                    EditorLocalization.Get("editor.workshop.author"));

                // ---- Nombre sube ----
                ((RectTransform)titleSec).anchoredPosition +=
                    new Vector2(0f, LocalDeltaY(titleSec, up));

                // ---- Descripción: su borde superior sube, el inferior no se mueve ----
                GrowTop((RectTransform)descSec, LocalDeltaY(descSec, up));

                // ---- Imagen previa: mismo borde superior que Descripción, mismo inferior ----
                var targetTop = rd.yMax + up;
                var shift = targetTop - rp.yMax;
                ((RectTransform)previewSec).anchoredPosition +=
                    new Vector2(0f, LocalDeltaY(previewSec, shift));

                if (widen > 0f)
                    GrowRight((RectTransform)previewSec, LocalDeltaX(previewSec, widen));

                var bottomNow = rp.yMin + shift;
                var extend = bottomNow - rd.yMin;
                if (Mathf.Abs(extend) > .0001f)
                    GrowBottom((RectTransform)previewSec,
                        LocalDeltaY(previewSec, extend));

                // ---- Autor: mismo ancho EXACTO (dibujo) que el panel de Imagen previa ----
                var visPreviewNow = VisibleRect(previewSec);
                AlignVisible(authorRect,
                    new Rect(visPreviewNow.xMin, 0f, visPreviewNow.width, 0f), true);

                // ---- la imagen ocupa su panel: 16:9 y mismo alto que el campo de descripción ----
                ApplyPreviewBox();

                // ---- acciones de imagen centradas en la cabecera del panel ----
                if (labelDesc != null)
                {
                    StyleFixedLabel(labelDesc);

                    var descLabelRect = WorldRect(labelDesc);
                    var buttonHeight = descLabelRect.height * .85f;
                    var buttonGap = 10f * canvasUnit;
                    var buttonTop = descLabelRect.center.y + buttonHeight * .5f;
                    var buttonsLeft = visPreviewNow.xMin + 14f * canvasUnit;
                    var buttonsWidth = visPreviewNow.width - 28f * canvasUnit;
                    var usableWidth = buttonsWidth - buttonGap * 2f;
                    var chooseWidth = usableWidth * 1.3f / 3.1f;
                    var createWidth = usableWidth / 3.1f;
                    var deleteWidth = usableWidth * .8f / 3.1f;

                    _buttonChooseThumbnail = CloneSmallButton(
                        previewSec, "UP Elegir miniatura",
                        EditorLocalization.Get("editor.workshop.choose_thumbnail"),
                        ChooseSelectedAsThumbnail);
                    _buttonCapture = CloneSmallButton(
                        previewSec, "UP Crear imagen",
                        EditorLocalization.Get("editor.workshop.create_image"), BeginCapture);
                    _buttonDeleteImage = CloneSmallButton(
                        previewSec, "UP Eliminar",
                        EditorLocalization.Get("editor.workshop.delete_image"),
                        DeleteSelectedImage);

                    if (_buttonChooseThumbnail != null)
                        SetWorldRect((RectTransform)_buttonChooseThumbnail.transform,
                            buttonsLeft, buttonTop,
                            chooseWidth, buttonHeight);
                    if (_buttonCapture != null)
                        SetWorldRect((RectTransform)_buttonCapture.transform,
                            buttonsLeft + chooseWidth + buttonGap, buttonTop,
                            createWidth, buttonHeight);
                    if (_buttonDeleteImage != null)
                        SetWorldRect((RectTransform)_buttonDeleteImage.transform,
                            buttonsLeft + chooseWidth + buttonGap + createWidth + buttonGap,
                            buttonTop, deleteWidth, buttonHeight);
                }

                // ---- panel inferior izquierdo = Imágenes ----
                if (scrollSec != null)
                {
                    var visAct = actSec != null ? VisibleRect(actSec) : VisibleRect(descSec);
                    var targetVisible = Rect.MinMaxRect(
                        visPreviewNow.xMin,
                        visAct.yMin,
                        visPreviewNow.xMax,
                        visPreviewNow.yMin - gap);

                    if (targetVisible.height > rowHeight * .5f)
                    {
                        var sliderRect = (RectTransform)scrollSec;
                        SetWorldRect(sliderRect, targetVisible.xMin, targetVisible.yMax,
                            targetVisible.width, targetVisible.height);
                        AlignVisible(sliderRect, targetVisible, false);

                        if (_textScrollPanelContent != null)
                            _textScrollPanelContent.gameObject.SetActive(false);
                        if (_scrollerPackContents != null)
                            _scrollerPackContents.enabled = false;

                        var panelVisible = VisibleRect(sliderRect);

                        // Sin rótulo: la tira ocupa todo el panel.
                        if (_textScrollPanelLabel != null)
                            _textScrollPanelLabel.gameObject.SetActive(false);

                        BuildGallery(sliderRect, canvasUnit, panelVisible);
                    }
                    else
                    {
                        scrollSec.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: faltan paneles; no se reorganiza.");
            }

            // ---- logo más pequeño + botón más grande, a la derecha ----
            if (brandSec != null && logo != null)
            {
                foreach (var image in brandSec.GetComponentsInChildren<Image>(true))
                {
                    if (image == null || ReferenceEquals(image, logo) ||
                        image.GetComponent<Selectable>() != null ||
                        IsInsideAny(image.transform, known))
                        continue;

                    if (ReferenceEquals(image.transform, brandSec) || IsPanelImage(image))
                        image.enabled = false;
                }

                logo.transform.localScale = logo.transform.localScale * .78f;
            }

            if (_buttonSteamWorkshop != null)
                _buttonSteamWorkshop.transform.localScale =
                    _buttonSteamWorkshop.transform.localScale * 1.3f;

            if (logo != null && _buttonSteamWorkshop != null)
            {
                var logoRect = WorldRect(logo.transform);
                var buttonRect = WorldRect(_buttonSteamWorkshop.transform);

                var between = 30f * canvasUnit;
                _buttonSteamWorkshop.transform.position +=
                    new Vector3((logoRect.xMax + between) - buttonRect.xMin, 0f, 0f);
                buttonRect = WorldRect(_buttonSteamWorkshop.transform);

                var groupMaxX = Mathf.Max(logoRect.xMax, buttonRect.xMax);
                var groupMinY = Mathf.Min(logoRect.yMin, buttonRect.yMin);
                var groupMaxY = Mathf.Max(logoRect.yMax, buttonRect.yMax);

                var targetRight = banner.HasValue
                    ? banner.Value.xMin - 40f * canvasUnit
                    : (previewSec != null ? WorldRect(previewSec).xMax : groupMaxX);
                var targetY = banner.HasValue
                    ? banner.Value.center.y
                    : (brandSec != null ? WorldRect(brandSec).center.y : groupMinY);

                var delta = new Vector3(
                    targetRight - groupMaxX,
                    targetY - (groupMinY + groupMaxY) * .5f,
                    0f);

                logo.transform.position += delta;
                _buttonSteamWorkshop.transform.position += delta;
            }

            // ---- se ocultan: acuerdo, refrescar y los 2 botones de la preview ----
            UnbindAndHide(_buttonSteamWorkshopRefresh);
            UnbindAndHide(_buttonResetPreviewImage);
            UnbindAndHide(_buttonChoosePreviewImage);
            UnbindAndHide(_buttonUserAgreementLink);

            // ---- ESCALA UNIFORME a 1400 de ancho + CENTRADO en pantalla ----
            // Sin contenedores nuevos: se escala el ancestro común de los paneles.
            var scaleTargets = measured.Concat(new[]
                {
                    _buttonCloseMenu != null ? _buttonCloseMenu.transform : null
                })
                .Where(x => x != null).ToArray();

            Transform scaleTarget = FindNearestCommonAncestor(scaleTargets) ?? root;
            if (scaleTarget == null) scaleTarget = root;

            var firstRect = MeasureWorldRect(measured);
            var nativeW = firstRect.width / canvasUnit;
            var nativeH = firstRect.height / canvasUnit;

            if (nativeW > 100f && nativeH > 100f)
            {
                const float padding = 60f;
                var scaleX = Mathf.Clamp(PanelWidth / (nativeW + padding), .3f, 3f);
                var scaleY = scaleX; // SIEMPRE uniforme: nada se estira

                scaleTarget.localScale = new Vector3(scaleX, scaleY, 1f);
                Canvas.ForceUpdateCanvases();

                // Recalibrado: se vuelve a medir y se corrige una vez.
                var second = MeasureWorldRect(measured);
                var afterW = second.width / canvasUnit;
                var afterH = second.height / canvasUnit;

                if (afterW > 100f && afterH > 100f)
                {
                    scaleX = Mathf.Clamp(
                        scaleX * PanelWidth / (afterW + padding * scaleX), .3f, 3f);
                    scaleY = scaleX;

                    scaleTarget.localScale = new Vector3(scaleX, scaleY, 1f);
                    Canvas.ForceUpdateCanvases();
                }

                // Centrado exacto en el canvas (centro del contenido = centro de pantalla).
                if (rootCanvas != null)
                {
                    var canvasRect = (RectTransform)rootCanvas.transform;
                    var screenCenter = canvasRect.TransformPoint(canvasRect.rect.center);
                    var contentRect = MeasureWorldRect(measured);
                    var contentCenter = new Vector3(
                        contentRect.center.x, contentRect.center.y, screenCenter.z);
                    var delta = screenCenter - contentCenter;
                    delta.z = 0f;
                    scaleTarget.position += delta;
                }

                // Velos/oscurecidos a pantalla completa que quedaron dentro de lo
                // escalado: se devuelven a su tamaño original.
                var rootWidth = Mathf.Abs(root.rect.width);
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                {
                    if (image == null || image.GetComponent<Selectable>() != null)
                        continue;
                    if (ReferenceEquals(image.transform, root) ||
                        ReferenceEquals(image.transform, scaleTarget))
                        continue;
                    if (!IsDescendantOrSelf(image.transform, scaleTarget))
                        continue;
                    if (_imageBusyModeInputBlocker != null &&
                        ReferenceEquals(image.transform, _imageBusyModeInputBlocker.transform))
                        continue;
                    if (_imageWorkshopBusyIcon != null &&
                        ReferenceEquals(image.transform, _imageWorkshopBusyIcon.transform))
                        continue;
                    if (image.sprite != null || image.color.a >= .99f)
                        continue;
                    if (measured.Any(sec => IsDescendantOrSelf(sec, image.transform)))
                        continue;
                    if (Mathf.Abs(image.rectTransform.rect.width) < rootWidth * .8f)
                        continue;

                    image.transform.localScale = new Vector3(
                        1f / scaleX, 1f / scaleY, 1f);
                }

                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Workshop layout nativo: nativo=" + nativeW + "x" + nativeH +
                    " escala=(" + scaleX + "," + scaleY + ")" +
                    " objetivo=" + PanelWidth + "x" + PanelHeight +
                    " escalado=" + PathOf(scaleTarget));
            }
            else
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo medir el panel nativo (" + nativeW + "x" +
                    nativeH + "); sin escalar.");
            }

            RemoveWorkshopTooltips(root);
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }

        private static void RemoveWorkshopTooltips(Transform root)
        {
            if (root == null) return;

            foreach (var tooltip in root.GetComponentsInChildren<TooltipSpawner>(true))
            {
                if (tooltip == null) continue;
                // Los botones clonados heredan el tooltip del boton de publicar.
                // Se invalida de inmediato y se desregistra al terminar el frame.
                tooltip.SetShouldShowFunc(() => false);
                UnityEngine.Object.Destroy(tooltip);
            }
        }

        private static Rect MeasureWorldRect(Transform[] sections)
        {
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var corners = new Vector3[4];

            foreach (var section in sections)
            {
                var rect = section as RectTransform;
                if (rect == null) continue;

                rect.GetWorldCorners(corners);
                for (var index = 0; index < 4; ++index)
                {
                    minX = Mathf.Min(minX, corners[index].x);
                    maxX = Mathf.Max(maxX, corners[index].x);
                    minY = Mathf.Min(minY, corners[index].y);
                    maxY = Mathf.Max(maxY, corners[index].y);
                }
            }

            return minX > maxX
                ? new Rect(0f, 0f, 0f, 0f)
                : new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static float MeasureCanvasWidth(Transform[] sections, float canvasUnit)
        {
            if (sections == null || sections.Length == 0) return 0f;

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var corners = new Vector3[4];

            foreach (var section in sections)
            {
                var rect = section as RectTransform;
                if (rect == null) continue;

                rect.GetWorldCorners(corners);
                for (var index = 0; index < 4; ++index)
                {
                    minX = Mathf.Min(minX, corners[index].x);
                    maxX = Mathf.Max(maxX, corners[index].x);
                }
            }

            return minX > maxX ? 0f : (maxX - minX) / canvasUnit;
        }

        private static Transform SetHeaderText(
            Transform root, List<Transform> known, Transform brandSec, string value)
        {
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp == null || IsInsideAny(tmp.transform, known) ||
                    (brandSec != null && IsDescendantOrSelf(tmp.transform, brandSec)) ||
                    tmp.GetComponentInParent<Selectable>() != null)
                    continue;

                if ((tmp.text ?? "").IndexOf("workshop", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DisableLocalisers(tmp.transform);
                tmp.text = value;
                tmp.enableWordWrapping = false;
                return tmp.transform;
            }

            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (text == null || IsInsideAny(text.transform, known) ||
                    (brandSec != null && IsDescendantOrSelf(text.transform, brandSec)) ||
                    text.GetComponentInParent<Selectable>() != null)
                    continue;

                if ((text.text ?? "").IndexOf("workshop", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DisableLocalisers(text.transform);
                text.text = value;
                return text.transform;
            }

            return null;
        }

        // ---------------- geometría en coordenadas de mundo ----------------
        private static Rect WorldRect(Transform transform)
        {
            var rect = transform as RectTransform;
            if (rect == null) return new Rect(0f, 0f, 0f, 0f);

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Rect(
                corners[0].x, corners[0].y,
                corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        // Coloca un RectTransform con esquina superior izquierda y tamaño en mundo.
        private static void SetWorldRect(
            RectTransform rect, float xMin, float yTop, float width, float height)
        {
            var parent = rect.parent as RectTransform;
            if (parent == null) return;

            var z = rect.position.z;
            var topLeft = parent.InverseTransformPoint(new Vector3(xMin, yTop, z));
            var bottomRight = parent.InverseTransformPoint(
                new Vector3(xMin + width, yTop - height, z));

            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(
                topLeft.x - parent.rect.xMin,
                topLeft.y - parent.rect.yMax);
            rect.sizeDelta = new Vector2(
                Mathf.Abs(bottomRight.x - topLeft.x),
                Mathf.Abs(topLeft.y - bottomRight.y));
        }

        private static RectTransform UnitOf(
            Component control, Transform panel, List<Transform> known)
        {
            if (control == null || panel == null) return null;

            var controlRect = control.transform as RectTransform;
            var unit = TopBelow(controlRect, panel) as RectTransform;

            if (unit == null || unit == controlRect ||
                known.Any(k => k != null && k != controlRect &&
                               IsDescendantOrSelf(k, unit)))
                return controlRect;

            return unit;
        }

        private static Rect? FindHeaderBannerRect(Transform headerText)
        {
            if (headerText == null) return null;

            var textRect = WorldRect(headerText);

            foreach (var image in headerText.GetComponentsInParent<Image>(true))
            {
                if (image == null || image.GetComponent<Selectable>() != null)
                    continue;

                var rect = WorldRect(image.transform);
                if (rect.height > 0f && rect.height < textRect.height * 3f &&
                    rect.width < textRect.width * 3f)
                    return rect;
            }

            // Sin imagen de banner propia: se usa el texto con un pequeño margen.
            return new Rect(
                textRect.xMin,
                textRect.yMin - textRect.height * .2f,
                textRect.width,
                textRect.height * 1.4f);
        }

        private static Image FindBrandLogo(Transform brandSec, List<Transform> known)
        {
            var corners = new Vector3[4];
            ((RectTransform)brandSec).GetWorldCorners(corners);
            var brandArea = Mathf.Abs(
                (corners[2].x - corners[0].x) * (corners[1].y - corners[0].y));

            Image logo = null;
            var bestArea = 0f;

            foreach (var image in brandSec.GetComponentsInChildren<Image>(true))
            {
                if (image == null || ReferenceEquals(image.transform, brandSec) ||
                    image.GetComponent<Selectable>() != null ||
                    IsInsideAny(image.transform, known) ||
                    image.sprite == null || IsPanelImage(image))
                    continue;

                image.rectTransform.GetWorldCorners(corners);
                var area = Mathf.Abs(
                    (corners[2].x - corners[0].x) * (corners[1].y - corners[0].y));

                if (area >= brandArea * .8f)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    logo = image;
                }
            }

            return logo;
        }

        // Imagen previa 16:9, centrada bajo el rótulo del panel.
        private void PlacePreview16x9(RectTransform panel, List<Transform> known)
        {
            if (_imagePreviewImage == null) return;

            var imageRect = _imagePreviewImage.rectTransform;
            var unit = UnitOf(_imagePreviewImage, panel, known);
            if (unit == null) return;

            var nested = unit != imageRect;
            var insets = nested ? ComputeInsets(unit, imageRect) : Vector4.zero;

            var topOffset = 64f;
            var sideMargin = 28f;
            var bottomMargin = 22f;

            var availableWidth = Mathf.Max(60f, panel.rect.width - sideMargin * 2f);
            var availableHeight = Mathf.Max(40f, panel.rect.height - topOffset - bottomMargin);

            var width = Mathf.Min(availableWidth, availableHeight * 16f / 9f);
            var height = width * 9f / 16f;

            unit.SetParent(panel, false);
            DisableAutoLayout(unit);
            PlaceRect(unit, (panel.rect.width - width) * .5f, topOffset, width, height);

            if (nested)
            {
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.offsetMin = new Vector2(insets.x, insets.y);
                imageRect.offsetMax = new Vector2(-insets.z, -insets.w);
            }

            _imagePreviewImage.preserveAspect = true;
        }

        // ---------------- tira de miniaturas (estilo Steam) ----------------
        private RectTransform _galleryContent;
        private float _galleryThumbHeight = 80f;
        private int _galleryIndex;
        private readonly List<Texture2D> _galleryTextures = new List<Texture2D>();
        private readonly List<Sprite> _gallerySprites = new List<Sprite>();
        private readonly List<Outline> _galleryOutlines = new List<Outline>();

        private void BuildGallery(RectTransform panel, float u, Rect visible)
        {
            var margin = 22f;
            var topMargin = 14f;
            var bottomMargin = 14f;
            var barHeight = 14f;
            var barGap = 10f;

            var viewTop = visible.yMax - topMargin * u;
            var viewBottom = visible.yMin + (bottomMargin + barHeight + barGap) * u;
            var viewHeight = Mathf.Max(30f * u, viewTop - viewBottom);
            var left = visible.xMin + margin * u;
            var fullWidth = Mathf.Max(80f * u, visible.width - margin * 2f * u);

            // ---- zona de miniaturas: desplazamiento HORIZONTAL con máscara de recorte ----
            var viewLeft = left;
            var viewWidth = fullWidth;

            var view = new GameObject(
                "UP Miniaturas",
                typeof(RectTransform), typeof(Image), typeof(Mask),
                typeof(ScrollRect), typeof(LayoutElement));
            view.transform.SetParent(panel, false);
            view.GetComponent<LayoutElement>().ignoreLayout = true;

            // Mask (stencil) en lugar de RectMask2D: este último no recortaba aquí.
            view.GetComponent<Image>().color = Color.white;
            view.GetComponent<Mask>().showMaskGraphic = false;

            var viewRect = (RectTransform)view.transform;
            SetWorldRect(viewRect, viewLeft, viewTop, viewWidth, viewHeight);

            // Alto de las miniaturas = alto REAL (local) de la zona; nunca más.
            _galleryThumbHeight = viewRect.rect.height;

            var content = new GameObject(
                "Content",
                typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(view.transform, false);

            _galleryContent = (RectTransform)content.transform;
            _galleryContent.anchorMin = new Vector2(0f, 0f);
            _galleryContent.anchorMax = new Vector2(0f, 1f);
            _galleryContent.pivot = new Vector2(0f, .5f);
            _galleryContent.offsetMin = Vector2.zero;
            _galleryContent.offsetMax = Vector2.zero;

            var layout = content.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // ---- barra de desplazamiento HORIZONTAL, debajo de las miniaturas ----
            var scrollbar = _galleryBarClone;
            RectTransform barRect;

            if (scrollbar != null)
            {
                // La barra nativa (vertical) reutilizada en horizontal.
                scrollbar.gameObject.SetActive(true);
                scrollbar.direction = Scrollbar.Direction.LeftToRight;
                barRect = (RectTransform)scrollbar.transform;
            }
            else
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: el scroller nativo no tiene barra; se crea una propia.");

                var barObject = new GameObject(
                    "UP Barra",
                    typeof(RectTransform), typeof(Image), typeof(Scrollbar),
                    typeof(LayoutElement));
                barObject.transform.SetParent(panel, false);
                barObject.GetComponent<LayoutElement>().ignoreLayout = true;
                barObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, .35f);
                barRect = (RectTransform)barObject.transform;

                var slidingObject = new GameObject("Sliding Area", typeof(RectTransform));
                slidingObject.transform.SetParent(barObject.transform, false);
                var slidingRect = (RectTransform)slidingObject.transform;
                slidingRect.anchorMin = Vector2.zero;
                slidingRect.anchorMax = Vector2.one;
                slidingRect.offsetMin = new Vector2(2f, 2f);
                slidingRect.offsetMax = new Vector2(-2f, -2f);

                var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
                handleObject.transform.SetParent(slidingObject.transform, false);
                var handleRect = (RectTransform)handleObject.transform;
                handleRect.anchorMin = Vector2.zero;
                handleRect.anchorMax = Vector2.one;
                handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
                var handleImage = handleObject.GetComponent<Image>();
                handleImage.color = new Color(1f, 1f, 1f, .9f);

                scrollbar = barObject.GetComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.LeftToRight;
                scrollbar.handleRect = handleRect;
                scrollbar.targetGraphic = handleImage;
            }

            SetWorldRect(barRect, viewLeft,
                visible.yMin + (bottomMargin + barHeight) * u, viewWidth, barHeight * u);
            barRect.SetAsLastSibling();

            // ---- ScrollRect: solo horizontal ----
            var scroll = view.GetComponent<ScrollRect>();
            scroll.content = _galleryContent;
            scroll.viewport = viewRect;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.horizontalScrollbar = scrollbar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        // La imagen elegida genera la miniatura principal; la lista se publica como galería.
        private List<string> GalleryPaths()
        {
            return ScenarioWorkshopMetaStore.GalleryPaths(_level);
        }

        private string CurrentGalleryPath()
        {
            var paths = GalleryPaths();
            if (paths.Count == 0) return null;

            if (_galleryIndex < 0 || _galleryIndex >= paths.Count)
                _galleryIndex = 0;

            return paths[_galleryIndex];
        }

        private void ClearGalleryResources()
        {
            foreach (var sprite in _gallerySprites)
                if (sprite != null) Destroy(sprite);
            foreach (var texture in _galleryTextures)
                if (texture != null) Destroy(texture);

            _gallerySprites.Clear();
            _galleryTextures.Clear();
            _galleryOutlines.Clear();
        }

        private void RefreshGallery()
        {
            if (_galleryContent == null)
                return;

            for (var index = _galleryContent.childCount - 1; index >= 0; --index)
            {
                var child = _galleryContent.GetChild(index);
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            ClearGalleryResources();

            var paths = GalleryPaths();
            if (_galleryIndex >= paths.Count) _galleryIndex = 0;

            var thumbnailPath = ScenarioWorkshopMetaStore.PreviewPath(_level);
            _thumbnailIndex = paths.FindIndex(path =>
                string.Equals(path, thumbnailPath, StringComparison.OrdinalIgnoreCase));
            _galleryCount = paths.Count;

            for (var index = 0; index < paths.Count; ++index)
                CreateGalleryThumb(index, paths[index], index == _thumbnailIndex);

            RefreshActionButtons();
        }

        private void CreateGalleryThumb(int index, string path, bool isThumbnail)
        {
            var thumb = new GameObject(
                "UP Miniatura " + index,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            thumb.transform.SetParent(_galleryContent, false);

            var background = thumb.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, .35f);

            // La miniatura elegida es cuadrada (con icono); las demás, 16:9.
            var element = thumb.GetComponent<LayoutElement>();
            element.preferredHeight = _galleryThumbHeight;
            element.preferredWidth = isThumbnail
                ? _galleryThumbHeight
                : _galleryThumbHeight * 16f / 9f;

            var imageObject = new GameObject("Img", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(thumb.transform, false);

            var imageRect = (RectTransform)imageObject.transform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(3f, 3f);
            imageRect.offsetMax = new Vector2(-3f, -3f);

            var image = imageObject.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;

            try
            {
                var full = ExtContentTextureUtils.LoadTexture2D(path);
                if (full != null)
                {
                    var texture = isThumbnail
                        ? WorkshopThumbnail.Build(full)
                        : WorkshopImageScaler.Scale(full, 256, 144, true, true, true);
                    Destroy(full);

                    if (texture != null)
                    {
                        _galleryTextures.Add(texture);
                        var sprite = Sprite.Create(
                            texture,
                            new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(.5f, .5f));
                        _gallerySprites.Add(sprite);
                        image.sprite = sprite;
                    }
                }
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo cargar la miniatura: " + exception.Message);
            }

            var outline = thumb.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.enabled = index == _galleryIndex;
            _galleryOutlines.Add(outline);

            var button = thumb.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;
            var captured = index;
            button.onClick.AddListener(() => SelectGallery(captured));
        }

        // Elimina la imagen seleccionada.
        private void DeleteSelectedImage()
        {
            if (_publishing || _level == null) return;

            var paths = GalleryPaths();
            if (paths.Count == 0) return;

            var index = Mathf.Clamp(_galleryIndex, 0, paths.Count - 1);

            try
            {
                ScenarioWorkshopMetaStore.RemoveImage(_level, Path.GetFileName(paths[index]));
            }
            catch (Exception exception)
            {
                ShowError(EditorLocalization.Format(
                    "editor.workshop.error.delete_image", exception.Message));
                return;
            }

            _galleryIndex = Mathf.Clamp(index, 0, Mathf.Max(0, GalleryPaths().Count - 1));
            Refresh();
        }

        // La imagen seleccionada pasa a ser la miniatura (la original no se modifica).
        private void ChooseSelectedAsThumbnail()
        {
            if (_publishing || _level == null) return;

            var paths = GalleryPaths();
            if (paths.Count == 0) return;

            var index = Mathf.Clamp(_galleryIndex, 0, paths.Count - 1);

            try
            {
                ScenarioWorkshopMetaStore.SetThumbnail(_level, Path.GetFileName(paths[index]));
            }
            catch (Exception exception)
            {
                ShowError(EditorLocalization.Format(
                    "editor.workshop.error.choose_thumbnail", exception.Message));
                return;
            }

            _galleryIndex = 0;
            Refresh();
        }

        private int _galleryCount;
        private int _thumbnailIndex = -1;

        private void SelectGallery(int index)
        {
            _galleryIndex = index;

            for (var i = 0; i < _galleryOutlines.Count; ++i)
                if (_galleryOutlines[i] != null)
                    _galleryOutlines[i].enabled = i == index;

            RefreshPreview();
            RefreshActionButtons();
        }

        private RectTransform _previewPanelRoot;
        private Sprite _previewSprite;

        private static void GrowRight(RectTransform rect, float d)
        {
            if (rect == null) return;
            if (Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x))
            {
                rect.sizeDelta += new Vector2(d, 0f);
                rect.anchoredPosition += new Vector2(d * rect.pivot.x, 0f);
            }
            else
            {
                rect.offsetMax += new Vector2(d, 0f);
            }
        }

        private static float LocalDeltaX(Transform transform, float worldDx)
        {
            var parent = transform.parent;
            return parent == null
                ? worldDx
                : parent.InverseTransformVector(new Vector3(worldDx, 0f, 0f)).x;
        }

        private DynamicButton _buttonDeleteImage;
        private DynamicButton _buttonChooseThumbnail;

        // Rótulo con tamaño fijo y alineación uniforme.
        private static void StyleFixedLabel(Transform target)
        {
            if (target == null) return;

            var tmp = target.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.enableAutoSizing = false;
                tmp.fontSize = 34f;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                return;
            }

            var text = target.GetComponent<Text>();
            if (text != null)
            {
                text.resizeTextForBestFit = false;
                text.fontSize = 34;
                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        // Botón pequeño con el aspecto nativo (clon de "Crear como artículo nuevo").
        private DynamicButton CloneSmallButton(
            Transform parent, string name, string label, Action action)
        {
            if (_buttonCreateAsNew == null || parent == null) return null;

            var clone = UnityEngine.Object.Instantiate(
                _buttonCreateAsNew.gameObject, parent, false);
            clone.name = name;
            clone.SetActive(true);

            var button = clone.GetComponent<DynamicButton>();
            if (button == null)
            {
                Destroy(clone);
                return null;
            }

            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => { if (action != null) action(); });

            if (clone.GetComponent<LayoutElement>() == null)
                clone.AddComponent<LayoutElement>().ignoreLayout = true;

            SetNativeButtonText(button, label);
            ExtContentUIUtils.SetSelectableInteractable(button, true);

            var tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 20f;
                tmp.fontSizeMax = 30f;
                tmp.enableWordWrapping = false;
            }

            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 20;
                text.resizeTextMaxSize = 30;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            return button;
        }

        private float _canvasUnit = 1f;
        private Scrollbar _galleryBarClone;

        // Rectángulo del DIBUJO del panel: el Image con borde más grande de su sección.
        private static Rect VisibleRect(Transform section)
        {
            if (section == null) return new Rect(0f, 0f, 0f, 0f);

            Image best = null;
            var bestArea = 0f;

            foreach (var image in section.GetComponentsInChildren<Image>(true))
            {
                if (image == null || !image.gameObject.activeInHierarchy ||
                    !image.enabled || image.sprite == null ||
                    image.GetComponentInParent<Selectable>() != null)
                    continue;

                if (image.type != Image.Type.Sliced &&
                    image.sprite.border.sqrMagnitude <= .01f)
                    continue;

                var rect = WorldRect(image.transform);
                var area = rect.width * rect.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = image;
                }
            }

            return best != null ? WorldRect(best.transform) : WorldRect(section);
        }

        // Ajusta una sección para que su DIBUJO coincida con el rectángulo objetivo.
        private static void AlignVisible(
            RectTransform section, Rect target, bool horizontalOnly)
        {
            if (section == null) return;

            for (var pass = 0; pass < 2; ++pass)
            {
                var visible = VisibleRect(section);
                if (visible.width <= 0f || visible.height <= 0f) return;

                var dxMin = target.xMin - visible.xMin;
                var dxMax = target.xMax - visible.xMax;
                var dyMax = horizontalOnly ? 0f : target.yMax - visible.yMax;
                var dyMin = horizontalOnly ? 0f : target.yMin - visible.yMin;

                section.anchoredPosition += new Vector2(
                    LocalDeltaX(section, dxMin), LocalDeltaY(section, dyMax));

                var dw = dxMax - dxMin;
                if (Mathf.Abs(dw) > .0001f)
                    GrowRight(section, LocalDeltaX(section, dw));

                var dh = dyMax - dyMin;
                if (!horizontalOnly && Mathf.Abs(dh) > .0001f)
                    GrowBottom(section, LocalDeltaY(section, dh));
            }
        }

        // La barra de desplazamiento nativa (vertical) se clona para reutilizarla.
        private Scrollbar CloneNativeScrollbar(Transform parent)
        {
            if (_scrollerPackContents == null || parent == null) return null;

            Scrollbar native = _scrollerPackContents.verticalScrollbar;
            if (native == null) native = _scrollerPackContents.horizontalScrollbar;
            if (native == null)
                native = _scrollerPackContents.GetComponentInChildren<Scrollbar>(true);
            if (native == null) return null;

            var clone = UnityEngine.Object.Instantiate(native.gameObject, parent, false);
            clone.name = "UP Barra nativa";
            clone.SetActive(true);

            var scrollbar = clone.GetComponent<Scrollbar>();
            if (scrollbar == null)
            {
                Destroy(clone);
                return null;
            }

            scrollbar.onValueChanged.RemoveAllListeners();
            if (clone.GetComponent<LayoutElement>() == null)
                clone.AddComponent<LayoutElement>().ignoreLayout = true;
            else
                clone.GetComponent<LayoutElement>().ignoreLayout = true;

            return scrollbar;
        }

        // Oculta TODO gráfico nativo del panel viejo (cuadro oscuro, barra...) salvo el
        // dibujo del propio panel, los textos y la barra clonada.
        private void HidePackLeftovers(Transform root, Transform panel, Scrollbar keepBar)
        {
            var region = WorldRect(panel);
            if (_scrollerPackContents != null)
            {
                var scrollerRect = WorldRect(_scrollerPackContents.transform);
                if (scrollerRect.width > 0f && scrollerRect.height > 0f)
                    region = Rect.MinMaxRect(
                        Mathf.Min(region.xMin, scrollerRect.xMin),
                        Mathf.Min(region.yMin, scrollerRect.yMin),
                        Mathf.Max(region.xMax, scrollerRect.xMax),
                        Mathf.Max(region.yMax, scrollerRect.yMax));
            }

            if (region.width <= 0f || region.height <= 0f) return;

            var keep = keepBar != null ? keepBar.transform : null;
            var inside = new List<Graphic>();

            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy ||
                    !graphic.enabled)
                    continue;
                if (graphic is Text || graphic is TMP_Text) continue;
                if (keep != null && IsDescendantOrSelf(graphic.transform, keep)) continue;
                if (!region.Contains(WorldRect(graphic.transform).center)) continue;

                inside.Add(graphic);
            }

            // El dibujo del panel = el Image con borde más grande de la zona.
            Graphic art = null;
            var artArea = 0f;
            foreach (var graphic in inside)
            {
                var image = graphic as Image;
                if (image == null || image.sprite == null) continue;
                if (image.type != Image.Type.Sliced &&
                    image.sprite.border.sqrMagnitude <= .01f)
                    continue;

                var rect = WorldRect(graphic.transform);
                var area = rect.width * rect.height;
                if (area > artArea)
                {
                    artArea = area;
                    art = graphic;
                }
            }

            foreach (var graphic in inside)
                if (!ReferenceEquals(graphic, art))
                    graphic.enabled = false;

            foreach (var bar in root.GetComponentsInChildren<Scrollbar>(true))
            {
                if (bar == null || !bar.gameObject.activeInHierarchy) continue;
                if (keep != null && IsDescendantOrSelf(bar.transform, keep)) continue;
                if (region.Contains(WorldRect(bar.transform).center))
                    bar.gameObject.SetActive(false);
            }
        }

        // La imagen previa llena su panel: 16:9, con el MISMO alto que el campo de descripción.
        private void ApplyPreviewBox()
        {
            if (_imagePreviewImage == null || _previewPanelRoot == null ||
                _inputDescription == null)
                return;

            Canvas.ForceUpdateCanvases();

            var panel = VisibleRect(_previewPanelRoot);
            var description = WorldRect(_inputDescription.transform);
            if (panel.width <= 0f || description.height <= 0f) return;

            var margin = 16f * _canvasUnit;
            var height = description.height;
            var width = height * 16f / 9f;

            var maxWidth = panel.width - margin * 2f;
            if (width > maxWidth)
            {
                width = maxWidth;
                height = width * 9f / 16f;
            }

            var left = panel.center.x - width * .5f;
            var top = description.yMax;

            var imageRect = _imagePreviewImage.rectTransform;
            var unit = imageRect;
            var nested = false;
            var insets = Vector4.zero;

            var parent = imageRect.parent as RectTransform;
            if (parent != null && !ReferenceEquals(parent, _previewPanelRoot))
            {
                var parentWorld = WorldRect(parent);
                var imageWorld = WorldRect(imageRect);
                if (imageWorld.width > 0f &&
                    parentWorld.width <= imageWorld.width * 1.15f &&
                    parentWorld.height <= imageWorld.height * 1.15f)
                {
                    unit = parent;
                    nested = true;
                    insets = ComputeInsets(parent, imageRect);
                }
            }

            DisableAutoLayout(unit.parent);
            if (unit.GetComponent<LayoutElement>() == null)
                unit.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            SetWorldRect(unit, left, top, width, height);

            if (nested)
            {
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.offsetMin = new Vector2(insets.x, insets.y);
                imageRect.offsetMax = new Vector2(-insets.z, -insets.w);
            }
        }

        // Mueve la parte superior hacia arriba (d>0) manteniendo el borde inferior.
        private static void GrowTop(RectTransform rect, float d)
        {
            if (rect == null) return;
            if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                rect.sizeDelta += new Vector2(0f, d);
                rect.anchoredPosition += new Vector2(0f, d * rect.pivot.y);
            }
            else
            {
                rect.offsetMax += new Vector2(0f, d);
            }
        }

        // Mueve el borde inferior hacia abajo (d>0) manteniendo el superior.
        private static void GrowBottom(RectTransform rect, float d)
        {
            if (rect == null) return;
            if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                rect.sizeDelta += new Vector2(0f, d);
                rect.anchoredPosition -= new Vector2(0f, d * (1f - rect.pivot.y));
            }
            else
            {
                rect.offsetMin -= new Vector2(0f, d);
            }
        }

        private static float LocalDeltaY(Transform transform, float worldDy)
        {
            var parent = transform.parent;
            return parent == null
                ? worldDy
                : parent.InverseTransformVector(new Vector3(0f, worldDy, 0f)).y;
        }

        // Alto = ancho * 9/16, manteniendo fijo el borde superior.
        private static void Make16x9(RectTransform rect)
        {
            if (rect == null) return;

            var width = rect.rect.width;
            var height = rect.rect.height;
            var target = width * 9f / 16f;
            var diff = height - target;
            if (width < 10f || Mathf.Abs(diff) < .5f) return;

            if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y - diff);
                rect.anchoredPosition += new Vector2(0f, diff * (1f - rect.pivot.y));
            }
            else
            {
                rect.offsetMin += new Vector2(0f, diff);
            }
        }

        // Oculta restos oscuros nativos (cuadro/barra) dentro de un panel.
        private static void HideDarkVisuals(Transform panel)
        {
            foreach (var image in panel.GetComponentsInChildren<Image>(true))
            {
                if (image == null || ReferenceEquals(image.transform, panel) ||
                    image.GetComponent<Selectable>() != null)
                    continue;

                var color = image.color;
                var luminance = (color.r + color.g + color.b) / 3f;
                if (color.a > .15f && luminance < .45f)
                    image.enabled = false;
            }
        }

        // Reduce el logo de Steam Workshop hasta la altura del botón (proporcional).
        private void ShrinkBrandLogo(Transform brandSec, List<Transform> known)
        {
            if (_buttonSteamWorkshop == null)
                return;

            var corners = new Vector3[4];
            ((RectTransform)_buttonSteamWorkshop.transform).GetWorldCorners(corners);
            var buttonHeight = Mathf.Abs(corners[1].y - corners[0].y);

            ((RectTransform)brandSec).GetWorldCorners(corners);
            var brandArea = Mathf.Abs(
                (corners[2].x - corners[0].x) * (corners[1].y - corners[0].y));

            Image logo = null;
            var bestArea = 0f;

            foreach (var image in brandSec.GetComponentsInChildren<Image>(true))
            {
                if (image == null || ReferenceEquals(image.transform, brandSec) ||
                    image.GetComponent<Selectable>() != null ||
                    IsInsideAny(image.transform, known) ||
                    image.sprite == null || IsPanelImage(image))
                    continue;

                image.rectTransform.GetWorldCorners(corners);
                var area = Mathf.Abs(
                    (corners[2].x - corners[0].x) * (corners[1].y - corners[0].y));

                if (area >= brandArea * .8f)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    logo = image;
                }
            }

            if (logo == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se encontró el logo en el panel de marca.");
                return;
            }

            logo.rectTransform.GetWorldCorners(corners);
            var logoHeight = Mathf.Abs(corners[1].y - corners[0].y);
            if (buttonHeight <= 0f || logoHeight <= buttonHeight * 1.02f)
                return;

            var factor = buttonHeight / logoHeight;
            logo.transform.localScale = logo.transform.localScale * factor;

            UnderPressureEditorPlugin.Log?.LogInfo(
                "Workshop: logo " + PathOf(logo.transform) + " reducido x" + factor);
        }

        // Cambia el texto de un rótulo y lo deja con estilo uniforme (autoajustable).
        private static void SetTextOn(Transform target, string value)
        {
            if (target == null) return;

            DisableLocalisers(target);

            var tmp = target.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = value;
                tmp.enableWordWrapping = false;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 16f;
                tmp.fontSizeMax = 34f;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                return;
            }

            var text = target.GetComponent<Text>();
            if (text != null)
            {
                text.text = value;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 16;
                text.resizeTextMaxSize = 34;
                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        private static Transform SetLabelText(
            Transform panel, List<Transform> known, Transform input, string value)
        {
            DisableLocalisers(panel);
            var excluded = new List<Transform>(known);
            if (input != null) excluded.Add(input);

            foreach (var tmp in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp == null || IsInsideAny(tmp.transform, excluded) ||
                    tmp.GetComponentInParent<Selectable>() != null)
                    continue;
                tmp.text = value;
                return tmp.transform;
            }

            foreach (var text in panel.GetComponentsInChildren<Text>(true))
            {
                if (text == null || IsInsideAny(text.transform, excluded) ||
                    text.GetComponentInParent<Selectable>() != null)
                    continue;
                text.text = value;
                return text.transform;
            }

            return null;
        }

        private void CreateCaptureButton(Transform frame)
        {
            if (_buttonSteamWorkshop == null)
                return;

            var clone = UnityEngine.Object.Instantiate(
                _buttonSteamWorkshop.gameObject, frame, false);
            clone.name = "UP Capturar imagen";
            clone.SetActive(true);

            _buttonCapture = clone.GetComponent<DynamicButton>();
            if (_buttonCapture == null)
            {
                UnityEngine.Object.Destroy(clone);
                return;
            }

            Bind(_buttonCapture, BeginCapture);
            SetNativeButtonText(_buttonCapture,
                EditorLocalization.Get("editor.workshop.create_image"));
            PlaceRect((RectTransform)clone.transform, LeftX, CaptureY, LeftW, CaptureH);
        }

        // ---------------- helpers de layout ----------------
        private static void AddKnown(List<Transform> list, Component component)
        {
            if (component != null && component.transform != null)
                list.Add(component.transform);
        }

        private static Transform FindSection(
            Transform root, List<Transform> known, params Component[] anchors)
        {
            var group = anchors
                .Where(a => a != null).Select(a => a.transform).Distinct().ToArray();
            if (group.Length == 0) return null;

            var forbidden = known.Where(t => !group.Contains(t)).ToArray();

            var current = FindNearestCommonAncestor(group) ?? group[0];

            // Si el grupo es un solo control, el panel es como mínimo su padre.
            if (group.Length == 1 && ReferenceEquals(current, group[0]) &&
                current.parent != null && !ReferenceEquals(current.parent, root))
                current = current.parent;

            // Subir mientras el padre NO contenga controles de otros grupos.
            while (current.parent != null &&
                   !ReferenceEquals(current.parent, root) &&
                   !forbidden.Any(f => IsDescendantOrSelf(f, current.parent)))
                current = current.parent;

            return current;
        }

        private static void DisableAutoLayout(Transform target)
        {
            foreach (var group in target.GetComponents<LayoutGroup>())
                group.enabled = false;
            foreach (var fitter in target.GetComponents<ContentSizeFitter>())
                fitter.enabled = false;
        }

        private static void PlaceRect(RectTransform rect, float x, float y, float w, float h)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one;
        }

        private static void PlacePanel(Transform panel, float x, float y, float w, float h)
        {
            var rect = panel as RectTransform;
            if (rect == null) return;
            DisableAutoLayout(rect);
            PlaceRect(rect, x, y, w, h);
        }

        // Mantiene el interior original del panel y lo escala uniformemente.
        private static void PlaceScaled(RectTransform rect, float x, float y, float w, float h)
        {
            if (rect == null) return;

            var originalW = Mathf.Max(1f, Mathf.Abs(rect.rect.width));
            var originalH = Mathf.Max(1f, Mathf.Abs(rect.rect.height));
            var scale = Mathf.Min(w / originalW, h / originalH);

            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(originalW, originalH);
            rect.localScale = new Vector3(scale, scale, 1f);
            rect.anchoredPosition = new Vector2(
                x + (w - originalW * scale) * .5f,
                -(y + (h - originalH * scale) * .5f));
        }

        private static Transform TopBelow(Transform child, Transform ancestor)
        {
            var current = child;
            while (current != null && current.parent != ancestor)
                current = current.parent;
            return current;
        }

        // Coloca un control dentro de un panel. Si el control está envuelto en
        // un contenedor propio (marco, fondo), se mueve el contenedor completo
        // y el control rellena el marco con sus márgenes originales.
        private static void PlaceControl(
            Component control, Transform panel, List<Transform> known,
            float x, float y, float w, float h)
        {
            if (control == null || panel == null) return;

            var controlRect = control.transform as RectTransform;
            if (controlRect == null) return;

            var unit = TopBelow(controlRect, panel) as RectTransform;
            if (unit == null || unit == controlRect ||
                known.Any(k => k != null && k != controlRect &&
                               IsDescendantOrSelf(k, unit)))
                unit = controlRect;

            var nested = unit != controlRect;
            var insets = nested ? ComputeInsets(unit, controlRect) : Vector4.zero;

            unit.SetParent(panel, false);
            DisableAutoLayout(unit);
            PlaceRect(unit, x, y, w, h);
            unit.gameObject.SetActive(true);

            if (nested)
            {
                controlRect.anchorMin = Vector2.zero;
                controlRect.anchorMax = Vector2.one;
                controlRect.offsetMin = new Vector2(insets.x, insets.y);
                controlRect.offsetMax = new Vector2(-insets.z, -insets.w);
            }
        }

        // (izquierda, abajo, derecha, arriba)
        private static Vector4 ComputeInsets(RectTransform unit, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            var bottomLeft = unit.InverseTransformPoint(corners[0]);
            var topRight = unit.InverseTransformPoint(corners[2]);
            var rect = unit.rect;
            return new Vector4(
                bottomLeft.x - rect.xMin,
                bottomLeft.y - rect.yMin,
                rect.xMax - topRight.x,
                rect.yMax - topRight.y);
        }

        private void LayoutRow(
            Transform panel, InputField input, List<Transform> known,
            string label, float panelWidth)
        {
            SetPanelLabel(panel, known, label, 26f, 10f, RowLabelWidth - 30f, RowH - 20f,
                input != null ? input.transform : null);

            if (input == null) return;
            PlaceControl(input, panel, known,
                RowLabelWidth, 13f, panelWidth - RowLabelWidth - 24f, RowH - 26f);
        }

        private static void SetPanelLabel(
            Transform panel, List<Transform> known, string value,
            float x, float y, float w, float h, Transform extraExcluded = null)
        {
            var excluded = new List<Transform>(known);
            if (extraExcluded != null) excluded.Add(extraExcluded);

            DisableLocalisers(panel);

            foreach (var tmp in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp == null || IsInsideAny(tmp.transform, excluded) ||
                    tmp.GetComponentInParent<Selectable>() != null)
                    continue;

                tmp.transform.SetParent(panel, false);
                PlaceRect(tmp.rectTransform, x, y, w, h);
                tmp.text = value;
                tmp.enableWordWrapping = false;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 16f;
                tmp.fontSizeMax = 34f;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                return;
            }

            foreach (var text in panel.GetComponentsInChildren<Text>(true))
            {
                if (text == null || IsInsideAny(text.transform, excluded) ||
                    text.GetComponentInParent<Selectable>() != null)
                    continue;

                text.transform.SetParent(panel, false);
                PlaceRect(text.rectTransform, x, y, w, h);
                text.text = value;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 16;
                text.resizeTextMaxSize = 34;
                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                return;
            }
        }

        private static bool IsInsideAny(Transform value, List<Transform> roots)
        {
            return roots.Any(r => r != null && IsDescendantOrSelf(value, r));
        }

        // ---------------- detección de paneles del prefab ----------------
        private static Transform FindFrame(Transform root, List<Transform> excluded)
        {
            var rootRect = root as RectTransform;
            var rootArea = rootRect != null
                ? Mathf.Abs(rootRect.rect.width * rootRect.rect.height)
                : float.MaxValue;

            Image best = null;
            var bestArea = 0f;

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.GetComponent<Selectable>() != null)
                    continue;
                if (IsInsideAny(image.transform, excluded))
                    continue;

                var rect = image.rectTransform.rect;
                var area = Mathf.Abs(rect.width * rect.height);

                // Descarta el velo/bloqueador a pantalla completa.
                if (ReferenceEquals(image.transform, root) && area >= rootArea * .97f &&
                    image.sprite == null)
                    continue;
                if (area >= rootArea * .97f && image.sprite == null)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    best = image;
                }
            }

            return best != null ? best.transform : null;
        }

        private static string PathOf(Transform value)
        {
            if (value == null) return "<null>";
            var parts = new List<string>();
            for (var current = value; current != null; current = current.parent)
                parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static Transform FindPanelRoot(
            Transform prefabRoot, Transform[] anchors, Transform[] forbidden)
        {
            var valid = anchors != null
                ? anchors.Where(t => t != null).Distinct().ToArray()
                : new Transform[0];
            if (valid.Length == 0) return null;

            var start = FindNearestCommonAncestor(valid) ?? valid[0];
            var anchorWidth = valid
                .Select(t => t as RectTransform).Where(r => r != null)
                .Select(r => Mathf.Abs(r.rect.width))
                .DefaultIfEmpty(1f).Max();
            var anchorHeight = valid
                .Select(t => t as RectTransform).Where(r => r != null)
                .Select(r => Mathf.Abs(r.rect.height))
                .DefaultIfEmpty(1f).Max();

            for (var current = start; current != null; current = current.parent)
            {
                if (!ReferenceEquals(current, start) &&
                    forbidden != null &&
                    forbidden.Any(t => t != null && IsDescendantOrSelf(t, current)))
                    break;

                if (current.GetComponent<Selectable>() != null ||
                    current.GetComponent<InputField>() != null ||
                    current.GetComponent<TMP_InputField>() != null ||
                    current.GetComponent<ScrollRect>() != null)
                    continue;

                var rect = current as RectTransform;
                if (rect == null) continue;

                var width = Mathf.Abs(rect.rect.width);
                var height = Mathf.Abs(rect.rect.height);
                if (width < anchorWidth * 1.03f || height < anchorHeight * 1.03f)
                    continue;

                if (HasPanelVisual(current))
                    return current;

                if (ReferenceEquals(current, prefabRoot))
                    break;
            }

            return null;
        }

        private static bool HasPanelVisual(Transform root)
        {
            if (root == null) return false;

            var queue = new Queue<KeyValuePair<Transform, int>>();
            queue.Enqueue(new KeyValuePair<Transform, int>(root, 0));

            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                var current = item.Key;
                if (item.Value > 2) continue;

                if (current.GetComponent<Selectable>() == null &&
                    current.GetComponent<InputField>() == null &&
                    current.GetComponent<TMP_InputField>() == null &&
                    IsPanelImage(current.GetComponent<Image>()))
                    return true;

                for (var index = 0; index < current.childCount; ++index)
                    queue.Enqueue(new KeyValuePair<Transform, int>(
                        current.GetChild(index), item.Value + 1));
            }

            return false;
        }

        private static bool IsPanelImage(Image image)
        {
            if (image == null) return false;
            var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            if (sprite == null) return false;

            var token = ((image.name ?? "") + " " + (sprite.name ?? "")).ToLowerInvariant();
            if (token.Contains("icon") || token.Contains("logo") ||
                token.Contains("preview") || token.Contains("busy") ||
                token.Contains("button"))
                return false;

            return image.type == Image.Type.Sliced ||
                   sprite.border.sqrMagnitude > .01f ||
                   image.GetComponent<Outline>() != null;
        }

        private static Transform FindHeaderRoot(
            Transform prefabRoot, List<Transform> excludedPanels, Transform close)
        {
            if (prefabRoot == null) return null;

            Image best = null;
            var bestScore = float.MinValue;

            foreach (var image in prefabRoot.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.rectTransform == null) continue;
                if (image.GetComponent<Selectable>() != null) continue;
                if (close != null && IsDescendantOrSelf(image.transform, close)) continue;
                if (excludedPanels != null &&
                    excludedPanels.Any(panel => panel != null &&
                        IsDescendantOrSelf(image.transform, panel)))
                    continue;

                var rect = image.rectTransform.rect;
                var width = Mathf.Abs(rect.width);
                var height = Mathf.Abs(rect.height);
                if (width < 350f || height < 35f || height > 220f) continue;
                if (width / Mathf.Max(1f, height) < 3.2f) continue;

                var color = image.color;
                var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
                var score =
                    (color.g + color.b - color.r) * 500000f +
                    width * height +
                    (sprite != null && sprite.border.sqrMagnitude > .01f ? 250000f : 0f);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = image;
                }
            }

            return best != null ? best.transform : null;
        }

        private static Transform FindNearestCommonAncestor(IEnumerable<Transform> transforms)
        {
            var valid = transforms != null
                ? transforms.Where(t => t != null).Distinct().ToArray()
                : new Transform[0];
            if (valid.Length == 0) return null;

            for (var candidate = valid[0]; candidate != null; candidate = candidate.parent)
            {
                var all = true;
                for (var index = 1; index < valid.Length; ++index)
                {
                    if (!IsDescendantOrSelf(valid[index], candidate))
                    {
                        all = false;
                        break;
                    }
                }
                if (all) return candidate;
            }

            return null;
        }

        private static bool IsDescendantOrSelf(Transform child, Transform ancestor)
        {
            if (child == null || ancestor == null) return false;
            for (var current = child; current != null; current = current.parent)
                if (ReferenceEquals(current, ancestor))
                    return true;
            return false;
        }

        // ================================================================
        // DATOS DEL MODELO
        // ================================================================
        private void LoadFieldsFromModel()
        {
            if (_level == null)
                return;

            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            var title = !string.IsNullOrWhiteSpace(settings.DisplayName)
                ? settings.DisplayName
                : ScenarioEditorStorage.CurrentMapName(_level);

            if (_inputTitle != null)
                _inputTitle.SetTextWithoutNotify(title ?? "");

            if (_inputAuthor != null)
                _inputAuthor.SetTextWithoutNotify(
                    ScenarioWorkshopMetaStore.Current(_level).author ?? "");

            if (_inputDescription != null)
                _inputDescription.SetTextWithoutNotify(
                    settings.DescriptionPerStar == null
                        ? ""
                        : string.Join("\n", settings.DescriptionPerStar));
        }

        private void CommitDrafts(bool persistAuthor)
        {
            if (_level == null)
                return;

            var settings = ScenarioEditorStorage.CurrentScenarioSettings(_level);
            settings.DisplayName = (_inputTitle != null ? _inputTitle.text : "").Trim();

            var description = (_inputDescription != null ? _inputDescription.text : "")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            settings.DescriptionPerStar = string.IsNullOrEmpty(description)
                ? new string[0]
                : description.Split(new[] { '\n' }, StringSplitOptions.None);

            ScenarioEditorStorage.SetScenarioSettings(_level, settings);

            if (_inputAuthor == null)
                return;

            var author = (_inputAuthor.text ?? "").Trim();
            if (persistAuthor)
                ScenarioWorkshopMetaStore.SetAuthor(_level, author);
            else
                ScenarioWorkshopMetaStore.Current(_level).author = author;
        }

        internal void Refresh()
        {
            if (_level == null)
                return;

            RefreshPreview();
            RefreshGallery();
            RefreshVisibilityButtons();
            RefreshActionButtons();
        }

        internal void OnPreviewCaptured()
        {
            // La imagen recién capturada queda seleccionada.
            _galleryIndex = Mathf.Max(0, GalleryPaths().Count - 1);
            RefreshPreview();
            RefreshGallery();
            RefreshActionButtons();
        }

        private void RefreshActionButtons()
        {
            if (_level == null)
                return;

            var mapPath = ScenarioEditorStorage.CurrentMapPath(_level);
            var previewPath = ScenarioWorkshopMetaStore.PreviewPath(_level);
            var title = (_inputTitle != null ? _inputTitle.text : "").Trim();

            var ready =
                !_publishing &&
                !string.IsNullOrWhiteSpace(title) &&
                !string.IsNullOrEmpty(mapPath) &&
                File.Exists(mapPath) &&
                !string.IsNullOrEmpty(previewPath) &&
                File.Exists(previewPath);

            ulong publishedId;
            var published = ScenarioWorkshopMetaStore.TryGetPublishedId(
                _level, out publishedId);

            if (_buttonCreateUpdate != null)
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonCreateUpdate, ready && published);

            if (_buttonCreateAsNew != null)
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonCreateAsNew, ready);

            if (_buttonSteamWorkshop != null)
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonSteamWorkshop, !_publishing && published);

            if (_buttonDeleteImage != null)
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonDeleteImage, !_publishing && _galleryCount > 0);

            if (_buttonChooseThumbnail != null)
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonChooseThumbnail,
                    !_publishing && _galleryCount > 0 && _galleryIndex != _thumbnailIndex);

            if (_buttonCapture != null)
                ExtContentUIUtils.SetSelectableInteractable(
                    _buttonCapture, !_publishing);
        }

        // ================================================================
        // VISIBILIDAD (Público / Solo amigos / Oculto)
        // ================================================================
        private void SetVisibility(ScenarioWorkshopVisibility visibility)
        {
            if (_publishing)
                return;

            _visibility = visibility;
            RefreshVisibilityButtons();
        }

        private void RefreshVisibilityButtons()
        {
            if (_buttonVisibilityPublic != null)
                ExtContentUIUtils.SetSelectableSelectability(
                    _buttonVisibilityPublic, !_publishing,
                    _visibility == ScenarioWorkshopVisibility.Public);

            if (_buttonVisibilityFriends != null)
                ExtContentUIUtils.SetSelectableSelectability(
                    _buttonVisibilityFriends, !_publishing,
                    _visibility == ScenarioWorkshopVisibility.FriendsOnly);

            if (_buttonVisibilityPrivate != null)
                ExtContentUIUtils.SetSelectableSelectability(
                    _buttonVisibilityPrivate, !_publishing,
                    _visibility == ScenarioWorkshopVisibility.Private);
        }

        // ================================================================
        // VISTA PREVIA
        // ================================================================
        private void RefreshPreview()
        {
            if (_imagePreviewImage == null)
                return;

            var path = CurrentGalleryPath();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ExtContentTextureUtils.UpdateImageTexture(
                    ref _imagePreviewImage, null, true);
                return;
            }

            try
            {
                var texture = ExtContentTextureUtils.LoadTexture2D(path);
                if (texture == null)
                {
                    ExtContentTextureUtils.UpdateImageTexture(
                        ref _imagePreviewImage, null, true);
                    return;
                }

                // La miniatura elegida se ve cuadrada y con icono; el resto, a 512 x 288.
                // En esta vista casi 1:1 no se usan mipmaps para conservar la nitidez.
                var thumbnailPath = ScenarioWorkshopMetaStore.PreviewPath(_level);
                var isThumbnail = string.Equals(
                    path, thumbnailPath, StringComparison.OrdinalIgnoreCase);

                var display = isThumbnail
                    ? WorkshopThumbnail.BuildLetterboxed(texture, 512, 288)
                    : WorkshopImageScaler.Scale(texture, 512, 288, true, false, true);
                Destroy(texture);

                if (display == null)
                {
                    ExtContentTextureUtils.UpdateImageTexture(
                        ref _imagePreviewImage, null, true);
                    return;
                }

                if (_previewTexture != null &&
                    !ReferenceEquals(_previewTexture, display))
                    Destroy(_previewTexture);

                _previewTexture = display;

                ExtContentTextureUtils.UpdateImageTexture(
                    ref _imagePreviewImage, _previewTexture, true);

                // Se muestra la imagen ENTERA (sin recorte) y la caja se fija a 16:9.
                if (_previewSprite != null) Destroy(_previewSprite);
                _previewSprite = Sprite.Create(
                    _previewTexture,
                    new Rect(0f, 0f, _previewTexture.width, _previewTexture.height),
                    new Vector2(.5f, .5f));
                _imagePreviewImage.sprite = _previewSprite;
                _imagePreviewImage.type = Image.Type.Simple;
                _imagePreviewImage.preserveAspect = true;

                ApplyPreviewBox();
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo mostrar la vista previa nativa: " + exception);
            }
        }

        private void SetBusy(bool value)
        {
            _publishing = value;

            if (_imageWorkshopBusyIcon != null)
                _imageWorkshopBusyIcon.gameObject.SetActive(value);

            if (_imageBusyModeInputBlocker != null)
                _imageBusyModeInputBlocker.gameObject.SetActive(value);

            if (_buttonCloseMenu != null)
                ExtContentUIUtils.SetSelectableInteractable(_buttonCloseMenu, !value);

            RefreshVisibilityButtons();
            RefreshActionButtons();
        }

        // ================================================================
        // ACCIONES
        // ================================================================
        private void BeginCapture()
        {
            if (_publishing ||
                ScenarioWorkshopCapture.IsActive ||
                _level == null)
                return;

            var saveName = ScenarioEditorStorage.NameForSave(_level);
            if (string.IsNullOrWhiteSpace(saveName))
            {
                ShowError(EditorLocalization.Get("editor.workshop.error.save_first"));
                return;
            }

            if (GalleryPaths().Count >= ScenarioWorkshopMetaData.MaxImages)
            {
                ShowError(EditorLocalization.Format(
                    "editor.workshop.error.max_images",
                    ScenarioWorkshopMetaData.MaxImages));
                return;
            }

            try
            {
                CommitDrafts(true);
                ScenarioEditorStorage.Save(_level, saveName);
                ScenarioWorkshopMetaStore.EnsureSavedIdentity(_level);
            }
            catch (Exception exception)
            {
                ShowError(EditorLocalization.Format(
                    "editor.workshop.error.save_map", exception.Message));
                return;
            }

            ClearUiSelection();
            gameObject.SetActive(false);
            ScenarioWorkshopCapture.Begin(_level, this);
        }

        private void BeginPublish(bool createNew)
        {
            if (_publishing || _level == null)
                return;

            var saveName = ScenarioEditorStorage.NameForSave(_level);
            if (string.IsNullOrWhiteSpace(saveName))
            {
                ShowError(EditorLocalization.Get("editor.workshop.error.save_first"));
                return;
            }

            if (string.IsNullOrWhiteSpace(
                    (_inputTitle != null ? _inputTitle.text : "").Trim()))
            {
                ShowError(EditorLocalization.Get(
                    "editor.workshop.error.title_required"));
                return;
            }

            var preview = ScenarioWorkshopMetaStore.PreviewPath(_level);
            if (string.IsNullOrEmpty(preview) || !File.Exists(preview))
            {
                ShowError(EditorLocalization.Get(
                    "editor.workshop.error.capture_first"));
                return;
            }

            if (!createNew)
            {
                ulong existingId;
                if (!ScenarioWorkshopMetaStore.TryGetPublishedId(_level, out existingId))
                {
                    ShowError(EditorLocalization.Get(
                        "editor.workshop.error.no_article"));
                    return;
                }
            }

            try
            {
                CommitDrafts(true);
                ScenarioEditorStorage.Save(_level, saveName);
                ScenarioWorkshopMetaStore.EnsureSavedIdentity(_level);
            }
            catch (Exception exception)
            {
                ShowError(EditorLocalization.Format(
                    "editor.workshop.error.save_map", exception.Message));
                return;
            }

            StartCoroutine(PublishCoroutine(createNew));
        }

        private IEnumerator PublishCoroutine(bool createNew)
        {
            SetBusy(true);

            string staging = null;
            string finalError = null;
            string mapPath = null;
            ScenarioWorkshopMetaData meta = null;
            string title = null;
            string description = null;
            ulong publishedId = 0UL;
            SteamWorkshopAsyncCall createWait = null;
            SteamWorkshopAsyncCall previewsQueryWait = null;
            uint existingAdditionalPreviews = 0U;

            try
            {
                if (!WorkshopUtils.AreSteamWorkshopFeaturesAvailable())
                    throw new InvalidOperationException(
                        EditorLocalization.Get(
                            "editor.workshop.error.unavailable"));

                mapPath = ScenarioEditorStorage.CurrentMapPath(_level);
                if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath))
                    throw new FileNotFoundException(
                        EditorLocalization.Get(
                            "editor.workshop.error.map_missing"), mapPath);

                meta = ScenarioWorkshopMetaStore.Current(_level);
                if (string.IsNullOrWhiteSpace(meta.localId))
                {
                    ScenarioWorkshopMetaStore.EnsureSavedIdentity(_level);
                    meta = ScenarioWorkshopMetaStore.Current(_level);
                }

                title = TrimTo(
                    (_inputTitle != null ? _inputTitle.text : "").Trim(), 120);
                description =
                    (_inputDescription != null ? _inputDescription.text : "").Trim();

                if (!string.IsNullOrWhiteSpace(meta.author))
                {
                    description = EditorLocalization.Format(
                        "editor.workshop.author_line", meta.author) +
                        (string.IsNullOrWhiteSpace(description)
                            ? ""
                            : "\n\n" + description);
                }
                description = TrimTo(description, 7000);

                if (createNew)
                {
                    createWait = SteamWorkshopBridge.BeginCreateItem();
                }
                else if (!ScenarioWorkshopMetaStore.TryGetPublishedId(
                             _level, out publishedId))
                {
                    throw new InvalidOperationException(
                        "El mapa no tiene PublishedFileId para actualizar.");
                }
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: preparación fallida: " + exception);
                finalError = exception.Message;
            }

            if (finalError == null && createWait != null)
            {
                yield return createWait.Waiter;

                try
                {
                    if (createWait.IoFailure ||
                        !SteamWorkshopBridge.IsResultOk(createWait.Result))
                        throw new InvalidOperationException(
                            "Steam CreateItem: " +
                            SteamWorkshopBridge.ResultCode(createWait.Result));

                    publishedId = SteamWorkshopBridge.ResultPublishedFileId(
                        createWait.Result);
                    if (publishedId == 0UL)
                        throw new InvalidOperationException(
                            EditorLocalization.Get(
                                "editor.workshop.error.invalid_published_id"));

                    ScenarioWorkshopMetaStore.SetPublishedFileId(_level, publishedId);
                    meta = ScenarioWorkshopMetaStore.Current(_level);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Workshop: CreateItem falló: " + exception);
                    finalError = exception.Message;
                }
            }

            if (finalError == null && !createNew)
            {
                try
                {
                    previewsQueryWait =
                        SteamWorkshopBridge.BeginAdditionalPreviewsQuery(publishedId);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Workshop: consulta de imágenes adicionales falló: " + exception);
                    finalError = exception.Message;
                }
            }

            if (finalError == null && previewsQueryWait != null)
            {
                yield return previewsQueryWait.Waiter;

                try
                {
                    if (previewsQueryWait.IoFailure ||
                        !SteamWorkshopBridge.IsResultOk(previewsQueryWait.Result))
                        throw new InvalidOperationException(
                            SteamWorkshopBridge.ResultCode(previewsQueryWait.Result));

                    existingAdditionalPreviews =
                        SteamWorkshopBridge.GetAdditionalPreviewsCount(
                            previewsQueryWait.Result);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Workshop: lectura de imágenes adicionales falló: " + exception);
                    finalError = exception.Message;
                }
                finally
                {
                    SteamWorkshopBridge.ReleaseQuery(previewsQueryWait.Result);
                }
            }

            SteamWorkshopAsyncCall submitWait = null;
            string steamThumbnailFolder = null;

            if (finalError == null)
            {
                try
                {
                    staging = BuildStagingFolder(meta.localId, mapPath);
                    var update = SteamWorkshopBridge.StartItemUpdate(publishedId);

                    var ok = true;
                    ok = SteamWorkshopBridge.SetItemTitle(update, title) && ok;
                    ok = SteamWorkshopBridge.SetItemDescription(update, description) && ok;
                    ok = SteamWorkshopBridge.SetItemUpdateLanguage(update, "english") && ok;
                    ok = SteamWorkshopBridge.SetItemVisibility(update, _visibility) && ok;

                    SteamWorkshopBridge.RemoveItemKeyValueTags(update, "ContentType");
                    ok = SteamWorkshopBridge.AddItemKeyValueTag(
                        update, "ContentType", "Bundle") && ok;

                    SteamWorkshopBridge.RemoveItemKeyValueTags(update, "AssetVersion");
                    ok = SteamWorkshopBridge.AddItemKeyValueTag(
                        update, "AssetVersion", "1") && ok;

                    ok = SteamWorkshopBridge.SetItemMetadata(
                        update,
                        ScenarioWorkshopMetaData.SteamMetadataPrefix + meta.localId) && ok;

                    var preview = ScenarioWorkshopMetaStore.PreviewPath(_level);
                    if (string.IsNullOrEmpty(preview) || !File.Exists(preview))
                        throw new FileNotFoundException(
                            EditorLocalization.Get(
                                "editor.workshop.error.preview_missing"));

                    var steamThumbnail = BuildSteamThumbnailFile(
                        preview, meta.localId, out steamThumbnailFolder);
                    ok = SteamWorkshopBridge.SetItemPreview(update, steamThumbnail) && ok;

                    for (var index = existingAdditionalPreviews; index > 0U; --index)
                        ok = SteamWorkshopBridge.RemoveItemPreview(
                            update, index - 1U) && ok;

                    var gallery = ScenarioWorkshopMetaStore.GalleryPaths(_level)
                        .Where(File.Exists)
                        .Where(image => !string.Equals(
                            image, preview, StringComparison.OrdinalIgnoreCase))
                        .Take(ScenarioWorkshopMetaData.MaxImages)
                        .ToList();
                    foreach (var image in gallery)
                        ok = SteamWorkshopBridge.AddItemPreviewFile(update, image) && ok;

                    ok = SteamWorkshopBridge.SetItemContent(update, staging) && ok;

                    if (!ok)
                        throw new InvalidOperationException(
                            EditorLocalization.Get(
                                "editor.workshop.error.parameters_rejected"));

                    submitWait = SteamWorkshopBridge.BeginSubmitItemUpdate(update, "");
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Workshop: preparación de subida fallida: " + exception);
                    finalError = exception.Message;
                }
            }

            var legalAgreementRequired = false;
            var publishedSuccessfully = false;

            if (finalError == null && submitWait != null)
            {
                yield return submitWait.Waiter;

                try
                {
                    if (submitWait.IoFailure ||
                        !SteamWorkshopBridge.IsResultOk(submitWait.Result))
                        throw new InvalidOperationException(
                            "Steam SubmitItemUpdate: " +
                            SteamWorkshopBridge.ResultCode(submitWait.Result));

                    ScenarioWorkshopMetaStore.SetPublishedFileId(_level, publishedId);

                    legalAgreementRequired =
                        SteamWorkshopBridge.ResultNeedsLegalAgreement(submitWait.Result);

                    publishedSuccessfully = true;
                    ScenarioWorkshopLibrary.EnumerateSubscribedMaps(true);
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Workshop: SubmitItemUpdate falló: " + exception);
                    finalError = exception.Message;
                }
            }

            if (!string.IsNullOrEmpty(staging))
                DeleteDirectory(staging);
            if (!string.IsNullOrEmpty(steamThumbnailFolder))
                DeleteDirectory(steamThumbnailFolder);

            SetBusy(false);
            Refresh();

            if (!string.IsNullOrEmpty(finalError))
            {
                ShowError(finalError);
                yield break;
            }

            if (publishedSuccessfully)
            {
                if (legalAgreementRequired)
                    OpenUserAgreement();
            }
        }

        private void OpenSteamWorkshop()
        {
            try
            {
                ulong publishedId;
                if (!ScenarioWorkshopMetaStore.TryGetPublishedId(_level, out publishedId))
                    return;

                var url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + publishedId;
                Application.OpenURL(url);
            }
            catch (Exception exception)
            {
                ShowError(EditorLocalization.Format(
                    "editor.workshop.error.open_steam", exception.Message));
            }
        }

        private static void OpenUserAgreement()
        {
            try
            {
                var config =
                    ExtContentUtils.ExtContentManager?.Config?
                        .WorkshopContentCreationManagerConfig?.Instance;

                if (config == null)
                    return;

                WorkshopUtils.OpenSteamOverlay(
                    config.steamOverlayWorkshopAgreementURL,
                    config.steamOverlayWorkshopAgreementURLBrowser);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo abrir el acuerdo: " + exception.Message);
            }
        }

        private static string BuildStagingFolder(string localId, string mapPath)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "UnderPressureWorkshop",
                string.IsNullOrWhiteSpace(localId)
                    ? Guid.NewGuid().ToString("N")
                    : localId);

            DeleteDirectory(root);
            Directory.CreateDirectory(root);

            File.Copy(mapPath, Path.Combine(root, Path.GetFileName(mapPath)), true);
            return root;
        }

        private static string BuildSteamThumbnailFile(
            string sourcePath, string localId, out string folder)
        {
            folder = Path.Combine(
                Path.GetTempPath(),
                "UnderPressureWorkshopPreviews",
                string.IsNullOrWhiteSpace(localId)
                    ? Guid.NewGuid().ToString("N")
                    : localId);

            DeleteDirectory(folder);
            Directory.CreateDirectory(folder);

            Texture2D source = null;
            Texture2D thumbnail = null;
            try
            {
                source = ExtContentTextureUtils.LoadTexture2D(sourcePath);
                if (source == null)
                    throw new IOException(EditorLocalization.Get(
                        "editor.workshop.error.preview_missing"));

                thumbnail = WorkshopThumbnail.Build(source);
                if (thumbnail == null)
                    throw new IOException(EditorLocalization.Get(
                        "editor.workshop.error.preview_missing"));

                var bytes = ImageConversion.EncodeToJPG(thumbnail, 92);
                if (bytes == null || bytes.Length == 0 || bytes.Length >= 1024 * 1024)
                    throw new IOException(EditorLocalization.Get(
                        "editor.workshop.error.preview_missing"));

                var path = Path.Combine(folder, "workshop_thumbnail.jpg");
                File.WriteAllBytes(path, bytes);
                return path;
            }
            finally
            {
                if (thumbnail != null) Destroy(thumbnail);
                if (source != null) Destroy(source);
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            try { Directory.Delete(path, true); }
            catch { }
        }

        private static string TrimTo(string value, int maximum)
        {
            value = value ?? "";
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static void ShowError(string body)
        {
            ExtContentMessages.ShowErrorMessageBox(
                ExtContentMessages.GetMessageString(
                    EMessageType.SomethingWentWrongTitle, true),
                body ?? "");
        }

        private void ClearUiSelection()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
                return;

            if (eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
                eventSystem.SetSelectedGameObject(null);
        }

        private void Close()
        {
            if (_publishing || ScenarioWorkshopCapture.IsActive)
                return;

            try { CommitDrafts(false); }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo conservar el borrador: " + exception.Message);
            }

            ClearUiSelection();
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_previewTexture != null)
                Destroy(_previewTexture);

            ClearGalleryResources();
            if (_previewSprite != null)
                Destroy(_previewSprite);

            if (ReferenceEquals(_active, this))
                _active = null;
        }
    }

    internal sealed class SteamWorkshopAsyncCall
    {
        private readonly object _waiter;
        private readonly FieldInfo _resultField;
        private readonly FieldInfo _ioFailureField;

        internal SteamWorkshopAsyncCall(
            object waiter, FieldInfo resultField, FieldInfo ioFailureField)
        {
            _waiter = waiter;
            _resultField = resultField;
            _ioFailureField = ioFailureField;
        }

        internal object Waiter => _waiter;

        internal object Result =>
            _waiter != null && _resultField != null
                ? _resultField.GetValue(_waiter)
                : null;

        internal bool IoFailure =>
            _waiter != null &&
            _ioFailureField != null &&
            Convert.ToBoolean(_ioFailureField.GetValue(_waiter));
    }

    internal static class SteamWorkshopBridge
    {
        private const BindingFlags StaticPublic =
            BindingFlags.Static | BindingFlags.Public;
        private const BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type _steamUgc;
        private static Type _steamUtils;
        private static Type _steamFriends;
        private static Type _publishedFileId;
        private static Type _eItemState;
        private static Type _eWorkshopFileType;
        private static Type _eVisibility;
        private static Type _eItemPreviewType;
        private static Type _createItemResult;
        private static Type _submitItemUpdateResult;
        private static Type _steamUgcQueryCompleted;
        private static Type _waitForCallResultOpen;

        private static Type SteamUgc =>
            _steamUgc ?? (_steamUgc = Resolve("Steamworks.SteamUGC"));

        private static Type SteamUtilsType =>
            _steamUtils ?? (_steamUtils = Resolve("Steamworks.SteamUtils"));

        private static Type SteamFriendsType =>
            _steamFriends ?? (_steamFriends = Resolve("Steamworks.SteamFriends"));

        private static Type PublishedFileIdType =>
            _publishedFileId ?? (_publishedFileId =
                Resolve("Steamworks.PublishedFileId_t"));

        private static Type ItemStateType =>
            _eItemState ?? (_eItemState =
                Resolve("Steamworks.EItemState"));

        private static Type WorkshopFileType =>
            _eWorkshopFileType ?? (_eWorkshopFileType =
                Resolve("Steamworks.EWorkshopFileType"));

        private static Type VisibilityType =>
            _eVisibility ?? (_eVisibility =
                Resolve("Steamworks.ERemoteStoragePublishedFileVisibility"));

        private static Type ItemPreviewType =>
            _eItemPreviewType ?? (_eItemPreviewType =
                Resolve("Steamworks.EItemPreviewType"));

        private static Type CreateItemResultType =>
            _createItemResult ?? (_createItemResult =
                Resolve("Steamworks.CreateItemResult_t"));

        private static Type SubmitItemUpdateResultType =>
            _submitItemUpdateResult ?? (_submitItemUpdateResult =
                Resolve("Steamworks.SubmitItemUpdateResult_t"));

        private static Type SteamUgcQueryCompletedType =>
            _steamUgcQueryCompleted ?? (_steamUgcQueryCompleted =
                Resolve("Steamworks.SteamUGCQueryCompleted_t"));

        private static Type WaitForCallResultOpen =>
            _waitForCallResultOpen ?? (_waitForCallResultOpen =
                typeof(Level).Assembly.GetType(
                    "TH20.WaitForCallResult`1", true));

        internal static SteamWorkshopAsyncCall BeginCreateItem()
        {
            var appId = GetAppId();
            var fileType = Enum.Parse(
                WorkshopFileType,
                "k_EWorkshopFileTypeFirst",
                false);
            var apiCall = InvokeStatic(
                SteamUgc, "CreateItem", appId, fileType);
            return BuildAsyncCall(CreateItemResultType, apiCall);
        }

        internal static SteamWorkshopAsyncCall BeginSubmitItemUpdate(
            object updateHandle, string changeNote)
        {
            var apiCall = InvokeStatic(
                SteamUgc, "SubmitItemUpdate",
                updateHandle, changeNote ?? "");
            return BuildAsyncCall(
                SubmitItemUpdateResultType, apiCall);
        }

        internal static object StartItemUpdate(ulong publishedFileId)
        {
            return InvokeStatic(
                SteamUgc,
                "StartItemUpdate",
                GetAppId(),
                CreatePublishedFileId(publishedFileId));
        }

        internal static SteamWorkshopAsyncCall BeginAdditionalPreviewsQuery(
            ulong publishedFileId)
        {
            var ids = Array.CreateInstance(PublishedFileIdType, 1);
            ids.SetValue(CreatePublishedFileId(publishedFileId), 0);

            var query = InvokeStatic(
                SteamUgc, "CreateQueryUGCDetailsRequest", ids, 1U);
            if (!Convert.ToBoolean(InvokeStatic(
                    SteamUgc, "SetReturnAdditionalPreviews", query, true)))
            {
                InvokeStatic(SteamUgc, "ReleaseQueryUGCRequest", query);
                throw new InvalidOperationException();
            }

            var apiCall = InvokeStatic(SteamUgc, "SendQueryUGCRequest", query);
            return BuildAsyncCall(SteamUgcQueryCompletedType, apiCall);
        }

        internal static bool SetItemTitle(
            object updateHandle, string value) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemTitle",
                updateHandle, value ?? ""));

        internal static bool SetItemDescription(
            object updateHandle, string value) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemDescription",
                updateHandle, value ?? ""));

        internal static bool SetItemUpdateLanguage(
            object updateHandle, string value) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemUpdateLanguage",
                updateHandle, value ?? "english"));

        internal static bool SetItemVisibility(
            object updateHandle, ScenarioWorkshopVisibility visibility)
        {
            string steamName;
            switch (visibility)
            {
                case ScenarioWorkshopVisibility.Public:
                    steamName = "k_ERemoteStoragePublishedFileVisibilityPublic";
                    break;
                case ScenarioWorkshopVisibility.FriendsOnly:
                    steamName = "k_ERemoteStoragePublishedFileVisibilityFriendsOnly";
                    break;
                default:
                    steamName = "k_ERemoteStoragePublishedFileVisibilityPrivate";
                    break;
            }

            var steamVisibility = Enum.Parse(
                VisibilityType, steamName, false);
            return Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemVisibility",
                updateHandle, steamVisibility));
        }

        internal static bool RemoveItemKeyValueTags(
            object updateHandle, string key) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "RemoveItemKeyValueTags",
                updateHandle, key ?? ""));

        internal static bool AddItemKeyValueTag(
            object updateHandle, string key, string value) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "AddItemKeyValueTag",
                updateHandle, key ?? "", value ?? ""));

        internal static bool SetItemMetadata(
            object updateHandle, string value) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemMetadata",
                updateHandle, value ?? ""));

        internal static bool SetItemPreview(
            object updateHandle, string path) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemPreview",
                updateHandle, path));

        internal static bool AddItemPreviewFile(
            object updateHandle, string path)
        {
            var imageType = Enum.Parse(
                ItemPreviewType, "k_EItemPreviewType_Image", false);
            return Convert.ToBoolean(InvokeStatic(
                SteamUgc, "AddItemPreviewFile",
                updateHandle, path, imageType));
        }

        internal static bool RemoveItemPreview(
            object updateHandle, uint index) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "RemoveItemPreview",
                updateHandle, index));

        internal static bool SetItemContent(
            object updateHandle, string path) =>
            Convert.ToBoolean(InvokeStatic(
                SteamUgc, "SetItemContent",
                updateHandle, path));

        internal static ulong[] GetSubscribedItems()
        {
            var count = Convert.ToUInt32(
                InvokeStatic(SteamUgc, "GetNumSubscribedItems"));
            if (count == 0) return new ulong[0];

            var array = Array.CreateInstance(
                PublishedFileIdType, checked((int)count));

            var actual = Convert.ToUInt32(
                InvokeStatic(
                    SteamUgc, "GetSubscribedItems",
                    array, count));

            var result = new ulong[checked((int)actual)];
            for (var index = 0U; index < actual; ++index)
                result[checked((int)index)] = PublishedFileIdValue(
                    array.GetValue(checked((int)index)));

            return result;
        }

        internal static uint GetItemState(ulong publishedFileId)
        {
            var value = InvokeStatic(
                SteamUgc, "GetItemState",
                CreatePublishedFileId(publishedFileId));
            return Convert.ToUInt32(value);
        }

        internal static bool IsInstalled(uint state)
        {
            var flag = Convert.ToUInt32(Enum.Parse(
                ItemStateType,
                "k_EItemStateInstalled",
                false));
            return (state & flag) != 0U;
        }

        internal static bool NeedsUpdate(uint state)
        {
            var flag = Convert.ToUInt32(Enum.Parse(
                ItemStateType,
                "k_EItemStateNeedsUpdate",
                false));
            return (state & flag) != 0U;
        }

        internal static bool DownloadItem(
            ulong publishedFileId, bool highPriority)
        {
            return Convert.ToBoolean(InvokeStatic(
                SteamUgc, "DownloadItem",
                CreatePublishedFileId(publishedFileId),
                highPriority));
        }

        internal static bool TryGetInstallFolder(
            ulong publishedFileId, out string folder)
        {
            folder = null;
            var args = new object[]
            {
                CreatePublishedFileId(publishedFileId),
                0UL,
                null,
                4096U,
                0U
            };

            var method = FindMethod(SteamUgc, "GetItemInstallInfo", args);
            var ok = Convert.ToBoolean(method.Invoke(null, args));
            if (!ok) return false;

            folder = args[2] as string;
            return !string.IsNullOrWhiteSpace(folder);
        }

        internal static void OpenWorkshopPage(string url)
        {
            InvokeStatic(
                SteamFriendsType,
                "ActivateGameOverlayToWebPage",
                url ?? "");
        }

        internal static bool IsResultOk(object result) =>
            string.Equals(
                ResultCode(result),
                "k_EResultOK",
                StringComparison.Ordinal);

        internal static string ResultCode(object result)
        {
            var value = ReadField(result, "m_eResult");
            return value != null
                ? value.ToString()
                : "Resultado desconocido";
        }

        internal static ulong ResultPublishedFileId(object result)
        {
            return PublishedFileIdValue(
                ReadField(result, "m_nPublishedFileId"));
        }

        internal static bool ResultNeedsLegalAgreement(object result)
        {
            var value = ReadField(
                result,
                "m_bUserNeedsToAcceptWorkshopLegalAgreement");
            return value != null && Convert.ToBoolean(value);
        }

        internal static uint GetAdditionalPreviewsCount(object queryResult)
        {
            var query = ReadField(queryResult, "m_handle");
            if (query == null) throw new InvalidOperationException();
            return Convert.ToUInt32(InvokeStatic(
                SteamUgc, "GetQueryUGCNumAdditionalPreviews", query, 0U));
        }

        internal static void ReleaseQuery(object queryResult)
        {
            var query = ReadField(queryResult, "m_handle");
            if (query != null)
                InvokeStatic(SteamUgc, "ReleaseQueryUGCRequest", query);
        }

        private static object GetAppId() =>
            InvokeStatic(SteamUtilsType, "GetAppID");

        private static object CreatePublishedFileId(ulong value)
        {
            return Activator.CreateInstance(
                PublishedFileIdType,
                new object[] { value });
        }

        private static ulong PublishedFileIdValue(object value)
        {
            if (value == null) return 0UL;

            var field = value.GetType().GetField(
                "m_PublishedFileId", InstanceAny);
            if (field == null)
                throw new MissingFieldException(
                    value.GetType().FullName,
                    "m_PublishedFileId");

            return Convert.ToUInt64(field.GetValue(value));
        }

        private static SteamWorkshopAsyncCall BuildAsyncCall(
            Type resultType, object apiCall)
        {
            if (resultType == null || apiCall == null)
                throw new InvalidOperationException(
                    "Steam no devolvió una llamada asíncrona válida.");

            var waiterType =
                WaitForCallResultOpen.MakeGenericType(resultType);
            var waiter = Activator.CreateInstance(
                waiterType, new[] { apiCall });

            var resultField = waiterType.GetField(
                "Result", InstanceAny);
            var ioFailureField = waiterType.GetField(
                "IoFailure", InstanceAny);

            if (waiter == null ||
                resultField == null ||
                ioFailureField == null)
                throw new InvalidOperationException(
                    "No se pudo construir WaitForCallResult de Steam.");

            return new SteamWorkshopAsyncCall(
                waiter, resultField, ioFailureField);
        }

        private static object ReadField(
            object instance, string name)
        {
            if (instance == null) return null;
            var field = instance.GetType().GetField(
                name, InstanceAny);
            return field?.GetValue(instance);
        }

        private static object InvokeStatic(
            Type type, string name, params object[] args)
        {
            var method = FindMethod(type, name, args);
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static MethodInfo FindMethod(
            Type type, string name, object[] args)
        {
            if (type == null)
                throw new TypeLoadException(
                    "Steamworks no está cargado.");

            var methods = type.GetMethods(StaticPublic)
                .Where(method =>
                    method.Name == name &&
                    method.GetParameters().Length == args.Length)
                .ToArray();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var compatible = true;

                for (var index = 0;
                     index < parameters.Length;
                     ++index)
                {
                    var parameterType =
                        parameters[index].ParameterType;
                    if (parameterType.IsByRef)
                        continue;

                    if (args[index] == null)
                    {
                        if (parameterType.IsValueType)
                        {
                            compatible = false;
                            break;
                        }
                        continue;
                    }

                    if (!parameterType.IsInstanceOfType(
                            args[index]))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible) return method;
            }

            throw new MissingMethodException(
                type.FullName,
                name + "(" + args.Length + " parámetros)");
        }

        private static Type Resolve(string fullName)
        {
            foreach (var assembly in
                     AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(
                    fullName, false, false);
                if (type != null) return type;
            }

            throw new TypeLoadException(
                "No se encontró " + fullName +
                " en los ensamblados cargados.");
        }
    }

    // ================================================================
    // CAPTURA DE PREVIEW
    // ================================================================
    // Reescala texturas por pasos de /2 con filtrado bilineal (sin escalones/aliasing).
    internal static class WorkshopImageScaler
    {
        internal static Texture2D Scale(
            Texture source, int width, int height,
            bool cropToAspect, bool mipmaps, bool allowUpscale, bool keepAlpha = false)
        {
            if (source == null || width <= 0 || height <= 0) return null;

            var ratio = (float)height / width;
            var scale = Vector2.one;
            var offset = Vector2.zero;

            if (cropToAspect)
            {
                var sourceAspect = (float)source.width / source.height;
                var targetAspect = (float)width / height;
                if (sourceAspect > targetAspect)
                {
                    scale.x = targetAspect / sourceAspect;
                    offset.x = (1f - scale.x) * .5f;
                }
                else if (sourceAspect < targetAspect)
                {
                    scale.y = sourceAspect / targetAspect;
                    offset.y = (1f - scale.y) * .5f;
                }
            }

            var croppedWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * scale.x));
            var croppedHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * scale.y));

            if (!allowUpscale && croppedWidth < width)
            {
                width = croppedWidth;
                height = Mathf.Max(1, Mathf.RoundToInt(width * ratio));
            }

            var current = RenderTexture.GetTemporary(
                croppedWidth, croppedHeight, 0, RenderTextureFormat.ARGB32);
            current.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, current, scale, offset);

            while (current.width > width * 2 || current.height > height * 2)
            {
                var nextWidth = Mathf.Max(width, current.width / 2);
                var nextHeight = Mathf.Max(height, current.height / 2);
                var next = RenderTexture.GetTemporary(
                    nextWidth, nextHeight, 0, RenderTextureFormat.ARGB32);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, next);
                RenderTexture.ReleaseTemporary(current);
                current = next;
            }

            if (current.width != width || current.height != height)
            {
                var last = RenderTexture.GetTemporary(
                    width, height, 0, RenderTextureFormat.ARGB32);
                last.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, last);
                RenderTexture.ReleaseTemporary(current);
                current = last;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = current;

            var result = new Texture2D(
                width, height, keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, mipmaps);
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            result.Apply(mipmaps, false);
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(current);
            return result;
        }
    }

    // Miniatura de Steam: cuadrado centrado de 288 x 288 + icono del mapa abajo a la derecha.
    // La imagen original NO se modifica: la miniatura se genera al vuelo.
    internal static class WorkshopThumbnail
    {
        internal const int Size = 288;
        private const int IconMax = 84;
        private const int IconMargin = 8;

        private static Texture2D _icon;
        private static bool _iconWarned;

        // <carpeta del juego>/Mods/Assets/MapIcon.png (ruta relativa a TPH).
        internal static string IconPath()
        {
            var root = Directory.GetParent(Application.dataPath);
            if (root == null) return null;

            return Path.Combine(
                Path.Combine(Path.Combine(root.FullName, "Mods"), "Assets"),
                "MapIcon.png");
        }

        private static Texture2D LoadIcon()
        {
            if (_icon != null) return _icon;

            var path = IconPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (!_iconWarned)
                {
                    _iconWarned = true;
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Workshop: no se encontró el icono de miniatura: " + path);
                }
                return null;
            }

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                _icon = texture;
                return _icon;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo leer MapIcon.png: " + exception.Message);
                return null;
            }
        }

        // Miniatura cuadrada centrada en un lienzo transparente (p. ej. 512 x 288), para que
        // el visor 16:9 la muestre CUADRADA y sin zoom.
        internal static Texture2D BuildLetterboxed(Texture source, int width, int height)
        {
            var square = Build(source);
            if (square == null) return null;

            var squarePixels = square.GetPixels32();
            var side = square.width;
            UnityEngine.Object.Destroy(square);

            var pixels = new Color32[width * height];
            var offsetX = (width - side) / 2;
            var offsetY = (height - side) / 2;

            for (var y = 0; y < side; ++y)
            {
                var dy = offsetY + y;
                if (dy < 0 || dy >= height) continue;

                for (var x = 0; x < side; ++x)
                {
                    var dx = offsetX + x;
                    if (dx < 0 || dx >= width) continue;
                    pixels[dy * width + dx] = squarePixels[y * side + x];
                }
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply(false, false);
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Bilinear;
            return result;
        }

        internal static Texture2D Build(Texture source)
        {
            var square = WorkshopImageScaler.Scale(source, Size, Size, true, false, true);
            if (square == null) return null;

            var pixels = square.GetPixels32();
            UnityEngine.Object.Destroy(square);

            var icon = LoadIcon();
            if (icon != null)
            {
                var ratio = (float)icon.width / icon.height;
                var iconWidth = ratio >= 1f
                    ? IconMax : Mathf.Max(1, Mathf.RoundToInt(IconMax * ratio));
                var iconHeight = ratio >= 1f
                    ? Mathf.Max(1, Mathf.RoundToInt(IconMax / ratio)) : IconMax;

                var scaled = WorkshopImageScaler.Scale(
                    icon, iconWidth, iconHeight, false, false, true, true);

                if (scaled != null)
                {
                    var iconPixels = scaled.GetPixels32();
                    UnityEngine.Object.Destroy(scaled);

                    for (var y = 0; y < iconHeight; ++y)
                    {
                        for (var x = 0; x < iconWidth; ++x)
                        {
                            var s = iconPixels[y * iconWidth + x];
                            if (s.a == 0) continue;

                            // Esquina inferior derecha (la fila 0 es la de abajo).
                            var dx = Size - IconMargin - iconWidth + x;
                            var dy = IconMargin + y;
                            if (dx < 0 || dx >= Size || dy < 0 || dy >= Size) continue;

                            var index = dy * Size + dx;
                            var d = pixels[index];
                            var a = s.a / 255f;
                            d.r = (byte)Mathf.RoundToInt(s.r * a + d.r * (1f - a));
                            d.g = (byte)Mathf.RoundToInt(s.g * a + d.g * (1f - a));
                            d.b = (byte)Mathf.RoundToInt(s.b * a + d.b * (1f - a));
                            d.a = 255;
                            pixels[index] = d;
                        }
                    }
                }
            }

            var result = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            result.SetPixels32(pixels);
            result.Apply(true, false);
            result.wrapMode = TextureWrapMode.Clamp;
            result.filterMode = FilterMode.Trilinear;
            return result;
        }
    }

    internal sealed class ScenarioWorkshopCapture : MonoBehaviour
    {
        private static ScenarioWorkshopCapture _active;

        private readonly Dictionary<Canvas, bool> _canvasStates =
            new Dictionary<Canvas, bool>();

        private Level _level;
        private ScenarioWorkshopPanel _panel;
        private Canvas _overlayCanvas;
        private bool _capturing;

        internal static bool IsActive =>
            _active != null &&
            _active.gameObject != null &&
            _active.gameObject.activeInHierarchy;

        internal static void Begin(
            Level level, ScenarioWorkshopPanel panel)
        {
            if (IsActive || level == null || panel == null) return;

            var root = new GameObject(
                "UP Workshop Capture",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(ScenarioWorkshopCapture));

            var rect = (RectTransform)root.transform;
            rect.SetParent(null, false);
            Stretch(rect);

            _active = root.GetComponent<ScenarioWorkshopCapture>();
            _active._level = level;
            _active._panel = panel;
            _active.HideUiAndBuildGuide();
            _active.InstallInputBlock();
        }

        // ---- durante la captura NO se puede coger ni seleccionar ningún objeto ----
        private Harmony _inputBlock;

        private void InstallInputBlock()
        {
            try
            {
                var harmony = new Harmony("underpressure.workshop.capture.input");
                var prefix = new HarmonyMethod(
                    typeof(ScenarioWorkshopCapture), nameof(BlockLeftMouse));

                foreach (var name in new[]
                         { "GetMouseButtonDown", "GetMouseButtonUp", "GetMouseButton" })
                {
                    var original = AccessTools.Method(
                        typeof(Input), name, new[] { typeof(int) });
                    if (original != null)
                        harmony.Patch(original, prefix: prefix);
                }

                _inputBlock = harmony;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo bloquear el ratón durante la captura: " +
                    exception.Message);
            }
        }

        private void RemoveInputBlock()
        {
            if (_inputBlock == null) return;

            try { _inputBlock.UnpatchSelf(); }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Workshop: no se pudo restaurar el ratón: " + exception.Message);
            }

            _inputBlock = null;
        }

        // Solo el botón izquierdo (el derecho/central siguen moviendo la cámara).
        private static bool BlockLeftMouse(int button, ref bool __result)
        {
            if (button != 0 || !IsActive) return true;

            __result = false;
            return false;
        }

        private void Update()
        {
            if (_capturing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Finish(false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
                StartCoroutine(CaptureCoroutine());
        }

        private void LateUpdate()
        {
            if (!IsActive) return;

            // La deteccion puede continuar con normalidad. Se vacia unicamente
            // la lista visual nativa antes de que el postproceso dibuje el contorno.
            HighlightRendererProxy.Instance.Clear();
        }

        private void HideUiAndBuildGuide()
        {
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || !canvas.gameObject.scene.IsValid()) continue;
                _canvasStates[canvas] = canvas.enabled;
                canvas.enabled = false;
            }

            _overlayCanvas = GetComponent<Canvas>();
            _overlayCanvas.overrideSorting = true;
            _overlayCanvas.sortingOrder = 10000;
            _overlayCanvas.enabled = true;

            // Capa invisible a pantalla completa: ningún clic llega a la interfaz del juego.
            var blocker = NewCaptureImage("Input Blocker", new Color(0f, 0f, 0f, 0f));
            Stretch((RectTransform)blocker.transform);
            blocker.GetComponent<Image>().raycastTarget = true;

            var cyan = new Color(.07f, .72f, 1f, 1f);
            const float border = 7f;

            CreateBorder("Top",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(.5f, 1f),
                new Vector2(0f, 0f),
                new Vector2(0f, border), cyan);

            CreateBorder("Bottom",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(.5f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, border), cyan);

            CreateBorder("Left",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, .5f),
                new Vector2(0f, 0f),
                new Vector2(border, 0f), cyan);

            CreateBorder("Right",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, .5f),
                new Vector2(0f, 0f),
                new Vector2(border, 0f), cyan);

            var bar = NewCaptureImage(
                "Instruction Background",
                new Color(0f, .30f, .48f, .86f));
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(.5f, 1f);
            barRect.anchorMax = new Vector2(.5f, 1f);
            barRect.pivot = new Vector2(.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -18f);
            barRect.sizeDelta = new Vector2(720f, 58f);

            var text = NewCaptureText(
                EditorLocalization.Get("editor.workshop.capture_instruction"),
                23f, Color.white);
            text.transform.SetParent(bar.transform, false);
            Stretch(text.rectTransform);
        }

        private IEnumerator CaptureCoroutine()
        {
            _capturing = true;
            _overlayCanvas.enabled = false;

            yield return new WaitForEndOfFrame();

            Texture2D screenshot = null;
            var captured = false;

            try
            {
                var width = Mathf.Max(1, Screen.width);
                var height = Mathf.Max(1, Screen.height);

                screenshot = new Texture2D(
                    width, height, TextureFormat.RGB24, false);

                screenshot.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0, 0, false);
                screenshot.Apply(false, false);

                // Máximo 1366 x 768 (16:9, recorte centrado si la ventana es otra).
                var scaled = WorkshopImageScaler.Scale(
                    screenshot, 1366, 768, true, false, false);
                var jpg = EncodePreviewJpeg(scaled != null ? scaled : screenshot);
                if (scaled != null) Destroy(scaled);

                ScenarioWorkshopMetaStore.AddImage(_level, jpg);
                captured = true;
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Workshop: captura fallida: " + exception);
            }

            if (screenshot != null)
                Destroy(screenshot);

            if (captured)
            {
                Finish(true);
                yield break;
            }

            _overlayCanvas.enabled = true;
            _capturing = false;
        }

        private static byte[] EncodePreviewJpeg(Texture2D source)
        {
            var quality = 92;
            byte[] bytes = null;

            while (quality >= 55)
            {
                bytes = ImageConversion.EncodeToJPG(source, quality);
                if (bytes != null &&
                    bytes.Length > 0 &&
                    bytes.Length < 900 * 1024)
                    return bytes;
                quality -= 8;
            }

            if (bytes == null || bytes.Length == 0)
                throw new IOException(
                    "No se pudo codificar la imagen de Workshop.");

            return bytes;
        }

        private void Finish(bool captured)
        {
            RemoveInputBlock();

            foreach (var pair in _canvasStates)
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            _canvasStates.Clear();

            if (_panel != null)
            {
                _panel.gameObject.SetActive(true);
                if (captured) _panel.OnPreviewCaptured();
                else _panel.Refresh();
            }

            if (ReferenceEquals(_active, this))
                _active = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            RemoveInputBlock();

            if (_canvasStates.Count > 0)
            {
                foreach (var pair in _canvasStates)
                    if (pair.Key != null)
                        pair.Key.enabled = pair.Value;
                _canvasStates.Clear();
            }
            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private void CreateBorder(
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var image = NewCaptureImage(name, color);
            var rect = (RectTransform)image.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private GameObject NewCaptureImage(
            string name, Color color)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            root.transform.SetParent(transform, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return root;
        }

        private TextMeshProUGUI NewCaptureText(
            string value, float size, Color color)
        {
            var root = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            var text = root.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    // ESC durante la captura o con el panel abierto no debe abrir otro menú.
    [HarmonyPatch(typeof(Level), "HandlePauseLogic")]
    internal static class ScenarioWorkshopEscapePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (!ScenarioEditorSession.Active ||
                !ScenarioEditorFlow.IsRunning ||
                !Input.GetKeyDown(KeyCode.Escape))
                return true;

            if (ScenarioWorkshopCapture.IsActive)
                return false;

            if (ScenarioWorkshopPanel.IsOpen)
            {
                ScenarioWorkshopPanel.CloseActive();
                return false;
            }

            return true;
        }
    }
}
