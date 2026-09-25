using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace UnderPressure
{
    internal static class ModLocalization
    {
        private const string FallbackLanguage = "en";
        private static readonly Dictionary<string, Dictionary<string, string>> Values =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        internal static string Get(string key)
        {
            EnsureLoaded();
            if (!Values.TryGetValue(key, out var translations))
                return key;

            var language = CurrentLanguageCode();
            if (translations.TryGetValue(language, out var value) && !string.IsNullOrEmpty(value))
                return value;
            if (translations.TryGetValue(FallbackLanguage, out value) && !string.IsNullOrEmpty(value))
                return value;
            return key;
        }

        private static string CurrentLanguageCode()
        {
            try
            {
                var manager = AccessTools.TypeByName("I2.Loc.LocalizationManager");
                var property = AccessTools.Property(manager, "CurrentLanguageCode");
                var code = property?.GetValue(null, null) as string;
                if (string.IsNullOrEmpty(code))
                    return FallbackLanguage;
                var normalized = code.Replace('_', '-').ToLowerInvariant();
                if (normalized.StartsWith("zh", StringComparison.Ordinal))
                {
                    if (normalized.Contains("tw") || normalized.Contains("hk") ||
                        normalized.Contains("mo") || normalized.Contains("hant") ||
                        normalized.Contains("traditional"))
                        return "zh-hant";
                    return "zh-hans";
                }
                var separator = normalized.IndexOf('-');
                return separator > 0 ? normalized.Substring(0, separator) : normalized;
            }
            catch
            {
                return FallbackLanguage;
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;

            var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var paths = Directory.GetFiles(folder ?? string.Empty, "translations*.csv");
            if (paths.Length == 0)
                return;
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
                LoadFile(path);
        }

        private static void LoadFile(string path)
        {
            var rows = File.ReadAllLines(path, Encoding.UTF8);
            if (rows.Length == 0)
                return;
            var headers = ParseCsvLine(rows[0]);
            for (var rowIndex = 1; rowIndex < rows.Length; ++rowIndex)
            {
                var columns = ParseCsvLine(rows[rowIndex]);
                if (columns.Count == 0 || string.IsNullOrWhiteSpace(columns[0]))
                    continue;
                var key = columns[0].Trim();
                if (!Values.TryGetValue(key, out var translations))
                {
                    translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Values[key] = translations;
                }
                for (var columnIndex = 1; columnIndex < headers.Count && columnIndex < columns.Count; ++columnIndex)
                    translations[headers[columnIndex].Trim()] = columns[columnIndex];
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var value = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; ++i)
            {
                var character = line[i];
                if (character == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        ++i;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (character == ',' && !quoted)
                {
                    result.Add(value.ToString());
                    value.Length = 0;
                }
                else
                {
                    value.Append(character);
                }
            }
            result.Add(value.ToString());
            return result;
        }
    }
}
