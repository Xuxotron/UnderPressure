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
        internal readonly float CircularEmissionRadius;
        internal readonly float ScaleMultiplier;
        internal readonly float SpeedMultiplier;
        internal readonly bool TintMagenta;

        internal WorldMapMesh(string id, WorldMapSource source, string asset, Vector3 position)
            : this(id, source, asset, position, 0f, 1f, 1f, false)
        {
        }

        internal WorldMapMesh(string id, WorldMapSource source, string asset, Vector3 position,
            float circularEmissionRadius)
            : this(id, source, asset, position, circularEmissionRadius, 1f, 1f, false)
        {
        }

        internal WorldMapMesh(string id, WorldMapSource source, string asset, Vector3 position,
            float circularEmissionRadius, float scaleMultiplier, float speedMultiplier,
            bool tintMagenta = false)
        {
            Id = id;
            Source = source;
            Asset = asset;
            Position = position;
            CircularEmissionRadius = circularEmissionRadius;
            ScaleMultiplier = scaleMultiplier;
            SpeedMultiplier = speedMultiplier;
            TintMagenta = tintMagenta;
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
                "conflux_swirl",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Portal[1]/[pfx] Time Portal[5]/Swirl [5]",
                new Vector3(1.991f, 71.773f, 1026.279f),
                0f, 0.7f, 0.5f, true),
            new WorldMapMesh(
                "conflux_halo",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Portal[1]/[pfx] Time Portal[5]/Halo[3]",
                new Vector3(1.991f, 71.773f, 1026.279f),
                0f, 0.7f, 0.5f, true),
            new WorldMapMesh(
                "conflux_glow_lines",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Portal[1]/[pfx] Time Portal[5]/Glow Lines[2]",
                new Vector3(1.991f, 71.773f, 1026.279f),
                0f, 1f, 1f, true),
            new WorldMapMesh(
                "conflux_time_leak",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Fissure[0]/[pfx] Time Leak[2]",
                new Vector3(1.991f, 71.773f, 1026.279f),
                55.84f, 1f, 1f, true)
        };

        internal static readonly WorldMapHospital[] Hospitals =
        {
            new WorldMapHospital(
				"hogsport_plus",
                "Metagame[0]/Pins[7]/MapPin_Hospital_R1_L1[0]",
                new Vector3(4.336f, 98.794f, 3.267f),
				@"Xuxotron\Hogsport_Plus\config.json"),

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
