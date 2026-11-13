// Capa global de trafico y puntos nativos de llegada/salida del escenario.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioTraffic
    {
        internal const string FileName = "traffic.json";
        private const int Version = 1;
        private const int MaxFileBytes = 4 * 1024 * 1024;
        private const int MaxObjects = 2048;

        internal const string Ambient = "ambient";
        internal const string StaticVehicle = "static_vehicle";
        internal const string RoadArrival = "road_arrival";
        internal const string HelipadArrival = "helipad_arrival";
        internal const string SubwayArrival = "subway_arrival";
        internal const string ItemArrival = "item_arrival";
        internal const string TimePortalArrival = "time_portal_arrival";
        internal const string TimePortalLanding = "time_portal_landing";
        internal const string SubwayDeparture = "subway_departure";
        internal const string TunnelDeparture = "tunnel_departure";

        internal enum Category
        {
            Ambient,
            Arrivals,
            Departures
        }

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
            public string id = "";
            public string name = "";
            public string hierarchy = "";
            public string[] components = new string[0];
            public float originalX;
            public float originalY;
            public float originalZ;
            public float originalRotation;
            public float x;
            public float y;
            public float z;
            public float rotation;
        }

        [Serializable]
        internal sealed class Addition
        {
            public string id = "";
            public string name = "";
            public string[] components = new string[0];
            public float x;
            public float y;
            public float z;
            public float rotation;
        }

        internal sealed class ItemInfo
        {
            internal string Id;
            internal string Name;
            internal Category Category;
            internal bool Added;
            internal Vector3 Position;
        }

        private sealed class Item
        {
            internal string Id;
            internal string Name;
            internal Transform Transform;
            internal string[] Components;
            internal bool Added;
            internal Vector3 OriginalPosition;
            internal Quaternion OriginalRotation;
            internal GameObject Marker;
            internal Renderer[] Renderers;
        }

        // Adaptador de edicion: el objeto funcional de la escena permanece
        // intacto y el cursor del juego opera sobre un LandscapeRoomItem real.
        private sealed class NativeProxy
        {
            internal Item Source;
            internal LandscapeRoomItem RoomItem;
            internal GameObject Prefab;
            internal Vector3 SourceOffsetLocal;
            internal List<Mesh> BakedMeshes;
            internal Renderer[] SourceRenderers;
            internal bool[] RendererStates;
        }

        private struct TransformState
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly Dictionary<string, ObjectEntry> Edited =
            new Dictionary<string, ObjectEntry>(StringComparer.Ordinal);
        private static readonly List<Addition> Additions = new List<Addition>();
        private static readonly Dictionary<Transform, TransformState> OriginalStates =
            new Dictionary<Transform, TransformState>();
        private static readonly List<GameObject> AddedObjects = new List<GameObject>();
        private static readonly Dictionary<Renderer, bool> VisibilityBeforeHide =
            new Dictionary<Renderer, bool>();
        private static readonly List<GameObject> RuntimeAddedObjects = new List<GameObject>();
        private static readonly Dictionary<Transform, TransformState> RuntimeOriginalStates =
            new Dictionary<Transform, TransformState>();
        private static readonly FieldInfo SpawnPointField =
            AccessTools.Field(typeof(ArrivalBaseComponent), "_spawnPoint");
        private static readonly FieldInfo TimePortalLandingsField =
            AccessTools.Field(typeof(ArrivalTimePortalComponent), "_landingPoints");
        private static readonly MethodInfo MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly Dictionary<RoomItem, NativeProxy> NativeByRoomItem =
            new Dictionary<RoomItem, NativeProxy>();
        private static readonly Dictionary<Item, NativeProxy> NativeBySource =
            new Dictionary<Item, NativeProxy>();

        private static Data _loaded;
        private static Level _level;
        private static Item _selected;
        private static HospitalPlot _nativeHostPlot;
        private static CursorEditHospitalItem _nativeEditorCursor;
        private static bool _destroyingNativeProxies;
        private static bool _rebuildNativeProxies;
        private static Material _markerMaterial;
        private static Category _category = Category.Arrivals;

        internal static bool IsActive { get; private set; }
        internal static bool ObjectsVisible { get; private set; } = true;
        internal static string SelectedId => _selected?.Id ?? "";
        internal static Category CurrentCategory => _category;
        internal static bool IsMoving => _level?.CursorManager != null &&
            _level.CursorManager.IsModeActive<CursorRoomItem>();

        internal static void Bind(Level level)
        {
            if (level == null || ReferenceEquals(_level, level)) return;
            UnbindNativeEvents();
            _level = level;
            BindNativeEvents();
            Rebuild();
            ApplySaved();
        }

        internal static void Activate(Level level)
        {
            if (level == null || !ScenarioEditorSession.Active ||
                !ReferenceEquals(level, ScenarioEditorSession.Level)) return;
            ScenarioLandscape.Deselect();
            IsActive = true;
            _level = level;
            Rebuild();
            ApplySaved();
            RefreshMarkers();
            BuildNativeProxies();
            BeginNativeEditor();
            ApplyVisibility();
        }

        internal static void Deselect()
        {
            EndNativeEditor();
            DestroyNativeProxies();
            IsActive = false;
            _selected = null;
            DestroyMarkers();
        }

        internal static void Release()
        {
            RestoreVisibility();
            EndNativeEditor();
            DestroyNativeProxies();
            RestoreEditorScene();
            DestroyMarkers();
            Items.Clear();
            Edited.Clear();
            Additions.Clear();
            _loaded = null;
            UnbindNativeEvents();
            _level = null;
            _selected = null;
            IsActive = false;
            ObjectsVisible = true;
            if (_markerMaterial != null) UnityEngine.Object.Destroy(_markerMaterial);
            _markerMaterial = null;
        }

        internal static void SetMapFile(string path)
        {
            RestoreVisibility();
            EndNativeEditor();
            DestroyNativeProxies();
            RestoreEditorScene();
            DestroyMarkers();
            Items.Clear();
            Edited.Clear();
            Additions.Clear();
            _loaded = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _loaded = Read(path);
                foreach (var entry in _loaded.objects)
                    Edited[entry.id] = entry;
                Additions.AddRange(_loaded.added);
            }
            if (_level != null)
            {
                Rebuild();
                ApplySaved();
                if (IsActive) RefreshMarkers();
                if (IsActive)
                {
                    BuildNativeProxies();
                    BeginNativeEditor();
                }
                ApplyVisibility();
            }
        }

        internal static void RefreshAfterMapLoad()
        {
            if (_level == null) return;
            Rebuild();
            ApplySaved();
            if (IsActive) RefreshMarkers();
            if (IsActive)
            {
                BuildNativeProxies();
                BeginNativeEditor();
            }
            ApplyVisibility();
        }

        internal static void SetCategory(Category category)
        {
            _category = category;
            if (_selected != null && CategoryFor(_selected.Components) != category)
                _selected = null;
            RefreshMarkerVisibility();
            RefreshNativeProxyVisibility();
        }

        internal static List<ItemInfo> GetItems()
        {
            var visible = Items.Where(item => CategoryFor(item.Components) == _category)
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return visible.Select((item, index) => new ItemInfo
                {
                    Id = item.Id,
                    Name = LabelFor(item.Components, item.Added) + " " + (index + 1),
                    Category = CategoryFor(item.Components),
                    Added = item.Added,
                    Position = item.Transform == null ? Vector3.zero : item.Transform.position
                }).ToList();
        }

        internal static bool Select(string id)
        {
            var item = Items.FirstOrDefault(value => value.Id == id);
            if (item == null) return false;
            _selected = item;
            _category = CategoryFor(item.Components);
            return true;
        }

        internal static bool SelectAndFocus(string id)
        {
            if (!Select(id)) return false;
            RefreshMarkerVisibility();
            RefreshNativeProxyVisibility();
            NativeProxy proxy;
            var point = NativeBySource.TryGetValue(_selected, out proxy) &&
                proxy?.RoomItem != null ? proxy.RoomItem.WorldPosition : _selected.Transform.position;
            _level?.CameraLogic?.SetFocalPoint(point, false);
            return true;
        }

        internal static bool BeginMoveSelected()
        {
            return BeginNativeMove(_selected);
        }

        internal static bool AddPoint(string kind)
        {
            if (!IsActive || _level == null || !CanAdd(kind)) return false;
            Vector3 position;
            if (!GroundPoint(Input.mousePosition, out position))
                position = Camera.main == null ? Vector3.zero :
                    Camera.main.transform.position + Camera.main.transform.forward * 20f;
            position.y = Mathf.Max(0f, position.y);
            var id = "added_" + Guid.NewGuid().ToString("N");
            var components = ComponentsForAddition(kind);
            var addition = new Addition
            {
                id = id,
                name = LabelFor(components, true),
                components = components,
                x = position.x,
                y = position.y,
                z = position.z,
                rotation = 0f
            };
            var created = CreatePointObject(addition, false);
            if (created == null) return false;
            Additions.Add(addition);
            AddedObjects.Add(created);
            var item = MakeAddedItem(addition, created.transform);
            Items.Add(item);
            _selected = item;
            _category = CategoryFor(components);
            item.Marker = CreateMarker(item);
            item.Renderers = item.Marker.GetComponentsInChildren<Renderer>(true);
            RefreshMarkerVisibility();
            BuildNativeProxy(item);
            RefreshNativeProxyVisibility();
            ApplyVisibility();
            return BeginMoveSelected();
        }

        internal static bool CanAdd(string kind) =>
            kind == RoadArrival || kind == HelipadArrival ||
            kind == SubwayArrival || kind == SubwayDeparture ||
            kind == TunnelDeparture;

        private static string[] ComponentsForAddition(string kind)
        {
            return new[] { kind };
        }

        internal static void SetObjectsVisible(bool visible)
        {
            if (ObjectsVisible == visible) return;
            RestoreVisibility();
            ObjectsVisible = visible;
            ApplyVisibility();
            RefreshNativeProxyVisibility();
        }

        private static void ApplyVisibility()
        {
            RestoreVisibility();
            if (ObjectsVisible) return;
            foreach (var item in Items)
            {
                if (item.Marker != null) item.Marker.SetActive(false);
                if (!item.Components.Contains(Ambient) &&
                    !item.Components.Contains(StaticVehicle)) continue;
                foreach (var renderer in item.Renderers ?? new Renderer[0])
                {
                    if (renderer == null || VisibilityBeforeHide.ContainsKey(renderer)) continue;
                    VisibilityBeforeHide.Add(renderer, renderer.enabled);
                    renderer.enabled = false;
                }
            }
        }

        private static void RestoreVisibility()
        {
            foreach (var pair in VisibilityBeforeHide)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            VisibilityBeforeHide.Clear();
            foreach (var item in Items)
                if (item.Marker != null) item.Marker.SetActive(ObjectsVisible && IsActive &&
                    CategoryFor(item.Components) == _category);
        }

        private static void Rebuild()
        {
            DestroyNativeProxies();
            var selectedId = _selected?.Id;
            DestroyMarkers();
            foreach (var added in AddedObjects)
                if (added != null) UnityEngine.Object.Destroy(added);
            AddedObjects.Clear();
            Items.Clear();
            foreach (var captured in CaptureNativeItems())
            {
                if (!OriginalStates.ContainsKey(captured.Transform))
                    OriginalStates.Add(captured.Transform, new TransformState
                    {
                        Position = captured.Transform.position,
                        Rotation = captured.Transform.rotation
                    });
                var state = OriginalStates[captured.Transform];
                captured.OriginalPosition = state.Position;
                captured.OriginalRotation = state.Rotation;
                Items.Add(captured);
            }
            foreach (var addition in Additions)
            {
                var created = CreatePointObject(addition, false);
                if (created == null) continue;
                AddedObjects.Add(created);
                Items.Add(MakeAddedItem(addition, created.transform));
            }
            _selected = Items.FirstOrDefault(item => item.Id == selectedId);
        }

        private static void ApplySaved()
        {
            foreach (var item in Items.Where(value => !value.Added))
            {
                ObjectEntry entry;
                if (!Edited.TryGetValue(item.Id, out entry) || item.Transform == null) continue;
                item.Transform.position = new Vector3(entry.x, entry.y, entry.z);
                item.Transform.rotation = Quaternion.Euler(0f, entry.rotation, 0f);
            }
        }

        private static void RestoreEditorScene()
        {
            foreach (var pair in OriginalStates)
                if (pair.Key != null)
                {
                    pair.Key.position = pair.Value.Position;
                    pair.Key.rotation = pair.Value.Rotation;
                }
            OriginalStates.Clear();
            foreach (var added in AddedObjects)
                if (added != null) UnityEngine.Object.Destroy(added);
            AddedObjects.Clear();
        }

        private static List<Item> CaptureNativeItems()
        {
            var result = new Dictionary<Transform, HashSet<string>>();
            var names = new Dictionary<Transform, string>();
            var traffic = UnityEngine.Object.FindObjectsOfType<Transform>()
                .FirstOrDefault(value => value != null && value.parent == null &&
                    value.name == "Traffic");
            if (traffic != null)
                for (var index = 0; index < traffic.childCount; ++index)
                {
                    var child = traffic.GetChild(index);
                    var looping = child.GetComponentsInChildren<Animator>(true)
                        .Any(animator => animator != null && animator.runtimeAnimatorController != null &&
                            animator.runtimeAnimatorController.name.IndexOf("DriveLoop",
                                StringComparison.OrdinalIgnoreCase) >= 0);
                    if (looping) AddCaptured(result, names, child, Ambient, child.name);
                }

            foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>())
            {
                if (animator == null || animator.GetComponent<VehicleAnimationEventListener>() != null)
                    continue;
                var vehicle = VehicleRoot(animator.transform);
                if (vehicle == null || IsInsideArrivalPoint(vehicle) ||
                    result.ContainsKey(vehicle)) continue;
                AddCaptured(result, names, vehicle, StaticVehicle, vehicle.name);
            }

            foreach (var component in UnityEngine.Object.FindObjectsOfType<ArrivalCarComponent>())
                AddCaptured(result, names, component.GetTransform(), RoadArrival, component.name);
            foreach (var component in UnityEngine.Object.FindObjectsOfType<ArrivalHelipadComponent>())
                AddCaptured(result, names, component.GetTransform(), HelipadArrival, component.name);
            foreach (var component in UnityEngine.Object.FindObjectsOfType<ArrivalSubwayComponent>())
                AddCaptured(result, names, component.GetTransform(), SubwayArrival, component.name);
            foreach (var component in UnityEngine.Object.FindObjectsOfType<ArrivalItemComponent>())
                AddCaptured(result, names, component.GetTransform(), ItemArrival, component.name);
            foreach (var component in UnityEngine.Object.FindObjectsOfType<ArrivalTimePortalComponent>())
            {
                AddCaptured(result, names, component.transform, TimePortalArrival, component.name);
                var landings = TimePortalLandingsField?.GetValue(component) as List<Transform>;
                if (landings != null)
                    for (var index = 0; index < landings.Count; ++index)
                        AddCaptured(result, names, landings[index], TimePortalLanding,
                            component.name + " / " + (index + 1));
            }
            foreach (var component in UnityEngine.Object.FindObjectsOfType<DepartureSubwayComponent>())
                AddCaptured(result, names, component.GetTransform(), SubwayDeparture, component.name);
            foreach (var component in UnityEngine.Object.FindObjectsOfType<DepartureTunnelComponent>())
                AddCaptured(result, names, component.GetTransform(), TunnelDeparture, component.name);

            var items = new List<Item>();
            foreach (var pair in result)
            {
                var hierarchy = Hierarchy(pair.Key);
                var id = StableId(hierarchy, pair.Key.position);
                items.Add(new Item
                {
                    Id = id,
                    Name = LabelFor(pair.Value.ToArray(), false) + " — " + names[pair.Key],
                    Transform = pair.Key,
                    Components = pair.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    Added = false,
                    OriginalPosition = pair.Key.position,
                    OriginalRotation = pair.Key.rotation,
                    Renderers = pair.Key.GetComponentsInChildren<Renderer>(true)
                });
            }
            return items;
        }

        private static void AddCaptured(Dictionary<Transform, HashSet<string>> result,
            Dictionary<Transform, string> names, Transform point, string kind, string name)
        {
            if (point == null || point.name.StartsWith("UP Traffic Added - ",
                    StringComparison.Ordinal)) return;
            HashSet<string> kinds;
            if (!result.TryGetValue(point, out kinds))
            {
                kinds = new HashSet<string>(StringComparer.Ordinal);
                result.Add(point, kinds);
                names.Add(point, string.IsNullOrEmpty(name) ? point.name : name);
            }
            kinds.Add(kind);
        }

        private static Transform VehicleRoot(Transform node)
        {
            Transform found = null;
            for (var current = node; current != null; current = current.parent)
            {
                if (current.name.StartsWith("A_CAR_", StringComparison.OrdinalIgnoreCase) ||
                    current.name.StartsWith("A_Ambulance", StringComparison.OrdinalIgnoreCase))
                    found = current;
                if (current.parent == null || current.name == "Traffic") break;
            }
            return found;
        }

        private static bool IsInsideArrivalPoint(Transform node)
        {
            for (var current = node; current != null; current = current.parent)
                if (current.GetComponent<ArrivalBaseComponent>() != null) return true;
            return false;
        }

        private static Item MakeAddedItem(Addition addition, Transform transform) => new Item
        {
            Id = addition.id,
            Name = addition.name,
            Transform = transform,
            Components = addition.components,
            Added = true,
            OriginalPosition = transform.position,
            OriginalRotation = transform.rotation,
            Renderers = new Renderer[0]
        };

        private static Category CategoryFor(IEnumerable<string> components)
        {
            if (components.Contains(Ambient) || components.Contains(StaticVehicle))
                return Category.Ambient;
            if (components.Any(value => value.EndsWith("_arrival", StringComparison.Ordinal)))
                return Category.Arrivals;
            return Category.Departures;
        }

        internal static string LabelFor(string[] components, bool added)
        {
            var label = components.Contains(Ambient) ? "editor.traffic.ambient_item" :
                components.Contains(StaticVehicle) ? "editor.traffic.static_vehicle" :
                components.Contains(RoadArrival) ? "editor.traffic.road_arrival" :
                components.Contains(HelipadArrival) ? "editor.traffic.helipad_arrival" :
                components.Contains(SubwayArrival) ? "editor.traffic.subway" :
                components.Contains(ItemArrival) ? "editor.traffic.item_arrival" :
                components.Contains(TimePortalArrival) ? "editor.traffic.time_portal" :
                components.Contains(TimePortalLanding) ? "editor.traffic.time_portal_landing" :
                components.Contains(SubwayDeparture) ? "editor.traffic.subway_departure" :
                "editor.traffic.tunnel_departure";
            return EditorLocalization.Get(label) + (added ? " *" : "");
        }

        private static void RefreshMarkers()
        {
            DestroyMarkers();
            if (!IsActive || !ObjectsVisible) return;
            foreach (var item in Items)
            {
                if ((item.Components.Contains(Ambient) ||
                     item.Components.Contains(StaticVehicle)) || item.Transform == null) continue;
                item.Marker = CreateMarker(item);
                item.Marker.SetActive(CategoryFor(item.Components) == _category);
                item.Renderers = item.Marker.GetComponentsInChildren<Renderer>(true);
            }
        }

        private static void RefreshMarkerVisibility()
        {
            foreach (var item in Items)
                if (item.Marker != null)
                    item.Marker.SetActive(IsActive && ObjectsVisible &&
                        CategoryFor(item.Components) == _category);
        }

        private static GameObject CreateMarker(Item item)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "UP Traffic Marker - " + item.Id;
            marker.transform.SetParent(item.Transform, false);
            marker.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = new Vector3(.7f, 1.5f, .7f);
            var collider = marker.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (_markerMaterial == null)
                {
                    var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                    if (shader != null)
                    {
                        _markerMaterial = new Material(shader)
                        {
                            name = "UP Traffic Point Marker",
                            hideFlags = HideFlags.DontSave
                        };
                        _markerMaterial.color = new Color(.08f, .85f, 1f, .82f);
                    }
                }
                if (_markerMaterial != null) renderer.sharedMaterial = _markerMaterial;
            }
            return marker;
        }

        private static void DestroyMarkers()
        {
            foreach (var item in Items)
            {
                if (item.Marker != null) UnityEngine.Object.Destroy(item.Marker);
                item.Marker = null;
                if (item.Transform != null)
                    item.Renderers = item.Transform.GetComponentsInChildren<Renderer>(true)
                        .Where(renderer => renderer != null &&
                            !renderer.name.StartsWith("UP Traffic Marker - ", StringComparison.Ordinal))
                        .ToArray();
            }
        }

        internal static void UpdateSelection()
        {
            if (!IsActive || _level == null || !ScenarioEditorSession.Active ||
                !ReferenceEquals(_level, ScenarioEditorSession.Level)) return;
            if (_rebuildNativeProxies && _level.CursorManager != null &&
                !_level.CursorManager.IsModeActive<CursorRoomItem>())
            {
                _rebuildNativeProxies = false;
                DestroyNativeProxies();
                BuildNativeProxies();
                RefreshNativeProxyVisibility();
            }
        }

        // Este es el mismo filtro que CursorEditHospitalItem.GetSelectedItem:
        // RoomItemVisual.RayCast y la menor distancia. No se usan bounds,
        // colliders añadidos ni un selector paralelo.
        internal static RoomItem GetNativeSelectedItem(InputManager inputManager)
        {
            if (!IsActive || inputManager == null || Camera.main == null) return null;
            var ray = Camera.main.ScreenPointToRay(inputManager.GetMousePos());
            var nearest = float.MaxValue;
            RoomItem selected = null;
            foreach (var pair in NativeByRoomItem)
            {
                var proxy = pair.Value;
                var visual = pair.Key?.Visual;
                if (proxy?.Source == null ||
                    CategoryFor(proxy.Source.Components) != _category ||
                    visual?.GameObject == null || !visual.GameObject.activeInHierarchy)
                    continue;
                float distance;
                if (!visual.RayCast(ray, out distance) || distance >= nearest) continue;
                nearest = distance;
                selected = pair.Key;
            }
            return selected;
        }

        private static void BindNativeEvents()
        {
            if (_level?.BuildEvents == null) return;
            _level.BuildEvents.OnRoomItemPlaced += OnNativeProxyPlaced;
            _level.BuildEvents.OnRoomItemCancel += OnNativeProxyCancelled;
        }

        private static void UnbindNativeEvents()
        {
            if (_level?.BuildEvents == null) return;
            _level.BuildEvents.OnRoomItemPlaced -= OnNativeProxyPlaced;
            _level.BuildEvents.OnRoomItemCancel -= OnNativeProxyCancelled;
        }

        private static void BeginNativeEditor()
        {
            if (!IsActive || _level?.CursorManager == null || _nativeHostPlot == null ||
                _nativeEditorCursor != null) return;
            _level.CursorManager.PopMode<CursorEditHospitalItem>();
            _nativeEditorCursor = new CursorEditHospitalItem(_level.CursorManager, _level,
                _nativeHostPlot, HospitalPlotLayer.Base);
            _level.CursorManager.PushMode(_nativeEditorCursor);
        }

        private static void EndNativeEditor()
        {
            if (_level?.CursorManager == null)
            {
                _nativeEditorCursor = null;
                return;
            }
            _destroyingNativeProxies = true;
            try
            {
                _level.CursorManager.PopMode<CursorRoomItem>();
                if (_nativeEditorCursor != null)
                    _level.CursorManager.PopMode<CursorEditHospitalItem>();
            }
            finally
            {
                _nativeEditorCursor = null;
                _destroyingNativeProxies = false;
            }
        }

        private static void BuildNativeProxies()
        {
            if (!IsActive || _level?.WorldState?.HospitalPlots == null ||
                NativeByRoomItem.Count != 0) return;
            _nativeHostPlot = _level.WorldState.HospitalPlots.FirstOrDefault(plot =>
                plot?.HospitalMap?.FloorPlan != null && plot.HospitalMap.RoomVisual != null);
            if (_nativeHostPlot == null) return;
            foreach (var item in Items)
                BuildNativeProxy(item, false);
            _nativeHostPlot.HospitalMap.RoomVisual.CreateRoomItems(Vector3.zero, 0f, null);
            foreach (var proxy in NativeByRoomItem.Values)
                HideSourceRenderers(proxy);
            RefreshNativeProxyVisibility();
        }

        private static void BuildNativeProxy(Item item) => BuildNativeProxy(item, true);

        private static void BuildNativeProxy(Item item, bool refreshVisuals)
        {
            if (!IsActive || item?.Transform == null || NativeBySource.ContainsKey(item)) return;
            if (_nativeHostPlot == null)
                _nativeHostPlot = _level?.WorldState?.HospitalPlots?.FirstOrDefault(plot =>
                    plot?.HospitalMap?.FloorPlan != null && plot.HospitalMap.RoomVisual != null);
            var floor = _nativeHostPlot?.HospitalMap?.FloorPlan;
            var sourceVisual = item.Components.Contains(Ambient) ||
                item.Components.Contains(StaticVehicle) ? item.Transform.gameObject : item.Marker;
            if (floor == null || sourceVisual == null) return;

            Vector3 sourceOffsetLocal;
            List<Mesh> bakedMeshes;
            var model = item.Components.Contains(Ambient) ||
                item.Components.Contains(StaticVehicle);
            var prefab = CreateProxyPrefab(sourceVisual, item.Id, model,
                out sourceOffsetLocal, out bakedMeshes);
            var definition = CreateProxyDefinition(prefab, item.Id);
            if (prefab == null || definition == null)
            {
                if (prefab != null) UnityEngine.Object.Destroy(prefab);
                return;
            }

            var roomItem = new LandscapeRoomItem(definition, floor, _level,
                HospitalPlotLayer.Base)
            {
                WorldPosition = item.Transform.position +
                    item.Transform.rotation * sourceOffsetLocal,
                Rotation = item.Transform.eulerAngles.y,
                HasBeenPurchased = true,
                IsInBoughtPlot = true
            };
            var renderers = item.Transform.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null &&
                    !renderer.name.StartsWith("UP Traffic Marker - ", StringComparison.Ordinal))
                .ToArray();
            if (!(item.Components.Contains(Ambient) || item.Components.Contains(StaticVehicle)))
                renderers = item.Marker.GetComponentsInChildren<Renderer>(true);
            var proxy = new NativeProxy
            {
                Source = item,
                RoomItem = roomItem,
                Prefab = prefab,
                SourceOffsetLocal = sourceOffsetLocal,
                BakedMeshes = bakedMeshes,
                SourceRenderers = renderers,
                RendererStates = renderers.Select(renderer => renderer.enabled).ToArray()
            };
            NativeByRoomItem.Add(roomItem, proxy);
            NativeBySource.Add(item, proxy);
            floor.AddItem(roomItem);
            if (!refreshVisuals) return;
            _nativeHostPlot.HospitalMap.RoomVisual.CreateRoomItems(Vector3.zero, 0f, null);
            HideSourceRenderers(proxy);
        }

        private static GameObject CreateProxyPrefab(GameObject source, string id,
            bool freezeModel, out Vector3 sourceOffsetLocal, out List<Mesh> bakedMeshes)
        {
            sourceOffsetLocal = Vector3.zero;
            bakedMeshes = new List<Mesh>();
            if (source == null) return null;
            var root = new GameObject("UP Traffic Native Prefab - " + id)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            root.SetActive(false);
            var visual = UnityEngine.Object.Instantiate(source, root.transform);
            visual.name = source.name;
            visual.transform.localPosition = source.transform.localPosition;
            visual.transform.localRotation = source.transform.localRotation;
            visual.transform.localScale = source.transform.localScale;
            if (source.transform.parent == null ||
                !source.name.StartsWith("UP Traffic Marker - ", StringComparison.Ordinal))
            {
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }
            if (freezeModel)
            {
                FreezeSkinnedMeshes(visual, bakedMeshes);
                Bounds bounds;
                if (TryCalculateMeshBounds(root.transform, out bounds))
                {
                    sourceOffsetLocal = bounds.center;
                    visual.transform.localPosition -= sourceOffsetLocal;
                }
            }
            foreach (var behaviour in visual.GetComponentsInChildren<Behaviour>(true))
                if (behaviour != null) behaviour.enabled = false;
            return root;
        }

        private static void FreezeSkinnedMeshes(GameObject visual, List<Mesh> bakedMeshes)
        {
            foreach (var skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned == null) continue;
                var mesh = new Mesh
                {
                    name = "UP Traffic Frozen " + skinned.name,
                    hideFlags = HideFlags.HideAndDontSave
                };
                skinned.BakeMesh(mesh);
                bakedMeshes.Add(mesh);
                var filter = skinned.gameObject.GetComponent<MeshFilter>() ??
                    skinned.gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = skinned.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = skinned.sharedMaterials;
                renderer.shadowCastingMode = skinned.shadowCastingMode;
                renderer.receiveShadows = skinned.receiveShadows;
                renderer.lightProbeUsage = skinned.lightProbeUsage;
                renderer.reflectionProbeUsage = skinned.reflectionProbeUsage;
                renderer.probeAnchor = skinned.probeAnchor;
                UnityEngine.Object.DestroyImmediate(skinned);
            }
        }

        private static bool TryCalculateMeshBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            var found = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter?.sharedMesh;
                if (mesh == null) continue;
                var local = mesh.bounds;
                var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var min = local.min;
                var max = local.max;
                for (var x = 0; x < 2; ++x)
                    for (var y = 0; y < 2; ++y)
                        for (var z = 0; z < 2; ++z)
                        {
                            var point = matrix.MultiplyPoint3x4(new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z));
                            if (!found)
                            {
                                bounds = new Bounds(point, Vector3.zero);
                                found = true;
                            }
                            else bounds.Encapsulate(point);
                        }
            }
            return found;
        }

        private static RoomItemDefinition CreateProxyDefinition(GameObject prefab, string id)
        {
            var source = _level?.App?.Metagame?.LandscapeItemDatabase?.Instance?.RoomItems?
                .Select(reference => reference?.Instance as RoomItemDefinition)
                .FirstOrDefault(definition => definition != null &&
                    definition.ItemType == RoomItemDefinition.Type.Landscape);
            if (source == null || MemberwiseCloneMethod == null) return null;
            var definition = MemberwiseCloneMethod.Invoke(source, null) as RoomItemDefinition;
            if (definition == null) return null;
            SetDefinitionField(definition, "_guid", Guid.NewGuid());
            SetDefinitionField(definition, "_debugTag", "UP_Traffic_" + id);
            SetDefinitionField(definition, "_type", RoomItemDefinition.Type.Landscape);
            SetDefinitionField(definition, "_prefab", prefab);
            SetDefinitionField(definition, "_blueprintPrefab", null);
            SetDefinitionField(definition, "_gridSnap", 1f);
            SetDefinitionField(definition, "_rotationSnap", 90f);
            SetDefinitionField(definition, "_hasCollision", false);
            SetDefinitionField(definition, "_ignoreValidation", true);
            SetDefinitionField(definition, "_isSelectable", true);
            SetDefinitionField(definition, "_affectsNavigation", false);
            SetDefinitionField(definition, "_singlePlace", true);
            SetDefinitionField(definition, "_placementEffect", null);
            SetDefinitionField(definition, "_wallMagnetism", false);
            SetDefinitionField(definition, "_placeOnWall", false);
            SetDefinitionField(definition, "_cost", 0);
            SetDefinitionField(definition, "_canBeSold", false);
            SetDefinitionField(definition, "_components", new EntityComponent[0]);
            SetDefinitionField(definition, "_interactions", new InteractionDefinition[0]);
            SetDefinitionField(definition, "_attributes", new ObjectAttributes.Definition[0]);
            SetDefinitionField(definition, "_upgrades",
                new SharedInstance<RoomItemUpgradeDefinition>[0]);
            SetDefinitionField(definition, "_canBePlacedIn", new RoomDefinition.Type[0]);
            SetDefinitionField(definition, "_cantBePlacedIn", new RoomDefinition.Type[0]);
            return definition;
        }

        private static void SetDefinitionField(RoomItemDefinition definition, string name,
            object value)
        {
            var field = AccessTools.Field(typeof(RoomItemDefinition), name);
            if (field == null)
                throw new MissingFieldException(typeof(RoomItemDefinition).FullName, name);
            field.SetValue(definition, value);
        }

        private static void HideSourceRenderers(NativeProxy proxy)
        {
            if (proxy?.SourceRenderers == null) return;
            foreach (var renderer in proxy.SourceRenderers)
                if (renderer != null) renderer.enabled = false;
        }

        private static void RefreshNativeProxyVisibility()
        {
            foreach (var pair in NativeByRoomItem)
            {
                var proxy = pair.Value;
                var visible = IsActive && ObjectsVisible && proxy?.Source != null &&
                    CategoryFor(proxy.Source.Components) == _category;
                if (pair.Key?.Visual?.GameObject != null)
                    pair.Key.Visual.GameObject.SetActive(visible);
            }
        }

        private static void DestroyNativeProxies()
        {
            if (NativeByRoomItem.Count == 0)
            {
                _nativeHostPlot = null;
                return;
            }
            _destroyingNativeProxies = true;
            var floor = _nativeHostPlot?.HospitalMap?.FloorPlan;
            try
            {
                foreach (var proxy in NativeByRoomItem.Values.ToArray())
                {
                    if (proxy == null) continue;
                    if (proxy.SourceRenderers != null)
                        for (var index = 0; index < proxy.SourceRenderers.Length; ++index)
                            if (proxy.SourceRenderers[index] != null &&
                                index < proxy.RendererStates.Length)
                                proxy.SourceRenderers[index].enabled =
                                    proxy.RendererStates[index];
                    if (proxy.RoomItem != null)
                    {
                        if (floor != null && (floor.Items.Contains(proxy.RoomItem) ||
                            floor.LandscapeItems.Contains(proxy.RoomItem)))
                            floor.RemoveItem(proxy.RoomItem);
                        proxy.RoomItem.Visual = null;
                        try { proxy.RoomItem.Destroy(); }
                        catch (Exception) { }
                    }
                    if (proxy.Prefab != null) UnityEngine.Object.Destroy(proxy.Prefab);
                    if (proxy.BakedMeshes != null)
                        foreach (var mesh in proxy.BakedMeshes)
                            if (mesh != null) UnityEngine.Object.Destroy(mesh);
                }
                if (floor != null && _nativeHostPlot.HospitalMap.RoomVisual != null)
                    _nativeHostPlot.HospitalMap.RoomVisual.CreateRoomItems(Vector3.zero, 0f, null);
            }
            finally
            {
                NativeByRoomItem.Clear();
                NativeBySource.Clear();
                _nativeHostPlot = null;
                _destroyingNativeProxies = false;
            }
        }

        private static void OnNativeProxyPlaced(RoomItem roomItem, FloorPlan floorPlan)
        {
            NativeProxy proxy;
            if (!NativeByRoomItem.TryGetValue(roomItem, out proxy) || proxy?.Source?.Transform == null)
                return;
            var rotation = Quaternion.Euler(0f, roomItem.Rotation, 0f);
            proxy.Source.Transform.rotation = rotation;
            proxy.Source.Transform.position = roomItem.WorldPosition -
                rotation * proxy.SourceOffsetLocal;
            _selected = proxy.Source;
            UpdateEdited(proxy.Source);
            ScenarioEditorHistory.ObjectDropped();
            RefreshNativeProxyVisibility();
        }

        private static void OnNativeProxyCancelled(RoomItem roomItem, bool requestedByUser)
        {
            if (_destroyingNativeProxies || !NativeByRoomItem.ContainsKey(roomItem)) return;
            _rebuildNativeProxies = true;
        }

        internal static bool IsNativeProxy(RoomItem item) =>
            item != null && NativeByRoomItem.ContainsKey(item);

        internal static bool BeginNativeMove(string id)
        {
            var item = Items.FirstOrDefault(value => value.Id == id);
            if (item == null) return false;
            _selected = item;
            _category = CategoryFor(item.Components);
            RefreshMarkerVisibility();
            RefreshNativeProxyVisibility();
            return BeginNativeMove(item);
        }

        private static bool BeginNativeMove(Item item)
        {
            if (!IsActive || item == null || _level?.BuildEvents == null) return false;
            NativeProxy proxy;
            if (!NativeBySource.TryGetValue(item, out proxy) || proxy?.RoomItem == null ||
                proxy.RoomItem.OwningRoom == null) return false;
            _level.BuildEvents.StartItemEdit(proxy.RoomItem, proxy.RoomItem.OwningRoom);
            return true;
        }

        private static void UpdateEdited(Item item)
        {
            if (item?.Transform == null) return;
            if (item.Added)
            {
                var addition = Additions.FirstOrDefault(value => value.id == item.Id);
                if (addition == null) return;
                addition.x = item.Transform.position.x;
                addition.y = item.Transform.position.y;
                addition.z = item.Transform.position.z;
                addition.rotation = item.Transform.eulerAngles.y;
                return;
            }
            ObjectEntry entry;
            if (!Edited.TryGetValue(item.Id, out entry))
                entry = EntryFor(item);
            entry.x = item.Transform.position.x;
            entry.y = item.Transform.position.y;
            entry.z = item.Transform.position.z;
            entry.rotation = item.Transform.eulerAngles.y;
            Edited[item.Id] = entry;
        }

        private static bool PlanePoint(Vector2 pointer, float y, out Vector3 point)
        {
            point = Vector3.zero;
            if (Camera.main == null) return false;
            var ray = Camera.main.ScreenPointToRay(pointer);
            if (!new Plane(Vector3.up, new Vector3(0f, y, 0f)).Raycast(ray, out var distance))
                return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private static bool GroundPoint(Vector2 pointer, out Vector3 point)
        {
            point = Vector3.zero;
            var camera = Camera.main;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out var hit, camera.farClipPlane))
            {
                point = hit.point;
                return true;
            }
            return PlanePoint(pointer, 0f, out point);
        }

        internal static Data Capture(Data previous = null)
        {
            if (_level == null) _level = ScenarioEditorSession.Level;
            if (_level != null && Items.Count == 0)
            {
                Rebuild();
                ApplySaved();
            }
            var data = new Data();
            data.objects = Items.Where(item => !item.Added)
                .Select(item =>
                {
                    ObjectEntry edited;
                    return Edited.TryGetValue(item.Id, out edited) ? edited : EntryFor(item);
                })
                .OrderBy(entry => entry.id, StringComparer.Ordinal).ToArray();
            data.added = Additions.Select(CopyAddition).ToArray();
            return data;
        }

        internal static Data CaptureHistory() => CopyData(Capture());

        internal static void ApplyHistory(Data data)
        {
            if (data == null) return;
            EndNativeEditor();
            DestroyNativeProxies();
            Edited.Clear();
            Additions.Clear();
            foreach (var entry in data.objects ?? new ObjectEntry[0])
                if (entry != null) Edited[entry.id] = CopyObject(entry);
            foreach (var addition in data.added ?? new Addition[0])
                if (addition != null) Additions.Add(CopyAddition(addition));
            Rebuild();
            ApplySaved();
            if (IsActive) RefreshMarkers();
            if (IsActive)
            {
                BuildNativeProxies();
                BeginNativeEditor();
            }
            ApplyVisibility();
        }

        private static Data CopyData(Data value) => new Data
        {
            version = value?.version ?? Version,
            objects = (value?.objects ?? new ObjectEntry[0]).Select(CopyObject).ToArray(),
            added = (value?.added ?? new Addition[0]).Select(CopyAddition).ToArray()
        };

        private static ObjectEntry CopyObject(ObjectEntry value) => new ObjectEntry
        {
            id = value.id,
            name = value.name,
            hierarchy = value.hierarchy,
            components = value.components?.ToArray() ?? new string[0],
            originalX = value.originalX,
            originalY = value.originalY,
            originalZ = value.originalZ,
            originalRotation = value.originalRotation,
            x = value.x,
            y = value.y,
            z = value.z,
            rotation = value.rotation
        };

        private static ObjectEntry EntryFor(Item item)
        {
            var transform = item.Transform;
            return new ObjectEntry
            {
                id = item.Id,
                name = item.Name,
                hierarchy = Hierarchy(transform),
                components = item.Components,
                originalX = item.OriginalPosition.x,
                originalY = item.OriginalPosition.y,
                originalZ = item.OriginalPosition.z,
                originalRotation = item.OriginalRotation.eulerAngles.y,
                x = transform.position.x,
                y = transform.position.y,
                z = transform.position.z,
                rotation = transform.eulerAngles.y
            };
        }

        private static Addition CopyAddition(Addition value) => new Addition
        {
            id = value.id,
            name = value.name,
            components = value.components?.ToArray() ?? new string[0],
            x = value.x,
            y = value.y,
            z = value.z,
            rotation = value.rotation
        };

        internal static Data Read(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxFileBytes)
                throw new InvalidDataException("traffic.json ausente, vacio o demasiado grande.");
            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonUtility.FromJson<Data>(json);
            if (data == null || data.version != Version)
                throw new InvalidDataException("Version de traffic.json no compatible.");
            data.objects = json.Contains("\"objects\"")
                ? ScenarioMapFormat.ParseObjects<ObjectEntry>(json, "objects")
                : new ObjectEntry[0];
            data.added = json.Contains("\"added\"")
                ? ScenarioMapFormat.ParseObjects<Addition>(json, "added")
                : new Addition[0];
            if (data.objects.Length > MaxObjects || data.added.Length > MaxObjects)
                throw new InvalidDataException("Demasiados objetos de trafico.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in data.objects)
                ValidateEntry(entry?.id, entry?.components, ids);
            foreach (var entry in data.added)
                ValidateEntry(entry?.id, entry?.components, ids);
            return data;
        }

        private static void ValidateEntry(string id, string[] components, HashSet<string> ids)
        {
            if (string.IsNullOrEmpty(id) || components == null || components.Length == 0 ||
                !ids.Add(id) || components.Any(component => !KnownComponent(component)))
                throw new InvalidDataException("traffic.json contiene una identidad invalida.");
        }

        private static bool KnownComponent(string component) =>
            component == Ambient || component == StaticVehicle || component == RoadArrival ||
            component == HelipadArrival || component == SubwayArrival ||
            component == ItemArrival || component == TimePortalArrival ||
            component == TimePortalLanding ||
            component == SubwayDeparture || component == TunnelDeparture;

        private static GameObject CreatePointObject(Addition addition, bool runtime)
        {
            if (addition?.components == null || addition.components.Length == 0 ||
                addition.components.Contains(Ambient) ||
                addition.components.Contains(StaticVehicle) ||
                addition.components.Contains(ItemArrival) ||
                addition.components.Contains(TimePortalArrival)) return null;
            var go = new GameObject("UP Traffic Added - " + addition.id);
            go.SetActive(false);
            go.transform.position = new Vector3(addition.x, addition.y, addition.z);
            go.transform.rotation = Quaternion.Euler(0f, addition.rotation, 0f);
            try
            {
                foreach (var kind in addition.components)
                {
                    Component component;
                    if (kind == RoadArrival) component = go.AddComponent<ArrivalCarComponent>();
                    else if (kind == HelipadArrival) component = go.AddComponent<ArrivalHelipadComponent>();
                    else if (kind == SubwayArrival) component = go.AddComponent<ArrivalSubwayComponent>();
                    else if (kind == SubwayDeparture) component = go.AddComponent<DepartureSubwayComponent>();
                    else if (kind == TunnelDeparture) component = go.AddComponent<DepartureTunnelComponent>();
                    else continue;
                    var field = AccessTools.Field(component.GetType(), "_spawnPoint") ?? SpawnPointField;
                    field?.SetValue(component, go.transform);
                }
                go.SetActive(true);
                return go;
            }
            catch
            {
                UnityEngine.Object.Destroy(go);
                if (runtime) throw;
                return null;
            }
        }

        internal static void ApplyRuntime(string path)
        {
            RestoreRuntime();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var data = Read(path);
            var natives = CaptureNativeItems().ToDictionary(item => item.Id,
                item => item, StringComparer.Ordinal);
            foreach (var entry in data.objects)
            {
                Item item;
                if (!natives.TryGetValue(entry.id, out item) || item.Transform == null) continue;
                if (!RuntimeOriginalStates.ContainsKey(item.Transform))
                    RuntimeOriginalStates.Add(item.Transform, new TransformState
                    {
                        Position = item.Transform.position,
                        Rotation = item.Transform.rotation
                    });
                item.Transform.position = new Vector3(entry.x, entry.y, entry.z);
                item.Transform.rotation = Quaternion.Euler(0f, entry.rotation, 0f);
            }
            foreach (var addition in data.added)
            {
                var go = CreatePointObject(addition, true);
                if (go != null) RuntimeAddedObjects.Add(go);
            }
            UnderPressureEditorPlugin.Log?.LogInfo("Trafico: traffic.json aplicado: " +
                data.objects.Length + " originales, " + data.added.Length + " anadidos.");
        }

        internal static void RestoreRuntime()
        {
            foreach (var pair in RuntimeOriginalStates)
                if (pair.Key != null)
                {
                    pair.Key.position = pair.Value.Position;
                    pair.Key.rotation = pair.Value.Rotation;
                }
            RuntimeOriginalStates.Clear();
            foreach (var go in RuntimeAddedObjects)
                if (go != null) UnityEngine.Object.Destroy(go);
            RuntimeAddedObjects.Clear();
        }

        private static string Hierarchy(Transform node)
        {
            var parts = new List<string>();
            for (var current = node; current != null; current = current.parent)
                parts.Add(current.name + "[" + current.GetSiblingIndex() + "]");
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string StableId(string hierarchy, Vector3 point)
        {
            var invariant = CultureInfo.InvariantCulture;
            var source = hierarchy + "|" + point.x.ToString("R", invariant) + "|" +
                point.y.ToString("R", invariant) + "|" + point.z.ToString("R", invariant);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                return BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    // CursorEditHospitalItem conserva su flujo nativo completo. Solo se limita
    // su búsqueda a los LandscapeRoomItem temporales de la capa de Tráfico.
    [HarmonyPatch(typeof(CursorEditHospitalItem), "GetSelectedItem")]
    internal static class ScenarioTrafficNativeSelectionPatch
    {
        private static bool Prefix(InputManager __0, ref RoomItem __result)
        {
            if (!ScenarioTraffic.IsActive) return true;
            __result = ScenarioTraffic.GetNativeSelectedItem(__0);
            return false;
        }
    }

    // Los adaptadores no forman parte de HospitalPlotDefinition: su posición se
    // guarda exclusivamente en traffic.json al terminar el movimiento nativo.
    [HarmonyPatch(typeof(CursorEditHospitalItem), "OnRoomItemAdded")]
    internal static class ScenarioTrafficDoNotPersistProxyPatch
    {
        private static bool Prefix(RoomItem __0) => !ScenarioTraffic.IsNativeProxy(__0);
    }
}
