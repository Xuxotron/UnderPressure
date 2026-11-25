using System.Collections.Generic;
using TH20;

namespace UnderPressure
{
    // La planta pertenece a la parcela completa; sus objetos conservan X/Z.
    public static class StoreyMapRegistry
    {
        public const int GroundStorey = 0;
        public const int UpperStorey = 1;
        public const float StoreyHeight = 2f;

        private static readonly Dictionary<HospitalPlotDefinition, int> DefinitionStoreys =
            new Dictionary<HospitalPlotDefinition, int>();

        public static void SetStorey(HospitalPlotDefinition definition, int storey)
        {
            if (definition == null) return;
            DefinitionStoreys[definition] = storey == UpperStorey
                ? UpperStorey : GroundStorey;
        }

        public static int GetStorey(HospitalPlotDefinition definition)
        {
            if (definition == null) return GroundStorey;
            return DefinitionStoreys.TryGetValue(definition, out var storey)
                ? storey : GroundStorey;
        }

        public static int GetStorey(HospitalPlot plot) => GetStorey(plot?.Definition);

        public static void Clear() => DefinitionStoreys.Clear();
    }
}
