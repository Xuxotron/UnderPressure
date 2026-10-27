// Paquete unico de mapa: ZIP sin compresion con extension .upmap.
using System;
using System.Collections.Generic;
using System.IO;
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
        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint EndHeaderSignature = 0x06054b50;
        private const ushort Utf8Flag = 0x0800;
        private const ushort StoredMethod = 0;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            internal long Length;
            internal DateTime ModifiedUtc;
            internal string Folder;
        }

        private sealed class PackageEntry
        {
            internal string Name;
            internal ushort Flags;
            internal ushort Method;
            internal uint Crc32;
            internal uint CompressedSize;
            internal uint Length;
            internal uint LocalHeaderOffset;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

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
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                var entries = ReadDirectory(stream, reader);
                var matches = entries.Where(entry => string.Equals(
                    entry.Name, entryName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException("Entrada inexistente o duplicada: " + entryName);
                if (matches[0].Length > maximumBytes)
                    throw new InvalidDataException("Entrada demasiado grande: " + entryName);
                var bytes = ReadEntry(stream, reader, matches[0]);
                using (var input = new MemoryStream(bytes, false))
                using (var text = new StreamReader(input, Encoding.UTF8, true))
                    return text.ReadToEnd();
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
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                var entries = ReadDirectory(stream, reader);
                foreach (var entry in entries)
                {
                    total += entry.Length;
                    if (total > MaxExtractedBytes)
                        throw new InvalidDataException("El paquete supera el tamano permitido.");
                    var output = Path.GetFullPath(Path.Combine(root, entry.Name));
                    if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Ruta no permitida en el paquete.");
                    var bytes = ReadEntry(stream, reader, entry);
                    File.WriteAllBytes(output, bytes);
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
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    var files = Directory.GetFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (files.Length == 0 || files.Length > MaxEntries)
                        throw new InvalidDataException("Cantidad de archivos incorrecta para el paquete.");
                    var entries = new List<PackageEntry>(files.Length);
                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file);
                        ValidateEntryName(name);
                        var bytes = File.ReadAllBytes(file);
                        if (bytes.LongLength > uint.MaxValue)
                            throw new InvalidDataException("Archivo demasiado grande para el paquete: " + name);
                        var entry = new PackageEntry
                        {
                            Name = name,
                            Flags = Utf8Flag,
                            Method = StoredMethod,
                            Crc32 = ComputeCrc32(bytes),
                            CompressedSize = (uint)bytes.Length,
                            Length = (uint)bytes.Length,
                            LocalHeaderOffset = CheckedOffset(stream.Position)
                        };
                        WriteLocalHeader(writer, entry);
                        writer.Write(bytes);
                        entries.Add(entry);
                    }
                    WriteDirectory(writer, entries);
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

        private static List<PackageEntry> ReadDirectory(Stream stream, BinaryReader reader)
        {
            if (stream.Length < 22)
                throw new InvalidDataException("Cabecera ZIP inexistente.");
            var searchLength = (int)Math.Min(stream.Length, 22L + ushort.MaxValue);
            stream.Position = stream.Length - searchLength;
            var tail = reader.ReadBytes(searchLength);
            var endOffset = -1;
            for (var i = tail.Length - 22; i >= 0; --i)
            {
                if (ReadUInt32(tail, i) != EndHeaderSignature) continue;
                var commentLength = ReadUInt16(tail, i + 20);
                if (i + 22 + commentLength == tail.Length)
                {
                    endOffset = i;
                    break;
                }
            }
            if (endOffset < 0)
                throw new InvalidDataException("Final ZIP inexistente.");

            var disk = ReadUInt16(tail, endOffset + 4);
            var directoryDisk = ReadUInt16(tail, endOffset + 6);
            var entriesOnDisk = ReadUInt16(tail, endOffset + 8);
            var entryCount = ReadUInt16(tail, endOffset + 10);
            var directorySize = ReadUInt32(tail, endOffset + 12);
            var directoryOffset = ReadUInt32(tail, endOffset + 16);
            if (disk != 0 || directoryDisk != 0 || entriesOnDisk != entryCount)
                throw new InvalidDataException("No se admiten paquetes ZIP divididos.");
            if (entryCount == 0 || entryCount > MaxEntries || entryCount == ushort.MaxValue ||
                directorySize == uint.MaxValue || directoryOffset == uint.MaxValue)
                throw new InvalidDataException("Cantidad de entradas incorrecta en el paquete.");
            if ((long)directoryOffset + directorySize > stream.Length)
                throw new InvalidDataException("Directorio ZIP fuera del archivo.");

            stream.Position = directoryOffset;
            var entries = new List<PackageEntry>(entryCount);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < entryCount; ++index)
            {
                if (reader.ReadUInt32() != CentralHeaderSignature)
                    throw new InvalidDataException("Directorio ZIP incorrecto.");
                reader.ReadUInt16();
                reader.ReadUInt16();
                var flags = reader.ReadUInt16();
                var method = reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt16();
                var crc = reader.ReadUInt32();
                var compressedSize = reader.ReadUInt32();
                var length = reader.ReadUInt32();
                var nameLength = reader.ReadUInt16();
                var extraLength = reader.ReadUInt16();
                var commentLength = reader.ReadUInt16();
                var startDisk = reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt32();
                var localOffset = reader.ReadUInt32();
                if ((flags & 1) != 0)
                    throw new InvalidDataException("No se admiten entradas ZIP cifradas.");
                if (method != StoredMethod || compressedSize != length)
                    throw new InvalidDataException("El paquete debe ser un ZIP sin compresion.");
                if (startDisk != 0 || compressedSize == uint.MaxValue || length == uint.MaxValue ||
                    localOffset == uint.MaxValue)
                    throw new InvalidDataException("No se admite ZIP64 ni paquetes divididos.");
                var nameBytes = reader.ReadBytes(nameLength);
                if (nameBytes.Length != nameLength)
                    throw new EndOfStreamException("Nombre ZIP incompleto.");
                var name = DecodeName(nameBytes, flags);
                ValidateEntryName(name);
                if (!names.Add(name))
                    throw new InvalidDataException("Entrada duplicada en el paquete: " + name);
                SkipExact(stream, extraLength + commentLength);
                entries.Add(new PackageEntry
                {
                    Name = name,
                    Flags = flags,
                    Method = method,
                    Crc32 = crc,
                    CompressedSize = compressedSize,
                    Length = length,
                    LocalHeaderOffset = localOffset
                });
            }
            return entries;
        }

        private static byte[] ReadEntry(Stream stream, BinaryReader reader, PackageEntry entry)
        {
            stream.Position = entry.LocalHeaderOffset;
            if (reader.ReadUInt32() != LocalHeaderSignature)
                throw new InvalidDataException("Cabecera local ZIP incorrecta: " + entry.Name);
            reader.ReadUInt16();
            var flags = reader.ReadUInt16();
            var method = reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            var nameLength = reader.ReadUInt16();
            var extraLength = reader.ReadUInt16();
            if ((flags & 1) != 0 || method != StoredMethod || method != entry.Method)
                throw new InvalidDataException("Entrada ZIP incompatible: " + entry.Name);
            SkipExact(stream, nameLength + extraLength);
            if ((long)entry.CompressedSize > int.MaxValue ||
                stream.Position + entry.CompressedSize > stream.Length)
                throw new InvalidDataException("Entrada ZIP demasiado grande o incompleta: " + entry.Name);
            var bytes = reader.ReadBytes((int)entry.CompressedSize);
            if (bytes.Length != entry.CompressedSize || ComputeCrc32(bytes) != entry.Crc32)
                throw new InvalidDataException("CRC ZIP incorrecto: " + entry.Name);
            return bytes;
        }

        private static void WriteLocalHeader(BinaryWriter writer, PackageEntry entry)
        {
            var name = Encoding.UTF8.GetBytes(entry.Name);
            writer.Write(LocalHeaderSignature);
            writer.Write((ushort)20);
            writer.Write(entry.Flags);
            writer.Write(entry.Method);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(entry.Crc32);
            writer.Write(entry.CompressedSize);
            writer.Write(entry.Length);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)0);
            writer.Write(name);
        }

        private static void WriteDirectory(BinaryWriter writer, IList<PackageEntry> entries)
        {
            var stream = writer.BaseStream;
            var directoryOffset = CheckedOffset(stream.Position);
            foreach (var entry in entries)
            {
                var name = Encoding.UTF8.GetBytes(entry.Name);
                writer.Write(CentralHeaderSignature);
                writer.Write((ushort)20);
                writer.Write((ushort)20);
                writer.Write(entry.Flags);
                writer.Write(entry.Method);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(entry.Crc32);
                writer.Write(entry.CompressedSize);
                writer.Write(entry.Length);
                writer.Write((ushort)name.Length);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((uint)0);
                writer.Write(entry.LocalHeaderOffset);
                writer.Write(name);
            }
            var directorySize = CheckedOffset(stream.Position - directoryOffset);
            writer.Write(EndHeaderSignature);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)entries.Count);
            writer.Write((ushort)entries.Count);
            writer.Write(directorySize);
            writer.Write(directoryOffset);
            writer.Write((ushort)0);
            writer.Flush();
        }

        private static uint CheckedOffset(long value)
        {
            if (value < 0 || value > uint.MaxValue)
                throw new InvalidDataException("El paquete supera el tamano ZIP admitido.");
            return (uint)value;
        }

        private static string DecodeName(byte[] bytes, ushort flags)
        {
            if ((flags & Utf8Flag) != 0) return Encoding.UTF8.GetString(bytes);
            try { return Encoding.GetEncoding(437).GetString(bytes); }
            catch (ArgumentException) { return Encoding.UTF8.GetString(bytes); }
        }

        private static void SkipExact(Stream stream, int count)
        {
            if (count < 0 || stream.Position + count > stream.Length)
                throw new EndOfStreamException("Paquete ZIP incompleto.");
            stream.Position += count;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 |
                          bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; ++index)
            {
                var value = index;
                for (var bit = 0; bit < 8; ++bit)
                    value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
                table[index] = value;
            }
            return table;
        }

        private static uint ComputeCrc32(byte[] bytes)
        {
            var crc = uint.MaxValue;
            for (var i = 0; i < bytes.Length; ++i)
                crc = CrcTable[(crc ^ bytes[i]) & 0xff] ^ (crc >> 8);
            return crc ^ uint.MaxValue;
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
