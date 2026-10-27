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

        internal WorldMapHospitalCamera(string levelConfigName, Vector3? cameraFocalPoint,
            float cameraRotation, float cameraZoom)
        {
            LevelConfigName = levelConfigName;
            HasCamera = cameraFocalPoint.HasValue;
            CameraFocalPoint = cameraFocalPoint.GetValueOrDefault();
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
				@"Xuxotron\Hogsport_Plus.upmap"),

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

        // Cada hospital está listo para pegar los tres valores de Ctrl+Alt+F10.
        // Mientras CameraFocalPoint sea null, ese hospital conserva el enfoque nativo.
        internal static readonly WorldMapHospitalCamera[] HospitalCameras =
        {
            new WorldMapHospitalCamera(
                levelConfigName: "R1_TutorialHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R1_IntroHospital2",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R1_IntroHospital3",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R2_ResearchHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R2_WealthyHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R2_FractureHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R3_TeachingHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R3_AbandonedHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R3_PublicHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R4_DisasterHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R4_TouristHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R4_EpidemicHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R5_HighTechHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R5_FailingHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R5_FinalHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R6_InjuriesHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R6_MoreTrainingHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R6_CastleHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R7_JungleClearingHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R7_HordeHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R7_AtollHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R8_KitschAmericanaHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R8_AirBaseHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R8_SecretFacilityHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R9_HippyCommuneHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R9_AmishHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R9_EcoCityHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R10_TelevisionHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R10_FestivalHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R10_GalaHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R11_Medieval",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R11_Prehistory",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R11_Future",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R12_RoadHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R12_RoadAndAirHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f),
            new WorldMapHospitalCamera(
                levelConfigName: "R12_RescueHospital",
                cameraFocalPoint: null,
                cameraRotation: 0f,
                cameraZoom: 0f)
        };
    }
}
