using System;
using I2.Loc;
using TH20;

namespace UnderPressure.PowerGrid
{
    internal static class EnergyLocalization
    {
        internal static LocalisedString Create(string key, string spanish, string english)
        {
            var value = LocalisedString.CreateNewTerm(key,
                global::UnderPressure.ModLocalization.Get(key, "en", english));
            try
            {
                if (LocalizationManager.Sources == null || LocalizationManager.Sources.Count == 0)
                    return value;
                var source = LocalizationManager.Sources[0];
                var term = source.GetTermData(key, false) ?? source.AddTerm(key, eTermType.Text, true);
                var codes = source.GetLanguagesCode(false, false);
                if (term?.Languages == null || codes == null) return value;
                for (var index = 0; index < term.Languages.Length && index < codes.Count; ++index)
                {
                    var code = codes[index] ?? string.Empty;
                    var fallback = code.StartsWith("es", StringComparison.OrdinalIgnoreCase)
                        ? spanish : english;
                    term.Languages[index] = global::UnderPressure.ModLocalization.Get(
                        key, code, fallback);
                }
                source.UpdateDictionary(false);
            }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogWarning("No se pudieron registrar las traducciones de " + key + ": " +
                                               exception.Message);
            }
            return value;
        }
    }
}
