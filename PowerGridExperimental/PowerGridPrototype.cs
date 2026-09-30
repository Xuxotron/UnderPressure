using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using FullInspector.Generated.SharedInstance;
using HarmonyLib;
using TMPro;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnderPressure.PowerGrid
{
    [HarmonyPatch(typeof(DataViewButtons), "Setup")]
    internal static class PowerGridDataViewSetupPatch
    {
        private static void Postfix(DataViewButtons __instance, DataViewManager __0)
        {
            var prototype = __instance.GetComponent<PowerGridPrototype>();
            if (prototype == null) prototype = __instance.gameObject.AddComponent<PowerGridPrototype>();
            prototype.Initialise(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(CursorSelect), "CursorUpdate")]
    internal static class PowerGridBlockWorldSelectionPatch
    {
        private static bool Prefix()
        {
            // The electrical plane owns the scene mouse while its data view is open.
            // This prevents the normal selection cursor from grabbing room items or people
            // underneath a cable drag.
            return !PowerGridPrototype.BlocksWorldSelection;
        }
    }

    internal sealed class PowerGridPrototype : MonoBehaviour
    {
        private const int ElectricityMode = 602;
        private const int DefaultContractedEnergy = 1000;
        private const int LegacyDefaultContractedEnergy = 2000;
        private const int EnergyHundredths = 100;
        private const int BatteryMaximumHundredths = 200 * EnergyHundredths;
        private const int LowVoltageMaximumLength = 5;
        private const float PowerTileSize = 1f;
        private const float PanelAnimationSpeed = 7f;
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
        private static readonly LocalisedString EnergyText = EnergyLocalization.Create("energy.label");
        private static readonly LocalisedString BatteryText = EnergyLocalization.Create("energy.battery.name");
        private static readonly LocalisedString DailyText = EnergyLocalization.Create("energy.daily");
        private static readonly LocalisedString TasksText = EnergyLocalization.Create("energy.tasks");

        private readonly HashSet<PowerCoord> _cells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, GameObject> _cellVisuals = new Dictionary<PowerCoord, GameObject>();
        private readonly HashSet<PowerCoord> _lowVoltageCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, GameObject> _lowVoltageVisuals =
            new Dictionary<PowerCoord, GameObject>();
        private readonly HashSet<PowerCoord> _generatorCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, int> _tilePowerValue = new Dictionary<PowerCoord, int>();
        private readonly HashSet<PowerCoord> _panelCells = new HashSet<PowerCoord>();
        private readonly HashSet<PowerCoord> _activePanelCells = new HashSet<PowerCoord>();
        private readonly Dictionary<PowerCoord, int> _lowVoltagePowerValue =
            new Dictionary<PowerCoord, int>();
        private readonly List<GameObject> _previewVisuals = new List<GameObject>();
        private readonly List<GameObject> _pathAreaVisuals = new List<GameObject>();
        private readonly List<GameObject> _flowVisuals = new List<GameObject>();
        private readonly List<ElectricityCostIndicator> _electricityCostIndicators =
            new List<ElectricityCostIndicator>();
        private readonly HashSet<PowerCoord> _hiddenDeletePreview = new HashSet<PowerCoord>();
        private readonly Dictionary<int, BatteryState> _batteryStates = new Dictionary<int, BatteryState>();
        private DataViewButtons _owner;
        private DataViewManager _manager;
        private Level _level;
        private static readonly Color PoweredColor = new Color(1f, 0.61f, 0.20f, 1f);
        private static readonly Color PreviewAddColor = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color LowVoltageColor = new Color(1f, 0.86f, 0.08f, 1f);
        private static readonly Color LowVoltagePreviewColor = new Color(1f, 0.94f, 0.30f, 1f);
        private static readonly Color LowVoltageDisconnectedColor = new Color(0.62f, 0.65f, 0.68f, 1f);
        private static readonly Color PanelCellColor = new Color(0.08f, 0.78f, 0.72f, 1f);
        private static readonly Color DisconnectedColor = new Color(0.32f, 0.35f, 0.38f, 1f);
        private static readonly Color GeneratorColor = new Color(0.58f, 1f, 0.08f, 1f);
        private static readonly Color UnpoweredObjectColor = new Color(0.27f, 0.29f, 0.32f, 1f);
        private GameObject _visualRoot;
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
        private ToolMode _toolMode;
        private bool _showFlow;
        private bool _dragging;
        private PowerCoord _dragStart;
        private PowerCoord _dragEnd;
        private bool _initialised;
        private bool _networkDirty;
        private bool _energyStateInitialised;
        private int _contractedEnergy;
        private int _batteryEnergyHundredths;
        private int _energyCapacityHundredths;
        private int _lastDailyEnergy;
        private int _lastTaskEnergy;
        private int _currentDayTaskEnergy;
        private float _dailyEnergyRemainder;
        private bool _gridOverloaded;
        private RectTransform _energyHudRoot;
        private RectTransform _energyHudFill;
        private TMP_Text _energyHudFirstValue;
        private TMP_Text _energyHudSecondValue;
        private TooltipSpawner _energyHudTooltip;

        private sealed class ElectricityCostIndicator
        {
            internal GameObject Root;
            internal InWorldHUDElement HudElement;
        }
        private bool _eventsSubscribed;
        private bool _restoringExtraState;

        private enum ToolMode
        {
            None,
            HighVoltage,
            LowVoltage,
            Remove
        }

        internal static PowerGridPrototype Active { get; private set; }

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

        private void Update()
        {
            if (_manager == null || _level == null || ManagerModeField == null) return;
            if (_networkDirty) RebuildNetwork();
            if (_toolPanel == null) TryCreateToolPanel();
            if (_energyHudRoot == null) TryCreateEnergyHud();
            RefreshEnergyHud();
            var active = Convert.ToInt32(ManagerModeField.GetValue(_manager)) == ElectricityMode;
            BlocksWorldSelection = active && _toolMode != ToolMode.None;
            if (active != _electricityViewActive)
            {
                _electricityViewActive = active;
                SetCommittedVisualsActive(active);
                if (_generatorAreaVisual != null) _generatorAreaVisual.SetActive(active);
                if (_panelAreaVisual != null) _panelAreaVisual.SetActive(active);
                SetFlowVisualsActive(active && _showFlow);
                if (active)
                {
                    if (_pathAreaVisuals.Count == 0) RefreshPathAreaVisuals();
                    else SetPathAreaVisualsActive(true);
                    RefreshElectricItemColors();
                }
                else
                {
                    SetPathAreaVisualsActive(false);
                    CancelPainting();
                }
            }
            AnimateToolPanel(active);
            if (!active) return;

            var input = _level.InputManager;
            if (input == null || input.IsMouseOverGuiOrDraggingScrollbar()) return;
            if (Input.GetKeyDown(KeyCode.Delete))
            {
                DeleteCableUnderCursor();
                return;
            }
            if (_toolMode == ToolMode.None) return;
            if (!_dragging && input.GetMouseDownOnScene((MouseButton)0))
            {
                _dragging = true;
                _dragStart = PowerCoord.FromWorldPosition(_level.CursorManager.WorldPosition);
                _dragEnd = _dragStart;
                RefreshPreview();
            }
            else if (_dragging && input.GetMouse((MouseButton)0))
            {
                var current = PowerCoord.FromWorldPosition(_level.CursorManager.WorldPosition);
                if (current != _dragEnd)
                {
                    _dragEnd = current;
                    RefreshPreview();
                }
            }
            if (_dragging && input.GetMouseUp((MouseButton)0)) CommitDrag();
            if (_dragging && input.GetMouseDown((MouseButton)1)) CancelDrag();
        }

        private void SelectTool(ToolMode mode)
        {
            if (_toolMode == mode)
            {
                CancelPainting();
                PowerGridPlugin.Log.LogInfo("Herramienta de cableado desactivada.");
                return;
            }
            _toolMode = mode;
            CancelDrag();
            RefreshToolButtonColors();
            PowerGridPlugin.Log.LogInfo("Herramienta electrica seleccionada: " + mode + ".");
        }

        private void ToggleFlowDisplay()
        {
            _showFlow = !_showFlow;
            if (_showFlow) RebuildFlowVisuals();
            else DestroyFlowVisuals();
            RefreshToolButtonColors();
            PowerGridPlugin.Log.LogInfo(_showFlow
                ? "Diagnostico de direccion y distancia electrica activado."
                : "Diagnostico de direccion y distancia electrica desactivado.");
        }

        private void CommitDrag()
        {
            var planned = BuildValidDrag();
            foreach (var coord in planned)
            {
                if (_toolMode == ToolMode.Remove)
                {
                    RemoveCable(coord, _cells, _cellVisuals);
                    RemoveCable(coord, _lowVoltageCells, _lowVoltageVisuals);
                    continue;
                }
                var lowVoltage = _toolMode == ToolMode.LowVoltage;
                var cells = lowVoltage ? _lowVoltageCells : _cells;
                var visuals = lowVoltage ? _lowVoltageVisuals : _cellVisuals;
                if (!cells.Add(coord)) continue;
                var visual = CreateTileVisual(coord, lowVoltage ? LowVoltageColor : PoweredColor,
                    lowVoltage ? "LowVoltageCell" : "HighVoltageCell");
                visual.SetActive(_electricityViewActive);
                visuals.Add(coord, visual);
            }
            // Deleted visuals have already disappeared during the drag. Do not restore
            // the entries which have just been removed from the electrical plane.
            _hiddenDeletePreview.Clear();
            CancelDrag();
            RebuildNetwork();
            PowerGridPlugin.Log.LogInfo($"Plano electrico: {_cells.Count} cables Alto V. y " +
                                        $"{_lowVoltageCells.Count} cables Bajo V.");
        }

        private static void RemoveCable(PowerCoord coord, HashSet<PowerCoord> cells,
            Dictionary<PowerCoord, GameObject> visuals)
        {
            if (!cells.Remove(coord)) return;
            if (visuals.TryGetValue(coord, out var oldVisual)) Object.Destroy(oldVisual);
            visuals.Remove(coord);
        }

        private void DeleteCableUnderCursor()
        {
            var coord = PowerCoord.FromWorldPosition(_level.CursorManager.WorldPosition);
            var existed = _cells.Contains(coord) || _lowVoltageCells.Contains(coord);
            if (!existed) return;
            CancelDrag();
            RemoveCable(coord, _cells, _cellVisuals);
            RemoveCable(coord, _lowVoltageCells, _lowVoltageVisuals);
            RebuildNetwork();
            PowerGridPlugin.Log.LogInfo("Cable electrico eliminado con Suprimir.");
        }

        private string GetExtraDataPath(int? saveSlotOverride = null, string levelIdOverride = null)
        {
            var saveSystem = _level?.App?.SaveSystem;
            var slot = saveSlotOverride ?? (saveSystem == null ? -1 : saveSystem.CurrentSaveSlot);
            var levelId = string.IsNullOrEmpty(levelIdOverride)
                ? (string.IsNullOrEmpty(_level?.UniqueID) ? "unknown-level" : _level.UniqueID)
                : levelIdOverride;
            foreach (var invalid in Path.GetInvalidFileNameChars()) levelId = levelId.Replace(invalid, '_');
            var directory = Path.Combine(Paths.ConfigPath, "UnderPressure", "Saves", "Slot" + (slot + 1));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, levelId + ".upsav");
        }

        private void LoadExtraState()
        {
            try
            {
                EnergyCampaignSystem.ResetLevel(_level?.UniqueID);
                global::UnderPressure.PermanentMachineWearSystem.BeginLoad(_level);
                var path = GetExtraDataPath();
                if (!File.Exists(path))
                {
                    global::UnderPressure.PermanentMachineWearSystem.CompleteLoad(_level);
                    return;
                }
                var lines = File.ReadAllLines(path);
                if (lines.Length == 0 || lines[0] != "UNDERPRESSURE_SAVE_1")
                {
                    PowerGridPlugin.Log.LogWarning("Formato de guardado complementario desconocido: " + path);
                    return;
                }
                var roomRecords = new Dictionary<int, ExtraRoomRecord>();
                var staffJobRecords = new Dictionary<int, bool>();
                var batteryRecords = new List<LoadedBatteryRecord>();
                for (var index = 1; index < lines.Length; ++index)
                {
                    var parts = lines[index].Split(',');
                    if (parts.Length == 3 && parts[0] == "C" && int.TryParse(parts[1], out var x) &&
                        int.TryParse(parts[2], out var y))
                    {
                        AddLoadedCable(new PowerCoord(x, y), false);
                        continue;
                    }
                    if (parts.Length == 3 && parts[0] == "L" && int.TryParse(parts[1], out x) &&
                        int.TryParse(parts[2], out y))
                    {
                        AddLoadedCable(new PowerCoord(x, y), true);
                        continue;
                    }
                    if (parts.Length == 9 && parts[0] == "R" &&
                        int.TryParse(parts[1], out var roomIndex) && int.TryParse(parts[2], out var plot) &&
                        int.TryParse(parts[3], out var anchorX) && int.TryParse(parts[4], out var anchorY) &&
                        int.TryParse(parts[5], out var width) && int.TryParse(parts[6], out var height))
                    {
                        roomRecords[roomIndex] = new ExtraRoomRecord(plot, anchorX, anchorY, width, height,
                            parts[7], parts[8]);
                        continue;
                    }
                    if (parts.Length == 9 && parts[0] == "I" &&
                        int.TryParse(parts[1], out var itemRoomIndex) && Guid.TryParse(parts[2], out var guid) &&
                        TryFloat(parts[3], out var localX) && TryFloat(parts[4], out var localY) &&
                        TryFloat(parts[5], out var localZ) && TryFloat(parts[6], out var rotation) &&
                        TryFloat(parts[7], out var maintenance) && roomRecords.TryGetValue(itemRoomIndex, out var roomRecord))
                        roomRecord.Items.Add(new ExtraItemRecord(guid, new Vector3(localX, localY, localZ), rotation,
                            maintenance, parts[8]));
                    if (parts.Length == 3 && parts[0] == "S" && int.TryParse(parts[1], out var staffId) &&
                        (parts[2] == "0" || parts[2] == "1"))
                        staffJobRecords[staffId] = parts[2] == "1";
                    if ((parts.Length == 3 || parts.Length == 4) && parts[0] == "E" && int.TryParse(parts[1], out var stored) &&
                        int.TryParse(parts[2], out var capacity))
                    {
                        // Legacy saves stored a depleting reserve. Preserve their former total
                        // as the first contracted service value and begin the new daily model cleanly.
                        _contractedEnergy = NormaliseContractedEnergy(capacity);
                        _batteryEnergyHundredths = 0;
                        _lastDailyEnergy = Mathf.Max(0,
                            Mathf.FloorToInt(ElectricityGameplay.GetMonthlyEnergyDemand(_level) / 30f));
                        _lastTaskEnergy = 0;
                        _currentDayTaskEnergy = 0;
                        _dailyEnergyRemainder = parts.Length == 4 && TryFloat(parts[3], out var remainder)
                            ? Mathf.Clamp(remainder, 0f, 0.999999f)
                            : 0f;
                        _gridOverloaded = false;
                        _energyStateInitialised = true;
                    }
                    if (parts.Length == 8 && parts[0] == "E2" &&
                        int.TryParse(parts[1], out var contracted) && int.TryParse(parts[2], out var batteries) &&
                        int.TryParse(parts[3], out var dailyEnergy) && int.TryParse(parts[4], out var taskEnergy) &&
                        int.TryParse(parts[5], out var currentTasks) && TryFloat(parts[6], out var dailyRemainder) &&
                        (parts[7] == "0" || parts[7] == "1"))
                    {
                        _contractedEnergy = NormaliseContractedEnergy(contracted);
                        _batteryEnergyHundredths = Math.Max(0, batteries) * EnergyHundredths;
                        _lastDailyEnergy = Math.Max(0, dailyEnergy);
                        _lastTaskEnergy = Math.Max(0, taskEnergy);
                        _currentDayTaskEnergy = Math.Max(0, currentTasks);
                        _dailyEnergyRemainder = Mathf.Clamp(dailyRemainder, 0f, 0.999999f);
                        _gridOverloaded = parts[7] == "1";
                        _energyStateInitialised = true;
                    }
                    if (parts.Length == 7 && parts[0] == "E3" &&
                        int.TryParse(parts[1], out contracted) && int.TryParse(parts[2], out dailyEnergy) &&
                        int.TryParse(parts[3], out taskEnergy) && int.TryParse(parts[4], out currentTasks) &&
                        TryFloat(parts[5], out dailyRemainder) && (parts[6] == "0" || parts[6] == "1"))
                    {
                        _contractedEnergy = NormaliseContractedEnergy(contracted);
                        _lastDailyEnergy = Math.Max(0, dailyEnergy);
                        _lastTaskEnergy = Math.Max(0, taskEnergy);
                        _currentDayTaskEnergy = Math.Max(0, currentTasks);
                        _dailyEnergyRemainder = Mathf.Clamp(dailyRemainder, 0f, 0.999999f);
                        _gridOverloaded = parts[6] == "1";
                        _energyStateInitialised = true;
                    }
                    if (parts.Length == 6 && parts[0] == "B" && int.TryParse(parts[1], out var batteryId) &&
                        int.TryParse(parts[2], out var maximumHundredths) &&
                        int.TryParse(parts[3], out var chargeHundredths) && TryFloat(parts[4], out var worldX) &&
                        TryFloat(parts[5], out var worldZ))
                        batteryRecords.Add(new LoadedBatteryRecord(batteryId, maximumHundredths,
                            chargeHundredths, new Vector2(worldX, worldZ)));
                    if (parts.Length == 3 && parts[0] == "W" && int.TryParse(parts[1], out var wearItemId) &&
                        TryFloat(parts[2], out var repairedPoints))
                        global::UnderPressure.PermanentMachineWearSystem.LoadRecord(
                            _level, wearItemId, repairedPoints);
                    if (parts.Length == 7 && parts[0] == "P" && int.TryParse(parts[1], out var campaignX) &&
                        int.TryParse(parts[2], out var campaignY) && int.TryParse(parts[3], out var activeCampaign) &&
                        TryFloat(parts[4], out var hackProgress) && TryFloat(parts[5], out var debugProgress) &&
                        TryFloat(parts[6], out var climateProgress))
                        EnergyCampaignSystem.Load(_level?.UniqueID, campaignX, campaignY, activeCampaign,
                            hackProgress, debugProgress, climateProgress);
                    if (parts.Length == 9 && parts[0] == "P" && int.TryParse(parts[1], out campaignX) &&
                        int.TryParse(parts[2], out campaignY) && int.TryParse(parts[3], out activeCampaign) &&
                        TryFloat(parts[4], out hackProgress) && TryFloat(parts[5], out debugProgress) &&
                        TryFloat(parts[6], out climateProgress) && int.TryParse(parts[7], out var durationMonths) &&
                        int.TryParse(parts[8], out var remainingDays))
                        EnergyCampaignSystem.Load(_level?.UniqueID, campaignX, campaignY, activeCampaign,
                            hackProgress, debugProgress, climateProgress, durationMonths, remainingDays);
                }
                RestoreEnergyRooms(roomRecords);
                ReconcileBatteryStates(batteryRecords);
                RestoreStaffJobAssignments(staffJobRecords);
                global::UnderPressure.PermanentMachineWearSystem.CompleteLoad(_level);
                PowerGridPlugin.Log.LogInfo($"Guardado complementario cargado: {_cells.Count} Alto V., " +
                                            $"{_lowVoltageCells.Count} Bajo V., " +
                                            $"{roomRecords.Count} salas y {staffJobRecords.Count} marcados ({path}).");
            }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogError("No se pudo cargar el fichero extra de la red electrica: " + exception);
            }
        }

        private void AddLoadedCable(PowerCoord coord, bool lowVoltage)
        {
            var cells = lowVoltage ? _lowVoltageCells : _cells;
            var visuals = lowVoltage ? _lowVoltageVisuals : _cellVisuals;
            if (!cells.Add(coord)) return;
            var visual = CreateTileVisual(coord, lowVoltage ? LowVoltageColor : PoweredColor,
                lowVoltage ? "LowVoltageCell" : "HighVoltageCell");
            visual.SetActive(false);
            visuals.Add(coord, visual);
        }

        internal void SaveExtraState(bool rotateBackups = false, int? saveSlotOverride = null,
            Level savedLevel = null, string levelIdOverride = null)
        {
            if (_restoringExtraState || _level?.WorldState == null) return;
            if (savedLevel != null && !ReferenceEquals(_level, savedLevel))
            {
                PowerGridPlugin.Log.LogWarning("Omitido .upsav: el nivel guardado no coincide con el nivel electrico activo.");
                return;
            }
            try
            {
                ReconcileBatteryStates();
                var path = GetExtraDataPath(saveSlotOverride, levelIdOverride);
                var ordered = new List<PowerCoord>(_cells);
                ordered.Sort((left, right) => left.X != right.X
                    ? left.X.CompareTo(right.X)
                    : left.Y.CompareTo(right.Y));
                var orderedLow = new List<PowerCoord>(_lowVoltageCells);
                orderedLow.Sort((left, right) => left.X != right.X
                    ? left.X.CompareTo(right.X)
                    : left.Y.CompareTo(right.Y));
                var lines = new List<string>(ordered.Count + orderedLow.Count + 32) { "UNDERPRESSURE_SAVE_1" };
                foreach (var coord in ordered) lines.Add("C," + coord.X + "," + coord.Y);
                foreach (var coord in orderedLow) lines.Add("L," + coord.X + "," + coord.Y);
                lines.Add("E3," + _contractedEnergy + "," + _lastDailyEnergy + "," + _lastTaskEnergy + "," +
                          _currentDayTaskEnergy + "," + F(_dailyEnergyRemainder) + "," +
                          (_gridOverloaded ? "1" : "0"));
                var orderedBatteries = new List<BatteryState>(_batteryStates.Values);
                orderedBatteries.Sort((left, right) => left.Item.ID.CompareTo(right.Item.ID));
                foreach (var battery in orderedBatteries)
                {
                    var position = battery.Item.WorldPosition;
                    lines.Add("B," + battery.Item.ID + "," + battery.MaximumHundredths + "," +
                              battery.ChargeHundredths + "," + F(position.x) + "," + F(position.z));
                }
                SaveEnergyRooms(lines);
                SaveStaffJobAssignments(lines);
                EnergyCampaignSystem.AppendSave(lines, _level);
                global::UnderPressure.PermanentMachineWearSystem.AppendSaveRecords(lines, _level);
                if (rotateBackups) RotateBackups(path);
                var temporary = path + ".tmp";
                File.WriteAllLines(temporary, lines.ToArray());
                File.Copy(temporary, path, true);
                File.Delete(temporary);
                PowerGridPlugin.Log.LogInfo($"Guardado complementario escrito: {_cells.Count} Alto V., " +
                                            $"{_lowVoltageCells.Count} Bajo V., salas, objetos " +
                                            $"y marcados de personal ({path}).");
            }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogError("No se pudo guardar el fichero extra de la red electrica: " + exception);
            }
        }

        private static void RotateBackups(string path)
        {
            // Match the native rolling layout: current, .2.bak ... .6.bak.
            var oldest = path + ".6.bak";
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var index = 5; index >= 2; --index)
            {
                var source = path + "." + index + ".bak";
                if (!File.Exists(source)) continue;
                File.Move(source, path + "." + (index + 1) + ".bak");
            }
            if (File.Exists(path)) File.Move(path, path + ".2.bak");
        }

        internal static void ApplyBackup(SaveSystem saveSystem, string levelId)
        {
            if (saveSystem == null || string.IsNullOrEmpty(levelId)) return;
            foreach (var invalid in Path.GetInvalidFileNameChars()) levelId = levelId.Replace(invalid, '_');
            var directory = Path.Combine(Paths.ConfigPath, "UnderPressure", "Saves",
                "Slot" + (saveSystem.CurrentSaveSlot + 1));
            var path = Path.Combine(directory, levelId + ".upsav");
            var firstBackup = path + ".2.bak";
            if (!File.Exists(firstBackup)) return;
            if (File.Exists(path)) File.Delete(path);
            File.Move(firstBackup, path);
            for (var index = 3; index <= 6; ++index)
            {
                var source = path + "." + index + ".bak";
                if (!File.Exists(source)) continue;
                File.Move(source, path + "." + (index - 1) + ".bak");
            }
            PowerGridPlugin.Log.LogInfo("Restaurada tambien la copia complementaria de UnderPressure: " + path);
        }

        private void SaveEnergyRooms(List<string> lines)
        {
            var roomIndex = 0;
            foreach (var room in _level.WorldState.AllRooms)
            {
                if (room == null || !PowerPlantRoomRegistry.IsPowerPlant(room.Definition) || room.FloorPlan == null)
                    continue;
                var plan = room.FloorPlan;
                var tiles = plan.Tiles;
                var plot = _level.WorldState.GetHospitalPlotFromRoom(room);
                var plotIndex = plot == null ? -1 : _level.WorldState.GetHospitalPlotIndex(plot);
                var bits = new char[tiles.Length];
                var bitIndex = 0;
                for (var x = 0; x < tiles.GetLength(0); ++x)
                for (var y = 0; y < tiles.GetLength(1); ++y)
                    bits[bitIndex++] = tiles[x, y] ? '1' : '0';
                lines.Add(string.Join(",", "R", roomIndex, plotIndex, plan.Anchor.X, plan.Anchor.Y,
                    tiles.GetLength(0), tiles.GetLength(1), new string(bits), "ENERGY"));
                foreach (var item in plan.Items)
                {
                    var definition = item?.Definition as RoomItemDefinition;
                    if (definition == null) continue;
                    var maintenance = item.MaintenanceLevel == null ? -1f : item.MaintenanceLevel.Value();
                    var position = item.LocalPosition;
                    lines.Add(string.Join(",", "I", roomIndex, definition.GUID.ToString("D"),
                        F(position.x), F(position.y), F(position.z), F(item.Rotation), F(maintenance),
                        definition.DebugTag ?? string.Empty));
                }
                ++roomIndex;
            }
        }

        private void SaveStaffJobAssignments(List<string> lines)
        {
            var staffMembers = _level?.CharacterManager?.StaffMembers;
            if (staffMembers == null) return;
            foreach (var staff in staffMembers)
            {
                if (staff == null || staff.Definition == null ||
                    staff.Definition._type != StaffDefinition.Type.Janitor) continue;
                var enabled = true;
                var exclusions = staff.JobExclusions;
                if (exclusions != null)
                    foreach (var exclusion in exclusions)
                        if (PowerPlantRoomRegistry.IsEnergyJobDescription(exclusion))
                        {
                            enabled = false;
                            break;
                        }
                lines.Add("S," + staff.ID + "," + (enabled ? "1" : "0"));
            }
        }

        private void RestoreStaffJobAssignments(Dictionary<int, bool> records)
        {
            if (records.Count == 0) return;
            var staffMembers = _level?.CharacterManager?.StaffMembers;
            if (staffMembers == null) return;
            foreach (var staff in staffMembers)
            {
                if (staff == null || !records.TryGetValue(staff.ID, out var enabled) || staff.JobExclusions == null)
                    continue;
                for (var index = staff.JobExclusions.Count - 1; index >= 0; --index)
                    if (PowerPlantRoomRegistry.IsEnergyJobDescription(staff.JobExclusions[index]))
                        staff.JobExclusions.RemoveAt(index);
                if (!enabled) staff.JobExclusions.Add(PowerPlantRoomRegistry.CreateEnergyJobDescription());
            }
        }

        private void RestoreEnergyRooms(Dictionary<int, ExtraRoomRecord> records)
        {
            if (records.Count == 0 || PowerPlantRoomRegistry.Definition == null) return;
            _restoringExtraState = true;
            try
            {
                foreach (var record in records.Values)
                {
                    if (EnergyRoomExists(record.AnchorX, record.AnchorY)) continue;
                    if (record.PlotIndex < 0 || record.PlotIndex >= _level.WorldState.HospitalPlots.Count) continue;
                    var hospitalMap = _level.WorldState.HospitalPlots[record.PlotIndex].HospitalMap;
                    if (hospitalMap == null) continue;
                    var tiles = record.CreateTiles();
                    if (tiles == null) continue;
                    if (RoomItemLayoutType == null || BuildRoomMethod == null) continue;
                    var wrappers = new List<SharedInstance_TH20TH20_RoomItemDefinition>();
                    var layoutListType = typeof(List<>).MakeGenericType(RoomItemLayoutType);
                    var layouts = (IList)Activator.CreateInstance(layoutListType);
                    foreach (var item in record.Items)
                    {
                        var wrapper = FindItemWrapper(item.Guid, item.DebugTag);
                        if (wrapper == null) continue;
                        wrappers.Add(wrapper);
                        var layout = Activator.CreateInstance(RoomItemLayoutType);
                        AccessTools.Field(RoomItemLayoutType, "ID").SetValue(layout, wrapper.ID);
                        AccessTools.Field(RoomItemLayoutType, "LocalPosition").SetValue(layout, item.LocalPosition);
                        AccessTools.Field(RoomItemLayoutType, "Rotation").SetValue(layout, item.Rotation);
                        layouts.Add(layout);
                    }
                    BuildRoomMethod.Invoke(_level, new object[]
                    {
                        PowerPlantRoomRegistry.Definition, hospitalMap,
                        new GridCoord(record.AnchorX, record.AnchorY), tiles, layouts, wrappers.ToArray(),
                        new List<RoomItemDefinitionUGC>()
                    });
                    ApplySavedMaintenance(record);
                }
                PowerGridPlugin.Log.LogInfo($"Salas de energia restauradas desde el fichero extra: {records.Count}.");
            }
            finally
            {
                _restoringExtraState = false;
            }
        }

        private bool EnergyRoomExists(int anchorX, int anchorY)
        {
            foreach (var room in _level.WorldState.AllRooms)
                if (room != null && PowerPlantRoomRegistry.IsPowerPlant(room.Definition) &&
                    room.FloorPlan != null && room.FloorPlan.Anchor.X == anchorX && room.FloorPlan.Anchor.Y == anchorY)
                    return true;
            return false;
        }

        private SharedInstance_TH20TH20_RoomItemDefinition FindItemWrapper(Guid guid, string debugTag)
        {
            var entries = _level.App?.Metagame?.RoomItemDatabase?.Instance?.RoomItems;
            if (entries == null) return null;
            foreach (var entry in entries)
            {
                var definition = entry?.Instance;
                if (definition == null || (definition.GUID != guid && definition.DebugTag != debugTag)) continue;
                return entry as SharedInstance_TH20TH20_RoomItemDefinition;
            }
            return null;
        }

        private void ApplySavedMaintenance(ExtraRoomRecord record)
        {
            Room restored = null;
            foreach (var room in _level.WorldState.AllRooms)
                if (room != null && PowerPlantRoomRegistry.IsPowerPlant(room.Definition) && room.FloorPlan != null &&
                    room.FloorPlan.Anchor.X == record.AnchorX && room.FloorPlan.Anchor.Y == record.AnchorY)
                {
                    restored = room;
                    break;
                }
            if (restored?.FloorPlan?.Items == null) return;
            foreach (var saved in record.Items)
            {
                if (saved.Maintenance < 0f) continue;
                foreach (var item in restored.FloorPlan.Items)
                {
                    var definition = item?.Definition as RoomItemDefinition;
                    if (definition == null || item.MaintenanceLevel == null || definition.GUID != saved.Guid ||
                        Vector3.Distance(item.LocalPosition, saved.LocalPosition) > 0.01f) continue;
                    item.MaintenanceLevel.SetValue(saved.Maintenance, true);
                    break;
                }
            }
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static bool TryFloat(string value, out float result) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

        private void RefreshPreview()
        {
            DestroyPreview();
            RestoreDeletePreview();
            foreach (var coord in BuildValidDrag())
            {
                if (_toolMode == ToolMode.Remove)
                {
                    if (_cellVisuals.TryGetValue(coord, out var visual) && visual != null)
                    {
                        visual.SetActive(false);
                        _hiddenDeletePreview.Add(coord);
                    }
                    if (_lowVoltageVisuals.TryGetValue(coord, out visual) && visual != null)
                    {
                        visual.SetActive(false);
                        _hiddenDeletePreview.Add(coord);
                    }
                    continue;
                }
                var lowVoltage = _toolMode == ToolMode.LowVoltage;
                _previewVisuals.Add(CreateTileVisual(coord,
                    lowVoltage ? LowVoltagePreviewColor : PreviewAddColor,
                    lowVoltage ? "LowVoltagePreview" : "HighVoltagePreview"));
            }
        }

        private List<PowerCoord> BuildValidDrag()
        {
            var result = new List<PowerCoord>();
            if (_toolMode == ToolMode.Remove)
            {
                foreach (var coord in EnumerateLine(_dragStart, _dragEnd))
                    if (_cells.Contains(coord) || _lowVoltageCells.Contains(coord)) result.Add(coord);
                return result;
            }

            var lowVoltage = _toolMode == ToolMode.LowVoltage;
            var existing = lowVoltage ? _lowVoltageCells : _cells;
            var simulated = new HashSet<PowerCoord>(existing);
            foreach (var coord in EnumerateLine(_dragStart, _dragEnd))
            {
                if (simulated.Contains(coord)) continue;
                if (!CanAddCable(coord, simulated, lowVoltage)) break;
                simulated.Add(coord);
                result.Add(coord);
            }
            return result;
        }

        private bool CanAddCable(PowerCoord coord, HashSet<PowerCoord> sameVoltage, bool lowVoltage)
        {
            if (!IsValidCell(coord) || _cells.Contains(coord) || _lowVoltageCells.Contains(coord)) return false;
            var neighbours = CardinalNeighbours(coord);
            var attachedNeighbours = new List<PowerCoord>(2);
            foreach (var neighbour in neighbours)
            {
                if (!sameVoltage.Contains(neighbour)) continue;
                attachedNeighbours.Add(neighbour);
            }

            if (attachedNeighbours.Count > 2)
                return false;
            if (attachedNeighbours.Count == 1 &&
                CountCardinalNeighbours(attachedNeighbours[0], sameVoltage) >= 2)
                return false;
            if (attachedNeighbours.Count == 2 &&
                !CanReconnectDisconnectedEnds(attachedNeighbours[0], attachedNeighbours[1], sameVoltage,
                    lowVoltage))
                return false;
            if (!lowVoltage) return true;

            var withCandidate = new HashSet<PowerCoord>(sameVoltage) { coord };
            var distance = DistanceFromActivePanel(coord, withCandidate);
            // A disconnected low-voltage layout may be prepared anywhere. Once it reaches
            // an active panel, the normal five-cell limit becomes mandatory.
            return distance < 0 || distance <= LowVoltageMaximumLength;
        }

        private bool CanReconnectDisconnectedEnds(PowerCoord first, PowerCoord second,
            HashSet<PowerCoord> sameVoltage, bool lowVoltage)
        {
            // A bridge may only consume two genuine ends. This keeps the no-branch rule.
            if (CountCardinalNeighbours(first, sameVoltage) > 1 ||
                CountCardinalNeighbours(second, sameVoltage) > 1)
                return false;

            // Joining two cells of the same component would close a loop.
            var visited = new HashSet<PowerCoord> { first };
            var queue = new Queue<PowerCoord>();
            queue.Enqueue(first);
            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbour in CardinalNeighbours(current))
                {
                    if (!sameVoltage.Contains(neighbour) || !visited.Add(neighbour)) continue;
                    if (neighbour.Equals(second)) return false;
                    queue.Enqueue(neighbour);
                }
            }

            // The exception exists specifically to repair an unpowered (grey) section.
            var values = lowVoltage ? _lowVoltagePowerValue : _tilePowerValue;
            return IsDisconnected(first, values) || IsDisconnected(second, values);
        }

        private static bool IsDisconnected(PowerCoord coord, Dictionary<PowerCoord, int> values) =>
            values.TryGetValue(coord, out var value) && value <= 0;

        private int DistanceFromActivePanel(PowerCoord start, HashSet<PowerCoord> lowCells)
        {
            var visited = new HashSet<PowerCoord> { start };
            var queue = new Queue<PowerCoord>();
            var distances = new Queue<int>();
            queue.Enqueue(start);
            distances.Enqueue(1);
            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                var distance = distances.Dequeue();
                foreach (var neighbour in CardinalNeighbours(current))
                {
                    if (_activePanelCells.Contains(neighbour)) return distance;
                    if (!lowCells.Contains(neighbour) || !visited.Add(neighbour)) continue;
                    queue.Enqueue(neighbour);
                    distances.Enqueue(distance + 1);
                }
            }
            return -1;
        }

        private static int CountCardinalNeighbours(PowerCoord coord, HashSet<PowerCoord> cells)
        {
            var count = 0;
            foreach (var neighbour in CardinalNeighbours(coord))
                if (cells.Contains(neighbour)) count++;
            return count;
        }

        private static PowerCoord[] CardinalNeighbours(PowerCoord coord) => new[]
        {
            new PowerCoord(coord.X - 1, coord.Y), new PowerCoord(coord.X + 1, coord.Y),
            new PowerCoord(coord.X, coord.Y - 1), new PowerCoord(coord.X, coord.Y + 1)
        };

        private static IEnumerable<PowerCoord> EnumerateLine(PowerCoord start, PowerCoord end)
        {
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            if (Math.Abs(deltaX) >= Math.Abs(deltaY))
            {
                var step = deltaX < 0 ? -1 : 1;
                for (var x = start.X;; x += step)
                {
                    yield return new PowerCoord(x, start.Y);
                    if (x == end.X) break;
                }
            }
            else
            {
                var step = deltaY < 0 ? -1 : 1;
                for (var y = start.Y;; y += step)
                {
                    yield return new PowerCoord(start.X, y);
                    if (y == end.Y) break;
                }
            }
        }

        private bool IsValidCell(PowerCoord coord)
        {
            // Generator floor is a source plane, never a cable plane. Connection is only
            // cardinally adjacent to the lime area.
            if (_generatorCells.Contains(coord) || _panelCells.Contains(coord)) return false;
            var roomCoord = GridCoord.WorldPositionToGridCoord(coord.ToWorldPosition());
            foreach (var map in _level.WorldState.HospitalMaps)
            {
                if (map == null || map.Plot == null || !map.Plot.Bought) continue;
                var validArea = map.IndoorOrPathState;
                if (validArea == null) continue;
                var localX = roomCoord.X - map.Anchor.X;
                var localY = roomCoord.Y - map.Anchor.Y;
                if (localX < 0 || localY < 0 || localX >= validArea.GetLength(0) ||
                    localY >= validArea.GetLength(1)) continue;
                // Every 1x1 quarter of an authored indoor/path cell is valid. Restricting
                // this to a centreline created alternating holes between native 2x2 cells.
                if (validArea[localX, localY]) return true;
            }
            return false;
        }

        private GameObject CreateTileVisual(PowerCoord coord, Color color, string objectName)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.name = objectName;
            tile.transform.SetParent(_visualRoot.transform, false);
            tile.transform.position = coord.ToWorldPosition() + new Vector3(0f, 0.06f, 0f);
            tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            tile.transform.localScale = Vector3.one * PowerTileSize * 0.90f;
            var collider = tile.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            var renderer = tile.GetComponent<Renderer>();
            // Use the game's own data-view material. Generic Unlit materials are rendered
            // white by the hospital desaturation pass.
            renderer.sharedMaterial = _manager.ValueMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
            return tile;
        }

        private void TryCreateToolPanel()
        {
            if (_owner == null) return;
            var electricityTransform = FindChildByName(_owner.transform, "Under Pressure Electricity View");
            var electricity = electricityTransform == null ? null : electricityTransform.GetComponent<DynamicButton>();
            if (electricity == null) return;
            var grid = electricity.transform.parent as RectTransform;
            var tab = grid?.parent as RectTransform;
            if (grid == null || tab == null) return;

            DynamicButton nativeTextButton;
            if (!TryFindNativeTextButton(out nativeTextButton)) return;

            var layout = grid.GetComponent<GridLayoutGroup>();
            var cellWidth = layout != null ? layout.cellSize.x : 42f;
            var cellHeight = layout != null ? layout.cellSize.y : 42f;
            var spacingY = layout != null ? layout.spacing.y : 3f;
            var gridHeight = cellHeight * 3f + spacingY * 2f;
            var nativeButtonRect = nativeTextButton.transform as RectTransform;
            var nativeSize = nativeButtonRect == null ? new Vector2(cellWidth * 2.4f, cellHeight) : nativeButtonRect.rect.size;
            var buttonWidth = nativeSize.x > 10f ? nativeSize.x : cellWidth * 2.4f;
            var buttonHeight = nativeSize.y > 10f ? nativeSize.y : cellHeight;
            var verticalMargin = 3f;
            var horizontalMargin = 9f;
            var gap = 3f;
            var maximumButtonHeight = (ToolPanelHeight - verticalMargin * 2f - gap * 2f) / 3f;
            var toolButtonScale = Mathf.Min(0.86f, maximumButtonHeight / buttonHeight);
            var visualButtonWidth = buttonWidth * toolButtonScale;
            var visualButtonHeight = buttonHeight * toolButtonScale;
            var visualIconWidth = cellWidth * toolButtonScale;
            var panelWidth = Mathf.Max(ToolPanelMinimumWidth,
                visualButtonWidth + visualIconWidth + gap + horizontalMargin * 2f);
            var panelHeight = ToolPanelHeight;
            var panelObject = new GameObject("Under Pressure Electricity Tools", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            _toolPanel = panelObject.GetComponent<RectTransform>();
            _toolPanel.SetParent(tab, false);
            _toolPanel.anchorMin = _toolPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _toolPanel.pivot = new Vector2(0.5f, 0.5f);
            _toolPanel.sizeDelta = new Vector2(panelWidth, panelHeight);

            var panelImage = panelObject.GetComponent<Image>();
            var backgroundTransform = tab.Find("Background") as RectTransform;
            var nativeBackground = backgroundTransform == null ? null : backgroundTransform.GetComponent<Image>();
            if (nativeBackground != null)
            {
                panelImage.sprite = nativeBackground.sprite;
                panelImage.material = nativeBackground.material;
                panelImage.type = nativeBackground.type;
                panelImage.color = nativeBackground.color;
            }
            else panelImage.color = new Color(0f, 0.72f, 0.79f, 0.98f);

            // The icon grid does not end at the visible edge of the enlarged HUD. Anchor
            // the drawer to the actual rendered background corners instead.
            var backgroundRight = grid.localPosition.x + grid.rect.xMax;
            var backgroundBottom = grid.localPosition.y + grid.rect.yMin;
            if (backgroundTransform != null)
            {
                var corners = new Vector3[4];
                backgroundTransform.GetWorldCorners(corners);
                backgroundRight = tab.InverseTransformPoint(corners[2]).x;
                backgroundBottom = tab.InverseTransformPoint(corners[0]).y;
            }
            // Reference measurement: the drawer and main HUD share the same bottom edge.
            // Its 96-unit height therefore grows upwards from this common baseline.
            var panelY = backgroundBottom + panelHeight * 0.5f;
            _panelClosedPosition = new Vector3(backgroundRight - panelWidth * 0.5f - ToolPanelOverlap,
                panelY, 0f);
            // The reference begins behind the final part of the native HUD, not after it.
            // Keep that shared origin fixed and grow the drawer only towards the right.
            _panelOpenPosition = new Vector3(backgroundRight + panelWidth * 0.5f - ToolPanelOverlap,
                panelY, 0f);
            _toolPanel.localPosition = _panelClosedPosition;
            _toolPanel.SetAsFirstSibling();
            _toolPanelCanvas = panelObject.GetComponent<CanvasGroup>();
            _toolPanelCanvas.interactable = false;
            _toolPanelCanvas.blocksRaycasts = false;

            // Keep the three text buttons tightly packed in one column. The diagnostic
            // toggle remains beside the high-voltage tool.
            var buttonX = -(visualIconWidth + gap) * 0.5f;
            var flowButtonX = (visualButtonWidth + gap) * 0.5f;
            var rowStep = visualButtonHeight + gap;
            var highCaption = EnergyLocalization.Create("energy.high_voltage").Translation;
            var lowCaption = EnergyLocalization.Create("energy.low_voltage").Translation;
            var deleteCaption = EnergyLocalization.Create("energy.delete").Translation;
            _addButton = CreateToolButton(nativeTextButton, panelObject.transform, "High Voltage Cable", highCaption,
                buttonX, rowStep, buttonWidth, buttonHeight, toolButtonScale, ToolMode.HighVoltage);
            _lowVoltageButton = CreateToolButton(nativeTextButton, panelObject.transform, "Low Voltage Cable", lowCaption,
                buttonX, 0f, buttonWidth, buttonHeight, toolButtonScale, ToolMode.LowVoltage);
            _deleteButton = CreateToolButton(nativeTextButton, panelObject.transform, "Delete Power Cable", deleteCaption,
                buttonX, -rowStep, buttonWidth, buttonHeight, toolButtonScale, ToolMode.Remove);
            _flowButton = CreateFlowButton(electricity, panelObject.transform, flowButtonX,
                rowStep, cellWidth, cellHeight, toolButtonScale);
            RefreshToolButtonColors();
        }

        private void TryCreateEnergyHud()
        {
            var menu = _level?.HUD?.FindMenu<TimeAndStatsMenu>(false);
            var menuRect = menu == null ? null : menu.transform as RectTransform;
            if (menuRect == null) return;

            var sprite = PowerGridPlugin.BatterySprite;
            if (sprite == null)
            {
                PowerGridPlugin.Log.LogWarning("No se encontro el sprite bateria para el HUD electrico.");
                return;
            }

            var root = new GameObject("UnderPressure Stored Energy", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            root.layer = menu.gameObject.layer;
            _energyHudRoot = root.GetComponent<RectTransform>();
            _energyHudRoot.SetParent(menuRect, false);
            _energyHudRoot.anchorMin = _energyHudRoot.anchorMax = new Vector2(0f, 0.5f);
            _energyHudRoot.pivot = new Vector2(1f, 0.5f);
            _energyHudRoot.anchoredPosition = new Vector2(-6f, -4f);
            _energyHudRoot.sizeDelta = new Vector2(156f, 92f);
            var hitArea = root.GetComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;

            var fillObject = new GameObject("Battery Charge Fill", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            _energyHudFill = fillObject.GetComponent<RectTransform>();
            _energyHudFill.SetParent(_energyHudRoot, false);
            _energyHudFill.anchorMin = _energyHudFill.anchorMax = new Vector2(0.5f, 0.5f);
            _energyHudFill.pivot = new Vector2(0f, 0.5f);
            _energyHudFill.anchoredPosition = new Vector2(-65f, -10f);
            _energyHudFill.sizeDelta = new Vector2(0f, 48f);
            var fillImage = fillObject.GetComponent<Image>();
            fillImage.color = new Color(0.86f, 0.08f, 0.07f, 0.95f);
            fillImage.raycastTarget = false;

            var iconObject = new GameObject("Battery Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(_energyHudRoot, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(0f, -12f);
            iconRect.sizeDelta = new Vector2(146f, 65f);
            var icon = iconObject.GetComponent<Image>();
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.color = Color.white;
            icon.raycastTarget = false;

            var textTemplate = menu.GetComponentInChildren<TMP_Text>(true);
            _energyHudFirstValue = CreateEnergyHudText(_energyHudRoot, textTemplate, "Battery First Value",
                new Vector2(-39f, 31f), new Vector2(62f, 25f), "0");
            CreateEnergyHudText(_energyHudRoot, textTemplate, "Battery Value Separator",
                new Vector2(0f, 31f), new Vector2(18f, 25f), "/");
            _energyHudSecondValue = CreateEnergyHudText(_energyHudRoot, textTemplate, "Battery Second Value",
                new Vector2(39f, 31f), new Vector2(62f, 25f), "0");

            var genericTooltip = AccessTools.Field(typeof(TimeAndStatsMenu), "_yearTooltipSpawner")
                ?.GetValue(menu) as TooltipSpawner;
            _energyHudTooltip = root.AddComponent<TooltipSpawner>();
            if (genericTooltip != null)
            {
                _energyHudTooltip.HoverTime = genericTooltip.HoverTime;
                _energyHudTooltip.Prefab = genericTooltip.Prefab;
            }
            _energyHudTooltip.AnchorToMouse = true;
            _energyHudTooltip.AnchorOffset = new Vector3(18f, 18f, 0f);
            _energyHudTooltip.SetDataProvider(tooltip =>
            {
                tooltip.Text = "<color=#202020>" + EnergyText.Translation + ": " +
                                _contractedEnergy.ToString(CultureInfo.InvariantCulture) + "</color>" +
                                "\n<color=#E88124>" + BatteryText.Translation + ": " +
                                FormatEnergy(_batteryEnergyHundredths) + "</color>" +
                               "\n<color=#258DB8>" + DailyText.Translation + ": " +
                               DisplayedDailyEnergy().ToString(CultureInfo.InvariantCulture) + "</color>" +
                               "\n<color=#E04898>" + TasksText.Translation + ": " +
                               _lastTaskEnergy.ToString(CultureInfo.InvariantCulture) + "</color>";
            });
            RefreshEnergyHud();
        }

        private static TMP_Text CreateEnergyHudText(Transform parent, TMP_Text template, string name,
            Vector2 position, Vector2 size, string initialText)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = textObject.AddComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
            }
            text.text = initialText;
            text.fontSize = 20f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.outlineColor = new Color32(35, 50, 38, 255);
            text.outlineWidth = 0.18f;
            return text;
        }

        private void RefreshEnergyHud()
        {
            if (_energyHudRoot == null || _energyHudFill == null) return;
            var dailyConsumption = DisplayedDailyEnergy() + _lastTaskEnergy;
            var ratio = _energyCapacityHundredths <= 0
                ? 0f
                : Mathf.Clamp01(1f - dailyConsumption * EnergyHundredths /
                    (float)_energyCapacityHundredths);
            _energyHudFill.sizeDelta = new Vector2(120f * ratio, 48f);
            if (_energyHudFirstValue != null)
                _energyHudFirstValue.text = FormatEnergy(_energyCapacityHundredths);
            if (_energyHudSecondValue != null)
                _energyHudSecondValue.text = dailyConsumption.ToString(CultureInfo.InvariantCulture);
            var image = _energyHudFill.GetComponent<Image>();
            if (image == null) return;
            if (ratio < 0.33f)
                image.color = Color.Lerp(new Color(0.86f, 0.08f, 0.07f, 0.95f),
                    new Color(1f, 0.45f, 0.04f, 0.95f), ratio / 0.33f);
            else if (ratio < 0.66f)
                image.color = Color.Lerp(new Color(1f, 0.45f, 0.04f, 0.95f),
                    new Color(1f, 0.86f, 0.04f, 0.95f), (ratio - 0.33f) / 0.33f);
            else
                image.color = Color.Lerp(new Color(1f, 0.86f, 0.04f, 0.95f),
                    new Color(0.20f, 0.92f, 0.23f, 0.95f), (ratio - 0.66f) / 0.34f);
            if (_energyHudSecondValue != null) _energyHudSecondValue.color = image.color;
        }

        private int DisplayedDailyEnergy()
        {
            if (_energyStateInitialised) return Math.Max(0, _lastDailyEnergy);
            return Mathf.Max(0, Mathf.FloorToInt(ElectricityGameplay.GetMonthlyEnergyDemand(_level) / 30f) +
                CountPoweredHighVoltageCables());
        }

        private int CountPoweredHighVoltageCables()
        {
            if (_gridOverloaded) return 0;
            var powered = 0;
            foreach (var coord in _cells)
                if (_tilePowerValue.TryGetValue(coord, out var distance) && distance > 0)
                    ++powered;
            return powered;
        }

        private static int NormaliseContractedEnergy(int stored)
        {
            // Contract changes are not exposed yet, so 2000 can only be the former default.
            return stored == LegacyDefaultContractedEnergy
                ? DefaultContractedEnergy
                : Math.Max(0, stored);
        }

        private static string FormatEnergy(int hundredths)
        {
            return (Math.Max(0, hundredths) / (decimal)EnergyHundredths)
                .ToString("0.##", CultureInfo.InvariantCulture);
        }

        private DynamicButton CreateToolButton(DynamicButton template, Transform parent, string name,
            string caption, float x, float y, float width, float height, float scale, ToolMode mode)
        {
            var button = Instantiate(template, parent);
            button.name = name;
            button.gameObject.SetActive(true);
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => SelectTool(mode));
            button.onSecondaryDown.RemoveAllListeners();
            button.interactable = true;
            button.SetTMPText(caption);
            var tooltip = button.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null) tooltip.enabled = false;

            var rect = button.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            // Scale the complete native hierarchy uniformly; do not resize individual
            // sprites or text, which would deform the original button design.
            rect.localScale = Vector3.one * scale;
            return button;
        }

        private DynamicButton CreateFlowButton(DynamicButton template, Transform parent, float x, float y,
            float width, float height, float scale)
        {
            var button = Instantiate(template, parent);
            button.name = "Power Flow Diagnostic";
            button.gameObject.SetActive(true);
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(ToggleFlowDisplay);
            button.onSecondaryDown.RemoveAllListeners();
            button.interactable = true;
            var tooltip = button.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null) tooltip.enabled = false;
            ReplaceButtonIcon(button, "status_can't_navigate", "status_cant_navigate");

            var rect = button.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * scale;
            return button;
        }

        private static void ReplaceButtonIcon(DynamicButton button, params string[] spriteNames)
        {
            Sprite sprite = null;
            foreach (var spriteName in spriteNames)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (candidate != null && candidate.name.Equals(spriteName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sprite = candidate;
                        break;
                    }
                if (sprite != null) break;
            }
            if (sprite == null)
            {
                PowerGridPlugin.Log.LogWarning("No se encontro el icono status_can't_navigate.");
                return;
            }

            var targetGraphic = button.targetGraphic;
            Image best = null;
            var bestArea = float.MaxValue;
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                if (image == targetGraphic || image.sprite == null) continue;
                var size = image.rectTransform.rect.size;
                var area = Mathf.Abs(size.x * size.y);
                if (area <= 1f || area >= bestArea) continue;
                bestArea = area;
                best = image;
            }
            if (best != null) best.sprite = sprite;
        }

        private static bool TryFindNativeTextButton(out DynamicButton button)
        {
            button = null;
            foreach (var hubButtons in Resources.FindObjectsOfTypeAll<HubMenuButtons>())
            {
                if (hubButtons == null || hubButtons.RoomsButtonAnimator == null) continue;
                button = hubButtons.RoomsButtonAnimator.Button;
                if (button == null) continue;
                return true;
            }
            return false;
        }

        private void AnimateToolPanel(bool open)
        {
            if (_toolPanel == null) return;
            _panelOpenAmount = Mathf.MoveTowards(_panelOpenAmount, open ? 1f : 0f,
                Time.unscaledDeltaTime * PanelAnimationSpeed);
            var eased = _panelOpenAmount * _panelOpenAmount * (3f - 2f * _panelOpenAmount);
            _toolPanel.localPosition = Vector3.Lerp(_panelClosedPosition, _panelOpenPosition, eased);
            if (_toolPanelCanvas != null)
            {
                var usable = open && _panelOpenAmount > 0.92f;
                _toolPanelCanvas.interactable = usable;
                _toolPanelCanvas.blocksRaycasts = usable;
            }
        }

        private void RefreshToolButtonColors()
        {
            SetToolButtonState(_addButton, _toolMode == ToolMode.HighVoltage);
            SetToolButtonState(_lowVoltageButton, _toolMode == ToolMode.LowVoltage);
            SetToolButtonState(_deleteButton, _toolMode == ToolMode.Remove);
            SetToolButtonState(_flowButton, _showFlow);
        }

        private static void SetToolButtonState(DynamicButton button, bool selected)
        {
            if (button == null) return;
            var animator = button.GetComponent<ButtonAnimator>();
            if (animator != null)
                animator.CurrentState = selected ? ButtonAnimator.State.Selected : ButtonAnimator.State.Selectable;
        }

        private static Transform FindChildByName(Transform root, string wantedName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == wantedName) return child;
            return null;
        }

        private void SubscribeBuildEvents()
        {
            if (_eventsSubscribed || _level?.BuildEvents == null) return;
            var events = _level.BuildEvents;
            events.OnRoomAdded += OnRoomChanged;
            events.OnRoomRemoved += OnRoomChanged;
            events.OnRoomDeleted += OnRoomChanged;
            events.OnNewRoomBuiltEvent += OnRoomChanged;
            events.OnRoomBuiltEvent += OnRoomBuilt;
            events.OnAcceptRoom += OnRoomGeometryChanged;
            events.OnRoomDragEnd += OnRoomGeometryChanged;
            events.OnMoveRoomEnd += OnMoveRoomEnd;
            events.OnRoomItemAdded += OnRoomItemChanged;
            events.OnRoomItemRemoved += OnRoomItemChanged;
            events.OnRoomItemRotated += OnRoomItemRotated;
            _eventsSubscribed = true;
        }

        private void UnsubscribeBuildEvents()
        {
            if (!_eventsSubscribed || _level?.BuildEvents == null) return;
            var events = _level.BuildEvents;
            events.OnRoomAdded -= OnRoomChanged;
            events.OnRoomRemoved -= OnRoomChanged;
            events.OnRoomDeleted -= OnRoomChanged;
            events.OnNewRoomBuiltEvent -= OnRoomChanged;
            events.OnRoomBuiltEvent -= OnRoomBuilt;
            events.OnAcceptRoom -= OnRoomGeometryChanged;
            events.OnRoomDragEnd -= OnRoomGeometryChanged;
            events.OnMoveRoomEnd -= OnMoveRoomEnd;
            events.OnRoomItemAdded -= OnRoomItemChanged;
            events.OnRoomItemRemoved -= OnRoomItemChanged;
            events.OnRoomItemRotated -= OnRoomItemRotated;
            _eventsSubscribed = false;
        }

        private void OnRoomChanged(Room room) { _networkDirty = true; }
        private void OnRoomBuilt(Room room, int cost) { _networkDirty = true; }
        private void OnRoomGeometryChanged() { _networkDirty = true; }
        private void OnMoveRoomEnd(bool moved, Vector3 position) { _networkDirty = true; }
        private void OnRoomItemChanged(RoomItem item, FloorPlan plan) { _networkDirty = true; }
        private void OnRoomItemRotated(RoomItem item) { _networkDirty = true; }

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
            foreach (var generator in _generatorCells) _tilePowerValue[generator] = 0;
            foreach (var cable in _cells) _tilePowerValue[cable] = -1;

            var queue = new Queue<PowerCoord>();
            foreach (var cable in _cells)
            {
                if (!HasAdjacentGenerator(cable)) continue;
                _tilePowerValue[cable] = 1;
                queue.Enqueue(cable);
            }

            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                var nextDistance = _tilePowerValue[current] + 1;
                foreach (var neighbour in CardinalNeighbours(current))
                    VisitCableNeighbour(neighbour, nextDistance, queue, _cells, _tilePowerValue, int.MaxValue);
            }

            _activePanelCells.Clear();
            foreach (var panel in _panelCells)
                foreach (var neighbour in CardinalNeighbours(panel))
                    if (_tilePowerValue.TryGetValue(neighbour, out var highDistance) && highDistance > 0)
                    {
                        _activePanelCells.Add(panel);
                        break;
                    }

            _lowVoltagePowerValue.Clear();
            foreach (var cable in _lowVoltageCells) _lowVoltagePowerValue[cable] = -1;
            queue.Clear();
            foreach (var cable in _lowVoltageCells)
            {
                if (!HasAdjacentActivePanel(cable)) continue;
                _lowVoltagePowerValue[cable] = 1;
                queue.Enqueue(cable);
            }
            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                var nextDistance = _lowVoltagePowerValue[current] + 1;
                foreach (var neighbour in CardinalNeighbours(current))
                    VisitCableNeighbour(neighbour, nextDistance, queue, _lowVoltageCells,
                        _lowVoltagePowerValue, LowVoltageMaximumLength);
            }

            foreach (var pair in _cellVisuals)
            {
                var powered = !_gridOverloaded &&
                              _tilePowerValue.TryGetValue(pair.Key, out var distance) && distance > 0;
                SetVisualColor(pair.Value, powered ? PoweredColor : DisconnectedColor);
            }
            foreach (var pair in _lowVoltageVisuals)
            {
                var powered = !_gridOverloaded &&
                              _lowVoltagePowerValue.TryGetValue(pair.Key, out var distance) && distance > 0;
                SetVisualColor(pair.Value, powered ? LowVoltageColor : LowVoltageDisconnectedColor);
            }
            RebuildGeneratorVisual();
            RebuildPanelVisual();
            if (_showFlow) RebuildFlowVisuals();
            if (_electricityViewActive) RefreshElectricItemColors();
            PowerGridPlugin.Log.LogInfo($"Red recalculada: {_generatorCells.Count} fuentes, " +
                                        $"{_activePanelCells.Count}/{_panelCells.Count} cuadros activos, " +
                                        $"{_cells.Count} Alto V. y {_lowVoltageCells.Count} Bajo V.");
        }

        private void RefreshEnergyCapacity()
        {
            if (!_energyStateInitialised)
            {
                _contractedEnergy = DefaultContractedEnergy;
                _batteryEnergyHundredths = 0;
                _lastDailyEnergy = Mathf.Max(0,
                    Mathf.FloorToInt(ElectricityGameplay.GetMonthlyEnergyDemand(_level) / 30f) +
                    CountPoweredHighVoltageCables());
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
            var active = Active;
            if (active == null || amount <= 0 || !ReferenceEquals(active._level?.FinanceManager, manager)) return;
            active._currentDayTaskEnergy += amount;
        }

        internal static void ConsumeDailyMonthlyEnergy(Level level)
        {
            var active = Active;
            if (active == null || level == null || !ReferenceEquals(active._level, level)) return;
            active.ReconcileBatteryStates();
            var monthly = ElectricityGameplay.GetMonthlyEnergyDemand(level);
            active._dailyEnergyRemainder += Math.Max(0, monthly) / 30f;
            var monthlyDailyEnergy = Mathf.FloorToInt(active._dailyEnergyRemainder + 0.000001f);
            active._dailyEnergyRemainder -= monthlyDailyEnergy;
            var dailyEnergy = monthlyDailyEnergy + active.CountPoweredHighVoltageCables();
            active._lastDailyEnergy = Math.Max(0, dailyEnergy);
            active._lastTaskEnergy = Math.Max(0, active._currentDayTaskEnergy);
            active._currentDayTaskEnergy = 0;
            active.RechargeBatteries();
            active.RefreshBatteryEnergy();
            var availableHundredths = Math.Max(0, active._contractedEnergy) * EnergyHundredths +
                                      active._batteryEnergyHundredths;
            var dailyConsumption = active._lastDailyEnergy + active._lastTaskEnergy;
            var dailyConsumptionHundredths = dailyConsumption * EnergyHundredths;
            var contractedHundredths = Math.Max(0, active._contractedEnergy) * EnergyHundredths;
            var batteryDemandHundredths = Math.Max(0, dailyConsumptionHundredths - contractedHundredths);
            var wasOverloaded = active._gridOverloaded;
            active._gridOverloaded = dailyConsumptionHundredths > availableHundredths;
            active.DischargeBatteries(Math.Min(batteryDemandHundredths, active._batteryEnergyHundredths));
            active.RefreshBatteryEnergy();
            active._energyCapacityHundredths = contractedHundredths + active._batteryEnergyHundredths;
            active.RefreshEnergyHud();
            active.RefreshElectricItemColors();
            active.RefreshNetworkOutageVisuals();
            if (active._gridOverloaded != wasOverloaded)
                PowerGridPlugin.Log.LogWarning(active._gridOverloaded
                    ? "Red electrica caida: el consumo diario " + dailyConsumption +
                      " supera la energia disponible " + FormatEnergy(availableHundredths) + "."
                    : "Red electrica recuperada al comenzar el nuevo dia.");
            else
                PowerGridPlugin.Log.LogInfo("Consumo electrico diario: " + dailyConsumption + "/" +
                                            FormatEnergy(availableHundredths) + " (Diario " + active._lastDailyEnergy +
                                            ", Tareas " + active._lastTaskEnergy + ").");
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

        private void RefreshNetworkOutageVisuals()
        {
            foreach (var pair in _cellVisuals)
            {
                if (pair.Value == null) continue;
                var powered = !_gridOverloaded && _tilePowerValue.TryGetValue(pair.Key, out var distance) && distance > 0;
                SetVisualColor(pair.Value, powered ? PoweredColor : DisconnectedColor);
            }
            foreach (var pair in _lowVoltageVisuals)
            {
                if (pair.Value == null) continue;
                var powered = !_gridOverloaded &&
                              _lowVoltagePowerValue.TryGetValue(pair.Key, out var distance) && distance > 0;
                SetVisualColor(pair.Value, powered ? LowVoltageColor : LowVoltageDisconnectedColor);
            }
        }

        private void RebuildGeneratorCells()
        {
            _generatorCells.Clear();
            if (_level?.WorldState?.AllRooms == null) return;
            foreach (var room in _level.WorldState.AllRooms)
            {
                if (room == null || !PowerPlantRoomRegistry.IsPowerPlant(room.Definition)) continue;
                var plan = room.FloorPlan;
                var tiles = plan?.Tiles;
                if (tiles == null) continue;
                for (var x = 0; x < tiles.GetLength(0); ++x)
                for (var y = 0; y < tiles.GetLength(1); ++y)
                {
                    if (!tiles[x, y]) continue;
                    var gridX = plan.Anchor.X + x;
                    var gridY = plan.Anchor.Y + y;
                    _generatorCells.Add(new PowerCoord(gridX * 2 - 1, gridY * 2 - 1));
                    _generatorCells.Add(new PowerCoord(gridX * 2, gridY * 2 - 1));
                    _generatorCells.Add(new PowerCoord(gridX * 2 - 1, gridY * 2));
                    _generatorCells.Add(new PowerCoord(gridX * 2, gridY * 2));
                }
            }
        }

        private bool HasAdjacentGenerator(PowerCoord cable)
        {
            return _generatorCells.Contains(new PowerCoord(cable.X - 1, cable.Y)) ||
                   _generatorCells.Contains(new PowerCoord(cable.X + 1, cable.Y)) ||
                   _generatorCells.Contains(new PowerCoord(cable.X, cable.Y - 1)) ||
                   _generatorCells.Contains(new PowerCoord(cable.X, cable.Y + 1));
        }

        private static void VisitCableNeighbour(PowerCoord neighbour, int distance, Queue<PowerCoord> queue,
            HashSet<PowerCoord> cells, Dictionary<PowerCoord, int> powerValues, int maximumDistance)
        {
            if (distance > maximumDistance || !cells.Contains(neighbour)) return;
            if (powerValues.TryGetValue(neighbour, out var previous) && previous >= 0 && previous <= distance)
                return;
            powerValues[neighbour] = distance;
            queue.Enqueue(neighbour);
        }

        private bool HasAdjacentActivePanel(PowerCoord cable)
        {
            foreach (var neighbour in CardinalNeighbours(cable))
                if (_activePanelCells.Contains(neighbour)) return true;
            return false;
        }

        private void RebuildPanelCells()
        {
            _panelCells.Clear();
            if (_level?.WorldState == null || EnergyRoomItems.Panel == null) return;
            var panels = _level.WorldState.GetRoomItemsOfType(EnergyRoomItems.Panel);
            if (panels == null) return;
            foreach (var panel in panels)
            {
                if (!EnergyRoomItems.IsPanel(panel)) continue;
                _panelCells.Add(GetPanelCell(panel));
            }
        }

        private static PowerCoord GetPanelCell(RoomItem panel)
        {
            var facing = panel.GridRotation.DirectionVector();
            return PowerCoord.FromWorldPosition(panel.WorldPosition + facing * 0.5f);
        }

        private void RebuildGeneratorVisual()
        {
            DestroyAreaVisual(ref _generatorAreaVisual);
            if (_generatorCells.Count == 0) return;
            _generatorAreaVisual = CreateAreaVisual(_generatorCells, "PowerGeneratorArea", GeneratorColor, 0.07f);
            _generatorAreaVisual.SetActive(_electricityViewActive);
        }

        private void RebuildPanelVisual()
        {
            DestroyAreaVisual(ref _panelAreaVisual);
            if (_panelCells.Count == 0) return;
            _panelAreaVisual = CreateAreaVisual(_panelCells, "ElectricalPanelCells", PanelCellColor, 0.08f);
            _panelAreaVisual.SetActive(_electricityViewActive);
        }

        private static void SetVisualColor(GameObject visual, Color color)
        {
            if (visual == null) return;
            var renderer = visual.GetComponent<Renderer>();
            if (renderer == null) return;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }

        private void RebuildFlowVisuals()
        {
            DestroyFlowVisuals();
            if (!_showFlow) return;
            foreach (var pair in _tilePowerValue)
            {
                if (pair.Value <= 0 || !_cells.Contains(pair.Key)) continue;
                _flowVisuals.Add(CreateDistanceLabel(pair.Key, pair.Value));
                if (TryGetPowerPredecessor(pair.Key, pair.Value, _tilePowerValue, _generatorCells,
                        out var predecessor))
                    _flowVisuals.Add(CreateFlowArrow(pair.Key, predecessor, PoweredColor));
            }
            foreach (var pair in _lowVoltagePowerValue)
            {
                if (pair.Value <= 0 || !_lowVoltageCells.Contains(pair.Key)) continue;
                _flowVisuals.Add(CreateDistanceLabel(pair.Key, pair.Value));
                if (TryGetPowerPredecessor(pair.Key, pair.Value, _lowVoltagePowerValue, _activePanelCells,
                        out var predecessor))
                    _flowVisuals.Add(CreateFlowArrow(pair.Key, predecessor, LowVoltageColor));
            }
            RebuildElectricityCostIndicators();
            SetFlowVisualsActive(_electricityViewActive);
        }

        private void RebuildElectricityCostIndicators()
        {
            DestroyElectricityCostIndicators();
            if (_level?.WorldState?.AllRooms == null || _level.StatusIconManager == null || _level.HUD == null)
                return;

            var template = _level.StatusIconManager.GetStatusIcon(StatusIcon.Type.QueuePosition);
            if (template == null) return;
            foreach (var room in _level.WorldState.AllRooms)
            {
                var items = room?.FloorPlan?.Items;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (!ElectricityGameplay.TryGetDisplayCost(item, out var cost, out var kind) ||
                        item?.Visual?.GameObject == null) continue;
                    var indicator = CreateElectricityCostIndicator(template, item, cost, kind);
                    if (indicator != null) _electricityCostIndicators.Add(indicator);
                }
            }
        }

        private ElectricityCostIndicator CreateElectricityCostIndicator(StatusIcon template, RoomItem item, int cost,
            ElectricityGameplay.CostKind kind)
        {
            var root = Object.Instantiate(template.gameObject);
            root.name = "UnderPressureElectricityCost_" + cost;
            root.SetActive(false);

            var queueIcon = root.GetComponent<StatusIconQueuePosition>();
            var hudElement = StatusIconHudElementField?.GetValue(queueIcon) as InWorldHUDElement;
            var text = QueuePositionTextField?.GetValue(queueIcon) as Component;
            if (queueIcon == null || hudElement == null || text == null)
            {
                Object.Destroy(root);
                return null;
            }

            // Keep the exact queue-number visual while disabling its patient-specific update.
            queueIcon.enabled = false;
            AccessTools.Property(text.GetType(), "text")?.SetValue(text, cost.ToString(), null);
            var badgeColor = kind == ElectricityGameplay.CostKind.Monthly
                ? new Color(0.42f, 0.84f, 1f, 1f)
                : new Color(0.90f, 0.28f, 0.62f, 1f);
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null) continue;
                image.color = badgeColor;
            }
            hudElement.Position = ElectricityIndicatorPosition(item);
            hudElement.CanBeHidden = false;
            _level.HUD.AddElement(hudElement, _level.HUD.InWorldTransform);
            root.SetActive(_electricityViewActive && _showFlow);
            return new ElectricityCostIndicator { Root = root, HudElement = hudElement };
        }

        private static Vector3 ElectricityIndicatorPosition(RoomItem item)
        {
            var center = item.WorldCenter;
            var top = center.y + 1.05f;
            var visual = item.Visual?.GameObject;
            if (visual != null)
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null) top = Mathf.Max(top, renderer.bounds.max.y + 0.08f);
            var definition = item.Definition as RoomItemDefinition;
            var prefabName = definition?.GetPrefab(0)?.name;
            // RI_OfficeDesk contains imported helper geometry above the visible desk.
            // Compensate that known bad bound without disturbing correctly imported objects.
            if (string.Equals(prefabName, "RI_OfficeDesk", StringComparison.OrdinalIgnoreCase)) top -= 0.34f;
            return new Vector3(center.x, top, center.z);
        }

        private static bool TryGetPowerPredecessor(PowerCoord coord, int distance,
            Dictionary<PowerCoord, int> powerValues, HashSet<PowerCoord> sources, out PowerCoord predecessor)
        {
            var neighbours = new[]
            {
                new PowerCoord(coord.X - 1, coord.Y), new PowerCoord(coord.X + 1, coord.Y),
                new PowerCoord(coord.X, coord.Y - 1), new PowerCoord(coord.X, coord.Y + 1)
            };
            foreach (var neighbour in neighbours)
            {
                if (distance == 1 && sources.Contains(neighbour))
                {
                    predecessor = neighbour;
                    return true;
                }
                if (distance > 1 && powerValues.TryGetValue(neighbour, out var value) &&
                    value == distance - 1)
                {
                    predecessor = neighbour;
                    return true;
                }
            }
            predecessor = default(PowerCoord);
            return false;
        }

        private GameObject CreateDistanceLabel(PowerCoord coord, int distance)
        {
            var label = new GameObject("PowerDistance_" + distance, typeof(TextMesh));
            label.transform.SetParent(_visualRoot.transform, false);
            label.transform.position = coord.ToWorldPosition() + new Vector3(0f, 0.145f, 0f);
            // Hospital entrances and the normal level viewpoint are generally to the
            // south, so orient diagnostics to be read from that side.
            label.transform.rotation = Quaternion.Euler(90f, 180f, 0f);
            var text = label.GetComponent<TextMesh>();
            text.text = distance.ToString();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 64;
            text.characterSize = 0.075f;
            text.color = Color.white;
            return label;
        }

        private GameObject CreateFlowArrow(PowerCoord coord, PowerCoord predecessor, Color color)
        {
            var directionX = coord.X - predecessor.X;
            var directionY = coord.Y - predecessor.Y;
            var arrow = new GameObject("PowerFlowArrow");
            arrow.transform.SetParent(_visualRoot.transform, false);
            arrow.transform.position = coord.ToWorldPosition() +
                                       new Vector3(directionX * -0.40f, 0.14f, directionY * -0.40f);
            arrow.transform.rotation = Quaternion.Euler(0f,
                Mathf.Atan2(directionX, directionY) * Mathf.Rad2Deg, 0f);

            const float fillOffset = -0.018f;
            CreateArrowOutline(arrow.transform, 0.30f, 0.36f, 0.70f, fillOffset);
            CreateArrowTriangle(arrow.transform, "PowerFlowArrowFill", 0.21f, 0.252f,
                fillOffset, color);
            return arrow;
        }

        private void CreateArrowOutline(Transform parent, float width, float height, float innerScale,
            float innerOffset)
        {
            var outline = new GameObject("PowerFlowArrowBorder", typeof(MeshFilter), typeof(MeshRenderer));
            outline.transform.SetParent(parent, false);
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var innerWidth = halfWidth * innerScale;
            var innerHeight = halfHeight * innerScale;
            var mesh = new Mesh { name = "UnderPressurePowerFlowArrowBorder" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, halfHeight),
                new Vector3(-halfWidth, 0f, -halfHeight),
                new Vector3(halfWidth, 0f, -halfHeight),
                new Vector3(0f, 0f, innerHeight + innerOffset),
                new Vector3(-innerWidth, 0f, -innerHeight + innerOffset),
                new Vector3(innerWidth, 0f, -innerHeight + innerOffset)
            };
            mesh.triangles = new[]
            {
                0, 4, 1, 0, 3, 4,
                1, 4, 5, 1, 5, 2,
                2, 5, 3, 2, 3, 0
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            outline.GetComponent<MeshFilter>().sharedMesh = mesh;
            SetArrowRendererColor(outline.GetComponent<MeshRenderer>(), Color.white);
        }

        private void CreateArrowTriangle(Transform parent, string name, float width, float height,
            float longitudinalOffset, Color color)
        {
            var triangle = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            triangle.transform.SetParent(parent, false);
            triangle.transform.localPosition = new Vector3(0f, 0f, longitudinalOffset);
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var mesh = new Mesh { name = "UnderPressure" + name };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, halfHeight),
                new Vector3(-halfWidth, 0f, -halfHeight),
                new Vector3(halfWidth, 0f, -halfHeight)
            };
            // Clockwise from above, so the visible face and normal point upwards.
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            triangle.GetComponent<MeshFilter>().sharedMesh = mesh;
            SetArrowRendererColor(triangle.GetComponent<MeshRenderer>(), color);
        }

        private void SetArrowRendererColor(MeshRenderer renderer, Color color)
        {
            renderer.sharedMaterial = _manager.ValueMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }

        private void SetFlowVisualsActive(bool active)
        {
            foreach (var visual in _flowVisuals)
                if (visual != null) visual.SetActive(active);
            foreach (var indicator in _electricityCostIndicators)
                if (indicator?.Root != null) indicator.Root.SetActive(active);
        }

        private void DestroyFlowVisuals()
        {
            foreach (var visual in _flowVisuals)
            {
                if (visual == null) continue;
                foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                    if (filter != null && filter.sharedMesh != null)
                        Object.Destroy(filter.sharedMesh);
                Object.Destroy(visual);
            }
            _flowVisuals.Clear();
            DestroyElectricityCostIndicators();
        }

        private void DestroyElectricityCostIndicators()
        {
            foreach (var indicator in _electricityCostIndicators)
            {
                if (indicator == null) continue;
                if (indicator.HudElement != null && _level?.HUD != null)
                    _level.HUD.RemoveElement(indicator.HudElement);
                if (indicator.Root != null) Object.Destroy(indicator.Root);
            }
            _electricityCostIndicators.Clear();
        }

        private void RefreshElectricItemColors()
        {
            if (!_electricityViewActive || _level?.WorldState?.AllRooms == null) return;

            // Panels may be mounted in rooms or corridors, so colour them from the
            // WorldState collection instead of relying on a room floor-plan owner.
            var panels = EnergyRoomItems.Panel == null
                ? null
                : _level.WorldState.GetRoomItemsOfType(EnergyRoomItems.Panel);
            if (panels != null)
                foreach (var panel in panels)
                {
                    if (!EnergyRoomItems.IsPanel(panel) || panel.Visual == null) continue;
                    var powered = !_gridOverloaded && _activePanelCells.Contains(GetPanelCell(panel));
                    panel.Visual.SetValueMaterial(powered ? PoweredColor : UnpoweredObjectColor);
                    panel.Visual.EnableValueMaterial();
                }

            foreach (var room in _level.WorldState.AllRooms)
            {
                var items = room?.FloorPlan?.Items;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (item?.Visual == null || EnergyRoomItems.IsPanel(item) ||
                        !ElectricityGameplay.RequiresPower(item)) continue;
                    var powered = IsItemPoweredInternal(item);
                    item.Visual.SetValueMaterial(powered ? PoweredColor : UnpoweredObjectColor);
                    item.Visual.EnableValueMaterial();
                }
            }
        }

        internal static bool IsPowered(RoomItem item)
        {
            var active = Active;
            // Before the level data-view has created the grid controller, retain native
            // behaviour instead of temporarily disabling every electrical object.
            return active == null || item == null || active.IsItemPoweredInternal(item);
        }

        private bool IsItemPoweredInternal(RoomItem item)
        {
            if (_gridOverloaded) return false;
            if (EnergyRoomItems.IsPanel(item)) return _activePanelCells.Contains(GetPanelCell(item));
            if (!ElectricityGameplay.TryGetDisplayCost(item, out _, out var kind)) return true;
            var powerValues = kind == ElectricityGameplay.CostKind.Monthly
                ? _lowVoltagePowerValue
                : _tilePowerValue;
            var center = PowerCoord.FromWorldPosition(item.WorldCenter);
            for (var offsetX = -1; offsetX <= 1; ++offsetX)
            for (var offsetY = -1; offsetY <= 1; ++offsetY)
            {
                // Exactly the eight surrounding 1x1 electrical cells. A cable directly
                // under the object is deliberately excluded.
                if (offsetX == 0 && offsetY == 0) continue;
                var neighbour = new PowerCoord(center.X + offsetX, center.Y + offsetY);
                if (powerValues.TryGetValue(neighbour, out var distance) && distance > 0)
                    return true;
            }
            return false;
        }

        private static bool ConsumesElectricity(IRoomItemDefinition definition)
        {
            if (definition == null) return false;
            // The transformer is part of the future generation system. It neither
            // consumes power nor participates in the powered/unpowered tinting.
            if (EnergyRoomItems.IsTransformer(definition)) return false;
            if (definition.EnergyCost(0) > 0) return true;
            foreach (var modifier in definition.InteractionAttributeModifiers ??
                     Array.Empty<InteractionAttributeModifier>())
            {
                if (modifier == null) continue;
                var reference = AccessTools.Field(typeof(InteractionAttributeModifier),
                    "_financeModifier")?.GetValue(modifier);
                if (reference == null) continue;
                var type = reference.GetType();
                FieldInfo instanceField = null;
                while (type != null && instanceField == null)
                {
                    instanceField = type.GetField("Instance", BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    type = type.BaseType;
                }
                var finance = instanceField?.GetValue(reference) as FinanceModifier;
                if (finance != null && finance.EnergyCost > 0) return true;
            }
            return false;
        }

        private void SetCommittedVisualsActive(bool active)
        {
            foreach (var visual in _cellVisuals.Values)
                if (visual != null) visual.SetActive(active);
            foreach (var visual in _lowVoltageVisuals.Values)
                if (visual != null) visual.SetActive(active);
        }

        private void RefreshPathAreaVisuals()
        {
            DestroyPathAreaVisuals();
            var added = new HashSet<PowerCoord>();
            foreach (var map in _level.WorldState.HospitalMaps)
            {
                if (map == null || map.Plot == null || !map.Plot.Bought) continue;
                var indoorOrPath = map.IndoorOrPathState;
                var indoor = map.IndoorState;
                if (indoorOrPath == null) continue;
                for (var x = 0; x < indoorOrPath.GetLength(0); ++x)
                for (var y = 0; y < indoorOrPath.GetLength(1); ++y)
                {
                    if (!indoorOrPath[x, y]) continue;
                    if (indoor != null && x < indoor.GetLength(0) && y < indoor.GetLength(1) && indoor[x, y])
                        continue;
                    var gridX = map.Anchor.X + x;
                    var gridY = map.Anchor.Y + y;
                    added.Add(new PowerCoord(gridX * 2 - 1, gridY * 2 - 1));
                    added.Add(new PowerCoord(gridX * 2, gridY * 2 - 1));
                    added.Add(new PowerCoord(gridX * 2 - 1, gridY * 2));
                    added.Add(new PowerCoord(gridX * 2, gridY * 2));
                }
            }
            if (added.Count != 0)
                _pathAreaVisuals.Add(CreateAreaVisual(added, "PowerPathArea", Color.white, 0.04f));
        }

        private GameObject CreateAreaVisual(IEnumerable<PowerCoord> coords, string objectName, Color color,
            float height)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            var half = PowerTileSize * 0.45f;
            foreach (var coord in coords)
            {
                var center = coord.ToWorldPosition() + new Vector3(0f, height, 0f);
                var first = vertices.Count;
                vertices.Add(center + new Vector3(-half, 0f, -half));
                vertices.Add(center + new Vector3(-half, 0f, half));
                vertices.Add(center + new Vector3(half, 0f, half));
                vertices.Add(center + new Vector3(half, 0f, -half));
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }

            var area = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
            area.transform.SetParent(_visualRoot.transform, false);
            var mesh = new Mesh { name = "UnderPressurePathAreaMesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            area.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = area.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _manager.ValueMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
            return area;
        }

        private void SetPathAreaVisualsActive(bool active)
        {
            foreach (var visual in _pathAreaVisuals)
                if (visual != null) visual.SetActive(active);
        }

        private void DestroyPathAreaVisuals()
        {
            foreach (var visual in _pathAreaVisuals)
                DestroyAreaVisual(visual);
            _pathAreaVisuals.Clear();
        }

        private static void DestroyAreaVisual(GameObject visual)
        {
            if (visual == null) return;
            var filter = visual.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
            Object.Destroy(visual);
        }

        private static void DestroyAreaVisual(ref GameObject visual)
        {
            DestroyAreaVisual(visual);
            visual = null;
        }

        private void CancelPainting()
        {
            _toolMode = ToolMode.None;
            CancelDrag();
            RefreshToolButtonColors();
        }

        private void CancelDrag()
        {
            _dragging = false;
            RestoreDeletePreview();
            DestroyPreview();
        }

        private void RestoreDeletePreview()
        {
            foreach (var coord in _hiddenDeletePreview)
            {
                if (_cellVisuals.TryGetValue(coord, out var visual) && visual != null)
                    visual.SetActive(_electricityViewActive);
                if (_lowVoltageVisuals.TryGetValue(coord, out visual) && visual != null)
                    visual.SetActive(_electricityViewActive);
            }
            _hiddenDeletePreview.Clear();
        }

        private void DestroyPreview()
        {
            foreach (var preview in _previewVisuals)
                if (preview != null) Object.Destroy(preview);
            _previewVisuals.Clear();
        }

        private void LogDefinitions()
        {
            var rooms = _level.WorldState.AvailableRooms;
            var items = _level.WorldState.AvailableRoomItems;
            var generators = 0;
            foreach (var item in items)
            {
                if (!item.GeneratesElectricity) continue;
                generators++;
                PowerGridPlugin.Log.LogInfo($"Generador nativo: {item}");
            }
            PowerGridPlugin.Log.LogInfo($"Diagnostico P0: {rooms.Count} salas, {items.Count} objetos, {generators} generadores nativos.");
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            BlocksWorldSelection = false;
            UnsubscribeBuildEvents();
            DestroyPreview();
            DestroyFlowVisuals();
            DestroyPathAreaVisuals();
            DestroyAreaVisual(ref _generatorAreaVisual);
            DestroyAreaVisual(ref _panelAreaVisual);
            foreach (var visual in _cellVisuals.Values)
                if (visual != null) Object.Destroy(visual);
            _cellVisuals.Clear();
            foreach (var visual in _lowVoltageVisuals.Values)
                if (visual != null) Object.Destroy(visual);
            _lowVoltageVisuals.Clear();
            _lowVoltageCells.Clear();
            _generatorCells.Clear();
            _panelCells.Clear();
            _activePanelCells.Clear();
            _tilePowerValue.Clear();
            _lowVoltagePowerValue.Clear();
            if (_toolPanel != null) Object.Destroy(_toolPanel.gameObject);
            if (_energyHudRoot != null) Object.Destroy(_energyHudRoot.gameObject);
            if (_visualRoot != null) Object.Destroy(_visualRoot);
        }

        private sealed class ExtraRoomRecord
        {
            internal readonly int PlotIndex;
            internal readonly int AnchorX;
            internal readonly int AnchorY;
            private readonly int _width;
            private readonly int _height;
            private readonly string _tiles;
            internal readonly List<ExtraItemRecord> Items = new List<ExtraItemRecord>();

            internal ExtraRoomRecord(int plotIndex, int anchorX, int anchorY, int width, int height,
                string tiles, string marker)
            {
                PlotIndex = plotIndex;
                AnchorX = anchorX;
                AnchorY = anchorY;
                _width = width;
                _height = height;
                _tiles = tiles;
            }

            internal bool[,] CreateTiles()
            {
                if (_width <= 0 || _height <= 0 || string.IsNullOrEmpty(_tiles) ||
                    _tiles.Length != _width * _height) return null;
                var result = new bool[_width, _height];
                var index = 0;
                for (var x = 0; x < _width; ++x)
                for (var y = 0; y < _height; ++y)
                    result[x, y] = _tiles[index++] == '1';
                return result;
            }
        }

        private sealed class BatteryState
        {
            internal RoomItem Item;
            internal int MaximumHundredths;
            internal int ChargeHundredths;

            internal BatteryState(RoomItem item, int maximumHundredths, int chargeHundredths)
            {
                Item = item;
                MaximumHundredths = maximumHundredths;
                ChargeHundredths = chargeHundredths;
            }
        }

        private sealed class LoadedBatteryRecord
        {
            internal readonly int ItemId;
            internal readonly int MaximumHundredths;
            internal readonly int ChargeHundredths;
            internal readonly Vector2 WorldPosition;

            internal LoadedBatteryRecord(int itemId, int maximumHundredths, int chargeHundredths,
                Vector2 worldPosition)
            {
                ItemId = itemId;
                MaximumHundredths = maximumHundredths;
                ChargeHundredths = chargeHundredths;
                WorldPosition = worldPosition;
            }
        }

        private sealed class ExtraItemRecord
        {
            internal readonly Guid Guid;
            internal readonly Vector3 LocalPosition;
            internal readonly float Rotation;
            internal readonly float Maintenance;
            internal readonly string DebugTag;

            internal ExtraItemRecord(Guid guid, Vector3 localPosition, float rotation, float maintenance,
                string debugTag)
            {
                Guid = guid;
                LocalPosition = localPosition;
                Rotation = rotation;
                Maintenance = maintenance;
                DebugTag = debugTag;
            }
        }

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

    internal static class PowerGridExtraStateSavePatch
    {
        private static void Postfix(SaveSystem __instance, SaveData __0, string __1, int __2, bool __result)
        {
            var levelId = string.IsNullOrEmpty(__1) ? __0?.Level?.UniqueID : __1;
            PowerGridPlugin.Log.LogInfo("Resultado del guardado nativo del nivel " +
                                        (levelId ?? "<desconocido>") + ": " + __result);
            if (__result)
                PowerGridPrototype.Active?.SaveExtraState(true, __2, __0?.Level, levelId);
            else
                LogSerializationFailure(__instance, __0);
        }

        private static void LogSerializationFailure(SaveSystem saveSystem, SaveData saveData)
        {
            try
            {
                var serializer = AccessTools.Field(typeof(SaveSystem), "_serializerLevel")
                    ?.GetValue(saveSystem) as FullSerializerSave.fsSerializer;
                if (serializer == null || saveData == null) return;
                FullSerializerSave.fsData ignored;
                var result = serializer.TrySerialize(saveData, out ignored);
                PowerGridPlugin.Log.LogError("Diagnostico exacto del serializador de nivel: " +
                                             result.FormattedMessages);
            }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogError("No se pudo obtener el diagnostico detallado del serializador: " + exception);
            }
        }
    }

    internal static class PowerGridExtraStateBackupPatch
    {
        private static void Postfix(SaveSystem __instance, string __0)
        {
            try { PowerGridPrototype.ApplyBackup(__instance, __0); }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogError("No se pudo restaurar la copia complementaria: " + exception);
            }
        }
    }

    // SaveSystem's static constructor depends on paths initialised by App during
    // MainScript.Start. Referencing SaveSystem from the initial PatchAll runs that
    // constructor too early and prevents the main menu from starting. Install the
    // two save hooks only after MainScript.Start has completed successfully.
    [HarmonyPatch(typeof(MainScript), "Start")]
    internal static class PowerGridLateSavePatchBootstrap
    {
        private static void Postfix()
        {
            PowerGridLateSavePatches.Install();
        }
    }

    internal static class PowerGridLateSavePatches
    {
        private static bool _installed;

        internal static void Install()
        {
            if (_installed) return;

            try
            {
                var harmony = new Harmony(PowerGridPlugin.PluginGuid);
                var saveMethod = AccessTools.Method(typeof(SaveSystem), "SaveLevelImplementationInner");
                var backupMethod = AccessTools.Method(typeof(SaveSystem), "ApplyBackupLevelSave");
                if (saveMethod == null || backupMethod == null)
                    throw new MissingMethodException("No se encontraron los puntos de guardado nativos.");

                harmony.Patch(
                    saveMethod,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(PowerGridExtraStateSavePatch), "Postfix")));
                harmony.Patch(
                    backupMethod,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(PowerGridExtraStateBackupPatch), "Postfix")));

                _installed = true;
                PowerGridPlugin.Log.LogInfo("Parches tardios de guardado .upsav instalados.");
            }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogError("No se pudieron instalar los parches tardios de guardado: " + exception);
            }
        }
    }
}
