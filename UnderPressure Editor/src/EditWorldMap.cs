using UnityEngine;

namespace UnderPressure.Editor
{
    internal enum WorldMapSource
    {
        Scene,
        Bundle
    }

    internal sealed class WorldMapMesh
    {
        internal readonly string Id;
        internal readonly WorldMapSource Source;
        internal readonly string Asset;
        internal readonly Vector3 Position;

        internal WorldMapMesh(string id, WorldMapSource source, string asset, Vector3 position)
        {
            Id = id;
            Source = source;
            Asset = asset;
            Position = position;
        }
    }

    internal sealed class WorldMapHospital
    {
        internal readonly string Id;
        internal readonly string Template;
        internal readonly Vector3 Position;

        internal WorldMapHospital(string id, string template, Vector3 position)
        {
            Id = id;
            Template = template;
            Position = position;
        }
    }

    // Configuracion privada del mapamundi. Cada entrada se aplica al compilar
    // UnderPressure.Editor; no se lee ningun fichero JSON en tiempo de ejecucion.
    internal static class EditWorldMap
    {
        internal static readonly WorldMapMesh[] Meshes =
        {
            new WorldMapMesh(
                "isla",
                WorldMapSource.Scene,
                "Metagame[0]/Meta_Map_Test_V3[8]/Xmas_village[488]/polySurface7719[43]",
                new Vector3(4.336f, 98.794f, 3.267f))
        };

        internal static readonly WorldMapHospital[] Hospitals =
        {
            new WorldMapHospital(
                "hospital_nuevo",
                "MapPin_Hospital_R1_L1",
                new Vector3(4.336f, 98.794f, 3.267f))
        };
    }
}
