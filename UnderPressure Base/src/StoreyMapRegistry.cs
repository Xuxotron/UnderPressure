using System.Collections.Generic;
using System;
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
        private static Func<Level, IList<HospitalPlot>> _plotProvider;
        private static Func<HospitalPlot, WorldState> _worldProvider;
        private static Action<Level> _refreshHandler;
        private static Action<Level> _activateGroundHandler;
        private static Action<Level, HospitalPlot> _plotAddedHandler;
        private static Action<Level, HospitalPlot> _plotRemovedHandler;

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

        public static IList<HospitalPlot> GetPlots(Level level)
        {
            var provided = _plotProvider?.Invoke(level);
            return provided ?? (IList<HospitalPlot>)level?.WorldState?.HospitalPlots ??
                new HospitalPlot[0];
        }

        public static WorldState GetWorld(Level level, HospitalPlot plot) =>
            _worldProvider?.Invoke(plot) ?? level?.WorldState;

        public static void Refresh(Level level) => _refreshHandler?.Invoke(level);

        public static void ActivateGround(Level level) => _activateGroundHandler?.Invoke(level);

        public static void NotifyPlotAdded(Level level, HospitalPlot plot) =>
            _plotAddedHandler?.Invoke(level, plot);

        public static void NotifyPlotRemoved(Level level, HospitalPlot plot)
        {
            _plotRemovedHandler?.Invoke(level, plot);
            if (plot?.Definition != null) DefinitionStoreys.Remove(plot.Definition);
        }

        public static void RegisterRuntime(Func<Level, IList<HospitalPlot>> plotProvider,
            Func<HospitalPlot, WorldState> worldProvider, Action<Level> refreshHandler,
            Action<Level> activateGroundHandler,
            Action<Level, HospitalPlot> plotAddedHandler,
            Action<Level, HospitalPlot> plotRemovedHandler)
        {
            _plotProvider = plotProvider;
            _worldProvider = worldProvider;
            _refreshHandler = refreshHandler;
            _activateGroundHandler = activateGroundHandler;
            _plotAddedHandler = plotAddedHandler;
            _plotRemovedHandler = plotRemovedHandler;
        }

        public static void UnregisterRuntime()
        {
            _plotProvider = null;
            _worldProvider = null;
            _refreshHandler = null;
            _activateGroundHandler = null;
            _plotAddedHandler = null;
            _plotRemovedHandler = null;
        }

        public static void Clear()
        {
            DefinitionStoreys.Clear();
            UnregisterRuntime();
        }
    }
}
