using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.PowerGrid
{
    internal enum EnergyCampaignKind
    {
        None = -1,
        HackPowerCompany = 0,
        DebugCodeWithAi = 1,
        DenyClimateChange = 2
    }

    internal sealed class EnergyCampaignState
    {
        internal EnergyCampaignKind Active = EnergyCampaignKind.None;
        internal readonly float[] Progress = new float[3];
        internal int DurationMonths;
        internal int TimeRemainingDays;
    }

    internal static class EnergyCampaignSystem
    {
        private struct RoomKey : IEquatable<RoomKey>
        {
            internal readonly string Level;
            internal readonly int X;
            internal readonly int Y;
            internal RoomKey(string level, int x, int y) { Level = level ?? string.Empty; X = x; Y = y; }
            public bool Equals(RoomKey other) => Level == other.Level && X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is RoomKey other && Equals(other);
            public override int GetHashCode() => unchecked(((Level.GetHashCode() * 397) ^ X) * 397 ^ Y);
        }

        private static readonly Dictionary<RoomKey, EnergyCampaignState> States = new Dictionary<RoomKey, EnergyCampaignState>();

        internal static EnergyCampaignState Get(Room room)
        {
            var key = Key(room);
            if (!States.TryGetValue(key, out var state))
            {
                state = new EnergyCampaignState();
                States.Add(key, state);
            }
            return state;
        }

        internal static void ResetLevel(string levelId)
        {
            levelId = levelId ?? string.Empty;
            var remove = new List<RoomKey>();
            foreach (var key in States.Keys)
                if (key.Level == levelId) remove.Add(key);
            foreach (var key in remove) States.Remove(key);
        }

        internal static float GetEffect(Level level, EnergyCampaignKind kind)
        {
            if (level == null || kind == EnergyCampaignKind.None) return 0f;
            var best = 0f;
            foreach (var room in level.WorldState?.AllRooms ?? new List<Room>())
            {
                if (room == null || !PowerPlantRoomRegistry.IsPowerPlant(room.Definition)) continue;
                var state = Get(room);
                best = Mathf.Max(best, state.Progress[(int)kind] / 100f);
            }
            return Mathf.Clamp01(best);
        }

        internal static float GetMaximumProgress(Room room, EnergyCampaignKind kind)
        {
            if (room == null || kind == EnergyCampaignKind.None) return 0f;
            // Provisional workforce input until the Training-style assignment window
            // supplies the selected janitors and their real qualification points.
            var janitors = 0;
            if (room.StaffWorkingInRoom != null)
                foreach (var staff in room.StaffWorkingInRoom)
                    if (staff?.Definition != null && staff.Definition._type == StaffDefinition.Type.Janitor) janitors++;
            var capacityPerJanitor = kind == EnergyCampaignKind.HackPowerCompany ? 20f :
                kind == EnergyCampaignKind.DebugCodeWithAi ? 16f : 12f;
            return Mathf.Clamp(janitors * capacityPerJanitor, 0f, 100f);
        }

        internal static EnergyCampaignController GetController(Room room)
        {
            if (room?.Level?.WorldState == null || EnergyRoomItems.Transformer == null) return null;
            var items = room.Level.WorldState.GetRoomItemsOfType(EnergyRoomItems.Transformer);
            if (items == null) return null;
            foreach (var item in items)
                if (item != null && ReferenceEquals(item.OwningRoom, room))
                    return EnergyCampaignRuntime.GetController(item);
            return null;
        }

        internal static void AppendSave(List<string> lines, Level level)
        {
            if (lines == null || level?.WorldState?.AllRooms == null) return;
            foreach (var room in level.WorldState.AllRooms)
            {
                if (room?.FloorPlan == null || !PowerPlantRoomRegistry.IsPowerPlant(room.Definition)) continue;
                var state = Get(room);
                lines.Add(string.Join(",", "P", room.FloorPlan.Anchor.X, room.FloorPlan.Anchor.Y,
                    (int)state.Active, F(state.Progress[0]), F(state.Progress[1]), F(state.Progress[2]),
                    state.DurationMonths, state.TimeRemainingDays));
            }
        }

        internal static void Load(string levelId, int x, int y, int active, float hack, float debug, float climate)
        {
            Load(levelId, x, y, active, hack, debug, climate, 3, active < 0 ? 0 : 91);
        }

        internal static void Load(string levelId, int x, int y, int active, float hack, float debug, float climate,
            int durationMonths, int timeRemainingDays)
        {
            var selected = active >= (int)EnergyCampaignKind.None && active <= (int)EnergyCampaignKind.DenyClimateChange
                ? (EnergyCampaignKind)active : EnergyCampaignKind.None;
            var state = new EnergyCampaignState { Active = selected };
            state.Progress[0] = Mathf.Clamp(hack, 0f, 100f);
            state.Progress[1] = Mathf.Clamp(debug, 0f, 100f);
            state.Progress[2] = Mathf.Clamp(climate, 0f, 100f);
            state.DurationMonths = Mathf.Clamp(durationMonths, 3, 12);
            state.TimeRemainingDays = Mathf.Max(0, timeRemainingDays);
            States[new RoomKey(levelId, x, y)] = state;
        }

        private static RoomKey Key(Room room)
        {
            var anchor = room?.FloorPlan == null ? new GridCoord(0, 0) : room.FloorPlan.Anchor;
            return new RoomKey(room?.Level?.UniqueID, anchor.X, anchor.Y);
        }

        private static string F(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal sealed class EnergyCampaignRoomData : InspectorSubDataRoom
    {
        private static readonly LocalisedString StartText = EnergyLocalization.Create(
            "UnderPressure/EnergyCampaign/Start", "Iniciar campaña", "Start campaign");
        private static readonly LocalisedString ActiveText = EnergyLocalization.Create(
            "UnderPressure/EnergyCampaign/Active", "Campaña eléctrica", "Energy campaign");
        private static readonly LocalisedString TooltipText = EnergyLocalization.Create(
            "UnderPressure/EnergyCampaign/Tooltip", "Gestionar campañas de la Sala de energía", "Manage Energy Room campaigns");

        internal EnergyCampaignRoomData(Room room) : base(room) { }

        public override string GetText()
        {
            var state = EnergyCampaignSystem.Get(_room);
            return state.Active == EnergyCampaignKind.None ? StartText.Translation : ActiveText.Translation;
        }

        public override string GetTooltip() => TooltipText.Translation;
        public override bool ShouldShowButton() => true;

        public override bool OnButtonPressed()
        {
            EnergyCampaignMenu.Open(_room);
            return true;
        }
    }

    [HarmonyPatch(typeof(InspectorDataRoom), "SelectRoom")]
    internal static class EnergyCampaignInspectorPatch
    {
        private static readonly FieldInfo RoomSubDataField = AccessTools.Field(typeof(InspectorDataRoom), "_roomSubData");

        private static void Postfix(InspectorDataRoom __instance, Room __0, bool __result)
        {
            if (!__result || __0 == null || !PowerPlantRoomRegistry.IsPowerPlant(__0.Definition)) return;
            RoomSubDataField?.SetValue(__instance, new EnergyCampaignRoomData(__0));
        }
    }

}
