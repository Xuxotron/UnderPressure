using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Storey
{
    public static class StoreyApi
    {
        public const int GroundStorey = 0;
        public const int UpperStorey = 1;
        public const float StoreyHeight = 2f;

        private static readonly Dictionary<HospitalPlotDefinition, int> Storeys =
            new Dictionary<HospitalPlotDefinition, int>();

        public static void SetStorey(HospitalPlotDefinition definition, int storey)
        {
            if (definition == null) return;
            Storeys[definition] = storey == UpperStorey ? UpperStorey : GroundStorey;
        }

        public static int GetStorey(HospitalPlotDefinition definition)
        {
            if (definition == null) return GroundStorey;
            return Storeys.TryGetValue(definition, out var storey)
                ? storey
                : GroundStorey;
        }

        public static int GetStorey(HospitalPlot plot) =>
            GetStorey(plot?.Definition);

        public static Vector3 GetMapOffset(HospitalPlotDefinition definition) =>
            new Vector3(0f, GetStorey(definition) * StoreyHeight, 0f);

        public static void SetCursorStorey(CursorManager cursor, int storey)
        {
            cursor?.SetPlaneOffset(
                (storey == UpperStorey ? UpperStorey : GroundStorey) *
                StoreyHeight);
        }

        public static void ApplyVisualOrigin(
            RoomFloorPlanVisual visual,
            HospitalPlotDefinition definition)
        {
            var root = visual?.GameObject?.transform;
            if (root == null) return;
            var position = root.position;
            position.y = GetMapOffset(definition).y;
            root.position = position;
        }

        internal static void Clear() => Storeys.Clear();
    }

    // RoomFloorPlanVisual es el contenedor nativo común de suelo, paredes,
    // puertas y objetos. El desplazamiento se aplica una sola vez al mapa.
    [HarmonyPatch(typeof(RoomFloorPlanVisual), "UpdateFromRoom")]
    internal static class StoreyMapOriginPatch
    {
        private static void Prefix(FloorPlan floorPlan, ref Vector3 cellOffset)
        {
            var definition = floorPlan?.HospitalMap?.Plot?.Definition;
            if (StoreyApi.GetStorey(definition) != StoreyApi.UpperStorey)
                return;
            cellOffset += StoreyApi.GetMapOffset(definition);
        }
    }

    // Algunos cursores nativos actualizan directamente una visual y omiten el
    // cellOffset que UpdateFromRoom sí propaga. Completar el mismo parámetro
    // conserva todas las coordenadas lógicas del objeto en Y=0.
    [HarmonyPatch(typeof(RoomItemVisual), "UpdateFrom")]
    internal static class StoreyRoomItemOffsetPatch
    {
        private static void Prefix(RoomItem item, ref Vector3 cellOffset)
        {
            var definition = item?.FloorPlan?.HospitalMap?.Plot?.Definition;
            if (StoreyApi.GetStorey(definition) != StoreyApi.UpperStorey ||
                !Mathf.Approximately(cellOffset.y, 0f))
                return;
            cellOffset += StoreyApi.GetMapOffset(definition);
        }
    }

    // El editor nativo crea el plano temporal de arrastre con HospitalMap=null.
    // Asociarlo al mapa seleccionado permite que use el mismo origen visual.
    [HarmonyPatch(typeof(CursorEditHospitalItem), MethodType.Constructor)]
    internal static class StoreyLandscapeBlueprintPatch
    {
        private static readonly FieldInfo PlotField =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_hospitalPlot");
        private static readonly FieldInfo BlueprintField =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_dragFloorPlan");

        private static void Postfix(CursorEditHospitalItem __instance)
        {
            var plot = PlotField?.GetValue(__instance) as HospitalPlot;
            var blueprint = BlueprintField?.GetValue(__instance) as BlueprintFloorPlan;
            if (plot?.HospitalMap == null || blueprint == null) return;
            blueprint.SetHospitalMap(plot.HospitalMap);
        }
    }

    // CursorManager conserva X/Z lógicos en Y=0 aunque proyecte el ratón
    // sobre otro plano. Sólo el indicador visual adopta la altura activa.
    [HarmonyPatch(typeof(CursorManager), "UpdateCursorPosition")]
    internal static class StoreyCursorVisualPatch
    {
        private static readonly FieldInfo PlaneOffsetField =
            AccessTools.Field(typeof(CursorManager), "_planeOffset");
        private static readonly FieldInfo VisualField =
            AccessTools.Field(typeof(CursorManager), "_visualisation");

        private static void Postfix(CursorManager __instance)
        {
            if (!(PlaneOffsetField?.GetValue(__instance) is float offset) ||
                Mathf.Approximately(offset, 0f))
                return;
            var visual = VisualField?.GetValue(__instance) as GameObject;
            if (visual == null) return;
            var position = visual.transform.position;
            position.y = offset;
            visual.transform.position = position;
        }
    }
}
