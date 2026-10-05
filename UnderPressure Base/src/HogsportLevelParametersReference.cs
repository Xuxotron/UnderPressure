// Purpose: Records Hogsport's original level parameters as a typed reference catalogue.
using System;

namespace UnderPressure.Research
{
    /// <summary>
    /// Copia legible de los parametros originales del primer nivel, Hogsport.
    ///
    /// Este archivo es documental: no modifica el juego y esta excluido de
    /// UnderPressure.csproj mediante Compile Remove="tools\**\*.cs".
    ///
    /// Fuente verificada: TPH_Data/sharedassets3.assets.
    /// Assets principales: R1_TutorialHospital (path 156482),
    /// Config_CharacterManager_R1_TutorialHospital (156472),
    /// Config_Finance_R1_TutorialHospital (156473) y
    /// Config_ChallengeManager_R1_TutorialHospital (156471).
    /// </summary>
    public static class HogsportLevelParameters
    {
        public const string InternalId = "901";
        public const string InternalAsset = "R1_TutorialHospital";
        public const string DisplayName = "Hogsport";
        public const string Scene = "Scene_R1_TutorialHospital";

        public const int InitialMoney = 200000;
        public const float LocalMarketRateModifier = 1f;
        public const bool IssuesSharesOnStart = false;
        public const float StaffHappinessModifier = 10f;
        public const int DebtWarningBalance = -150000;
        public const int GameOverBalance = -300000;

        public const int MaximumStaff = 512;
        public const int MaximumPatients = 250;
        public const float BasePatientSpawnRate = 48f;
        public const float ReputationArrivalRateMinimum = 0.8f;
        public const float ReputationArrivalRateMaximum = 1.2f;
        public const float ArrivalRandomFactor = 10f;
        public const float PatientSelectionRandomJitter = 0.35f;
        public const float PatientTimePortalSpawnRate = 1f;
        public const float PatientTimePortalSpawnReductionPerInstance = 0.1f;
        public const int ChanceOfLowHygieneEffect = 50;
        public const float LowHygieneCheckInterval = 4f;
        public const bool NeverSpawnPatients = false;

        // El juego calcula el intervalo nativo a partir del valor base y lo
        // reduce con los multiplicadores de reputacion y nivel del hospital.
        // ArrivalRandomFactor se conserva como valor bruto porque su unidad
        // no esta declarada en el asset.
        public static readonly ArrivalMethodData[] RandomArrivalMethods =
        {
            new ArrivalMethodData("ArrivalMethod_Car_Random", 100, 4),
            new ArrivalMethodData("ArrivalMethod_Bus", 25, 10),
            new ArrivalMethodData("ArrivalMethod_Ambulance", 25, 4),
        };

        // La probabilidad de escoger cada metodo, antes de considerar su
        // capacidad, es 100/150, 25/150 y 25/150 respectivamente.
        public const float CarSelectionPercentage = 66.66667f;
        public const float BusSelectionPercentage = 16.66667f;
        public const float AmbulanceSelectionPercentage = 16.66667f;

        public static readonly IllnessData[] PatientTypes =
        {
            new IllnessData(
                "Illness_Pharm_D0_Grout", true, 100, 200, 0, 0,
                "Inicial", "Room_T_Pharmacy", 5000, 100f, 100f, 0f,
                new[] { new DiagnosisData("Room_D_GPOffice", 1000f) }),
            new IllnessData(
                "Illness_Pharm_D1_Clamp", true, 100, 200, 0, 5,
                "Inicial; empieza a ser elegible tras 5 pacientes generados",
                "Room_T_Pharmacy", 6000, 90f, 100f, 0f,
                new[] { new DiagnosisData("Room_D_GPOffice", 1000f) }),
            new IllnessData(
                "Illness_Pharm_D3_Bogwarts", false, 100, 200, 0, 10,
                "El tutorial la anade al desbloquear Diagnostico General",
                "Room_T_Pharmacy", 6000, 60f, 99f, 20f,
                new[]
                {
                    new DiagnosisData("Room_D_GPOffice", 60f),
                    new DiagnosisData("Room_D_GeneralDiagnosis", 35f),
                    new DiagnosisData("Room_DT_Ward", 7f),
                }),
            new IllnessData(
                "Illness_Pharm_D4_MiseryGuts", false, 100, 200, 1, 0,
                "El tutorial la anade al desbloquear Diagnostico General; minimo 1 estrella en la tabla",
                "Room_T_Pharmacy", 7000, 50f, 99f, 20f,
                new[]
                {
                    new DiagnosisData("Room_D_GPOffice", 60f),
                    new DiagnosisData("Room_D_Cardio", 30f),
                    new DiagnosisData("Room_D_FluidAnalysis", 25f),
                }),
            new IllnessData(
                "Illness_Ward_D1_Bedface", false, 400, 800, 0, 10,
                "El tutorial la anade al desbloquear la Planta",
                "Room_DT_Ward", 5000, 80f, 99f, 10f,
                new[]
                {
                    new DiagnosisData("Room_D_GPOffice", 85f),
                    new DiagnosisData("Room_DT_Psychiatry", 5f),
                }),
            new IllnessData(
                "Illness_Special_LightHeaded", false, 200, 400, 0, 0,
                "El tutorial la anade al desbloquear la Clinica De-Lux",
                "Room_T_LightHeaded", 8000, 80f, 99f, 100f,
                new[] { new DiagnosisData("Room_D_GPOffice", 90f) }),
        };

        // Config_HospitalStatusLevel heredado de Config_Level. PatientArrivalRate
        // es el multiplicador nativo de afluencia ligado al nivel/prestigio.
        public static readonly PrestigeLevelData[] PrestigeLevels =
        {
            new PrestigeLevelData(1, 10f, 1f, 0),
            new PrestigeLevelData(2, 20f, 1f, 0),
            new PrestigeLevelData(3, 30f, 1.05f, 0),
            new PrestigeLevelData(4, 40f, 1.1f, 0),
            new PrestigeLevelData(5, 50f, 1.4f, 1),
            new PrestigeLevelData(6, 60f, 1.6f, 1),
            new PrestigeLevelData(7, 70f, 1.8f, 1),
            new PrestigeLevelData(8, 80f, 2f, 1),
            new PrestigeLevelData(9, 90f, 2.2f, 1),
            new PrestigeLevelData(10, 100f, 2.4f, 2),
            new PrestigeLevelData(11, 120f, 2.6f, 2),
            new PrestigeLevelData(12, 140f, 3f, 2),
            new PrestigeLevelData(13, 160f, 3.4f, 2),
            new PrestigeLevelData(14, 180f, 3.8f, 2),
            new PrestigeLevelData(15, 200f, 4.2f, 3),
            new PrestigeLevelData(16, 220f, 4.6f, 3),
            new PrestigeLevelData(17, 240f, 5f, 3),
            new PrestigeLevelData(18, 260f, 5.4f, 3),
            new PrestigeLevelData(19, 280f, 5.8f, 3),
            new PrestigeLevelData(20, 300f, 6.2f, 4),
            new PrestigeLevelData(21, 340f, 6.6f, 4),
            new PrestigeLevelData(22, 380f, 7.4f, 4),
            new PrestigeLevelData(23, 420f, 8.2f, 4),
            new PrestigeLevelData(24, 460f, 9f, 4),
            new PrestigeLevelData(25, 500f, 9.8f, 5),
            new PrestigeLevelData(26, 540f, 10.6f, 5),
            new PrestigeLevelData(27, 580f, 11.4f, 5),
            new PrestigeLevelData(28, 620f, 12.2f, 5),
            new PrestigeLevelData(29, 660f, 13f, 5),
            new PrestigeLevelData(30, 700f, 13.8f, 5),
        };

        public static class Reputation
        {
            public const float MedicalWeight = 1f;
            public const float PatientWeight = 0.5f;
            public const float PricesWeight = 0.0005f;
            public const float StaffWeight = 1f;
            public const float SpecialWeight = 1f;
            public const float StaffRate = 0.1f;
            public const float PatientRate = 0.1f;
            public const float OverallPowK = 0.02f;
            public const float OverallPowE = 2.71828f;

            public const float IllnessPowK = 0.05f;
            public const float IllnessPowE = 2.71828f;
            public const float IllnessMinimum = -200f;
            public const float IllnessMaximum = 200f;
            public const float IllnessDecayRate = 0.0007f;

            public const float MedicalPowK = 0.02f;
            public const float MedicalPowE = 2.71828f;
            public const float MedicalMinimum = -1000f;
            public const float MedicalMaximum = 1000f;

            public const float StaffPowK = 0.05f;
            public const float StaffPowE = 2.71828f;
            public const float StaffMinimum = -200f;
            public const float StaffMaximum = 200f;

            public const float PatientPowK = 0.05f;
            public const float PatientPowE = 2.71828f;
            public const float PatientMinimum = -200f;
            public const float PatientMaximum = 200f;

            public const float SpecialPowK = 0.02f;
            public const float SpecialPowE = 2.71828f;
            public const float SpecialMinimum = -1000f;
            public const float SpecialMaximum = 1000f;
            public const float SpecialDecayRate = 0.0007f;

            public const float PricePowK = 0.00003f;
            public const float PricePowE = 2.71828f;
            public const float PricesMinimum = -1000000f;
            public const float PricesMaximum = 1000000f;
            public const float PriceDecayRate = 7f;
        }

        public static class Time
        {
            public const float RealTimeToGameTimeMultiplier = 30f;
            public const int DefaultTimeScaleIndex = 1;
            public const int ReleaseMaximumTimeScaleIndex = 2;

            public static readonly float[] TimeScales = { 0.5f, 1f, 2f, 4f, 8f };
        }

        public static class HospitalPolicy
        {
            public const float DiagnosisCertaintyMinimum = 50f;
            public const float DiagnosisCertaintyMaximum = 100f;
            public const float DiagnosisCertaintyDefault = 90f;
            public const int QueueWarningMinimum = 1;
            public const int QueueWarningMaximum = 20;
            public const int QueueWarningDefault = 6;
            public const bool AutoSendForTreatment = false;
            public const bool StaffLeaveRooms = true;
            public const bool StaffTrainingRequests = true;
            public const bool AutomaticStaffPromotion = false;
        }

        public static readonly ApplicantPoolData ApplicantPool = new ApplicantPoolData(
            new[] { "Doctor", "Nurse", "Janitor", "Assistant" },
            1f, 120f, 60f, 185f, 1f,
            new[] { 100, 80, 60, 40, 20 }, 3, 50,
            new[] { 10f, 12.5f, 15f, 17.5f, 20f });

        public static readonly LoanData[] Loans =
        {
            new LoanData("Two Point Bank", 25000, 50000, 50000, 5, 24, 0, 0, 0f),
            new LoanData("Swindles", 75000, 100000, 100000, 10, 24, 100000, 0, 0f),
            new LoanData("Smell My Cash", 150000, 250000, 250000, 15, 36, 500000, 2, 0.25f),
        };

        public static class WorkLifeBalance
        {
            // Se aplica por igual a Doctor, Nurse, Assistant y Janitor.
            public const int StaffRank = -1;
            public const float InitialSliderValue = 0.5f;
            public const float MinimumPercentage = 0f;
            public const float MaximumPercentage = 100f;
            public const string ResignationObjective = "StaffChallenge_Resignation";
        }

        // Ambos calendarios empiezan desactivados en el asset. El LevelScript
        // ejecuta ChallengeEnableSchedule para "Challenges" y "VIPs" justo
        // despues de WaitPost1Star.
        public static readonly ChallengeScheduleData[] ChallengeSchedules =
        {
            new ChallengeScheduleData("Challenges", false, true, 90, 120),
            new ChallengeScheduleData("VIPs", false, true, 180, 365),
        };

        public static readonly EmergencyData[] Emergencies =
        {
            new EmergencyData(
                "Challenge_Patients_LightHeaded_TL_1_R1",
                "Illness_Special_LightHeaded", "Room_T_LightHeaded",
                7, 0f, 100f, "ArrivalMethod_Helicopter", "Die", 90, 1),
            new EmergencyData(
                "Challenge_Patients_Pharm_Bogwarts_TL_1",
                "Illness_Pharm_D3_Bogwarts", "Room_T_Pharmacy",
                5, 0f, 100f, "ArrivalMethod_Helicopter", "StayInHospital", 90, 1),
            new EmergencyData(
                "Challenge_Patients_Ward_Bedface_TL_1",
                "Illness_Ward_D1_Bedface", "Room_DT_Ward",
                5, 0f, 100f, "ArrivalMethod_Helicopter", "StayInHospital", 90, 1),
        };

        public const bool EmergencyNoticeCanBeRejected = true;
        public const bool EmergencyWaitsForNoticeResponse = true;
        public const int EmergencyDaysUntilStart = 0;
        public const int EmergencyDaysUntilDebrief = 1;
        public const int EmergencySuccessScore = 50;
        public const bool UsesAmbulanceDepartments = false;

        public static readonly RewardTierData[] EmergencyRewards =
        {
            new RewardTierData("Puntuacion < 50", 0, -5, 0),
            new RewardTierData("Puntuacion >= 50", 10000, 10, 10),
            new RewardTierData("Puntuacion >= 100", 20000, 15, 15),
        };

        public static readonly VipData HealthInspector = new VipData(
            "Challenge_VIP_Inspector_R1_Hogsport", "ArrivalMethod_Car_Blue",
            10, 5, 5, 0.9f, 180, 365);

        public static readonly string[] HealthInspectorPreferredRooms =
        {
            "GPOffice",
            "Pharmacy",
            "XRay",
            "GeneralDiagnosis",
            "Cardiography",
            "Cafe",
            "Ward",
            "StaffRoom",
            "Toilets",
        };

        public static readonly string[] HealthInspectorExcludedRooms = { "Reception" };

        public static readonly VipCriterionData[] HealthInspectorCriteria =
        {
            new VipCriterionData("EnvironmentAttractiveness", 1f),
            new VipCriterionData("EnvironmentTemperature", 1f),
            new VipCriterionData("EnvironmentHygiene", 2f),
            new VipCriterionData("StaffHappiness", 0.5f),
            new VipCriterionData("StaffEnergy", 1f),
            new VipCriterionData("StaffRankQualification", 1f),
            new VipCriterionData("StaffGotFired", -1f),
            new VipCriterionData("PatientHappiness", 0.5f),
            new VipCriterionData("PatientHealth", 1f),
            new VipCriterionData("PatientRageQuitting", -3f),
            new VipCriterionData("PatientIsDead", -5f),
            new VipCriterionData("PatientIsCured", 1f),
            new VipCriterionData("PatientTreatmentIneffective", -1f),
            new VipCriterionData("RoomUnderstaffed", -1f),
            new VipCriterionData("ItemMaintenance", 2f),
            new VipCriterionData("WasteItems", -2f),
            new VipCriterionData("RoomPrestige", 1f),
            new VipCriterionData("TourTooShort", -5f),
            new VipCriterionData("HospitalEcoRating", 0f),
        };

        public static readonly RewardTierData[] HealthInspectorRewards =
        {
            new RewardTierData("Puntuacion < -10", 0, -15, 0),
            new RewardTierData("Puntuacion < 0", 0, -10, 0),
            new RewardTierData("Puntuacion < 10", 0, 0, 0),
            new RewardTierData("Puntuacion < 20", 5000, 8, 10),
            new RewardTierData("Puntuacion >= 20", 10000, 15, 20),
        };

        public const int MaximumActiveStaffChallenges = 0;
        public const float StaffChallengeGenerationMinimumSeconds = 300f;
        public const float StaffChallengeGenerationMaximumSeconds = 600f;

        public static readonly StarObjectiveData[] StarObjectives =
        {
            new StarObjectiveData(1, 3, "Illness_Special_LightHeaded", 0, 0, 10000, 100,
                "Item_Picture_Poster_LightHeaded"),
            new StarObjectiveData(2, 25, null, 200000, 6, 20000, 150,
                "Item_VendingMachine_SaltySnacks"),
            new StarObjectiveData(3, 30, null, 400000, 0, 30000, 200,
                "Item_Shop_Kiosk_Gifts", 750000),
        };

        public static readonly AwardData[] YearlyAwards =
        {
            new AwardData("DoctorOfTheYear", 5f),
            new AwardData("NurseOfTheYear", 5f),
            new AwardData("JanitorOfTheYear", 50f),
            new AwardData("AssistantOfTheYear", 50f),
            new AwardData("EmployerOfTheYear", 10f),
            new AwardData("NoDeaths", 0f),
            new AwardData("MostPrestigious", 3f),
            new AwardData("PatientsChoice", 10f),
        };

        public const int AwardMoney = 5000;
        public const int AwardKudosh = 5;
        public const int AwardReputation = 3;

        public static class EventsAndDisasters
        {
            // No hay configuracion de terremotos, epidemias, volcanes ni otros
            // desastres asignada a Hogsport o a su LevelScript.
            public const bool HasScheduledCatastrophes = false;
            public static readonly string[] ScheduledCatastrophes = Array.Empty<string>();

            // Estos incidentes si aparecen en el arbol del nivel y pueden
            // ocurrir por el desgaste normal de las maquinas.
            public static readonly string[] MachineIncidents =
            {
                "MachineOnFire",
                "MachineExploded",
            };

            // El registro global heredado contiene entradas para terremotos y
            // epidemias. Solo permite registrarlos si otro sistema los crea;
            // no los activa en Hogsport.
            public static readonly string[] GlobalLogOnlyDisasterEntries =
            {
                "HospitalEventEarthquake",
                "HospitalEventEpidemicStart",
                "HospitalEventEpidemicEnd",
            };
        }

        public static class World
        {
            public const float InitialTemperature = 0f;
            public const float InitialAttractiveness = 0f;
            public const float InitialHygiene = 1f;
            public const bool CreateBaseLandscapeItems = false;
            public const float PerimeterOffset = 0.75f;
            public const string InitialPlot = "TutorialHospital_Plot_00";
            public const string FogOfWar = "FogOfWarDefinition_Hogsport";
            public static readonly string[] InitiallyAvailableRooms = Array.Empty<string>();
        }

        // El LevelScript va habilitando estas salas y enfermedades; no estan
        // todas disponibles al cargar el mapa.
        public static readonly string[] TutorialUnlockFlow =
        {
            "GPOffice",
            "Pharmacy + Illness_Pharm_D0_Grout/Illness_Pharm_D1_Clamp",
            "StaffRoom",
            "Toilets",
            "GeneralDiagnosis + Bogwarts + MiseryGuts",
            "Ward + Bedface",
            "Room_T_LightHeaded + LightHeaded",
            "Tras 1 estrella: calendarios Challenges y VIPs",
        };

        // Limites globales heredados. La presencia de Lightning o Volcanic en
        // esta tabla no activa una catastrofe; solo limita objetos ya creados.
        public static readonly SpawnLimitData[] SpawnLimits =
        {
            new SpawnLimitData("BodilyFluids", 128),
            new SpawnLimitData("Debris", 256),
            new SpawnLimitData("Ectoplasm", 64),
            new SpawnLimitData("Litter", 128),
            new SpawnLimitData("Lightning", 32),
            new SpawnLimitData("Volcanic", 64),
        };

        public static readonly string[] InheritedSystems =
        {
            "Config_GameTime",
            "Config_JobApplicantManager",
            "Config_Research (111 proyectos globales; el nivel no define una lista propia)",
            "Config_ReputationTracker",
            "Config_LoanManager",
            "Config_MarketingManager",
            "Config_WorkLifeBalance",
            "Config_PriceModifiables",
            "Config_HospitalStatusLevel",
            "Config_HospitalEventLog",
            "Config_ItemSpawnLimits",
            "Config_HospitalPolicy",
        };
    }

    public sealed class ArrivalMethodData
    {
        public readonly string Asset;
        public readonly int Weight;
        public readonly int MaximumVehicleCapacity;

        public ArrivalMethodData(string asset, int weight, int maximumVehicleCapacity)
        {
            Asset = asset;
            Weight = weight;
            MaximumVehicleCapacity = maximumVehicleCapacity;
        }
    }

    public sealed class DiagnosisData
    {
        public readonly string Room;
        public readonly float CertaintyIncrease;

        public DiagnosisData(string room, float certaintyIncrease)
        {
            Room = room;
            CertaintyIncrease = certaintyIncrease;
        }
    }

    public sealed class IllnessData
    {
        public readonly string Asset;
        public readonly bool InitiallyUnlocked;
        public readonly int MinimumWeight;
        public readonly int MaximumWeight;
        public readonly int MinimumStarRating;
        public readonly int MinimumPatientsSpawned;
        public readonly string UnlockCondition;
        public readonly string TreatmentRoom;
        public readonly int TreatmentPrice;
        public readonly float BaseTreatmentEffectiveness;
        public readonly float MaximumTreatmentEffectiveness;
        public readonly float DeathChanceOnTreatmentFailure;
        public readonly DiagnosisData[] DiagnosisRooms;

        public IllnessData(string asset, bool initiallyUnlocked, int minimumWeight,
            int maximumWeight, int minimumStarRating, int minimumPatientsSpawned,
            string unlockCondition, string treatmentRoom, int treatmentPrice,
            float baseTreatmentEffectiveness, float maximumTreatmentEffectiveness,
            float deathChanceOnTreatmentFailure, DiagnosisData[] diagnosisRooms)
        {
            Asset = asset;
            InitiallyUnlocked = initiallyUnlocked;
            MinimumWeight = minimumWeight;
            MaximumWeight = maximumWeight;
            MinimumStarRating = minimumStarRating;
            MinimumPatientsSpawned = minimumPatientsSpawned;
            UnlockCondition = unlockCondition;
            TreatmentRoom = treatmentRoom;
            TreatmentPrice = treatmentPrice;
            BaseTreatmentEffectiveness = baseTreatmentEffectiveness;
            MaximumTreatmentEffectiveness = maximumTreatmentEffectiveness;
            DeathChanceOnTreatmentFailure = deathChanceOnTreatmentFailure;
            DiagnosisRooms = diagnosisRooms;
        }
    }

    public sealed class PrestigeLevelData
    {
        public readonly int Level;
        public readonly float Points;
        public readonly float PatientArrivalRate;
        public readonly int ExtraJobApplicantSlots;

        public PrestigeLevelData(int level, float points, float patientArrivalRate,
            int extraJobApplicantSlots)
        {
            Level = level;
            Points = points;
            PatientArrivalRate = patientArrivalRate;
            ExtraJobApplicantSlots = extraJobApplicantSlots;
        }
    }

    public sealed class ApplicantPoolData
    {
        public readonly string[] StaffTypes;
        public readonly float InitialPercentageToFill;
        public readonly float TimeUntilNextApplicant;
        public readonly float TimeUntilNextApplicantMax;
        public readonly float TimeUntilRemoveApplicant;
        public readonly float MarketingBoostMultiplier;
        public readonly int[] RankWeights;
        public readonly int MinimumSlots;
        public readonly int ChanceOfEmptyTrainingSlot;
        public readonly float[] RecruitmentFeePercentages;

        public ApplicantPoolData(string[] staffTypes, float initialPercentageToFill,
            float timeUntilNextApplicant, float timeUntilNextApplicantMax,
            float timeUntilRemoveApplicant, float marketingBoostMultiplier,
            int[] rankWeights, int minimumSlots, int chanceOfEmptyTrainingSlot,
            float[] recruitmentFeePercentages)
        {
            StaffTypes = staffTypes;
            InitialPercentageToFill = initialPercentageToFill;
            TimeUntilNextApplicant = timeUntilNextApplicant;
            TimeUntilNextApplicantMax = timeUntilNextApplicantMax;
            TimeUntilRemoveApplicant = timeUntilRemoveApplicant;
            MarketingBoostMultiplier = marketingBoostMultiplier;
            RankWeights = rankWeights;
            MinimumSlots = minimumSlots;
            ChanceOfEmptyTrainingSlot = chanceOfEmptyTrainingSlot;
            RecruitmentFeePercentages = recruitmentFeePercentages;
        }
    }

    public sealed class LoanData
    {
        public readonly string Name;
        public readonly int MinimumAmount;
        public readonly int MaximumAmount;
        public readonly int DefaultAmount;
        public readonly int InterestRate;
        public readonly int RepaymentPeriod;
        public readonly int RequiredHospitalValue;
        public readonly int RequiredHospitalLevel;
        public readonly float RequiredReputation;

        public LoanData(string name, int minimumAmount, int maximumAmount,
            int defaultAmount, int interestRate, int repaymentPeriod,
            int requiredHospitalValue, int requiredHospitalLevel,
            float requiredReputation)
        {
            Name = name;
            MinimumAmount = minimumAmount;
            MaximumAmount = maximumAmount;
            DefaultAmount = defaultAmount;
            InterestRate = interestRate;
            RepaymentPeriod = repaymentPeriod;
            RequiredHospitalValue = requiredHospitalValue;
            RequiredHospitalLevel = requiredHospitalLevel;
            RequiredReputation = requiredReputation;
        }
    }

    public sealed class ChallengeScheduleData
    {
        public readonly string Name;
        public readonly bool EnabledOnStart;
        public readonly bool StartsWithCooldown;
        public readonly int MinimumCooldownDays;
        public readonly int MaximumCooldownDays;

        public ChallengeScheduleData(string name, bool enabledOnStart,
            bool startsWithCooldown, int minimumCooldownDays, int maximumCooldownDays)
        {
            Name = name;
            EnabledOnStart = enabledOnStart;
            StartsWithCooldown = startsWithCooldown;
            MinimumCooldownDays = minimumCooldownDays;
            MaximumCooldownDays = maximumCooldownDays;
        }
    }

    public sealed class EmergencyData
    {
        public readonly string Asset;
        public readonly string Illness;
        public readonly string RequiredUnlockedRoom;
        public readonly int PatientCount;
        public readonly float PatientSpawnRate;
        public readonly float DiagnosisComplete;
        public readonly string ArrivalMethod;
        public readonly string ActionOnFailure;
        public readonly int TimeLengthDays;
        public readonly int ScheduleWeight;

        public EmergencyData(string asset, string illness, string requiredUnlockedRoom,
            int patientCount, float patientSpawnRate, float diagnosisComplete,
            string arrivalMethod, string actionOnFailure, int timeLengthDays,
            int scheduleWeight)
        {
            Asset = asset;
            Illness = illness;
            RequiredUnlockedRoom = requiredUnlockedRoom;
            PatientCount = patientCount;
            PatientSpawnRate = patientSpawnRate;
            DiagnosisComplete = diagnosisComplete;
            ArrivalMethod = arrivalMethod;
            ActionOnFailure = actionOnFailure;
            TimeLengthDays = timeLengthDays;
            ScheduleWeight = scheduleWeight;
        }
    }

    public sealed class RewardTierData
    {
        public readonly string Condition;
        public readonly int Money;
        public readonly int Reputation;
        public readonly int Kudosh;

        public RewardTierData(string condition, int money, int reputation, int kudosh)
        {
            Condition = condition;
            Money = money;
            Reputation = reputation;
            Kudosh = kudosh;
        }
    }

    public sealed class VipData
    {
        public readonly string Asset;
        public readonly string ArrivalMethod;
        public readonly int DaysUntilStart;
        public readonly int MinimumRoomsInTour;
        public readonly int MaximumRoomsInTour;
        public readonly float PreferredRoomProbability;
        public readonly int ScheduleMinimumCooldownDays;
        public readonly int ScheduleMaximumCooldownDays;

        public VipData(string asset, string arrivalMethod, int daysUntilStart,
            int minimumRoomsInTour, int maximumRoomsInTour,
            float preferredRoomProbability, int scheduleMinimumCooldownDays,
            int scheduleMaximumCooldownDays)
        {
            Asset = asset;
            ArrivalMethod = arrivalMethod;
            DaysUntilStart = daysUntilStart;
            MinimumRoomsInTour = minimumRoomsInTour;
            MaximumRoomsInTour = maximumRoomsInTour;
            PreferredRoomProbability = preferredRoomProbability;
            ScheduleMinimumCooldownDays = scheduleMinimumCooldownDays;
            ScheduleMaximumCooldownDays = scheduleMaximumCooldownDays;
        }
    }

    public sealed class VipCriterionData
    {
        public readonly string Criterion;
        public readonly float Weight;

        public VipCriterionData(string criterion, float weight)
        {
            Criterion = criterion;
            Weight = weight;
        }
    }

    public sealed class StarObjectiveData
    {
        public readonly int Star;
        public readonly int PatientsToCure;
        public readonly string RequiredIllness;
        public readonly int MoneyToEarn;
        public readonly int RequiredHospitalLevel;
        public readonly int RewardMoney;
        public readonly int RewardKudosh;
        public readonly string RewardItem;
        public readonly int RequiredHospitalValue;

        public StarObjectiveData(int star, int patientsToCure, string requiredIllness,
            int moneyToEarn, int requiredHospitalLevel, int rewardMoney,
            int rewardKudosh, string rewardItem, int requiredHospitalValue = 0)
        {
            Star = star;
            PatientsToCure = patientsToCure;
            RequiredIllness = requiredIllness;
            MoneyToEarn = moneyToEarn;
            RequiredHospitalLevel = requiredHospitalLevel;
            RewardMoney = rewardMoney;
            RewardKudosh = rewardKudosh;
            RewardItem = rewardItem;
            RequiredHospitalValue = requiredHospitalValue;
        }
    }

    public sealed class AwardData
    {
        public readonly string Type;
        public readonly float ScoreThreshold;

        public AwardData(string type, float scoreThreshold)
        {
            Type = type;
            ScoreThreshold = scoreThreshold;
        }
    }

    public sealed class SpawnLimitData
    {
        public readonly string Category;
        public readonly int MaximumCount;

        public SpawnLimitData(string category, int maximumCount)
        {
            Category = category;
            MaximumCount = maximumCount;
        }
    }
}
