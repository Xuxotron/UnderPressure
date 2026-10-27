// Paquete unico de mapa: ZIP sin compresion con extension .upmap.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace UnderPressure.Editor
{
    internal static class ScenarioMapPackage
    {
        internal const string Extension = ".upmap";
        internal const string SearchPattern = "*.upmap";
        private const int MaxEntries = 512;
        private const long MaxExtractedBytes = 1024L * 1024L * 1024L;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            internal long Length;
            internal DateTime ModifiedUtc;
            internal string Folder;
        }

        internal static IEnumerable<string> Enumerate(string root)
        {
            if (!Directory.Exists(root)) yield break;
            foreach (var creator in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(creator);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("_", StringComparison.Ordinal))
                    continue;
                foreach (var package in Directory.GetFiles(creator, SearchPattern,
                             SearchOption.TopDirectoryOnly))
                    yield return package;
            }
        }

        internal static bool IsDirectPackage(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(full), Extension, StringComparison.OrdinalIgnoreCase))
                return false;
            var rootFull = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var creator = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(creator)) return false;
            var parent = Path.GetDirectoryName(creator);
            return string.Equals(parent, rootFull, StringComparison.OrdinalIgnoreCase);
        }

        internal static string CreateStagingDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "UnderPressure", "upmaps");
            Directory.CreateDirectory(root);
            var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        internal static string Materialize(string package)
        {
            var full = Path.GetFullPath(package);
            var info = new FileInfo(full);
            if (!info.Exists) throw new FileNotFoundException("Paquete de mapa inexistente.", full);
            lock (Sync)
            {
                CacheEntry cached;
                if (Cache.TryGetValue(full, out cached) && cached.Length == info.Length &&
                    cached.ModifiedUtc == info.LastWriteTimeUtc && Directory.Exists(cached.Folder))
                    return cached.Folder;
            }

            var folder = CreateStagingDirectory();
            try
            {
                Extract(full, folder);
                lock (Sync)
                {
                    CacheEntry old;
                    if (Cache.TryGetValue(full, out old) && old.Folder != folder)
                        DeleteDirectory(old.Folder);
                    Cache[full] = new CacheEntry
                    {
                        Length = info.Length,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        Folder = folder
                    };
                }
                return folder;
            }
            catch
            {
                DeleteDirectory(folder);
                throw;
            }
        }

        internal static string ReadText(string package, string entryName, int maximumBytes)
        {
            ValidateEntryName(entryName);
            using (var stream = File.OpenRead(package))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
                    throw new InvalidDataException("Cantidad de entradas incorrecta en el paquete.");
                var matches = archive.Entries.Where(entry => string.Equals(
                    entry.FullName, entryName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException("Entrada inexistente o duplicada: " + entryName);
                var entry = matches[0];
                if (entry == null || entry.Length > maximumBytes)
                    throw new InvalidDataException("Entrada inexistente o demasiado grande: " + entryName);
                using (var input = entry.Open())
                using (var reader = new StreamReader(input, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }

        internal static void Extract(string package, string destination)
        {
            Directory.CreateDirectory(destination);
            var root = Path.GetFullPath(destination).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            long total = 0;
            using (var stream = File.OpenRead(package))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
                    throw new InvalidDataException("Cantidad de entradas incorrecta en el paquete.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        throw new InvalidDataException("El paquete no admite directorios internos.");
                    ValidateEntryName(entry.FullName);
                    if (!names.Add(entry.FullName))
                        throw new InvalidDataException("Entrada duplicada en el paquete: " + entry.FullName);
                    total += entry.Length;
                    if (total > MaxExtractedBytes)
                        throw new InvalidDataException("El paquete supera el tamano permitido.");
                    var output = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Ruta no permitida en el paquete.");
                    using (var input = entry.Open())
                    using (var file = new FileStream(output, FileMode.Create, FileAccess.Write,
                               FileShare.None))
                        input.CopyTo(file);
                }
            }
        }

        internal static void Publish(string sourceFolder, string targetPackage,
            Action<string> verify)
        {
            var target = Path.GetFullPath(targetPackage);
            var parent = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(parent)) throw new InvalidDataException("Destino .upmap incorrecto.");
            Directory.CreateDirectory(parent);
            var temporary = Path.Combine(parent,
                Path.GetFileNameWithoutExtension(target) + "." + Guid.NewGuid().ToString("N") + Extension);
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                           FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                {
                    var files = Directory.GetFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (files.Length == 0 || files.Length > MaxEntries)
                        throw new InvalidDataException("Cantidad de archivos incorrecta para el paquete.");
                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file);
                        ValidateEntryName(name);
                        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                        using (var input = File.OpenRead(file))
                        using (var output = entry.Open())
                            input.CopyTo(output);
                    }
                }
                verify?.Invoke(temporary);
                Invalidate(temporary);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                Invalidate(target);
            }
            finally
            {
                Invalidate(temporary);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static string UpdateEntry(string package, string entryName, byte[] content,
            Action<string> verify)
        {
            ValidateEntryName(entryName);
            if (content == null) throw new ArgumentNullException(nameof(content));
            var staging = CreateStagingDirectory();
            try
            {
                Extract(package, staging);
                File.WriteAllBytes(Path.Combine(staging, entryName), content);
                Publish(staging, package, verify);
                return Materialize(package);
            }
            finally { DeleteDirectory(staging); }
        }

        internal static void Invalidate(string package)
        {
            var full = Path.GetFullPath(package);
            lock (Sync)
            {
                CacheEntry cached;
                if (!Cache.TryGetValue(full, out cached)) return;
                Cache.Remove(full);
                DeleteDirectory(cached.Folder);
            }
        }

        internal static void DeleteDirectory(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            try { Directory.Delete(folder, true); }
            catch (Exception) { }
        }

        private static void ValidateEntryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
                name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 ||
                name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Nombre de entrada no permitido: " + name);
        }
    }
}
