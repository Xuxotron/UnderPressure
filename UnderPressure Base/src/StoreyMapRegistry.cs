using System;
using System.Collections.Generic;
using TH20;

namespace UnderPressure
{
    // Puente de datos entre el Editor y Storey. Las definiciones conservan
    // coordenadas X/Z; la planta pertenece al mapa, nunca a sus objetos.
    public static class StoreyMapRegistry
    {
        public const int GroundStorey = 0;
        public const int UpperStorey = 1;
        public const float StoreyHeight = 2f;

        private static readonly Dictionary<HospitalPlotDefinition, int> DefinitionStoreys =
            new Dictionary<HospitalPlotDefinition, int>();
        private static Func<Level, IList<HospitalPlot>> _plotProvider;
        private static Func<HospitalPlot, WorldState> _worldProvider;
        private static Action<Level> _refreshHandler;
        private static Action<Level, HospitalPlot> _plotAddedHandler;
        private static Action<Level, HospitalPlot> _plotRemovedHandler;

        public static void SetStorey(HospitalPlotDefinition definition, int storey)
        {
            if (definition == null) return;
            DefinitionStoreys[definition] = Normalize(storey);
        }

        public static int GetStorey(HospitalPlotDefinition definition)
        {
            if (definition == null) return GroundStorey;
            return DefinitionStoreys.TryGetValue(definition, out var storey)
                ? storey : GroundStorey;
        }

        public static int GetStorey(HospitalPlot plot) =>
            GetStorey(plot?.Definition);

        public static IList<HospitalPlot> GetPlots(Level level)
        {
            var plots = _plotProvider?.Invoke(level);
            return plots ?? (IList<HospitalPlot>)level?.WorldState?.HospitalPlots ??
                new HospitalPlot[0];
        }

        public static WorldState GetWorld(Level level, HospitalPlot plot) =>
            _worldProvider?.Invoke(plot) ?? level?.WorldState;

        public static void Refresh(Level level) => _refreshHandler?.Invoke(level);

        public static void NotifyPlotAdded(Level level, HospitalPlot plot) =>
            _plotAddedHandler?.Invoke(level, plot);

        public static void NotifyPlotRemoved(Level level, HospitalPlot plot)
        {
            _plotRemovedHandler?.Invoke(level, plot);
            if (plot?.Definition != null) DefinitionStoreys.Remove(plot.Definition);
        }

        public static void RegisterRuntime(
            Func<Level, IList<HospitalPlot>> plotProvider,
            Func<HospitalPlot, WorldState> worldProvider,
            Action<Level> refreshHandler,
            Action<Level, HospitalPlot> plotAddedHandler,
            Action<Level, HospitalPlot> plotRemovedHandler)
        {
            _plotProvider = plotProvider;
            _worldProvider = worldProvider;
            _refreshHandler = refreshHandler;
            _plotAddedHandler = plotAddedHandler;
            _plotRemovedHandler = plotRemovedHandler;
        }

        public static void UnregisterRuntime()
        {
            _plotProvider = null;
            _worldProvider = null;
            _refreshHandler = null;
            _plotAddedHandler = null;
            _plotRemovedHandler = null;
        }

        public static void Clear()
        {
            DefinitionStoreys.Clear();
        }

        private static int Normalize(int storey) =>
            storey == UpperStorey ? UpperStorey : GroundStorey;
    }
}
