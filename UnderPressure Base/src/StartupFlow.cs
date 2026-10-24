// Updated: 2026-10-15 - Carga directa de partidas y editor sin crear la escena visual de Carrera.
// Purpose: Groups startup screen skipping and automatic save loading.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnderPressure
{
    [HarmonyPatch(typeof(App), "LoadAndShowOpeningScreen")]
    internal static class SkipFrontEndForAutoLoadPatch
    {
        private static readonly FieldInfo MostRecentLevelSavesField =
            AccessTools.Field(typeof(SaveSystem), "_mMostRecentSave");
        private static readonly FieldInfo MostRecentMetagameSlotField =
            AccessTools.Field(typeof(SaveSystem), "_mostRecentMetagameSaveSlotIndex");
        private static readonly FieldInfo RoomTemplatesInitialisedField =
            AccessTools.Field(typeof(RoomTemplatesManager), "_initialised");

        internal static bool KeepLoadingScreenVisible { get; private set; }
        internal static SaveFileHeader SelectedLevelSave { get; private set; }
        internal static int SelectedSaveSlot { get; private set; } = -1;

        private static void Postfix(App __instance, ref IEnumerator __result)
        {
            if (!UnderPressurePlugin.ShouldAutoLoadLastSave || __instance?.SaveSystem == null)
                return;

            if (!TrySelectMostRecentHospital(__instance.SaveSystem))
            {
                UnderPressurePlugin.Log.LogInfo(
                    "Autocarga omitida: ninguna campaña contiene hospitales guardados.");
                return;
            }

            var originalMetagameSlot = __instance.SaveSystem.MostRecentMetagameSaveSlotIndex;
            MostRecentMetagameSlotField.SetValue(__instance.SaveSystem, SelectedSaveSlot);
            __result = RunWithNativeFrontEndSkip(__instance, originalMetagameSlot, __result);
        }

        private static bool TrySelectMostRecentHospital(SaveSystem saveSystem)
        {
            SelectedLevelSave = null;
            SelectedSaveSlot = -1;

            var savesBySlot = MostRecentLevelSavesField?.GetValue(saveSystem) as SaveFileHeader[];
            if (savesBySlot == null)
                return false;

            for (var slot = 0; slot < savesBySlot.Length; ++slot)
            {
                var candidate = savesBySlot[slot];
                if (candidate == null || candidate.IsBroken ||
                    saveSystem.GetMetagameSaveHeaderForSlot(slot) == null)
                    continue;

                if (SelectedLevelSave != null && candidate.Date <= SelectedLevelSave.Date)
                    continue;

                SelectedLevelSave = candidate;
                SelectedSaveSlot = slot;
            }

            return SelectedLevelSave != null;
        }

        private static IEnumerator RunWithNativeFrontEndSkip(
            App app, int originalMetagameSlot, IEnumerator original)
        {
            var saveSystem = app.SaveSystem;
            var previousValue = DebugVars.SkipFrontEnd.Value;
            InitialiseRoomTemplatesForSkippedFrontEnd(app);
            DebugVars.SkipFrontEnd.Value = true;
            KeepLoadingScreenVisible = true;
            UnderPressurePlugin.Log.LogInfo(
                $"Hospital más reciente global: slot {SelectedSaveSlot + 1}, " +
                $"nivel {SelectedLevelSave.LevelID}, fecha {SelectedLevelSave.Date:O}.");
            try
            {
                yield return original;
            }
            finally
            {
                DebugVars.SkipFrontEnd.Value = previousValue;
                MostRecentMetagameSlotField.SetValue(saveSystem, originalMetagameSlot);
            }
        }

        private static void InitialiseRoomTemplatesForSkippedFrontEnd(App app)
        {
            try
            {
                var manager = app?.RoomTemplatesManager;
                if (manager == null || app.SaveSystem == null) return;
                if (RoomTemplatesInitialisedField?.GetValue(manager) is bool initialised && initialised) return;

                app.SaveSystem.LoadRoomTemplatesSaveData(manager);
                manager.RestoreFromSave(app);
                var loaded = 0;
                foreach (var group in manager.RoomTemplates.Values) loaded += group.Count;
                UnderPressurePlugin.Log.LogInfo(
                    "Plantillas de salas restauradas antes de la autocarga: " + loaded + ".");
            }
            catch (System.Exception exception)
            {
                UnderPressurePlugin.Log.LogError(
                    "No se pudieron restaurar las plantillas antes de la autocarga: " + exception);
            }
        }

        internal static void ReleaseLoadingScreen(App app, bool hide)
        {
            KeepLoadingScreenVisible = false;
            if (hide)
                app?.LoadSaveProgressScreen?.Hide();
        }
    }

    [HarmonyPatch(typeof(LoadSaveProgressScreen), "Hide")]
    internal static class KeepLoadingScreenVisiblePatch
    {
        private static bool Prefix()
        {
            return !SkipFrontEndForAutoLoadPatch.KeepLoadingScreenVisible;
        }
    }

    // Fallback nativo, solo cuando no se ha tomado la ruta de hospital directo.
    [HarmonyPatch(typeof(MetagameMap), "Open")]
    internal static class AutoLoadLastSavePatch
    {
        private static readonly FieldInfo ReadyToStartField =
            AccessTools.Field(typeof(MetagameMap), "<IsReadyToStart>k__BackingField");
        private static bool _attempted;

        private static bool Prefix(MetagameMap __instance)
        {
            if (_attempted || !UnderPressurePlugin.ShouldAutoLoadLastSave)
                return true;

            _attempted = true;

            var app = __instance?.App;
            var saveSystem = app?.SaveSystem;
            var metagame = __instance?.Metagame;
            var stateMachine = __instance?.StateMachine;
            if (app == null || saveSystem == null || metagame == null || stateMachine == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: la carrera terminó de cargar sin todos los estados necesarios.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            var saveHeader = SkipFrontEndForAutoLoadPatch.SelectedLevelSave;
            if (saveHeader == null || string.IsNullOrEmpty(saveHeader.LevelID))
            {
                UnderPressurePlugin.Log.LogInfo(
                    "Autocarga omitida: la ranura de carrera no contiene hospitales guardados.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            var levelConfig = metagame.LevelList?.GetLevelConfigByID(saveHeader.LevelID);
            if (levelConfig == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"Autocarga cancelada: no existe LevelConfig para el nivel guardado '{saveHeader.LevelID}'.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            if (app.ShowMessageBoxIfSaveHeaderCantLoad(saveHeader, levelConfig))
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"Autocarga cancelada: la cabecera del nivel '{saveHeader.LevelID}' no se puede cargar.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            var stateData = stateMachine.GetStateMachineData<MetagameStateData>();
            if (stateData == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "Autocarga cancelada: no está disponible MetagameStateData.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                return true;
            }

            stateData.LoadLevel = levelConfig;
            stateData.OnLoadRestartLevel = false;
            stateData.OnLoadSaveOldLevel = true;
            app.LoadSaveProgressScreen.Show(levelConfig);
            __instance.RootTransform.gameObject.SetActive(false);
            app.MetagameMapScene.RootObject.SetActive(false);
            ReadyToStartField.SetValue(__instance, true);
            __instance.StateMachine.Update();
            SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, false);

            UnderPressurePlugin.Log.LogInfo(
                $"Carga directa preparada para {saveHeader.LevelID}; se cancela la apertura del mapa de campaña.");
            return false;
        }
    }

    [HarmonyPatch(typeof(SplashScreen), "StartIntro")]
    internal static class SkipIntroScreensPatch
    {
        private static bool Prefix(SplashScreen __instance, ref IEnumerator __result)
        {
            if (!UnderPressurePlugin.ShouldSkipIntroScreens)
                return true;

            __result = LoadMainDirectly(__instance);
            return false;
        }

        private static IEnumerator LoadMainDirectly(SplashScreen screen)
        {
            SetActive(screen, "_segaIdentVideo", false);
            SetActive(screen, "_twoPointSplash", false);
            SetActive(screen, "_legalSplash", false);
            SetActive(screen, "_masterFadeObject", false);
            SetActive(screen, "_splashFadeObject", false);
            SetActive(screen, "_loadingIcon", true);

            UnityEngine.Debug.Log("[UnderPressure] Pantallas iniciales omitidas. Cargando escena Main.");
            yield return null;

            var operation = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Additive);
            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f)
                yield return null;

            operation.allowSceneActivation = true;
            yield return operation;

            SetActive(screen, "_camera", false);
            SetActive(screen, "_loadingIcon", false);
            SceneManager.UnloadSceneAsync("SplashScreen");
        }

        private static void SetActive(SplashScreen screen, string fieldName, bool active)
        {
            var value = AccessTools.Field(typeof(SplashScreen), fieldName)?.GetValue(screen);
            if (value is Component component)
                component.gameObject.SetActive(active);
            else if (value is GameObject gameObject)
                gameObject.SetActive(active);
        }
    }


    internal static class StartupDirectHospital
    {
        private static readonly FieldInfo GameModeApp =
            AccessTools.Field(typeof(GameMode), "<App>k__BackingField");
        private static readonly FieldInfo GameModeMap =
            AccessTools.Field(typeof(GameMode), "<MetagameMap>k__BackingField");
        private static readonly FieldInfo LevelMap =
            AccessTools.Field(typeof(Level), "_metagameMap");
        private static readonly MethodInfo NativeLoadInner =
            AccessTools.Method(typeof(GameMode), "LoadAsyncInner");
        private static readonly FieldInfo AutoLoadAlreadyAttempted =
            AccessTools.Field(typeof(AutoLoadLastSavePatch), "_attempted");
        private static readonly FieldInfo GlobalEvents =
            AccessTools.Field(typeof(GameEventsRegistry), "_globalEvents");
        private static readonly FieldInfo AudioMap =
            AccessTools.Field(typeof(AudioListenerManager), "_metagameMap");
        // OpeningScreen.StartGame oculta este contenido antes de cargar Carrera.
        // La ruta directa omite StartGame y debe reproducir SOLO esa limpieza.
        private static readonly FieldInfo OpeningSceneContent =
            AccessTools.Field(typeof(OpeningScreen), "_sceneContent");

        private static MetagameMap _bridge;
        private static MetagameStateInHospital _hospitalState;
        private static GameMode _mode;
        private static bool _launchClaimed;
        private static bool _openingCareer;
        private static bool _levelLoaded;
        private static bool _requestCareer;

        internal static bool IsBridge(MetagameMap map) =>
            map != null && ReferenceEquals(map, _bridge);

        internal static bool BridgeExists => _bridge != null;

        internal static void SetHospitalState(LevelConfig config)
        {
            if (_bridge == null || config == null)
                throw new InvalidOperationException("No hay puente de Carrera para el hospital.");
            _hospitalState = new MetagameStateInHospital(_bridge, config, false, false);
            _bridge.StateMachine.PushState(_hospitalState);
        }

        internal static bool IsHospitalState(MetagameStateInHospital state) =>
            state != null && ReferenceEquals(state, _hospitalState);

        internal static bool TryIntercept(GameMode mode, bool ignoreSave, int slot,
            out IEnumerator replacement)
        {
            replacement = null;
            if (_launchClaimed || ignoreSave || !(mode is GameModeCareer) ||
                !UnderPressurePlugin.ShouldAutoLoadLastSave ||
                !SkipFrontEndForAutoLoadPatch.KeepLoadingScreenVisible ||
                SkipFrontEndForAutoLoadPatch.SelectedLevelSave == null ||
                slot != SkipFrontEndForAutoLoadPatch.SelectedSaveSlot)
                return false;
            _launchClaimed = true;
            replacement = LoadDirect(mode, slot);
            return true;
        }

        private static IEnumerator LoadDirect(GameMode mode, int slot)
        {
            var app = GameModeApp?.GetValue(mode) as App;
            var header = SkipFrontEndForAutoLoadPatch.SelectedLevelSave;
            if (app == null || header == null || NativeLoadInner == null || GameModeMap == null)
            {
                UnderPressurePlugin.Log?.LogWarning(
                    "Autocarga directa: faltan dependencias; se utiliza la carga nativa.");
                if (app != null) yield return OriginalCareerLoad(mode, slot);
                yield break;
            }

            // Esta escena contiene exclusivamente el equipamiento comun de los
            // hospitales: no se carga GetMetagameSceneName() (mapa de Carrera).
            UnderPressurePlugin.Log?.LogInfo(
                "Autocarga directa: preparando datos y hospital " + header.LevelID +
                " sin cargar la escena de Carrera.");
            app.LoadSaveProgressScreen.Show(null);
            yield return app.LoadLevelCommon();

            LevelConfig config = null;
            bool ready = false;
            try
            {
                // Esta llamada restaura los datos de la campana desde su save;
                // NO llama a SceneManager.LoadSceneAsync.
                mode.LoadMetagame(false, slot);
                config = mode.Metagame?.LevelList?.GetLevelConfigByID(header.LevelID);
                if (config != null && !header.IsBroken &&
                    !app.ShowMessageBoxIfSaveHeaderCantLoad(header, config))
                {
                    CreateBridge(mode, app);
                    ready = true;
                }
            }
            catch (Exception error)
            {
                UnderPressurePlugin.Log?.LogError(
                    "Autocarga directa: error al preparar el hospital: " + error);
                DestroyBridge();
            }
            if (!ready)
            {
                UnderPressurePlugin.Log?.LogWarning(
                    "Autocarga directa: no se puede continuar; se recupera el flujo original.");
                yield return OriginalCareerLoad(mode, slot);
                yield break;
            }

            // La ruta nativa desactiva primero OpeningScreen._sceneContent y,
            // al cerrar el mapa de Carrera, MetagameMapScene.RootObject.
            // Si no se hace, sus cámaras/proyectores pueden seguir dibujando
            // encima de los objetos del hospital ya restaurado desde el .sav.
            HideFrontendAndMetagameVisuals(app);

            _levelLoaded = false;
            _requestCareer = false;
            Action<Level, bool> loaded = (level, restored) =>
            {
                if (level != null && level.Config != null &&
                    level.Config.UniqueId == header.LevelID)
                    _levelLoaded = true;
            };
            app.OnLevelLoaded += loaded;
            bool loadRequestFailed = false;
            try
            {
                // App.Load conserva el reconocimiento de guardados de mapas personalizados.
                app.Load(header);
            }
            catch (Exception error)
            {
                app.OnLevelLoaded -= loaded;
                UnderPressurePlugin.Log?.LogError(
                    "Autocarga directa: fallo al solicitar el save: " + error);
                DestroyBridge();
                loadRequestFailed = true;
            }
            if (loadRequestFailed)
            {
                yield return OriginalCareerLoad(mode, slot);
                yield break;
            }

            var start = Time.realtimeSinceStartup;
            while (!_levelLoaded && !_requestCareer &&
                   Time.realtimeSinceStartup - start < 120f)
            {
                // Si App.Load ha rechazado la cabecera antes de iniciar la
                // carga, no esperar dos minutos a un evento imposible.
                if (Time.realtimeSinceStartup - start > 2f &&
                    !app.IsLoading && app.Level == null)
                    break;
                yield return null;
            }
            app.OnLevelLoaded -= loaded;

            if (_levelLoaded)
                HideFrontendAndMetagameVisuals(app);

            if (!_levelLoaded)
            {
                UnderPressurePlugin.Log?.LogError(
                    "Autocarga directa: el hospital no se ha restaurado. " +
                    "No se crea ningun guardado nuevo ni se sobrescribe el anterior.");
                SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
                if (!_openingCareer)
                    app.StartCoroutine(LoadCareerOnDemand(app));
                yield break;
            }

            try
            {
                SetHospitalState(config);
                AutoLoadAlreadyAttempted?.SetValue(null, true);
                UnderPressurePlugin.Log?.LogInfo(
                    "Autocarga directa terminada: hospital " + header.LevelID +
                    " restaurado; escena de Carrera NO cargada.");
            }
            catch (Exception error)
            {
                UnderPressurePlugin.Log?.LogError(
                    "Autocarga directa: error al preparar carga rapida: " + error);
            }
            SkipFrontEndForAutoLoadPatch.ReleaseLoadingScreen(app, true);
        }

        // Reproduce el estado VISUAL del flujo nativo tras entrar en un nivel:
        // no se instancia, activa ni carga la escena de Carrera.
        internal static void HideFrontendAndMetagameVisuals(App app)
        {
            if (app == null) return;
            var opening = app.OpeningScreen;
            var content = opening == null ? null :
                OpeningSceneContent?.GetValue(opening) as Transform;
            if (content != null && content.gameObject.activeSelf)
            {
                content.gameObject.SetActive(false);
                UnderPressurePlugin.Log?.LogInfo(
                    "Autocarga directa: desactivado contenido visual del menu principal.");
            }

            var scene = app.MetagameMapScene;
            var mapRoot = scene?.RootObject;
            if (mapRoot != null && mapRoot.activeSelf)
            {
                mapRoot.SetActive(false);
                UnderPressurePlugin.Log?.LogInfo(
                    "Autocarga directa: desactivado RootObject visual de Carrera sin cargar su escena.");
            }

            if (app.Level != null)
            {
                var hospitalCamera = app.Level.CameraLogic?.CameraComponent;
                UnderPressurePlugin.Log?.LogInfo(
                    "Autocarga directa: camara del hospital=" +
                    (hospitalCamera == null ? "ausente" :
                        (hospitalCamera.isActiveAndEnabled ? "activa" : "inactiva")) +
                    ", escena Carrera cargada=" +
                    SceneManager.GetSceneByName(_mode.GetMetagameSceneName()).isLoaded + ".");
            }
        }

        private static IEnumerator OriginalCareerLoad(GameMode mode, int slot)
        {
            var original = NativeLoadInner?.Invoke(mode, new object[] { false, slot }) as IEnumerator;
            if (original != null)
                yield return original;
            else
                UnderPressurePlugin.Log?.LogError(
                    "Autocarga directa: no se encontro la ruta nativa de recuperacion.");
        }

        internal static void CreateBridge(GameMode mode, App app)
        {
            if (GameModeMap == null || app.LevelCommonScript == null ||
                app.MetagameMapScene == null)
                throw new InvalidOperationException("El cargador comun no esta inicializado.");

            var gameObject = new GameObject("UP Direct Save - Career Bridge");
            var visibleRoot = new GameObject("UP Unused Career Visuals");
            visibleRoot.transform.SetParent(gameObject.transform, false);
            visibleRoot.SetActive(false);
            var map = gameObject.AddComponent<MetagameMap>();
            map.enabled = false; // No actualizar ni dibujar el mapa de Carrera.
            try
            {
                SetMapField(map, "_app", app);
                SetMapField(map, "_saveSystem", app.SaveSystem);
                SetMapField(map, "_mapScene", app.MetagameMapScene);
                SetMapField(map, "_rootObject", visibleRoot.transform);
                SetMapField(map, "_inputManager", app.InputManager);
                SetMapField(map, "_menusTransform", app.LevelCommonScript.MenusTransform);
                SetMapField(map, "_inWorldTransform", app.LevelCommonScript.InWorldTransform);
                SetMapField(map, "_metagameSceneName", mode.GetMetagameSceneName());
                SetMapField(map, "_stateMachine", new StateMachine(new MetagameStateData()));
                GameModeMap.SetValue(mode, map);
                _mode = mode;
                _bridge = map;
            }
            catch
            {
                RemoveFromGlobalEvents(map);
                UnityEngine.Object.Destroy(gameObject);
                throw;
            }
        }

        private static void SetMapField(MetagameMap map, string name, object value)
        {
            var field = AccessTools.Field(typeof(MetagameMap), name);
            if (field == null) throw new MissingFieldException(typeof(MetagameMap).FullName, name);
            field.SetValue(map, value);
        }

        private static void RemoveFromGlobalEvents(MetagameMap map)
        {
            var events = GlobalEvents?.GetValue(null) as List<IGameEventsBase>;
            events?.Remove(map);
        }

        internal static void DestroyBridge()
        {
            if (_bridge == null) return;
            var old = _bridge;
            _bridge = null;
            _hospitalState = null;
            _requestCareer = false;
            RemoveFromGlobalEvents(old);
            try
            {
                old.StateMachine?.Destroy();
            }
            catch (Exception error)
            {
                UnderPressurePlugin.Log?.LogWarning(
                    "Autocarga directa: limpieza de estados: " + error.Message);
            }
            if (old.gameObject != null)
                UnityEngine.Object.Destroy(old.gameObject);
        }

        internal static void RequestCareer(MetagameMap map)
        {
            if (!IsBridge(map) || _openingCareer) return;
            _requestCareer = true;
            var app = GameModeApp?.GetValue(_mode) as App;
            if (app != null)
                app.StartCoroutine(LoadCareerOnDemand(app));
        }

        // Solo se carga Carrera si el usuario PIDE regresar a su mapa.
        private static IEnumerator LoadCareerOnDemand(App app)
        {
            if (_openingCareer || _mode == null || _bridge == null) yield break;
            _openingCareer = true;
            var sceneName = _mode.GetMetagameSceneName();
            UnderPressurePlugin.Log?.LogInfo(
                "Autocarga directa: el usuario solicita Carrera; cargando su escena ahora.");
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (operation == null)
            {
                _openingCareer = false;
                app.QuitToMenu();
                yield break;
            }
            yield return operation;

            try
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                MetagameMap nativeMap = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    nativeMap = root.GetComponentInChildren<MetagameMap>(true);
                    if (nativeMap != null) break;
                }
                if (nativeMap == null)
                    throw new InvalidOperationException("La escena no contiene MetagameMap.");
                nativeMap.Initialise(app, app.MetagameMapScene, sceneName,
                    app.InputManager, app.LevelCommonScript.MenusTransform,
                    app.LevelCommonScript.InWorldTransform, app.SaveSystem,
                    app.UserPreferences, app.LocalPreferences);

                var currentLevel = app.Level;
                if (currentLevel != null)
                    nativeMap.InitialiseFromLevel(currentLevel,
                        currentLevel.GameTime, currentLevel.CameraLogic);

                // Mantener los callbacks de audio del hospital activo.
                nativeMap.OnOpen += _bridge.OnOpen;
                nativeMap.OnClose += _bridge.OnClose;
                if (currentLevel != null)
                {
                    LevelMap?.SetValue(currentLevel, nativeMap);
                    var listener = AudioListenerManager.Instance;
                    if (listener != null) AudioMap?.SetValue(listener, nativeMap);
                }
                GameModeMap.SetValue(_mode, nativeMap);
                DestroyBridge();
                _openingCareer = false;
                nativeMap.Open();
            }
            catch (Exception error)
            {
                _openingCareer = false;
                UnderPressurePlugin.Log?.LogError(
                    "Autocarga directa: error al abrir Carrera a peticion: " + error);
                // No dejar un estado intermedio inutilizable: volver al menu
                // guardando primero a traves de la llamada nativa.
                app.QuitToMenu();
            }
        }
    }

    // ===== INICIO: carga directa del editor de escenarios =====
    // No se modifica UnderPressure.Editor ni la logica del selector: solo se
    // evita la escena de Carrera al ejecutar la peticion ya seleccionada.
    internal static class StartupDirectScenarioEditor
    {
        private static readonly FieldInfo GameModeApp =
            AccessTools.Field(typeof(GameMode), "<App>k__BackingField");
        private static readonly MethodInfo NativeLoadInner =
            AccessTools.Method(typeof(GameMode), "LoadAsyncInner");
        private static bool _claimed;

        // Base nunca referencia estaticamente la DLL del editor: puede estar
        // desactivada o no instalada. Reconocer solo la seleccion en curso.
        private static bool TryGetEditorSelection(out LevelConfig selected)
        {
            selected = null;
            var flow = AccessTools.TypeByName("UnderPressure.Editor.ScenarioEditorFlow");
            if (flow == null) return false;
            var state = AccessTools.Field(flow, "_state");
            var level = AccessTools.Field(flow, "_selectedLevel");
            if (state == null || level == null ||
                !string.Equals(state.GetValue(null)?.ToString(), "Loading",
                    StringComparison.Ordinal))
                return false;
            selected = level.GetValue(null) as LevelConfig;
            return selected != null;
        }

        internal static bool TryIntercept(GameMode mode, bool ignoreSave,
            int saveSlotIndex, out IEnumerator replacement)
        {
            replacement = null;
            if (_claimed || !ignoreSave || saveSlotIndex != 0 ||
                !(mode is GameModeCareer) || StartupDirectHospital.BridgeExists)
                return false;
            LevelConfig selected;
            if (!TryGetEditorSelection(out selected) || GameModeApp == null ||
                NativeLoadInner == null)
                return false;
            _claimed = true;
            replacement = LoadSelectedEditorLevel(mode, saveSlotIndex, selected);
            return true;
        }

        private static IEnumerator NativeEditorLoad(GameMode mode, int slot)
        {
            // Ruta de recuperacion normal: incluye Carrera solamente si la
            // carga directa no se ha podido preparar.
            var iterator = NativeLoadInner?.Invoke(mode,
                new object[] { true, slot }) as IEnumerator;
            if (iterator != null)
                yield return iterator;
            else
                UnderPressurePlugin.Log?.LogError(
                    "Editor directo: falta el cargador nativo de recuperacion.");
        }

        private static IEnumerator LoadSelectedEditorLevel(
            GameMode mode, int slot, LevelConfig selected)
        {
            var app = GameModeApp?.GetValue(mode) as App;
            if (app == null || selected == null || app.SaveSystem == null)
            {
                UnderPressurePlugin.Log?.LogWarning(
                    "Editor directo: no hay App o nivel; se usa el flujo nativo.");
                _claimed = false;
                yield return NativeEditorLoad(mode, slot);
                yield break;
            }

            // A diferencia de Cargar ultimo nivel, el editor SIEMPRE comienza
            // con el selector del menu principal. Solo llegamos aqui despues
            // de que ScenarioEditorFlow haya recibido el LevelConfig elegido.
            UnderPressurePlugin.Log?.LogInfo(
                "Editor directo: preparando " + selected.UniqueId +
                " sin escena de Carrera (guardados del hospital ignorados).");
            app.LoadSaveProgressScreen.Show(null);
            yield return app.LoadLevelCommon();

            bool prepared = false;
            try
            {
                // Es la MISMA metapartida temporal que se inicializaba antes,
                // pero sin GameMode.LoadAsyncInner ni SceneManager.LoadSceneAsync
                // del mapa visual de Carrera.
                mode.LoadMetagame(true, slot);
                if (mode.Metagame == null)
                    throw new InvalidOperationException(
                        "No se ha inicializado la metapartida temporal del editor.");
                StartupDirectHospital.CreateBridge(mode, app);
                prepared = true;
            }
            catch (Exception error)
            {
                UnderPressurePlugin.Log?.LogError(
                    "Editor directo: error de inicializacion: " + error);
                StartupDirectHospital.DestroyBridge();
            }
            if (!prepared)
            {
                // LevelCommon es reutilizable: evita cargarlo dos veces.
                _claimed = false;
                yield return NativeEditorLoad(mode, slot);
                yield break;
            }

            StartupDirectHospital.HideFrontendAndMetagameVisuals(app);

            bool levelLoaded = false;
            Action<Level, bool> onLoaded = (level, restored) =>
            {
                if (level != null && level.Config != null &&
                    string.Equals(level.Config.UniqueId, selected.UniqueId,
                        StringComparison.Ordinal))
                    levelLoaded = true;
            };
            app.OnLevelLoaded += onLoaded;
            bool requestFailed = false;
            try
            {
                // El editor siempre abre el nivel ORIGINAL. El config.json pendiente
                // se aplica despues mediante ScenarioEditorMapPicker.OnEditorOpened.
                // El patch ScenarioEditorIgnoreExistingLevelSavePatch permanece
                // operativo; no se invoca App.Load con saves de otras partidas.
                app.LoadLevel(selected, null, true, false);
            }
            catch (Exception error)
            {
                UnderPressurePlugin.Log?.LogError(
                    "Editor directo: no se pudo iniciar el nivel: " + error);
                requestFailed = true;
            }
            if (requestFailed)
            {
                app.OnLevelLoaded -= onLoaded;
                StartupDirectHospital.DestroyBridge();
                _claimed = false;
                yield return NativeEditorLoad(mode, slot);
                yield break;
            }

            float startedAt = Time.realtimeSinceStartup;
            while ((!levelLoaded || app.IsLoading) &&
                   Time.realtimeSinceStartup - startedAt < 120f)
            {
                if (!levelLoaded && Time.realtimeSinceStartup - startedAt > 2f &&
                    !app.IsLoading && app.Level == null)
                    break;
                yield return null;
            }
            app.OnLevelLoaded -= onLoaded;

            if (!levelLoaded || app.Level == null)
            {
                UnderPressurePlugin.Log?.LogError(
                    "Editor directo: el nivel no se ha abierto. " +
                    "No se guardaran datos del hospital ni se borraran partidas.");
                _claimed = false;
                if (!app.IsLoading)
                    app.QuitToMenuDontSave();
                yield break;
            }

            // Mantener un estado compatible con las funciones de Level, pero
            // impedir su Enter nativo: la carga ya esta completada.
            try
            {
                StartupDirectHospital.SetHospitalState(selected);
            }
            catch (Exception error)
            {
                UnderPressurePlugin.Log?.LogWarning(
                    "Editor directo: estado de retorno no inicializado: " + error);
            }
            StartupDirectHospital.HideFrontendAndMetagameVisuals(app);
            UnderPressurePlugin.Log?.LogInfo(
                "Editor directo: nivel " + selected.UniqueId +
                " abierto sin cargar Carrera. El config.json lo administra el editor.");
            _claimed = false;
        }
    }
    // ===== FIN: carga directa del editor de escenarios =====

    // Anula exclusivamente la carga inicial de GameModeCareer.LoadAsync,
    // para iniciar primero datos de campana y despues el .sav del hospital.
    [HarmonyPatch(typeof(GameMode), "LoadAsync", new[] { typeof(bool), typeof(int) })]
    internal static class StartupDirectHospitalLoadPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GameMode __instance, bool ignoreSave, int saveSlotIndex,
            ref IEnumerator __result)
        {
            IEnumerator replacement;
            if (!StartupDirectScenarioEditor.TryIntercept(__instance, ignoreSave,
                    saveSlotIndex, out replacement) &&
                !StartupDirectHospital.TryIntercept(__instance, ignoreSave,
                    saveSlotIndex, out replacement)) return true;
            __result = replacement;
            return false;
        }
    }

    [HarmonyPatch(typeof(MetagameStateInHospital), "Enter")]
    internal static class StartupDirectHospitalStatePatch
    {
        private static bool Prefix(MetagameStateInHospital __instance) =>
            !StartupDirectHospital.IsHospitalState(__instance);
    }

    [HarmonyPatch(typeof(MetagameMap), "Open")]
    internal static class StartupDirectHospitalReturnPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(MetagameMap __instance)
        {
            if (!StartupDirectHospital.IsBridge(__instance)) return true;
            StartupDirectHospital.RequestCareer(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(MetagameMap), "Uninitialise")]
    internal static class StartupDirectHospitalUninitialisePatch
    {
        private static bool Prefix(MetagameMap __instance) =>
            !StartupDirectHospital.IsBridge(__instance);
    }

    [HarmonyPatch(typeof(MetagameMap), "Destroy")]
    internal static class StartupDirectHospitalDestroyPatch
    {
        private static bool Prefix(MetagameMap __instance)
        {
            if (!StartupDirectHospital.IsBridge(__instance)) return true;
            StartupDirectHospital.DestroyBridge();
            return false;
        }
    }
}
