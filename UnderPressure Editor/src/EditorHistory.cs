// Updated: 2026-10-15 - Historial tras acciones confirmadas; seleccionar no registra eliminaciones.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    // Historial exclusivo del editor de escenarios. No afecta al juego normal
    // ni modifica los archivos .upmap hasta que el usuario elige Guardar.
    internal sealed class ScenarioEditorHistory : MonoBehaviour
    {
        private const int MaxChanges = 40;
        // No restaurar fotografias que impliquen reemplazar centenas de objetos.
        // Las operaciones de grandes lotes requieren una transaccion nativa propia.
        private const int MaxObjectsPerStep = 16;
        private const float CaptureInterval = 0.22f;
        private const float PositionEpsilonSquared = 0.0004f;
        private const float RotationEpsilon = 0.05f;
        private static readonly FieldInfo LandscapeCarryingField =
            AccessTools.Field(typeof(ScenarioLandscape), "_carrying");
        private static readonly FieldInfo LandscapeEditedField =
            AccessTools.Field(typeof(ScenarioLandscape), "Edited");
        private static readonly FieldInfo SceneSourceChangesField =
            AccessTools.Field(typeof(ScenarioEditorScene), "SourceChanges");
        private static readonly MethodInfo LandscapeRebuild =
            AccessTools.Method(typeof(ScenarioLandscape), "RebuildSelectables");
        private static readonly MethodInfo LandscapeApply =
            AccessTools.Method(typeof(ScenarioLandscape), "ApplySavedPositions");
        private static readonly FieldInfo CameraBoundField =
            AccessTools.Field(typeof(TopDownCameraLogic), "_boundCamera");

        private static ScenarioEditorHistory _active;
        private readonly List<Change> _changes = new List<Change>();
        private readonly Dictionary<LandscapeRoomItem, int> _nativeIds =
            new Dictionary<LandscapeRoomItem, int>();
        private readonly Dictionary<ScenarioEditorScene.SceneObject, int> _sceneIds =
            new Dictionary<ScenarioEditorScene.SceneObject, int>();
        private int _nextId = 1;
        private int _position;
        private int _initialFrames;
        private float _nextCapture;
        private bool _pendingChanges;
        private Level _level;
        private State _baseline;
        private HospitalPlot[] _plotIdentity;
        private Vector3[] _plotCenters;
        private bool _applying;
        private bool _initialVisibilityPending = true;
        private float _ignoreChangesUntil;
        private GameObject _historyUi;
        private RectTransform _historyPanel;
        private RectTransform _historyContent;
        private ScrollRect _historyScroll;
        private Text _historyTitle;
        private Font _historyFont;
        private static bool _dockVisible;
        private static float _dockWidth = ScenarioEditorDockLayout.PanelWidth;
        private static float _dockHeight = 320f;
        private static float _dockTop = ScenarioEditorDockLayout.BarHeight;

        internal static void SetDockVisible(bool visible)
        {
            _dockVisible = visible;
            if (_active?._historyUi != null) _active._historyUi.SetActive(visible);
        }

        internal static void SetDockRect(float width, float height, float top)
        {
            _dockWidth = width;
            _dockHeight = Mathf.Max(0f, height);
            _dockTop = top;
            _active?.ApplyDockRect();
        }

        internal static void SetDockStyle(Sprite sprite, Color color)
        {
            if (_active?._historyPanel == null) return;
            var image = _active._historyPanel.GetComponent<Image>();
            if (image == null) return;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            color.a = 1f;
            image.color = color;
        }

        private static string T(string key) => EditorLocalization.Get(key);

        internal static bool Ready => _active != null && _active._baseline != null &&
            !_active._applying && _active.IsValid();

        internal static void StartHistory(Level level)
        {
            if (level == null || !ScenarioEditorFlow.IsRunning) return;
            if (_active != null)
            {
                if (ReferenceEquals(_active._level, level)) return;
                Destroy(_active.gameObject);
                _active = null;
            }
            var go = new GameObject("UP Editor - Historial");
            var editorMenu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            var canvas = editorMenu == null ? null : editorMenu.GetComponentInParent<Canvas>();
            ScenarioEditorUiHierarchy.Attach(go, canvas);
            var history = go.AddComponent<ScenarioEditorHistory>();
            history._level = level;
            _active = history;
            history._ignoreChangesUntil = Time.unscaledTime + 1.5f;
            // No instalar eventos nativos: SetLayerVisible regenera items y no debe
            // disparar capturas de historial. Solo se observan gestos del usuario.
        }

        private bool IsValid() => _level != null && ScenarioEditorFlow.IsRunning &&
            ScenarioEditorSession.Active && ReferenceEquals(ScenarioEditorSession.Level, _level) &&
            _level.WorldState != null;

        private void OnDestroy()
        {
            if (ReferenceEquals(_active, this)) _active = null;
        }

        // Solo en acciones confirmadas: nada de comprobaciones cada segundo.
        internal static void ObjectDropped()
        {
            var history = _active;
            if (history == null || !history.IsValid() || history._applying) return;
            history._pendingChanges = true;
            history._nextCapture = Time.unscaledTime + CaptureInterval;
        }

        internal static void ResetAfterMapLoad()
        {
            if (_active != null) _active.ResetHistory();
        }

        internal static void StopHistory()
        {
            var active = _active;
            _active = null;
            if (active == null || active.gameObject == null) return;
            active.gameObject.SetActive(false);
            Destroy(active.gameObject);
        }

        private void ResetHistory()
        {
            _changes.Clear();
            _position = 0;
            _baseline = null;
            _plotIdentity = null;
            _plotCenters = null;
            _nativeIds.Clear();
            _sceneIds.Clear();
            _nextId = 1;
            _initialFrames = 0;
            _nextCapture = Time.unscaledTime + CaptureInterval;
            _pendingChanges = false;
            // Aplicar la configuracion inicial a las NUEVAS parcelas del mapa.
            _initialVisibilityPending = true;
            _ignoreChangesUntil = Time.unscaledTime + 1.5f;
            RefreshHistoryUI();
        }

        private void InitialiseVisibleLayers()
        {
            if (!_initialVisibilityPending || !IsValid()) return;
            _initialVisibilityPending = false;
            var plots = _level.WorldState.HospitalPlots;
            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (plot == null) continue;
                // Mantener TODAS las parcelas habilitadas: la seleccion es
                // independiente de la casilla de visibilidad de cada parcela.
                if (!plot.IsVisible()) plot.SetVisible(true);
                if (!plot.IsLayerVisible(HospitalPlotLayer.Base))
                    plot.SetLayerVisible(HospitalPlotLayer.Base, true);
                if (!plot.IsLayerVisible(HospitalPlotLayer.Built))
                    plot.SetLayerVisible(HospitalPlotLayer.Built, true);
                if (plot.IsLayerVisible(HospitalPlotLayer.Unbuilt))
                    plot.SetLayerVisible(HospitalPlotLayer.Unbuilt, false);
            }
            if (plots.Count > 0 && plots[0] != null)
                _level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(plots[0]);
            EnableFreeCamera();
            SynchronisePlotChecks(); // Solo una vez al abrir el mapa.
        }

        private void EnableFreeCamera()
        {
            var camera = _level?.CameraLogic;
            if (camera == null || CameraBoundField == null) return;
            try
            {
                var bounded = CameraBoundField.GetValue(camera);
                if (bounded is bool && (bool)bounded)
                    camera.ToggleCameraBounds();
            }
            catch (Exception e)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Editor: no se pudo activar Camara libre: " + e.Message);
            }
        }

        private static readonly string[] LayerToggleFields = {
            "_toggleLayerBase", "_toggleLayerBuilt", "_toggleLayerUnbuilt"
        };
        private static readonly HospitalPlotLayer[] Layers = {
            HospitalPlotLayer.Base, HospitalPlotLayer.Built, HospitalPlotLayer.Unbuilt
        };
        private static readonly FieldInfo PlotSelectionPlotField =
            AccessTools.Field(typeof(PlotSelectionItem), "_plot");
        private static readonly FieldInfo PlotVisibleToggleField =
            AccessTools.Field(typeof(PlotSelectionItem), "_toggleVisible");
        private static readonly FieldInfo[] PlotLayerToggleFields = LayerToggleFields
            .Select(name => AccessTools.Field(typeof(PlotSelectionItem), name)).ToArray();

        private static void SynchronisePlotChecks()
        {
            foreach (var row in UnityEngine.Object.FindObjectsOfType<PlotSelectionItem>())
            {
                var plot = PlotSelectionPlotField?.GetValue(row) as HospitalPlot;
                if (plot == null) continue;
                var master = PlotVisibleToggleField?.GetValue(row) as Toggle;
                if (master != null) master.SetIsOnWithoutNotify(plot.IsVisible());
                for (int i = 0; i < Layers.Length; ++i)
                {
                    var toggle = PlotLayerToggleFields[i]?.GetValue(row) as Toggle;
                    if (toggle != null)
                        toggle.SetIsOnWithoutNotify(plot.IsLayerVisible(Layers[i]));
                }
            }
        }

        // No recorrer miles de GameObjects al activar o desactivar capas:
        // su representacion pertenece a los controles nativos del FloorPlan.

        private bool PlotsChanged()
        {
            var plots = _level.WorldState.HospitalPlots;
            if (_plotIdentity == null || _plotIdentity.Length != plots.Count) return true;
            for (int i = 0; i < plots.Count; ++i)
            {
                if (!ReferenceEquals(_plotIdentity[i], plots[i])) return true;
                var floor = plots[i]?.HospitalMap?.FloorPlan;
                if (floor != null && _plotCenters != null &&
                    (floor.WorldBounds.Center.ToWorldPosition() - _plotCenters[i]).sqrMagnitude > .0001f)
                    return true;
            }
            return false;
        }

        private void Update()
        {
            if (!IsValid())
            {
                if (_level != null && !ScenarioEditorFlow.IsRunning) Destroy(gameObject);
                return;
            }
            // OnEditorOpened aplica el .upmap en otro Postfix: esperar a que
            // termine y NO crear un paso falso contra el mapa original.
            if (_baseline == null)
            {
                if (++_initialFrames < 12 || IsBusy() ||
                    Time.unscaledTime < _ignoreChangesUntil) return;
                _plotIdentity = _level.WorldState.HospitalPlots.ToArray();
                _plotCenters = _plotIdentity.Select(plot =>
                    plot?.HospitalMap?.FloorPlan == null ? Vector3.zero :
                    plot.HospitalMap.FloorPlan.WorldBounds.Center.ToWorldPosition()).ToArray();
                InitialiseVisibleLayers();
                _baseline = CaptureState();
                _nextCapture = Time.unscaledTime + CaptureInterval;
                RefreshHistoryUI();
                return;
            }

            if (PlotsChanged())
            {
                // Una nueva lista de parcelas invalida las referencias de
                // objetos anteriores; no conservar operaciones peligrosas.
                ResetHistory();
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Historial: nueva estructura de parcelas; comienza un historial independiente.");
                return;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && !EditingText() && !IsBusy())
            {
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    if (shift) Redo(); else Undo();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    Redo();
                    return;
                }
            }
            // El clic solo inicia una seleccion. El cursor nativo retira
            // temporalmente el objeto de LandscapeItems mientras se edita;
            // fotografiar durante esa seleccion creaba falsas eliminaciones.
            // El historial se activa EXCLUSIVAMENTE al finalizar una accion
            // real, desde los parches de confirmacion situados al final.
            if (Time.unscaledTime < _ignoreChangesUntil)
            {
                _pendingChanges = false;
                return;
            }
            if (!_pendingChanges || IsBusy() || Time.unscaledTime < _nextCapture) return;
            _pendingChanges = false;
            _nextCapture = Time.unscaledTime + CaptureInterval;
            try
            {
                var state = CaptureState();
                if (!Equivalent(_baseline, state))
                {
                    var change = new StateChange(_baseline, state);
                    if (change.TouchedObjects > MaxObjectsPerStep)
                    {
                        // P. ej. 1642 objetos movidos al regenerar visuales:
                        // NO convertir esa reconstruccion en un paso de Undo.
                        ClearUnsafeHistory(state, "cambio masivo ajeno al historial (" +
                            change.TouchedObjects + " objetos)");
                        return;
                    }
                    if (change.TouchedObjects > 0)
                        Push(change, false);
                }
                _baseline = state;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Historial: no se pudo registrar el cambio: " + ex.Message);
            }
        }

        private bool IsBusy()
        {
            if (ScenarioEditorStorageMenu.IsDialogOpen || ScenarioEditorScene.IsDragging ||
                (LandscapeCarryingField != null &&
                 LandscapeCarryingField.GetValue(null) is bool carrying && carrying) ||
                Input.GetMouseButton(0) || Input.GetMouseButton(1)) return true;
            var cursor = _level?.CursorManager;
            return cursor != null &&
                (cursor.IsModeActive<CursorRoomMove>() ||
                 cursor.IsModeActive<CursorRoomItem>());
        }

        private static bool EditingText()
        {
            var current = EventSystem.current?.currentSelectedGameObject;
            return current != null &&
                (current.GetComponent<TMP_InputField>() != null ||
                 current.GetComponent<InputField>() != null);
        }

        private void ClearUnsafeHistory(State current, string reason)
        {
            _changes.Clear();
            _position = 0;
            _baseline = current;
            _pendingChanges = false;
            _nextCapture = Time.unscaledTime + CaptureInterval;
            RefreshHistoryUI();
            UnderPressureEditorPlugin.Log?.LogWarning(
                "Historial: estado descartado por seguridad: " + reason + ".");
        }

        private void Push(Change change, bool rescan)
        {
            if (change == null || _applying || _baseline == null) return;
            if (_position < _changes.Count)
                _changes.RemoveRange(_position, _changes.Count - _position);
            _changes.Add(change);
            if (_changes.Count > MaxChanges) _changes.RemoveAt(0);
            _position = _changes.Count;
            if (rescan) _baseline = CaptureState();
            _pendingChanges = false;
            _nextCapture = Time.unscaledTime + CaptureInterval;
            RefreshHistoryUI();
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Historial: " + change.Name + " (" + _position + "/" + _changes.Count + ").");
        }

        private void Undo()
        {
            if (_position == 0) return;
            ApplyChange(_changes[_position - 1], false);
        }

        private void Redo()
        {
            if (_position >= _changes.Count) return;
            ApplyChange(_changes[_position], true);
        }

        private void ApplyChange(Change action, bool forward)
        {
            if (action == null || !IsValid() || _baseline == null) return;
            // Un cambio externo sobre las parcelas invalida las referencias:
            // nunca restaurar destructivamente una captura que ya no coincide.
            if (action is StateChange stateChange)
            {
                State actual;
                try { actual = CaptureState(); }
                catch (Exception e)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Historial: Undo cancelado; imposible comprobar estado: " + e.Message);
                    return;
                }
                if (stateChange.TouchedObjects > MaxObjectsPerStep ||
                    !Equivalent(actual, stateChange.GetExpected(forward)))
                {
                    ClearUnsafeHistory(actual,
                        "los objetos ya no coinciden con el paso solicitado");
                    return;
                }
            }
            _applying = true;
            try
            {
                action.Apply(this, forward);
                _position += forward ? 1 : -1;
                _baseline = CaptureState();
                _pendingChanges = false;
                _nextCapture = Time.unscaledTime + CaptureInterval;
                RefreshHistoryUI();
                    UnderPressureEditorPlugin.Log?.LogInfo(
                    "Historial: " + (forward ? "rehacer " : "deshacer ") +
                    action.Name + " (" + _position + "/" + _changes.Count + ").");
            }
            catch (Exception ex)
            {
                // Nunca adelantar el indice si falló la restauracion.
                ResetHistory();
                UnderPressureEditorPlugin.Log?.LogError(
                    "Historial: operacion interrumpida y memoria de deshacer reiniciada: " + ex);
            }
            finally { _applying = false; }
        }

        private abstract class Change
        {
            internal abstract string Name { get; }
            internal abstract void Apply(ScenarioEditorHistory history, bool forward);
        }

        private sealed class NativeEntry
        {
            internal int Id, Plot;
            internal RoomItemDefinition Definition;
            internal HospitalPlotLayer Layer;
            internal Vector3 Position;
            internal float Rotation;
        }

        private sealed class SceneEntry
        {
            internal int Id;
            internal RoomItemDefinition Definition;
            internal string SourcePath;
            internal Vector3 BasePosition, Position;
            internal float BaseRotation, Rotation;
        }

        private sealed class State
        {
            internal readonly List<NativeEntry> Native = new List<NativeEntry>();
            internal readonly List<SceneEntry> Scene = new List<SceneEntry>();
            internal readonly Dictionary<string, ScenarioLandscape.ObjectEntry> Landscape =
                new Dictionary<string, ScenarioLandscape.ObjectEntry>(StringComparer.Ordinal);
            internal readonly Dictionary<string, ScenarioMapFormat.OriginalChange> Sources =
                new Dictionary<string, ScenarioMapFormat.OriginalChange>(StringComparer.Ordinal);
        }


        private static string NativeKey(int plot, RoomItemDefinition definition,
            HospitalPlotLayer layer, Vector3 position, float rotation)
        {
            // Clave logica: cambiar la visibilidad puede recrear instancias de
            // LandscapeRoomItem sin que el usuario haya modificado el objeto.
            return plot + ":" + RuntimeHelpers.GetHashCode(definition) + ":" +
                (int)layer + ":" + Mathf.RoundToInt(position.x * 100f) + ":" +
                Mathf.RoundToInt(position.y * 100f) + ":" +
                Mathf.RoundToInt(position.z * 100f) + ":" +
                Mathf.RoundToInt(Mathf.Repeat(rotation, 360f) * 10f);
        }

        private State CaptureState()
        {
            // Solo LEER los objetos: Capture() modifica el inventario de
            // origen y generaba operaciones ficticias al registrar el historial.
            var state = new State();
            var plots = _level.WorldState.HospitalPlots;
            // Los ids existentes se preservan siempre. Solo reconectar con la
            // posicion logica si Unity ha reemplazado la instancia del item.
            var liveIds = new HashSet<int>();
            foreach (var plot in plots)
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                    if (item != null && _nativeIds.TryGetValue(item, out var id))
                        liveIds.Add(id);
            }
            var candidates = new Dictionary<string, Queue<int>>();
            if (_baseline != null)
                foreach (var old in _baseline.Native)
                {
                    if (liveIds.Contains(old.Id)) continue;
                    var key = NativeKey(old.Plot, old.Definition, old.Layer,
                        old.Position, old.Rotation);
                    if (!candidates.TryGetValue(key, out var queue))
                        candidates[key] = queue = new Queue<int>();
                    queue.Enqueue(old.Id);
                }
            var freshIds = new Dictionary<LandscapeRoomItem, int>();
            for (int i = 0; i < plots.Count; ++i)
            {
                var items = plots[i]?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    var definition = item?.Definition as RoomItemDefinition;
                    if (definition == null) continue;
                    int id;
                    if (!_nativeIds.TryGetValue(item, out id))
                    {
                        var key = NativeKey(i, definition, item.Layer,
                            item.WorldPosition, item.Rotation);
                        if (candidates.TryGetValue(key, out var queue) && queue.Count > 0)
                            id = queue.Dequeue();
                        else id = _nextId++;
                    }
                    freshIds[item] = id;
                    state.Native.Add(new NativeEntry
                    {
                        Id = id, Plot = i, Definition = definition,
                        Layer = item.Layer, Position = item.WorldPosition,
                        Rotation = item.Rotation
                    });
                }
            }
            _nativeIds.Clear();
            foreach (var pair in freshIds) _nativeIds[pair.Key] = pair.Value;
            foreach (var entry in ScenarioEditorScene.SceneObjects)
            {
                if (entry?.Visual == null || entry.Definition == null) continue;
                int id;
                if (!_sceneIds.TryGetValue(entry, out id))
                    _sceneIds[entry] = id = _nextId++;
                state.Scene.Add(new SceneEntry
                {
                    Id = id, Definition = entry.Definition,
                    SourcePath = entry.SourcePath, BasePosition = entry.BasePosition,
                    BaseRotation = entry.BaseRotation,
                    Position = entry.Visual.transform.position,
                    Rotation = entry.Visual.transform.eulerAngles.y
                });
            }
            var edited = LandscapeEditedField?.GetValue(null) as
                Dictionary<string, ScenarioLandscape.ObjectEntry>;
            if (edited != null)
                foreach (var pair in edited)
                    state.Landscape.Add(pair.Key, CopyLandscape(pair.Value));
            var sources = SceneSourceChangesField?.GetValue(null) as
                Dictionary<string, ScenarioMapFormat.OriginalChange>;
            if (sources != null)
                foreach (var pair in sources)
                    state.Sources.Add(pair.Key, CopySource(pair.Value));
            return state;
        }

        private static ScenarioLandscape.ObjectEntry CopyLandscape(ScenarioLandscape.ObjectEntry e)
        {
            if (e == null) return null;
            // JsonUtility respeta los campos serializados de ObjectEntry y crea
            // objetos nuevos; evita referencias mutables entre dos cambios.
            return JsonUtility.FromJson<ScenarioLandscape.ObjectEntry>(JsonUtility.ToJson(e));
        }

        private static ScenarioMapFormat.OriginalChange CopySource(ScenarioMapFormat.OriginalChange e)
        {
            return e == null ? null :
                JsonUtility.FromJson<ScenarioMapFormat.OriginalChange>(JsonUtility.ToJson(e));
        }

        private static bool Equivalent(State a, State b)
        {
            if (a.Native.Count != b.Native.Count || a.Scene.Count != b.Scene.Count ||
                a.Landscape.Count != b.Landscape.Count || a.Sources.Count != b.Sources.Count)
                return false;
            // FloorPlan reordena LandscapeItems al mover o refrescar objetos.
            // Comparar por ID, NUNCA por indice de la lista.
            var natives = b.Native.ToDictionary(x => x.Id);
            foreach (var x in a.Native)
            {
                NativeEntry y;
                if (!natives.TryGetValue(x.Id, out y) || x.Plot != y.Plot ||
                    x.Layer != y.Layer || x.Definition != y.Definition ||
                    (x.Position - y.Position).sqrMagnitude > PositionEpsilonSquared ||
                    Mathf.Abs(Mathf.DeltaAngle(x.Rotation, y.Rotation)) > RotationEpsilon)
                    return false;
            }
            var scenes = b.Scene.ToDictionary(x => x.Id);
            foreach (var x in a.Scene)
            {
                SceneEntry y;
                if (!scenes.TryGetValue(x.Id, out y) || x.Definition != y.Definition ||
                    x.SourcePath != y.SourcePath ||
                    (x.Position - y.Position).sqrMagnitude > PositionEpsilonSquared ||
                    Mathf.Abs(Mathf.DeltaAngle(x.Rotation, y.Rotation)) > RotationEpsilon)
                    return false;
            }
            foreach (var item in a.Landscape)
            {
                ScenarioLandscape.ObjectEntry value;
                if (!b.Landscape.TryGetValue(item.Key, out value) ||
                    JsonUtility.ToJson(item.Value) != JsonUtility.ToJson(value)) return false;
            }
            foreach (var item in a.Sources)
            {
                ScenarioMapFormat.OriginalChange value;
                if (!b.Sources.TryGetValue(item.Key, out value) ||
                    JsonUtility.ToJson(item.Value) != JsonUtility.ToJson(value)) return false;
            }
            return true;
        }

        private sealed class StateChange : Change
        {
            private readonly State _before, _after;
            private readonly string _name;
            internal int TouchedObjects { get; private set; }
            internal State GetExpected(bool forward) => forward ? _before : _after;
            internal StateChange(State before, State after)
            {
                _before = before;
                _after = after;
                _name = DescribeChanges(before, after, out var touched);
                TouchedObjects = touched;
            }
            internal override string Name => _name;
            internal override void Apply(ScenarioEditorHistory history, bool forward)
            {
                history.RestoreState(forward ? _after : _before);
            }
        }

        private static string DescribeChanges(State before, State after, out int touched)
        {
            var oldNative = before.Native.ToDictionary(e => e.Id);
            var newNative = after.Native.ToDictionary(e => e.Id);
            var oldScene = before.Scene.ToDictionary(e => e.Id);
            var newScene = after.Scene.ToDictionary(e => e.Id);
            int placed = newNative.Keys.Count(id => !oldNative.ContainsKey(id)) +
                newScene.Keys.Count(id => !oldScene.ContainsKey(id));
            int deleted = oldNative.Keys.Count(id => !newNative.ContainsKey(id)) +
                oldScene.Keys.Count(id => !newScene.ContainsKey(id));
            int moved = 0;
            foreach (var e in after.Native)
            {
                NativeEntry old;
                if (oldNative.TryGetValue(e.Id, out old) &&
                    (old.Plot != e.Plot || old.Layer != e.Layer ||
                     (old.Position - e.Position).sqrMagnitude > PositionEpsilonSquared ||
                     Mathf.Abs(Mathf.DeltaAngle(old.Rotation, e.Rotation)) > RotationEpsilon))
                    ++moved;
            }
            foreach (var e in after.Scene)
            {
                SceneEntry old;
                if (oldScene.TryGetValue(e.Id, out old) &&
                    ((old.Position - e.Position).sqrMagnitude > PositionEpsilonSquared ||
                     Mathf.Abs(Mathf.DeltaAngle(old.Rotation, e.Rotation)) > RotationEpsilon))
                    ++moved;
            }
            foreach (var kv in after.Landscape)
            {
                ScenarioLandscape.ObjectEntry previous;
                if (!before.Landscape.TryGetValue(kv.Key, out previous) ||
                    JsonUtility.ToJson(kv.Value) != JsonUtility.ToJson(previous))
                    ++moved;
            }
            // Al trasladar de una capa a Escenario hay dos instancias tecnicas,
            // pero para el usuario es UN SOLO movimiento.
            // Solo considerar traslado cuando el objeto que desaparece y el
            // que aparece tienen realmente la misma definicion (Alt).
            var removedByDefinition = new Dictionary<RoomItemDefinition, int>();
            foreach (var e in before.Native)
                if (!newNative.ContainsKey(e.Id) && e.Definition != null)
                {
                    int count;
                    removedByDefinition.TryGetValue(e.Definition, out count);
                    removedByDefinition[e.Definition] = count + 1;
                }
            foreach (var e in before.Scene)
                if (!newScene.ContainsKey(e.Id) && e.Definition != null)
                {
                    int count;
                    removedByDefinition.TryGetValue(e.Definition, out count);
                    removedByDefinition[e.Definition] = count + 1;
                }
            int transfers = 0;
            foreach (var e in after.Scene)
                if (!oldScene.ContainsKey(e.Id) && e.Definition != null)
                {
                    int count;
                    if (removedByDefinition.TryGetValue(e.Definition, out count) && count > 0)
                    {
                        removedByDefinition[e.Definition] = count - 1;
                        transfers++;
                    }
                }
            foreach (var e in after.Native)
                if (!oldNative.ContainsKey(e.Id) && e.Definition != null)
                {
                    int count;
                    if (removedByDefinition.TryGetValue(e.Definition, out count) && count > 0)
                    {
                        removedByDefinition[e.Definition] = count - 1;
                        transfers++;
                    }
                }
            placed -= transfers;
            deleted -= transfers;
            moved += transfers;
            touched = placed + deleted + moved;
            var result = new List<string>();
            if (moved > 0)
                result.Add(CountLabel(moved, "editor.history.moved.one",
                    "editor.history.moved.many"));
            if (placed > 0)
                result.Add(CountLabel(placed, "editor.history.placed.one",
                    "editor.history.placed.many"));
            if (deleted > 0)
                result.Add(CountLabel(deleted, "editor.history.deleted.one",
                    "editor.history.deleted.many"));
            return result.Count == 0
                ? T("editor.history.objects.modified")
                : string.Join(", ", result.ToArray());
        }

        private static string CountLabel(int count, string singularKey, string pluralKey) =>
            count == 1 ? T(singularKey) : EditorLocalization.Format(pluralKey, count);

        private void JumpTo(int target)
        {
            if (_applying || !IsValid() || EditingText() ||
                ScenarioEditorStorageMenu.IsDialogOpen || ScenarioEditorScene.IsDragging ||
                target < 0 || target > _changes.Count) return;
            // No interpolar fotografias a medias. Recorrer operaciones en orden
            // preserva la identidad de cada objeto colocado o recuperado.
            while (_position > target)
            {
                int previous = _position;
                Undo();
                if (_position == previous) break;
            }
            while (_position < target)
            {
                int previous = _position;
                Redo();
                if (_position == previous) break;
            }
            RefreshHistoryUI();
        }

        // Interfaz nativa Unity UI: no requiere UnityEngine.IMGUIModule.
        // Se construye una vez y solo se actualiza al cambiar el historial.
        private static RectTransform UiRect(GameObject go, Transform parent)
        {
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Text UiText(string name, Transform parent, Font font,
            int size, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            UiRect(go, parent);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private void EnsureHistoryUI()
        {
            if (_historyUi != null) return;
            _historyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            _historyUi = new GameObject("UP Historial Canvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UiRect(_historyUi, transform);
            var canvas = _historyUi.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2800;
            var scaler = _historyUi.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var panelGo = new GameObject("Historial", typeof(RectTransform), typeof(Image));
            _historyPanel = UiRect(panelGo, _historyUi.transform);
            _historyPanel.anchorMin = new Vector2(1f, 1f);
            _historyPanel.anchorMax = new Vector2(1f, 1f);
            _historyPanel.pivot = new Vector2(1f, 1f);
            panelGo.GetComponent<Image>().color = new Color(.045f, .070f, .095f, .94f);

            _historyTitle = UiText("Titulo", _historyPanel, _historyFont, 15, TextAnchor.MiddleLeft);
            var titleRect = _historyTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -5f);
            titleRect.sizeDelta = new Vector2(-18f, 30f);

            var scrollGo = new GameObject("Desplazamiento", typeof(RectTransform),
                typeof(ScrollRect));
            var scrollRect = UiRect(scrollGo, _historyPanel);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(8f, 8f);
            scrollRect.offsetMax = new Vector2(-8f, -37f);
            _historyScroll = scrollGo.GetComponent<ScrollRect>();
            _historyScroll.horizontal = false;
            _historyScroll.vertical = true;
            _historyScroll.scrollSensitivity = 22f;
            _historyScroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Recorte", typeof(RectTransform),
                typeof(Image), typeof(RectMask2D));
            var viewport = UiRect(viewportGo, scrollRect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(.03f, .045f, .065f, .7f);

            var contentGo = new GameObject("Pasos", typeof(RectTransform));
            _historyContent = UiRect(contentGo, viewport);
            _historyContent.anchorMin = new Vector2(0f, 1f);
            _historyContent.anchorMax = new Vector2(1f, 1f);
            _historyContent.pivot = new Vector2(.5f, 1f);
            _historyContent.anchoredPosition = Vector2.zero;
            _historyContent.sizeDelta = Vector2.zero;
            _historyScroll.viewport = viewport;
            _historyScroll.content = _historyContent;
            ScenarioEditorDockLayout.InstallNativeVerticalScrollbar(_historyPanel,
                _historyScroll, "UP History Native Vertical Scrollbar", 37f, 8f, true);
            ApplyDockRect();
            _historyUi.SetActive(_dockVisible);
        }

        private void ApplyDockRect()
        {
            if (_historyPanel == null) return;
            _historyPanel.anchorMin = _historyPanel.anchorMax = Vector2.one;
            _historyPanel.pivot = Vector2.one;
            _historyPanel.anchoredPosition = new Vector2(0f, -_dockTop);
            _historyPanel.sizeDelta = new Vector2(_dockWidth, _dockHeight);
        }

        private void RefreshHistoryUI()
        {
            if (!IsValid()) return;
            EnsureHistoryUI();
            _historyTitle.text = T("editor.history.title");
            for (var j = _historyContent.childCount - 1; j >= 0; --j)
                Destroy(_historyContent.GetChild(j).gameObject);
            const float rowHeight = 29f;
            const float spacing = 2f;
            _historyContent.sizeDelta = new Vector2(0f,
                (_changes.Count + 1) * (rowHeight + spacing));
            for (int i = 0; i <= _changes.Count; ++i)
            {
                int step = i;
                var go = new GameObject("Paso " + i, typeof(RectTransform),
                    typeof(Image), typeof(Button));
                var rect = UiRect(go, _historyContent);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -i * (rowHeight + spacing));
                rect.sizeDelta = new Vector2(0f, rowHeight);
                var image = go.GetComponent<Image>();
                image.color = i == _position
                    ? new Color(.15f, .40f, .57f, 1f)
                    : i > _position
                        ? new Color(.46f, .52f, .56f, 1f)
                        : new Color(.12f, .16f, .21f, 1f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.interactable = i != _position;
                button.onClick.AddListener(() => JumpTo(step));
                var label = UiText("Texto", rect, _historyFont, 13, TextAnchor.MiddleLeft);
                var labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(7f, 0f);
                labelRect.offsetMax = new Vector2(-4f, 0f);
                label.color = i > _position
                    ? new Color(.72f, .78f, .82f, 1f)
                    : Color.white;
                label.text = i == 0 ? T("editor.history.start") : _changes[i - 1].Name;
            }
            if (_position == _changes.Count)
                _historyScroll.verticalNormalizedPosition = 0f;
        }

        private bool OverHistory()
        {
            if (_historyUi == null || !_historyUi.activeInHierarchy) return false;
            return _historyPanel != null && RectTransformUtility.RectangleContainsScreenPoint(
                _historyPanel, Input.mousePosition, null);
        }

        private void RestoreState(State desired)
        {
            // 1. Restaurar objetos de las parcelas por identidad logica.
            // Las instancias destruidas se recrean desde su definicion original.
            var targetNative = desired.Native.ToDictionary(x => x.Id);
            var currentNative = new Dictionary<int, LandscapeRoomItem>();
            foreach (var plot in _level.WorldState.HospitalPlots)
                foreach (var item in plot?.HospitalMap?.FloorPlan?.LandscapeItems ??
                    new List<LandscapeRoomItem>())
                {
                    int id;
                    if (item != null && _nativeIds.TryGetValue(item, out id))
                        currentNative[id] = item;
                }
            var touched = new HashSet<HospitalPlot>();
            foreach (var pair in currentNative)
            {
                if (targetNative.ContainsKey(pair.Key)) continue;
                var item = pair.Value;
                var plot = item.FloorPlan?.HospitalMap?.Plot;
                if (plot != null) touched.Add(plot);
                _level.BuildEvents.OnRoomItemDestroy?.Invoke(item);
                // Algunas decoraciones no tienen visual de sala y el evento
                // nativo no las retira en ese caso.
                if (item.FloorPlan != null &&
                    item.FloorPlan.LandscapeItems.Contains(item))
                {
                    item.FloorPlan.RemoveItem(item);
                    item.Visual?.Destroy();
                    item.Destroy();
                }
                if (plot != null) ScenarioEditorScene.MarkPlotLayerModified(plot, item.Layer);
            }
            foreach (var record in desired.Native)
            {
                var plot = _level.WorldState.HospitalPlots[record.Plot];
                var floor = plot?.HospitalMap?.FloorPlan;
                if (floor == null || record.Definition == null)
                    throw new InvalidOperationException("Parcela u objeto no disponible durante Undo.");
                LandscapeRoomItem item;
                if (currentNative.TryGetValue(record.Id, out item))
                {
                    var oldPlot = item.FloorPlan?.HospitalMap?.Plot;
                    var changed = item.FloorPlan != floor || item.Layer != record.Layer ||
                        (item.WorldPosition - record.Position).sqrMagnitude > PositionEpsilonSquared ||
                        Mathf.Abs(Mathf.DeltaAngle(item.Rotation, record.Rotation)) > RotationEpsilon;
                    if (changed && oldPlot?.Definition != null)
                    {
                        oldPlot.Definition.RemoveItem(item);
                        SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(oldPlot.Definition);
                    }
                    if (item.FloorPlan != floor)
                    {
                        if (oldPlot != null) touched.Add(oldPlot);
                        item.FloorPlan?.RemoveItem(item);
                        item.FloorPlan = floor;
                        floor.AddItem(item);
                        touched.Add(plot);
                        ScenarioEditorScene.MarkPlotLayerModified(plot, record.Layer);
                    }
                    if ((item.WorldPosition - record.Position).sqrMagnitude > PositionEpsilonSquared ||
                        Mathf.Abs(Mathf.DeltaAngle(item.Rotation, record.Rotation)) > RotationEpsilon)
                    {
                        item.WorldPosition = record.Position;
                        item.Rotation = record.Rotation;
                        touched.Add(plot);
                        ScenarioEditorScene.MarkPlotLayerModified(plot, record.Layer);
                    }
                    if (item.Layer != record.Layer)
                    {
                        AccessTools.Field(typeof(LandscapeRoomItem), "_layer")
                            ?.SetValue(item, record.Layer);
                        touched.Add(plot);
                        ScenarioEditorScene.MarkPlotLayerModified(plot, record.Layer);
                    }
                    if (changed && plot.Definition != null &&
                        !plot.Definition.Contains(item, floor, record.Layer, default(Vector3)))
                    {
                        plot.Definition.AddItem(new HospitalPlotItem
                        {
                            Definition = SharedInstanceUtils.GetSharedInstance<RoomItemDefinition>(
                                record.Definition),
                            Position = item.LocalPosition,
                            Rotation = item.Rotation
                        }, record.Layer);
                        SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(plot.Definition);
                    }
                }
                else
                {
                    item = new LandscapeRoomItem(record.Definition, floor, _level, record.Layer);
                    item.WorldPosition = record.Position;
                    item.Rotation = record.Rotation;
                    floor.AddItem(item);
                    _nativeIds[item] = record.Id;
                    if (plot.Definition != null &&
                        !plot.Definition.Contains(item, floor, record.Layer, default(Vector3)))
                    {
                        plot.Definition.AddItem(new HospitalPlotItem
                        {
                            Definition = SharedInstanceUtils.GetSharedInstance<RoomItemDefinition>(
                                record.Definition),
                            Position = item.LocalPosition,
                            Rotation = item.Rotation
                        }, record.Layer);
                        SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(plot.Definition);
                    }
                    touched.Add(plot);
                    ScenarioEditorScene.MarkPlotLayerModified(plot, record.Layer);
                }
            }
            foreach (var plot in touched)
                if (plot?.HospitalMap?.RoomVisual != null)
                    plot.HospitalMap.RoomVisual.UpdateFromRoom(
                        plot.HospitalMap.FloorPlan, Vector3.zero, 0f);

            // 2. Decoracion de la capa global de Escenario.
            var targetScene = desired.Scene.ToDictionary(x => x.Id);
            var currentScene = new Dictionary<int, ScenarioEditorScene.SceneObject>();
            foreach (var sceneObject in ScenarioEditorScene.SceneObjects)
            {
                int id;
                if (sceneObject != null && _sceneIds.TryGetValue(sceneObject, out id))
                    currentScene[id] = sceneObject;
            }
            foreach (var pair in currentScene)
                if (!targetScene.ContainsKey(pair.Key))
                    ScenarioEditorScene.RemoveObject(pair.Value);
            foreach (var record in desired.Scene)
            {
                ScenarioEditorScene.SceneObject obj;
                if (!currentScene.TryGetValue(record.Id, out obj) || obj.Visual == null)
                {
                    obj = ScenarioEditorScene.Add(record.Definition, record.Position, record.Rotation);
                    if (obj == null) throw new InvalidOperationException("No se pudo recuperar un objeto de Escenario.");
                    _sceneIds[obj] = record.Id;
                }
                obj.SourcePath = record.SourcePath;
                obj.BasePosition = record.BasePosition;
                obj.BaseRotation = record.BaseRotation;
                obj.Visual.transform.position = record.Position;
                obj.Visual.transform.rotation = Quaternion.Euler(0f, record.Rotation, 0f);
            }
            var sourceChanges = SceneSourceChangesField?.GetValue(null) as
                Dictionary<string, ScenarioMapFormat.OriginalChange>;
            if (sourceChanges != null)
            {
                sourceChanges.Clear();
                foreach (var pair in desired.Sources)
                    sourceChanges.Add(pair.Key, CopySource(pair.Value));
            }

            // 3. Landscape: restaurar desplazamientos de objetos originales
            // sin modificar los Transform fijos de Unity.
            var edited = LandscapeEditedField?.GetValue(null) as
                Dictionary<string, ScenarioLandscape.ObjectEntry>;
            if (edited != null)
            {
                edited.Clear();
                foreach (var pair in desired.Landscape)
                    edited.Add(pair.Key, CopyLandscape(pair.Value));
                if (ScenarioLandscape.IsActive)
                {
                    LandscapeRebuild?.Invoke(null, null);
                    LandscapeApply?.Invoke(null, null);
                }
            }
            // No recalcular iluminacion global por un cambio de objeto.
        }
    }

    // El cursor nativo retira temporalmente los objetos al recogerlos.
    // Nunca disparar ObjectDropped desde MouseDown/MouseUp: la confirmacion
    // llega al colocarlos, destruirlos o terminar una seleccion multiple.
    [HarmonyPatch(typeof(CursorRoomItem), "PlaceItem")]
    internal static class ScenarioEditorHistoryNativePlacedPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorHistory.ObjectDropped();
    }

    // Cancelar una colocacion tambien puede retirar definitivamente un item;
    // la captura se aplaza hasta que el cursor ha vuelto al modo estable.
    [HarmonyPatch(typeof(CursorRoomItem), "CancelItem")]
    internal static class ScenarioEditorHistoryNativeCancelPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorHistory.ObjectDropped();
    }

    [HarmonyPatch(typeof(CursorEditHospitalItem), "OnMoveRoomEnd")]
    internal static class ScenarioEditorHistoryNativeMultiFinishedPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorHistory.ObjectDropped();
    }

    [HarmonyPatch(typeof(BuildEvents), "RoomItemDestroy")]
    internal static class ScenarioEditorHistoryNativeDeletedPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorHistory.ObjectDropped();
    }

    // Acciones de objetos de Escenario, que no generan OnRoomItemPlaced.
    [HarmonyPatch(typeof(ScenarioEditorScene), "CommitDrag")]
    internal static class ScenarioEditorHistorySceneDropPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorHistory.ObjectDropped();
    }

    [HarmonyPatch(typeof(ScenarioEditorScene), "Remove")]
    internal static class ScenarioEditorHistorySceneRemovePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorHistory.ObjectDropped();
    }

    // Solo operaciones de objetos; no se intercepta la pintura ni el movimiento de parcelas.
    [HarmonyPatch(typeof(ScenarioEditorFlow), "OpenNativeEditor")]
    internal static class ScenarioEditorHistoryOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Level level) => ScenarioEditorHistory.StartHistory(level);
    }

    // Importar originales al SELECCIONAR Escenario no es una modificacion del
    // usuario. No debe entrar en Undo ni borrarse al revertir otra accion.
    [HarmonyPatch(typeof(ScenarioEditorScene), "Select")]
    internal static class ScenarioEditorHistorySceneImportPatch
    {
        private static void Prefix(ref int __state) =>
            __state = ScenarioEditorScene.SceneObjects.Count;

        private static void Postfix(int __state)
        {
            if (ScenarioEditorScene.SceneObjects.Count != __state)
                ScenarioEditorHistory.ResetAfterMapLoad();
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "Load")]
    internal static class ScenarioEditorHistoryMapLoadPatch
    {
        private static void Postfix()
        {
            ScenarioEditorHistory.ResetAfterMapLoad();
        }
    }


}
