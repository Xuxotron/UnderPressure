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
        internal readonly bool ContinuousStaticRotation;
        internal readonly string TextureOverrideSourceName;
        internal readonly string TextureOverrideFile;

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
            bool tintMagenta = false, bool continuousStaticRotation = false,
            string textureOverrideSourceName = null, string textureOverrideFile = null)
        {
            Id = id;
            Source = source;
            Asset = asset;
            Position = position;
            CircularEmissionRadius = circularEmissionRadius;
            ScaleMultiplier = scaleMultiplier;
            SpeedMultiplier = speedMultiplier;
            TintMagenta = tintMagenta;
            ContinuousStaticRotation = continuousStaticRotation;
            TextureOverrideSourceName = textureOverrideSourceName;
            TextureOverrideFile = textureOverrideFile;
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
        internal readonly Vector3 CameraFocalPoint;
        internal readonly float CameraRotation;
        internal readonly float CameraZoom;

        internal WorldMapConflux(string id, string template, Vector3 position,
            Vector3 cameraFocalPoint, float cameraRotation, float cameraZoom)
        {
            Id = id;
            Template = template;
            Position = position;
            CameraFocalPoint = cameraFocalPoint;
            CameraRotation = cameraRotation;
            CameraZoom = cameraZoom;
        }
    }

    internal sealed class WorldMapHospitalCamera
    {
        internal readonly string LevelConfigName;
        internal readonly bool HasCamera;
        internal readonly Vector3 CameraFocalPoint;
        internal readonly float CameraRotation;
        internal readonly float CameraZoom;

        internal WorldMapHospitalCamera(string levelConfigName)
        {
            LevelConfigName = levelConfigName;
        }

        internal WorldMapHospitalCamera(string levelConfigName, Vector3 cameraFocalPoint,
            float cameraRotation, float cameraZoom)
        {
            LevelConfigName = levelConfigName;
            HasCamera = true;
            CameraFocalPoint = cameraFocalPoint;
            CameraRotation = cameraRotation;
            CameraZoom = cameraZoom;
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
                new Vector3(2f, 80f, 1020f),
                0f, 0.7f, 0.5f, true, true,
                "T_Portal_Whirl", "T_Portal_Whirl2.png"),
            new WorldMapMesh(
                "conflux_halo",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Portal[1]/[pfx] Time Portal[5]/Halo[3]",
                new Vector3(2f, 70f, 1020f),
                0f, 0.7f, 0.5f, true, true),
            new WorldMapMesh(
                "conflux_glow_lines",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Portal[1]/[pfx] Time Portal[5]/Glow Lines[2]",
                new Vector3(2f, 70f, 1020f),
                0f, 0.5f, 1f, true, true),
            new WorldMapMesh(
                "conflux_time_leak",
                WorldMapSource.Scene,
                "Metagame[0]/DLC_06[50]/Time Fissure[0]/[pfx] Time Leak[2]",
                new Vector3(2f, 50f, 1020f),
                50f, 1f, 1f, true, true)
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
                new Vector3(1.991f, 71.773f, 1026.279f),
                // Encuadre al seleccionar el Conflux. Punto focal, rotacion y zoom
                // se pueden copiar desde Ctrl+Alt+F10 y ajustar manualmente aqui.
                new Vector3(106.0239f, 0.75f, 948.1385f),
                3f,
                0f)
        };

        // Camaras opcionales de los 36 hospitales reales del juego. Las entradas
        // vacias conservan el enfoque nativo. Para configurarlas, sustituir una por:
        // new WorldMapHospitalCamera("Nombre", new Vector3(x, y, z), rotacion, zoom)
        internal static readonly WorldMapHospitalCamera[] HospitalCameras =
        {
            new WorldMapHospitalCamera("R1_TutorialHospital"),
            new WorldMapHospitalCamera("R1_IntroHospital2"),
            new WorldMapHospitalCamera("R1_IntroHospital3"),
            new WorldMapHospitalCamera("R2_ResearchHospital"),
            new WorldMapHospitalCamera("R2_WealthyHospital"),
            new WorldMapHospitalCamera("R2_FractureHospital"),
            new WorldMapHospitalCamera("R3_TeachingHospital"),
            new WorldMapHospitalCamera("R3_AbandonedHospital"),
            new WorldMapHospitalCamera("R3_PublicHospital"),
            new WorldMapHospitalCamera("R4_DisasterHospital"),
            new WorldMapHospitalCamera("R4_TouristHospital"),
            new WorldMapHospitalCamera("R4_EpidemicHospital"),
            new WorldMapHospitalCamera("R5_HighTechHospital"),
            new WorldMapHospitalCamera("R5_FailingHospital"),
            new WorldMapHospitalCamera("R5_FinalHospital"),
            new WorldMapHospitalCamera("R6_InjuriesHospital"),
            new WorldMapHospitalCamera("R6_MoreTrainingHospital"),
            new WorldMapHospitalCamera("R6_CastleHospital"),
            new WorldMapHospitalCamera("R7_JungleClearingHospital"),
            new WorldMapHospitalCamera("R7_HordeHospital"),
            new WorldMapHospitalCamera("R7_AtollHospital"),
            new WorldMapHospitalCamera("R8_KitschAmericanaHospital"),
            new WorldMapHospitalCamera("R8_AirBaseHospital"),
            new WorldMapHospitalCamera("R8_SecretFacilityHospital"),
            new WorldMapHospitalCamera("R9_HippyCommuneHospital"),
            new WorldMapHospitalCamera("R9_AmishHospital"),
            new WorldMapHospitalCamera("R9_EcoCityHospital"),
            new WorldMapHospitalCamera("R10_TelevisionHospital"),
            new WorldMapHospitalCamera("R10_FestivalHospital"),
            new WorldMapHospitalCamera("R10_GalaHospital"),
            new WorldMapHospitalCamera("R11_Medieval"),
            new WorldMapHospitalCamera("R11_Prehistory"),
            new WorldMapHospitalCamera("R11_Future"),
            new WorldMapHospitalCamera("R12_RoadHospital"),
            new WorldMapHospitalCamera("R12_RoadAndAirHospital"),
            new WorldMapHospitalCamera("R12_RescueHospital")
        };
    }
}
