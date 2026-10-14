// Updated: 2026-10-14 - Ctrl: movimiento multiple nativo, cancela y restaura objetos sin modificar parcelas.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using FullInspector;
using TMPro;
using TH20;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed class ScenarioEditorObjectInteraction : MonoBehaviour
    {
        private static ScenarioEditorObjectInteraction _active;
        private static string _pendingSearch;
        private readonly Dictionary<RoomItemDefinition, string> _names =
            new Dictionary<RoomItemDefinition, string>();
        private static readonly MethodInfo NativeHighlightMultiple =
            AccessTools.Method(typeof(HighlightManager), "HighlightObjects", new[] { typeof(List<Renderer>) });
        private Level _level;
        private HospitalPlot _selection;
        private Vector2 _rightDown;
        private bool _rightClickPending;
        private static readonly FieldInfo CursorPlotField =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_hospitalPlot");

        // Ctrl selecciona por el rectangulo horizontal, sin extraer objetos
        // de sus FloorPlans. La relacion instancia/parcela queda intacta.
        private readonly Dictionary<LandscapeRoomItem, HospitalPlot> _ctrlSelected =
            new Dictionary<LandscapeRoomItem, HospitalPlot>();
        private readonly HashSet<ScenarioEditorScene.SceneObject> _ctrlSceneSelected =
            new HashSet<ScenarioEditorScene.SceneObject>();
        private bool _ctrlDragging;
        private bool _ctrlHoverLocked;
        private Vector3 _ctrlStart;
        private Vector3 _ctrlEnd;
        private GameObject _ctrlRectangle;
        private LineRenderer _ctrlLine;
        private Material _ctrlMaterial;
        private HospitalPlotLayer _ctrlLayer;
        private bool _ctrlSceneLayer;

        // Identidad permanente del objeto durante el arrastre NATIVO. El juego
        // usa un solo BlueprintFloorPlan para mover, pero cada item retorna
        // a su FloorPlan original al confirmar o cancelar.
        private sealed class NativeMultiMember
        {
            internal HospitalPlot Owner;
            internal HospitalPlotLayer Layer;
            internal Vector3 OriginalWorld;
            internal Vector3 OriginalLocal;
            internal float OriginalRotation;
        }

        private static readonly FieldInfo NativeDragFloor =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_dragFloorPlan");
        private static readonly FieldInfo NativeDragVisual =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_dragFloorPlanVisualisation");
        private static readonly FieldInfo NativeMultiItems =
            AccessTools.Field(typeof(CursorEditHospitalItem), "_multiSelectItems");
        private readonly Dictionary<LandscapeRoomItem, NativeMultiMember> _nativeMulti =
            new Dictionary<LandscapeRoomItem, NativeMultiMember>();
        private CursorEditHospitalItem _nativeOwnerCursor;
        private BlueprintFloorPlan _nativeFloor;
        private bool _nativeMoveCancelled;
        private static readonly FieldInfo NativeMoveRotateControl =
            AccessTools.Field(typeof(CursorRoomMove), "_rotateControl");

        // El objeto original no pasa por StartItemEdit: solo se inicia la
        // colocacion NATIVA de otro objeto nuevo, en la parcela elegida.
        private LandscapeRoomItem _cloneSource;
        private HospitalPlot _cloneDestination;

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            if (menu == null || level == null || !ScenarioEditorFlow.IsRunning) return;
            var component = menu.GetComponent<ScenarioEditorObjectInteraction>() ??
                menu.gameObject.AddComponent<ScenarioEditorObjectInteraction>();
            component.Configure(level);
        }

        private void Configure(Level level)
        {
            if (ReferenceEquals(_level, level)) return;
            Detach();
            _level = level;
            _active = this;
            _selection = level.WorldState?.HospitalPlots?.FirstOrDefault();
            level.HospitalEditEvents.OnSelectHospitalPlot += PlotSelected;
        }

        private void PlotSelected(HospitalPlot plot) { _selection = plot; }

        private void Detach()
        {
            if (_level != null) _level.HospitalEditEvents.OnSelectHospitalPlot -= PlotSelected;
            if (_nativeMulti.Count > 0) CompleteNativeMultiple(_nativeOwnerCursor, false, Vector3.zero, true);
            ClearCtrlSelection();
            _cloneSource = null;
            _cloneDestination = null;
            _level = null;
            _rightClickPending = false;
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private void OnEnable()
        {
            if (_level == null && ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Level != null)
                Configure(ScenarioEditorSession.Level);
        }
        private void OnDisable() { Detach(); }
        private void OnDestroy() { Detach(); }

        private bool Ready => _level != null && ScenarioEditorSession.Active &&
            ScenarioEditorFlow.IsRunning && ReferenceEquals(ScenarioEditorSession.Level, _level) &&
            !ScenarioEditorStorageMenu.IsDialogOpen;

        private static bool OverUi() => EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();

        private static bool CtrlDown => Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        internal static bool CtrlRectangleDragging => _active != null && _active._ctrlDragging;

        // El cursor original hace la seleccion, el highlight y StartItemEdit.
        // _hospitalPlot solo se sustituye durante SU CursorUpdate; el evento
        // OnSelectHospitalPlot NO se emite al mantener Mayus o Alt.
        internal static bool BeforeNativeCursor(CursorEditHospitalItem cursor,
            out HospitalPlot previousPlot)
        {
            previousPlot = null;
            var self = _active;
            if (self == null || !self.Ready || cursor == null) return true;
            if (self._nativeMulti.Count > 0) return false;
            if (self._rightClickPending &&
                Vector2.Distance(self._rightDown, Input.mousePosition) <= 5f) return false;
            if (CtrlDown)
            {
                // NO crear el BlueprintFloorPlan del juego: agruparia objetos
                // de varias parcelas bajo un solo propietario.
                self.UpdateCtrlRectangle();
                return false;
            }
            if (OverUi()) return true;
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (ScenarioEditorScene.IsSelected) return false;
            if (shift && Input.GetMouseButtonDown(0))
            {
                HospitalPlot pickedPlot;
                LandscapeRoomItem pickedItem;
                // Consumir SIEMPRE el clic Mayus: ni siquiera cuando no haya
                // objeto se debe coger accidentalmente el original.
                if (self.TryPickNative(Input.mousePosition, out pickedPlot, out pickedItem))
                    self.QueueNativeClone(pickedItem, self._selection ?? pickedPlot);
                return false;
            }
            if (!shift && !alt) return true;
            HospitalPlot owner;
            LandscapeRoomItem item;
            if (!self.TryPickNative(Input.mousePosition, out owner, out item) ||
                owner == null || ReferenceEquals(owner, self._selection) ||
                CursorPlotField == null) return true;
            previousPlot = CursorPlotField.GetValue(cursor) as HospitalPlot;
            CursorPlotField.SetValue(cursor, owner);
            return true;
        }

        internal static void AfterNativeCursor(CursorEditHospitalItem cursor,
            HospitalPlot previousPlot)
        {
            if (cursor != null && previousPlot != null && CursorPlotField != null)
                CursorPlotField.SetValue(cursor, previousPlot);
        }

        private bool HasMultipleSelection =>
            _ctrlSelected.Count + _ctrlSceneSelected.Count >= 2;

        private void QueueNativeClone(LandscapeRoomItem original, HospitalPlot destination)
        {
            if (original?.Definition == null || destination?.HospitalMap?.FloorPlan == null ||
                destination.HospitalMap.RoomVisual == null) return;
            _cloneSource = original;
            _cloneDestination = destination;
            // No llamar a BuildEvents.StartItemEdit(original, ...): esa ruta
            // retira el item del FloorPlan y es la causa de que desaparezca.
            _level.HighlightManager.HighlightObject(original);
        }

        private void LateUpdate()
        {
            var original = _cloneSource;
            var destination = _cloneDestination;
            _cloneSource = null;
            _cloneDestination = null;
            if (original == null || destination == null || !Ready ||
                _level?.CursorManager == null ||
                !_level.CursorManager.IsModeActive<CursorEditHospitalItem>()) return;
            try
            {
                var config = _level.BuildingLogic.Configuration.RoomItemEditConfig;
                var next = new CursorRoomItem(_level.CursorManager, _level, config,
                    original.Definition, original.Rotation,
                    destination.HospitalMap.FloorPlan, destination.HospitalMap.RoomVisual,
                    null, false);
                _level.CursorManager.PushMode(next);
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("Mayus: no se pudo iniciar la copia: " + ex);
            }
        }

        // El cursor del juego levanta y visualiza todos los objetos a la vez,
        // tal y como hace StartMultiSelectEdit() del juego original. Solo
        // sustituimos su lista de origen y el retorno a cada parcela.
        private bool BeginNativeMultiple()
        {
            if (!Ready || _ctrlSelected.Count == 0 ||
                _level.CursorManager == null ||
                !_level.CursorManager.TryGetActiveMode<CursorEditHospitalItem>(out var cursor))
                return false;
            var floor = NativeDragFloor?.GetValue(cursor) as BlueprintFloorPlan;
            var visual = NativeDragVisual?.GetValue(cursor) as BlueprintFloorPlanVisual;
            var nativeItems = NativeMultiItems?.GetValue(cursor) as List<LandscapeRoomItem>;
            if (floor == null || visual == null || nativeItems == null ||
                nativeItems.Count != 0 || floor.LandscapeItems.Count != 0)
                return false;

            var start = _ctrlStart.ToGridCoord();
            var end = _ctrlEnd.ToGridCoord();
            floor.Anchor = new GridCoord(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
            floor.Tiles = new bool[Mathf.Abs(end.X - start.X) + 1,
                Mathf.Abs(end.Y - start.Y) + 1];
            ArrayUtils.Populate<bool>(floor.Tiles, true);
            floor.RecalculateWalls();
            floor.ValidRoomSize = false;
            visual.CompletelyInvalid = true;
            _nativeFloor = floor;
            _nativeOwnerCursor = cursor;
            _nativeMoveCancelled = false;
            _nativeMulti.Clear();
            var owners = new HashSet<HospitalPlot>();
            try
            {
                foreach (var pair in _ctrlSelected)
                {
                    var item = pair.Key;
                    var owner = pair.Value;
                    if (item == null || owner?.HospitalMap?.FloorPlan == null) continue;
                    _nativeMulti.Add(item, new NativeMultiMember
                    {
                        Owner = owner, Layer = ReadLayer(item),
                        OriginalWorld = item.WorldPosition,
                        OriginalLocal = item.LocalPosition,
                        OriginalRotation = item.Rotation
                    });
                    // API NATIVA: destruye su visual anterior y conserva la
                    // misma instancia mientras la recoge el cursor del juego.
                    RoomAlgorithms.MoveItemToFloorPlan(item, floor);
                    nativeItems.Add(item);
                    owners.Add(owner);
                }
                if (_nativeMulti.Count == 0) return false;
                foreach (var owner in owners)
                    owner.HospitalMap.RoomVisual.UpdateFromRoom(
                        owner.HospitalMap.FloorPlan, Vector3.zero, 0f);
                visual.UpdateFromRoom(floor, Vector3.zero, 0f);
                visual.SetWallsFloorVisible(false);
                visual.SetVisible(true);
                _level.CursorManager.PushMode(new CursorRoomMove(
                    _level.CursorManager, _level, _level.WorldState,
                    _level.BuildEvents, floor, visual, true));
                _ctrlSelected.Clear();
                _ctrlSceneSelected.Clear();
                return true;
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ctrl: fallo al iniciar seleccion multiple nativa: " + ex);
                CompleteNativeMultiple(cursor, false, Vector3.zero, true);
                return false;
            }
        }

        // CursorRoomMove usa OnMoveRoomEnd(false, offset) tanto al COLOCAR
        // como al CANCELAR. Guardar la opcion Cancel del control nativo
        // permite restaurar todos los originales sin dar por hecho un guardado.
        internal static void TrackNativeMultiCancel(CursorControlRotatePlace control)
        {
            var self = _active;
            if (self == null || self._nativeMulti.Count == 0 ||
                control == null || self._level?.CursorManager == null ||
                !self._level.CursorManager.TryGetActiveMode<CursorRoomMove>(out var move) ||
                !ReferenceEquals(NativeMoveRotateControl?.GetValue(move), control)) return;
            self._nativeMoveCancelled = control.Cancel;
        }

        // Se llama desde el OnMoveRoomEnd original, antes de que el juego
        // intente devolver todos los objetos a UNA sola parcela.
        internal static bool InterceptNativeMultiple(CursorEditHospitalItem cursor,
            bool deleted, Vector3 cellOffset)
        {
            var self = _active;
            if (self == null || !ReferenceEquals(cursor, self._nativeOwnerCursor) ||
                self._nativeMulti.Count == 0) return false;
            self.CompleteNativeMultiple(cursor, deleted, cellOffset,
                self._nativeMoveCancelled);
            return true;
        }

        private void CompleteNativeMultiple(CursorEditHospitalItem cursor,
            bool deleted, Vector3 cellOffset, bool cancel)
        {
            var floor = _nativeFloor;
            var members = _nativeMulti.ToArray();
            // Liberar el modo antes de emitir eventos para impedir reentradas.
            _nativeMulti.Clear();
            _nativeOwnerCursor = null;
            _nativeFloor = null;
            _nativeMoveCancelled = false;
            cellOffset.y = 0f;
            var affected = new HashSet<HospitalPlot>();
            var usedRecords = new HashSet<HospitalPlotItem>();
            var nativeItems = cursor == null ? null :
                NativeMultiItems?.GetValue(cursor) as List<LandscapeRoomItem>;
            try
            {
                foreach (var pair in members)
                {
                    var item = pair.Key;
                    var member = pair.Value;
                    var owner = member.Owner;
                    if (item == null || owner?.HospitalMap?.FloorPlan == null) continue;
                    var targetFloor = owner.HospitalMap.FloorPlan;
                    var destination = (cancel || deleted) ? member.OriginalWorld :
                        item.WorldPosition + cellOffset;
                    // El movimiento y cambio de plano son los del juego.
                    item.WorldPosition = destination;
                    RoomAlgorithms.MoveItemToFloorPlan(item, targetFloor);
                    if (cancel || deleted)
                        item.Rotation = member.OriginalRotation;
                    if (!cancel && !deleted)
                    {
                        var records = owner.Definition?.GetItems(member.Layer);
                        if (records != null)
                            foreach (var record in records)
                            {
                                if (record == null || usedRecords.Contains(record) ||
                                    !ReferenceEquals(record.Definition?.Instance, item.Definition) ||
                                    (record.Position - member.OriginalLocal).sqrMagnitude > 0.0001f ||
                                    Mathf.Abs(record.Rotation - member.OriginalRotation) > .01f) continue;
                                record.Position = item.LocalPosition;
                                record.Rotation = item.Rotation;
                                usedRecords.Add(record);
                                SharedInstanceUtils.MarkAsDirty<HospitalPlotDefinition>(owner.Definition);
                                break;
                            }
                        ScenarioEditorScene.MarkPlotLayerModified(owner, member.Layer);
                    }
                    affected.Add(owner);
                }
                nativeItems?.Clear();
                foreach (var owner in affected)
                {
                    owner.HospitalMap.RoomVisual.UpdateFromRoom(
                        owner.HospitalMap.FloorPlan, Vector3.zero, 0f);
                    _level.HospitalEditEvents.OnHospitalPlotUpdated?.Invoke(owner);
                }
                if (floor != null)
                {
                    // Vaciar y ocultar siempre el plano temporal: al cancelar
                    // tambien hay que eliminar las visuales de previsualizacion.
                    var visual = cursor == null ? null :
                        NativeDragVisual?.GetValue(cursor) as BlueprintFloorPlanVisual;
                    visual?.UpdateFromRoom(floor, Vector3.zero, 0f);
                    visual?.SetVisible(false);
                }
            }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Ctrl: fallo al devolver objetos a sus parcelas: " + ex);
            }
        }

        // Ctrl cambia la parcela al pasar por encima de un objeto, SIN clic.
        // Bloq Mayus desactiva solo este cambio automatico.
        private void UpdateCtrlPlotHover()
        {
            if (!CtrlDown || _ctrlDragging || _nativeMulti.Count > 0 || _ctrlHoverLocked ||
                Input.GetMouseButton(0) || OverUi() ||
                _level?.CursorManager == null ||
                !_level.CursorManager.IsModeActive<CursorEditHospitalItem>() ||
                _level.CursorManager.IsModeActive<CursorRoomItem>()) return;
            HospitalPlot owner;
            LandscapeRoomItem item;
            if (!TryPickNative(Input.mousePosition, out owner, out item) || owner == null) return;
            if (!ScenarioEditorStorageMenu.PlotIsolationActive &&
                !ReferenceEquals(owner, _selection))
                _level.HospitalEditEvents.OnSelectHospitalPlot?.Invoke(owner);
            if (_ctrlSelected.Count == 0 && _ctrlSceneSelected.Count == 0)
                _level.HighlightManager.HighlightObject(item);
        }

        private void UpdateCtrlRectangle()
        {
            if (_nativeMulti.Count > 0) return;
            if (_ctrlDragging && !CtrlDown)
            {
                // El usuario puede soltar Ctrl antes que el boton izquierdo.
                // Finalizar la seleccion con el rectangulo que estaba dibujando.
                if (ScenarioEditorScene.GetGroundPoint(Input.mousePosition, out var end))
                    _ctrlEnd = end;
                CollectCtrlItems();
                FinishCtrlRectangle();
                return;
            }
            if (_level?.CursorManager == null ||
                !_level.CursorManager.IsModeActive<CursorEditHospitalItem>() ||
                _level.CursorManager.IsModeActive<CursorRoomItem>()) return;
            if (!_ctrlDragging)
            {
                if (!Input.GetMouseButtonDown(0) || OverUi()) return;
                if (!ScenarioEditorScene.GetGroundPoint(Input.mousePosition, out _ctrlStart)) return;
                _ctrlEnd = _ctrlStart;
                _ctrlSceneLayer = ScenarioEditorScene.IsSelected;
                var menu = GetComponent<EditHospitalMenu>();
                var dropdown = menu == null ? null :
                    AccessTools.Field(typeof(EditHospitalMenu), "_plotLayerDropdown")?.GetValue(menu)
                    as TMP_Dropdown;
                _ctrlLayer = (HospitalPlotLayer)Mathf.Clamp(dropdown?.value ?? 0, 0, 2);
                // Cada rectangulo define una seleccion nueva, sin acumular
                // objetos fuera de sus bordes ni mezclar capas.
                _ctrlSelected.Clear();
                _ctrlSceneSelected.Clear();
                _ctrlHoverLocked = true;
                _ctrlDragging = true;
                BeginCtrlRectangle();
            }
            if (!_ctrlDragging) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ClearCtrlSelection();
                return;
            }
            Vector3 ground;
            if (ScenarioEditorScene.GetGroundPoint(Input.mousePosition, out ground))
                _ctrlEnd = ground;
            UpdateCtrlRectangleVisual();
            CollectCtrlItems();
            HighlightCtrlSelection();
            if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                FinishCtrlRectangle();
        }

        private void FinishCtrlRectangle()
        {
            _ctrlDragging = false;
            DestroyCtrlRectangle();
            if (_ctrlSelected.Count + _ctrlSceneSelected.Count == 0)
            {
                ClearCtrlSelection();
                return;
            }
            // Soltar el rectangulo inicia DIRECTAMENTE CursorRoomMove nativo.
            // La seleccion de Escenario (objetos externos a FloorPlan) aun no
            // participa en ese cursor; se conserva su resaltado sin moverlos.
            if (!_ctrlSceneLayer && !BeginNativeMultiple())
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Ctrl: no se pudo iniciar CursorRoomMove sobre la seleccion.");
        }

        private void BeginCtrlRectangle()
        {
            DestroyCtrlRectangle();
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            _ctrlMaterial = new Material(shader);
            _ctrlRectangle = new GameObject("UP Ctrl - rectangulo sobre el terreno");
            _ctrlLine = _ctrlRectangle.AddComponent<LineRenderer>();
            _ctrlLine.material = _ctrlMaterial;
            _ctrlLine.useWorldSpace = true;
            _ctrlLine.loop = true;
            _ctrlLine.positionCount = 4;
            _ctrlLine.widthMultiplier = 0.065f;
            _ctrlLine.startColor = _ctrlLine.endColor =
                new Color(0.22f, 0.7f, 1f, 0.95f);
        }

        private void UpdateCtrlRectangleVisual()
        {
            if (_ctrlLine == null) return;
            var y = Mathf.Max(_ctrlStart.y, _ctrlEnd.y) + 0.12f;
            _ctrlLine.SetPosition(0, new Vector3(_ctrlStart.x, y, _ctrlStart.z));
            _ctrlLine.SetPosition(1, new Vector3(_ctrlEnd.x, y, _ctrlStart.z));
            _ctrlLine.SetPosition(2, new Vector3(_ctrlEnd.x, y, _ctrlEnd.z));
            _ctrlLine.SetPosition(3, new Vector3(_ctrlStart.x, y, _ctrlEnd.z));
        }

        private void CollectCtrlItems()
        {
            // No acumular: un objeto que sale del rectangulo DEJA de estar
            // seleccionado en ese mismo fotograma.
            _ctrlSelected.Clear();
            _ctrlSceneSelected.Clear();
            var minX = Mathf.Min(_ctrlStart.x, _ctrlEnd.x);
            var maxX = Mathf.Max(_ctrlStart.x, _ctrlEnd.x);
            var minZ = Mathf.Min(_ctrlStart.z, _ctrlEnd.z);
            var maxZ = Mathf.Max(_ctrlStart.z, _ctrlEnd.z);
            if (_ctrlSceneLayer)
            {
                if (!ScenarioEditorScene.Visible) return;
                foreach (var scene in ScenarioEditorScene.SceneObjects)
                    if (scene?.Visual != null && scene.Visual.activeInHierarchy &&
                        IntersectsXZ(scene.Visual, minX, maxX, minZ, maxZ))
                        _ctrlSceneSelected.Add(scene);
                return;
            }
            var plots = _level?.WorldState?.HospitalPlots;
            if (plots == null) return;
            foreach (var plot in plots)
            {
                if (plot == null || !plot.IsVisible() || !plot.IsLayerVisible(_ctrlLayer)) continue;
                var items = plot.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (item == null || ReadLayer(item) != _ctrlLayer) continue;
                    var visual = item.Visual?.GameObject;
                    if (visual == null || !visual.activeInHierarchy)
                        continue;
                    if (IntersectsXZ(visual, minX, maxX, minZ, maxZ))
                        _ctrlSelected.Add(item, plot);
                }
            }
        }

        private static bool IntersectsXZ(GameObject visual, float minX, float maxX,
            float minZ, float maxZ)
        {
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                if (b.max.x >= minX && b.min.x <= maxX &&
                    b.max.z >= minZ && b.min.z <= maxZ) return true;
            }
            // Un prefab sin renderer tambien puede tener un pivote valido.
            var p = visual.transform.position;
            return p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;
        }

        private void HighlightCtrlSelection()
        {
            if (_level?.HighlightManager == null || NativeHighlightMultiple == null) return;
            var renderers = new List<Renderer>();
            foreach (var pair in _ctrlSelected)
            {
                if (pair.Value == null || !pair.Value.IsVisible() ||
                    pair.Key?.Visual?.GameObject == null ||
                    !pair.Key.Visual.GameObject.activeInHierarchy) continue;
                foreach (var renderer in pair.Key.Visual.GameObject.GetComponentsInChildren<Renderer>())
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        renderers.Add(renderer);
            }
            foreach (var scene in _ctrlSceneSelected)
                if (scene?.Visual != null)
                    foreach (var renderer in scene.Visual.GetComponentsInChildren<Renderer>())
                        if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                            renderers.Add(renderer);
            if (renderers.Count > 0)
                NativeHighlightMultiple.Invoke(_level.HighlightManager,
                    new object[] { renderers });
            else if (_ctrlDragging)
                HighlightRendererProxy.Instance.Clear();
        }

        private void DestroyCtrlRectangle()
        {
            if (_ctrlRectangle != null) UnityEngine.Object.Destroy(_ctrlRectangle);
            if (_ctrlMaterial != null) UnityEngine.Object.Destroy(_ctrlMaterial);
            _ctrlRectangle = null;
            _ctrlLine = null;
            _ctrlMaterial = null;
        }

        internal static void ClearCurrentSelection()
        {
            if (_active != null) _active.ClearCtrlSelection();
        }

        private void ClearCtrlSelection()
        {
            _ctrlDragging = false;
            _ctrlHoverLocked = false;
            _ctrlSelected.Clear();
            _ctrlSceneSelected.Clear();
            DestroyCtrlRectangle();
        }

        private bool TryPickNative(Vector2 mouse, out HospitalPlot owner,
            out LandscapeRoomItem selected)
        {
            owner = null;
            selected = null;
            var camera = Camera.main;
            var plots = _level?.WorldState?.HospitalPlots;
            if (camera == null || plots == null) return false;
            var ray = camera.ScreenPointToRay(mouse);
            var nearest = float.PositiveInfinity;
            foreach (var plot in plots)
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    if (!plot.IsVisible() || item?.Visual == null || item.Visual.GameObject == null ||
                        !item.Visual.GameObject.activeInHierarchy) continue;
                    float distance;
                    if (!item.Visual.RayCast(ray, out distance) || distance >= nearest) continue;
                    owner = plot;
                    selected = item;
                    nearest = distance;
                }
            }
            return selected != null;
        }

        private void Update()
        {
            if (_level == null && ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Level != null)
                Configure(ScenarioEditorSession.Level);
            if (!Ready) return;
            RefreshSearch();

            if (!CtrlDown && !_ctrlDragging) _ctrlHoverLocked = false;
            UpdateCtrlPlotHover();
            if (_ctrlDragging || (CtrlDown && !OverUi())) UpdateCtrlRectangle();
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
                !CtrlDown && !OverUi() &&
                _level.CursorManager.IsModeActive<CursorEditHospitalItem>())
            {
                HospitalPlot owner;
                LandscapeRoomItem item;
                if (TryPickNative(Input.mousePosition, out owner, out item))
                    _level.HighlightManager.HighlightObject(item);
            }
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) &&
                (_ctrlSelected.Count != 0 || _ctrlSceneSelected.Count != 0))
                HighlightCtrlSelection();
            // Un nuevo clic fuera de Ctrl deja paso a la seleccion nativa.
            if (!CtrlDown && !_ctrlDragging && _nativeMulti.Count == 0 &&
                !HasMultipleSelection && Input.GetMouseButtonDown(0) && !OverUi())
                ClearCtrlSelection();
            if (!CtrlDown && !_ctrlDragging && _nativeMulti.Count == 0 &&
                HasMultipleSelection && Input.GetMouseButtonDown(0) && !OverUi() &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                // Cuando se pincha fuera del grupo, dejar que el cursor nativo
                // seleccione el objeto por separado.
                HospitalPlot owner;
                LandscapeRoomItem hit;
                var onGroup = TryPickNative(Input.mousePosition, out owner, out hit) &&
                    _ctrlSelected.ContainsKey(hit);
                if (!onGroup && _ctrlSceneLayer && Camera.main != null)
                {
                    var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    foreach (var scene in _ctrlSceneSelected)
                        if (scene?.Visual != null &&
                            RayPickVisual(scene.Visual, ray, out var distance))
                        {
                            onGroup = true;
                            break;
                        }
                }
                if (!onGroup) ClearCtrlSelection();
            }
            // No se crean highlights. Se llama al postproceso NATIVO, incluso
            // para la decoracion global, cuyos prefabs no son RoomItem.
            if (ScenarioEditorScene.IsSelected && !CtrlDown && !_ctrlDragging && !OverUi())
            {
                ScenarioEditorScene.SceneObject scene;
                HospitalPlot owner;
                object item;
                RoomItemDefinition definition;
                if (TryPickForScene(out scene, out owner, out item, out definition))
                {
                    if (item is ICursorSelectable selectable)
                        _level.HighlightManager.HighlightObject(selectable);
                    else if (scene?.Visual != null)
                    {
                        var renderers = scene.Visual.GetComponentsInChildren<Renderer>()
                            .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToList();
                        if (renderers.Count > 0 && NativeHighlightMultiple != null)
                            NativeHighlightMultiple.Invoke(_level.HighlightManager,
                                new object[] { renderers });
                    }
                }
            }
            if (Input.GetMouseButtonDown(1))
            {
                _rightDown = Input.mousePosition;
                _rightClickPending = true;
            }
            if (_rightClickPending && Input.GetMouseButton(1) &&
                Vector2.Distance(_rightDown, Input.mousePosition) > 5f)
                _rightClickPending = false;
            if (_rightClickPending && Input.GetMouseButtonUp(1))
            {
                if (Vector2.Distance(_rightDown, Input.mousePosition) <= 5f &&
                    TryPickForScene(out var scene, out var owner, out var item, out var definition) &&
                    definition != null)
                    SetSearch(GetObjectName(definition));
                _rightClickPending = false;
            }
        }

        private static IList Items(HospitalPlot plot) =>
            plot?.HospitalMap?.FloorPlan?.LandscapeItems as IList;

        private static object ReadMember(object obj, string name)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var property = t.GetProperty(name, flags);
                if (property?.GetGetMethod(true) != null)
                    try { return property.GetValue(obj, null); } catch { }
                foreach (var candidate in new[] { name,
                    "_" + char.ToLowerInvariant(name[0]) + name.Substring(1),
                    "<" + name + ">k__BackingField" })
                {
                    var field = t.GetField(candidate, flags);
                    if (field != null) try { return field.GetValue(obj); } catch { }
                }
            }
            return null;
        }

        private static HospitalPlotLayer ReadLayer(object item)
        {
            var value = ReadMember(item, "Layer");
            return value is HospitalPlotLayer layer ? layer : HospitalPlotLayer.Base;
        }

        // Conserva el formato de retorno que utiliza ScenarioEditorScene.
        // Seleccion EXACTAMENTE igual que el cursor nativo para las parcelas.
        internal static bool TryPickForScene(out ScenarioEditorScene.SceneObject scene,
            out HospitalPlot owner, out object item, out RoomItemDefinition definition)
        {
            scene = null;
            owner = null;
            item = null;
            definition = null;
            var self = _active;
            if (self == null || !self.Ready || Camera.main == null) return false;
            LandscapeRoomItem picked;
            var native = self.TryPickNative(Input.mousePosition, out owner, out picked);
            float nativeDistance = float.PositiveInfinity;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (native && picked != null)
                picked.Visual.RayCast(ray, out nativeDistance);
            float sceneDistance = float.PositiveInfinity;
            foreach (var candidate in ScenarioEditorScene.SceneObjects)
            {
                float distance;
                if (candidate?.Visual == null ||
                    !RayPickVisual(candidate.Visual, ray, out distance) ||
                    distance >= sceneDistance) continue;
                scene = candidate;
                sceneDistance = distance;
            }
            if (scene != null && sceneDistance < nativeDistance)
            {
                owner = null;
                item = null;
                definition = scene.Definition;
                return true;
            }
            scene = null;
            if (!native || picked == null) return false;
            item = picked;
            definition = picked.Definition as RoomItemDefinition;
            return definition != null;
        }

        internal static bool TryPickNativeForScene(out HospitalPlot owner, out object item,
            out RoomItemDefinition definition)
        {
            ScenarioEditorScene.SceneObject scene;
            return TryPickForScene(out scene, out owner, out item, out definition) &&
                owner != null;
        }

        internal static bool RayPickVisual(GameObject go, Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            if (go == null || !go.activeInHierarchy) return false;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || !filter.gameObject.activeInHierarchy) continue;
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var local = new Ray(filter.transform.InverseTransformPoint(ray.origin),
                    filter.transform.InverseTransformVector(ray.direction));
                float boundDistance;
                if (!mesh.bounds.IntersectRay(local, out boundDistance)) continue;
                try
                {
                    var vertices = mesh.vertices;
                    var triangles = mesh.triangles;
                    for (var i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        float at;
                        if (!RayIntersectsTriangle(local, vertices[triangles[i]],
                            vertices[triangles[i + 1]], vertices[triangles[i + 2]], out at)) continue;
                        if (at >= 0f && at < distance) distance = at;
                    }
                }
                catch (UnityException) { }
            }
            return distance < float.PositiveInfinity;
        }

        private static bool RayIntersectsTriangle(Ray ray, Vector3 a, Vector3 b,
            Vector3 c, out float distance)
        {
            distance = 0f;
            var edge1 = b - a;
            var edge2 = c - a;
            var p = Vector3.Cross(ray.direction, edge2);
            var det = Vector3.Dot(edge1, p);
            if (Mathf.Abs(det) < 0.000001f) return false;
            var inverse = 1f / det;
            var tvec = ray.origin - a;
            var u = Vector3.Dot(tvec, p) * inverse;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(tvec, edge1);
            var v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }

        internal static GameObject ReadNativeVisual(object item)
        {
            var visual = ReadMember(item, "Visual");
            return ReadMember(visual, "GameObject") as GameObject;
        }

        internal static Vector3? ReadNativePosition(object item)
        {
            var value = ReadMember(item, "LocalPosition");
            return value is Vector3 position ? position : (Vector3?)null;
        }

        internal static bool RemoveNativeAfterSceneTransfer(HospitalPlot owner, object item)
        {
            var objects = Items(owner);
            if (objects == null || !objects.Contains(item)) return false;
            var visual = ReadNativeVisual(item);
            var layer = ReadLayer(item);
            var position = ReadNativePosition(item);
            var definition = ReadMember(item, "Definition") as RoomItemDefinition;
            // La definición y el FloorPlan deben reflejar la misma transferencia.
            var saved = owner.Definition?.GetItems(layer);
            if (saved != null && definition != null && position.HasValue)
            {
                var matches = -1;
                for (var i = saved.Count - 1; i >= 0; --i)
                {
                    var placement = saved[i];
                    if (placement?.Definition?.Instance == null ||
                        !ReferenceEquals(placement.Definition.Instance, definition) ||
                        (placement.Position - position.Value).sqrMagnitude > 0.0001f)
                        continue;
                    matches = i;
                    break;
                }
                // Si la definicion conserva una posicion distinta, detener el
                // traslado antes de eliminar la instancia visible.
                if (matches >= 0) saved.RemoveAt(matches);
                else if (saved.Any(placement => placement?.Definition != null &&
                            ReferenceEquals(placement.Definition.Instance, definition)))
                    return false;
            }
            objects.Remove(item);
            ScenarioEditorScene.MarkPlotLayerModified(owner, layer);
            if (visual != null) UnityEngine.Object.Destroy(visual);
            var level = _active?._level;
            level?.HospitalEditEvents.OnHospitalPlotUpdated?.Invoke(owner);
            return true;
        }

        private static string SafePrefabName(RoomItemDefinition definition)
        {
            try { return definition?.GetPrefab(0)?.name ?? ""; }
            catch { return ""; }
        }

        private string GetObjectName(RoomItemDefinition definition)
        {
            if (definition == null) return "";
            if (_names.TryGetValue(definition, out var cached)) return cached;
            // Obtener el texto traducido que tambien usa la paleta. El objeto
            // NameLocalised puede tener diferentes implementaciones segun DLC.
            var value = ReadMember(definition, "NameLocalised");
            var text = ReadMember(value, "Translation") as string ??
                ReadMember(value, "Text") as string ??
                ReadMember(value, "Value") as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                foreach (var methodName in new[] { "GetLocalised", "GetLocalized",
                    "GetLocalisedText", "GetLocalizedText", "GetTranslation" })
                {
                    try
                    {
                        var method = value == null ? null : AccessTools.Method(value.GetType(), methodName,
                            Type.EmptyTypes);
                        if (method == null || method.ReturnType != typeof(string)) continue;
                        text = method.Invoke(value, null) as string;
                        if (!string.IsNullOrWhiteSpace(text)) break;
                    }
                    catch { }
                }
            }
            if (string.IsNullOrWhiteSpace(text))
                text = ReadMember(definition, "DisplayName") as string ??
                    ReadMember(definition, "Name") as string;
            if (string.IsNullOrWhiteSpace(text))
                text = !string.IsNullOrWhiteSpace(definition.DebugTag)
                    ? definition.DebugTag : SafePrefabName(definition);
            return _names[definition] = (text ?? "").Trim();
        }

        private static TMP_InputField FindSearch()
        {
            TMP_InputField fallback = null;
            foreach (var menu in Resources.FindObjectsOfTypeAll<LandscapeObjectsMenu>())
            {
                if (menu == null || !menu.gameObject.scene.IsValid()) continue;
                var field = AccessTools.Field(typeof(LandscapeObjectsMenu), "_searchBox");
                var input = field?.GetValue(menu) as TMP_InputField ??
                    menu.GetComponentInChildren<TMP_InputField>(true);
                if (input == null) continue;
                if (menu.gameObject.activeInHierarchy) return input;
                if (fallback == null) fallback = input;
            }
            return fallback;
        }

        private static void SetSearch(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _pendingSearch = name;
            var input = FindSearch();
            if (input == null || !input.gameObject.activeInHierarchy)
            {
                // Abrir la paleta real; escribir en un input desactivado no
                // actualiza los resultados y parecia que el clic no funcionaba.
                var menu = _active == null ? null : _active.GetComponent<EditHospitalMenu>();
                var button = menu == null ? null : AccessTools.Field(typeof(EditHospitalMenu),
                    "_objectsButton")?.GetValue(menu) as Button;
                if (button != null && button.interactable)
                    button.onClick.Invoke();
            }
            RefreshSearch();
        }

        internal static void RefreshSearch()
        {
            if (string.IsNullOrWhiteSpace(_pendingSearch) || !ScenarioEditorFlow.IsRunning) return;
            var input = FindSearch();
            if (input == null || !input.gameObject.activeInHierarchy) return;
            input.text = _pendingSearch;
            input.onValueChanged.Invoke(input.text);
            input.ForceLabelUpdate();
            _pendingSearch = null;
        }
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioEditorObjectInteractionSetupPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level __0) =>
            ScenarioEditorObjectInteraction.Attach(__instance, __0);
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "StartEdit")]
    internal static class ScenarioEditorObjectInteractionBeginPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active) return;
            var menu = UnityEngine.Object.FindObjectOfType<EditHospitalMenu>();
            if (menu != null)
                ScenarioEditorObjectInteraction.Attach(menu, ScenarioEditorSession.Level);
        }
    }

    [HarmonyPatch(typeof(CursorEditHospitalItem), "CursorUpdate")]
    internal static class ScenarioEditorCrossPlotPickupPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorEditHospitalItem __instance,
            out HospitalPlot __state) =>
            ScenarioEditorObjectInteraction.BeforeNativeCursor(__instance, out __state);

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CursorEditHospitalItem __instance,
            HospitalPlot __state) =>
            ScenarioEditorObjectInteraction.AfterNativeCursor(__instance, __state);
    }

    // Detectar la cancelacion en el propio control nativo de colocar/rotar.
    // Se ejecuta dentro de CursorRoomMove.CursorUpdate, antes de OnMoveRoomEnd.
    [HarmonyPatch(typeof(CursorControlRotatePlace), "Update")]
    internal static class ScenarioEditorNativeMultiCancelPatch
    {
        private static void Postfix(CursorControlRotatePlace __instance) =>
            ScenarioEditorObjectInteraction.TrackNativeMultiCancel(__instance);
    }

    // Intercepta SOLO el fin de nuestro CursorRoomMove multiple y evita que
    // el juego entregue todos los objetos a la parcela actualmente elegida.
    [HarmonyPatch(typeof(CursorEditHospitalItem), "OnMoveRoomEnd")]
    internal static class ScenarioEditorNativeMultiReturnPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CursorEditHospitalItem __instance,
            bool deleted, Vector3 cellOffset) =>
            !ScenarioEditorObjectInteraction.InterceptNativeMultiple(
                __instance, deleted, cellOffset);
    }

    [HarmonyPatch(typeof(LandscapeObjectsMenu), "Setup")]
    internal static class ScenarioEditorPickSearchSetupPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => ScenarioEditorObjectInteraction.RefreshSearch();
    }

    // El juego aplica su contorno en HighlightRenderer.Render mediante _Color.
    // Cambiamos SOLO el RGB del color nativo, sin crear geometria ni materiales.
    [HarmonyPatch(typeof(HighlightRenderer), "Render")]
    internal static class ScenarioEditorNativeOutlineColorPatch
    {
        private static readonly ConstructorInfo ColorConstructor =
            AccessTools.Constructor(typeof(Color),
                new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo TintMethod =
            AccessTools.Method(typeof(ScenarioEditorNativeOutlineColorPatch), nameof(Tint));
        private static readonly ConstructorInfo VectorConstructor =
            AccessTools.Constructor(typeof(Vector3),
                new[] { typeof(float), typeof(float), typeof(float) });
        private static readonly MethodInfo FullRangeMethod =
            AccessTools.Method(typeof(ScenarioEditorNativeOutlineColorPatch), nameof(FullRange));

        private static Color Tint(Color native)
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active) return native;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                return new Color(1f, 0.1f, 0.85f, native.a);
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return new Color(0.12f, 0.46f, 1f, native.a);
            return native;
        }

        // _MousePosInvRange.z es el multiplicador radial del resaltado nativo.
        // Cero elimina el degradado sin alterar el shader ni generar siluetas.
        private static Vector3 FullRange(Vector3 native)
        {
            if (ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Active)
                native.z = 0f;
            return native;
        }

        // El radio que usa el juego para RECORTAR los renderers se obtiene al
        // multiplicar el tamano de la pantalla por HighlightKeyholeSize.
        // Modificamos ese valor en el propio metodo nativo, sin acceder a
        // PostProcessEffectRenderer<T>.settings (DLL opcional del proyecto).
        private static readonly MethodInfo FullSizeMethod =
            AccessTools.Method(typeof(ScenarioEditorNativeOutlineColorPatch), nameof(FullSize));

        private static float FullSize(float native)
        {
            return ScenarioEditorFlow.IsRunning && ScenarioEditorSession.Active
                ? native * 40f : native;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool waitingForKeyholeMultiply = false;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is FieldInfo field &&
                    field.Name == "HighlightKeyholeSize")
                    waitingForKeyholeMultiply = true;

                yield return instruction;

                // Ensanchar tambien el recorte nativo de la camara: quitar
                // solo el degradado del shader dejaba extremos sin contorno.
                if (waitingForKeyholeMultiply && instruction.opcode == OpCodes.Mul)
                {
                    waitingForKeyholeMultiply = false;
                    if (FullSizeMethod != null)
                        yield return new CodeInstruction(OpCodes.Call, FullSizeMethod);
                }
                if (ColorConstructor != null && TintMethod != null &&
                    instruction.opcode == OpCodes.Newobj &&
                    Equals(instruction.operand, ColorConstructor))
                    yield return new CodeInstruction(OpCodes.Call, TintMethod);
                if (VectorConstructor != null && FullRangeMethod != null &&
                    instruction.opcode == OpCodes.Newobj &&
                    Equals(instruction.operand, VectorConstructor))
                    yield return new CodeInstruction(OpCodes.Call, FullRangeMethod);
            }
        }
    }
}
