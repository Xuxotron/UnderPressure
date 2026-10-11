// Purpose: Implements .upsav persistence, backups, electrical-room restoration, and staff job records.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
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
            var permanentWearLoadPending = false;
            try
            {
                EnergyCampaignSystem.ResetLevel(_level?.UniqueID);
                global::UnderPressure.PermanentMachineWearSystem.BeginLoad(_level);
                permanentWearLoadPending = true;
                var path = GetExtraDataPath();
                var saveExists = File.Exists(path);
                global::UnderPressure.UnderPressureAssetBundle.ReportExtraSaveLoad(path, saveExists);
                if (!saveExists)
                {
                    permanentWearLoadPending = false;
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
                var taskEnergyWasAppliedImmediately = false;
                _pendingContractedEnergy = NoPendingContractedEnergy;
                _contractActivationMonth = -1;
                _blackoutDaysRemaining = 0;
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
                            ElectricityGameplay.GetDailyRecurringDemand(_level));
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
                    if (parts.Length == 7 && parts[0] == "E4" &&
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
                        taskEnergyWasAppliedImmediately = true;
                    }
                    if (parts.Length == 7 && parts[0] == "E5" &&
                        int.TryParse(parts[1], out contracted) && int.TryParse(parts[2], out dailyEnergy) &&
                        int.TryParse(parts[3], out taskEnergy) && int.TryParse(parts[4], out currentTasks) &&
                        int.TryParse(parts[5], out var accruedDailyBill) && (parts[6] == "0" || parts[6] == "1"))
                    {
                        _contractedEnergy = NormaliseContractedEnergy(contracted);
                        _lastDailyEnergy = Math.Max(0, dailyEnergy);
                        _lastTaskEnergy = Math.Max(0, taskEnergy);
                        _currentDayTaskEnergy = Math.Max(0, currentTasks);
                        _dailyEnergyRemainder = 0f;
                        _accruedDailyEnergyBill = Math.Max(0, accruedDailyBill);
                        _gridOverloaded = parts[6] == "1";
                        _energyStateInitialised = true;
                        taskEnergyWasAppliedImmediately = true;
                    }
                    if (parts.Length == 9 && parts[0] == "E6" &&
                        int.TryParse(parts[1], out contracted) && int.TryParse(parts[2], out var pendingContracted) &&
                        int.TryParse(parts[3], out var activationMonth) && int.TryParse(parts[4], out dailyEnergy) &&
                        int.TryParse(parts[5], out taskEnergy) && int.TryParse(parts[6], out currentTasks) &&
                        int.TryParse(parts[7], out var accruedContractBill) &&
                        (parts[8] == "0" || parts[8] == "1"))
                    {
                        _contractedEnergy = NormaliseSelectableContractedEnergy(contracted);
                        _pendingContractedEnergy = pendingContracted < 0
                            ? NoPendingContractedEnergy
                            : NormaliseSelectableContractedEnergy(pendingContracted);
                        _contractActivationMonth = _pendingContractedEnergy < 0 ? -1 : Math.Max(0, activationMonth);
                        _lastDailyEnergy = Math.Max(0, dailyEnergy);
                        _lastTaskEnergy = Math.Max(0, taskEnergy);
                        _currentDayTaskEnergy = Math.Max(0, currentTasks);
                        _dailyEnergyRemainder = 0f;
                        _accruedDailyEnergyBill = Math.Max(0, accruedContractBill);
                        _gridOverloaded = parts[8] == "1";
                        _energyStateInitialised = true;
                        taskEnergyWasAppliedImmediately = true;
                    }
                    if (parts.Length == 10 && parts[0] == "E7" &&
                        int.TryParse(parts[1], out contracted) && int.TryParse(parts[2], out pendingContracted) &&
                        int.TryParse(parts[3], out activationMonth) && int.TryParse(parts[4], out dailyEnergy) &&
                        int.TryParse(parts[5], out taskEnergy) && int.TryParse(parts[6], out currentTasks) &&
                        int.TryParse(parts[7], out accruedContractBill) &&
                        (parts[8] == "0" || parts[8] == "1") &&
                        int.TryParse(parts[9], out var blackoutDaysRemaining))
                    {
                        _contractedEnergy = NormaliseSelectableContractedEnergy(contracted);
                        _pendingContractedEnergy = pendingContracted < 0
                            ? NoPendingContractedEnergy
                            : NormaliseSelectableContractedEnergy(pendingContracted);
                        _contractActivationMonth = _pendingContractedEnergy < 0 ? -1 : Math.Max(0, activationMonth);
                        _lastDailyEnergy = Math.Max(0, dailyEnergy);
                        _lastTaskEnergy = Math.Max(0, taskEnergy);
                        _currentDayTaskEnergy = Math.Max(0, currentTasks);
                        _dailyEnergyRemainder = 0f;
                        _accruedDailyEnergyBill = Math.Max(0, accruedContractBill);
                        _blackoutDaysRemaining = Mathf.Clamp(blackoutDaysRemaining, 0, BlackoutDurationDays);
                        _gridOverloaded = _blackoutDaysRemaining > 0 || parts[8] == "1";
                        _energyStateInitialised = true;
                        taskEnergyWasAppliedImmediately = true;
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
                if (!taskEnergyWasAppliedImmediately && _currentDayTaskEnergy > 0)
                {
                    var pendingTaskEnergy = _currentDayTaskEnergy;
                    _currentDayTaskEnergy = 0;
                    _lastTaskEnergy = 0;
                    ConsumeTaskEnergyImmediately(pendingTaskEnergy);
                }
                RestoreStaffJobAssignments(staffJobRecords);
                permanentWearLoadPending = false;
                global::UnderPressure.PermanentMachineWearSystem.CompleteLoad(_level);
                PowerGridPlugin.Log.LogInfo($"Guardado complementario cargado: {_cells.Count} Alto V., " +
                                            $"{_lowVoltageCells.Count} Bajo V., " +
                                            $"{roomRecords.Count} salas y {staffJobRecords.Count} marcados ({path}).");
            }
            catch (Exception exception)
            {
                PowerGridPlugin.Log.LogError("No se pudo cargar el fichero extra de la red electrica: " + exception);
            }
            finally
            {
                if (permanentWearLoadPending)
                {
                    try
                    {
                        global::UnderPressure.PermanentMachineWearSystem.CompleteLoad(_level);
                    }
                    catch (Exception exception)
                    {
                        PowerGridPlugin.Log.LogError(
                            "No se pudo finalizar la restauracion del desgaste permanente: " + exception);
                    }
                }
            }
        }

        private void AddLoadedCable(PowerCoord coord, bool lowVoltage)
        {
            var cells = lowVoltage ? _lowVoltageCells : _cells;
            var visuals = lowVoltage ? _lowVoltageVisuals : _cellVisuals;
            if (!cells.Add(coord)) return;
            var visual = CreateTileVisual(coord, lowVoltage ? LowVoltageColor : PoweredColor,
                lowVoltage ? "LowVoltageCell" : "HighVoltageCell");
            visual.SetActive(_electricityViewActive);
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
                lines.Add("E7," + _contractedEnergy + "," + _pendingContractedEnergy + "," +
                          _contractActivationMonth + "," + _lastDailyEnergy + "," + _lastTaskEnergy + "," +
                          _currentDayTaskEnergy + "," + _accruedDailyEnergyBill + "," +
                          (_gridOverloaded ? "1" : "0") + "," + _blackoutDaysRemaining);
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
                    if (EnergyRoomExists(record.AnchorX, record.AnchorY))
                    {
                        ApplySavedMaintenance(record);
                        continue;
                    }
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
    }

    internal static class PowerGridExtraStateSavePatch
    {
        private static void Postfix(SaveSystem __instance, SaveData __0, string __1, int __2, bool __result)
        {
            var levelId = string.IsNullOrEmpty(__1) ? __0?.Level?.UniqueID : __1;
            PowerGridPlugin.Log.LogInfo("Resultado del guardado nativo del nivel " +
                                        (levelId ?? "<desconocido>") + ": " + __result);
            if (__result)
                PowerGridSystem.Active?.SaveExtraState(true, __2, __0?.Level, levelId);
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
            try { PowerGridSystem.ApplyBackup(__instance, __0); }
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
