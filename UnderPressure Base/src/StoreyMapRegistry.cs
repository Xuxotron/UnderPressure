using System;
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
        private static Func<Level, IList<HospitalPlot>> _plotProvider;
        private static Func<HospitalPlot, WorldState> _worldProvider;
        private static Action<HospitalPlot> _toggleHandler;
        private static Action<Level> _refreshHandler;
        private static Action<Level> _activateGroundHandler;
        private static Func<Level, int> _activeStoreyProvider;
        private static Action<Level, int> _activateStoreyHandler;
        private static Action<Level, HospitalPlot> _plotAddedHandler;
        private static Action<Level, HospitalPlot> _plotRemovedHandler;

        public static event Action<Level, int> ActiveStoreyChanged;

        public static bool RuntimeAvailable => _activateStoreyHandler != null;

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

        public static IList<HospitalPlot> GetPlots(Level level) =>
            _plotProvider?.Invoke(level) ??
            (IList<HospitalPlot>)level?.WorldState?.HospitalPlots ?? new HospitalPlot[0];

        public static WorldState GetWorld(Level level, HospitalPlot plot) =>
            _worldProvider?.Invoke(plot) ?? level?.WorldState;

        public static void Toggle(HospitalPlot plot) => _toggleHandler?.Invoke(plot);
        public static void Refresh(Level level) => _refreshHandler?.Invoke(level);
        public static void ActivateGround(Level level) => _activateGroundHandler?.Invoke(level);
        public static int GetActiveStorey(Level level) =>
            _activeStoreyProvider?.Invoke(level) ?? GroundStorey;
        public static void ActivateStorey(Level level, int storey) =>
            _activateStoreyHandler?.Invoke(level, storey == UpperStorey
                ? UpperStorey : GroundStorey);
        public static void NotifyActiveStoreyChanged(Level level, int storey) =>
            ActiveStoreyChanged?.Invoke(level, storey == UpperStorey
                ? UpperStorey : GroundStorey);
        public static void NotifyPlotAdded(Level level, HospitalPlot plot) =>
            _plotAddedHandler?.Invoke(level, plot);
        public static void NotifyPlotRemoved(Level level, HospitalPlot plot)
        {
            _plotRemovedHandler?.Invoke(level, plot);
            if (plot?.Definition != null) DefinitionStoreys.Remove(plot.Definition);
        }

        public static void RegisterRuntime(Func<Level, IList<HospitalPlot>> plotProvider,
            Func<HospitalPlot, WorldState> worldProvider, Action<HospitalPlot> toggleHandler,
            Action<Level> refreshHandler, Action<Level> activateGroundHandler,
            Func<Level, int> activeStoreyProvider, Action<Level, int> activateStoreyHandler,
            Action<Level, HospitalPlot> plotAddedHandler,
            Action<Level, HospitalPlot> plotRemovedHandler)
        {
            _plotProvider = plotProvider;
            _worldProvider = worldProvider;
            _toggleHandler = toggleHandler;
            _refreshHandler = refreshHandler;
            _activateGroundHandler = activateGroundHandler;
            _activeStoreyProvider = activeStoreyProvider;
            _activateStoreyHandler = activateStoreyHandler;
            _plotAddedHandler = plotAddedHandler;
            _plotRemovedHandler = plotRemovedHandler;
        }

        public static void UnregisterRuntime()
        {
            _plotProvider = null;
            _worldProvider = null;
            _toggleHandler = null;
            _refreshHandler = null;
            _activateGroundHandler = null;
            _activeStoreyProvider = null;
            _activateStoreyHandler = null;
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
