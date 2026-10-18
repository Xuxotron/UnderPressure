// Updated: 2026-10-15 - Editor: desactivar profundidad de campo automaticamente y restaurarla al salir.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TH20;
using UnderPressure.LoaderRuntime;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioEditorTerrainPainter.cs =====
// Updated: 2026-10-14 - Panel de pintura elevado sobre el catalogo de objetos.
// Pincel de terreno: UV reales, proyeccion de emergencia y mezcla normalizada entre pasadas.
// Destino: UnderPressure Editor/src/ScenarioEditorTerrainPainter.cs
namespace UnderPressure.Editor
{
    internal static class ScenarioEditorTerrainPainter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly List<TerrainPassContext> Passes = new List<TerrainPassContext>();
        private static readonly List<TerrainLayerSlot> Slots = new List<TerrainLayerSlot>();
        private static readonly Dictionary<Texture2D, Color32[]> OriginalAlphaPixels = new Dictionary<Texture2D, Color32[]>();
        private static readonly Dictionary<Renderer, Material[]> OriginalMaterials = new Dictionary<Renderer, Material[]>();
        private static readonly Dictionary<Mesh, CachedTerrainMesh> MeshCache = new Dictionary<Mesh, CachedTerrainMesh>();
        private static bool _reportedMissingMeshUv;
        private static bool _reportedFallback;
        private static bool _reportedNoPick;
        private static bool _reportedLargeTexture;
        private static bool _loggedFirstPaint;
        private static readonly RaycastHit[] GroundRayHits = new RaycastHit[128];
        private static GameObject _terrainRoot;
        private static Bounds _paintBounds;
        private static bool _initialized;
        private static bool _brushEnabled;
        private static float _brushSize = 5f;
        private static float _brushOpacity = 0.5f;
        private static TerrainLayerSlot _selectedSlot;
        private static ScenarioEditorTerrainPainterBehaviour _behaviour;
        private static Canvas _canvas;
        private static float _nextBrushTime;
        private static Texture2D _crosshairCursor;
        private static bool _cursorLoadAttempted;
        private static bool _customCursorActive;
        private static float _nextCursorRefresh;

        internal enum SculptMode { Off, Raise, Lower, Flatten }
        private static SculptMode _sculptMode = SculptMode.Off;
        private static bool _sculptMeshUnavailable;
        private static bool _sculptClearedByUser;
        internal static bool SculptMeshUnavailable => _sculptMeshUnavailable;
        private static readonly Dictionary<MeshFilter, SculptMesh> SculptMeshes =
            new Dictionary<MeshFilter, SculptMesh>();
        private sealed class SculptMesh
        {
            internal MeshFilter Filter;
            internal Mesh Original;
            internal Mesh Cloned;
            internal MeshCollider Collider;
            internal Mesh ColliderOriginal;
            internal Vector3[] Vertices;
        }
        private static bool _wireframeEnabled;
        private static readonly List<TerrainWireOverlay> WireOverlays =
            new List<TerrainWireOverlay>();
        private static Material _wireMaterial;
        private sealed class TerrainWireOverlay
        {
            internal MeshFilter Source;
            internal GameObject Object;
            internal Mesh Lines;
        }
        internal static bool WireframeEnabled => _wireframeEnabled;
        internal static void ToggleWireframe()
        {
            _wireframeEnabled = !_wireframeEnabled;
            if (_wireframeEnabled) BuildTerrainWireframe();
            else ClearTerrainWireframe();
            _behaviour?.RefreshTexts();
        }
        private static void BuildTerrainWireframe()
        {
            ClearTerrainWireframe();
            var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");
            if (shader == null) return;
            _wireMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (_wireMaterial.HasProperty("_Color"))
                _wireMaterial.SetColor("_Color", new Color(.95f, 1f, 1f, .57f));
            if (_wireMaterial.HasProperty("_SrcBlend"))
            {
                _wireMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _wireMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _wireMaterial.SetInt("_ZWrite", 0);
            }
            var visited = new HashSet<MeshFilter>();
            int count = 0;
            foreach (var pass in Passes)
            {
                var filter = pass.Renderer != null ? pass.Renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || !visited.Add(filter)) continue;
                var source = filter.sharedMesh;
                if (source == null || !source.isReadable) continue;
                try
                {
                    var vertices = source.vertices;
                    var indices = source.triangles;
                    var edges = new List<int>(24000);
                    var unique = new HashSet<ulong>();
                    int triangleCount = indices.Length / 3;
                    int step = Mathf.Max(1, triangleCount / 7000);
                    for (int tri = 0; tri < triangleCount && edges.Count < 24000; tri += step)
                    {
                        var a = indices[tri*3]; var b = indices[tri*3+1]; var c = indices[tri*3+2];
                        AddEdge(a,b,unique,edges);
                        AddEdge(b,c,unique,edges);
                        AddEdge(c,a,unique,edges);
                    }
                    if (edges.Count == 0) continue;
                    var copy = new Vector3[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var world = filter.transform.TransformPoint(vertices[i]) + Vector3.up * .08f;
                        copy[i] = filter.transform.InverseTransformPoint(world);
                    }
                    var mesh = new Mesh { name = "UP_TerrainWireframe", hideFlags = HideFlags.DontSave };
                    mesh.indexFormat = vertices.Length > 65535 ?
                        UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                    mesh.vertices = copy;
                    mesh.SetIndices(edges.ToArray(), MeshTopology.Lines, 0);
                    var go = new GameObject("UP_TerrainWireframe", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(filter.transform, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var render = go.GetComponent<MeshRenderer>();
                    render.sharedMaterial = _wireMaterial;
                    render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    render.receiveShadows = false;
                    WireOverlays.Add(new TerrainWireOverlay { Source = filter, Object = go, Lines = mesh });
                    count++;
                }
                catch (Exception error)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning("Wireframe del terreno: " + error.Message);
                }
            }
            UnderPressureEditorPlugin.Log?.LogInfo("Wireframe del terreno: " + count + " mallas.");
        }
        private static void AddEdge(int a, int b, HashSet<ulong> unique, List<int> list)
        {
            if (a == b) return;
            uint lo = (uint)Mathf.Min(a,b); uint hi = (uint)Mathf.Max(a,b);
            var id = ((ulong)lo << 32) | hi;
            if (unique.Add(id)) { list.Add(a); list.Add(b); }
        }
        private static void RefreshTerrainWireframe(MeshFilter changed)
        {
            if (!_wireframeEnabled) return;
            foreach (var overlay in WireOverlays)
            {
                if (overlay.Source != changed || overlay.Lines == null) continue;
                var source = changed.sharedMesh;
                if (source == null || !source.isReadable) return;
                var points = source.vertices;
                if (points.Length != overlay.Lines.vertexCount) return;
                for (int i = 0; i < points.Length; ++i)
                {
                    var world = changed.transform.TransformPoint(points[i]) + Vector3.up * .08f;
                    points[i] = changed.transform.InverseTransformPoint(world);
                }
                overlay.Lines.vertices = points;
                overlay.Lines.RecalculateBounds();
            }
        }
        private static void ClearTerrainWireframe()
        {
            foreach (var overlay in WireOverlays)
            {
                if (overlay.Object != null) UnityEngine.Object.Destroy(overlay.Object);
                if (overlay.Lines != null) UnityEngine.Object.Destroy(overlay.Lines);
            }
            WireOverlays.Clear();
            if (_wireMaterial != null) UnityEngine.Object.Destroy(_wireMaterial);
            _wireMaterial = null;
        }
        internal static SculptMode CurrentSculptMode => _sculptMode;

        internal static bool Ready => _initialized && Passes.Count > 0;
        internal static bool BrushEnabled => _brushEnabled;
        internal static float BrushSize => _brushSize;
        // El rango visual 1..20 representa 0.85..15 unidades del mundo.
        internal static float WorldBrushRadius => 0.85f +
            (Mathf.Clamp(_brushSize, 1f, 20f) - 1f) * (14.15f / 19f);
        internal static float BrushOpacity => _brushOpacity;
        internal static TerrainLayerSlot SelectedSlot => _selectedSlot;
        internal static IReadOnlyList<TerrainLayerSlot> LayerSlots => Slots;
        internal static string TerrainFolderRoot => ScenarioEditorStorage.Folder;

        internal static void Initialize(Level level)
        {
            Cleanup();
            try
            {
                _terrainRoot = FindTerrainRoot();
                if (_terrainRoot == null)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning("Terrain painter: no se encontro el contenedor Terrain_*");
                    return;
                }

                var renderers = _terrainRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning("Terrain painter: no se encontraron renderers de terreno.");
                    return;
                }

                var boundsSet = false;
                foreach (var renderer in renderers)
                {
                    if (!renderer.gameObject.name.StartsWith("Pass", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var materials = renderer.sharedMaterials;
                    if (materials == null || materials.Length == 0 || materials[0] == null)
                        continue;

                    // NO instanciar materiales ni reemplazar _Control al abrir el
                    // editor. Esa sustitucion prematura modificaba todas las
                    // capas del terreno y creaba las manchas oscuras.
                    var material = materials[0];
                    var pass = new TerrainPassContext
                    {
                        Renderer = renderer,
                        OriginalMaterial = material,
                        PassName = renderer.gameObject.name,
                    };
                    if (material.HasProperty("_Control"))
                        pass.OriginalAlphaTexture = material.GetTexture("_Control") as Texture2D;

                    for (var channel = 0; channel < 4; ++channel)
                    {
                        var property = "_Splat" + channel.ToString(Invariant);
                        if (!material.HasProperty(property)) continue;
                        var texture = material.GetTexture(property) as Texture2D;
                        if (texture == null) continue;
                        var slot = new TerrainLayerSlot
                        {
                            Pass = pass,
                            Channel = channel,
                            Texture = texture,
                            PropertyName = property,
                            DisplayName = BuildSlotName(pass.PassName, channel, texture.name)
                        };
                        pass.Slots.Add(slot);
                        Slots.Add(slot);
                    }

                    Passes.Add(pass);
                    if (!boundsSet)
                    {
                        _paintBounds = renderer.bounds;
                        boundsSet = true;
                    }
                    else
                    {
                        _paintBounds.Encapsulate(renderer.bounds);
                    }
                }

                _selectedSlot = Slots.FirstOrDefault(p => p.Pass.OriginalAlphaTexture != null);
                _initialized = Passes.Any(p => p.OriginalAlphaTexture != null);
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Terrain painter: " + Passes.Count + " pass(es), " + Slots.Count + " capa(s); " +
                    Passes.Count(p => p.OriginalAlphaTexture != null) +
                    " mascara(s) originales sin modificar. Bounds=" + _paintBounds);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("Terrain painter: error al inicializar: " + exception);
                Cleanup();
            }
        }

        internal static void EnsureUi(EditHospitalMenu menu)
        {
            if (!_initialized || menu == null || _behaviour != null) return;
            _canvas = menu.GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                var hud = UnityEngine.Object.FindObjectOfType<Canvas>();
                _canvas = hud;
            }
            if (_canvas == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Terrain painter: no se encontro Canvas para la UI.");
                return;
            }

            var go = new GameObject("UP_TerrainPainterUI",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScenarioEditorTerrainPainterBehaviour));
            go.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-14f, 14f);
            rect.sizeDelta = new Vector2(180f, 535f);
            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.55f);
            _behaviour = go.GetComponent<ScenarioEditorTerrainPainterBehaviour>();
            _behaviour.BuildUi();
            // El hospital asigna su mascara de Fog of War al proxy global de
            // postprocesado. Se anula sólo esa referencia durante la edición.
            ScenarioEditorFogOfWar.EnterEditor();
            _behaviour.RefreshTexts();
        }

        internal static void Cleanup()
        {
            _wireframeEnabled = false;
            ClearTerrainWireframe();
            ReleaseCursor();
            if (_crosshairCursor != null) UnityEngine.Object.Destroy(_crosshairCursor);
            _crosshairCursor = null;
            _cursorLoadAttempted = false;
            _nextCursorRefresh = 0f;
            if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour.gameObject);
            _behaviour = null;
            _canvas = null;
            _brushEnabled = false;
            _sculptMode = SculptMode.Off;
            _sculptMeshUnavailable = false;
            _sculptClearedByUser = false;
            _reportedSculptCorrespondence = false;
            RestoreSculptMeshes();
            _selectedSlot = null;
            _nextBrushTime = 0f;

            // Restaurar renderers ANTES de liberar materiales instanciados.
            foreach (var pair in OriginalMaterials)
            {
                if (pair.Key != null && pair.Value != null)
                    pair.Key.sharedMaterials = pair.Value;
            }
            foreach (var pass in Passes)
            {
                if (pass.AlphaTexture != null)
                    UnityEngine.Object.Destroy(pass.AlphaTexture);
                if (pass.Material != null)
                    UnityEngine.Object.Destroy(pass.Material);
            }

            Passes.Clear();
            Slots.Clear();
            OriginalAlphaPixels.Clear();
            OriginalMaterials.Clear();
            MeshCache.Clear();
            _reportedMissingMeshUv = false;
            _reportedFallback = false;
            _reportedNoPick = false;
            _reportedLargeTexture = false;
            _loggedFirstPaint = false;
            _terrainRoot = null;
            _initialized = false;
        }

        internal static void ToggleBrush()
        {
            _sculptMode = SculptMode.Off;
            _brushEnabled = !_brushEnabled;
            RefreshCursor();
            _behaviour?.RefreshTexts();
        }

        private static void ReleaseCursor()
        {
            if (!_customCursorActive) return;
            Cursor.SetCursor(null, Vector2.zero, UnityEngine.CursorMode.Auto);
            _customCursorActive = false;
        }

        private static void RefreshCursor()
        {
            if (!_brushEnabled)
            {
                ReleaseCursor();
                return;
            }
            if (!_cursorLoadAttempted)
            {
                _cursorLoadAttempted = true;
                var locations = new[]
                {
                    Path.Combine(Paths.ConfigPath, "UnderPressure", "cursor_icon_crosshair.png"),
                    Path.Combine(Paths.PluginPath, "UnderPressure", "Assets", "cursor_icon_crosshair.png")
                };
                foreach (var file in locations)
                {
                    if (!File.Exists(file)) continue;
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        if (bytes.Length > 2 * 1024 * 1024)
                            throw new InvalidDataException("La imagen del cursor supera los 2 MB.");
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        texture.name = "UP_cursor_icon_crosshair";
                        if (!ImageConversion.LoadImage(texture, bytes, false))
                        {
                            UnityEngine.Object.Destroy(texture);
                            throw new InvalidDataException("No se pudo abrir el PNG del cursor.");
                        }
                        texture.filterMode = FilterMode.Point;
                        _crosshairCursor = texture;
                        UnderPressureEditorPlugin.Log?.LogInfo("Cursor del pincel: " + file);
                        break;
                    }
                    catch (Exception exception)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "No se pudo cargar cursor_icon_crosshair.png: " + exception.Message);
                    }
                }
                if (_crosshairCursor == null)
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Cursor del pincel no encontrado en config/UnderPressure ni plugins/UnderPressure/Assets.");
            }
            if (_crosshairCursor == null) return;
            Cursor.SetCursor(_crosshairCursor,
                new Vector2(_crosshairCursor.width * 0.5f, _crosshairCursor.height * 0.5f),
                UnityEngine.CursorMode.Auto);
            _customCursorActive = true;
        }

        internal static void SetSelectedSlot(TerrainLayerSlot slot)
        {
            if (slot == null) return;
            _selectedSlot = slot;
            // Volver al modo pintura cuando se elige una textura,
            // sin seguir deformando la geometria por accidente.
            _sculptMode = SculptMode.Off;
            _behaviour?.RefreshSelection();
            _behaviour?.RefreshTexts();
        }

        internal static void SetBrushSize(float value)
        {
            _brushSize = Mathf.Clamp(Mathf.Round(value), 1f, 20f);
            _behaviour?.RefreshTexts();
        }

        internal static void SetBrushOpacity(float value)
        {
            _brushOpacity = Mathf.Clamp01(Mathf.Round(value) / 100f);
            _behaviour?.RefreshTexts();
        }

        internal static void AdjustSize(float delta)
        {
            _brushSize = Mathf.Clamp(Mathf.Round(_brushSize + delta), 1f, 20f);
            _behaviour?.RefreshTexts();
        }

        internal static void AdjustOpacity(float delta)
        {
            _brushOpacity = Mathf.Clamp01(Mathf.Round(_brushOpacity * 100f + delta * 100f) / 100f);
            _behaviour?.RefreshTexts();
        }

        // La herramienta nativa de RoomItemModifyTerrainComponent solo edita
        // TerrainData. Aqui, el suelo es una malla. Nunca tocar meshes compartidos
        // del juego: se instancian y se restablecen al salir del Editor.
        internal static void SelectSculptMode(SculptMode mode)
        {
            if (mode != SculptMode.Off && !HasReadableTerrainMeshes())
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Esculpir suelo: la geometria Terrain_* no contiene Mesh legible. " +
                    "No se ha modificado ninguna malla.");
                _sculptMode = SculptMode.Off;
                _brushEnabled = false;
                _sculptMeshUnavailable = true;
            }
            else
            {
                _sculptMeshUnavailable = false;
                _sculptMode = _sculptMode == mode ? SculptMode.Off : mode;
                _brushEnabled = _sculptMode != SculptMode.Off;
            }
            RefreshCursor();
            _behaviour?.RefreshTexts();
        }

        private static bool HasReadableTerrainMeshes()
        {
            foreach (var pass in Passes)
            {
                var filter = pass.Renderer == null ? null : pass.Renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable)
                    return true;
            }
            return false;
        }

        private static SculptMesh PrepareSculptMesh(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null) return null;
            if (SculptMeshes.TryGetValue(filter, out var existing)) return existing;
            var original = filter.sharedMesh;
            if (!original.isReadable) return null;
            var mesh = UnityEngine.Object.Instantiate(original);
            mesh.name = original.name + "_UP_SCULPT";
            var collider = filter.GetComponent<MeshCollider>();
            var entry = new SculptMesh
            {
                Filter = filter, Original = original, Cloned = mesh,
                Collider = collider,
                ColliderOriginal = collider != null ? collider.sharedMesh : null,
                Vertices = mesh.vertices
            };
            SculptMeshes.Add(filter, entry);
            filter.sharedMesh = mesh;
            if (collider != null && collider.sharedMesh == original)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }
            return entry;
        }

        private static void RestoreSculptMeshes()
        {
            foreach (var entry in SculptMeshes.Values)
            {
                if (entry.Filter != null) entry.Filter.sharedMesh = entry.Original;
                if (entry.Collider != null) entry.Collider.sharedMesh = entry.ColliderOriginal;
                if (entry.Cloned != null) UnityEngine.Object.Destroy(entry.Cloned);
            }
            SculptMeshes.Clear();
        }

        // Agrupar por coordenadas horizontales (centimetros). Los 3 pases de
        // terreno suelen compartir X/Z, pero no se presupone cantidad ni nombre.
        // Cada grupo conserva la distancia original entre sus superficies.
        private sealed class SculptVertexGroup
        {
            internal float X;
            internal float Z;
            internal float HighestWorldY = float.NegativeInfinity;
            internal readonly List<SculptVertexRef> Members = new List<SculptVertexRef>();
        }

        private struct SculptVertexRef
        {
            internal SculptMesh Mesh;
            internal int Index;
            internal Vector3 World;
        }

        private static bool _reportedSculptCorrespondence;

        private static void SculptAtCursor()
        {
            if (!Input.GetMouseButton(0) || Time.unscaledTime < _nextBrushTime ||
                IsPointerOverInteractiveUi() || _brushSize < 1f) return;
            var cam = Camera.main;
            if (cam == null || _terrainRoot == null) return;
            Vector3 target;
            var pass = _selectedSlot?.Pass ?? Passes.FirstOrDefault();
            if (!TryLocateGroundPoint(cam.ScreenPointToRay(Input.mousePosition), pass,
                    out target)) return;
            _nextBrushTime = Time.unscaledTime + .03f;

            // Primero leer TODOS los vertices de los pases sin cambiar ninguno.
            // El movimiento se decide UNA sola vez por X/Z, usando la altura
            // superior; las demas superficies reciben exactamente el mismo delta.
            var groups = new Dictionary<long, SculptVertexGroup>();
            var visited = new HashSet<MeshFilter>();
            foreach (var terrainPass in Passes)
            {
                var filter = terrainPass.Renderer == null ? null :
                    terrainPass.Renderer.GetComponent<MeshFilter>();
                if (filter == null || !visited.Add(filter)) continue;
                var terrain = PrepareSculptMesh(filter);
                if (terrain == null) continue;
                var vertices = terrain.Vertices;
                var tr = filter.transform;
                for (int i = 0; i < vertices.Length; ++i)
                {
                    var world = tr.TransformPoint(vertices[i]);
                    var dx = world.x - target.x;
                    var dz = world.z - target.z;
                    if (dx * dx + dz * dz > WorldBrushRadius * WorldBrushRadius) continue;
                    int ix = Mathf.RoundToInt(world.x * 100f);
                    int iz = Mathf.RoundToInt(world.z * 100f);
                    long key = ((long)ix << 32) | (uint)iz;
                    SculptVertexGroup group;
                    if (!groups.TryGetValue(key, out group))
                    {
                        group = new SculptVertexGroup { X = world.x, Z = world.z };
                        groups.Add(key, group);
                    }
                    group.HighestWorldY = Mathf.Max(group.HighestWorldY, world.y);
                    group.Members.Add(new SculptVertexRef
                    {
                        Mesh = terrain, Index = i, World = world
                    });
                }
            }
            if (groups.Count == 0) return;

            var changed = new HashSet<SculptMesh>();
            int shared = 0;
            foreach (var group in groups.Values)
            {
                if (group.Members.Count > 1) ++shared;
                var dx = group.X - target.x;
                var dz = group.Z - target.z;
                var radius = WorldBrushRadius;
                var factor = 1f - Mathf.Sqrt(dx * dx + dz * dz) / radius;
                var strength = Mathf.Clamp01(factor * factor * _brushOpacity);
                float delta;
                if (_sculptMode == SculptMode.Raise) delta = 0.3f * strength;
                else if (_sculptMode == SculptMode.Lower) delta = -0.3f * strength;
                else if (_sculptMode == SculptMode.Flatten)
                    delta = -group.HighestWorldY * strength; // superior -> Y=0
                else return;
                if (Mathf.Abs(delta) < 0.000001f) continue;
                foreach (var member in group.Members)
                {
                    var world = member.World;
                    world.y += delta; // identico delta: preserva distancia vertical
                    member.Mesh.Vertices[member.Index] =
                        member.Mesh.Filter.transform.InverseTransformPoint(world);
                    changed.Add(member.Mesh);
                }
            }
            if (!_reportedSculptCorrespondence)
            {
                _reportedSculptCorrespondence = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Esculpir terreno: " + groups.Count + " grupos X/Z, " +
                    shared + " con vertices superpuestos entre pases.");
                if (Passes.Count > 1 && shared == 0)
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Los pases no comparten vertices X/Z en la zona; " +
                        "comprobar la correspondencia de sus triangulaciones.");
            }
            if (changed.Count == 0) return;
            _sculptClearedByUser = false;
            foreach (var terrain in changed)
            {
                terrain.Cloned.vertices = terrain.Vertices;
                terrain.Cloned.RecalculateBounds();
                terrain.Cloned.RecalculateNormals();
                RefreshTerrainWireframe(terrain.Filter);
                if (terrain.Collider != null && terrain.ColliderOriginal == terrain.Original)
                {
                    terrain.Collider.sharedMesh = null;
                    terrain.Collider.sharedMesh = terrain.Cloned;
                }
            }
        }

        private static void ExportSculptMeshes(string folder)
        {
            var path = Path.Combine(folder, "terrain_mesh.bin");
            if (SculptMeshes.Count == 0)
            {
                // No borrar datos de un mapa anterior si las mallas no pudieron
                // leerse en esta sesion. Solo eliminar tras Limpiar mapa explicito.
                if (_sculptClearedByUser && File.Exists(path)) File.Delete(path);
                return;
            }
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x55504D31); // UPM1
                var entries = new List<Tuple<int, SculptMesh>>();
                for (var i = 0; i < Passes.Count; i++)
                {
                    var filter = Passes[i].Renderer == null ? null :
                        Passes[i].Renderer.GetComponent<MeshFilter>();
                    if (filter != null && SculptMeshes.TryGetValue(filter, out var mesh))
                        entries.Add(Tuple.Create(i, mesh));
                }
                writer.Write(entries.Count);
                foreach (var entry in entries)
                {
                    writer.Write(entry.Item1);
                    var heights = entry.Item2.Vertices;
                    writer.Write(heights.Length);
                    for (var i = 0; i < heights.Length; i++) writer.Write(heights[i].y);
                }
            }
        }

        private static void ImportSculptMeshes(string folder)
        {
            RestoreSculptMeshes();
            _sculptClearedByUser = false;
            var path = Path.Combine(folder, "terrain_mesh.bin");
            if (!File.Exists(path)) return;
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != 0x55504D31)
                    throw new InvalidDataException("Cabecera de terrain_mesh.bin invalida.");
                var total = reader.ReadInt32();
                if (total < 0 || total > Passes.Count)
                    throw new InvalidDataException("Numero invalido de mallas guardadas.");
                for (var i = 0; i < total; i++)
                {
                    var passIndex = reader.ReadInt32();
                    var vertexCount = reader.ReadInt32();
                    if (passIndex < 0 || passIndex >= Passes.Count || vertexCount < 0 ||
                        vertexCount > 2000000)
                        throw new InvalidDataException("Entrada invalida en terrain_mesh.bin.");
                    var filter = Passes[passIndex].Renderer == null ? null :
                        Passes[passIndex].Renderer.GetComponent<MeshFilter>();
                    var entry = PrepareSculptMesh(filter);
                    if (entry == null || entry.Vertices.Length != vertexCount)
                        throw new InvalidDataException("Malla de terreno incompatible con mapa guardado.");
                    for (var v = 0; v < vertexCount; v++)
                    {
                        var position = entry.Vertices[v];
                        position.y = reader.ReadSingle();
                        entry.Vertices[v] = position;
                    }
                    entry.Cloned.vertices = entry.Vertices;
                    entry.Cloned.RecalculateBounds();
                    entry.Cloned.RecalculateNormals();
                    if (entry.Collider != null && entry.ColliderOriginal == entry.Original)
                    {
                        entry.Collider.sharedMesh = null;
                        entry.Collider.sharedMesh = entry.Cloned;
                    }
                }
            }
        }

        // Proyeccion del radio en unidades del terreno a pixeles de pantalla.
        // La geometria se toma del terreno cuando hay collider; si no, usa
        // plano Y=0 sin interferir con el pincel de texturas existente.
        internal static bool TryGetBrushWorldPoint(out Vector3 center, out Vector3 normal)
        {
            center = Vector3.zero;
            normal = Vector3.up;
            if (!_initialized || !_brushEnabled || _selectedSlot?.Pass == null ||
                _brushSize <= 0f || IsPointerOverInteractiveUi()) return false;
            var camera = Camera.main;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!TryLocateGroundPoint(ray, _selectedSlot.Pass, out center)) return false;
            // Normal real cuando la colisión corresponde a un terreno, para orientar
            // el anillo perpendicularmente a la superficie.
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 10000f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) && hit.collider != null && _terrainRoot != null &&
                (hit.collider.transform == _terrainRoot.transform ||
                 hit.collider.transform.IsChildOf(_terrainRoot.transform)))
            {
                center = hit.point;
                normal = hit.normal.normalized;
            }
            return true;
        }

        internal static bool TryGetBrushScreenDiameter(out float diameter)
        {
            diameter = 0f;
            if (!_initialized || !_brushEnabled || _selectedSlot?.Pass == null ||
                _brushSize <= 0f || IsPointerOverInteractiveUi()) return false;
            var camera = Camera.main;
            if (camera == null) return false;
            Vector3 point;
            if (!TryLocateGroundPoint(camera.ScreenPointToRay(Input.mousePosition),
                _selectedSlot.Pass, out point)) return false;
            var center = camera.WorldToScreenPoint(point);
            if (center.z <= 0f) return false;
            var right = camera.WorldToScreenPoint(point + Vector3.right * WorldBrushRadius);
            var forward = camera.WorldToScreenPoint(point + Vector3.forward * WorldBrushRadius);
            var radius = Mathf.Max(Vector2.Distance(new Vector2(center.x, center.y),
                    new Vector2(right.x, right.y)),
                Vector2.Distance(new Vector2(center.x, center.y),
                    new Vector2(forward.x, forward.y)));
            diameter = Mathf.Clamp(radius * 2f, 4f, 1000f);
            return true;
        }

        internal static void Update()
        {
            if (_brushEnabled && _crosshairCursor != null && Time.unscaledTime >= _nextCursorRefresh)
            {
                _nextCursorRefresh = Time.unscaledTime + 0.25f;
                RefreshCursor();
            }
            if (!_initialized || !_brushEnabled) return;
            if (_sculptMode != SculptMode.Off)
            {
                SculptAtCursor();
                return;
            }
            if (_selectedSlot == null || _brushSize < 1f) return;
            // El HUD nativo contiene graficos transparentes a pantalla completa.
            // IsPointerOverGameObject() puede bloquear toda la escena; solo bloqueamos
            // los controles interactivos reales bajo el raton.
            if (IsPointerOverInteractiveUi()) return;
            if (!Input.GetMouseButton(0)) return;
            if (Time.unscaledTime < _nextBrushTime) return;
            _nextBrushTime = Time.unscaledTime + 0.09f;

            var camera = Camera.main;
            if (camera == null) return;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            Vector2 uv;
            float radiusU;
            float radiusV;
            if (!TryGetTerrainUv(ray, _selectedSlot.Pass, out uv, out radiusU, out radiusV))
                return;
            PaintAtUv(uv, radiusU, radiusV);
        }

        // Primero se intenta la interseccion exacta con la malla y sus UV0.
        // Algunas mallas de Unity se importan como no legibles en tiempo de ejecucion;
        // cuando eso ocurre, no podemos leer mesh.vertices ni mesh.triangles.
        // Las escenas de Region1 utilizan Terrain-to-Mesh con UV de las mascaras
        // proyectadas sobre el terreno de 500 unidades (raiz Terrain_*).
        // Solo entonces usamos esta proyeccion de emergencia: nunca el bounding box
        // ajustado a la malla, que desplaza enormemente las pinceladas.
        private static bool TryGetTerrainUv(Ray cameraRay, TerrainPassContext pass,
            out Vector2 uv, out float radiusU, out float radiusV)
        {
            uv = Vector2.zero;
            radiusU = radiusV = 0f;
            if (pass?.Renderer == null) return false;
            // MeshCollider.textureCoord es la ruta más barata cuando existe un collider
            // sobre Terrain_*. Se verifica que pertenezca a ese terreno.
            RaycastHit groundHit;
            if (Physics.Raycast(cameraRay, out groundHit, 10000f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                groundHit.collider is MeshCollider && _terrainRoot != null &&
                (groundHit.collider.transform == _terrainRoot.transform ||
                 groundHit.collider.transform.IsChildOf(_terrainRoot.transform)) &&
                IsValidUv(groundHit.textureCoord))
            {
                uv = groundHit.textureCoord;
                var spanX = Mathf.Max(0.001f, _paintBounds.size.x);
                var spanZ = Mathf.Max(0.001f, _paintBounds.size.z);
                radiusU = WorldBrushRadius / spanX;
                radiusV = WorldBrushRadius / spanZ;
                return true;
            }
            var filter = pass.Renderer.GetComponent<MeshFilter>();
            var mesh = filter?.sharedMesh;
            if (mesh != null)
            {
                CachedTerrainMesh cache;
                if (!MeshCache.TryGetValue(mesh, out cache))
                {
                    cache = new CachedTerrainMesh();
                    try
                    {
                        if (mesh.vertexCount > 80000)
                            throw new InvalidOperationException(
                                "Demasiados vertices para interseccion CPU; se usa proyeccion rapida.");
                        cache.Vertices = mesh.vertices;
                        cache.UV = mesh.uv;
                        cache.Triangles = mesh.triangles;
                        if (cache.Vertices == null || cache.UV == null ||
                            cache.UV.Length != cache.Vertices.Length ||
                            cache.Triangles == null || cache.Triangles.Length < 3)
                            throw new InvalidOperationException("La malla no tiene triangulos o UV0 validas.");
                    }
                    catch (Exception exception)
                    {
                        cache.Unavailable = true;
                        if (!_reportedMissingMeshUv)
                        {
                            _reportedMissingMeshUv = true;
                            UnderPressureEditorPlugin.Log?.LogWarning(
                                "Pincel: UV exactas no disponibles en " + mesh.name +
                                " (" + exception.Message + "). Intentando proyeccion del terreno.");
                        }
                    }
                    MeshCache.Add(mesh, cache);
                }
                // La prueba manual de cientos de miles de triángulos por arrastre
                // puede bloquear Unity. Usarla solo con geometría pequeña.
                if (!cache.Unavailable && cache.Triangles.Length <= 45000 &&
                    TryRaycastTerrainMesh(cameraRay, pass, cache, out uv, out radiusU, out radiusV))
                    return true;
            }
            // Una malla no legible o un fallo puntual de interseccion ya no impiden pintar.
            return TryProjectTerrainUv(cameraRay, pass, out uv, out radiusU, out radiusV);
        }

        private static bool TryRaycastTerrainMesh(Ray cameraRay, TerrainPassContext pass,
            CachedTerrainMesh cache, out Vector2 uv, out float radiusU, out float radiusV)
        {
            uv = Vector2.zero;
            radiusU = radiusV = 0f;
            var transform = pass.Renderer.transform;
            var origin = transform.worldToLocalMatrix.MultiplyPoint3x4(cameraRay.origin);
            var direction = transform.worldToLocalMatrix.MultiplyVector(cameraRay.direction);
            var distance = float.PositiveInfinity;
            var triangle = -1;
            float closestU = 0f, closestV = 0f;
            var vertices = cache.Vertices;
            var indices = cache.Triangles;
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                float rayT, bu, bv;
                if (!RayTriangle(origin, direction, vertices[indices[i]],
                    vertices[indices[i + 1]], vertices[indices[i + 2]],
                    out rayT, out bu, out bv) || rayT >= distance)
                    continue;
                distance = rayT;
                closestU = bu;
                closestV = bv;
                triangle = i;
            }
            if (triangle < 0) return false;
            var i0 = indices[triangle];
            var i1 = indices[triangle + 1];
            var i2 = indices[triangle + 2];
            var tex = cache.UV;
            uv = tex[i0] * (1f - closestU - closestV) +
                 tex[i1] * closestU + tex[i2] * closestV;
            if (!IsValidUv(uv)) return false;
            var a = transform.TransformPoint(vertices[i0]);
            var b = transform.TransformPoint(vertices[i1]);
            var c = transform.TransformPoint(vertices[i2]);
            var x1 = b.x - a.x;
            var z1 = b.z - a.z;
            var x2 = c.x - a.x;
            var z2 = c.z - a.z;
            var determinant = x1 * z2 - x2 * z1;
            if (Mathf.Abs(determinant) < 0.00001f)
            {
                radiusU = WorldBrushRadius / Mathf.Max(0.001f, _paintBounds.size.x);
                radiusV = WorldBrushRadius / Mathf.Max(0.001f, _paintBounds.size.z);
            }
            else
            {
                var du1 = tex[i1].x - tex[i0].x;
                var du2 = tex[i2].x - tex[i0].x;
                var dv1 = tex[i1].y - tex[i0].y;
                var dv2 = tex[i2].y - tex[i0].y;
                var duDx = (du1 * z2 - du2 * z1) / determinant;
                var duDz = (x1 * du2 - x2 * du1) / determinant;
                var dvDx = (dv1 * z2 - dv2 * z1) / determinant;
                var dvDz = (x1 * dv2 - x2 * dv1) / determinant;
                radiusU = WorldBrushRadius * Mathf.Sqrt(duDx * duDx + duDz * duDz);
                radiusV = WorldBrushRadius * Mathf.Sqrt(dvDx * dvDx + dvDz * dvDz);
            }
            return radiusU > 0f && radiusV > 0f &&
                   !float.IsNaN(radiusU) && !float.IsNaN(radiusV);
        }

        private static bool TryProjectTerrainUv(Ray ray, TerrainPassContext pass,
            out Vector2 uv, out float radiusU, out float radiusV)
        {
            uv = Vector2.zero;
            radiusU = radiusV = 0f;
            if (_terrainRoot == null || pass?.Renderer == null) return false;

            // Evitar que la altura de arboles o fachadas desplace la posicion:
            // priorizar el collider del propio terreno. Si no existe, usar el
            // punto de la escena situado mas cerca de la altura de terreno.
            Vector3 point;
            if (!TryLocateGroundPoint(ray, pass, out point))
            {
                if (!_reportedNoPick)
                {
                    _reportedNoPick = true;
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Pincel: no se encontro superficie bajo el cursor.");
                }
                return false;
            }
            // Region1 usa UV = (worldXZ - TerrainRootXZ) / (5 * escala Pass).
            // Se comprueba que el origen del terreno este definido y la escala sea valida.
            var scale = pass.Renderer.transform.lossyScale;
            var spanX = Mathf.Abs(scale.x) * 5f;
            var spanZ = Mathf.Abs(scale.z) * 5f;
            if (spanX < 0.001f || spanZ < 0.001f) return false;
            var terrainOrigin = _terrainRoot.transform.position;
            uv = new Vector2((point.x - terrainOrigin.x) / spanX,
                             (point.z - terrainOrigin.z) / spanZ);
            if (!IsValidUv(uv)) return false;
            radiusU = WorldBrushRadius / spanX;
            radiusV = WorldBrushRadius / spanZ;
            if (!_reportedFallback)
            {
                _reportedFallback = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Pincel: usando proyeccion de emergencia del terreno: raiz=" +
                    terrainOrigin + " tamano=" + spanX + "x" + spanZ);
            }
            return true;
        }

        private static bool TryLocateGroundPoint(Ray ray, TerrainPassContext pass, out Vector3 point)
        {
            point = Vector3.zero;
            var hitCount = Physics.RaycastNonAlloc(ray, GroundRayHits, 10000f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var terrainHit = false;
            var nearestTerrain = float.PositiveInfinity;
            var candidate = false;
            var candidateDistance = float.PositiveInfinity;
            Vector3 nearestPoint = Vector3.zero;
            for (int hitIndex = 0; hitIndex < hitCount; ++hitIndex)
            {
                var hit = GroundRayHits[hitIndex];
                if (hit.collider == null) continue;
                var go = hit.collider.gameObject;
                var isTerrain = go.transform == _terrainRoot.transform ||
                                go.transform.IsChildOf(_terrainRoot.transform) ||
                                go.name.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isTerrain && hit.distance < nearestTerrain)
                {
                    terrainHit = true;
                    nearestTerrain = hit.distance;
                    point = hit.point;
                }
                if (hit.distance < candidateDistance)
                {
                    candidateDistance = hit.distance;
                    nearestPoint = hit.point;
                    candidate = true;
                }
            }
            if (terrainHit) return true;
            if (candidate)
            {
                point = nearestPoint;
                return true;
            }
            // Ultimo recurso: aproximar altura cero, que es el plano de
            // construccion del hospital, sin usar el centro del bounding box.
            var plane = new Plane(Vector3.up, Vector3.zero);
            float distance;
            if (!plane.Raycast(ray, out distance) || distance < 0f) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private static bool IsValidUv(Vector2 uv)
        {
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f &&
                   !float.IsNaN(uv.x) && !float.IsNaN(uv.y);
        }

        // Bloquear botones reales del HUD, pero no los paneles transparentes
        // que ocupan la pantalla entera y absorben raycasts del EventSystem.
        private static bool IsPointerOverInteractiveUi()
        {
            if (EventSystem.current == null) return false;
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var result in results)
            {
                var go = result.gameObject;
                if (go == null) continue;
                if (go.GetComponentInParent<Selectable>() != null ||
                    go.GetComponentInParent<ScrollRect>() != null)
                    return true;
                if (_behaviour != null &&
                    (go == _behaviour.gameObject ||
                     go.transform.IsChildOf(_behaviour.transform)))
                    return true;
            }
            return false;
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance, out float u, out float v)
        {
            distance = u = v = 0f;
            var edge1 = b - a;
            var edge2 = c - a;
            var cross = Vector3.Cross(direction, edge2);
            var det = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(det) < 0.00000001f) return false;
            var inv = 1f / det;
            var delta = origin - a;
            u = Vector3.Dot(delta, cross) * inv;
            if (u < -0.00001f || u > 1.00001f) return false;
            var q = Vector3.Cross(delta, edge1);
            v = Vector3.Dot(direction, q) * inv;
            if (v < -0.00001f || u + v > 1.00001f) return false;
            distance = Vector3.Dot(edge2, q) * inv;
            return distance > 0.00001f;
        }

        // El clon del material y su _Control se crean al pintar por primera vez,
        // nunca durante la carga de la escena. Los demas Pass permanecen intactos.
        private static bool EnsureWritablePass(TerrainPassContext pass)
        {
            if (pass == null || pass.Renderer == null || pass.OriginalAlphaTexture == null || pass.CopyFailed)
                return false;
            if (pass.AlphaTexture != null) return true;
            Texture2D clone = null;
            Material instance = null;
            try
            {
                clone = CloneReadableTexture(pass.OriginalAlphaTexture,
                    "UP_TERRAIN_ALPHA_" + pass.PassName);
                instance = new Material(pass.OriginalMaterial);
                instance.name = "UP_" + pass.OriginalMaterial.name;
                instance.hideFlags = HideFlags.DontSave;
                instance.SetTexture("_Control", clone);

                var originals = pass.Renderer.sharedMaterials;
                if (originals == null || originals.Length == 0)
                    throw new InvalidOperationException("Renderer de terreno sin materiales.");
                var replacements = (Material[])originals.Clone();
                var materialIndex = Array.IndexOf(originals, pass.OriginalMaterial);
                if (materialIndex < 0)
                    throw new InvalidOperationException("Material original no encontrado en el renderer.");
                replacements[materialIndex] = instance;

                if (!OriginalMaterials.ContainsKey(pass.Renderer))
                    OriginalMaterials.Add(pass.Renderer, originals);
                pass.Renderer.sharedMaterials = replacements;
                pass.Material = instance;
                pass.AlphaTexture = clone;
                pass.OriginalPixels = clone.GetPixels32();
                // GetPixels32 ya devuelve una copia inmutable para la restauración.
                // Duplicarla otra vez consumía decenas de MB adicionales por pasada.
                OriginalAlphaPixels[clone] = pass.OriginalPixels;
                return true;
            }
            catch (Exception exception)
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                if (clone != null) UnityEngine.Object.Destroy(clone);
                // No reintentar cada frame una pasada irrecuperable: en el log
                // anterior se repetia 138 veces mientras el raton seguia pulsado.
                pass.CopyFailed = true;
                UnderPressureEditorPlugin.Log?.LogError(
                    "Pincel: copia del control fallida en " + pass.PassName +
                    "; se suspende esta pasada hasta reabrir el editor: " + exception);
                return false;
            }
        }

        // Las 3 texturas _Control de Hogsport son una unica mezcla de hasta
        // 12 canales. Su suma es aproximadamente 1 por pixel. Aumentar solo
        // el canal escogido duplica la contribucion del shader y blanquea el
        // terreno. Reducimos proporcionalmente TODOS los demas canales.
        private static void PaintAtUv(Vector2 uv, float uvRadiusX, float uvRadiusY)
        {
            var active = _selectedSlot?.Pass;
            if (active == null || active.OriginalAlphaTexture == null) return;

            // Todas las pasadas se deben preparar juntas para conservar la mezcla.
            var paintPasses = Passes.Where(p => p.OriginalAlphaTexture != null).ToList();
            // Máximo 16 millones de texeles entre máscaras: superar ese límite
            // exige demasiadas copias de RAM/VRAM y puede cerrar Unity sin excepción.
            long texels = 0;
            foreach (var p in paintPasses)
                texels += (long)p.OriginalAlphaTexture.width * p.OriginalAlphaTexture.height;
            if (texels > 16000000L)
            {
                if (!_reportedLargeTexture)
                {
                    _reportedLargeTexture = true;
                    UnderPressureEditorPlugin.Log?.LogError(
                        "Pincel: máscaras demasiado grandes (" + texels +
                        " texeles). Pintura suspendida para evitar falta de memoria. " +
                        "Enviar mods.log con dimensiones de _Control.");
                }
                return;
            }
            foreach (var pass in paintPasses)
                if (!EnsureWritablePass(pass)) return;
            if (!paintPasses.Contains(active) || active.AlphaTexture == null) return;

            var width = active.AlphaTexture.width;
            var height = active.AlphaTexture.height;
            foreach (var pass in paintPasses)
                if (pass.AlphaTexture.width != width || pass.AlphaTexture.height != height)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Pincel: las mascaras de las pasadas tienen resoluciones diferentes.");
                    return;
                }

            var cx = Mathf.RoundToInt(uv.x * (width - 1));
            var cy = Mathf.RoundToInt(uv.y * (height - 1));
            var rx = Mathf.Clamp(Mathf.CeilToInt(uvRadiusX * width), 1, width);
            var ry = Mathf.Clamp(Mathf.CeilToInt(uvRadiusY * height), 1, height);
            var xmin = Mathf.Max(0, cx - rx);
            var xmax = Mathf.Min(width - 1, cx + rx);
            var ymin = Mathf.Max(0, cy - ry);
            var ymax = Mathf.Min(height - 1, cy + ry);
            var regionWidth = xmax - xmin + 1;
            var regionHeight = ymax - ymin + 1;
            var regions = new Color[paintPasses.Count][];
            for (var i = 0; i < paintPasses.Count; ++i)
                regions[i] = paintPasses[i].AlphaTexture.GetPixels(xmin, ymin, regionWidth, regionHeight);

            var selectedPassIndex = paintPasses.IndexOf(active);
            var selectedChannel = _selectedSlot.Channel;
            for (var y = ymin; y <= ymax; ++y)
            {
                var dy = (y - cy) / (float)ry;
                for (var x = xmin; x <= xmax; ++x)
                {
                    var dx = (x - cx) / (float)rx;
                    var d = dx * dx + dy * dy;
                    if (d >= 1f) continue;
                    var strength = _brushOpacity * (1f - Mathf.Sqrt(d));
                    var index = (y - ymin) * regionWidth + x - xmin;
                    var sum = 0f;
                    for (var i = 0; i < regions.Length; ++i)
                    {
                        var pixel = regions[i][index];
                        sum += pixel.r + pixel.g + pixel.b + pixel.a;
                    }
                    if (sum < 0.001f) continue;

                    var chosenPixel = regions[selectedPassIndex][index];
                    var chosen = GetChannel(chosenPixel, selectedChannel) / sum;
                    var newChosen = Mathf.Lerp(chosen, 1f, strength);
                    var remaining = Mathf.Max(0f, 1f - chosen);
                    if (remaining < 0.00001f) continue;
                    var othersScale = (1f - newChosen) / remaining;
                    var inv = 1f / sum;
                    for (var i = 0; i < regions.Length; ++i)
                    {
                        var color = regions[i][index];
                        for (var c = 0; c < 4; ++c)
                        {
                            var value = (i == selectedPassIndex && c == selectedChannel)
                                ? newChosen : GetChannel(color, c) * inv * othersScale;
                            SetChannel(ref color, c, Mathf.Clamp01(value));
                        }
                        regions[i][index] = color;
                    }
                }
            }

            for (var i = 0; i < paintPasses.Count; ++i)
            {
                paintPasses[i].AlphaTexture.SetPixels(xmin, ymin, regionWidth, regionHeight, regions[i]);
                paintPasses[i].AlphaTexture.Apply(false, false);
            }
            if (!_loggedFirstPaint)
            {
                _loggedFirstPaint = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Pincel: primera pincelada aplicada. Pass=" + active.PassName +
                    " UV=" + uv + " region=" + regionWidth + "x" + regionHeight +
                    " masks=" + paintPasses.Count);
            }
        }

        private static float GetChannel(Color color, int channel)
        {
            return channel == 0 ? color.r : channel == 1 ? color.g : channel == 2 ? color.b : color.a;
        }

        private static void SetChannel(ref Color color, int channel, float value)
        {
            if (channel == 0) color.r = value;
            else if (channel == 1) color.g = value;
            else if (channel == 2) color.b = value;
            else color.a = value;
        }

        internal static void ClearMap(Level level)
        {
            try
            {
                RestoreOriginalTerrain();
                RestoreSculptMeshes();
                _sculptClearedByUser = true;
                ClearPlacedLandscape(level);
                foreach (var plot in level.WorldState.HospitalPlots)
                {
                    if (plot?.HospitalMap != null)
                        plot.HospitalMap.Build(false);
                }
                level.WorldState.CalculateLighting();
                UnderPressureEditorPlugin.Log?.LogInfo("Terrain painter: limpiar mapa ejecutado.");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("Terrain painter: limpiar mapa fallo: " + exception);
            }
        }

        private static void RestoreOriginalTerrain()
        {
            foreach (var pass in Passes)
            {
                if (pass.AlphaTexture == null) continue;
                Color32[] original;
                if (!OriginalAlphaPixels.TryGetValue(pass.AlphaTexture, out original) || original == null) continue;
                pass.AlphaTexture.SetPixels32(original.ToArray());
                pass.AlphaTexture.Apply(false, false);
            }
        }

        private static void ClearPlacedLandscape(Level level)
        {
            if (level == null) return;
            foreach (var plot in level.WorldState.HospitalPlots)
            {
                var definition = plot?.Definition;
                if (definition == null) continue;
                foreach (HospitalPlotLayer layer in Enum.GetValues(typeof(HospitalPlotLayer)))
                {
                    var items = definition.GetItems(layer);
                    if (items == null) continue;
                    items.Clear();
                }
            }
        }

        internal static string GetSidecarFolder(Level level, string mapPath)
        {
            return Path.GetDirectoryName(Path.GetFullPath(mapPath));
        }

        internal static void ExportSidecar(Level level, string mapPath)
        {
            if (!Ready || level == null || string.IsNullOrEmpty(mapPath)) return;
            var folder = GetSidecarFolder(level, mapPath);
            Directory.CreateDirectory(folder);
            for (var i = 0; i < Passes.Count; ++i)
            {
                var pass = Passes[i];
                Texture2D toExport = pass.AlphaTexture;
                var temporary = false;
                if (toExport == null && pass.OriginalAlphaTexture != null)
                {
                    toExport = CloneReadableTexture(pass.OriginalAlphaTexture,
                        "UP_TERRAIN_EXPORT_" + i.ToString(Invariant));
                    temporary = true;
                }
                if (toExport == null) continue;
                try
                {
                    File.WriteAllBytes(Path.Combine(folder, "alpha_" + i.ToString(Invariant) + ".png"),
                        ImageConversion.EncodeToPNG(toExport));
                }
                finally
                {
                    if (temporary) UnityEngine.Object.Destroy(toExport);
                }
            }
            ExportSculptMeshes(folder);
            UnderPressureEditorPlugin.Log?.LogInfo("Terrain painter: sidecar exportado en " + folder);
        }

        internal static void ImportSidecar(Level level, string mapPath)
        {
            if (!Ready || level == null || string.IsNullOrEmpty(mapPath)) return;
            var folder = GetSidecarFolder(level, mapPath);
            // Al cambiar de mapa, el terreno anterior no debe quedar por debajo.
            // Se restablece el original y solo se sustituyen las pasadas con PNG.
            RestoreOriginalTerrain();
            RestoreSculptMeshes();
            if (!Directory.Exists(folder))
            {
                UnderPressureEditorPlugin.Log?.LogInfo("Terrain painter: no existe sidecar en " + folder);
                return;
            }
            for (var i = 0; i < Passes.Count; ++i)
            {
                var png = Path.Combine(folder, "alpha_" + i.ToString(Invariant) + ".png");
                if (!File.Exists(png)) continue;
                var pass = Passes[i];
                if (!EnsureWritablePass(pass)) continue;
                var target = pass.AlphaTexture;
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                ImageConversion.LoadImage(source, File.ReadAllBytes(png), false);
                if (source.width != target.width || source.height != target.height)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Terrain painter: alpha_" + i + " ignorada por dimensiones incompatibles.");
                    UnityEngine.Object.Destroy(source);
                    continue;
                }
                target.SetPixels32(source.GetPixels32());
                target.Apply(false, false);
                UnityEngine.Object.Destroy(source);
            }
            ImportSculptMeshes(folder);
            UnderPressureEditorPlugin.Log?.LogInfo("Terrain painter: sidecar importado desde " + folder);
        }

        internal static string GetManifest(Level level)
        {
            return Ready && level != null ? BuildManifest(level) : string.Empty;
        }

        private static string BuildManifest(Level level)
        {
            var lines = new List<string>
            {
                "hospital=" + (level.Config?.UniqueId ?? ""),
                "display_name=" + (level.Config?.GetLocalisedDisplayName() ?? ""),
                "passes=" + Passes.Count.ToString(Invariant),
                "sculpt_file=" + (SculptMeshes.Count > 0 ? "terrain_mesh.bin" : ""),
            };
            for (var i = 0; i < Slots.Count; ++i)
            {
                var slot = Slots[i];
                lines.Add("slot_" + i + "=" + slot.DisplayName);
            }
            return string.Join("\n", lines.ToArray());
        }

        private static string BuildSlotName(string passName, int channel, string textureName)
        {
            var channelName = channel == 0 ? "R" : channel == 1 ? "G" : channel == 2 ? "B" : "A";
            return passName + " " + channelName + " · " + textureName;
        }

        private static Texture2D CloneReadableTexture(Texture source, string name)
        {
            // Los materiales devuelven Texture; _Control puede ser Texture2D u otro tipo.
            // La copia siempre es una Texture2D legible, sin modificar el recurso original.
            var clone = new Texture2D(source.width, source.height,
                TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = source.filterMode,
                wrapMode = source.wrapMode,
                anisoLevel = source.anisoLevel,
                hideFlags = HideFlags.DontSave
            };
            var source2D = source as Texture2D;
            if (source2D != null)
            {
                try
                {
                    clone.SetPixels32(source2D.GetPixels32());
                    clone.Apply(false, false);
                    return clone;
                }
                catch (UnityException)
                {
                    // Algunas versiones de Unity lanzan UnityException.
                }
                catch (ArgumentException)
                {
                    // TPH/Unity 2021 lanza ArgumentException en GetPixels32
                    // cuando _Control no es legible (confirmado en Player.log).
                }
                // Ambos casos continúan por Graphics.Blit + ReadPixels (GPU).
            }

            var rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                clone.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                clone.Apply(false, false);
                return clone;
            }
            catch
            {
                UnityEngine.Object.Destroy(clone);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static GameObject FindTerrainRoot()
        {
            foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (transform == null) continue;
                if (transform.name.StartsWith("Terrain_", StringComparison.OrdinalIgnoreCase))
                    return transform.gameObject;
            }
            return null;
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; ++i)
                if (invalid.Contains(chars[i])) chars[i] = '_';
            return new string(chars).Trim();
        }

        private sealed class CachedTerrainMesh
        {
            internal Vector3[] Vertices;
            internal Vector2[] UV;
            internal int[] Triangles;
            internal bool Unavailable;
        }

        internal sealed class TerrainPassContext
        {
            internal string PassName;
            internal Renderer Renderer;
            internal Material Material;
            internal Material OriginalMaterial;
            internal Texture2D AlphaTexture;
            internal Texture OriginalAlphaTexture;
            internal Color32[] OriginalPixels;
            internal bool CopyFailed;
            internal readonly List<TerrainLayerSlot> Slots = new List<TerrainLayerSlot>();
        }

        internal sealed class TerrainLayerSlot
        {
            internal TerrainPassContext Pass;
            internal int Channel;
            internal Texture2D Texture;
            internal string PropertyName;
            internal string DisplayName;
        }
    }

    internal sealed class ScenarioEditorTerrainPainterBehaviour : MonoBehaviour
    {
        private Text _toggleText;
        private Text _sizeText;
        private Text _opacityText;
        private Slider _sizeSlider;
        private Slider _opacitySlider;
        private GameObject _dialog;
        private readonly List<Button> _slots = new List<Button>();
        private readonly List<Texture2D> _previewCopies = new List<Texture2D>();
        private Text _fogOfWarText;
        private Text _wireText;
        private RectTransform _cursor;
        private RawImage _cursorImage;
        private Texture2D _cursorTexture;
        private LineRenderer _worldRing;
        private Material _worldRingMaterial;
        private const int RingSegments = 64;
        private RectTransform _content;
        private ScrollRect _scroll;

        private static string T(string key) => EditorLocalization.Get(key);

        internal void BuildUi()
        {
            var root = (RectTransform)transform;
            CreateText(root, "Title", T("editor.terrain.title"),
                new Vector2(9, -9), new Vector2(162, 24), 16, TextAnchor.MiddleCenter);
            CreateButton(root, new Vector2(9, -39), new Vector2(162, 32),
                T("editor.terrain.brush"), () =>
                ScenarioEditorTerrainPainter.ToggleBrush(), out _toggleText);
            CreateText(root, "Size", T("editor.terrain.size"),
                new Vector2(9, -80), new Vector2(104, 19), 12, TextAnchor.MiddleLeft);
            _sizeText = CreateText(root, "SizeValue", "5", new Vector2(121, -80),
                new Vector2(50, 19), 12, TextAnchor.MiddleRight);
            _sizeSlider = CreateSlider(root, new Vector2(12, -108), new Vector2(156, 17), 1, 20, 5,
                value => ScenarioEditorTerrainPainter.SetBrushSize(value));
            CreateText(root, "Opacity", T("editor.terrain.opacity"),
                new Vector2(9, -133), new Vector2(104, 19), 12, TextAnchor.MiddleLeft);
            _opacityText = CreateText(root, "OpacityValue", "50%", new Vector2(121, -133),
                new Vector2(50, 19), 12, TextAnchor.MiddleRight);
            _opacitySlider = CreateSlider(root, new Vector2(12, -162), new Vector2(156, 17), 0, 100, 50,
                value => ScenarioEditorTerrainPainter.SetBrushOpacity(value));
            CreateButton(root, new Vector2(9, -191), new Vector2(51, 28),
                T("editor.terrain.raise"), () => ScenarioEditorTerrainPainter.SelectSculptMode(
                    ScenarioEditorTerrainPainter.SculptMode.Raise), out _);
            CreateButton(root, new Vector2(65, -191), new Vector2(51, 28),
                T("editor.terrain.lower"), () => ScenarioEditorTerrainPainter.SelectSculptMode(
                    ScenarioEditorTerrainPainter.SculptMode.Lower), out _);
            CreateButton(root, new Vector2(121, -191), new Vector2(51, 28),
                T("editor.terrain.flatten"), () => ScenarioEditorTerrainPainter.SelectSculptMode(
                    ScenarioEditorTerrainPainter.SculptMode.Flatten), out _);

            CreateButton(root, new Vector2(9, -226), new Vector2(162, 30),
                T("editor.terrain.fog_of_war"), () =>
                {
                    ScenarioEditorFogOfWar.Toggle();
                    RefreshTexts();
                }, out _fogOfWarText);
            CreateButton(root, new Vector2(9, -261), new Vector2(162, 30),
                T("editor.terrain.wireframe"), () => ScenarioEditorTerrainPainter.ToggleWireframe(),
                out _wireText);
            var scrollRoot = NewUi("Textures", root, typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollRoot.transform;
            Place(scrollRect, new Vector2(8, -299), new Vector2(164, 227));
            scrollRoot.GetComponent<Image>().color = new Color(0f,0f,0f,0.18f);
            _scroll = scrollRoot.GetComponent<ScrollRect>();
            _scroll.vertical = true;
            _scroll.horizontal = false;
            _scroll.scrollSensitivity = 54f;
            var viewport = NewUi("Viewport", scrollRect, typeof(Image), typeof(Mask));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(1f,1f,1f,0.015f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            var content = NewUi("Content", viewportRect, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f,1f);
            _content.anchorMax = new Vector2(1f,1f);
            _content.pivot = new Vector2(.5f,1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 5, 5);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
            _scroll.viewport = viewportRect;
            foreach (var slot in ScenarioEditorTerrainPainter.LayerSlots)
            {
                if (slot.Texture == null) continue;
                var button = CreateTextureButton(_content, slot);
                _slots.Add(button);
            }
            _scroll.verticalNormalizedPosition = 1f;
            CreateCursorOverlay(root.GetComponentInParent<Canvas>());
            RefreshTexts();
            RefreshSelection();
        }

        private static GameObject NewUi(string name, Transform parent, params Type[] components)
        {
            var types = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
            types.AddRange(components);
            var go = new GameObject(name, types.ToArray());
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Place(RectTransform rect, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f,1f);
            rect.pivot = new Vector2(0f,1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        private static Text CreateText(Transform parent, string name, string value,
            Vector2 pos, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var go = NewUi(name, parent, typeof(Text));
            Place((RectTransform)go.transform, pos, size);
            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = alignment;
            label.color = Color.white;
            label.text = value;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(Transform parent, Vector2 pos, Vector2 size,
            string title, Action action, out Text label)
        {
            var go = NewUi("Button_"+title, parent, typeof(Image), typeof(Button));
            Place((RectTransform)go.transform, pos, size);
            go.GetComponent<Image>().color = new Color(.58f,.68f,.74f,1f);
            var button = go.GetComponent<Button>();
            if (action != null) button.onClick.AddListener(() => action());
            label = CreateText(go.transform,"Text",title,Vector2.zero,size,13,TextAnchor.MiddleCenter);
            return button;
        }

        private static Slider CreateSlider(Transform parent, Vector2 pos, Vector2 size,
            int min, int max, int initial, Action<float> onChange)
        {
            var go = NewUi("Slider",parent,typeof(Slider),typeof(Image));
            Place((RectTransform)go.transform,pos,size);
            go.GetComponent<Image>().color = new Color(.18f,.27f,.33f,1f);
            var fill = NewUi("Fill",go.transform,typeof(Image));
            var fillRect=(RectTransform)fill.transform;
            fillRect.anchorMin= new Vector2(0f,0f);
            fillRect.anchorMax= new Vector2(1f,1f);
            fillRect.offsetMin= new Vector2(3f,3f);
            fillRect.offsetMax= new Vector2(-3f,-3f);
            fill.GetComponent<Image>().color = new Color(.23f,.74f,.98f,1f);
            fill.GetComponent<Image>().raycastTarget = false;
            var handle = NewUi("Handle",go.transform,typeof(Image));
            var hr=(RectTransform)handle.transform;
            hr.anchorMin=new Vector2(.5f,.5f);
            hr.anchorMax=new Vector2(.5f,.5f);
            hr.sizeDelta = new Vector2(13f,23f);
            handle.GetComponent<Image>().color = new Color(.97f,.97f,.91f,1f);
            var slider=go.GetComponent<Slider>();
            slider.targetGraphic=handle.GetComponent<Image>();
            slider.fillRect=fillRect;
            slider.handleRect=hr;
            slider.direction=Slider.Direction.LeftToRight;
            slider.wholeNumbers=true;
            slider.minValue=min;
            slider.maxValue=max;
            slider.value=initial;
            slider.onValueChanged.AddListener(value => onChange?.Invoke(value));
            return slider;
        }

        private Button CreateTextureButton(Transform parent,
            ScenarioEditorTerrainPainter.TerrainLayerSlot slot)
        {
            var item=NewUi("Texture",parent,typeof(Image),typeof(Button),typeof(LayoutElement));
            item.GetComponent<Image>().color = new Color(.16f,.34f,.42f,1f);
            var element=item.GetComponent<LayoutElement>();
            element.minHeight=85f;
            element.preferredHeight=85f;
            var image=NewUi("Preview",item.transform,typeof(RawImage));
            var rect=(RectTransform)image.transform;
            rect.anchorMin=Vector2.zero;
            rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(5f,5f);
            rect.offsetMax=new Vector2(-5f,-5f);
            var preview=image.GetComponent<RawImage>();
            // Las texturas tienen un canal alfa de material: dibujarlas
            // directamente sobre el boton azul produce miniaturas azuladas.
            // Copia reducida con alfa 255, sin modificar el recurso original.
            preview.texture=MakeOpaquePreview(slot.Texture);
            preview.color=Color.white;
            preview.raycastTarget=false;
            var button=item.GetComponent<Button>();
            button.onClick.AddListener(() => ScenarioEditorTerrainPainter.SetSelectedSlot(slot));
            return button;
        }

        private Texture2D MakeOpaquePreview(Texture2D source)
        {
            if (source == null) return null;
            const int w = 96, h = 76;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var before = RenderTexture.active;
            Texture2D thumbnail = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                thumbnail = new Texture2D(w,h,TextureFormat.RGBA32,false);
                thumbnail.ReadPixels(new Rect(0,0,w,h),0,0,false);
                var colors = thumbnail.GetPixels32();
                for (int i=0;i<colors.Length;i++) colors[i].a = 255;
                thumbnail.SetPixels32(colors);
                thumbnail.Apply(false,false);
                thumbnail.filterMode = FilterMode.Bilinear;
                _previewCopies.Add(thumbnail);
                return thumbnail;
            }
            catch (Exception ex)
            {
                if (thumbnail != null) UnityEngine.Object.Destroy(thumbnail);
                UnderPressureEditorPlugin.Log?.LogWarning("Miniatura de terreno: " + ex.Message);
                return source;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void ShowClearConfirmation()
        {
            if (_dialog != null) return;
            var canvas=GetComponentInParent<Canvas>();
            if (canvas==null) return;
            _dialog=NewUi("UP_Clear_Confirm",canvas.transform,typeof(Image));
            var rect=(RectTransform)_dialog.transform;
            rect.anchorMin=Vector2.zero;
            rect.anchorMax=Vector2.one;
            rect.offsetMin=Vector2.zero;
            rect.offsetMax=Vector2.zero;
            _dialog.GetComponent<Image>().color=new Color(0f,0f,0f,.70f);
            _dialog.transform.SetAsLastSibling();
            var panel=NewUi("ConfirmPanel",_dialog.transform,typeof(Image));
            var pr=(RectTransform)panel.transform;
            pr.anchorMin=pr.anchorMax=new Vector2(.5f,.5f);
            pr.pivot=new Vector2(.5f,.5f);
            pr.sizeDelta=new Vector2(480f,214f);
            panel.GetComponent<Image>().color=new Color(.22f,.26f,.29f,.98f);
            CreateText(panel.transform,"Warning",T("editor.terrain.clear.warning"),
                new Vector2(20,-27),new Vector2(440,95),17,TextAnchor.MiddleCenter);
            CreateButton(panel.transform,new Vector2(33,-161),new Vector2(190,39),
                T("editor.terrain.clear.cancel"),CloseDialog,out _);
            CreateButton(panel.transform,new Vector2(255,-161),new Vector2(190,39),
                T("editor.terrain.clear.accept"),() =>
                {
                    CloseDialog();
                    ScenarioEditorTerrainPainter.ClearMap(ScenarioEditorSession.Level);
                },out _);
        }

        private void CloseDialog()
        {
            if (_dialog != null) UnityEngine.Object.Destroy(_dialog);
            _dialog=null;
        }

        private void CreateCursorOverlay(Canvas canvas)
        {
            if (canvas==null) return;
            var go=NewUi("UP_Paint_Crosshair",canvas.transform,typeof(RawImage));
            go.transform.SetAsLastSibling();
            _cursor=(RectTransform)go.transform;
            _cursor.anchorMin=_cursor.anchorMax=new Vector2(.5f,.5f);
            _cursor.sizeDelta=new Vector2(40f,40f);
            _cursorImage=go.GetComponent<RawImage>();
            _cursorImage.raycastTarget=false;
            _cursorImage.color=Color.white;
            // If PNG is absent, show the normal pointer; no opaque square.
            var paths=new []{
                Path.Combine(Paths.ConfigPath,"UnderPressure","cursor_icon_crosshair.png"),
                Path.Combine(Paths.PluginPath,"UnderPressure","Assets","cursor_icon_crosshair.png")};
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var tmp=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    if (!ImageConversion.LoadImage(tmp,File.ReadAllBytes(path),false))
                    {
                        UnityEngine.Object.Destroy(tmp);
                        continue;
                    }
                    _cursorTexture=tmp;
                    _cursorImage.texture=tmp;
                    break;
                }
                catch(Exception ex){UnderPressureEditorPlugin.Log?.LogWarning("Cursor: "+ex.Message);}
            }
            // Always display a cursor, even if the external PNG is missing.
            if (_cursorTexture == null)
            {
                var fallback = new Texture2D(33, 33, TextureFormat.RGBA32, false);
                var pixels = new Color32[33 * 33];
                for (var y = 0; y < 33; y++)
                    for (var x = 0; x < 33; x++)
                    {
                        var cross = (Mathf.Abs(x - 16) <= 2 && Mathf.Abs(y - 16) <= 13) ||
                                    (Mathf.Abs(y - 16) <= 2 && Mathf.Abs(x - 16) <= 13);
                        var inner = (x == 16 && Mathf.Abs(y - 16) <= 12) ||
                                    (y == 16 && Mathf.Abs(x - 16) <= 12);
                        pixels[y * 33 + x] = !cross ? new Color32(0,0,0,0) :
                            inner ? new Color32(255,255,255,255) : new Color32(10,10,10,255);
                    }
                fallback.SetPixels32(pixels);
                fallback.Apply(false, false);
                _cursorTexture = fallback;
                _cursorImage.texture = fallback;
            }
            go.SetActive(false);
            // Contorno vectorial situado en el MUNDO, no un círculo 2D en pantalla.
            var world = new GameObject("UP_WorldBrushRing", typeof(LineRenderer));
            _worldRing = world.GetComponent<LineRenderer>();
            _worldRing.useWorldSpace = true;
            _worldRing.loop = true;
            _worldRing.positionCount = RingSegments;
            _worldRing.startWidth = _worldRing.endWidth = 0.06f;
            _worldRing.numCapVertices = 0;
            _worldRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _worldRing.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _worldRingMaterial = new Material(shader);
                _worldRingMaterial.hideFlags = HideFlags.DontSave;
                _worldRing.material = _worldRingMaterial;
            }
            _worldRing.startColor = _worldRing.endColor =
                new Color(0.94f, 0.99f, 1f, 0.74f);
            world.SetActive(false);
        }

        internal void RefreshTexts()
        {
            if (_wireText != null) _wireText.text = T(
                ScenarioEditorTerrainPainter.WireframeEnabled
                    ? "editor.terrain.wireframe.on" : "editor.terrain.wireframe.off");
            if (_fogOfWarText != null)
                _fogOfWarText.text = T(ScenarioEditorFogOfWar.Disabled
                    ? "editor.terrain.fog.off" : "editor.terrain.fog.on");
            if (_toggleText != null)
                _toggleText.text = ScenarioEditorTerrainPainter.SculptMeshUnavailable ?
                    T("editor.terrain.mesh_uneditable") :
                    ScenarioEditorTerrainPainter.CurrentSculptMode !=
                    ScenarioEditorTerrainPainter.SculptMode.Off ?
                    EditorLocalization.Format("editor.terrain.sculpting",
                        T(ScenarioEditorTerrainPainter.CurrentSculptMode ==
                        ScenarioEditorTerrainPainter.SculptMode.Raise ? "editor.terrain.raise" :
                        ScenarioEditorTerrainPainter.CurrentSculptMode ==
                        ScenarioEditorTerrainPainter.SculptMode.Lower ? "editor.terrain.lower" :
                        "editor.terrain.flatten")) :
                    ScenarioEditorTerrainPainter.BrushEnabled ?
                    T("editor.terrain.brush.on") :
                    T("editor.terrain.brush.off");
            if (_sizeText!=null) _sizeText.text=ScenarioEditorTerrainPainter.BrushSize.ToString("0",CultureInfo.InvariantCulture);
            if (_opacityText!=null) _opacityText.text=Mathf.RoundToInt(ScenarioEditorTerrainPainter.BrushOpacity*100f)+"%";
            if (_sizeSlider!=null && Mathf.Abs(_sizeSlider.value-ScenarioEditorTerrainPainter.BrushSize)>.1f)
                _sizeSlider.SetValueWithoutNotify(ScenarioEditorTerrainPainter.BrushSize);
            if (_opacitySlider!=null && Mathf.Abs(_opacitySlider.value-ScenarioEditorTerrainPainter.BrushOpacity*100f)>.1f)
                _opacitySlider.SetValueWithoutNotify(ScenarioEditorTerrainPainter.BrushOpacity*100f);
        }

        internal void RefreshSelection()
        {
            var slots=ScenarioEditorTerrainPainter.LayerSlots;
            var selected=ScenarioEditorTerrainPainter.SelectedSlot;
            for(int i=0;i<_slots.Count;i++)
            {
                var image=_slots[i].GetComponent<Image>();
                image.color=slots[i]==selected?new Color(.96f,.73f,.17f,1f):new Color(.19f,.31f,.39f,1f);
            }
        }

        private void Update()
        {
            ScenarioEditorTerrainPainter.Update();
            var canvas = GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.transform as RectTransform : null;
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            Vector2 position = Vector2.zero;
            var positioned = root != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root,
                    Input.mousePosition, camera, out position);
            if (_worldRing != null)
            {
                Vector3 center, normal;
                bool showRing = ScenarioEditorTerrainPainter.TryGetBrushWorldPoint(
                    out center, out normal);
                if (_worldRing.gameObject.activeSelf != showRing)
                    _worldRing.gameObject.SetActive(showRing);
                if (showRing)
                {
                    // Dos vectores ortogonales en el plano tangente al suelo.
                    var tangent = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
                    if (tangent.sqrMagnitude < 0.01f)
                        tangent = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
                    var bitangent = Vector3.Cross(normal, tangent).normalized;
                    var radius = ScenarioEditorTerrainPainter.WorldBrushRadius;
                    for (int i = 0; i < RingSegments; ++i)
                    {
                        var a = i * (Mathf.PI * 2f / RingSegments);
                        _worldRing.SetPosition(i, center + normal * 0.12f +
                            radius * (Mathf.Cos(a) * tangent + Mathf.Sin(a) * bitangent));
                    }
                }
            }
            if (_cursor==null || _cursorImage==null) return;
            bool visible=positioned && ScenarioEditorTerrainPainter.BrushEnabled && _cursorTexture!=null;
            _cursor.gameObject.SetActive(visible);
            if (!visible) return;
            if (positioned) _cursor.anchoredPosition = position;
        }

        private void LateUpdate()
        {
            ScenarioEditorFogOfWar.EnforceDisabled();
        }

        private void OnDestroy()
        {
            CloseDialog();
            ScenarioEditorFogOfWar.Restore();
            foreach (var preview in _previewCopies)
                if (preview != null) UnityEngine.Object.Destroy(preview);
            _previewCopies.Clear();
            if(_cursor!=null) UnityEngine.Object.Destroy(_cursor.gameObject);
            if(_cursorTexture!=null) UnityEngine.Object.Destroy(_cursorTexture);
            if(_worldRing!=null) UnityEngine.Object.Destroy(_worldRing.gameObject);
            if(_worldRingMaterial!=null) UnityEngine.Object.Destroy(_worldRingMaterial);
        }
    }

    // FogOfWarRenderer deja pasar la imagen sin modificar cuando la definición
    // del proxy es nula. Se conserva cada valor original para restaurarlo al salir.
    internal static class ScenarioEditorFogOfWar
    {
        private static readonly Dictionary<PostProcessingRendererData,
            FogOfWarLevelTextureDefinition> Originals =
            new Dictionary<PostProcessingRendererData, FogOfWarLevelTextureDefinition>();
        private static bool _enteredEditor;
        internal static bool Disabled { get; private set; }

        internal static void EnterEditor()
        {
            if (_enteredEditor) return;
            _enteredEditor = true;
            Disabled = true;
            EnforceDisabled();
        }

        internal static void Toggle()
        {
            if (Disabled) { Restore(); return; }
            Disabled = true;
            EnforceDisabled();
        }

        internal static void EnforceDisabled()
        {
            if (!Disabled) return;
            var data = PostProcessingRendererProxy.Instance?.PostProcessRendererData;
            if (data == null) return;
            if (!Originals.ContainsKey(data))
                Originals.Add(data, data.FogOfWarDefinition);
            data.FogOfWarDefinition = null;
        }

        internal static void Restore()
        {
            foreach (var entry in Originals)
            {
                try { if (entry.Key != null) entry.Key.FogOfWarDefinition = entry.Value; }
                catch { }
            }
            Originals.Clear();
            Disabled = false;
            _enteredEditor = false;
        }
    }

    // Mientras el pincel esta activo, el cursor de suelo no debe recibir el
    // mismo clic ni reconstruir la parcela al pintar una textura de terreno.
    [HarmonyPatch(typeof(CursorEditHospitalBuild), "CursorUpdate")]
    internal static class ScenarioTerrainBrushBlocksFloorCursorPatch
    {
        private static bool Prefix() => !ScenarioEditorTerrainPainter.BrushEnabled;
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "StartEdit")]
    internal static class ScenarioTerrainPainterStartPatch
    {
        private static void Postfix(HospitalEditEvents __instance)
        {
            if (!ScenarioEditorFlow.IsRunning) return;
            var field = AccessTools.Field(typeof(HospitalEditEvents), "_level");
            if (!ScenarioEditorTerrainPainter.Ready)
                ScenarioEditorTerrainPainter.Initialize(field?.GetValue(__instance) as Level);
        }
    }

    [HarmonyPatch(typeof(HospitalEditEvents), "EndEdit")]
    internal static class ScenarioTerrainPainterEndPatch
    {
        private static void Prefix()
        {
            ScenarioEditorTerrainPainter.Cleanup();
        }
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioTerrainPainterMenuPatch
    {
        private static void Postfix(EditHospitalMenu __instance)
        {
            if (!ScenarioEditorFlow.IsRunning || !ScenarioEditorSession.Active) return;
            if (!ScenarioEditorTerrainPainter.Ready)
                ScenarioEditorTerrainPainter.Initialize(ScenarioEditorSession.Level);
            ScenarioEditorTerrainPainter.EnsureUi(__instance);
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "Save")]
    internal static class ScenarioTerrainPainterSavePatch
    {
        private static void Postfix(Level level, string displayName, string __result)
        {
            try
            {
                ScenarioEditorTerrainPainter.ExportSidecar(level, __result);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("Terrain painter: export post-save fallo: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorStorage), "Load")]
    internal static class ScenarioTerrainPainterLoadPatch
    {
        private static void Postfix(Level level, string path)
        {
            try
            {
                ScenarioEditorTerrainPainter.ImportSidecar(level, path);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("Terrain painter: import post-load fallo: " + exception);
            }
        }
    }
}
// ===== END ORIGINAL: ScenarioEditorTerrainPainter.cs =====
