// Updated: 2026-10-12
// Purpose: Implements electrical networks, capacity, consumption, contracts, batteries, and blackouts.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnderPressure.LoaderRuntime;
using FullInspector.Generated.SharedInstance;
using HarmonyLib;
using I2.Loc;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnderPressure.PowerGrid
{
    internal sealed partial class PowerGridSystem : MonoBehaviour
    {
        private const int ElectricityMode = 602;
        private const int DefaultContractedEnergy = 1000;
        private const int LegacyDefaultContractedEnergy = 2000;
        private const int NoPendingContractedEnergy = -1;
        private const decimal ContractedEnergyBaseRate = 2000m;
        private const decimal ContractedEnergyRateIncrease = 8000m;
        private const decimal ContractedEnergyRateIntervals = 9m;
        private static readonly decimal[] ContractedEnergySurcharges =
        {
            0m, 0m, 0.04m, 0.10m, 0.20m, 0.35m, 0.55m, 0.80m, 1.15m, 1.55m, 2m
        };
        private const int EnergyHundredths = 100;
        private const int BatteryMaximumHundredths = 200 * EnergyHundredths;
        private const int BlackoutDurationDays = 10;
        private const int DefaultCellCapacity = 800;
        private const float PowerTileSize = 1f;
        private const float PanelAnimationSpeed = 7f;
        private const int MaximumCellCableOutputs = 1;
        private const int MaximumPanelCableOutputs = 3;
        // The drawer is a sibling of the native HUD, whose parent is scaled at runtime.
        // These values produce the 95 px visible height from the supplied reference and
        // leave enough horizontal body for both complete text buttons.
        private const float ToolPanelHeight = 106f;
        private const float ToolPanelMinimumWidth = 148f;
        private const float ToolPanelOverlap = 20f;
        private static readonly FieldInfo ManagerModeField = AccessTools.Field(typeof(DataViewManager), "_mode");
        private static readonly FieldInfo LevelField = AccessTools.Field(typeof(DataViewManager), "_level");
        private static readonly Type RoomItemLayoutType = AccessTools.TypeByName("TH20.Level+RoomItemLayout");
        private static readonly MethodInfo BuildRoomMethod = AccessTools.Method(typeof(Level), "BuildRoom");
        private static readonly FieldInfo QueuePositionTextField =
            AccessTools.Field(typeof(StatusIconQueuePosition), "_queuePositionText");
        private static readonly FieldInfo StatusIconHudElementField =
            AccessTools.Field(typeof(StatusIcon), "_inWorldHUDElement");
        private static readonly LocalisedString EnergyText = global::UnderPressure.ModLocalization.Create("energy.label");
        private static readonly LocalisedString BatteryText = global::UnderPressure.ModLocalization.Create("energy.battery.name");
        private static readonly LocalisedString DailyText = global::UnderPressure.ModLocalization.Create("energy.daily");
        private static readonly LocalisedString TasksText = global::UnderPressure.ModLocalization.Create("energy.tasks");

        private readonly HashSet<PowerCoord> _cells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, GameObject> _cellVisuals = new Dictionary<PowerCoord, GameObject>();
        private readonly HashSet<PowerCoord> _lowVoltageCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, GameObject> _lowVoltageVisuals =
            new Dictionary<PowerCoord, GameObject>();
        private readonly HashSet<PowerCoord> _generatorCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, RoomItem> _powerCellsByConnector =
            new Dictionary<PowerCoord, RoomItem>();
        private readonly Dictionary<PowerCoord, PowerCoord> _highVoltageCellSource =
            new Dictionary<PowerCoord, PowerCoord>();
        private readonly Dictionary<PowerCoord, int> _cellConnectedLoad =
            new Dictionary<PowerCoord, int>();
        private readonly HashSet<PowerCoord> _overloadedCellCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, int> _tilePowerValue = new Dictionary<PowerCoord, int>();
        private readonly HashSet<PowerCoord> _panelCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, RoomItem> _panelsByCell =
            new Dictionary<PowerCoord, RoomItem>();
        private readonly HashSet<PowerCoord> _activePanelCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, PowerCoord> _panelHighVoltageSource =
            new Dictionary<PowerCoord, PowerCoord>();
        private readonly Dictionary<PowerCoord, int> _lowVoltagePowerValue =
            new Dictionary<PowerCoord, int>();
        private readonly Dictionary<PowerCoord, int> _lowVoltagePhysicalDistance =
            new Dictionary<PowerCoord, int>();
        private readonly Dictionary<PowerCoord, PowerCoord> _lowVoltagePanelSource =
            new Dictionary<PowerCoord, PowerCoord>();
        private readonly Dictionary<PowerCoord, int> _panelConnectedLoad =
            new Dictionary<PowerCoord, int>();
        private readonly HashSet<PowerCoord> _overloadedPanelCells = new HashSet<PowerCoord>();
        private readonly List<GameObject> _previewVisuals = new List<GameObject>();
        private readonly List<GameObject> _pathAreaVisuals = new List<GameObject>();
        private readonly List<GameObject> _flowVisuals = new List<GameObject>();
        private readonly Dictionary<int, ElectricityCostIndicator> _electricityCostIndicators =
            new Dictionary<int, ElectricityCostIndicator>();
        private readonly HashSet<PowerCoord> _hiddenDeletePreview = new HashSet<PowerCoord>();
        private readonly Dictionary<int, BatteryState> _batteryStates = new Dictionary<int, BatteryState>();
        private DataViewButtons _owner;
        private DataViewManager _manager;
        private Level _level;
        private static readonly Color PoweredColor = new Color(1f, 0.61f, 0.20f, 1f);
        private static readonly Color PreviewAddColor = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color HoverPreviewAddColor = new Color(1f, 0.76f, 0.30f, 0.5f);
        private static readonly Color LowVoltageColor = new Color(1f, 0.86f, 0.08f, 1f);
        private static readonly Color LowVoltagePreviewColor = new Color(1f, 0.94f, 0.30f, 1f);
        private static readonly Color LowVoltageHoverPreviewColor = new Color(1f, 0.94f, 0.30f, 0.5f);
        private static readonly Color LowVoltageDisconnectedColor = new Color(0.62f, 0.65f, 0.68f, 1f);
        private static readonly Color PanelCellColor = new Color(0.08f, 0.78f, 0.72f, 1f);
        private static readonly Color PanelOverloadColor = new Color(0.92f, 0.10f, 0.08f, 1f);
        private static readonly Color DisconnectedColor = new Color(0.32f, 0.35f, 0.38f, 1f);
        private static readonly Color UnpoweredObjectColor = new Color(0.27f, 0.29f, 0.32f, 1f);
        private GameObject _visualRoot;
        private GameObject _hoverPreview;
        private PowerCoord _hoverPreviewCoord;
        private ToolMode _hoverPreviewMode;
        private GameObject _generatorAreaVisual;
        private GameObject _panelAreaVisual;
        private RectTransform _toolPanel;
        private CanvasGroup _toolPanelCanvas;
        private DynamicButton _addButton;
        private DynamicButton _lowVoltageButton;
        private DynamicButton _deleteButton;
        private DynamicButton _flowButton;
        private Vector3 _panelClosedPosition;
        private Vector3 _panelOpenPosition;
        private float _panelOpenAmount;
        private bool _electricityViewActive;
        private bool _electricityGameplayStateKnown;
        private bool _electricityGameplayEnabled;
        private ToolMode _toolMode;
        private bool _showFlow;
        private bool _dragging;
        private PowerCoord _dragStart;
        private PowerCoord _dragEnd;
        private bool _initialised;
        private bool _networkDirty;
        private bool _energyStateInitialised;
        private int _contractedEnergy;
        private int _pendingContractedEnergy = NoPendingContractedEnergy;
        private int _contractActivationMonth = -1;
        private int _batteryEnergyHundredths;
        private int _energyCapacityHundredths;
        private int _lastDailyEnergy;
        private int _lastTaskEnergy;
        private int _currentDayTaskEnergy;
        private int _accruedDailyEnergyBill;
        private bool _gridOverloaded;
        private int _blackoutDaysRemaining;
        private RectTransform _energyHudRoot;
        private RectTransform _energyHudFill;
        private TMP_Text _energyHudFirstValue;
        private TMP_Text _energyHudSecondValue;
        private TooltipSpawner _energyHudTooltip;

        private sealed class ElectricityCostIndicator
        {
            internal GameObject Root;
            internal InWorldHUDElement HudElement;
            internal Component Text;
            internal Image[] Images;
            internal Color[] DefaultImageColors;
            internal RectTransform[] WidthRects;
            internal float[] DefaultWidths;
            internal LayoutElement Layout;
            internal float DefaultMinWidth;
            internal float DefaultPreferredWidth;
        }
        private bool _eventsSubscribed;
        private bool _restoringExtraState;
        private Camera _worldCamera;

        private enum ToolMode
        {
            None,
            HighVoltage,
            LowVoltage,
            Remove
        }

        internal static PowerGridSystem Active { get; private set; }
        internal static Level ActiveLevel => Active?._level;
        internal static void ReloadActiveExtraState()
        {
            var active = Active;
            if (active == null)
            {
                PowerGridPlugin.Log?.LogWarning(
                    "Recarga .upsav omitida: no hay un PowerGridSystem activo para el nivel actual.");
                return;
            }

            PowerGridPlugin.Log?.LogInfo("Forzando recarga en caliente del .upsav del nivel activo.");
            active.LoadExtraState();
            active.SetCommittedVisualsActive(active._electricityViewActive);
            active._networkDirty = true;
        }

        internal static void NotifyPanelMaintenanceChanged(RoomItem panel)
        {
            var active = Active;
            if (active == null || panel == null || !EnergyRoomItems.IsPanel(panel)) return;
            var panelCell = GetPanelCell(panel);
            var shouldBeActive = active._panelHighVoltageSource.ContainsKey(panelCell) &&
                                 panel.IsFunctional();
            if (active._activePanelCells.Contains(panelCell) != shouldBeActive)
                active._networkDirty = true;
        }

        internal static FloorPlan CurrentBuildingFloorPlan =>
            Active?._level?.BuildingLogic?.CurrentBlueprintFloorPlan;

        internal static bool IsCellPlacementValid(RoomItem item)
        {
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return true;
            var active = Active;
            return active == null || item == null || active.IsCellPlacementValidInternal(item);
        }

        private bool IsCellPlacementValidInternal(RoomItem item)
        {
            var connector = GetCellConnector(item);
            if (_cells.Contains(connector) || _lowVoltageCells.Contains(connector) ||
                _panelCells.Contains(connector)) return false;
            if (_powerCellsByConnector.TryGetValue(connector, out var existingCell) &&
                !ReferenceEquals(existingCell, item)) return false;
            if (!HasValidSourceConnections(connector, _cells, MaximumCellCableOutputs)) return false;

            foreach (var neighbour in CardinalNeighbours(connector))
            {
                if (!_highVoltageCellSource.TryGetValue(neighbour, out var source)) continue;
                if (!_powerCellsByConnector.TryGetValue(source, out var sourceCell) ||
                    !ReferenceEquals(sourceCell, item)) return false;
            }
            return true;
        }

        internal static bool IsPanelPlacementValid(RoomItem item)
        {
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return true;
            var active = Active;
            return active == null || item == null || active.IsPanelPlacementValidInternal(item);
        }

        private bool IsPanelPlacementValidInternal(RoomItem item)
        {
            var connector = GetPanelCell(item);
            if (_cells.Contains(connector) || _lowVoltageCells.Contains(connector) ||
                _generatorCells.Contains(connector)) return false;
            if (_panelsByCell.TryGetValue(connector, out var existingPanel) &&
                !ReferenceEquals(existingPanel, item)) return false;
            if (!HasValidSourceConnections(connector, _lowVoltageCells, MaximumPanelCableOutputs)) return false;

            foreach (var neighbour in CardinalNeighbours(connector))
            {
                if (!_lowVoltagePanelSource.TryGetValue(neighbour, out var source)) continue;
                if (!_panelsByCell.TryGetValue(source, out var sourcePanel) ||
                    !ReferenceEquals(sourcePanel, item)) return false;
            }
            return true;
        }

        internal static bool BlocksWorldSelection { get; private set; }

        internal void Initialise(DataViewButtons owner, DataViewManager manager)
        {
            _owner = owner;
            _manager = manager;
            _level = LevelField?.GetValue(manager) as Level;
            if (_level == null)
            {
                PowerGridPlugin.Log.LogWarning("No se pudo obtener el nivel para el prototipo electrico.");
                return;
            }
            Active = this;
            if (_visualRoot == null)
            {
                _visualRoot = new GameObject("UnderPressurePowerFloorRoot");
                _visualRoot.hideFlags = HideFlags.HideAndDontSave;
            }
            if (!_initialised)
            {
                _initialised = true;
                LoadExtraState();
                SubscribeBuildEvents();
                _networkDirty = true;
                LogDefinitions();
            }
        }

        private void RebuildNetwork()
        {
            _networkDirty = false;
            RebuildGeneratorCells();
            RebuildPanelCells();
            RefreshEnergyCapacity();

            // Sources, panels and both cable voltages are mutually exclusive per cell.
            var overlaps = new List<PowerCoord>();
            foreach (var coord in _cells)
                if (_generatorCells.Contains(coord) || _panelCells.Contains(coord)) overlaps.Add(coord);
            foreach (var coord in overlaps)
                RemoveCable(coord, _cells, _cellVisuals);
            overlaps.Clear();
            foreach (var coord in _lowVoltageCells)
                if (_generatorCells.Contains(coord) || _panelCells.Contains(coord) || _cells.Contains(coord))
                    overlaps.Add(coord);
            foreach (var coord in overlaps)
                RemoveCable(coord, _lowVoltageCells, _lowVoltageVisuals);

            _tilePowerValue.Clear();
            _highVoltageCellSource.Clear();
            foreach (var generator in _generatorCells) _tilePowerValue[generator] = 0;
            foreach (var cable in _cells) _tilePowerValue[cable] = -1;

            var generators = new List<PowerCoord>(_generatorCells);
            generators.Sort(ComparePowerCoords);
            foreach (var generator in generators)
            {
                var neighbours = new List<PowerCoord>(CardinalNeighbours(generator));
                neighbours.Sort(ComparePowerCoords);
                var alreadyClaimed = false;
                foreach (var neighbour in neighbours)
                    if (_cells.Contains(neighbour) && _highVoltageCellSource.ContainsKey(neighbour))
                    {
                        alreadyClaimed = true;
                        break;
                    }
                if (alreadyClaimed) continue;

                PowerCoord first = default(PowerCoord);
                var found = false;
                foreach (var neighbour in neighbours)
                    if (_cells.Contains(neighbour))
                    {
                        first = neighbour;
                        found = true;
                        break;
                    }
                if (!found) continue;

                var queue = new Queue<PowerCoord>();
                _tilePowerValue[first] = 1;
                _highVoltageCellSource[first] = generator;
                queue.Enqueue(first);
                while (queue.Count != 0)
                {
                    var current = queue.Dequeue();
                    var nextDistance = _tilePowerValue[current] + 1;
                    foreach (var neighbour in CardinalNeighbours(current))
                    {
                        if (!_cells.Contains(neighbour) || _highVoltageCellSource.ContainsKey(neighbour)) continue;
                        _tilePowerValue[neighbour] = nextDistance;
                        _highVoltageCellSource[neighbour] = generator;
                        queue.Enqueue(neighbour);
                    }
                }
            }

            _activePanelCells.Clear();
            _panelHighVoltageSource.Clear();
            foreach (var panel in _panelCells)
                foreach (var neighbour in CardinalNeighbours(panel))
                    if (_tilePowerValue.TryGetValue(neighbour, out var highDistance) && highDistance > 0 &&
                        _highVoltageCellSource.TryGetValue(neighbour, out var source))
                    {
                        _panelHighVoltageSource[panel] = source;
                        if (IsPanelFunctional(panel)) _activePanelCells.Add(panel);
                        break;
                    }

            RebuildLowVoltageTopology();
            RebuildPanelLoads();
            RebuildCellLoads();

            RefreshNetworkOutageVisuals();
            RebuildGeneratorVisual();
            RebuildPanelVisual();
            if (_showFlow) RebuildFlowVisuals();
            if (_electricityViewActive) RefreshElectricItemColors();
            PowerGridPlugin.Log.LogInfo($"Red recalculada: {_generatorCells.Count} celdas, " +
                                        $"{_overloadedCellCells.Count} sobrecargadas, " +
                                        $"{_activePanelCells.Count}/{_panelCells.Count} cuadros activos, " +
                                        $"{_overloadedPanelCells.Count} sobrecargados, " +
                                        $"{_cells.Count} Alto V. y {_lowVoltageCells.Count} Bajo V.");
        }

        private void RebuildLowVoltageTopology()
        {
            _lowVoltagePhysicalDistance.Clear();
            _lowVoltagePanelSource.Clear();
            _lowVoltagePowerValue.Clear();
            foreach (var cable in _lowVoltageCells)
            {
                _lowVoltagePhysicalDistance[cable] = -1;
                _lowVoltagePowerValue[cable] = -1;
            }

            var panels = new List<PowerCoord>(_panelCells);
            panels.Sort(ComparePowerCoords);
            var assignedPerPanel = new Dictionary<PowerCoord, int>();
            var queue = new Queue<PowerCoord>();
            foreach (var panel in panels)
            {
                assignedPerPanel[panel] = 0;
                foreach (var cable in CardinalNeighbours(panel))
                {
                    if (!_lowVoltageCells.Contains(cable) || _lowVoltagePanelSource.ContainsKey(cable) ||
                        assignedPerPanel[panel] >=
                        ElectricalPanelSpecialParameters.MaximumConnectedTiles) continue;
                    _lowVoltagePhysicalDistance[cable] = 1;
                    _lowVoltagePanelSource[cable] = panel;
                    assignedPerPanel[panel]++;
                    queue.Enqueue(cable);
                }
            }

            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                var nextDistance = _lowVoltagePhysicalDistance[current] + 1;
                var source = _lowVoltagePanelSource[current];
                foreach (var neighbour in CardinalNeighbours(current))
                {
                    if (!_lowVoltageCells.Contains(neighbour) || _lowVoltagePanelSource.ContainsKey(neighbour))
                        continue;
                    if (assignedPerPanel[source] >=
                        ElectricalPanelSpecialParameters.MaximumConnectedTiles) continue;
                    _lowVoltagePhysicalDistance[neighbour] = nextDistance;
                    _lowVoltagePanelSource[neighbour] = source;
                    assignedPerPanel[source]++;
                    queue.Enqueue(neighbour);
                }
            }

            foreach (var pair in _lowVoltagePhysicalDistance)
                if (pair.Value > 0 && _lowVoltagePanelSource.TryGetValue(pair.Key, out var panel) &&
                    _activePanelCells.Contains(panel))
                    _lowVoltagePowerValue[pair.Key] = pair.Value;
        }

        private void RebuildPanelLoads()
        {
            _panelConnectedLoad.Clear();
            _overloadedPanelCells.Clear();
            foreach (var panel in _panelCells) _panelConnectedLoad[panel] = 0;
            if (_level?.WorldState?.AllRooms == null) return;

            foreach (var room in _level.WorldState.AllRooms)
            {
                var items = room?.FloorPlan?.Items;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (item?.Definition == null || EnergyRoomItems.IsPanel(item) ||
                        !ElectricityGameplay.TryGetPowerType(item.Definition, out var power) ||
                        power != Power.Bajo ||
                        !ElectricityGameplay.TryGetDisplayCost(item, out var consumption, out _) ||
                        consumption <= 0 ||
                        !TryGetLowVoltagePanel(item, out var panel)) continue;
                    var previous = _panelConnectedLoad.TryGetValue(panel, out var load) ? load : 0;
                    _panelConnectedLoad[panel] = consumption > int.MaxValue - previous
                        ? int.MaxValue
                        : previous + consumption;
                }
            }

            foreach (var pair in _panelConnectedLoad)
                if (pair.Value > PanelCapacity(pair.Key)) _overloadedPanelCells.Add(pair.Key);
        }

        private void RebuildCellLoads()
        {
            _cellConnectedLoad.Clear();
            _overloadedCellCells.Clear();
            foreach (var cell in _generatorCells) _cellConnectedLoad[cell] = 0;

            foreach (var pair in _panelConnectedLoad)
            {
                if (!_panelHighVoltageSource.TryGetValue(pair.Key, out var source)) continue;
                AddCellLoad(source, pair.Value);
            }

            if (_level?.WorldState?.AllRooms != null)
                foreach (var room in _level.WorldState.AllRooms)
                {
                    var items = room?.FloorPlan?.Items;
                    if (items == null) continue;
                    foreach (var item in items)
                    {
                        if (item?.Definition == null || EnergyRoomItems.IsCell(item) ||
                            EnergyRoomItems.IsPanel(item) ||
                            !ElectricityGameplay.TryGetPowerType(item.Definition, out var power) ||
                            power != Power.Alto ||
                            !ElectricityGameplay.TryGetDisplayCost(item, out var consumption, out _) ||
                            consumption <= 0 || !TryGetHighVoltageCell(item, out var source)) continue;
                        AddCellLoad(source, consumption);
                    }
                }

            foreach (var pair in _cellConnectedLoad)
                if (pair.Value > CellCapacity(pair.Key)) _overloadedCellCells.Add(pair.Key);
        }

        private void AddCellLoad(PowerCoord source, int amount)
        {
            if (amount <= 0 || !_cellConnectedLoad.TryGetValue(source, out var previous)) return;
            _cellConnectedLoad[source] = amount > int.MaxValue - previous
                ? int.MaxValue
                : previous + amount;
        }

        private static int ComparePowerCoords(PowerCoord left, PowerCoord right)
        {
            var x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Y.CompareTo(right.Y);
        }

        private static int PanelCapacity(PowerCoord panel) =>
            ElectricalPanelSpecialParameters.MaximumLoad;

        private static int CellCapacity(PowerCoord cell) => DefaultCellCapacity;

        private void RefreshEnergyCapacity()
        {
            if (!_energyStateInitialised)
            {
                _contractedEnergy = DefaultContractedEnergy;
                _batteryEnergyHundredths = 0;
                _lastDailyEnergy = Mathf.Max(0,
                    ElectricityGameplay.GetDailyRecurringDemand(_level) + CountPoweredHighVoltageCables());
                _lastTaskEnergy = 0;
                _currentDayTaskEnergy = 0;
                _gridOverloaded = false;
                _energyStateInitialised = true;
            }

            ReconcileBatteryStates();
            RefreshBatteryEnergy();
            _energyCapacityHundredths = Math.Max(0, _contractedEnergy) * EnergyHundredths +
                                        _batteryEnergyHundredths;
            RefreshEnergyHud();
        }

        internal static void RecordTaskEnergy(FinanceManager manager, int amount)
        {
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return;
            var active = Active;
            if (active == null || amount <= 0 || !ReferenceEquals(active._level?.FinanceManager, manager)) return;
            active.ConsumeTaskEnergyImmediately(amount);
        }

        private void ConsumeTaskEnergyImmediately(int amount)
        {
            if (amount <= 0 || _blackoutDaysRemaining > 0) return;
            if (!_energyStateInitialised) RefreshEnergyCapacity();
            ReconcileBatteryStates();
            RefreshBatteryEnergy();
            var previousTaskEnergy = Math.Max(0, _currentDayTaskEnergy);
            _currentDayTaskEnergy = amount > int.MaxValue - previousTaskEnergy
                ? int.MaxValue
                : previousTaskEnergy + amount;
            _lastTaskEnergy = _currentDayTaskEnergy;
            var contractedHundredths = Math.Max(0, _contractedEnergy) * EnergyHundredths;
            var previousConsumptionHundredths =
                (Math.Max(0, _lastDailyEnergy) + previousTaskEnergy) * EnergyHundredths;
            var currentConsumptionHundredths =
                (Math.Max(0, _lastDailyEnergy) + _currentDayTaskEnergy) * EnergyHundredths;
            var previousBatteryDemand = Math.Max(0, previousConsumptionHundredths - contractedHundredths);
            var currentBatteryDemand = Math.Max(0, currentConsumptionHundredths - contractedHundredths);
            var additionalBatteryDemand = Math.Max(0, currentBatteryDemand - previousBatteryDemand);
            var batteryAvailable = _batteryEnergyHundredths;
            var availableBeforeConsumption = (long)contractedHundredths +
                                             previousBatteryDemand + batteryAvailable;
            DischargeBatteries(Math.Min(additionalBatteryDemand, batteryAvailable));
            RefreshBatteryEnergy();
            _energyCapacityHundredths = contractedHundredths + _batteryEnergyHundredths;
            var depleted = currentConsumptionHundredths > 0 &&
                           currentConsumptionHundredths >= availableBeforeConsumption;
            _gridOverloaded = depleted;
            if (depleted) StartBlackout();
            RefreshEnergyHud();
            RefreshElectricItemColors();
            RefreshNetworkOutageVisuals();
        }

        internal static void ConsumeDailyMonthlyEnergy(Level level)
        {
            var active = Active;
            if (active == null || level == null || !ReferenceEquals(active._level, level)) return;
            if (active.AdvanceBlackoutDay()) return;
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return;
            active.ApplyPendingContractIfDue();
            active.ReconcileBatteryStates();
            var recurringDailyEnergy = Math.Max(0, ElectricityGameplay.GetDailyRecurringDemand(level));
            active.AccumulateDailyEnergyBill(recurringDailyEnergy);
            active._gridOverloaded = false;
            var dailyEnergy = recurringDailyEnergy + active.CountPoweredHighVoltageCables();
            active._lastDailyEnergy = Math.Max(0, dailyEnergy);
            active._lastTaskEnergy = 0;
            active._currentDayTaskEnergy = 0;
            active.RechargeBatteries();
            active.RefreshBatteryEnergy();
            var availableHundredths = Math.Max(0, active._contractedEnergy) * EnergyHundredths +
                                      active._batteryEnergyHundredths;
            var dailyConsumption = active._lastDailyEnergy;
            var dailyConsumptionHundredths = dailyConsumption * EnergyHundredths;
            var contractedHundredths = Math.Max(0, active._contractedEnergy) * EnergyHundredths;
            var batteryDemandHundredths = Math.Max(0, dailyConsumptionHundredths - contractedHundredths);
            active.DischargeBatteries(Math.Min(batteryDemandHundredths, active._batteryEnergyHundredths));
            active.RefreshBatteryEnergy();
            active._energyCapacityHundredths = contractedHundredths + active._batteryEnergyHundredths;
            var depleted = dailyConsumptionHundredths > 0 &&
                           dailyConsumptionHundredths >= availableHundredths;
            active._gridOverloaded = depleted;
            if (depleted) active.StartBlackout();
            active.RefreshEnergyHud();
            active.RefreshElectricItemColors();
            active.RefreshNetworkOutageVisuals();
            if (!depleted)
                PowerGridPlugin.Log.LogInfo("Consumo electrico diario: " + dailyConsumption + "/" +
                                            FormatEnergy(availableHundredths) + " (Diario " + active._lastDailyEnergy +
                                            ", Tareas " + active._lastTaskEnergy + ").");
        }

        private void StartBlackout()
        {
            if (_blackoutDaysRemaining > 0) return;
            _blackoutDaysRemaining = BlackoutDurationDays;
            _gridOverloaded = true;
            PowerGridPlugin.Log.LogWarning("Red electrica agotada: comienza un apagon de " +
                                           BlackoutDurationDays + " dias.");
        }

        private bool AdvanceBlackoutDay()
        {
            if (_blackoutDaysRemaining <= 0) return false;
            _blackoutDaysRemaining--;
            if (_blackoutDaysRemaining > 0) return true;

            _gridOverloaded = false;
            _lastDailyEnergy = 0;
            _lastTaskEnergy = 0;
            _currentDayTaskEnergy = 0;
            ReconcileBatteryStates();
            RechargeBatteries();
            RefreshBatteryEnergy();
            _energyCapacityHundredths = Math.Max(0, _contractedEnergy) * EnergyHundredths +
                                        _batteryEnergyHundredths;
            RefreshEnergyHud();
            RefreshElectricItemColors();
            RefreshNetworkOutageVisuals();
            PowerGridPlugin.Log.LogWarning("Suministro electrico restablecido tras diez dias de apagon.");
            return true;
        }

        private void AccumulateDailyEnergyBill(int amount)
        {
            if (amount <= 0) return;
            _accruedDailyEnergyBill = amount > int.MaxValue - _accruedDailyEnergyBill
                ? int.MaxValue
                : _accruedDailyEnergyBill + amount;
        }

        internal static bool TryGetAccruedDailyEnergyBill(FinanceManager manager, out int amount)
        {
            amount = 0;
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return false;
            var active = Active;
            if (active == null || manager == null || !ReferenceEquals(active._level?.FinanceManager, manager))
                return false;
            amount = Math.Max(0, active._accruedDailyEnergyBill);
            return true;
        }

        internal static void ClearAccruedDailyEnergyBill(FinanceManager manager)
        {
            var active = Active;
            if (active == null || manager == null || !ReferenceEquals(active._level?.FinanceManager, manager))
                return;
            active._accruedDailyEnergyBill = 0;
        }

        internal static bool TryGetBatteryCharge(RoomItem item, out float progress, out int roundedMaximum)
        {
            progress = 0f;
            roundedMaximum = 0;
            var active = Active;
            if (active == null || item == null || !EnergyRoomItems.IsBattery(item) ||
                !ReferenceEquals(active._level, item.Level)) return false;

            if (!active._batteryStates.TryGetValue(item.ID, out var state))
            {
                active.ReconcileBatteryStates();
                if (!active._batteryStates.TryGetValue(item.ID, out state)) return false;
            }

            var maximum = Math.Max(0, state.MaximumHundredths);
            roundedMaximum = (maximum + EnergyHundredths - 1) / EnergyHundredths;
            progress = maximum == 0
                ? 0f
                : Mathf.Clamp01(state.ChargeHundredths / (float)maximum);
            return true;
        }

        private void ReconcileBatteryStates(IList<LoadedBatteryRecord> loaded = null)
        {
            var current = new List<RoomItem>();
            if (_level?.WorldState != null && EnergyRoomItems.Battery != null)
            {
                var items = _level.WorldState.GetRoomItemsOfType(EnergyRoomItems.Battery);
                if (items != null)
                    foreach (var item in items)
                        if (EnergyRoomItems.IsBattery(item) && !item.HasBeenDestroyed()) current.Add(item);
            }

            var reconciled = new Dictionary<int, BatteryState>();
            var usedLoaded = loaded == null ? null : new bool[loaded.Count];
            foreach (var item in current)
            {
                BatteryState state = null;
                if (loaded == null)
                    _batteryStates.TryGetValue(item.ID, out state);
                else
                {
                    var recordIndex = FindLoadedBattery(loaded, usedLoaded, item, true);
                    if (recordIndex < 0) recordIndex = FindLoadedBattery(loaded, usedLoaded, item, false);
                    if (recordIndex >= 0)
                    {
                        usedLoaded[recordIndex] = true;
                        var record = loaded[recordIndex];
                        var maximum = record.MaximumHundredths > 0
                            ? record.MaximumHundredths
                            : BatteryMaximumHundredths;
                        state = new BatteryState(item, maximum,
                            Mathf.Clamp(record.ChargeHundredths, 0, maximum));
                    }
                }

                if (state == null)
                    state = new BatteryState(item, BatteryMaximumHundredths, 0);
                else
                    state.Item = item;
                reconciled[item.ID] = state;
            }

            _batteryStates.Clear();
            foreach (var pair in reconciled) _batteryStates[pair.Key] = pair.Value;
        }

        private static int FindLoadedBattery(IList<LoadedBatteryRecord> loaded, bool[] used, RoomItem item,
            bool requireId)
        {
            var position = new Vector2(item.WorldPosition.x, item.WorldPosition.z);
            for (var index = 0; index < loaded.Count; ++index)
            {
                if (used[index]) continue;
                var record = loaded[index];
                if (requireId)
                {
                    if (record.ItemId == item.ID) return index;
                    continue;
                }
                if ((record.WorldPosition - position).sqrMagnitude <= 0.0025f) return index;
            }
            return -1;
        }

        private void RechargeBatteries()
        {
            foreach (var state in _batteryStates.Values)
            {
                var dailyRecharge = (state.MaximumHundredths + 50) / 100;
                state.ChargeHundredths = Math.Min(state.MaximumHundredths,
                    state.ChargeHundredths + dailyRecharge);
            }
        }

        private void DischargeBatteries(int requestedHundredths)
        {
            if (requestedHundredths <= 0 || _batteryStates.Count == 0) return;
            var states = new List<BatteryState>();
            long total = 0;
            foreach (var state in _batteryStates.Values)
                if (state.ChargeHundredths > 0)
                {
                    states.Add(state);
                    total += state.ChargeHundredths;
                }
            if (total <= 0) return;
            states.Sort((left, right) => left.Item.ID.CompareTo(right.Item.ID));
            var target = (int)Math.Min(requestedHundredths, total);
            var shares = new int[states.Count];
            var allocated = 0;
            for (var index = 0; index < states.Count; ++index)
            {
                shares[index] = (int)((long)target * states[index].ChargeHundredths / total);
                allocated += shares[index];
            }
            for (var index = 0; allocated < target && index < states.Count; ++index)
                if (shares[index] < states[index].ChargeHundredths)
                {
                    ++shares[index];
                    ++allocated;
                }
            for (var index = 0; index < states.Count; ++index)
                states[index].ChargeHundredths -= shares[index];
        }

        private void RefreshBatteryEnergy()
        {
            long total = 0;
            foreach (var state in _batteryStates.Values) total += Math.Max(0, state.ChargeHundredths);
            _batteryEnergyHundredths = total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private void RebuildGeneratorCells()
        {
            _generatorCells.Clear();
            _powerCellsByConnector.Clear();
            if (_level?.WorldState == null || EnergyRoomItems.Cell == null) return;
            var cells = _level.WorldState.GetRoomItemsOfType(EnergyRoomItems.Cell);
            if (cells == null) return;
            foreach (var item in cells)
            {
                if (!EnergyRoomItems.IsCell(item)) continue;
                var connector = GetCellConnector(item);
                _generatorCells.Add(connector);
                _powerCellsByConnector[connector] = item;
            }
        }

        private static PowerCoord GetCellConnector(RoomItem item)
        {
            // La fuente pertenece al tile realmente ocupado por el archivador clonado.
            // El desplazamiento frontal anterior colocaba la placa un tile lejos del modelo.
            return PowerCoord.FromWorldPosition(item.WorldCenter);
        }

        private void RebuildPanelCells()
        {
            _panelCells.Clear();
            _panelsByCell.Clear();
            if (_level?.WorldState == null || EnergyRoomItems.Panel == null) return;
            var panels = _level.WorldState.GetRoomItemsOfType(EnergyRoomItems.Panel);
            if (panels == null) return;
            foreach (var panel in panels)
            {
                if (!EnergyRoomItems.IsPanel(panel)) continue;
                var cell = GetPanelCell(panel);
                _panelCells.Add(cell);
                _panelsByCell[cell] = panel;
            }
        }

        private static PowerCoord GetPanelCell(RoomItem panel)
        {
            var facing = panel.GridRotation.DirectionVector();
            return PowerCoord.FromWorldPosition(panel.WorldPosition + facing * 0.5f);
        }

        internal static int ConnectedLowVoltageTiles(RoomItem panel)
        {
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return 0;
            var active = Active;
            if (active == null || panel == null || !EnergyRoomItems.IsPanel(panel)) return 0;
            var panelCell = GetPanelCell(panel);
            var overloaded = active._overloadedPanelCells.Contains(panelCell) ||
                             active.IsPanelFedByOverloadedCell(panelCell);
            if (!active.IsPanelFunctional(panelCell)) return 0;
            var powered = !active._gridOverloaded && active._activePanelCells.Contains(panelCell);
            // El gris de la vista eléctrica representa un cuadro sin alimentación.
            // Aunque tenga cableado de baja tensión asignado, en ese estado no se desgasta.
            if (!overloaded && !powered) return 0;
            var count = 0;
            foreach (var source in active._lowVoltagePanelSource.Values)
                if (source.Equals(panelCell)) count++;
            return count;
        }

        internal static bool IsPowered(RoomItem item)
        {
            if (!UnderPressurePlugin.ElectricityGameplayEnabled) return true;
            var active = Active;
            // Before the level data-view has created the grid controller, retain native
            // behaviour instead of temporarily disabling every electrical object.
            return active == null || item == null || active.IsItemPoweredInternal(item);
        }

        private bool IsItemPoweredInternal(RoomItem item)
        {
            if (_gridOverloaded) return false;
            if (EnergyRoomItems.IsPanel(item))
            {
                var panel = GetPanelCell(item);
                return IsPanelOperational(panel);
            }
            if (!ElectricityGameplay.TryGetPowerType(item.Definition, out var power)) return true;
            if (power == Power.Bajo)
                return TryGetLowVoltagePanel(item, out var panel) &&
                       IsPanelOperational(panel);
            return TryGetHighVoltageCell(item, out var source) && !_overloadedCellCells.Contains(source);
        }

        private bool IsPanelFunctional(PowerCoord panel) =>
            _panelsByCell.TryGetValue(panel, out var item) && item != null && item.IsFunctional();

        private bool IsPanelOperational(PowerCoord panel) =>
            !_gridOverloaded && IsPanelFunctional(panel) && _activePanelCells.Contains(panel) &&
            !_overloadedPanelCells.Contains(panel) && !IsPanelFedByOverloadedCell(panel);

        private bool TryGetLowVoltagePanel(RoomItem item, out PowerCoord panel)
        {
            panel = default(PowerCoord);
            if (item == null) return false;
            var center = PowerCoord.FromWorldPosition(item.WorldCenter);
            var found = false;
            var bestDistance = int.MaxValue;
            for (var offsetX = -1; offsetX <= 1; ++offsetX)
            for (var offsetY = -1; offsetY <= 1; ++offsetY)
            {
                if (offsetX == 0 && offsetY == 0) continue;
                var neighbour = new PowerCoord(center.X + offsetX, center.Y + offsetY);
                if (!_lowVoltagePanelSource.TryGetValue(neighbour, out var candidate) ||
                    !_lowVoltagePhysicalDistance.TryGetValue(neighbour, out var distance) || distance <= 0)
                    continue;
                if (found && (distance > bestDistance ||
                    (distance == bestDistance && ComparePowerCoords(candidate, panel) >= 0))) continue;
                panel = candidate;
                bestDistance = distance;
                found = true;
            }
            return found;
        }

        private bool TryGetHighVoltageCell(RoomItem item, out PowerCoord source)
        {
            source = default(PowerCoord);
            if (item == null) return false;
            var center = PowerCoord.FromWorldPosition(item.WorldCenter);
            var found = false;
            var bestDistance = int.MaxValue;
            for (var offsetX = -1; offsetX <= 1; ++offsetX)
            for (var offsetY = -1; offsetY <= 1; ++offsetY)
            {
                if (offsetX == 0 && offsetY == 0) continue;
                var neighbour = new PowerCoord(center.X + offsetX, center.Y + offsetY);
                if (!_highVoltageCellSource.TryGetValue(neighbour, out var candidate) ||
                    !_tilePowerValue.TryGetValue(neighbour, out var distance) || distance <= 0) continue;
                if (found && (distance > bestDistance ||
                    (distance == bestDistance && ComparePowerCoords(candidate, source) >= 0))) continue;
                source = candidate;
                bestDistance = distance;
                found = true;
            }
            return found;
        }

        private bool IsPanelFedByOverloadedCell(PowerCoord panel) =>
            _panelHighVoltageSource.TryGetValue(panel, out var source) &&
            _overloadedCellCells.Contains(source);

        private struct PowerCoord : IEquatable<PowerCoord>
        {
            internal readonly int X;
            internal readonly int Y;
            internal PowerCoord(int x, int y) { X = x; Y = y; }
            internal static PowerCoord FromWorldPosition(Vector3 position)
            {
                return new PowerCoord(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
            }
            internal Vector3 ToWorldPosition() { return new Vector3(X + 0.5f, 0f, Y + 0.5f); }
            public bool Equals(PowerCoord other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj) { return obj is PowerCoord other && Equals(other); }
            public override int GetHashCode() { return unchecked((X * 397) ^ Y); }
            public static bool operator ==(PowerCoord left, PowerCoord right) { return left.Equals(right); }
            public static bool operator !=(PowerCoord left, PowerCoord right) { return !left.Equals(right); }
        }
    }
}
