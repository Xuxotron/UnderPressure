// Updated: 2026-10-14 - Agrupacion estructural del Editor desde GitHub beb96a79.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using TH20;
using UnderPressure.LoaderRuntime.Configuration;
using UnderPressure.LoaderRuntime.Logging;
using UnderPressure.LoaderRuntime;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: UnderPressureEditorPlugin.cs =====
// Updated: 2026-10-12
// Inicia el editor de escenarios y guarda la opción Mostrar nuevos mapas.
namespace UnderPressure.Editor
{
    [ModPlugin(PluginGuid, PluginName, PluginVersion)]
    [ModDependency("artef.twopointhospital.underpressure", ModDependencyAttribute.DependencyFlags.HardDependency)]
    public sealed class UnderPressureEditorPlugin : BaseModComponent
    {
        public const string PluginGuid = "artef.twopointhospital.underpressure.editor";
        public const string PluginName = "Bajo Presión - Editor de escenarios";
        public const string PluginVersion = "0.1.1";

        internal static ModLogSource Log { get; private set; }
        internal static ConfigEntry<bool> ShowNewMapsSetting { get; private set; }
        internal static ConfigEntry<bool> FocusLevelsSetting { get; private set; }
        internal static ConfigEntry<string> ArchitectNameSetting { get; private set; }
        internal static bool ShowNewMaps => ShowNewMapsSetting == null || ShowNewMapsSetting.Value;
        internal static bool FocusLevels => FocusLevelsSetting != null && FocusLevelsSetting.Value;
        internal static string ArchitectName =>
            string.IsNullOrWhiteSpace(ArchitectNameSetting?.Value)
                ? "Xuxotron" : ArchitectNameSetting.Value.Trim();

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ShowNewMapsSetting = Config.Bind(
                EditorLocalization.Get("editor.settings.career"),
                EditorLocalization.Get("editor.settings.show_new_maps"), true,
                EditorLocalization.Get("editor.settings.show_new_maps.description"));
            FocusLevelsSetting = Config.Bind(
                EditorLocalization.Get("editor.settings.career"),
                EditorLocalization.Get("editor.settings.focus_levels"), false,
                EditorLocalization.Get("editor.settings.focus_levels.description"));
            ArchitectNameSetting = Config.Bind(
                EditorLocalization.Get("editor.settings.maps"),
                EditorLocalization.Get("editor.settings.architect"), "Xuxotron",
                EditorLocalization.Get("editor.settings.architect.description"));
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} cargado.");
        }

        // Los controles creados por el editor no usan Localize nativo.
        // Actualizarlos al cambiar de idioma, sin reconstruir sus menús.
        private void Update()
        {
            EditorLocalization.RefreshVisibleUiOnLanguageChange();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
// ===== END ORIGINAL: UnderPressureEditorPlugin.cs =====

// ===== BEGIN ORIGINAL: EditorLocalization.cs =====
// Carga las traducciones del editor sin depender del módulo principal.
namespace UnderPressure.Editor
{
    internal static class EditorLocalization
    {
        private const string FallbackLanguage = "en";
        private static readonly Dictionary<string, Dictionary<string, string>> Values =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly PropertyInfo CurrentLanguageCodeProperty = ResolveLanguageCodeProperty();
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

        internal static string Format(string key, params object[] arguments) =>
            string.Format(Get(key), arguments ?? new object[0]);

        private static string _lastUiLanguage;

        // Solo se refrescan las etiquetas propias del editor, sin alterar los
        // textos del juego traducidos por I2.Loc. Los nuevos CSV disponibles
        // contienen ingles y español; los otros idiomas usan el ingles.
        internal static void RefreshVisibleUiOnLanguageChange()
        {
            var language = CurrentLanguageCode();
            if (language != "es") language = FallbackLanguage;
            if (_lastUiLanguage == null)
            {
                _lastUiLanguage = language;
                return;
            }
            if (string.Equals(language, _lastUiLanguage, StringComparison.Ordinal)) return;
            var previous = _lastUiLanguage;
            _lastUiLanguage = language;
            EnsureLoaded();

            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Values)
            {
                var key = item.Key;
                if (!key.StartsWith("editor.", StringComparison.OrdinalIgnoreCase) &&
                    !key.StartsWith("scenario_editor.", StringComparison.OrdinalIgnoreCase) &&
                    !key.StartsWith("career.", StringComparison.OrdinalIgnoreCase) &&
                    !key.StartsWith("common.", StringComparison.OrdinalIgnoreCase)) continue;

                var values = item.Value;
                if (!values.TryGetValue(FallbackLanguage, out var english) ||
                    string.IsNullOrEmpty(english)) continue;
                if (!values.TryGetValue(previous, out var before) ||
                    string.IsNullOrEmpty(before)) before = english;
                if (!values.TryGetValue(language, out var after) ||
                    string.IsNullOrEmpty(after)) after = english;
                // Los mensajes con parámetros son transitorios; evitar cambiar
                // la plantilla del texto mientras muestra datos del usuario.
                if (before == after || before.Contains("{") || after.Contains("{")) continue;
                if (replacements.TryGetValue(before, out var already) && already != after)
                    ambiguous.Add(before);
                else
                    replacements[before] = after;
            }
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
            {
                if (text == null || !BelongsToEditor(text.transform)) continue;
                if (replacements.TryGetValue(text.text, out var value) &&
                    !ambiguous.Contains(text.text)) text.text = value;
            }
            foreach (var text in UnityEngine.Object.FindObjectsOfType<Text>())
            {
                if (text == null || !BelongsToEditor(text.transform)) continue;
                if (replacements.TryGetValue(text.text, out var value) &&
                    !ambiguous.Contains(text.text)) text.text = value;
            }
            ScenarioEditorStorage.RefreshLocalizedPlotLabels();
        }

        private static bool BelongsToEditor(Transform source)
        {
            if (source == null) return false;
            // Panel del editor nativo, usado por las herramientas de UnderPressure.
            if (ScenarioEditorSession.Active &&
                source.GetComponentInParent<EditHospitalMenu>() != null) return true;
            // Selectores y paneles propios pueden estar fuera del editor nativo.
            for (var node = source; node != null; node = node.parent)
            {
                var name = node.name;
                if (name.StartsWith("UP_", StringComparison.Ordinal) ||
                    name == "Show New Maps" ||
                    name.StartsWith("Under Pressure - Scenario", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static string CurrentLanguageCode()
        {
            try
            {
                var preferences = ScenarioEditorSession.Level?.App?.UserPreferences?.Language;
                if (preferences != null)
                {
                    var selected = TH20.Preferences.LanguagePreferences.GetLanguageCode(
                        preferences.SelectedLanguage);
                    return NormalizeLanguageCode(selected);
                }

                var code = CurrentLanguageCodeProperty?.GetValue(null, null) as string;
                return NormalizeLanguageCode(code);
            }
            catch
            {
                return FallbackLanguage;
            }
        }

        private static string NormalizeLanguageCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return FallbackLanguage;
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

        private static PropertyInfo ResolveLanguageCodeProperty()
        {
            var manager = AccessTools.TypeByName("I2.Loc.LocalizationManager");
            return manager == null ? null : AccessTools.Property(manager, "CurrentLanguageCode");
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(folder)) return;
            var paths = Directory.GetFiles(folder, "translations*.csv");
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
                LoadFile(path);
        }

        private static void LoadFile(string path)
        {
            var rows = File.ReadAllLines(path, Encoding.UTF8);
            if (rows.Length == 0) return;
            var headers = ParseCsvLine(rows[0]);
            for (var rowIndex = 1; rowIndex < rows.Length; ++rowIndex)
            {
                var columns = ParseCsvLine(rows[rowIndex]);
                if (columns.Count == 0 || string.IsNullOrWhiteSpace(columns[0])) continue;
                var key = columns[0].Trim();
                if (!Values.TryGetValue(key, out var translations))
                {
                    translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Values[key] = translations;
                }

                for (var columnIndex = 1;
                     columnIndex < headers.Count && columnIndex < columns.Count;
                     ++columnIndex)
                    translations[headers[columnIndex].Trim()] = columns[columnIndex];
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var value = new StringBuilder();
            var quoted = false;
            for (var index = 0; index < line.Length; ++index)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        value.Append('"');
                        ++index;
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
// ===== END ORIGINAL: EditorLocalization.cs =====
