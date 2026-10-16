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
        internal readonly string MapPath;

        internal WorldMapHospital(string id, string template, Vector3 position)
            : this(id, template, position, null)
        {
        }

        internal WorldMapHospital(string id, string template, Vector3 position, string mapPath)
        {
            Id = id;
            Template = template;
            Position = position;
            MapPath = mapPath;
        }
    }

    internal sealed class WorldMapConflux
    {
        internal readonly string Id;
        internal readonly string Template;
        internal readonly Vector3 Position;

        internal WorldMapConflux(string id, string template, Vector3 position)
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
                "Transformador",
                new Vector3(4.336f, 98.794f, 3.267f)),
            new WorldMapMesh(
                "conflux_vortex",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Portal[1]/[pfx] Time Portal[5]/Vortex[4]",
                new Vector3(1.991f, 71.773f, 1026.279f))
        };

        internal static readonly WorldMapHospital[] Hospitals =
        {
            new WorldMapHospital(
				"hogsport_plus",
                "Metagame[0]/Pins[7]/MapPin_Hospital_R1_L1[0]",
                new Vector3(4.336f, 98.794f, 3.267f),
				@"901\Hogsport_Plus\Hogsport_Plus.upmap"),

        };

        internal static readonly WorldMapConflux[] Confluxes =
        {
            new WorldMapConflux(
                "Conflux",
                "Metagame[0]/Pins[7]/MapPin_Hospital_R1_L1[0]",
                new Vector3(1.991f, 71.773f, 1026.279f))
        };
    }
}
