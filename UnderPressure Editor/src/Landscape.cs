// Updated: 2026-10-15 - Visibilidad independiente de Landscape, controles y guardado XYZ/rotXYZ/scaXYZ.
// Sin modificar las parcelas ni el cargador ingame.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnderPressure.Editor
{
    // La misma estructura que las parcelas: el .upmap enlaza un archivo de datos.
    // La captura no crea objetos; documenta los elementos fijos de la escena que
    // no pertenecen a LandscapeItems_Base. Los cambios visuales se habilitaran
    // cuando hayamos validado el inventario en los hospitales originales.
    internal static class ScenarioLandscape
    {
        // Landscape es una parcela 0 LOGICA, nunca una instancia HospitalPlot.
        // Los elementos estaticos originales permanecen sin modificar: al
        // seleccionarlos se cambia exclusivamente su representacion visual
        // por el prefab dinamico, que puede resaltarse y moverse completo.
        internal static bool IsActive { get; private set; }
        internal static bool ObjectsVisible { get; private set; } = true;
        // Estado visual de cada renderer antes de ocultar el exterior. No se
        // cambia la escena guardada, ningun prefab ni las parcelas nativas.
        private static readonly Dictionary<Renderer, bool> VisibilityBeforeHide =
            new Dictionary<Renderer, bool>();

        internal static void SetObjectsVisible(bool visible)
        {
            if (ObjectsVisible == visible) return;
            RestoreExteriorVisibility();
            ObjectsVisible = visible;
            if (!visible) HideExteriorObjects();
        }

        private static void RestoreExteriorVisibility()
        {
            foreach (var item in VisibilityBeforeHide)
                if (item.Key != null) item.Key.enabled = item.Value;
            VisibilityBeforeHide.Clear();
        }

        // Los mismos filtros que Capture: exteriores originales, NO objetos
        // de parcelas, terrenos ni decoracion del LandscapeItems_Base.
        private static void HideExteriorObjects()
        {
            if (ObjectsVisible) return;
            var plotVisualRoots = GetPlotVisualRoots();
            var visited = new HashSet<Transform>();
            foreach (var node in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (node == null || !node.gameObject.activeInHierarchy ||
                    InsideOriginalBaseOrTerrain(node) ||
                    InsidePlotVisual(node, plotVisualRoots) ||
                    !HasOwnStaticRenderer(node.gameObject) ||
                    HasStaticRendererAncestor(node)) continue;
                var root = RootFor(node);
                if (root != null) visited.Add(root);
            }
            // Identificar todas las raices ANTES de ocultar renderizadores:
            // los flags de static batching pueden depender del estado visual.
            foreach (var root in visited)
                if (root != null)
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        HideRenderer(renderer);
            // Si existe un prefab de previsualizacion editable, ocultarlo
            // tambien sin destruirlo ni perder su posicion transformada.
            foreach (var selectable in Selectables)
                if (selectable.Visual != null)
                    foreach (var renderer in selectable.Visual.GetComponentsInChildren<Renderer>(true))
                        HideRenderer(renderer);
        }

        private static void HideRenderer(Renderer renderer)
        {
            if (renderer == null) return;
            if (!VisibilityBeforeHide.ContainsKey(renderer))
                VisibilityBeforeHide.Add(renderer, renderer.enabled);
            renderer.enabled = false;
        }

        private sealed class Member
        {
            internal ObjectEntry Entry;
            internal Transform Original;
        }
        private sealed class Selectable
        {
            internal Transform Node;
            internal readonly List<Member> Members = new List<Member>();
            internal Renderer[] Renderers;
            internal GameObject Visual;
            internal readonly Dictionary<Renderer, bool> HiddenRenderers =
                new Dictionary<Renderer, bool>();
            internal readonly Dictionary<Collider, bool> HiddenColliders =
                new Dictionary<Collider, bool>();
            internal Vector3 InitialPosition;
            internal Quaternion InitialRotation;
            internal float InitialYaw;
        }
        private static readonly List<Selectable> Selectables = new List<Selectable>();
        private static readonly Dictionary<string, ObjectEntry> Edited =
            new Dictionary<string, ObjectEntry>(StringComparer.Ordinal);
        private static Dictionary<string, GameObject> _prefabs;
        private static readonly MethodInfo NativeHighlight =
            AccessTools.Method(typeof(HighlightManager), "HighlightObjects",
                new[] { typeof(List<Renderer>) });
        private static Selectable _selected;
        private static Selectable _hoverPreview;
        private static Level _editorLevel;
        private static bool _missingHighlightLogged;
        private static bool _carrying;
        private static readonly Dictionary<string, ObjectEntry> _beforePickup =
            new Dictionary<string, ObjectEntry>(StringComparer.Ordinal);
        private static Vector2 _mouseStart;
        private static Vector3 _worldMouseStart;
        private static Vector3 _objectStart;
        private static float _objectYawStart;
        private static float _grabPlaneY;
        private static CursorControlRotatePlace _nativeRotateControl;
        private static bool _dragPlaneFound;
        private static Data _loaded;
        private static bool _warnedMissingPrefab;

        private static void ReleaseNativeRotate()
        {
            if (_nativeRotateControl == null) return;
            try { _nativeRotateControl.Destroy(); }
            catch (Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Landscape: cursor nativo al liberar: " + ex.Message);
            }
            _nativeRotateControl = null;
        }

        internal static void SetMapFile(string path)
        {
            // Volver a mostrar los renderizadores antes de reconstruir la
            // seleccion: si no, heredariamos el estado oculto como original.
            RestoreExteriorVisibility();
            ReleaseNativeRotate();
            _carrying = false;
            _beforePickup.Clear();
            foreach (var candidate in Selectables) RestoreVisual(candidate);
            Selectables.Clear();
            _loaded = null;
            Edited.Clear();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _loaded = Read(path);
                foreach (var e in _loaded.objects)
                    if (e != null && !string.IsNullOrEmpty(e.id)) Edited[e.id] = e;
            }
            if (IsActive) { RebuildSelectables(); ApplySavedPositions(); }
            if (!ObjectsVisible) HideExteriorObjects();
        }

        internal static void Activate(Level level)
        {
            if (level == null || !ScenarioEditorSession.Active ||
                !ReferenceEquals(level, ScenarioEditorSession.Level)) return;
            if (IsActive && ReferenceEquals(level, _editorLevel)) return;
            Deselect();
            IsActive = true;
            if (ReferenceEquals(_editorLevel, level) && Selectables.Count > 0)
            {
                // Retomar las representaciones editables ya desplazadas.
            }
            else
            {
                _editorLevel = level;
                RebuildSelectables();
                ApplySavedPositions();
            }
            if (!ObjectsVisible) HideExteriorObjects();
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Landscape: parcela 0 activada, " + Selectables.Count +
                " objetos exteriores independientes; las parcelas siguen intactas.");
        }

        internal static void Deselect()
        {
            ReleaseNativeRotate();
            _carrying = false;
            _beforePickup.Clear();
            _selected = null;
            _hoverPreview = null;
            // Los objetos desplazados deben seguir visibles tambien mientras
            // se trabaja en otra parcela. Solo restaurar previews intactos.
            foreach (var candidate in Selectables)
                if (!HasChanges(candidate)) RestoreVisual(candidate);
            if (!ObjectsVisible) HideExteriorObjects();
            IsActive = false;
            _missingHighlightLogged = false;
            _warnedMissingPrefab = false;
        }

        internal static void Release()
        {
            RestoreExteriorVisibility();
            ObjectsVisible = true;
            ReleaseNativeRotate();
            foreach (var candidate in Selectables) RestoreVisual(candidate);
            Selectables.Clear();
            _editorLevel = null;
            IsActive = false;
            _selected = null;
            _hoverPreview = null;
            _carrying = false;
            _beforePickup.Clear();
            _loaded = null;
            Edited.Clear();
            _prefabs = null;
        }

        private static bool HasChanges(Selectable candidate)
        {
            foreach (var member in candidate.Members)
            {
                ObjectEntry entry;
                if (!Edited.TryGetValue(member.Entry.id, out entry)) continue;
                var original = member.Original;
                if (Vector3.Distance(new Vector3(entry.x, entry.y, entry.z),
                        original.position) > .001f ||
                    Mathf.Abs(Mathf.DeltaAngle(entry.rotation,
                        original.eulerAngles.y)) > .01f || entry.deleted)
                    return true;
            }
            return false;
        }

        internal static void RefreshAfterMapLoad()
        {
            RestoreExteriorVisibility();
            if (IsActive && _editorLevel != null)
            {
                RebuildSelectables();
                ApplySavedPositions();
            }
            if (!ObjectsVisible) HideExteriorObjects();
        }

        // Se agrupan todas las piezas de un mismo prefab (tejado, fachada,
        // muro hijo...). La malla combinada NO define el limite del objeto.
        private static Transform RootFor(Transform node)
        {
            var selected = node;
            for (var parent = node.parent; parent != null; parent = parent.parent)
            {
                if (InsideOriginalBaseOrTerrain(parent)) break;
                if (IsExteriorModelName(parent.name)) selected = parent;
                else if (IsExteriorModelName(selected.name)) break;
            }
            return selected;
        }

        private static void RebuildSelectables()
        {
            ReleaseNativeRotate();
            foreach (var old in Selectables) RestoreVisual(old);
            Selectables.Clear();
            _selected = null;
            _hoverPreview = null;
            _carrying = false;
            _beforePickup.Clear();
            var roots = GetPlotVisualRoots();
            var entries = Capture(_loaded).objects;
            var indexed = new Dictionary<string, ObjectEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
                if (entry != null && !string.IsNullOrEmpty(entry.id)) indexed[entry.id] = entry;
            var groups = new Dictionary<Transform, Selectable>();
            foreach (var node in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (node == null || !node.gameObject.activeInHierarchy ||
                    InsideOriginalBaseOrTerrain(node) || InsidePlotVisual(node, roots) ||
                    !HasOwnStaticRenderer(node.gameObject) || HasStaticRendererAncestor(node))
                    continue;
                var key = StableId(OriginalHierarchy(node), node.position);
                if (!indexed.TryGetValue(key, out var entry)) continue;
                var groupRoot = RootFor(node);
                if (!groups.TryGetValue(groupRoot, out var group))
                {
                    group = new Selectable {
                        Node = groupRoot,
                        InitialPosition = groupRoot.position,
                        InitialRotation = groupRoot.rotation,
                        InitialYaw = groupRoot.eulerAngles.y,
                        Renderers = groupRoot.GetComponentsInChildren<Renderer>(true)
                    };
                    groups.Add(groupRoot, group);
                    if (group.Renderers.Length > 0) Selectables.Add(group);
                }
                group.Members.Add(new Member { Entry = entry, Original = node });
            }
        }

        private static void ApplySavedPositions()
        {
            foreach (var selectable in Selectables)
            {
                var member = selectable.Members.FirstOrDefault(m => m.Original == selectable.Node)
                    ?? selectable.Members.FirstOrDefault();
                if (member == null) continue;
                ObjectEntry saved;
                if (!Edited.TryGetValue(member.Entry.id, out saved)) continue;
                var move = new Vector3(saved.x - member.Original.position.x,
                    saved.y - member.Original.position.y,
                    saved.z - member.Original.position.z);
                if (move.sqrMagnitude < .000001f &&
                    Mathf.Abs(Mathf.DeltaAngle(member.Original.eulerAngles.y,
                        saved.rotation)) < .01f) continue;
                if (!MakeEditable(selectable)) continue;
                selectable.Visual.transform.rotation =
                    Quaternion.Euler(0f, Mathf.DeltaAngle(selectable.InitialYaw,
                        selectable.InitialYaw + saved.rotation - member.Original.eulerAngles.y), 0f) *
                    selectable.InitialRotation;
                SetPosition(selectable, selectable.InitialPosition + move);
            }
        }

        private static Selectable Pick(Vector2 pointer)
        {
            var camera = Camera.main;
            if (camera == null) return null;
            var ray = camera.ScreenPointToRay(pointer);
            float best = float.PositiveInfinity;
            Selectable closest = null;
            foreach (var candidate in Selectables)
            {
                if (candidate.Node == null || !candidate.Node.gameObject.activeInHierarchy)
                    continue;
                foreach (var renderer in candidate.Renderers)
                {
                    if (renderer == null || !renderer.enabled ||
                        !renderer.gameObject.activeInHierarchy) continue;
                    float distance;
                    if (!renderer.bounds.IntersectRay(ray, out distance) ||
                        distance >= best) continue;
                    best = distance;
                    closest = candidate;
                }
            }
            return closest;
        }

        // Conserva la altura ORIGINAL de la malla sin saltar al pivote
        // del prefab. El punto bajo el raton marca el plano de arrastre,
        // pero el desplazamiento se aplica al pivote original.
        private static float GrabbedSurfaceY(Selectable candidate, Vector2 pointer)
        {
            if (candidate == null) return 0f;
            if (Camera.main == null) return candidate.InitialPosition.y;
            var ray = Camera.main.ScreenPointToRay(pointer);
            var best = float.PositiveInfinity;
            foreach (var renderer in candidate.Renderers)
            {
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy) continue;
                float distance;
                if (renderer.bounds.IntersectRay(ray, out distance) && distance < best)
                    best = distance;
            }
            return best < float.PositiveInfinity
                ? ray.GetPoint(best).y
                : (candidate.Visual != null ? candidate.Visual.transform.position.y
                    : candidate.InitialPosition.y);
        }

        private static Dictionary<string, GameObject> Prefabs()
        {
            if (_prefabs != null) return _prefabs;
            _prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            var definitions = _editorLevel?.App?.Metagame?.LandscapeItemDatabase?.Instance?.RoomItems;
            if (definitions != null)
                foreach (var reference in definitions)
                {
                    var definition = reference?.Instance as RoomItemDefinition;
                    if (definition == null) continue;
                    try
                    {
                        var prefab = definition.GetPrefab(0);
                        if (prefab != null && !_prefabs.ContainsKey(prefab.name))
                            _prefabs.Add(prefab.name, prefab);
                    }
                    catch (Exception) { }
                }
            // Las casas y formaciones del mapa tambien pueden proceder de
            // prefabs de escena que NO existen como RoomItemDefinition.
            foreach (var prefab in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (prefab == null || prefab.scene.IsValid() ||
                    !IsExteriorModelName(prefab.name) || _prefabs.ContainsKey(prefab.name))
                    continue;
                if (prefab.GetComponentsInChildren<Renderer>(true).Length > 0)
                    _prefabs.Add(prefab.name, prefab);
            }
            return _prefabs;
        }

        private static bool MakeEditable(Selectable selected)
        {
            if (selected == null || selected.Node == null) return false;
            if (selected.Visual != null) return true;
            var root = selected.Node;
            var key = NormalizeSourceName(root.name);
            GameObject prefab;
            if (!Prefabs().TryGetValue(key, out prefab))
            {
                if (!_warnedMissingPrefab)
                {
                    _warnedMissingPrefab = true;
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Landscape: algunos modelos estaticos no tienen prefab independiente (ejemplo: " +
                        key + "). Se conservaran visibles y no se moveran hasta resolver su modelo.");
                }
                return false;
            }
            GameObject replacement = null;
            try
            {
                replacement = UnityEngine.Object.Instantiate(prefab,
                    selected.InitialPosition, selected.InitialRotation);
                replacement.name = "UP Landscape Editable - " + key;
                replacement.SetActive(true);
                var renders = replacement.GetComponentsInChildren<Renderer>(true);
                if (renders.Length == 0) throw new InvalidOperationException("Prefab sin renderer: " + key);
                selected.Visual = replacement;
                foreach (var renderer in selected.Renderers)
                {
                    if (renderer == null) continue;
                    selected.HiddenRenderers[renderer] = renderer.enabled;
                    renderer.enabled = false;
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null) continue;
                    selected.HiddenColliders[collider] = collider.enabled;
                    collider.enabled = false;
                }
                selected.Renderers = renders;
                if (!ObjectsVisible) HideExteriorObjects();
                return true;
            }
            catch (Exception ex)
            {
                if (replacement != null) UnityEngine.Object.Destroy(replacement);
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Landscape: prefab exterior no editable " + key + ": " + ex.Message);
                RestoreVisual(selected);
                return false;
            }
        }

        private static void RestoreVisual(Selectable selected)
        {
            if (selected == null) return;
            foreach (var entry in selected.HiddenRenderers)
                if (entry.Key != null)
                {
                    entry.Key.enabled = entry.Value;
                    // Si el preview se destruye estando el check apagado,
                    // recordar que la malla original vuelve a estar visible.
                    if (!ObjectsVisible && VisibilityBeforeHide.ContainsKey(entry.Key))
                        VisibilityBeforeHide[entry.Key] = entry.Value;
                }
            foreach (var entry in selected.HiddenColliders)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            selected.HiddenRenderers.Clear();
            selected.HiddenColliders.Clear();
            if (selected.Visual != null) UnityEngine.Object.Destroy(selected.Visual);
            selected.Visual = null;
            if (selected.Node != null)
                selected.Renderers = selected.Node.GetComponentsInChildren<Renderer>(true);
        }

        private static void SetPosition(Selectable selected, Vector3 destination)
        {
            if (selected.Visual == null) return;
            selected.Visual.transform.position = destination;
            var pivot = selected.InitialPosition;
            var rotateDelta = Quaternion.Euler(0f,
                selected.Visual.transform.eulerAngles.y - selected.InitialYaw, 0f);
            foreach (var member in selected.Members)
            {
                var original = member.Original.position;
                var transformed = destination + rotateDelta * (original - pivot);
                ObjectEntry modified;
                if (!Edited.TryGetValue(member.Entry.id, out modified))
                    modified = member.Entry;
                modified.x = transformed.x;
                modified.y = transformed.y;
                modified.z = transformed.z;
                modified.rotation = member.Original.eulerAngles.y +
                    Mathf.DeltaAngle(selected.InitialYaw, selected.Visual.transform.eulerAngles.y);
                modified.rotY = modified.rotation;
                Edited[member.Entry.id] = modified;
            }
        }

        private static bool PlanePoint(Vector2 pointer, float y, out Vector3 point)
        {
            point = Vector3.zero;
            if (Camera.main == null) return false;
            var ray = Camera.main.ScreenPointToRay(pointer);
            float distance;
            if (!new Plane(Vector3.up, new Vector3(0f, y, 0f))
                .Raycast(ray, out distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private static ObjectEntry CopyEntry(ObjectEntry value)
        {
            return new ObjectEntry {
                id = value.id, name = value.name, hierarchy = value.hierarchy,
                prefab = value.prefab, guid = value.guid, tag = value.tag,
                originalX = value.originalX, originalY = value.originalY,
                originalZ = value.originalZ, originalRotation = value.originalRotation,
                x = value.x, y = value.y, z = value.z,
                rotation = value.rotation, deleted = value.deleted,
                rotX = value.rotX, rotY = value.rotY, rotZ = value.rotZ,
                scaX = value.scaX, scaY = value.scaY, scaZ = value.scaZ
            };
        }

        private static void CancelPickup()
        {
            if (!_carrying || _selected == null) return;
            if (_selected.Visual != null)
            {
                _selected.Visual.transform.position = _objectStart;
                _selected.Visual.transform.rotation =
                    Quaternion.Euler(0f, _objectYawStart - _selected.InitialYaw, 0f) *
                    _selected.InitialRotation;
            }
            foreach (var entry in _beforePickup)
                Edited[entry.Key] = entry.Value;
            _beforePickup.Clear();
            _carrying = false;
            ReleaseNativeRotate();
            UnderPressureEditorPlugin.Log?.LogInfo("Landscape: movimiento cancelado.");
        }

        private static void PlacePickup()
        {
            if (!_carrying) return;
            _carrying = false;
            _beforePickup.Clear();
            ReleaseNativeRotate();
            if (_selected?.Visual != null)
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Landscape: objeto colocado con cursor nativo: " +
                    _selected.Node.name + " en " + _selected.Visual.transform.position +
                    ", rotacion=" + _selected.Visual.transform.eulerAngles.y);
        }

        // Reutiliza CursorControlRotatePlace de TPH: EXACTAMENTE las mismas
        // teclas, sensibilidad del raton y gesto de rotacion que CursorRoomItem.
        // No llama al FloorPlan ni ajusta a la cuadricula, pues el exterior
        // no es propiedad de ninguna parcela.
        internal static void UpdateSelection()
        {
            if (!IsActive || _editorLevel == null ||
                !ScenarioEditorSession.Active ||
                !ReferenceEquals(_editorLevel, ScenarioEditorSession.Level)) return;
            if (ScenarioEditorStorageMenu.IsDialogOpen ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                return;

            var hovered = _carrying ? _selected : Pick(Input.mousePosition);
            if (!_carrying && !ReferenceEquals(_hoverPreview, hovered))
            {
                if (_hoverPreview != null && _hoverPreview != _selected &&
                    !HasChanges(_hoverPreview)) RestoreVisual(_hoverPreview);
                _hoverPreview = hovered;
            }
            if (hovered != null && hovered.Visual == null)
                MakeEditable(hovered);

            if (_carrying && _selected?.Visual != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CancelPickup();
                else
                {
                    // Se invoca el MISMO control que usa CursorRoomItem.
                    // CursorControlRotatePlace ignora la pulsacion inicial
                    // hasta que se suelta el boton del raton.
                    if (_nativeRotateControl != null && _editorLevel.InputManager != null)
                    {
                        _nativeRotateControl.Update(_editorLevel.InputManager,
                            _editorLevel, 0.1f, RoomItemDefinition.Size.Large, null);
                        if (_nativeRotateControl.Cancel) CancelPickup();
                        else
                        {
                            var wantedYaw = _nativeRotateControl.SnapRotation;
                            _selected.Visual.transform.rotation =
                                Quaternion.Euler(0f, wantedYaw - _selected.InitialYaw, 0f) *
                                _selected.InitialRotation;
                            // Al girar con el raton mantenido, el juego detiene
                            // el movimiento hasta terminar la rotacion.
                            if (!_nativeRotateControl.Rotating)
                            {
                                Vector3 current;
                                if (PlanePoint(Input.mousePosition, _grabPlaneY, out current))
                                    SetPosition(_selected,
                                        _objectStart + current - _worldMouseStart);
                                else SetPosition(_selected, _selected.Visual.transform.position);
                            }
                            else SetPosition(_selected, _selected.Visual.transform.position);
                            if (_nativeRotateControl.Place) PlacePickup();
                        }
                    }
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                _selected = hovered;
                if (_selected != null && MakeEditable(_selected))
                {
                    _objectStart = _selected.Visual.transform.position;
                    _objectYawStart = _selected.Visual.transform.eulerAngles.y;
                    _mouseStart = Input.mousePosition;
                    _grabPlaneY = GrabbedSurfaceY(_selected, _mouseStart);
                    _dragPlaneFound = PlanePoint(_mouseStart, _grabPlaneY,
                        out _worldMouseStart);
                    _beforePickup.Clear();
                    foreach (var member in _selected.Members)
                    {
                        ObjectEntry entry;
                        if (!Edited.TryGetValue(member.Entry.id, out entry))
                            entry = member.Entry;
                        _beforePickup[member.Entry.id] = CopyEntry(entry);
                    }
                    if (_dragPlaneFound && _editorLevel.CursorManager != null)
                    {
                        ReleaseNativeRotate();
                        _nativeRotateControl = new CursorControlRotatePlace(
                            _objectYawStart, _editorLevel.CursorManager);
                        _nativeRotateControl.Initialise();
                        _carrying = true;
                    }
                }
            }

            var highlighted = _carrying ? _selected : hovered ?? _selected;
            if (highlighted == null || _editorLevel.HighlightManager == null) return;
            if (NativeHighlight == null)
            {
                if (!_missingHighlightLogged)
                {
                    _missingHighlightLogged = true;
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Landscape: HighlightManager.HighlightObjects no disponible.");
                }
                return;
            }
            var visible = new List<Renderer>();
            foreach (var renderer in highlighted.Renderers)
                if (renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy) visible.Add(renderer);
            if (visible.Count > 0)
                try { NativeHighlight.Invoke(_editorLevel.HighlightManager,
                    new object[] { visible }); }
                catch (Exception ex)
                {
                    if (!_missingHighlightLogged)
                    {
                        _missingHighlightLogged = true;
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Landscape: resaltado nativo no disponible: " + ex.Message);
                    }
                }
        }

        internal const string FileName = "landscape.json";
        private const int Version = 1;
        private const int MaxFileBytes = 24 * 1024 * 1024;
        private const int MaxObjects = 20000;

        [Serializable]
        internal sealed class Data
        {
            public int version = Version;
            public ObjectEntry[] objects = new ObjectEntry[0];
            public Addition[] added = new Addition[0];
        }

        [Serializable]
        internal sealed class ObjectEntry
        {
            // ID derivado del nombre, jerarquia y posicion ORIGINALES.
            // Permanece invariable aunque mas adelante se desplace el objeto.
            public string id = "";
            public string name = "";
            public string hierarchy = "";
            // Nombre del modelo fuente, cuando se pueda deducir del GameObject.
            // GUID/DebugTag quedan vacios hasta verificar la definicion nativa.
            public string prefab = "";
            public string guid = "";
            public string tag = "";
            public float originalX;
            public float originalY;
            public float originalZ;
            public float originalRotation;
            public float x;
            public float y;
            public float z;
            public float rotation;
            // Transformacion completa; el JSON conserva los valores no predeterminados.
            public float rotX;
            public float rotY;
            public float rotZ;
            public float scaX = 1f;
            public float scaY = 1f;
            public float scaZ = 1f;
            public bool deleted;
        }

        [Serializable]
        internal sealed class Addition
        {
            public string guid = "";
            public string tag = "";
            public string prefab = "";
            public float x = 0f;
            public float y = 0f;
            public float z = 0f;
            public float rotation = 0f;
            public float rotX = 0f;
            public float rotY;
            public float rotZ = 0f;
            public float scaX = 1f;
            public float scaY = 1f;
            public float scaZ = 1f;
        }

        private static bool HasCompactField(string text, string field)
        {
            return !string.IsNullOrEmpty(text) &&
                text.IndexOf("\"" + field + "\"", StringComparison.Ordinal) >= 0;
        }

        internal static Data Read(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxFileBytes)
                throw new InvalidDataException("landscape.json ausente, vacio o demasiado grande.");
            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonUtility.FromJson<Data>(json);
            if (data == null || data.version != Version)
                throw new InvalidDataException("Version de landscape.json no compatible.");
            // Los primeros landscape.json podian contener solo { "version": 1 }.
            // Se aceptan para permitir reabrir y corregir esos mapas al guardar.
            // Los arrays presentes SIEMPRE se leen elemento a elemento porque
            // JsonUtility del juego puede ignorar arrays de clases anidadas.
            data.objects = json.Contains("\"objects\"")
                ? ScenarioMapFormat.ParseObjects<ObjectEntry>(json, "objects")
                : new ObjectEntry[0];
            data.added = json.Contains("\"added\"")
                ? ScenarioMapFormat.ParseObjects<Addition>(json, "added")
                : new Addition[0];
            // La ausencia de rotX/rotZ/escala en ficheros antiguos se marca
            // solo en memoria; Capture recupera esos valores de la escena.
            var sourceEntries = json.Contains("\"objects\"")
                ? ScenarioMapFormat.ArrayElements(json, "objects")
                : new List<string>();
            if (sourceEntries.Count != data.objects.Length)
                throw new InvalidDataException("Landscape: inventario original incompleto.");
            for (var i = 0; i < data.objects.Length; i++)
            {
                var entry = data.objects[i];
                if (entry == null) continue;
                var raw = sourceEntries[i];
                if (HasCompactField(raw, "rotY")) entry.rotation = entry.rotY;
                else entry.rotY = entry.rotation;
                var legacy = HasCompactField(raw, "originalX") ||
                    HasCompactField(raw, "originalRotation") ||
                    HasCompactField(raw, "rotation");
                if (!HasCompactField(raw, "rotX")) entry.rotX = legacy ? float.NaN : 0f;
                if (!HasCompactField(raw, "rotZ")) entry.rotZ = legacy ? float.NaN : 0f;
                if (!HasCompactField(raw, "scaX")) entry.scaX = legacy ? float.NaN : 1f;
                if (!HasCompactField(raw, "scaY")) entry.scaY = legacy ? float.NaN : 1f;
                if (!HasCompactField(raw, "scaZ")) entry.scaZ = legacy ? float.NaN : 1f;
            }
            var addedEntries = json.Contains("\"added\"")
                ? ScenarioMapFormat.ArrayElements(json, "added")
                : new List<string>();
            if (addedEntries.Count != data.added.Length)
                throw new InvalidDataException("Landscape: inventario de anadidos incompleto.");
            for (var i = 0; i < data.added.Length; i++)
            {
                var entry = data.added[i];
                if (entry == null) continue;
                var raw = addedEntries[i];
                if (HasCompactField(raw, "rotY")) entry.rotation = entry.rotY;
                else entry.rotY = entry.rotation;
                if (!HasCompactField(raw, "scaX")) entry.scaX = 1f;
                if (!HasCompactField(raw, "scaY")) entry.scaY = 1f;
                if (!HasCompactField(raw, "scaZ")) entry.scaZ = 1f;
            }
            if (data.objects.Length > MaxObjects || data.added.Length > MaxObjects)
                throw new InvalidDataException("Demasiados objetos exteriores.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in data.objects)
                if (item == null || string.IsNullOrEmpty(item.id) ||
                    string.IsNullOrEmpty(item.name) || !identities.Add(item.id))
                    throw new InvalidDataException("landscape.json contiene una identidad exterior repetida o vacia.");
            foreach (var item in data.added)
                if (item == null ||
                    (string.IsNullOrEmpty(item.guid) && string.IsNullOrEmpty(item.tag) &&
                     string.IsNullOrEmpty(item.prefab)))
                    throw new InvalidDataException("Objeto nuevo de Landscape sin identidad.");
            return data;
        }

        // Capturar exclusivamente objetos fijos de la escena fuera de la capa
        // Base original: los objetos reconstruidos desde las parcelas quedan
        // dentro de LandscapeItems_Base y no se copian a este fichero.
        internal static Data Capture(Data previous = null)
        {
            var state = new Data();
            var prior = new Dictionary<string, ObjectEntry>(StringComparer.Ordinal);
            if (previous?.objects != null)
                foreach (var old in previous.objects)
                    if (old != null && !string.IsNullOrEmpty(old.id))
                        prior[old.id] = old;

            var collected = new SortedDictionary<string, ObjectEntry>(StringComparer.Ordinal);
            var examined = 0;
            var outsideBase = 0;
            var outsideWithRenderer = 0;
            var outsideStatic = 0;
            var outsideBatched = 0;
            var likelyExteriorMeshes = 0;
            var skippedByAncestor = 0;
            var skippedPlotVisuals = 0;
            // La decoracion de las parcelas editables puede estar FUERA de
            // LandscapeItems_Base. Identificarla por sus instancias nativas,
            // nunca por nombres, posiciones o por Combined Mesh.
            var plotVisualRoots = GetPlotVisualRoots();
            foreach (var node in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (node == null || !node.gameObject.activeInHierarchy) continue;
                ++examined;
                var go = node.gameObject;
                if (InsideOriginalBaseOrTerrain(node)) continue;
                if (InsidePlotVisual(node, plotVisualRoots))
                {
                    ++skippedPlotVisuals;
                    continue;
                }
                ++outsideBase;
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    ++outsideWithRenderer;
                    if (node.name.StartsWith("A_LS_", StringComparison.OrdinalIgnoreCase) ||
                        node.name.StartsWith("A_P_LS_", StringComparison.OrdinalIgnoreCase))
                        ++likelyExteriorMeshes;
                }
                if (go.isStatic) ++outsideStatic;
                if (renderer != null && renderer.isPartOfStaticBatch) ++outsideBatched;
                // En la version de Unity del juego, go.isStatic puede ser
                // false incluso para MeshRenderer incluidos en static batching.
                // Tambien reconocer los prefabs exteriores A_LS_/A_P_LS_
                // cuando la geometria combinada no declare el flag.
                if (!HasOwnStaticRenderer(go)) continue;
                // Un objeto compuesto se representa por su padre editable,
                // no por cada Renderer de sus hijos.
                if (HasStaticRendererAncestor(node))
                {
                    ++skippedByAncestor;
                    continue;
                }

                var point = node.position;
                var angles = node.eulerAngles;
                var angle = angles.y;
                var scale = node.localScale;
                var hierarchy = OriginalHierarchy(node);
                var id = StableId(hierarchy, point);
                if (collected.ContainsKey(id))
                {
                    // La escena contiene a veces instancias identicas en la
                    // misma posicion. No perderlas ni sobreescribir la anterior.
                    var suffix = 2;
                    var prefix = id;
                    while (collected.ContainsKey(id)) id = prefix + "_" + suffix++;
                }

                var item = new ObjectEntry
                {
                    id = id, name = node.name, hierarchy = hierarchy,
                    prefab = NormalizeSourceName(node.name),
                    originalX = point.x, originalY = point.y, originalZ = point.z,
                    originalRotation = angle,
                    x = point.x, y = point.y, z = point.z, rotation = angle,
                    rotX = angles.x, rotY = angles.y, rotZ = angles.z,
                    scaX = scale.x, scaY = scale.y, scaZ = scale.z
                };
                ObjectEntry edited;
                if (Edited.TryGetValue(id, out edited) || prior.TryGetValue(id, out edited))
                {
                    item.x = edited.x;
                    item.y = edited.y;
                    item.z = edited.z;
                    item.rotation = edited.rotation;
                    item.rotY = edited.rotation;
                    if (!float.IsNaN(edited.rotX)) item.rotX = edited.rotX;
                    if (!float.IsNaN(edited.rotZ)) item.rotZ = edited.rotZ;
                    if (!float.IsNaN(edited.scaX)) item.scaX = edited.scaX;
                    if (!float.IsNaN(edited.scaY)) item.scaY = edited.scaY;
                    if (!float.IsNaN(edited.scaZ)) item.scaZ = edited.scaZ;
                    item.deleted = edited.deleted;
                }
                collected.Add(id, item);
            }
            state.objects = collected.Values.ToArray();
            state.added = previous?.added ?? new Addition[0];
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Landscape: inventario exterior capturado: " + state.objects.Length +
                " objetos originales y " + state.added.Length + " anadidos; " +
                "examinados=" + examined + ", fuera_de_Base_y_Terrain=" + outsideBase +
                ", con_renderer=" + outsideWithRenderer + ", estaticos=" + outsideStatic +
                ", static_batching=" + outsideBatched +
                ", modelos_exteriores=" + likelyExteriorMeshes +
                ", visuales_parcela_omitidos=" + skippedPlotVisuals +
                ", hijos_omitidos=" + skippedByAncestor + ". " +
                "Sin modificar la escena ni los objetos de las parcelas.");
            if (state.objects.Length == 0 && likelyExteriorMeshes > 0)
                throw new InvalidOperationException(
                    "Landscape: se encontraron " + likelyExteriorMeshes +
                    " modelos exteriores originales, pero se capturaron 0 objetos. " +
                    "No se reemplazara landscape.json por un inventario vacio; " +
                    "revisa el registro Landscape para identificar el filtro.");
            if (state.objects.Length == 0)
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Landscape: ningun objeto exterior capturado. Revisar los contadores " +
                    "anteriores; no implica que el hospital carezca de exteriores.");
            return state;
        }

        private static bool InsideOriginalBaseOrTerrain(Transform node)
        {
            for (var tr = node; tr != null; tr = tr.parent)
            {
                if (tr.name == "LandscapeItems_Base" ||
                    tr.name.StartsWith("UP Landscape Editable - ", StringComparison.Ordinal) ||
                    tr.name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasOwnStaticRenderer(GameObject go)
        {
            var originalExterior = IsExteriorModelName(go.name);
            foreach (var renderer in go.GetComponents<Renderer>())
                if (renderer != null && (go.isStatic || renderer.isPartOfStaticBatch ||
                                         originalExterior))
                    return true;
            return false;
        }

        private static bool IsExteriorModelName(string name) =>
            !string.IsNullOrEmpty(name) &&
            (name.StartsWith("A_LS_", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("A_P_LS_", StringComparison.OrdinalIgnoreCase));

        private static bool HasStaticRendererAncestor(Transform node)
        {
            for (var tr = node.parent; tr != null; tr = tr.parent)
                if (HasOwnStaticRenderer(tr.gameObject)) return true;
            return false;
        }

        private static HashSet<Transform> GetPlotVisualRoots()
        {
            var roots = new HashSet<Transform>();
            var plots = ScenarioEditorSession.Level?.WorldState?.HospitalPlots;
            if (plots == null) return roots;
            foreach (var plot in plots)
            {
                var items = plot?.HospitalMap?.FloorPlan?.LandscapeItems;
                if (items == null) continue;
                foreach (var item in items)
                {
                    var visual = item?.Visual?.GameObject;
                    if (visual != null) roots.Add(visual.transform);
                }
            }
            return roots;
        }

        private static bool InsidePlotVisual(Transform node, HashSet<Transform> roots)
        {
            for (var tr = node; tr != null; tr = tr.parent)
                if (roots.Contains(tr)) return true;
            return false;
        }

        private static string NormalizeSourceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var value = name.Trim();
            if (value.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 7).TrimEnd();
            var start = value.LastIndexOf('(');
            if (start >= 0 && value.EndsWith(")", StringComparison.Ordinal))
            {
                var indexText = value.Substring(start + 1, value.Length - start - 2);
                if (int.TryParse(indexText, out var index))
                    value = value.Substring(0, start).TrimEnd();
            }
            return value;
        }

        private static string OriginalHierarchy(Transform node)
        {
            var names = new List<string>();
            for (var tr = node; tr != null; tr = tr.parent)
                names.Add(tr.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string StableId(string hierarchy, Vector3 point)
        {
            var inv = CultureInfo.InvariantCulture;
            var key = hierarchy + "|" + point.x.ToString("F4", inv) + "|" +
                point.y.ToString("F4", inv) + "|" + point.z.ToString("F4", inv);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                // Identificador compacto; no depende de InstanceID ni de Combined Mesh.
                var result = new StringBuilder(32);
                for (var i = 0; i < 16; ++i) result.Append(hash[i].ToString("x2", inv));
                return result.ToString();
            }
        }
    private static class CompactSerializer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [HarmonyPatch(typeof(ScenarioMapFormat), "WriteJson")]
        private static class SaveCompactLandscape
        {
            private static bool Prefix(string path, object value)
            {
                var landscape = value as ScenarioLandscape.Data;
                if (landscape == null) return true;
                WriteLandscape(path, landscape);
                return false;
            }
        }

        private static void WriteLandscape(string path, ScenarioLandscape.Data data)
        {
            if (data == null) throw new ArgumentNullException("data");
            var text = new StringBuilder(32768);
            text.Append("{\"version\":").Append(data.version).Append(",\"objects\":[");
            var entries = data.objects ?? new ScenarioLandscape.ObjectEntry[0];
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.id))
                    throw new InvalidDataException("Landscape: entrada sin identificador.");
                if (i > 0) text.Append(',');
                text.Append('{');
                Property(text, "id", entry.id);
                Property(text, "name", entry.name);
                Property(text, "hierarchy", entry.hierarchy);
                Property(text, "prefab", entry.prefab);
                Property(text, "guid", entry.guid);
                Property(text, "tag", entry.tag);
                Float(text, "x", entry.x, true);
                Float(text, "y", entry.y, true);
                Float(text, "z", entry.z, true);

                // Los objetos normales de parcelas solo pueden girar en Y.
                // Landscape, en cambio, conserva los tres ejes de la escena.
                var angles = new Vector3(entry.rotX, entry.rotation, entry.rotZ);
                Float(text, "rotX", angles.x, false);
                Float(text, "rotY", entry.rotation, false);
                Float(text, "rotZ", angles.z, false);

                // localScale por eje. Se omite UNICAMENTE el valor exacto 1.
                // Valores como 1.0001 se conservan sin tolerancia ni redondeo.
                var scale = new Vector3(entry.scaX, entry.scaY, entry.scaZ);
                Scale(text, "scaX", scale.x);
                Scale(text, "scaY", scale.y);
                Scale(text, "scaZ", scale.z);
                if (entry.deleted) text.Append(",\"deleted\":true");
                text.Append('}');
            }
            text.Append("],\"added\":[");
            var added = data.added ?? new ScenarioLandscape.Addition[0];
            for (var i = 0; i < added.Length; i++)
            {
                var item = added[i];
                if (item == null)
                    throw new InvalidDataException("Landscape: objeto anadido vacio.");
                if (i > 0) text.Append(',');
                text.Append('{');
                Property(text, "guid", item.guid);
                Property(text, "tag", item.tag);
                Property(text, "prefab", item.prefab);
                Float(text, "x", item.x, true);
                Float(text, "y", item.y, true);
                Float(text, "z", item.z, true);
                Float(text, "rotX", item.rotX, false);
                Float(text, "rotY", item.rotation, false);
                Float(text, "rotZ", item.rotZ, false);
                Scale(text, "scaX", item.scaX);
                Scale(text, "scaY", item.scaY);
                Scale(text, "scaZ", item.scaZ);
                text.Append('}');
            }
            text.Append("]}");
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("Landscape: directorio no disponible.");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text.ToString(), Utf8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            UnderPressureEditorPlugin.Log?.LogInfo(
                "Landscape: guardado compacto con escala y rotacion XYZ: " +
                entries.Length + " objetos y " + added.Length + " anadidos.");
        }

        private static void Scale(StringBuilder text, string key, float value)
        {
            if (value != 1f) Float(text, key, value, true);
        }

        private static void Float(StringBuilder text, string key, float value, bool always)
        {
            if (!always && value == 0f) return;
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("Landscape: valor numerico no valido: " + key);
            text.Append(",\"").Append(key).Append("\":")
                .Append(value.ToString("R", Invariant));
        }

        private static void Property(StringBuilder text, string key, string value)
        {
            if (text[text.Length - 1] != '{') text.Append(',');
            text.Append('"').Append(key).Append("\":\"");
            var str = value ?? "";
            foreach (var c in str)
            {
                switch (c)
                {
                    case '\\': text.Append("\\\\"); break;
                    case '"': text.Append("\\\""); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4", Invariant));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
    }
}
