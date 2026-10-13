// Updated: 2026-10-12 — rutas Maps, Saves y configuración única
// Configuracion, registro y componentes independientes de BepInEx para Under Pressure.
// Compilar UNICAMENTE en UnderPressure Base. Editor y Power referencian el ensamblado Base.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnderPressure.LoaderRuntime
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ModPluginAttribute : Attribute
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }
        public ModPluginAttribute(string id, string name, string version)
        {
            Id = id;
            Name = name;
            Version = version;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class ModDependencyAttribute : Attribute
    {
        public enum DependencyFlags { HardDependency, SoftDependency }
        public string Id { get; private set; }
        public DependencyFlags Flags { get; private set; }
        public ModDependencyAttribute(string id, DependencyFlags flags)
        {
            Id = id;
            Flags = flags;
        }
    }

    public static class Paths
    {
        // Equivalente local de Paths.ConfigPath; nunca lee ni requiere BepInEx.
        public static string GameRoot
        {
            get
            {
                var dataPath = Application.dataPath;
                var root = Path.GetDirectoryName(dataPath);
                return string.IsNullOrEmpty(root) ? Environment.CurrentDirectory : root;
            }
        }
        public static string PluginPath { get { return Path.Combine(GameRoot, "Mods"); } }
        public static string ConfigPath { get { return PluginPath; } }
        public static string MapsPath { get { return Path.Combine(PluginPath, "Maps"); } }
        public static string SavesPath { get { return Path.Combine(PluginPath, "Saves"); } }
    }

    public abstract class BaseModComponent : MonoBehaviour
    {
        private Logging.ModLogSource _logger;
        private static readonly Configuration.ConfigFile SharedConfig = new Configuration.ConfigFile("underpressure");

        protected Logging.ModLogSource Logger
        {
            get
            {
                if (_logger == null)
                {
                    var plugin = Attribute.GetCustomAttribute(GetType(), typeof(ModPluginAttribute)) as ModPluginAttribute;
                    _logger = new Logging.ModLogSource(plugin == null ? GetType().Name : plugin.Name);
                }
                return _logger;
            }
        }

        protected Configuration.ConfigFile Config
        {
            get { return SharedConfig; }
        }
    }
}

namespace UnderPressure.LoaderRuntime.Logging
{
    public sealed class ModLogSource
    {
        private static readonly object Sync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static bool _unityCaptureInstalled;
        [ThreadStatic] private static bool _writingUnityMessage;
        private readonly string _source;

        public ModLogSource(string source) { _source = source; }

        public void LogInfo(object value) { Write("INFO", value); }
        public void LogMessage(object value) { Write("INFO", value); }
        public void LogWarning(object value) { Write("WARN", value); }
        public void LogError(object value) { Write("ERROR", value); }
        public void LogDebug(object value) { Write("DEBUG", value); }
        public void LogFatal(object value) { Write("FATAL", value); }

        private static void EnsureUnityCapture()
        {
            lock (Sync)
            {
                if (_unityCaptureInstalled) return;
                // Unity exceptions previously appeared only in Player.log.
                Application.logMessageReceivedThreaded += RecordUnityError;
                _unityCaptureInstalled = true;
            }
        }

        private static void RecordUnityError(string condition, string stackTrace, LogType type)
        {
            if (_writingUnityMessage) return;
            // The mod already records its own INFO/WARN/ERROR messages above.
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            var line = "[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                "] [UNITY " + type.ToString().ToUpperInvariant() + "] " + condition;
            if (!string.IsNullOrEmpty(stackTrace)) line += Environment.NewLine + stackTrace;
            try { AppendLine(line); }
            catch { /* Logging must not interrupt Unity's exception handling. */ }
        }

        private static void AppendLine(string line)
        {
            var bytes = Utf8NoBom.GetBytes(line + Environment.NewLine);
            lock (Sync)
            {
                // Compatible with CreateFile(FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE)
                // in the native loader. Both writers append without holding the file open.
                using (var stream = new FileStream(
                    Path.Combine(UnderPressure.LoaderRuntime.Paths.PluginPath, "mods.log"),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
        }

        private void Write(string severity, object value)
        {
            var line = "[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                "] [" + severity + "] [" + _source + "] " + (value ?? "null");
            try
            {
                EnsureUnityCapture();
                AppendLine(line);
            }
            catch { /* Errors writing mods.log must not block Unity. */ }
            _writingUnityMessage = true;
            try
            {
                if (severity == "ERROR" || severity == "FATAL") Debug.LogError(line);
                else if (severity == "WARN") Debug.LogWarning(line);
                else Debug.Log(line);
            }
            finally { _writingUnityMessage = false; }
        }
    }
}

namespace UnderPressure.LoaderRuntime.Configuration
{
    public interface IAcceptableValue
    {
        object Clamp(object value);
    }

    public sealed class AcceptableValueRange<T> : IAcceptableValue where T : IComparable
    {
        public T MinValue { get; private set; }
        public T MaxValue { get; private set; }
        public AcceptableValueRange(T minValue, T maxValue)
        {
            if (minValue.CompareTo(maxValue) > 0)
                throw new ArgumentException("MinValue no puede superar MaxValue.");
            MinValue = minValue;
            MaxValue = maxValue;
        }
        public object Clamp(object raw)
        {
            var value = (T)raw;
            if (value.CompareTo(MinValue) < 0) return MinValue;
            if (value.CompareTo(MaxValue) > 0) return MaxValue;
            return value;
        }
    }

    public sealed class ConfigDescription
    {
        public string Description { get; private set; }
        public IAcceptableValue AcceptableValues { get; private set; }
        public ConfigDescription(string description, IAcceptableValue acceptableValues = null)
        {
            Description = description ?? "";
            AcceptableValues = acceptableValues;
        }
    }

    public sealed class ConfigEntry<T>
    {
        private T _value;
        private readonly ConfigFile _file;
        private readonly IAcceptableValue _acceptable;
        public string Section { get; private set; }
        public string Key { get; private set; }
        public ConfigDescription Description { get; private set; }
        public event EventHandler SettingChanged;

        internal ConfigEntry(ConfigFile file, string section, string key, T value, ConfigDescription description)
        {
            _file = file;
            Section = section;
            Key = key;
            Description = description;
            _acceptable = description == null ? null : description.AcceptableValues;
            _value = Validate(value);
        }

        private T Validate(T value)
        {
            return _acceptable == null ? value : (T)_acceptable.Clamp(value);
        }

        public T Value
        {
            get { return _value; }
            set
            {
                value = Validate(value);
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                if (_file.SaveOnConfigSet) _file.Save();
                var changed = SettingChanged;
                if (changed != null) changed(this, EventArgs.Empty);
            }
        }
        internal string Serialized
        {
            get { return Convert.ToString(_value, CultureInfo.InvariantCulture); }
        }
    }

    public sealed class ConfigFile
    {
        private sealed class Entry
        {
            public string Section;
            public string Key;
            public Func<string> Value;
            public string Description;
        }

        private readonly string _path;
        private readonly Dictionary<string, string> _loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entry> _entries = new List<Entry>();
        public bool SaveOnConfigSet { get; set; }

        public ConfigFile(string pluginId)
        {
            SaveOnConfigSet = true;
            _path = Path.Combine(UnderPressure.LoaderRuntime.Paths.ConfigPath, pluginId + ".cfg");
            // Leer la configuración antigua, si existe, sin volver a crearla.
            var legacyDirectory = Path.Combine(UnderPressure.LoaderRuntime.Paths.PluginPath, "config");
            if (Directory.Exists(legacyDirectory))
            {
                foreach (var legacyFile in Directory.GetFiles(legacyDirectory, "*.cfg"))
                    ReadValues(legacyFile);
            }
            // Los valores del archivo unificado tienen prioridad.
            ReadValues(_path);
        }

        private void ReadValues(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                string section = "";
                foreach (var row in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    var line = row.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    _loaded[section + "|" + line.Substring(0, separator).Trim()] =
                        line.Substring(separator + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Under Pressure: error leyendo configuracion " + filePath + ": " + ex.Message);
            }
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            T value = defaultValue;
            string raw;
            if (_loaded.TryGetValue(section + "|" + key, out raw))
            {
                try
                {
                    var type = typeof(T);
                    value = (T)(type.IsEnum ? Enum.Parse(type, raw, true) :
                        Convert.ChangeType(raw, type, CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Under Pressure: valor de configuracion invalido " + section + "/" + key + ": " + ex.Message);
                }
            }
            var entry = new ConfigEntry<T>(this, section, key, value, description);
            _entries.RemoveAll(existing => string.Equals(existing.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase));
            _entries.Add(new Entry
            {
                Section = section,
                Key = key,
                Value = () => entry.Serialized,
                Description = description == null ? "" : description.Description
            });
            return entry;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var output = new StringBuilder();
                string currentSection = null;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in _entries)
                {
                    if (currentSection != entry.Section)
                    {
                        if (output.Length > 0) output.AppendLine();
                        output.Append('[').Append(entry.Section).AppendLine("]");
                        currentSection = entry.Section;
                    }
                    if (!string.IsNullOrEmpty(entry.Description))
                        output.Append("# ").AppendLine(entry.Description.Replace('\r', ' ').Replace('\n', ' '));
                    var serialized = entry.Value();
                    output.Append(entry.Key).Append(" = ").AppendLine(serialized);
                    var id = entry.Section + "|" + entry.Key;
                    visited.Add(id);
                    _loaded[id] = serialized;
                }
                // No descartar opciones que aun no se han registrado en esta sesion.
                foreach (var item in _loaded)
                {
                    if (visited.Contains(item.Key)) continue;
                    var delimiter = item.Key.IndexOf('|');
                    if (delimiter < 0) continue;
                    string section = item.Key.Substring(0, delimiter);
                    string key = item.Key.Substring(delimiter + 1);
                    if (currentSection != section)
                    {
                        if (output.Length > 0) output.AppendLine();
                        output.Append('[').Append(section).AppendLine("]");
                        currentSection = section;
                    }
                    output.Append(key).Append(" = ").AppendLine(item.Value);
                }
                File.WriteAllText(_path, output.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Under Pressure: error guardando configuracion: " + ex.Message);
            }
        }
    }
}
