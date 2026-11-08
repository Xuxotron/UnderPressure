// Updated: 2026-11-04 - Manual superior en una sola linea; seleccion separada debajo; sin negritas.
// Ctrl+Alt+F12 activa/desactiva. Clic selecciona, arrastrar mueve y 8 tiradores redimensionan.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// Editor visual de RectTransform en tiempo de ejecución.
    ///
    /// Controles:
    /// - Ctrl + Alt + F12: activar/desactivar.
    /// - Clic izquierdo: seleccionar un elemento UI.
    /// - Arrastrar dentro de la selección: mover.
    /// - Arrastrar uno de los 8 tiradores: redimensionar.
    /// - Clic derecho: seleccionar el RectTransform padre.
    /// - Ctrl + C: copiar el elemento seleccionado a un portapapeles runtime persistente.
    /// - Ctrl + V: pegarlo como clon en el menú actual.
    /// - Ctrl + S: guardar layouts, clones, textos y colores en un fichero externo.
    /// - ENTER: abrir/cerrar el editor de propiedades del elemento seleccionado.
    /// - ENTER permite editar color, texto, tamaño/autoajuste de fuente y contorno.
    /// - El contorno puede activarse/desactivarse en cualquier Graphic seleccionado.
    /// - Probeta: copiar el color exacto de otro elemento UI.
    /// - Mantener Espacio: dejar pasar temporalmente los clics al juego para navegar.
    /// - Mayus al mover: restringir el movimiento al eje dominante.
    /// - Mayus al redimensionar: mantener la proporcion.
    /// - Flechas: mover la seleccion 1 px; Mayus + flechas: 5 px.
    /// - RePag/Page Up: subir un nivel visual; AvPag/Page Down: bajar un nivel visual.
    /// - Alt + clic sobre otro RectTransform: convertirlo en el nuevo padre.
    /// - Alt + arrastrar: cambiar de padre al soltar sobre otro RectTransform.
    /// - Ctrl + Z: deshacer mover, redimensionar, pegar o eliminar.
    /// - Supr/Delete: eliminar el elemento seleccionado.
    /// - Alt + Supr/Delete: eliminar solo el nodo seleccionado y subir sus hijos al padre.
    /// - Los overrides y clones guardados se reaplican al inicio/cambio de escena sin barrido continuo.
    /// - Escape con el diseñador activo: cancelar/deseleccionar; nunca llega al juego.
    ///
    /// No modifica prefabs ni assets originales del juego. Los cambios se guardan
    /// como overrides/recetas de clonación de UnderPressure y pueden revertirse
    /// eliminando el fichero de layouts.
    /// </summary>
    public sealed class RuntimeUiDesignerPlugin : MonoBehaviour
    {
        private const float HandleSize = 14f;
        private const float BorderThickness = 2f;
        private const float MinimumWidth = 12f;
        private const float MinimumHeight = 12f;
        private const float AutoApplyInterval = 0.35f;
        private const int AutoApplyPassesPerScene = 6;
        private const string LayoutFolderName = "UnderPressure";
        private const string LayoutFileName = "ui_layout_overrides.txt";

        private static readonly Color BorderColor =
            new Color(0.15f, 1f, 0.25f, 1f);

        private static readonly Color HandleColor =
            new Color(1f, 1f, 1f, 1f);

        private static readonly Color HandleBorderColor =
            new Color(0.05f, 0.05f, 0.05f, 1f);

        private static readonly Color HudBackgroundColor =
            new Color(0f, 0f, 0f, 0.78f);

        private static readonly Color HudTextColor =
            new Color(1f, 1f, 1f, 1f);

        private static RuntimeUiDesignerPlugin _activeInstance;
        private static bool _designerEscapeRead;
        private static bool _designerEnterRead;

        private bool _designerEnabled;
        private bool _temporaryPassThrough;
        private RectTransform _selected;
        private DragMode _dragMode;

        private GameObject _clipboardRoot;
        private GameObject _clipboardTemplate;
        private string _clipboardSourcePath;
        private string _clipboardOriginalName;
        private bool _clipboardOriginalActive;

        private readonly HashSet<RectTransform> _dirtyRects =
            new HashSet<RectTransform>();

        private readonly HashSet<int> _runtimeCloneInstanceIds =
            new HashSet<int>();

        private readonly Dictionary<int, string> _runtimeCloneOwnerByInstanceId =
            new Dictionary<int, string>();

        private readonly Dictionary<string, RectTransform> _runtimeCloneRoots =
            new Dictionary<string, RectTransform>(StringComparer.Ordinal);

        private readonly Dictionary<string, GameObject> _sourceTemplateCache =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private readonly Dictionary<string, RectLayoutRecord> _savedLayouts =
            new Dictionary<string, RectLayoutRecord>(StringComparer.Ordinal);

        private readonly Dictionary<RectTransform, string> _persistentOriginalPaths =
            new Dictionary<RectTransform, string>();

        private readonly Dictionary<string, CloneRecord> _savedClones =
            new Dictionary<string, CloneRecord>(StringComparer.Ordinal);

        private readonly HashSet<string> _savedDeletedPaths =
            new HashSet<string>(StringComparer.Ordinal);

        private readonly Stack<UndoRecord> _undoStack =
            new Stack<UndoRecord>();

        private RectLayoutRecord _dragUndoLayout;

        private float _nextAutoApplyTime;
        private int _autoApplyPassesRemaining;
        private bool _pageUpWasHeld;
        private bool _pageDownWasHeld;
        private string _layoutFilePath;

        private Vector2 _dragStartParentPoint;
        private Vector2 _dragStartAnchoredPosition;
        private Vector2 _dragStartOffsetMin;
        private Vector2 _dragStartOffsetMax;
        private float _dragStartWidth;
        private float _dragStartHeight;
        private RectTransform _dragStartParent;
        private int _dragStartSiblingIndex;

        private EventSystem _disabledEventSystem;
        private bool _disabledEventSystemWasEnabled;

        private bool _cursorWasVisible;
        private CursorLockMode _cursorWasLocked;

        private GUIStyle _hudStyle;
        private GUIStyle _selectedNameStyle;

        private bool _propertyEditorOpen;
        private bool _propertyEyedropperActive;
        private bool _propertyFocusTextNextGui;
        private RectTransform _propertyTarget;
        private RectLayoutRecord _propertyUndoLayout;
        private bool _propertyHasColor;
        private Color _propertyOriginalColor;
        private Color _propertyDraftColor;
        private bool _propertyHasText;
        private string _propertyOriginalText = string.Empty;
        private string _propertyDraftText = string.Empty;

        private bool _propertyHasTextStyle;
        private float _propertyOriginalFontSize;
        private float _propertyDraftFontSize;
        private bool _propertyOriginalAutoSize;
        private bool _propertyDraftAutoSize;
        private float _propertyOriginalFontSizeMin;
        private float _propertyDraftFontSizeMin;
        private float _propertyOriginalFontSizeMax;
        private float _propertyDraftFontSizeMax;
        private string _propertyFontSizeText = string.Empty;
        private string _propertyFontMinText = string.Empty;
        private string _propertyFontMaxText = string.Empty;

        private bool _propertyCanOutline;
        private bool _propertyOriginalHadOutline;
        private bool _propertyOriginalOutlineEnabled;
        private bool _propertyDraftOutlineEnabled;
        private Color _propertyOriginalOutlineColor = Color.black;
        private Color _propertyDraftOutlineColor = Color.black;
        private Vector2 _propertyOriginalOutlineDistance = new Vector2(1f, -1f);
        private Vector2 _propertyDraftOutlineDistance = new Vector2(1f, -1f);
        private bool _propertyOriginalOutlineUseGraphicAlpha = true;
        private bool _propertyDraftOutlineUseGraphicAlpha = true;
        private string _propertyOutlineWidthText = string.Empty;
        private bool _propertyEditingOutlineColor;

        private string _propertyHex = string.Empty;
        private float _propertyHue;
        private float _propertySaturation;
        private float _propertyValue;
        private Texture2D _propertyHueTexture;
        private Texture2D _propertySvTexture;
        private float _propertySvTextureHue = -1f;

        [Serializable]
        private sealed class RectLayoutRecord
        {
            public string path;
            public string parentPath;
            public int siblingIndex = -1;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 anchoredPosition;
            public Vector2 sizeDelta;

            public bool componentMetadataKnown;

            public bool hasLayoutElement;
            public float layoutMinWidth = -1f;
            public float layoutMinHeight = -1f;
            public float layoutPreferredWidth = -1f;
            public float layoutPreferredHeight = -1f;
            public float layoutFlexibleWidth = -1f;
            public float layoutFlexibleHeight = -1f;
            public bool layoutIgnore;

            public bool hasContentSizeFitter;
            public int contentHorizontalFit;
            public int contentVerticalFit;

            public bool hasGraphicColor;
            public Color graphicColor = Color.white;
            public bool hasTextOverride;
            public string textValue = string.Empty;

            public bool extendedAppearanceKnown;
            public bool hasOutline;
            public bool outlineEnabled;
            public Color outlineColor = Color.black;
            public Vector2 outlineDistance = new Vector2(1f, -1f);
            public bool outlineUseGraphicAlpha = true;
            public bool hasTextStyle;
            public float textFontSize = -1f;
            public bool textAutoSize;
            public float textFontSizeMin = -1f;
            public float textFontSizeMax = -1f;
        }

        [Serializable]
        private sealed class CloneRecord
        {
            public string id;
            public string sourcePath;
            public string targetParentPath;
            public string name;
            public int siblingIndex;
            public bool activeSelf;

            public List<RelativeRectLayoutRecord> rects =
                new List<RelativeRectLayoutRecord>();
        }

        [Serializable]
        private sealed class RelativeRectLayoutRecord
        {
            public string relativePath;
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 anchoredPosition;
            public Vector2 sizeDelta;
            public bool activeSelf;

            public bool hasGraphicColor;
            public Color graphicColor = Color.white;
            public bool hasTextOverride;
            public string textValue = string.Empty;

            public bool extendedAppearanceKnown;
            public bool hasOutline;
            public bool outlineEnabled;
            public Color outlineColor = Color.black;
            public Vector2 outlineDistance = new Vector2(1f, -1f);
            public bool outlineUseGraphicAlpha = true;
            public bool hasTextStyle;
            public float textFontSize = -1f;
            public bool textAutoSize;
            public float textFontSizeMin = -1f;
            public float textFontSizeMax = -1f;
        }

        private enum UndoKind
        {
            Transform,
            Hierarchy,
            PasteClone,
            DeleteOriginal,
            DeleteCloneChild,
            DeleteCloneRoot,
            DeleteOriginalPreserveChildren
        }

        private sealed class PromotedChildUndo
        {
            public RectTransform target;
            public RectLayoutRecord layout;
            public int siblingIndex;
        }

        private sealed class UndoRecord
        {
            public UndoKind kind;
            public RectTransform target;
            public RectLayoutRecord layout;
            public RectTransform hierarchyParent;
            public int siblingIndex;
            public string path;
            public string cloneId;
            public CloneRecord cloneRecord;
            public string objectName;
            public List<PromotedChildUndo> promotedChildren;
        }

        private enum DragMode
        {
            None,
            Move,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private struct Candidate
        {
            internal RectTransform Rect;
            internal int SortingOrder;
            internal int Depth;
        }

        private void Awake()
        {
            _activeInstance = this;
            DontDestroyOnLoad(gameObject);

            _clipboardRoot =
                new GameObject("UP UI Designer Clipboard");

            DontDestroyOnLoad(_clipboardRoot);
            _clipboardRoot.SetActive(false);

            _layoutFilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Mods",
                LayoutFolderName,
                LayoutFileName);

            LoadSavedLayouts();

            SceneManager.sceneLoaded +=
                OnSceneLoaded;

            ScheduleAutoApply();
        }

        private void OnSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            ScheduleAutoApply();
        }

        private void ScheduleAutoApply()
        {
            _autoApplyPassesRemaining =
                AutoApplyPassesPerScene;

            _nextAutoApplyTime =
                Time.unscaledTime + 0.15f;
        }

        public static void ReapplyPersistentStructureNow()
        {
            var instance = _activeInstance;

            if (instance == null ||
                instance._designerEnabled)
                return;

            instance.ReapplyPersistentStructure(null);
        }

        // Variante acotada para UI que se crea/reconstruye en caliente.
        // No busca RectTransform en todo el juego: resuelve unicamente las
        // rutas persistentes conocidas dentro del arbol indicado.
        public static void ReapplyPersistentStructureNow(
            Transform scopeRoot)
        {
            var instance = _activeInstance;

            if (instance == null ||
                scopeRoot == null)
                return;

            instance.ReapplyPersistentStructure(scopeRoot);
        }

        private void ReapplyPersistentStructure(
            Transform scopeRoot)
        {
            if (_savedDeletedPaths.Count == 0 &&
                _savedLayouts.Count == 0 &&
                _savedClones.Count == 0)
                return;

            var rectsByPath =
                BuildPersistenceRectMap(scopeRoot);

            if (rectsByPath.Count == 0)
                return;

            foreach (var path in _savedDeletedPaths)
            {
                RectTransform rect;

                if (rectsByPath.TryGetValue(path, out rect) &&
                    rect != null &&
                    rect.gameObject.activeSelf)
                {
                    rect.gameObject.SetActive(false);
                }
            }

            foreach (var pair in _savedLayouts)
            {
                var record = pair.Value;

                if (record == null ||
                    string.IsNullOrEmpty(record.path) ||
                    string.IsNullOrEmpty(record.parentPath))
                    continue;

                RectTransform rect;
                RectTransform targetParent;

                if (!rectsByPath.TryGetValue(
                        record.path,
                        out rect) ||
                    rect == null ||
                    !rectsByPath.TryGetValue(
                        record.parentPath,
                        out targetParent) ||
                    targetParent == null ||
                    targetParent == rect ||
                    targetParent.IsChildOf(rect))
                    continue;

                if (rect.parent != targetParent)
                {
                    rect.SetParent(targetParent, false);
                    ApplyStructuralRectLayout(
                        rect, record);
                }

                if (record.siblingIndex >= 0 &&
                    rect.parent != null)
                {
                    rect.SetSiblingIndex(
                        Mathf.Clamp(
                            record.siblingIndex,
                            0,
                            rect.parent.childCount - 1));
                }
            }
        }

        private static void ApplyStructuralRectLayout(
            RectTransform rect,
            RectLayoutRecord record)
        {
            if (rect == null || record == null)
                return;

            rect.anchorMin = record.anchorMin;
            rect.anchorMax = record.anchorMax;
            rect.pivot = record.pivot;
            rect.anchoredPosition =
                record.anchoredPosition;
            rect.sizeDelta = record.sizeDelta;

            var parentRect =
                rect.parent as RectTransform;

            if (parentRect != null)
                LayoutRebuilder.MarkLayoutForRebuild(
                    parentRect);
        }

        private Dictionary<string, RectTransform>
            BuildPersistenceRectMap(
                Transform scopeRoot)
        {
            var result =
                new Dictionary<string, RectTransform>(
                    StringComparer.Ordinal);

            foreach (var path in _savedDeletedPaths)
                AddResolvedPersistencePath(
                    result, path, scopeRoot);

            foreach (var pair in _savedLayouts)
            {
                var record = pair.Value;
                if (record == null) continue;

                AddResolvedPersistencePath(
                    result, record.path, scopeRoot);
                AddResolvedPersistencePath(
                    result, record.parentPath, scopeRoot);
            }

            foreach (var pair in _savedClones)
            {
                var record = pair.Value;
                if (record == null) continue;

                AddResolvedPersistencePath(
                    result, record.sourcePath, scopeRoot);
                AddResolvedPersistencePath(
                    result, record.targetParentPath, scopeRoot);
            }

            return result;
        }

        private void AddResolvedPersistencePath(
            Dictionary<string, RectTransform> result,
            string path,
            Transform scopeRoot)
        {
            if (result == null ||
                string.IsNullOrEmpty(path) ||
                result.ContainsKey(path))
                return;

            RectTransform rect;

            if (!TryGetCachedPersistentRect(
                    path, scopeRoot, out rect) &&
                !TryResolveStablePath(
                    path, scopeRoot, out rect))
                return;

            if (rect == null)
                return;

            result[path] = rect;

            if (!_persistentOriginalPaths.ContainsKey(rect))
                _persistentOriginalPaths[rect] = path;
        }

        private bool TryGetCachedPersistentRect(
            string path,
            Transform scopeRoot,
            out RectTransform rect)
        {
            rect = null;

            foreach (var pair in _persistentOriginalPaths)
            {
                var candidate = pair.Key;

                if (candidate == null ||
                    !string.Equals(
                        pair.Value,
                        path,
                        StringComparison.Ordinal))
                    continue;

                if (scopeRoot != null &&
                    candidate != scopeRoot &&
                    !candidate.IsChildOf(scopeRoot) &&
                    !scopeRoot.IsChildOf(candidate))
                    continue;

                rect = candidate;
                return true;
            }

            return false;
        }

        private static bool TryResolveStablePath(
            string stablePath,
            Transform scopeRoot,
            out RectTransform rect)
        {
            rect = null;

            if (string.IsNullOrEmpty(stablePath))
                return false;

            if (scopeRoot != null)
            {
                var scopePath =
                    BuildStableTransformPath(scopeRoot);

                if (string.Equals(
                        stablePath,
                        scopePath,
                        StringComparison.Ordinal))
                {
                    rect = scopeRoot as RectTransform;
                    return rect != null;
                }

                var prefix = scopePath + "/";

                if (stablePath.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                {
                    return TryResolveRelativeStablePath(
                        scopeRoot,
                        stablePath.Substring(prefix.Length),
                        out rect);
                }

                // Permite resolver un padre del scope sin recorrer la escena.
                for (var current = scopeRoot.parent;
                     current != null;
                     current = current.parent)
                {
                    if (!string.Equals(
                            BuildStableTransformPath(current),
                            stablePath,
                            StringComparison.Ordinal))
                        continue;

                    rect = current as RectTransform;
                    return rect != null;
                }

                return false;
            }

            var separator = stablePath.IndexOf('|');

            if (separator <= 0 ||
                separator >= stablePath.Length - 1)
                return false;

            var sceneName =
                stablePath.Substring(0, separator);
            var hierarchyPath =
                stablePath.Substring(separator + 1);
            var segments =
                hierarchyPath.Split('/');

            if (segments.Length == 0)
                return false;

            var scene =
                SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid() ||
                !scene.isLoaded)
                return false;

            var roots = scene.GetRootGameObjects();
            var currentRoot =
                FindRootByPathSegment(
                    roots, segments[0]);

            if (currentRoot == null)
                return false;

            Transform currentTransform =
                currentRoot.transform;

            for (var index = 1;
                 index < segments.Length;
                 ++index)
            {
                currentTransform =
                    FindChildByPathSegment(
                        currentTransform,
                        segments[index]);

                if (currentTransform == null)
                    return false;
            }

            rect = currentTransform as RectTransform;
            return rect != null;
        }

        private static bool TryResolveRelativeStablePath(
            Transform root,
            string relativePath,
            out RectTransform rect)
        {
            rect = null;

            if (root == null)
                return false;

            if (string.IsNullOrEmpty(relativePath))
            {
                rect = root as RectTransform;
                return rect != null;
            }

            var segments = relativePath.Split('/');
            Transform current = root;

            for (var index = 0;
                 index < segments.Length;
                 ++index)
            {
                current =
                    FindChildByPathSegment(
                        current,
                        segments[index]);

                if (current == null)
                    return false;
            }

            rect = current as RectTransform;
            return rect != null;
        }

        private static GameObject FindRootByPathSegment(
            GameObject[] roots,
            string segment)
        {
            if (roots == null)
                return null;

            string name;
            int ordinal;

            if (!TryParsePathSegment(
                    segment, out name, out ordinal))
                return null;

            var match = 0;

            for (var index = 0;
                 index < roots.Length;
                 ++index)
            {
                var root = roots[index];

                if (root == null ||
                    !string.Equals(
                        root.name,
                        name,
                        StringComparison.Ordinal))
                    continue;

                if (match == ordinal)
                    return root;

                ++match;
            }

            return null;
        }

        private static Transform FindChildByPathSegment(
            Transform parent,
            string segment)
        {
            if (parent == null)
                return null;

            string name;
            int ordinal;

            if (!TryParsePathSegment(
                    segment, out name, out ordinal))
                return null;

            var match = 0;

            for (var index = 0;
                 index < parent.childCount;
                 ++index)
            {
                var child = parent.GetChild(index);

                if (child == null ||
                    !string.Equals(
                        child.name,
                        name,
                        StringComparison.Ordinal))
                    continue;

                if (match == ordinal)
                    return child;

                ++match;
            }

            return null;
        }

        private static bool TryParsePathSegment(
            string segment,
            out string name,
            out int ordinal)
        {
            name = string.Empty;
            ordinal = 0;

            if (string.IsNullOrEmpty(segment))
                return false;

            var separator = segment.LastIndexOf('#');

            if (separator <= 0 ||
                separator >= segment.Length - 1 ||
                !int.TryParse(
                    segment.Substring(separator + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ordinal) ||
                ordinal < 0)
                return false;

            name = UnescapePathSegment(
                segment.Substring(0, separator));

            return true;
        }

        private static string UnescapePathSegment(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value == "<unnamed>")
                return string.Empty;

            return value
                .Replace("%23", "#")
                .Replace("%7C", "|")
                .Replace("%2F", "/")
                .Replace("%25", "%");
        }

        private void Update()
        {
            if (ToggleShortcutPressed())
            {
                SetDesignerEnabled(!_designerEnabled);
                return;
            }

            if (!_designerEnabled)
            {
                if (_autoApplyPassesRemaining > 0)
                    TryAutoApplySavedLayouts();

                return;
            }

            ValidateSelection();

            if (_propertyEditorOpen &&
                _propertyTarget == null)
            {
                ClosePropertyEditorState();
            }

            if (_propertyEditorOpen)
            {
                SetTemporaryPassThrough(false);
                EnsureCurrentEventSystemDisabled();

                if (DesignerEscapePressed())
                {
                    CancelPropertyEditor();
                    return;
                }

                if (DesignerEnterPressed())
                {
                    CommitPropertyEditor();
                    return;
                }

                if (_propertyEyedropperActive &&
                    Input.GetMouseButtonDown(0) &&
                    !IsMouseOverPropertyEditor())
                {
                    SamplePropertyColorAtPointer(
                        (Vector2)Input.mousePosition);
                }

                return;
            }

            if (DesignerEnterPressed())
            {
                SetTemporaryPassThrough(false);
                EnsureCurrentEventSystemDisabled();
                OpenPropertyEditor();
                return;
            }

            if (ClipboardShortcutPressed(KeyCode.C))
            {
                SetTemporaryPassThrough(false);
                CopySelected();
                return;
            }

            if (ClipboardShortcutPressed(KeyCode.V))
            {
                SetTemporaryPassThrough(false);
                PasteCopied();
                return;
            }

            if (ClipboardShortcutPressed(KeyCode.S))
            {
                SetTemporaryPassThrough(false);
                SaveDirtyLayouts();
                return;
            }

            if (ClipboardShortcutPressed(KeyCode.Z))
            {
                SetTemporaryPassThrough(false);
                UndoLastAction();
                return;
            }

            if (DesignerEscapePressed())
            {
                SetTemporaryPassThrough(false);

                if (_dragMode != DragMode.None)
                {
                    RestoreDragStartValues();
                    _dragUndoLayout = null;
                    _dragMode = DragMode.None;
                }

                _selected = null;
                return;
            }

            if (_dragMode != DragMode.None)
            {
                SetTemporaryPassThrough(false);
                UpdateActiveDrag();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                SetTemporaryPassThrough(false);

                if (AltHeld())
                    DeleteSelectedPreservingChildren();
                else
                    DeleteSelected();

                return;
            }

            if (SpaceHeld())
            {
                SetTemporaryPassThrough(true);
                return;
            }

            SetTemporaryPassThrough(false);
            EnsureCurrentEventSystemDisabled();

            if (TryNudgeSelected())
                return;

            if (TryChangeSiblingOrder())
                return;

            if (Input.GetMouseButtonDown(1))
            {
                SelectParent();
                return;
            }

            var mousePosition =
                (Vector2)Input.mousePosition;

            if (AltHeld() &&
                Input.GetMouseButtonDown(0) &&
                _selected != null)
            {
                if (TryReparentSelectedAtPointer(
                        mousePosition,
                        false))
                    return;
            }

            if (!Input.GetMouseButtonDown(0))
                return;

            if (_selected != null)
            {
                var handle = HitTestHandle(mousePosition);
                if (handle != DragMode.None)
                {
                    BeginDrag(handle, mousePosition);
                    return;
                }

                if (ContainsScreenPoint(_selected, mousePosition))
                {
                    BeginDrag(DragMode.Move, mousePosition);
                    return;
                }
            }

            var picked = PickTopmostRectTransform(mousePosition);
            _selected = picked;

            if (_selected != null)
                BeginDrag(DragMode.Move, mousePosition);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -=
                OnSceneLoaded;

            if (_designerEnabled)
                SetDesignerEnabled(false);

            DestroyPropertyEditorTextures();

            if (_activeInstance == this)
                _activeInstance = null;
        }

        private void OnApplicationQuit()
        {
            if (_designerEnabled)
                RestoreInputState();
        }

        private static bool ToggleShortcutPressed()
        {
            var control =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);

            var alt =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);

            return control && alt && Input.GetKeyDown(KeyCode.F12);
        }

        private static bool ClipboardShortcutPressed(
            KeyCode key)
        {
            var control =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);

            var alt =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);

            return control &&
                   !alt &&
                   Input.GetKeyDown(key);
        }

        private static bool ShiftHeld()
        {
            return
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
        }

        private static bool SpaceHeld()
        {
            return Input.GetKey(KeyCode.Space);
        }

        private static bool AltHeld()
        {
            return
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);
        }

        internal static bool BlockGameInput
        {
            get
            {
                return
                    _activeInstance != null &&
                    _activeInstance._designerEnabled;
            }
        }

        internal static bool BlockEscapeForGame
        {
            get
            {
                return
                    _activeInstance != null &&
                    _activeInstance._designerEnabled &&
                    !_designerEscapeRead;
            }
        }

        private static bool DesignerEscapePressed()
        {
            _designerEscapeRead = true;

            try
            {
                return Input.GetKeyDown(
                    KeyCode.Escape);
            }
            finally
            {
                _designerEscapeRead = false;
            }
        }

        internal static bool BlockEnterForGame
        {
            get
            {
                return
                    _activeInstance != null &&
                    _activeInstance._designerEnabled &&
                    !_designerEnterRead;
            }
        }

        private static bool DesignerEnterPressed()
        {
            _designerEnterRead = true;

            try
            {
                return
                    Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter);
            }
            finally
            {
                _designerEnterRead = false;
            }
        }

        private void SetDesignerEnabled(bool enabled)
        {
            if (_designerEnabled == enabled)
                return;

            _designerEnabled = enabled;
            _temporaryPassThrough = false;
            _dragMode = DragMode.None;
            _dragUndoLayout = null;

            if (_designerEnabled)
            {
                _pageUpWasHeld = false;
                _pageDownWasHeld = false;
                CaptureAndOverrideInputState();
            }
            else
            {
                if (_propertyEditorOpen)
                    CancelPropertyEditor();

                _selected = null;
                RestoreInputState();
            }
        }

        private void CaptureAndOverrideInputState()
        {
            _cursorWasVisible = Cursor.visible;
            _cursorWasLocked = Cursor.lockState;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _disabledEventSystem = EventSystem.current;

            if (_disabledEventSystem != null)
            {
                _disabledEventSystemWasEnabled =
                    _disabledEventSystem.enabled;

                if (_disabledEventSystemWasEnabled)
                    _disabledEventSystem.enabled = false;
            }
        }

        private void RestoreInputState()
        {
            if (_disabledEventSystem != null)
            {
                _disabledEventSystem.enabled =
                    _disabledEventSystemWasEnabled;

                _disabledEventSystem = null;
            }

            Cursor.visible = _cursorWasVisible;
            Cursor.lockState = _cursorWasLocked;
        }

        private void EnsureCurrentEventSystemDisabled()
        {
            var current = EventSystem.current;

            if (current == null)
                return;

            if (_disabledEventSystem == current)
            {
                if (current.enabled)
                    current.enabled = false;

                return;
            }

            if (_disabledEventSystem != null)
            {
                _disabledEventSystem.enabled =
                    _disabledEventSystemWasEnabled;
            }

            _disabledEventSystem = current;
            _disabledEventSystemWasEnabled = current.enabled;

            if (current.enabled)
                current.enabled = false;
        }

        private void SetTemporaryPassThrough(bool enabled)
        {
            if (_temporaryPassThrough == enabled)
                return;

            _temporaryPassThrough = enabled;

            if (_temporaryPassThrough)
            {
                var current = EventSystem.current;

                if (current != null &&
                    current != _disabledEventSystem)
                {
                    if (_disabledEventSystem != null)
                    {
                        _disabledEventSystem.enabled =
                            _disabledEventSystemWasEnabled;
                    }

                    _disabledEventSystem = current;
                    _disabledEventSystemWasEnabled =
                        current.enabled;
                }

                if (_disabledEventSystem != null)
                {
                    _disabledEventSystem.enabled =
                        _disabledEventSystemWasEnabled;
                }

                return;
            }

            EnsureCurrentEventSystemDisabled();
        }

        private void ValidateSelection()
        {
            if (_selected == null)
            {
                _selected = null;
                _dragMode = DragMode.None;
                return;
            }

            if (!_selected.gameObject.activeInHierarchy)
            {
                _selected = null;
                _dragMode = DragMode.None;
            }
        }

        private void CopySelected()
        {
            if (_selected == null)
                return;

            if (_clipboardTemplate != null)
            {
                Destroy(_clipboardTemplate);
                _clipboardTemplate = null;
            }

            _clipboardSourcePath =
                ResolvePersistentSourcePath(_selected);

            _clipboardOriginalName =
                _selected.gameObject.name;

            _clipboardOriginalActive =
                _selected.gameObject.activeSelf;

            _clipboardTemplate =
                Instantiate(
                    _selected.gameObject,
                    _clipboardRoot.transform,
                    false);

            _clipboardTemplate.name =
                "UP Clipboard Template - " +
                _clipboardOriginalName;

            _clipboardTemplate.SetActive(false);

            if (!string.IsNullOrEmpty(_clipboardSourcePath))
            {
                CacheSourceTemplate(
                    _clipboardSourcePath,
                    _selected);
            }
        }

        private void PasteCopied()
        {
            if (_clipboardTemplate == null)
                return;

            var targetSibling =
                ResolvePasteTarget();

            if (targetSibling == null)
                return;

            var targetParent =
                targetSibling.parent as RectTransform;

            if (targetParent == null)
                return;

            var cloneObject =
                Instantiate(
                    _clipboardTemplate,
                    targetParent,
                    false);

            cloneObject.name =
                BuildUniqueCloneName(
                    _clipboardOriginalName,
                    targetParent);

            cloneObject.SetActive(
                _clipboardOriginalActive);

            var cloneRect =
                cloneObject.transform as RectTransform;

            if (cloneRect == null)
            {
                Destroy(cloneObject);
                return;
            }

            cloneRect.SetSiblingIndex(
                Mathf.Min(
                    targetSibling.GetSiblingIndex() + 1,
                    targetParent.childCount - 1));

            MoveRectPivotToScreenPoint(
                cloneRect,
                targetParent,
                Input.mousePosition);

            var cloneId =
                Guid.NewGuid().ToString("N");

            var record = new CloneRecord
            {
                id = cloneId,
                sourcePath = _clipboardSourcePath,
                targetParentPath =
                    ResolvePersistentTargetPath(targetParent),
                name = cloneObject.name,
                siblingIndex = cloneRect.GetSiblingIndex(),
                activeSelf = cloneObject.activeSelf,
                rects = CaptureCloneLayouts(cloneRect)
            };

            _savedClones[cloneId] = record;

            RegisterRuntimeClone(
                cloneId,
                cloneRect);

            _selected = cloneRect;

            PushUndo(
                new UndoRecord
                {
                    kind = UndoKind.PasteClone,
                    target = cloneRect,
                    cloneId = cloneId
                });
        }

        private RectTransform ResolvePasteTarget()
        {
            if (_selected != null &&
                _selected.gameObject.activeInHierarchy)
                return _selected;

            return PickTopmostRectTransform(
                Input.mousePosition);
        }

        private static void MoveRectPivotToScreenPoint(
            RectTransform rect,
            RectTransform parent,
            Vector2 screenPoint)
        {
            if (rect == null || parent == null)
                return;

            var canvas =
                parent.GetComponentInParent<Canvas>();

            Vector2 localPoint;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent,
                    screenPoint,
                    GetCanvasCamera(canvas),
                    out localPoint))
                return;

            rect.localPosition =
                new Vector3(
                    localPoint.x,
                    localPoint.y,
                    rect.localPosition.z);
        }

        private void DeleteSelected()
        {
            if (_selected == null)
                return;

            var target = _selected;
            string cloneId;

            if (_runtimeCloneOwnerByInstanceId.TryGetValue(
                    target.gameObject.GetInstanceID(),
                    out cloneId))
            {
                RectTransform cloneRoot;

                if (_runtimeCloneRoots.TryGetValue(
                        cloneId,
                        out cloneRoot) &&
                    cloneRoot != null &&
                    cloneRoot == target)
                {
                    CloneRecord cloneRecord;

                    if (_savedClones.TryGetValue(
                            cloneId,
                            out cloneRecord))
                    {
                        cloneRecord.name =
                            cloneRoot.gameObject.name;
                        cloneRecord.siblingIndex =
                            cloneRoot.GetSiblingIndex();
                        cloneRecord.activeSelf =
                            cloneRoot.gameObject.activeSelf;
                        cloneRecord.rects =
                            CaptureCloneLayouts(cloneRoot);
                    }

                    PushUndo(
                        new UndoRecord
                        {
                            kind = UndoKind.DeleteCloneRoot,
                            target = cloneRoot,
                            cloneId = cloneId,
                            cloneRecord = cloneRecord,
                            objectName =
                                cloneRoot.gameObject.name
                        });

                    _savedClones.Remove(cloneId);
                    cloneRoot.gameObject.name =
                        "UP Deleted Clone " + cloneId;
                    cloneRoot.gameObject.SetActive(false);
                    _selected = null;
                    return;
                }

                PushUndo(
                    new UndoRecord
                    {
                        kind = UndoKind.DeleteCloneChild,
                        target = target,
                        cloneId = cloneId
                    });

                target.gameObject.SetActive(false);
                _selected = null;
                return;
            }

            var path =
                GetPersistentOriginalPath(
                    target);

            if (string.IsNullOrEmpty(path))
                return;

            PushUndo(
                new UndoRecord
                {
                    kind = UndoKind.DeleteOriginal,
                    target = target,
                    path = path
                });

            _savedDeletedPaths.Add(path);
            target.gameObject.SetActive(false);
            _selected = null;
        }

        private void DeleteSelectedPreservingChildren()
        {
            if (_selected == null)
                return;

            // La persistencia por parentPath existe para elementos originales.
            // En clones se conserva el comportamiento de borrado ya existente
            // para no generar una jerarquia que el formato de clon no representa.
            if (IsRuntimeCloneOrChild(_selected))
            {
                DeleteSelected();
                return;
            }

            var target = _selected;
            var targetParent =
                target.parent as RectTransform;

            if (targetParent == null)
            {
                DeleteSelected();
                return;
            }

            var targetPath =
                GetPersistentOriginalPath(target);

            if (string.IsNullOrEmpty(targetPath))
                return;

            GetPersistentOriginalPath(targetParent);

            var promoted =
                new List<PromotedChildUndo>();

            for (var index = 0;
                 index < target.childCount;
                 ++index)
            {
                var child =
                    target.GetChild(index) as RectTransform;

                if (child == null)
                    continue;

                var childPath =
                    string.Empty;

                if (IsRuntimeCloneOrChild(child))
                {
                    string childCloneId;
                    RectTransform childCloneRoot;

                    // Un clon raiz si puede subir de nivel: su CloneRecord ya
                    // guarda targetParentPath. Los hijos internos de un clon no
                    // se separan de su raiz porque su formato es relativo.
                    if (!_runtimeCloneOwnerByInstanceId.TryGetValue(
                            child.gameObject.GetInstanceID(),
                            out childCloneId) ||
                        !_runtimeCloneRoots.TryGetValue(
                            childCloneId,
                            out childCloneRoot) ||
                        childCloneRoot != child)
                        continue;
                }
                else
                {
                    childPath =
                        GetPersistentOriginalPath(child);

                    if (string.IsNullOrEmpty(childPath))
                        continue;
                }

                promoted.Add(
                    new PromotedChildUndo
                    {
                        target = child,
                        layout =
                            CaptureLayout(
                                child,
                                childPath),
                        siblingIndex =
                            child.GetSiblingIndex()
                    });
            }

            if (promoted.Count == 0)
            {
                DeleteSelected();
                return;
            }

            PushUndo(
                new UndoRecord
                {
                    kind =
                        UndoKind
                            .DeleteOriginalPreserveChildren,
                    target = target,
                    path = targetPath,
                    promotedChildren = promoted
                });

            var insertionIndex =
                target.GetSiblingIndex();

            for (var index = 0;
                 index < promoted.Count;
                 ++index)
            {
                var child =
                    promoted[index].target;

                if (child == null)
                    continue;

                child.SetParent(
                    targetParent,
                    true);

                child.SetSiblingIndex(
                    Mathf.Clamp(
                        insertionIndex + index,
                        0,
                        targetParent.childCount - 1));

                UpdateHierarchyPersistence(
                    child);
            }

            _savedDeletedPaths.Add(
                targetPath);

            target.gameObject.SetActive(false);
            _selected = null;
        }

        private void UndoLastAction()
        {
            while (_undoStack.Count > 0)
            {
                var undo = _undoStack.Pop();

                if (undo == null)
                    continue;

                if (undo.kind == UndoKind.Transform)
                {
                    if (undo.target == null ||
                        undo.layout == null)
                        continue;

                    ApplyLayout(
                        undo.target,
                        undo.layout);

                    if (!IsRuntimeCloneOrChild(
                            undo.target))
                    {
                        _dirtyRects.Add(
                            undo.target);
                    }

                    _selected = undo.target;
                    return;
                }

                if (undo.kind == UndoKind.Hierarchy)
                {
                    if (undo.target == null ||
                        undo.hierarchyParent == null ||
                        undo.layout == null)
                        continue;

                    undo.target.SetParent(
                        undo.hierarchyParent,
                        false);

                    ApplyLayout(
                        undo.target,
                        undo.layout);

                    undo.target.SetSiblingIndex(
                        Mathf.Clamp(
                            undo.siblingIndex,
                            0,
                            undo.hierarchyParent.childCount - 1));

                    UpdateHierarchyPersistence(
                        undo.target);

                    _selected =
                        undo.target;
                    return;
                }

                if (undo.kind == UndoKind.PasteClone)
                {
                    if (!string.IsNullOrEmpty(
                            undo.cloneId))
                    {
                        _savedClones.Remove(
                            undo.cloneId);
                    }

                    if (undo.target != null)
                    {
                        UnregisterRuntimeClone(
                            undo.cloneId,
                            undo.target);

                        Destroy(
                            undo.target.gameObject);
                    }

                    _selected = null;
                    return;
                }

                if (undo.kind ==
                    UndoKind.DeleteOriginalPreserveChildren)
                {
                    if (!string.IsNullOrEmpty(
                            undo.path))
                    {
                        _savedDeletedPaths.Remove(
                            undo.path);
                    }

                    if (undo.target == null)
                        return;

                    undo.target.gameObject.SetActive(
                        true);

                    if (undo.promotedChildren != null)
                    {
                        for (var index = 0;
                             index < undo.promotedChildren.Count;
                             ++index)
                        {
                            var childUndo =
                                undo.promotedChildren[index];

                            if (childUndo == null ||
                                childUndo.target == null ||
                                childUndo.layout == null)
                                continue;

                            childUndo.target.SetParent(
                                undo.target,
                                false);

                            ApplyLayout(
                                childUndo.target,
                                childUndo.layout);

                            childUndo.target.SetSiblingIndex(
                                Mathf.Clamp(
                                    childUndo.siblingIndex,
                                    0,
                                    undo.target.childCount - 1));

                            UpdateHierarchyPersistence(
                                childUndo.target);
                        }
                    }

                    _selected = undo.target;
                    return;
                }

                if (undo.kind == UndoKind.DeleteOriginal)
                {
                    if (!string.IsNullOrEmpty(
                            undo.path))
                    {
                        _savedDeletedPaths.Remove(
                            undo.path);
                    }

                    if (undo.target != null)
                    {
                        undo.target.gameObject.SetActive(
                            true);
                        _selected = undo.target;
                    }

                    return;
                }

                if (undo.kind == UndoKind.DeleteCloneChild)
                {
                    if (undo.target != null)
                    {
                        undo.target.gameObject.SetActive(
                            true);
                        _selected = undo.target;
                    }

                    return;
                }

                if (undo.kind == UndoKind.DeleteCloneRoot)
                {
                    if (!string.IsNullOrEmpty(
                            undo.cloneId) &&
                        undo.cloneRecord != null)
                    {
                        _savedClones[
                            undo.cloneId] =
                            undo.cloneRecord;
                    }

                    if (undo.target != null)
                    {
                        undo.target.gameObject.name =
                            string.IsNullOrEmpty(
                                undo.objectName)
                                ? "UI Element Copy"
                                : undo.objectName;

                        undo.target.gameObject.SetActive(
                            true);

                        _selected = undo.target;
                    }

                    return;
                }
            }
        }

        private void PushUndo(
            UndoRecord record)
        {
            if (record == null)
                return;

            _undoStack.Push(record);
        }

        private void UnregisterRuntimeClone(
            string cloneId,
            RectTransform root)
        {
            if (root != null)
            {
                var transforms =
                    root.GetComponentsInChildren<Transform>(
                        true);

                for (var index = 0;
                     index < transforms.Length;
                     ++index)
                {
                    var current = transforms[index];

                    if (current == null)
                        continue;

                    var instanceId =
                        current.gameObject.GetInstanceID();

                    _runtimeCloneInstanceIds.Remove(
                        instanceId);
                    _runtimeCloneOwnerByInstanceId.Remove(
                        instanceId);
                }
            }

            if (!string.IsNullOrEmpty(cloneId))
            {
                _runtimeCloneRoots.Remove(cloneId);
            }
        }

        private static bool LayoutEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null)
                return false;

            if (!Approximately(left.anchorMin, right.anchorMin) ||
                !Approximately(left.anchorMax, right.anchorMax) ||
                !Approximately(left.pivot, right.pivot) ||
                !Approximately(
                    left.anchoredPosition,
                    right.anchoredPosition) ||
                !Approximately(left.sizeDelta, right.sizeDelta))
                return false;

            if (left.componentMetadataKnown !=
                right.componentMetadataKnown)
                return false;

            if (!left.componentMetadataKnown)
                return true;

            return
                left.hasLayoutElement ==
                    right.hasLayoutElement &&
                Mathf.Approximately(
                    left.layoutMinWidth,
                    right.layoutMinWidth) &&
                Mathf.Approximately(
                    left.layoutMinHeight,
                    right.layoutMinHeight) &&
                Mathf.Approximately(
                    left.layoutPreferredWidth,
                    right.layoutPreferredWidth) &&
                Mathf.Approximately(
                    left.layoutPreferredHeight,
                    right.layoutPreferredHeight) &&
                Mathf.Approximately(
                    left.layoutFlexibleWidth,
                    right.layoutFlexibleWidth) &&
                Mathf.Approximately(
                    left.layoutFlexibleHeight,
                    right.layoutFlexibleHeight) &&
                left.layoutIgnore ==
                    right.layoutIgnore &&
                left.hasContentSizeFitter ==
                    right.hasContentSizeFitter &&
                left.contentHorizontalFit ==
                    right.contentHorizontalFit &&
                left.contentVerticalFit ==
                    right.contentVerticalFit;
        }

        private static bool Approximately(
            Vector2 left,
            Vector2 right)
        {
            return
                Mathf.Approximately(left.x, right.x) &&
                Mathf.Approximately(left.y, right.y);
        }

        private static string BuildUniqueCloneName(
            string sourceName,
            Transform parent)
        {
            var baseName =
                string.IsNullOrEmpty(sourceName)
                    ? "UI Element"
                    : sourceName;

            const string copySuffix = " Copy";

            if (baseName.EndsWith(
                    "(Clone)",
                    StringComparison.Ordinal))
            {
                baseName =
                    baseName.Substring(
                        0,
                        baseName.Length -
                        "(Clone)".Length).TrimEnd();
            }

            var candidate =
                baseName + copySuffix;

            if (parent.Find(candidate) == null)
                return candidate;

            for (var index = 2;
                 index < 10000;
                 ++index)
            {
                candidate =
                    baseName +
                    copySuffix +
                    " " +
                    index;

                if (parent.Find(candidate) == null)
                    return candidate;
            }

            return baseName +
                   copySuffix +
                   " " +
                   Guid.NewGuid().ToString("N");
        }

        private void MarkSelectedDirty()
        {
            if (_selected == null)
                return;

            if (IsRuntimeCloneOrChild(_selected))
                return;

            _dirtyRects.Add(_selected);
        }

        private void RegisterRuntimeClone(
            string cloneId,
            RectTransform root)
        {
            if (string.IsNullOrEmpty(cloneId) ||
                root == null)
                return;

            _runtimeCloneRoots[cloneId] = root;

            var transforms =
                root.GetComponentsInChildren<Transform>(true);

            for (var index = 0;
                 index < transforms.Length;
                 ++index)
            {
                var current = transforms[index];

                if (current == null)
                    continue;

                var instanceId =
                    current.gameObject.GetInstanceID();

                _runtimeCloneInstanceIds.Add(instanceId);
                _runtimeCloneOwnerByInstanceId[instanceId] =
                    cloneId;
            }
        }

        private bool IsRuntimeCloneOrChild(
            Transform transform)
        {
            if (transform == null)
                return false;

            return _runtimeCloneOwnerByInstanceId.ContainsKey(
                transform.gameObject.GetInstanceID());
        }

        private string ResolvePersistentSourcePath(
            RectTransform rect)
        {
            if (rect == null)
                return string.Empty;

            string cloneId;

            if (!_runtimeCloneOwnerByInstanceId.TryGetValue(
                    rect.gameObject.GetInstanceID(),
                    out cloneId))
            {
                return GetPersistentOriginalPath(rect);
            }

            CloneRecord cloneRecord;
            RectTransform cloneRoot;

            if (!_savedClones.TryGetValue(
                    cloneId,
                    out cloneRecord) ||
                !_runtimeCloneRoots.TryGetValue(
                    cloneId,
                    out cloneRoot) ||
                cloneRoot == null ||
                string.IsNullOrEmpty(
                    cloneRecord.sourcePath))
            {
                return string.Empty;
            }

            var relativePath =
                BuildRelativeTransformPath(
                    cloneRoot,
                    rect);

            if (string.IsNullOrEmpty(relativePath))
                return cloneRecord.sourcePath;

            return cloneRecord.sourcePath +
                   "/" +
                   relativePath;
        }

        private string ResolvePersistentTargetPath(
            RectTransform rect)
        {
            if (rect == null)
                return string.Empty;

            if (IsRuntimeCloneOrChild(rect))
                return string.Empty;

            return GetPersistentOriginalPath(rect);
        }

        private string GetPersistentOriginalPath(
            RectTransform rect)
        {
            if (rect == null ||
                IsRuntimeCloneOrChild(rect))
                return string.Empty;

            string path;

            if (_persistentOriginalPaths.TryGetValue(
                    rect,
                    out path) &&
                !string.IsNullOrEmpty(path))
                return path;

            path =
                BuildStableTransformPath(rect);

            if (!string.IsNullOrEmpty(path))
                _persistentOriginalPaths[rect] =
                    path;

            return path;
        }

        private void CacheSourceTemplate(
            string sourcePath,
            RectTransform source)
        {
            if (string.IsNullOrEmpty(sourcePath) ||
                source == null ||
                _sourceTemplateCache.ContainsKey(
                    sourcePath))
                return;

            var template =
                Instantiate(
                    source.gameObject,
                    _clipboardRoot.transform,
                    false);

            template.name =
                "UP Source Template";

            template.SetActive(false);

            _sourceTemplateCache[sourcePath] =
                template;
        }

        private void SaveDirtyLayouts()
        {
            if (_selected != null &&
                !IsRuntimeCloneOrChild(_selected))
            {
                _dirtyRects.Add(_selected);
            }

            var deadRects =
                new List<RectTransform>();

            foreach (var rect in _dirtyRects)
            {
                if (rect == null)
                {
                    deadRects.Add(rect);
                    continue;
                }

                if (IsRuntimeCloneOrChild(rect))
                    continue;

                var path =
                    GetPersistentOriginalPath(rect);

                if (string.IsNullOrEmpty(path))
                    continue;

                var record =
                    CaptureLayout(rect, path);

                var parentRect =
                    rect.parent as RectTransform;

                record.parentPath =
                    ResolvePersistentTargetPath(
                        parentRect);
                record.siblingIndex =
                    rect.GetSiblingIndex();

                _savedLayouts[path] =
                    record;
            }

            for (var index = 0;
                 index < deadRects.Count;
                 ++index)
            {
                _dirtyRects.Remove(
                    deadRects[index]);
            }

            var cloneIds =
                new List<string>(
                    _runtimeCloneRoots.Keys);

            for (var index = 0;
                 index < cloneIds.Count;
                 ++index)
            {
                var cloneId = cloneIds[index];

                RectTransform root;
                CloneRecord record;

                if (!_runtimeCloneRoots.TryGetValue(
                        cloneId,
                        out root) ||
                    root == null ||
                    !_savedClones.TryGetValue(
                        cloneId,
                        out record))
                    continue;

                record.name = root.gameObject.name;
                record.siblingIndex =
                    root.GetSiblingIndex();
                record.activeSelf =
                    root.gameObject.activeSelf;
                record.rects =
                    CaptureCloneLayouts(root);
            }

            WriteSavedLayouts();
            _dirtyRects.Clear();
        }

        private static RectLayoutRecord CaptureLayout(
            RectTransform rect,
            string path)
        {
            var layoutElement =
                rect != null
                    ? rect.GetComponent<LayoutElement>()
                    : null;

            var contentSizeFitter =
                rect != null
                    ? rect.GetComponent<ContentSizeFitter>()
                    : null;

            bool hasGraphicColor;
            Color graphicColor;
            bool hasTextOverride;
            string textValue;

            CaptureAppearance(
                rect,
                out hasGraphicColor,
                out graphicColor,
                out hasTextOverride,
                out textValue);

            bool hasOutline;
            bool outlineEnabled;
            Color outlineColor;
            Vector2 outlineDistance;
            bool outlineUseGraphicAlpha;
            bool hasTextStyle;
            float textFontSize;
            bool textAutoSize;
            float textFontSizeMin;
            float textFontSizeMax;

            CaptureExtendedAppearance(
                rect,
                out hasOutline,
                out outlineEnabled,
                out outlineColor,
                out outlineDistance,
                out outlineUseGraphicAlpha,
                out hasTextStyle,
                out textFontSize,
                out textAutoSize,
                out textFontSizeMin,
                out textFontSizeMax);

            return new RectLayoutRecord
            {
                path = path,
                siblingIndex =
                    rect != null
                        ? rect.GetSiblingIndex()
                        : -1,
                anchorMin =
                    rect != null
                        ? rect.anchorMin
                        : Vector2.zero,
                anchorMax =
                    rect != null
                        ? rect.anchorMax
                        : Vector2.zero,
                pivot =
                    rect != null
                        ? rect.pivot
                        : Vector2.zero,
                anchoredPosition =
                    rect != null
                        ? rect.anchoredPosition
                        : Vector2.zero,
                sizeDelta =
                    rect != null
                        ? rect.sizeDelta
                        : Vector2.zero,

                componentMetadataKnown = true,

                hasLayoutElement =
                    layoutElement != null,
                layoutMinWidth =
                    layoutElement != null
                        ? layoutElement.minWidth
                        : -1f,
                layoutMinHeight =
                    layoutElement != null
                        ? layoutElement.minHeight
                        : -1f,
                layoutPreferredWidth =
                    layoutElement != null
                        ? layoutElement.preferredWidth
                        : -1f,
                layoutPreferredHeight =
                    layoutElement != null
                        ? layoutElement.preferredHeight
                        : -1f,
                layoutFlexibleWidth =
                    layoutElement != null
                        ? layoutElement.flexibleWidth
                        : -1f,
                layoutFlexibleHeight =
                    layoutElement != null
                        ? layoutElement.flexibleHeight
                        : -1f,
                layoutIgnore =
                    layoutElement != null &&
                    layoutElement.ignoreLayout,

                hasContentSizeFitter =
                    contentSizeFitter != null,
                contentHorizontalFit =
                    contentSizeFitter != null
                        ? (int)contentSizeFitter.horizontalFit
                        : 0,
                contentVerticalFit =
                    contentSizeFitter != null
                        ? (int)contentSizeFitter.verticalFit
                        : 0,

                hasGraphicColor = hasGraphicColor,
                graphicColor = graphicColor,
                hasTextOverride = hasTextOverride,
                textValue = textValue,

                extendedAppearanceKnown = true,
                hasOutline = hasOutline,
                outlineEnabled = outlineEnabled,
                outlineColor = outlineColor,
                outlineDistance = outlineDistance,
                outlineUseGraphicAlpha = outlineUseGraphicAlpha,
                hasTextStyle = hasTextStyle,
                textFontSize = textFontSize,
                textAutoSize = textAutoSize,
                textFontSizeMin = textFontSizeMin,
                textFontSizeMax = textFontSizeMax
            };
        }

        private static List<RelativeRectLayoutRecord>
            CaptureCloneLayouts(
                RectTransform root)
        {
            var result =
                new List<RelativeRectLayoutRecord>();

            if (root == null)
                return result;

            var rects =
                root.GetComponentsInChildren<RectTransform>(
                    true);

            for (var index = 0;
                 index < rects.Length;
                 ++index)
            {
                var rect = rects[index];

                if (rect == null)
                    continue;

                bool hasGraphicColor;
                Color graphicColor;
                bool hasTextOverride;
                string textValue;

                CaptureAppearance(
                    rect,
                    out hasGraphicColor,
                    out graphicColor,
                    out hasTextOverride,
                    out textValue);

                bool hasOutline;
                bool outlineEnabled;
                Color outlineColor;
                Vector2 outlineDistance;
                bool outlineUseGraphicAlpha;
                bool hasTextStyle;
                float textFontSize;
                bool textAutoSize;
                float textFontSizeMin;
                float textFontSizeMax;

                CaptureExtendedAppearance(
                    rect,
                    out hasOutline,
                    out outlineEnabled,
                    out outlineColor,
                    out outlineDistance,
                    out outlineUseGraphicAlpha,
                    out hasTextStyle,
                    out textFontSize,
                    out textAutoSize,
                    out textFontSizeMin,
                    out textFontSizeMax);

                result.Add(
                    new RelativeRectLayoutRecord
                    {
                        relativePath =
                            BuildRelativeTransformPath(
                                root,
                                rect),
                        anchorMin = rect.anchorMin,
                        anchorMax = rect.anchorMax,
                        pivot = rect.pivot,
                        anchoredPosition =
                            rect.anchoredPosition,
                        sizeDelta = rect.sizeDelta,
                        activeSelf =
                            rect.gameObject.activeSelf,
                        hasGraphicColor =
                            hasGraphicColor,
                        graphicColor =
                            graphicColor,
                        hasTextOverride =
                            hasTextOverride,
                        textValue =
                            textValue,

                        extendedAppearanceKnown = true,
                        hasOutline = hasOutline,
                        outlineEnabled = outlineEnabled,
                        outlineColor = outlineColor,
                        outlineDistance = outlineDistance,
                        outlineUseGraphicAlpha = outlineUseGraphicAlpha,
                        hasTextStyle = hasTextStyle,
                        textFontSize = textFontSize,
                        textAutoSize = textAutoSize,
                        textFontSizeMin = textFontSizeMin,
                        textFontSizeMax = textFontSizeMax
                    });
            }

            return result;
        }

        private void LoadSavedLayouts()
        {
            _savedLayouts.Clear();
            _savedClones.Clear();
            _savedDeletedPaths.Clear();

            try
            {
                if (string.IsNullOrEmpty(
                        _layoutFilePath) ||
                    !File.Exists(
                        _layoutFilePath))
                    return;

                var lines =
                    File.ReadAllLines(
                        _layoutFilePath);

                if (lines.Length == 0 ||
                    (lines[0] != "UP_UI_LAYOUT_V1" &&
                     lines[0] != "UP_UI_LAYOUT_V2" &&
                     lines[0] != "UP_UI_LAYOUT_V3" &&
                     lines[0] != "UP_UI_LAYOUT_V4" &&
                     lines[0] != "UP_UI_LAYOUT_V5" &&
                     lines[0] != "UP_UI_LAYOUT_V6"))
                    return;

                CloneRecord currentClone = null;

                for (var index = 1;
                     index < lines.Length;
                     ++index)
                {
                    var line = lines[index];

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split('|');

                    if (parts.Length == 0)
                        continue;

                    if (parts[0] == "E")
                    {
                        RectLayoutRecord record;

                        if (TryParseElementRecord(
                                parts,
                                out record))
                        {
                            _savedLayouts[
                                record.path] = record;
                        }

                        currentClone = null;
                        continue;
                    }

                    if (parts[0] == "C")
                    {
                        CloneRecord record;

                        if (TryParseCloneRecord(
                                parts,
                                out record))
                        {
                            _savedClones[
                                record.id] = record;

                            currentClone = record;
                        }
                        else
                        {
                            currentClone = null;
                        }

                        continue;
                    }

                    if (parts[0] == "D")
                    {
                        if (parts.Length == 2)
                        {
                            var deletedPath =
                                DecodeText(parts[1]);

                            if (!string.IsNullOrEmpty(
                                    deletedPath))
                            {
                                _savedDeletedPaths.Add(
                                    deletedPath);
                            }
                        }

                        currentClone = null;
                        continue;
                    }

                    if (parts[0] == "R" &&
                        currentClone != null)
                    {
                        RelativeRectLayoutRecord record;

                        if (TryParseRelativeRecord(
                                parts,
                                out record))
                        {
                            currentClone.rects.Add(
                                record);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[UnderPressure UI Designer] " +
                    "No se pudo cargar el layout: " +
                    exception.Message);
            }
        }

        private void WriteSavedLayouts()
        {
            try
            {
                var directory =
                    Path.GetDirectoryName(
                        _layoutFilePath);

                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(
                        directory);

                var lines =
                    new List<string>();

                lines.Add("UP_UI_LAYOUT_V6");

                var layoutKeys =
                    new List<string>(
                        _savedLayouts.Keys);

                layoutKeys.Sort(
                    StringComparer.Ordinal);

                for (var index = 0;
                     index < layoutKeys.Count;
                     ++index)
                {
                    var record =
                        _savedLayouts[
                            layoutKeys[index]];

                    if (record == null)
                        continue;

                    lines.Add(
                        SerializeElementRecord(
                            record));
                }

                var deletedPaths =
                    new List<string>(
                        _savedDeletedPaths);

                deletedPaths.Sort(
                    StringComparer.Ordinal);

                for (var index = 0;
                     index < deletedPaths.Count;
                     ++index)
                {
                    lines.Add(
                        "D|" +
                        EncodeText(
                            deletedPaths[index]));
                }

                var cloneKeys =
                    new List<string>(
                        _savedClones.Keys);

                cloneKeys.Sort(
                    StringComparer.Ordinal);

                for (var index = 0;
                     index < cloneKeys.Count;
                     ++index)
                {
                    var record =
                        _savedClones[
                            cloneKeys[index]];

                    if (record == null)
                        continue;

                    lines.Add(
                        SerializeCloneRecord(
                            record));

                    if (record.rects == null)
                        continue;

                    for (var rectIndex = 0;
                         rectIndex <
                         record.rects.Count;
                         ++rectIndex)
                    {
                        var rectRecord =
                            record.rects[
                                rectIndex];

                        if (rectRecord == null)
                            continue;

                        lines.Add(
                            SerializeRelativeRecord(
                                rectRecord));
                    }
                }

                File.WriteAllLines(
                    _layoutFilePath,
                    lines.ToArray());
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[UnderPressure UI Designer] " +
                    "No se pudo guardar el layout: " +
                    exception.Message);
            }
        }

        private static string SerializeElementRecord(
            RectLayoutRecord record)
        {
            var fields = new List<string>
            {
                "E",
                EncodeText(record.path),
                FormatFloat(record.anchorMin.x),
                FormatFloat(record.anchorMin.y),
                FormatFloat(record.anchorMax.x),
                FormatFloat(record.anchorMax.y),
                FormatFloat(record.pivot.x),
                FormatFloat(record.pivot.y),
                FormatFloat(record.anchoredPosition.x),
                FormatFloat(record.anchoredPosition.y),
                FormatFloat(record.sizeDelta.x),
                FormatFloat(record.sizeDelta.y),
                EncodeText(record.parentPath),
                record.siblingIndex.ToString(CultureInfo.InvariantCulture),

                record.hasLayoutElement ? "1" : "0",
                FormatFloat(record.layoutMinWidth),
                FormatFloat(record.layoutMinHeight),
                FormatFloat(record.layoutPreferredWidth),
                FormatFloat(record.layoutPreferredHeight),
                FormatFloat(record.layoutFlexibleWidth),
                FormatFloat(record.layoutFlexibleHeight),
                record.layoutIgnore ? "1" : "0",

                record.hasContentSizeFitter ? "1" : "0",
                record.contentHorizontalFit.ToString(CultureInfo.InvariantCulture),
                record.contentVerticalFit.ToString(CultureInfo.InvariantCulture),

                record.hasGraphicColor ? "1" : "0",
                FormatFloat(record.graphicColor.r),
                FormatFloat(record.graphicColor.g),
                FormatFloat(record.graphicColor.b),
                FormatFloat(record.graphicColor.a),
                record.hasTextOverride ? "1" : "0",
                EncodeText(record.textValue)
            };

            if (record.extendedAppearanceKnown)
            {
                fields.Add(record.hasOutline ? "1" : "0");
                fields.Add(record.outlineEnabled ? "1" : "0");
                fields.Add(FormatFloat(record.outlineColor.r));
                fields.Add(FormatFloat(record.outlineColor.g));
                fields.Add(FormatFloat(record.outlineColor.b));
                fields.Add(FormatFloat(record.outlineColor.a));
                fields.Add(FormatFloat(record.outlineDistance.x));
                fields.Add(FormatFloat(record.outlineDistance.y));
                fields.Add(record.outlineUseGraphicAlpha ? "1" : "0");
                fields.Add(record.hasTextStyle ? "1" : "0");
                fields.Add(FormatFloat(record.textFontSize));
                fields.Add(record.textAutoSize ? "1" : "0");
                fields.Add(FormatFloat(record.textFontSizeMin));
                fields.Add(FormatFloat(record.textFontSizeMax));
            }

            return string.Join("|", fields.ToArray());
        }

        private static string SerializeCloneRecord(
            CloneRecord record)
        {
            return string.Join(
                "|",
                new[]
                {
                    "C",
                    EncodeText(record.id),
                    EncodeText(record.sourcePath),
                    EncodeText(record.targetParentPath),
                    EncodeText(record.name),
                    record.siblingIndex.ToString(
                        CultureInfo.InvariantCulture),
                    record.activeSelf ? "1" : "0"
                });
        }

        private static string SerializeRelativeRecord(
            RelativeRectLayoutRecord record)
        {
            var fields = new List<string>
            {
                "R",
                EncodeText(record.relativePath),
                FormatFloat(record.anchorMin.x),
                FormatFloat(record.anchorMin.y),
                FormatFloat(record.anchorMax.x),
                FormatFloat(record.anchorMax.y),
                FormatFloat(record.pivot.x),
                FormatFloat(record.pivot.y),
                FormatFloat(record.anchoredPosition.x),
                FormatFloat(record.anchoredPosition.y),
                FormatFloat(record.sizeDelta.x),
                FormatFloat(record.sizeDelta.y),
                record.activeSelf ? "1" : "0",
                record.hasGraphicColor ? "1" : "0",
                FormatFloat(record.graphicColor.r),
                FormatFloat(record.graphicColor.g),
                FormatFloat(record.graphicColor.b),
                FormatFloat(record.graphicColor.a),
                record.hasTextOverride ? "1" : "0",
                EncodeText(record.textValue)
            };

            if (record.extendedAppearanceKnown)
            {
                fields.Add(record.hasOutline ? "1" : "0");
                fields.Add(record.outlineEnabled ? "1" : "0");
                fields.Add(FormatFloat(record.outlineColor.r));
                fields.Add(FormatFloat(record.outlineColor.g));
                fields.Add(FormatFloat(record.outlineColor.b));
                fields.Add(FormatFloat(record.outlineColor.a));
                fields.Add(FormatFloat(record.outlineDistance.x));
                fields.Add(FormatFloat(record.outlineDistance.y));
                fields.Add(record.outlineUseGraphicAlpha ? "1" : "0");
                fields.Add(record.hasTextStyle ? "1" : "0");
                fields.Add(FormatFloat(record.textFontSize));
                fields.Add(record.textAutoSize ? "1" : "0");
                fields.Add(FormatFloat(record.textFontSizeMin));
                fields.Add(FormatFloat(record.textFontSizeMax));
            }

            return string.Join("|", fields.ToArray());
        }

        private static bool TryParseElementRecord(
            string[] parts,
            out RectLayoutRecord record)
        {
            record = null;

            if (parts == null ||
                (parts.Length != 12 &&
                 parts.Length != 14 &&
                 parts.Length != 25 &&
                 parts.Length != 32 &&
                 parts.Length != 46))
                return false;

            Vector2 anchorMin;
            Vector2 anchorMax;
            Vector2 pivot;
            Vector2 anchoredPosition;
            Vector2 sizeDelta;

            if (!TryParseVector2(
                    parts,
                    2,
                    out anchorMin) ||
                !TryParseVector2(
                    parts,
                    4,
                    out anchorMax) ||
                !TryParseVector2(
                    parts,
                    6,
                    out pivot) ||
                !TryParseVector2(
                    parts,
                    8,
                    out anchoredPosition) ||
                !TryParseVector2(
                    parts,
                    10,
                    out sizeDelta))
                return false;

            var parentPath =
                string.Empty;

            var siblingIndex =
                -1;

            if (parts.Length >= 14)
            {
                parentPath =
                    DecodeText(parts[12]);

                if (!int.TryParse(
                        parts[13],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out siblingIndex))
                    return false;
            }

            var parsed =
                new RectLayoutRecord
                {
                    path = DecodeText(parts[1]),
                    parentPath = parentPath,
                    siblingIndex = siblingIndex,
                    anchorMin = anchorMin,
                    anchorMax = anchorMax,
                    pivot = pivot,
                    anchoredPosition =
                        anchoredPosition,
                    sizeDelta = sizeDelta,
                    componentMetadataKnown =
                        parts.Length == 25 ||
                        parts.Length == 32 ||
                        parts.Length == 46
                };

            if (parts.Length == 25 ||
                parts.Length == 32 ||
                parts.Length == 46)
            {
                float minWidth;
                float minHeight;
                float preferredWidth;
                float preferredHeight;
                float flexibleWidth;
                float flexibleHeight;
                int horizontalFit;
                int verticalFit;

                if (!TryParseBool01(
                        parts[14],
                        out parsed.hasLayoutElement) ||
                    !float.TryParse(
                        parts[15],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out minWidth) ||
                    !float.TryParse(
                        parts[16],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out minHeight) ||
                    !float.TryParse(
                        parts[17],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out preferredWidth) ||
                    !float.TryParse(
                        parts[18],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out preferredHeight) ||
                    !float.TryParse(
                        parts[19],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out flexibleWidth) ||
                    !float.TryParse(
                        parts[20],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out flexibleHeight) ||
                    !TryParseBool01(
                        parts[21],
                        out parsed.layoutIgnore) ||
                    !TryParseBool01(
                        parts[22],
                        out parsed.hasContentSizeFitter) ||
                    !int.TryParse(
                        parts[23],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out horizontalFit) ||
                    !int.TryParse(
                        parts[24],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out verticalFit))
                    return false;

                parsed.layoutMinWidth =
                    minWidth;
                parsed.layoutMinHeight =
                    minHeight;
                parsed.layoutPreferredWidth =
                    preferredWidth;
                parsed.layoutPreferredHeight =
                    preferredHeight;
                parsed.layoutFlexibleWidth =
                    flexibleWidth;
                parsed.layoutFlexibleHeight =
                    flexibleHeight;
                parsed.contentHorizontalFit =
                    horizontalFit;
                parsed.contentVerticalFit =
                    verticalFit;
            }

            if (parts.Length == 32 ||
                parts.Length == 46)
            {
                float colorR;
                float colorG;
                float colorB;
                float colorA;

                if (!TryParseBool01(
                        parts[25],
                        out parsed.hasGraphicColor) ||
                    !float.TryParse(
                        parts[26],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorR) ||
                    !float.TryParse(
                        parts[27],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorG) ||
                    !float.TryParse(
                        parts[28],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorB) ||
                    !float.TryParse(
                        parts[29],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorA) ||
                    !TryParseBool01(
                        parts[30],
                        out parsed.hasTextOverride))
                    return false;

                parsed.graphicColor =
                    new Color(
                        colorR,
                        colorG,
                        colorB,
                        colorA);

                parsed.textValue =
                    DecodeText(parts[31]);
            }

            if (parts.Length == 46)
            {
                float outlineR;
                float outlineG;
                float outlineB;
                float outlineA;
                float outlineX;
                float outlineY;
                float fontSize;
                float fontMin;
                float fontMax;

                parsed.extendedAppearanceKnown = true;

                if (!TryParseBool01(parts[32], out parsed.hasOutline) ||
                    !TryParseBool01(parts[33], out parsed.outlineEnabled) ||
                    !float.TryParse(parts[34], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineR) ||
                    !float.TryParse(parts[35], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineG) ||
                    !float.TryParse(parts[36], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineB) ||
                    !float.TryParse(parts[37], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineA) ||
                    !float.TryParse(parts[38], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineX) ||
                    !float.TryParse(parts[39], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineY) ||
                    !TryParseBool01(parts[40], out parsed.outlineUseGraphicAlpha) ||
                    !TryParseBool01(parts[41], out parsed.hasTextStyle) ||
                    !float.TryParse(parts[42], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize) ||
                    !TryParseBool01(parts[43], out parsed.textAutoSize) ||
                    !float.TryParse(parts[44], NumberStyles.Float, CultureInfo.InvariantCulture, out fontMin) ||
                    !float.TryParse(parts[45], NumberStyles.Float, CultureInfo.InvariantCulture, out fontMax))
                    return false;

                parsed.outlineColor = new Color(outlineR, outlineG, outlineB, outlineA);
                parsed.outlineDistance = new Vector2(outlineX, outlineY);
                parsed.textFontSize = fontSize;
                parsed.textFontSizeMin = fontMin;
                parsed.textFontSizeMax = fontMax;
            }

            record = parsed;

            return !string.IsNullOrEmpty(
                record.path);
        }

        private static bool TryParseCloneRecord(
            string[] parts,
            out CloneRecord record)
        {
            record = null;

            if (parts == null ||
                parts.Length != 7)
                return false;

            int siblingIndex;

            if (!int.TryParse(
                    parts[5],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out siblingIndex))
                return false;

            record =
                new CloneRecord
                {
                    id = DecodeText(parts[1]),
                    sourcePath =
                        DecodeText(parts[2]),
                    targetParentPath =
                        DecodeText(parts[3]),
                    name = DecodeText(parts[4]),
                    siblingIndex =
                        siblingIndex,
                    activeSelf =
                        parts[6] == "1",
                    rects =
                        new List<
                            RelativeRectLayoutRecord>()
                };

            return !string.IsNullOrEmpty(
                record.id);
        }

        private static bool TryParseRelativeRecord(
            string[] parts,
            out RelativeRectLayoutRecord record)
        {
            record = null;

            if (parts == null ||
                (parts.Length != 12 &&
                 parts.Length != 13 &&
                 parts.Length != 20 &&
                 parts.Length != 34))
                return false;

            Vector2 anchorMin;
            Vector2 anchorMax;
            Vector2 pivot;
            Vector2 anchoredPosition;
            Vector2 sizeDelta;

            if (!TryParseVector2(
                    parts,
                    2,
                    out anchorMin) ||
                !TryParseVector2(
                    parts,
                    4,
                    out anchorMax) ||
                !TryParseVector2(
                    parts,
                    6,
                    out pivot) ||
                !TryParseVector2(
                    parts,
                    8,
                    out anchoredPosition) ||
                !TryParseVector2(
                    parts,
                    10,
                    out sizeDelta))
                return false;

            var parsed =
                new RelativeRectLayoutRecord
                {
                    relativePath =
                        DecodeText(parts[1]),
                    anchorMin = anchorMin,
                    anchorMax = anchorMax,
                    pivot = pivot,
                    anchoredPosition =
                        anchoredPosition,
                    sizeDelta = sizeDelta,
                    activeSelf =
                        parts.Length == 12 ||
                        parts[12] == "1"
                };

            if (parts.Length == 20 ||
                parts.Length == 34)
            {
                float colorR;
                float colorG;
                float colorB;
                float colorA;

                if (!TryParseBool01(
                        parts[13],
                        out parsed.hasGraphicColor) ||
                    !float.TryParse(
                        parts[14],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorR) ||
                    !float.TryParse(
                        parts[15],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorG) ||
                    !float.TryParse(
                        parts[16],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorB) ||
                    !float.TryParse(
                        parts[17],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out colorA) ||
                    !TryParseBool01(
                        parts[18],
                        out parsed.hasTextOverride))
                    return false;

                parsed.graphicColor =
                    new Color(
                        colorR,
                        colorG,
                        colorB,
                        colorA);

                parsed.textValue =
                    DecodeText(parts[19]);
            }

            if (parts.Length == 34)
            {
                float outlineR;
                float outlineG;
                float outlineB;
                float outlineA;
                float outlineX;
                float outlineY;
                float fontSize;
                float fontMin;
                float fontMax;

                parsed.extendedAppearanceKnown = true;

                if (!TryParseBool01(parts[20], out parsed.hasOutline) ||
                    !TryParseBool01(parts[21], out parsed.outlineEnabled) ||
                    !float.TryParse(parts[22], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineR) ||
                    !float.TryParse(parts[23], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineG) ||
                    !float.TryParse(parts[24], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineB) ||
                    !float.TryParse(parts[25], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineA) ||
                    !float.TryParse(parts[26], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineX) ||
                    !float.TryParse(parts[27], NumberStyles.Float, CultureInfo.InvariantCulture, out outlineY) ||
                    !TryParseBool01(parts[28], out parsed.outlineUseGraphicAlpha) ||
                    !TryParseBool01(parts[29], out parsed.hasTextStyle) ||
                    !float.TryParse(parts[30], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize) ||
                    !TryParseBool01(parts[31], out parsed.textAutoSize) ||
                    !float.TryParse(parts[32], NumberStyles.Float, CultureInfo.InvariantCulture, out fontMin) ||
                    !float.TryParse(parts[33], NumberStyles.Float, CultureInfo.InvariantCulture, out fontMax))
                    return false;

                parsed.outlineColor = new Color(outlineR, outlineG, outlineB, outlineA);
                parsed.outlineDistance = new Vector2(outlineX, outlineY);
                parsed.textFontSize = fontSize;
                parsed.textFontSizeMin = fontMin;
                parsed.textFontSizeMax = fontMax;
            }

            record = parsed;
            return true;
        }

        private static bool TryParseBool01(
            string value,
            out bool result)
        {
            if (value == "1")
            {
                result = true;
                return true;
            }

            if (value == "0")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static bool TryParseVector2(
            string[] parts,
            int startIndex,
            out Vector2 value)
        {
            value = Vector2.zero;

            float x;
            float y;

            if (!float.TryParse(
                    parts[startIndex],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out x) ||
                !float.TryParse(
                    parts[startIndex + 1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out y))
                return false;

            value = new Vector2(x, y);
            return true;
        }

        private static string FormatFloat(
            float value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }

        private static string EncodeText(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    value));
        }

        private static string DecodeText(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    value));
        }

        private void TryAutoApplySavedLayouts()
        {
            if (_savedLayouts.Count == 0 &&
                _savedClones.Count == 0 &&
                _savedDeletedPaths.Count == 0)
                return;

            if (Time.unscaledTime <
                _nextAutoApplyTime)
                return;

            _nextAutoApplyTime =
                Time.unscaledTime +
                AutoApplyInterval;

            _autoApplyPassesRemaining =
                Mathf.Max(
                    0,
                    _autoApplyPassesRemaining - 1);

            var rectsByPath =
                BuildPersistenceRectMap(null);

            foreach (var path in _savedDeletedPaths)
            {
                RectTransform deleted;

                if (rectsByPath.TryGetValue(
                        path, out deleted) &&
                    deleted != null &&
                    deleted.gameObject.activeSelf)
                {
                    deleted.gameObject.SetActive(false);
                }
            }

            foreach (var pair in _savedLayouts)
            {
                var record =
                    pair.Value;

                if (record == null ||
                    string.IsNullOrEmpty(
                        record.path))
                    continue;

                RectTransform rect;

                if (!rectsByPath.TryGetValue(
                        record.path,
                        out rect) ||
                    rect == null)
                    continue;

                if (!string.IsNullOrEmpty(
                        record.parentPath))
                {
                    RectTransform targetParent;

                    if (rectsByPath.TryGetValue(
                            record.parentPath,
                            out targetParent) &&
                        targetParent != null &&
                        targetParent != rect &&
                        !targetParent.IsChildOf(rect) &&
                        rect.parent != targetParent)
                    {
                        rect.SetParent(
                            targetParent,
                            false);
                    }
                }

                ApplyLayout(
                    rect,
                    record);

                if (record.siblingIndex >= 0 &&
                    rect.parent != null)
                {
                    rect.SetSiblingIndex(
                        Mathf.Clamp(
                            record.siblingIndex,
                            0,
                            rect.parent.childCount - 1));
                }
            }

            CacheSavedCloneSources(
                rectsByPath);

            RecreateSavedClones(
                rectsByPath);
        }

        private void CacheSavedCloneSources(
            Dictionary<string, RectTransform>
                rectsByPath)
        {
            foreach (var pair in _savedClones)
            {
                var record = pair.Value;

                if (record == null ||
                    string.IsNullOrEmpty(
                        record.sourcePath) ||
                    _sourceTemplateCache.ContainsKey(
                        record.sourcePath))
                    continue;

                RectTransform source;

                if (!rectsByPath.TryGetValue(
                        record.sourcePath,
                        out source) ||
                    source == null)
                    continue;

                CacheSourceTemplate(
                    record.sourcePath,
                    source);
            }
        }

        private void RecreateSavedClones(
            Dictionary<string, RectTransform>
                rectsByPath)
        {
            foreach (var pair in _savedClones)
            {
                var cloneId = pair.Key;
                var record = pair.Value;

                RectTransform existingRoot;

                if (_runtimeCloneRoots.TryGetValue(
                        cloneId,
                        out existingRoot) &&
                    existingRoot != null)
                    continue;

                if (record == null ||
                    string.IsNullOrEmpty(
                        record.sourcePath) ||
                    string.IsNullOrEmpty(
                        record.targetParentPath))
                    continue;

                RectTransform targetParent;

                if (!rectsByPath.TryGetValue(
                        record.targetParentPath,
                        out targetParent) ||
                    targetParent == null)
                    continue;

                GameObject sourceTemplate;

                if (!_sourceTemplateCache.TryGetValue(
                        record.sourcePath,
                        out sourceTemplate) ||
                    sourceTemplate == null)
                    continue;

                var cloneObject =
                    Instantiate(
                        sourceTemplate,
                        targetParent,
                        false);

                cloneObject.name =
                    string.IsNullOrEmpty(
                        record.name)
                        ? "UI Element Copy"
                        : record.name;

                var cloneRect =
                    cloneObject.transform as
                    RectTransform;

                if (cloneRect == null)
                {
                    Destroy(cloneObject);
                    continue;
                }

                cloneRect.SetSiblingIndex(
                    Mathf.Clamp(
                        record.siblingIndex,
                        0,
                        targetParent.childCount - 1));

                ApplyCloneLayouts(
                    cloneRect,
                    record.rects);

                cloneObject.SetActive(
                    record.activeSelf);

                RegisterRuntimeClone(
                    cloneId,
                    cloneRect);
            }
        }

        private static void ApplyCloneLayouts(
            RectTransform root,
            List<RelativeRectLayoutRecord> records)
        {
            if (root == null ||
                records == null ||
                records.Count == 0)
                return;

            var layouts =
                new Dictionary<
                    string,
                    RelativeRectLayoutRecord>(
                    StringComparer.Ordinal);

            for (var index = 0;
                 index < records.Count;
                 ++index)
            {
                var record = records[index];

                if (record == null)
                    continue;

                layouts[
                    record.relativePath ??
                    string.Empty] = record;
            }

            var rects =
                root.GetComponentsInChildren<
                    RectTransform>(true);

            for (var index = 0;
                 index < rects.Length;
                 ++index)
            {
                var rect = rects[index];

                if (rect == null)
                    continue;

                var relativePath =
                    BuildRelativeTransformPath(
                        root,
                        rect);

                RelativeRectLayoutRecord record;

                if (!layouts.TryGetValue(
                        relativePath,
                        out record))
                    continue;

                rect.anchorMin = record.anchorMin;
                rect.anchorMax = record.anchorMax;
                rect.pivot = record.pivot;
                rect.anchoredPosition =
                    record.anchoredPosition;
                rect.sizeDelta = record.sizeDelta;

                ApplyAppearance(
                    rect,
                    record.hasGraphicColor,
                    record.graphicColor,
                    record.hasTextOverride,
                    record.textValue);

                ApplyExtendedAppearance(
                    rect,
                    record.extendedAppearanceKnown,
                    record.hasOutline,
                    record.outlineEnabled,
                    record.outlineColor,
                    record.outlineDistance,
                    record.outlineUseGraphicAlpha,
                    record.hasTextStyle,
                    record.textFontSize,
                    record.textAutoSize,
                    record.textFontSizeMin,
                    record.textFontSizeMax);

                rect.gameObject.SetActive(
                    record.activeSelf);
            }
        }

        private static void ApplyLayout(
            RectTransform rect,
            RectLayoutRecord record)
        {
            if (rect == null || record == null)
                return;

            rect.anchorMin = record.anchorMin;
            rect.anchorMax = record.anchorMax;
            rect.pivot = record.pivot;
            rect.anchoredPosition =
                record.anchoredPosition;
            rect.sizeDelta = record.sizeDelta;

            ApplyAppearance(
                rect,
                record.hasGraphicColor,
                record.graphicColor,
                record.hasTextOverride,
                record.textValue);

            ApplyExtendedAppearance(
                rect,
                record.extendedAppearanceKnown,
                record.hasOutline,
                record.outlineEnabled,
                record.outlineColor,
                record.outlineDistance,
                record.outlineUseGraphicAlpha,
                record.hasTextStyle,
                record.textFontSize,
                record.textAutoSize,
                record.textFontSizeMin,
                record.textFontSizeMax);

            if (!record.componentMetadataKnown)
                return;

            var layoutElement =
                rect.GetComponent<LayoutElement>();

            if (record.hasLayoutElement)
            {
                if (layoutElement == null)
                {
                    layoutElement =
                        rect.gameObject
                            .AddComponent<LayoutElement>();
                }

                layoutElement.minWidth =
                    record.layoutMinWidth;
                layoutElement.minHeight =
                    record.layoutMinHeight;
                layoutElement.preferredWidth =
                    record.layoutPreferredWidth;
                layoutElement.preferredHeight =
                    record.layoutPreferredHeight;
                layoutElement.flexibleWidth =
                    record.layoutFlexibleWidth;
                layoutElement.flexibleHeight =
                    record.layoutFlexibleHeight;
                layoutElement.ignoreLayout =
                    record.layoutIgnore;
            }
            else if (layoutElement != null)
            {
                UnityEngine.Object.Destroy(
                    layoutElement);
            }

            var contentSizeFitter =
                rect.GetComponent<ContentSizeFitter>();

            if (record.hasContentSizeFitter &&
                contentSizeFitter != null)
            {
                contentSizeFitter.horizontalFit =
                    (ContentSizeFitter.FitMode)
                        record.contentHorizontalFit;

                contentSizeFitter.verticalFit =
                    (ContentSizeFitter.FitMode)
                        record.contentVerticalFit;
            }

            var parentRect =
                rect.parent as RectTransform;

            if (parentRect != null)
                LayoutRebuilder.MarkLayoutForRebuild(
                    parentRect);
        }

        private static string BuildStableTransformPath(
            Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var segments =
                new List<string>();

            var current = transform;

            while (current != null)
            {
                segments.Add(
                    BuildPathSegment(current));

                current = current.parent;
            }

            segments.Reverse();

            var sceneName =
                transform.gameObject.scene.IsValid()
                    ? transform.gameObject.scene.name
                    : "<invalid-scene>";

            return sceneName +
                   "|" +
                   string.Join(
                       "/",
                       segments.ToArray());
        }

        private static string BuildRelativeTransformPath(
            Transform root,
            Transform target)
        {
            if (root == null ||
                target == null)
                return string.Empty;

            if (root == target)
                return string.Empty;

            var segments =
                new List<string>();

            var current = target;

            while (current != null &&
                   current != root)
            {
                segments.Add(
                    BuildPathSegment(current));

                current = current.parent;
            }

            if (current != root)
                return string.Empty;

            segments.Reverse();

            return string.Join(
                "/",
                segments.ToArray());
        }

        private static string BuildPathSegment(
            Transform transform)
        {
            return
                EscapePathSegment(
                    transform.name) +
                "#" +
                GetSameNameSiblingOrdinal(
                    transform);
        }

        private static int GetSameNameSiblingOrdinal(
            Transform transform)
        {
            if (transform == null ||
                transform.parent == null)
                return 0;

            var ordinal = 0;

            for (var index = 0;
                 index < transform.GetSiblingIndex();
                 ++index)
            {
                var sibling =
                    transform.parent.GetChild(index);

                if (sibling != null &&
                    string.Equals(
                        sibling.name,
                        transform.name,
                        StringComparison.Ordinal))
                {
                    ++ordinal;
                }
            }

            return ordinal;
        }

        private static string EscapePathSegment(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return "<unnamed>";

            return value
                .Replace("%", "%25")
                .Replace("/", "%2F")
                .Replace("|", "%7C")
                .Replace("#", "%23");
        }

        private void SelectParent()
        {
            if (_selected == null)
                return;

            var parent = _selected.parent;

            while (parent != null)
            {
                var parentRect = parent as RectTransform;
                if (parentRect != null)
                {
                    _selected = parentRect;
                    return;
                }

                parent = parent.parent;
            }
        }
        private bool TryChangeSiblingOrder()
        {
            var pageUpHeld =
                Input.GetKey(
                    KeyCode.PageUp);

            var pageDownHeld =
                Input.GetKey(
                    KeyCode.PageDown);

            var pageUpPressed =
                pageUpHeld &&
                !_pageUpWasHeld;

            var pageDownPressed =
                pageDownHeld &&
                !_pageDownWasHeld;

            _pageUpWasHeld =
                pageUpHeld;

            _pageDownWasHeld =
                pageDownHeld;

            if (!pageUpPressed &&
                !pageDownPressed)
                return false;

            if (_selected == null ||
                _selected.parent == null)
                return true;

            var direction =
                pageUpPressed ? 1 : -1;

            var parent =
                _selected.parent as
                RectTransform;

            if (parent == null)
                return true;

            var oldIndex =
                _selected.GetSiblingIndex();

            var newIndex =
                Mathf.Clamp(
                    oldIndex + direction,
                    0,
                    parent.childCount - 1);

            var selectedCanvas =
                _selected.GetComponent<Canvas>();

            var useCanvasOrder =
                selectedCanvas != null &&
                selectedCanvas.overrideSorting;

            if (newIndex == oldIndex &&
                !useCanvasOrder)
                return true;

            PushUndo(
                new UndoRecord
                {
                    kind = UndoKind.Hierarchy,
                    target = _selected,
                    hierarchyParent = parent,
                    siblingIndex = oldIndex,
                    layout =
                        CaptureLayout(
                            _selected,
                            string.Empty)
                });

            if (newIndex != oldIndex)
            {
                _selected.SetSiblingIndex(
                    newIndex);
            }

            if (useCanvasOrder)
            {
                selectedCanvas.sortingOrder +=
                    direction;
            }

            UpdateHierarchyPersistence(
                _selected);

            return true;
        }

        private void UpdateHierarchyPersistence(
            RectTransform rect)
        {
            if (rect == null)
                return;

            string cloneId;

            if (_runtimeCloneOwnerByInstanceId
                    .TryGetValue(
                        rect.gameObject
                            .GetInstanceID(),
                        out cloneId))
            {
                RectTransform cloneRoot;
                CloneRecord cloneRecord;

                if (_runtimeCloneRoots.TryGetValue(
                        cloneId,
                        out cloneRoot) &&
                    cloneRoot == rect &&
                    _savedClones.TryGetValue(
                        cloneId,
                        out cloneRecord) &&
                    cloneRecord != null)
                {
                    var parent =
                        rect.parent as
                        RectTransform;

                    cloneRecord.targetParentPath =
                        ResolvePersistentTargetPath(
                            parent);
                    cloneRecord.siblingIndex =
                        rect.GetSiblingIndex();
                    cloneRecord.rects =
                        CaptureCloneLayouts(
                            rect);
                }

                return;
            }

            _dirtyRects.Add(rect);
        }

        private bool TryReparentSelectedAtPointer(
            Vector2 screenPoint,
            bool requireDragging)
        {
            if (_selected == null)
                return false;

            if (requireDragging &&
                _dragMode != DragMode.Move)
                return false;

            string cloneId;

            if (_runtimeCloneOwnerByInstanceId
                    .TryGetValue(
                        _selected.gameObject
                            .GetInstanceID(),
                        out cloneId))
            {
                RectTransform cloneRoot;

                if (!_runtimeCloneRoots.TryGetValue(
                        cloneId,
                        out cloneRoot) ||
                    cloneRoot != _selected)
                    return false;
            }

            var targetParent =
                PickTopmostRectTransform(
                    screenPoint,
                    _selected);

            if (targetParent == null ||
                targetParent == _selected ||
                targetParent.IsChildOf(
                    _selected))
                return false;

            if (IsRuntimeCloneOrChild(
                    targetParent))
                return false;

            var oldParent =
                _selected.parent as
                RectTransform;

            if (oldParent == null ||
                oldParent == targetParent)
                return false;

            var oldSiblingIndex =
                _selected.GetSiblingIndex();

            var oldLayout =
                requireDragging &&
                _dragUndoLayout != null
                    ? _dragUndoLayout
                    : CaptureLayout(
                        _selected,
                        string.Empty);

            if (!IsRuntimeCloneOrChild(
                    _selected))
            {
                GetPersistentOriginalPath(
                    _selected);

                GetPersistentOriginalPath(
                    targetParent);
            }

            PushUndo(
                new UndoRecord
                {
                    kind = UndoKind.Hierarchy,
                    target = _selected,
                    hierarchyParent =
                        oldParent,
                    siblingIndex =
                        oldSiblingIndex,
                    layout =
                        oldLayout
                });

            _selected.SetParent(
                targetParent,
                true);

            _selected.SetAsLastSibling();

            UpdateHierarchyPersistence(
                _selected);

            return true;
        }

        private bool TryNudgeSelected()
        {
            if (_selected == null)
                return false;

            var pixelStep =
                ShiftHeld() ? 5f : 1f;

            var screenDelta =
                Vector2.zero;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
                screenDelta.x -= pixelStep;

            if (Input.GetKeyDown(KeyCode.RightArrow))
                screenDelta.x += pixelStep;

            if (Input.GetKeyDown(KeyCode.DownArrow))
                screenDelta.y -= pixelStep;

            if (Input.GetKeyDown(KeyCode.UpArrow))
                screenDelta.y += pixelStep;

            if (screenDelta == Vector2.zero)
                return false;

            var parentDelta =
                ScreenPixelDeltaToParentDelta(
                    _selected,
                    screenDelta);

            if (parentDelta == Vector2.zero)
                return true;

            var previousLayout =
                CaptureLayout(
                    _selected,
                    string.Empty);

            _selected.anchoredPosition +=
                parentDelta;

            PushUndo(
                new UndoRecord
                {
                    kind = UndoKind.Transform,
                    target = _selected,
                    layout = previousLayout
                });

            MarkSelectedDirty();
            return true;
        }

        private static Vector2 ScreenPixelDeltaToParentDelta(
            RectTransform rect,
            Vector2 screenPixelDelta)
        {
            if (rect == null)
                return Vector2.zero;

            var parent =
                rect.parent as RectTransform;

            if (parent == null)
                return Vector2.zero;

            var canvas =
                rect.GetComponentInParent<Canvas>();

            var camera =
                GetCanvasCamera(canvas);

            var screenOrigin =
                RectTransformUtility.WorldToScreenPoint(
                    camera,
                    rect.position);

            Vector2 localOrigin;
            Vector2 localTarget;

            if (!RectTransformUtility
                    .ScreenPointToLocalPointInRectangle(
                        parent,
                        screenOrigin,
                        camera,
                        out localOrigin))
                return Vector2.zero;

            if (!RectTransformUtility
                    .ScreenPointToLocalPointInRectangle(
                        parent,
                        screenOrigin + screenPixelDelta,
                        camera,
                        out localTarget))
                return Vector2.zero;

            return localTarget - localOrigin;
        }

        private void BeginDrag(
            DragMode mode,
            Vector2 mouseScreenPosition)
        {
            if (_selected == null)
                return;

            var parentRect = _selected.parent as RectTransform;
            if (parentRect == null)
                return;

            Vector2 parentPoint;
            if (!ScreenToParentPoint(
                    _selected,
                    parentRect,
                    mouseScreenPosition,
                    out parentPoint))
                return;

            _dragMode = mode;
            _dragUndoLayout =
                CaptureLayout(
                    _selected,
                    string.Empty);
            _dragStartParentPoint = parentPoint;
            _dragStartAnchoredPosition =
                _selected.anchoredPosition;
            _dragStartOffsetMin = _selected.offsetMin;
            _dragStartOffsetMax = _selected.offsetMax;
            _dragStartWidth = _selected.rect.width;
            _dragStartHeight = _selected.rect.height;
            _dragStartParent =
                _selected.parent as RectTransform;
            _dragStartSiblingIndex =
                _selected.GetSiblingIndex();

            if (!IsRuntimeCloneOrChild(
                    _selected))
            {
                GetPersistentOriginalPath(
                    _selected);
            }
        }

        private void UpdateActiveDrag()
        {
            if (_selected == null)
            {
                _dragUndoLayout = null;
                _dragMode = DragMode.None;
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                var reparented =
                    AltHeld() &&
                    TryReparentSelectedAtPointer(
                        Input.mousePosition,
                        true);

                if (!reparented)
                {
                    var currentLayout =
                        CaptureLayout(
                            _selected,
                            string.Empty);

                    if (!LayoutEquals(
                            _dragUndoLayout,
                            currentLayout))
                    {
                        PushUndo(
                            new UndoRecord
                            {
                                kind = UndoKind.Transform,
                                target = _selected,
                                layout = _dragUndoLayout
                            });
                    }

                    MarkSelectedDirty();
                }

                _dragUndoLayout = null;
                _dragStartParent = null;
                _dragStartSiblingIndex = 0;
                _dragMode = DragMode.None;
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                _dragUndoLayout = null;
                _dragStartParent = null;
                _dragStartSiblingIndex = 0;
                _dragMode = DragMode.None;
                return;
            }

            var parentRect = _selected.parent as RectTransform;
            if (parentRect == null)
            {
                _dragUndoLayout = null;
                _dragMode = DragMode.None;
                return;
            }

            Vector2 currentParentPoint;
            if (!ScreenToParentPoint(
                    _selected,
                    parentRect,
                    Input.mousePosition,
                    out currentParentPoint))
                return;

            var delta =
                currentParentPoint - _dragStartParentPoint;

            if (_dragMode == DragMode.Move)
            {
                if (ShiftHeld())
                {
                    // Comportamiento de editor: Mayus restringe el movimiento
                    // al eje dominante para mantener una línea recta.
                    if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                        delta.y = 0f;
                    else
                        delta.x = 0f;
                }

                _selected.anchoredPosition =
                    _dragStartAnchoredPosition + delta;
                return;
            }

            ApplyResize(delta);
        }

        private void RestoreDragStartValues()
        {
            if (_selected == null)
                return;

            if (_dragMode == DragMode.Move)
            {
                _selected.anchoredPosition =
                    _dragStartAnchoredPosition;
                return;
            }

            _selected.offsetMin = _dragStartOffsetMin;
            _selected.offsetMax = _dragStartOffsetMax;
        }

        private bool ApplyManagedLayoutSize(
            bool resizeWidth,
            bool resizeHeight,
            float targetWidth,
            float targetHeight)
        {
            if (_selected == null)
                return false;

            var parentRect =
                _selected.parent as RectTransform;

            var parentLayout =
                parentRect != null
                    ? parentRect.GetComponent<LayoutGroup>()
                    : null;

            var horizontalOrVertical =
                parentLayout as
                    HorizontalOrVerticalLayoutGroup;

            var grid =
                parentLayout as GridLayoutGroup;

            var layoutElement =
                _selected.GetComponent<LayoutElement>();

            var contentSizeFitter =
                _selected.GetComponent<ContentSizeFitter>();

            var widthControlled =
                resizeWidth &&
                horizontalOrVertical != null &&
                horizontalOrVertical.enabled &&
                horizontalOrVertical.childControlWidth;

            var heightControlled =
                resizeHeight &&
                horizontalOrVertical != null &&
                horizontalOrVertical.enabled &&
                horizontalOrVertical.childControlHeight;

            var genericLayoutControlled =
                parentLayout != null &&
                parentLayout.enabled &&
                horizontalOrVertical == null;

            var needsLayoutElement =
                layoutElement != null ||
                widthControlled ||
                heightControlled ||
                genericLayoutControlled;

            var changed =
                false;

            if (needsLayoutElement)
            {
                if (layoutElement == null)
                {
                    layoutElement =
                        _selected.gameObject
                            .AddComponent<LayoutElement>();

                    // Al crearlo partimos del tamaño real que ya tenía el
                    // elemento para que el primer frame no salte de tamaño.
                    layoutElement.minWidth =
                        _dragStartWidth;
                    layoutElement.minHeight =
                        _dragStartHeight;
                    layoutElement.preferredWidth =
                        _dragStartWidth;
                    layoutElement.preferredHeight =
                        _dragStartHeight;
                    layoutElement.flexibleWidth = 0f;
                    layoutElement.flexibleHeight = 0f;
                    changed = true;
                }

                // GridLayoutGroup y otros LayoutGroup que no admiten tamaño
                // individual fuerzan todos sus hijos. Para un ajuste manual de
                // un único control hay que sacarlo del cálculo del layout.
                if (genericLayoutControlled)
                {
                    if (!layoutElement.ignoreLayout)
                    {
                        layoutElement.ignoreLayout = true;
                        changed = true;
                    }
                }
                else if (!layoutElement.ignoreLayout)
                {
                    if (resizeWidth &&
                        (widthControlled ||
                         layoutElement != null))
                    {
                        var width =
                            Mathf.Max(
                                MinimumWidth,
                                targetWidth);

                        layoutElement.minWidth =
                            width;
                        layoutElement.preferredWidth =
                            width;
                        layoutElement.flexibleWidth =
                            0f;
                        changed = true;
                    }

                    if (resizeHeight &&
                        (heightControlled ||
                         layoutElement != null))
                    {
                        var height =
                            Mathf.Max(
                                MinimumHeight,
                                targetHeight);

                        layoutElement.minHeight =
                            height;
                        layoutElement.preferredHeight =
                            height;
                        layoutElement.flexibleHeight =
                            0f;
                        changed = true;
                    }
                }
            }

            // ContentSizeFitter puede reescribir el RectTransform aunque el
            // padre no tenga LayoutGroup. Solo liberamos el eje que el usuario
            // está redimensionando manualmente.
            if (contentSizeFitter != null &&
                contentSizeFitter.enabled)
            {
                if (resizeWidth &&
                    contentSizeFitter.horizontalFit !=
                        ContentSizeFitter.FitMode.Unconstrained)
                {
                    contentSizeFitter.horizontalFit =
                        ContentSizeFitter.FitMode.Unconstrained;
                    changed = true;
                }

                if (resizeHeight &&
                    contentSizeFitter.verticalFit !=
                        ContentSizeFitter.FitMode.Unconstrained)
                {
                    contentSizeFitter.verticalFit =
                        ContentSizeFitter.FitMode.Unconstrained;
                    changed = true;
                }
            }

            if (changed &&
                parentRect != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(
                    parentRect);
            }

            return changed;
        }

        private void ApplyResize(Vector2 delta)
        {
            if (_selected == null)
                return;

            var left =
                _dragMode == DragMode.Left ||
                _dragMode == DragMode.TopLeft ||
                _dragMode == DragMode.BottomLeft;

            var right =
                _dragMode == DragMode.Right ||
                _dragMode == DragMode.TopRight ||
                _dragMode == DragMode.BottomRight;

            var top =
                _dragMode == DragMode.Top ||
                _dragMode == DragMode.TopLeft ||
                _dragMode == DragMode.TopRight;

            var bottom =
                _dragMode == DragMode.Bottom ||
                _dragMode == DragMode.BottomLeft ||
                _dragMode == DragMode.BottomRight;

            var resizeDelta = delta;
            var proportionalWidthDelta = 0f;
            var proportionalHeightDelta = 0f;

            if (ShiftHeld())
            {
                var startWidth =
                    Mathf.Max(
                        MinimumWidth,
                        _dragStartWidth);

                var startHeight =
                    Mathf.Max(
                        MinimumHeight,
                        _dragStartHeight);

                var minimumScale =
                    Mathf.Max(
                        MinimumWidth / startWidth,
                        MinimumHeight / startHeight);

                var horizontal =
                    left || right;

                var vertical =
                    top || bottom;

                var requestedWidthDelta =
                    right
                        ? resizeDelta.x
                        : left
                            ? -resizeDelta.x
                            : 0f;

                var requestedHeightDelta =
                    top
                        ? resizeDelta.y
                        : bottom
                            ? -resizeDelta.y
                            : 0f;

                if (horizontal && vertical)
                {
                    var scaleX =
                        (startWidth + requestedWidthDelta) /
                        startWidth;

                    var scaleY =
                        (startHeight + requestedHeightDelta) /
                        startHeight;

                    var scale =
                        Mathf.Abs(scaleX - 1f) >=
                        Mathf.Abs(scaleY - 1f)
                            ? scaleX
                            : scaleY;

                    scale =
                        Mathf.Max(
                            minimumScale,
                            scale);

                    var widthDelta =
                        startWidth * scale -
                        startWidth;

                    var heightDelta =
                        startHeight * scale -
                        startHeight;

                    resizeDelta.x =
                        right
                            ? widthDelta
                            : -widthDelta;

                    resizeDelta.y =
                        top
                            ? heightDelta
                            : -heightDelta;
                }
                else if (horizontal)
                {
                    var scale =
                        Mathf.Max(
                            minimumScale,
                            (startWidth +
                             requestedWidthDelta) /
                            startWidth);

                    proportionalWidthDelta =
                        startWidth * scale -
                        startWidth;

                    proportionalHeightDelta =
                        startHeight * scale -
                        startHeight;

                    resizeDelta.x =
                        right
                            ? proportionalWidthDelta
                            : -proportionalWidthDelta;
                }
                else if (vertical)
                {
                    var scale =
                        Mathf.Max(
                            minimumScale,
                            (startHeight +
                             requestedHeightDelta) /
                            startHeight);

                    proportionalWidthDelta =
                        startWidth * scale -
                        startWidth;

                    proportionalHeightDelta =
                        startHeight * scale -
                        startHeight;

                    resizeDelta.y =
                        top
                            ? proportionalHeightDelta
                            : -proportionalHeightDelta;
                }
            }

            if (left)
            {
                var maximumPositiveDelta =
                    _dragStartWidth - MinimumWidth;

                if (resizeDelta.x > maximumPositiveDelta)
                    resizeDelta.x = maximumPositiveDelta;
            }
            else if (right)
            {
                var minimumDelta =
                    MinimumWidth - _dragStartWidth;

                if (resizeDelta.x < minimumDelta)
                    resizeDelta.x = minimumDelta;
            }

            if (bottom)
            {
                var maximumPositiveDelta =
                    _dragStartHeight - MinimumHeight;

                if (resizeDelta.y > maximumPositiveDelta)
                    resizeDelta.y = maximumPositiveDelta;
            }
            else if (top)
            {
                var minimumDelta =
                    MinimumHeight - _dragStartHeight;

                if (resizeDelta.y < minimumDelta)
                    resizeDelta.y = minimumDelta;
            }

            var resizeWidth =
                left || right ||
                (ShiftHeld() &&
                 (top || bottom));

            var resizeHeight =
                top || bottom ||
                (ShiftHeld() &&
                 (left || right));

            var targetWidth =
                _dragStartWidth;

            if (left)
                targetWidth -= resizeDelta.x;
            else if (right)
                targetWidth += resizeDelta.x;
            else if (ShiftHeld() &&
                     (top || bottom))
                targetWidth +=
                    proportionalWidthDelta;

            var targetHeight =
                _dragStartHeight;

            if (bottom)
                targetHeight -= resizeDelta.y;
            else if (top)
                targetHeight += resizeDelta.y;
            else if (ShiftHeld() &&
                     (left || right))
                targetHeight +=
                    proportionalHeightDelta;

            ApplyManagedLayoutSize(
                resizeWidth,
                resizeHeight,
                targetWidth,
                targetHeight);

            var offsetMin = _dragStartOffsetMin;
            var offsetMax = _dragStartOffsetMax;

            if (left)
                offsetMin.x += resizeDelta.x;

            if (right)
                offsetMax.x += resizeDelta.x;

            if (bottom)
                offsetMin.y += resizeDelta.y;

            if (top)
                offsetMax.y += resizeDelta.y;

            if (ShiftHeld())
            {
                // Con un tirador lateral, la dimension perpendicular crece o
                // decrece alrededor del centro para conservar la proporcion.
                if ((left || right) && !top && !bottom)
                {
                    offsetMin.y -=
                        proportionalHeightDelta * 0.5f;

                    offsetMax.y +=
                        proportionalHeightDelta * 0.5f;
                }
                else if ((top || bottom) && !left && !right)
                {
                    offsetMin.x -=
                        proportionalWidthDelta * 0.5f;

                    offsetMax.x +=
                        proportionalWidthDelta * 0.5f;
                }
            }

            _selected.offsetMin = offsetMin;
            _selected.offsetMax = offsetMax;

            var parentRect =
                _selected.parent as RectTransform;

            if (parentRect != null)
                LayoutRebuilder.MarkLayoutForRebuild(
                    parentRect);
        }

        private RectTransform PickTopmostRectTransform(
            Vector2 screenPoint)
        {
            return PickTopmostRectTransform(
                screenPoint,
                null);
        }

        private RectTransform PickTopmostRectTransform(
            Vector2 screenPoint,
            RectTransform ignoredRoot)
        {
            var graphics =
                Resources.FindObjectsOfTypeAll<Graphic>();

            Candidate? bestCandidate = null;

            for (var index = 0;
                 index < graphics.Length;
                 ++index)
            {
                var graphic = graphics[index];

                if (graphic == null ||
                    !graphic.enabled ||
                    !graphic.gameObject.activeInHierarchy)
                    continue;

                var rect = graphic.rectTransform;
                var canvas = graphic.canvas;

                if (rect == null || canvas == null)
                    continue;

                if (ignoredRoot != null &&
                    (rect == ignoredRoot ||
                     rect.IsChildOf(
                         ignoredRoot)))
                    continue;

                var camera = GetCanvasCamera(canvas);

                if (!RectTransformUtility.RectangleContainsScreenPoint(
                        rect,
                        screenPoint,
                        camera))
                    continue;

                var candidate = new Candidate
                {
                    Rect = rect,
                    SortingOrder = canvas.sortingOrder,
                    Depth = graphic.canvasRenderer.absoluteDepth
                };

                if (!bestCandidate.HasValue ||
                    IsCandidateAbove(
                        candidate,
                        bestCandidate.Value))
                {
                    bestCandidate = candidate;
                }
            }

            return bestCandidate.HasValue
                ? bestCandidate.Value.Rect
                : null;
        }

        private static bool IsCandidateAbove(
            Candidate candidate,
            Candidate currentBest)
        {
            if (candidate.SortingOrder !=
                currentBest.SortingOrder)
            {
                return candidate.SortingOrder >
                       currentBest.SortingOrder;
            }

            return candidate.Depth > currentBest.Depth;
        }

        private static bool ContainsScreenPoint(
            RectTransform rect,
            Vector2 screenPoint)
        {
            if (rect == null)
                return false;

            var canvas =
                rect.GetComponentInParent<Canvas>();

            return RectTransformUtility.RectangleContainsScreenPoint(
                rect,
                screenPoint,
                GetCanvasCamera(canvas));
        }

        private static bool ScreenToParentPoint(
            RectTransform selected,
            RectTransform parent,
            Vector2 screenPoint,
            out Vector2 parentPoint)
        {
            var canvas =
                selected != null
                    ? selected.GetComponentInParent<Canvas>()
                    : null;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                screenPoint,
                GetCanvasCamera(canvas),
                out parentPoint);
        }

        private static Camera GetCanvasCamera(Canvas canvas)
        {
            if (canvas == null)
                return null;

            if (canvas.renderMode ==
                RenderMode.ScreenSpaceOverlay)
                return null;

            if (canvas.worldCamera != null)
                return canvas.worldCamera;

            return Camera.main;
        }

        private DragMode HitTestHandle(
            Vector2 mouseScreenPosition)
        {
            if (_selected == null)
                return DragMode.None;

            Vector2 bottomLeft;
            Vector2 topLeft;
            Vector2 topRight;
            Vector2 bottomRight;

            if (!TryGetScreenCorners(
                    _selected,
                    out bottomLeft,
                    out topLeft,
                    out topRight,
                    out bottomRight))
                return DragMode.None;

            var top =
                (topLeft + topRight) * 0.5f;
            var bottom =
                (bottomLeft + bottomRight) * 0.5f;
            var left =
                (bottomLeft + topLeft) * 0.5f;
            var right =
                (bottomRight + topRight) * 0.5f;

            if (HandleContains(
                    mouseScreenPosition,
                    topLeft))
                return DragMode.TopLeft;

            if (HandleContains(
                    mouseScreenPosition,
                    topRight))
                return DragMode.TopRight;

            if (HandleContains(
                    mouseScreenPosition,
                    bottomLeft))
                return DragMode.BottomLeft;

            if (HandleContains(
                    mouseScreenPosition,
                    bottomRight))
                return DragMode.BottomRight;

            if (HandleContains(
                    mouseScreenPosition,
                    top))
                return DragMode.Top;

            if (HandleContains(
                    mouseScreenPosition,
                    bottom))
                return DragMode.Bottom;

            if (HandleContains(
                    mouseScreenPosition,
                    left))
                return DragMode.Left;

            if (HandleContains(
                    mouseScreenPosition,
                    right))
                return DragMode.Right;

            return DragMode.None;
        }

        private static bool HandleContains(
            Vector2 mouseScreenPosition,
            Vector2 handleCenter)
        {
            var half = HandleSize * 0.5f;

            var rect = new Rect(
                handleCenter.x - half,
                handleCenter.y - half,
                HandleSize,
                HandleSize);

            return rect.Contains(mouseScreenPosition);
        }

        private static bool TryGetScreenCorners(
            RectTransform rect,
            out Vector2 bottomLeft,
            out Vector2 topLeft,
            out Vector2 topRight,
            out Vector2 bottomRight)
        {
            bottomLeft = Vector2.zero;
            topLeft = Vector2.zero;
            topRight = Vector2.zero;
            bottomRight = Vector2.zero;

            if (rect == null)
                return false;

            var canvas =
                rect.GetComponentInParent<Canvas>();

            var camera = GetCanvasCamera(canvas);
            var worldCorners = new Vector3[4];

            rect.GetWorldCorners(worldCorners);

            bottomLeft =
                RectTransformUtility.WorldToScreenPoint(
                    camera,
                    worldCorners[0]);

            topLeft =
                RectTransformUtility.WorldToScreenPoint(
                    camera,
                    worldCorners[1]);

            topRight =
                RectTransformUtility.WorldToScreenPoint(
                    camera,
                    worldCorners[2]);

            bottomRight =
                RectTransformUtility.WorldToScreenPoint(
                    camera,
                    worldCorners[3]);

            return true;
        }

        private void OpenPropertyEditor()
        {
            if (_selected == null)
                return;

            var graphic =
                _selected.GetComponent<Graphic>();

            string textValue;
            var hasText =
                TryGetEditableText(
                    _selected,
                    out textValue);

            if (graphic == null && !hasText)
                return;

            _propertyTarget = _selected;
            _propertyUndoLayout =
                CaptureLayout(
                    _propertyTarget,
                    string.Empty);

            _propertyHasColor = graphic != null;
            _propertyOriginalColor =
                graphic != null
                    ? graphic.color
                    : Color.white;
            _propertyDraftColor =
                _propertyOriginalColor;

            _propertyHasText = hasText;
            _propertyOriginalText =
                hasText
                    ? textValue ?? string.Empty
                    : string.Empty;
            _propertyDraftText =
                _propertyOriginalText;

            _propertyHasTextStyle =
                TryGetTextStyle(
                    _propertyTarget,
                    out _propertyOriginalFontSize,
                    out _propertyOriginalAutoSize,
                    out _propertyOriginalFontSizeMin,
                    out _propertyOriginalFontSizeMax);

            _propertyDraftFontSize = _propertyOriginalFontSize;
            _propertyDraftAutoSize = _propertyOriginalAutoSize;
            _propertyDraftFontSizeMin = _propertyOriginalFontSizeMin;
            _propertyDraftFontSizeMax = _propertyOriginalFontSizeMax;
            _propertyFontSizeText = FormatEditableFloat(_propertyDraftFontSize);
            _propertyFontMinText = FormatEditableFloat(_propertyDraftFontSizeMin);
            _propertyFontMaxText = FormatEditableFloat(_propertyDraftFontSizeMax);

            _propertyCanOutline = graphic != null;
            var outline =
                _propertyCanOutline
                    ? _propertyTarget.GetComponent<Outline>()
                    : null;

            _propertyOriginalHadOutline = outline != null;
            _propertyOriginalOutlineEnabled =
                outline != null && outline.enabled;
            _propertyDraftOutlineEnabled =
                _propertyOriginalOutlineEnabled;
            _propertyOriginalOutlineColor =
                outline != null
                    ? outline.effectColor
                    : Color.black;
            _propertyDraftOutlineColor =
                _propertyOriginalOutlineColor;
            _propertyOriginalOutlineDistance =
                outline != null
                    ? outline.effectDistance
                    : new Vector2(1f, -1f);
            _propertyDraftOutlineDistance =
                _propertyOriginalOutlineDistance;
            _propertyOriginalOutlineUseGraphicAlpha =
                outline == null || outline.useGraphicAlpha;
            _propertyDraftOutlineUseGraphicAlpha =
                _propertyOriginalOutlineUseGraphicAlpha;
            _propertyOutlineWidthText =
                FormatEditableFloat(
                    GetOutlineWidth(
                        _propertyDraftOutlineDistance));
            _propertyEditingOutlineColor = false;

            SyncPropertyColorEditorFromActiveColor();

            _propertyEyedropperActive = false;
            _propertyEditorOpen = true;
            _propertyFocusTextNextGui =
                _propertyHasText;

            _propertySvTextureHue = -1f;
        }

        private void CommitPropertyEditor()
        {
            if (!_propertyEditorOpen)
                return;

            var target = _propertyTarget;

            if (target != null)
            {
                ApplyPropertyDraft();

                if (PropertyEditorHasChanges())
                {
                    if (_propertyUndoLayout != null)
                    {
                        PushUndo(
                            new UndoRecord
                            {
                                kind = UndoKind.Transform,
                                target = target,
                                layout = _propertyUndoLayout
                            });
                    }

                    if (!IsRuntimeCloneOrChild(target))
                        _dirtyRects.Add(target);
                }
            }

            ClosePropertyEditorState();
        }

        private void CancelPropertyEditor()
        {
            if (!_propertyEditorOpen)
                return;

            if (_propertyTarget != null)
            {
                if (_propertyHasColor)
                {
                    var graphic =
                        _propertyTarget.GetComponent<Graphic>();

                    if (graphic != null)
                        graphic.color = _propertyOriginalColor;
                }

                if (_propertyHasText)
                {
                    SetEditableText(
                        _propertyTarget,
                        _propertyOriginalText);
                }

                if (_propertyHasTextStyle)
                {
                    SetTextStyle(
                        _propertyTarget,
                        _propertyOriginalFontSize,
                        _propertyOriginalAutoSize,
                        _propertyOriginalFontSizeMin,
                        _propertyOriginalFontSizeMax);
                }

                RestoreOriginalOutlineState();
            }

            ClosePropertyEditorState();
        }

        private void RestoreOriginalOutlineState()
        {
            if (!_propertyCanOutline ||
                _propertyTarget == null)
                return;

            var outline =
                _propertyTarget.GetComponent<Outline>();

            if (!_propertyOriginalHadOutline)
            {
                if (outline != null)
                {
                    outline.enabled = false;
                    Destroy(outline);
                }

                return;
            }

            if (outline == null)
                outline = _propertyTarget.gameObject.AddComponent<Outline>();

            outline.enabled = _propertyOriginalOutlineEnabled;
            outline.effectColor = _propertyOriginalOutlineColor;
            outline.effectDistance = _propertyOriginalOutlineDistance;
            outline.useGraphicAlpha = _propertyOriginalOutlineUseGraphicAlpha;
        }

        private void ClosePropertyEditorState()
        {
            _propertyEditorOpen = false;
            _propertyEyedropperActive = false;
            _propertyFocusTextNextGui = false;
            _propertyTarget = null;
            _propertyUndoLayout = null;
            _propertyHasColor = false;
            _propertyHasText = false;
            _propertyOriginalText = string.Empty;
            _propertyDraftText = string.Empty;
            _propertyHasTextStyle = false;
            _propertyFontSizeText = string.Empty;
            _propertyFontMinText = string.Empty;
            _propertyFontMaxText = string.Empty;
            _propertyCanOutline = false;
            _propertyOriginalHadOutline = false;
            _propertyDraftOutlineEnabled = false;
            _propertyOutlineWidthText = string.Empty;
            _propertyEditingOutlineColor = false;
            _propertyHex = string.Empty;
        }

        private bool PropertyEditorHasChanges()
        {
            if (_propertyHasColor &&
                !ColorsApproximatelyEqual(
                    _propertyOriginalColor,
                    _propertyDraftColor))
                return true;

            if (_propertyHasText &&
                !string.Equals(
                    _propertyOriginalText,
                    _propertyDraftText,
                    StringComparison.Ordinal))
                return true;

            if (_propertyHasTextStyle &&
                (!Mathf.Approximately(_propertyOriginalFontSize, _propertyDraftFontSize) ||
                 _propertyOriginalAutoSize != _propertyDraftAutoSize ||
                 !Mathf.Approximately(_propertyOriginalFontSizeMin, _propertyDraftFontSizeMin) ||
                 !Mathf.Approximately(_propertyOriginalFontSizeMax, _propertyDraftFontSizeMax)))
                return true;

            if (_propertyCanOutline)
            {
                if (_propertyOriginalOutlineEnabled != _propertyDraftOutlineEnabled)
                    return true;

                if ((_propertyOriginalHadOutline || _propertyDraftOutlineEnabled) &&
                    (!_propertyOriginalHadOutline ||
                     !ColorsApproximatelyEqual(_propertyOriginalOutlineColor, _propertyDraftOutlineColor) ||
                     !VectorsApproximatelyEqual(_propertyOriginalOutlineDistance, _propertyDraftOutlineDistance) ||
                     _propertyOriginalOutlineUseGraphicAlpha != _propertyDraftOutlineUseGraphicAlpha))
                    return true;
            }

            return false;
        }

        private void ApplyPropertyDraft()
        {
            if (_propertyTarget == null)
                return;

            if (_propertyHasColor)
            {
                var graphic =
                    _propertyTarget.GetComponent<Graphic>();

                if (graphic != null)
                    graphic.color = _propertyDraftColor;
            }

            if (_propertyHasText)
            {
                SetEditableText(
                    _propertyTarget,
                    _propertyDraftText);
            }

            if (_propertyHasTextStyle)
            {
                SetTextStyle(
                    _propertyTarget,
                    _propertyDraftFontSize,
                    _propertyDraftAutoSize,
                    _propertyDraftFontSizeMin,
                    _propertyDraftFontSizeMax);
            }

            ApplyPropertyOutlineDraft();
        }

        private void ApplyPropertyOutlineDraft()
        {
            if (!_propertyCanOutline ||
                _propertyTarget == null)
                return;

            var outline =
                _propertyTarget.GetComponent<Outline>();

            if (!_propertyDraftOutlineEnabled)
            {
                if (outline == null)
                    return;

                if (_propertyOriginalHadOutline)
                {
                    outline.enabled = false;
                    outline.effectColor = _propertyDraftOutlineColor;
                    outline.effectDistance = _propertyDraftOutlineDistance;
                    outline.useGraphicAlpha = _propertyDraftOutlineUseGraphicAlpha;
                }
                else
                {
                    outline.enabled = false;
                    Destroy(outline);
                }

                return;
            }

            if (outline == null)
                outline = _propertyTarget.gameObject.AddComponent<Outline>();

            outline.enabled = true;
            outline.effectColor = _propertyDraftOutlineColor;
            outline.effectDistance = _propertyDraftOutlineDistance;
            outline.useGraphicAlpha = _propertyDraftOutlineUseGraphicAlpha;
        }

        private Color GetActivePropertyColor()
        {
            return _propertyEditingOutlineColor
                ? _propertyDraftOutlineColor
                : _propertyDraftColor;
        }

        private void SetPropertyColorMode(bool outline)
        {
            _propertyEditingOutlineColor =
                outline && _propertyCanOutline;
            SyncPropertyColorEditorFromActiveColor();
        }

        private void SyncPropertyColorEditorFromActiveColor()
        {
            var color = GetActivePropertyColor();

            Color.RGBToHSV(
                color,
                out _propertyHue,
                out _propertySaturation,
                out _propertyValue);

            _propertyHex = ColorToHex(color);
            _propertySvTextureHue = -1f;
        }

        private void SetPropertyDraftColor(
            Color color,
            bool updateHsv,
            bool updateHex)
        {
            color.r = Mathf.Clamp01(color.r);
            color.g = Mathf.Clamp01(color.g);
            color.b = Mathf.Clamp01(color.b);
            color.a = Mathf.Clamp01(color.a);

            if (_propertyEditingOutlineColor)
                _propertyDraftOutlineColor = color;
            else
                _propertyDraftColor = color;

            if (updateHsv)
            {
                var previousHue = _propertyHue;

                Color.RGBToHSV(
                    color,
                    out _propertyHue,
                    out _propertySaturation,
                    out _propertyValue);

                if (!Mathf.Approximately(previousHue, _propertyHue))
                    _propertySvTextureHue = -1f;
            }

            if (updateHex)
                _propertyHex = ColorToHex(color);

            ApplyPropertyDraft();
        }

        private void ApplyPropertyHsvColor()
        {
            var active = GetActivePropertyColor();
            var color =
                Color.HSVToRGB(
                    Mathf.Repeat(_propertyHue, 1f),
                    Mathf.Clamp01(_propertySaturation),
                    Mathf.Clamp01(_propertyValue));

            color.a = active.a;

            SetPropertyDraftColor(
                color,
                false,
                true);
        }

        private void SamplePropertyColorAtPointer(
            Vector2 screenPoint)
        {
            if (!_propertyEditorOpen ||
                !_propertyHasColor)
                return;

            var picked =
                PickTopmostRectTransform(
                    screenPoint);

            if (picked == null)
                return;

            var graphic =
                picked.GetComponent<Graphic>();

            if (graphic == null)
                return;

            SetPropertyDraftColor(
                graphic.color,
                true,
                true);

            _propertyEyedropperActive = false;
        }

        private bool IsMouseOverPropertyEditor()
        {
            if (!_propertyEditorOpen)
                return false;

            return GetPropertyEditorRect()
                .Contains(
                    ScreenToGuiPoint(
                        Input.mousePosition));
        }

        private Rect GetPropertyEditorRect()
        {
            var height = 442f;

            if (_propertyHasText)
                height += 58f;
            if (_propertyHasTextStyle)
                height += 42f;
            if (_propertyCanOutline)
                height += 76f;

            height = Mathf.Min(
                height,
                Mathf.Max(300f, Screen.height - 88f));

            var width = 520f;

            return new Rect(
                Mathf.Max(
                    8f,
                    Screen.width - width - 18f),
                72f,
                width,
                height);
        }

        private void DrawPropertyEditor()
        {
            if (!_propertyEditorOpen ||
                _propertyTarget == null)
                return;

            var panel = GetPropertyEditorRect();
            var oldColor = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, .94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var x = panel.x + 16f;
            var y = panel.y + 12f;
            var innerWidth = panel.width - 32f;

            GUI.Label(
                new Rect(x, y, innerWidth, 24f),
                "PROPIEDADES  |  ENTER: aceptar  |  ESC: cancelar");

            y += 28f;

            GUI.Label(
                new Rect(x, y, innerWidth, 20f),
                _propertyTarget.name);

            y += 26f;

            if (_propertyHasText)
            {
                GUI.Label(new Rect(x, y, 70f, 22f), "Texto");
                GUI.SetNextControlName("UP_UI_DESIGNER_TEXT");

                var nextText =
                    GUI.TextField(
                        new Rect(x + 70f, y, innerWidth - 70f, 24f),
                        _propertyDraftText ?? string.Empty);

                if (!string.Equals(
                        nextText,
                        _propertyDraftText,
                        StringComparison.Ordinal))
                {
                    _propertyDraftText = nextText ?? string.Empty;
                    ApplyPropertyDraft();
                }

                if (_propertyFocusTextNextGui)
                {
                    _propertyFocusTextNextGui = false;
                    GUI.FocusControl("UP_UI_DESIGNER_TEXT");
                }

                y += 36f;
            }

            if (_propertyHasTextStyle)
            {
                GUI.Label(new Rect(x, y, 62f, 22f), "Fuente");

                GUI.Label(new Rect(x + 64f, y, 56f, 22f), "Tamaño");
                var nextSizeText =
                    GUI.TextField(
                        new Rect(x + 118f, y, 58f, 24f),
                        _propertyFontSizeText ?? string.Empty);
                if (nextSizeText != _propertyFontSizeText)
                {
                    _propertyFontSizeText = nextSizeText;
                    float parsed;
                    if (TryParseEditableFloat(nextSizeText, out parsed))
                    {
                        _propertyDraftFontSize = Mathf.Clamp(parsed, 1f, 300f);
                        ApplyPropertyDraft();
                    }
                }

                var nextAuto =
                    GUI.Toggle(
                        new Rect(x + 188f, y, 104f, 24f),
                        _propertyDraftAutoSize,
                        "Autoajuste");
                if (nextAuto != _propertyDraftAutoSize)
                {
                    _propertyDraftAutoSize = nextAuto;
                    ApplyPropertyDraft();
                }

                GUI.Label(new Rect(x + 300f, y, 30f, 22f), "Min");
                var nextMinText =
                    GUI.TextField(
                        new Rect(x + 332f, y, 54f, 24f),
                        _propertyFontMinText ?? string.Empty);
                if (nextMinText != _propertyFontMinText)
                {
                    _propertyFontMinText = nextMinText;
                    float parsed;
                    if (TryParseEditableFloat(nextMinText, out parsed))
                    {
                        _propertyDraftFontSizeMin = Mathf.Clamp(parsed, 0f, 300f);
                        ApplyPropertyDraft();
                    }
                }

                GUI.Label(new Rect(x + 394f, y, 32f, 22f), "Max");
                var nextMaxText =
                    GUI.TextField(
                        new Rect(x + 426f, y, 62f, 24f),
                        _propertyFontMaxText ?? string.Empty);
                if (nextMaxText != _propertyFontMaxText)
                {
                    _propertyFontMaxText = nextMaxText;
                    float parsed;
                    if (TryParseEditableFloat(nextMaxText, out parsed))
                    {
                        _propertyDraftFontSizeMax = Mathf.Clamp(parsed, 0f, 300f);
                        ApplyPropertyDraft();
                    }
                }

                y += 36f;
            }

            if (_propertyCanOutline)
            {
                GUI.Label(new Rect(x, y, 70f, 22f), "Contorno");

                var nextOutline =
                    GUI.Toggle(
                        new Rect(x + 72f, y, 76f, 24f),
                        _propertyDraftOutlineEnabled,
                        "Activo");
                if (nextOutline != _propertyDraftOutlineEnabled)
                {
                    _propertyDraftOutlineEnabled = nextOutline;
                    ApplyPropertyDraft();
                }

                GUI.Label(new Rect(x + 158f, y, 52f, 22f), "Grosor");
                var nextWidthText =
                    GUI.TextField(
                        new Rect(x + 210f, y, 56f, 24f),
                        _propertyOutlineWidthText ?? string.Empty);
                if (nextWidthText != _propertyOutlineWidthText)
                {
                    _propertyOutlineWidthText = nextWidthText;
                    float parsed;
                    if (TryParseEditableFloat(nextWidthText, out parsed))
                    {
                        SetDraftOutlineWidth(Mathf.Clamp(parsed, 0f, 20f));
                        ApplyPropertyDraft();
                    }
                }

                if (GUI.Button(
                        new Rect(x + 280f, y, 98f, 24f),
                        _propertyEditingOutlineColor ? "OBJETO" : "OBJETO ✓"))
                {
                    SetPropertyColorMode(false);
                }

                if (GUI.Button(
                        new Rect(x + 386f, y, 102f, 24f),
                        _propertyEditingOutlineColor ? "BORDE ✓" : "BORDE"))
                {
                    SetPropertyColorMode(true);
                }

                y += 36f;

                var nextUseAlpha =
                    GUI.Toggle(
                        new Rect(x + 72f, y, 150f, 24f),
                        _propertyDraftOutlineUseGraphicAlpha,
                        "Usar alfa del objeto");
                if (nextUseAlpha != _propertyDraftOutlineUseGraphicAlpha)
                {
                    _propertyDraftOutlineUseGraphicAlpha = nextUseAlpha;
                    ApplyPropertyDraft();
                }

                GUI.Label(
                    new Rect(x + 238f, y, innerWidth - 238f, 24f),
                    _propertyEditingOutlineColor
                        ? "El selector de color edita el contorno"
                        : "El selector de color edita el objeto");

                y += 34f;
            }

            if (!_propertyHasColor)
            {
                GUI.Label(
                    new Rect(x, y, innerWidth, 24f),
                    "Este elemento no tiene un Graphic editable.");

                GUI.color = oldColor;
                return;
            }

            GUI.Label(new Rect(x, y, 50f, 22f), "Color");

            var activeColor = GetActivePropertyColor();
            var previewRect = new Rect(x + 52f, y, 64f, 24f);
            GUI.color = activeColor;
            GUI.DrawTexture(previewRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var probeText =
                _propertyEyedropperActive
                    ? "PROBETA ACTIVA"
                    : "PROBETA";

            if (GUI.Button(
                    new Rect(x + 128f, y, 132f, 24f),
                    probeText))
            {
                _propertyEyedropperActive = !_propertyEyedropperActive;
            }

            GUI.Label(
                new Rect(x + 270f, y, innerWidth - 270f, 24f),
                _propertyEyedropperActive
                    ? "Haz clic en otro elemento UI"
                    : "Copia su color exacto");

            y += 34f;

            var svRect = new Rect(x, y, 250f, 190f);
            GUI.DrawTexture(svRect, GetPropertySvTexture());

            var hueRect =
                new Rect(svRect.xMax + 10f, y, 24f, svRect.height);
            GUI.DrawTexture(hueRect, GetPropertyHueTexture());

            HandlePropertySvInput(svRect);
            HandlePropertyHueInput(hueRect);
            DrawPropertySvMarker(svRect);
            DrawPropertyHueMarker(hueRect);

            var sliderX = hueRect.xMax + 18f;
            var sliderWidth = panel.xMax - 16f - sliderX;
            var color = GetActivePropertyColor();

            color.r = DrawPropertyChannel(sliderX, y + 0f, sliderWidth, "R", color.r);
            color.g = DrawPropertyChannel(sliderX, y + 42f, sliderWidth, "G", color.g);
            color.b = DrawPropertyChannel(sliderX, y + 84f, sliderWidth, "B", color.b);
            color.a = DrawPropertyChannel(sliderX, y + 126f, sliderWidth, "A", color.a);

            if (!ColorsApproximatelyEqual(
                    color,
                    GetActivePropertyColor()))
            {
                SetPropertyDraftColor(color, true, true);
            }

            y += 202f;

            GUI.Label(new Rect(x, y, 36f, 24f), "Hex");

            var nextHex =
                GUI.TextField(
                    new Rect(x + 42f, y, 136f, 24f),
                    _propertyHex ?? string.Empty);

            if (!string.Equals(
                    nextHex,
                    _propertyHex,
                    StringComparison.Ordinal))
            {
                _propertyHex = nextHex ?? string.Empty;

                Color parsed;
                if (TryParseHexColor(_propertyHex, out parsed))
                    SetPropertyDraftColor(parsed, true, false);
            }

            GUI.Label(
                new Rect(x + 190f, y, innerWidth - 190f, 24f),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "H {0:0}°   S {1:0}%   V {2:0}%",
                    _propertyHue * 360f,
                    _propertySaturation * 100f,
                    _propertyValue * 100f));

            y += 34f;

            GUI.Label(
                new Rect(x, y, innerWidth, 24f),
                _propertyEyedropperActive
                    ? "PROBETA: clic fuera de este panel para muestrear."
                    : "ENTER confirma todas las propiedades. ESC restaura los valores anteriores.");

            GUI.color = oldColor;
        }

        private float DrawPropertyChannel(
            float x,
            float y,
            float width,
            string label,
            float value)
        {
            GUI.Label(
                new Rect(
                    x,
                    y,
                    24f,
                    20f),
                label);

            var next =
                GUI.HorizontalSlider(
                    new Rect(
                        x + 24f,
                        y + 4f,
                        Mathf.Max(20f, width - 60f),
                        18f),
                    value,
                    0f,
                    1f);

            GUI.Label(
                new Rect(
                    x + width - 34f,
                    y,
                    34f,
                    20f),
                Mathf.RoundToInt(
                    next * 255f)
                    .ToString(
                        CultureInfo.InvariantCulture));

            return next;
        }

        private void HandlePropertySvInput(
            Rect rect)
        {
            var current = Event.current;

            if (current == null ||
                (current.type != EventType.MouseDown &&
                 current.type != EventType.MouseDrag) ||
                current.button != 0 ||
                !rect.Contains(current.mousePosition))
                return;

            _propertySaturation =
                Mathf.Clamp01(
                    (current.mousePosition.x -
                     rect.x) /
                    rect.width);

            _propertyValue =
                1f -
                Mathf.Clamp01(
                    (current.mousePosition.y -
                     rect.y) /
                    rect.height);

            ApplyPropertyHsvColor();
            current.Use();
        }

        private void HandlePropertyHueInput(
            Rect rect)
        {
            var current = Event.current;

            if (current == null ||
                (current.type != EventType.MouseDown &&
                 current.type != EventType.MouseDrag) ||
                current.button != 0 ||
                !rect.Contains(current.mousePosition))
                return;

            _propertyHue =
                1f -
                Mathf.Clamp01(
                    (current.mousePosition.y -
                     rect.y) /
                    rect.height);

            _propertySvTextureHue = -1f;
            ApplyPropertyHsvColor();
            current.Use();
        }

        private void DrawPropertySvMarker(
            Rect rect)
        {
            var point =
                new Vector2(
                    rect.x +
                    _propertySaturation *
                    rect.width,
                    rect.y +
                    (1f - _propertyValue) *
                    rect.height);

            var old = GUI.color;
            GUI.color = Color.black;

            GUI.DrawTexture(
                new Rect(
                    point.x - 5f,
                    point.y - 1f,
                    10f,
                    2f),
                Texture2D.whiteTexture);

            GUI.DrawTexture(
                new Rect(
                    point.x - 1f,
                    point.y - 5f,
                    2f,
                    10f),
                Texture2D.whiteTexture);

            GUI.color = Color.white;

            GUI.DrawTexture(
                new Rect(
                    point.x - 3f,
                    point.y - 1f,
                    6f,
                    2f),
                Texture2D.whiteTexture);

            GUI.DrawTexture(
                new Rect(
                    point.x - 1f,
                    point.y - 3f,
                    2f,
                    6f),
                Texture2D.whiteTexture);

            GUI.color = old;
        }

        private void DrawPropertyHueMarker(
            Rect rect)
        {
            var y =
                rect.y +
                (1f - _propertyHue) *
                rect.height;

            var old = GUI.color;

            GUI.color = Color.black;
            GUI.DrawTexture(
                new Rect(
                    rect.x - 3f,
                    y - 2f,
                    rect.width + 6f,
                    4f),
                Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.DrawTexture(
                new Rect(
                    rect.x - 2f,
                    y - 1f,
                    rect.width + 4f,
                    2f),
                Texture2D.whiteTexture);

            GUI.color = old;
        }

        private Texture2D GetPropertyHueTexture()
        {
            if (_propertyHueTexture != null)
                return _propertyHueTexture;

            const int width = 8;
            const int height = 256;

            _propertyHueTexture =
                new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false);

            _propertyHueTexture.name =
                "UP UI Designer Hue";

            _propertyHueTexture.hideFlags =
                HideFlags.HideAndDontSave;

            _propertyHueTexture.wrapMode =
                TextureWrapMode.Clamp;

            var pixels =
                new Color[width * height];

            for (var y = 0; y < height; ++y)
            {
                var hue =
                    y /
                    (float)(height - 1);

                var color =
                    Color.HSVToRGB(
                        hue,
                        1f,
                        1f);

                for (var x = 0; x < width; ++x)
                {
                    pixels[y * width + x] =
                        color;
                }
            }

            _propertyHueTexture.SetPixels(
                pixels);

            _propertyHueTexture.Apply(
                false,
                true);

            return _propertyHueTexture;
        }

        private Texture2D GetPropertySvTexture()
        {
            if (_propertySvTexture == null)
            {
                _propertySvTexture =
                    new Texture2D(
                        128,
                        128,
                        TextureFormat.RGBA32,
                        false);

                _propertySvTexture.name =
                    "UP UI Designer Saturation Value";

                _propertySvTexture.hideFlags =
                    HideFlags.HideAndDontSave;

                _propertySvTexture.wrapMode =
                    TextureWrapMode.Clamp;
            }

            if (!Mathf.Approximately(
                    _propertySvTextureHue,
                    _propertyHue))
            {
                RebuildPropertySvTexture();
            }

            return _propertySvTexture;
        }

        private void RebuildPropertySvTexture()
        {
            if (_propertySvTexture == null)
                return;

            var width =
                _propertySvTexture.width;

            var height =
                _propertySvTexture.height;

            var pixels =
                new Color[width * height];

            for (var y = 0; y < height; ++y)
            {
                var value =
                    y /
                    (float)(height - 1);

                for (var x = 0; x < width; ++x)
                {
                    var saturation =
                        x /
                        (float)(width - 1);

                    pixels[y * width + x] =
                        Color.HSVToRGB(
                            _propertyHue,
                            saturation,
                            value);
                }
            }

            _propertySvTexture.SetPixels(
                pixels);

            _propertySvTexture.Apply(
                false,
                false);

            _propertySvTextureHue =
                _propertyHue;
        }

        private void DestroyPropertyEditorTextures()
        {
            if (_propertyHueTexture != null)
            {
                Destroy(
                    _propertyHueTexture);

                _propertyHueTexture = null;
            }

            if (_propertySvTexture != null)
            {
                Destroy(
                    _propertySvTexture);

                _propertySvTexture = null;
            }
        }

        private static void CaptureAppearance(
            RectTransform rect,
            out bool hasGraphicColor,
            out Color graphicColor,
            out bool hasTextOverride,
            out string textValue)
        {
            hasGraphicColor = false;
            graphicColor = Color.white;
            hasTextOverride = false;
            textValue = string.Empty;

            if (rect == null)
                return;

            var graphic =
                rect.GetComponent<Graphic>();

            if (graphic != null)
            {
                hasGraphicColor = true;
                graphicColor = graphic.color;
            }

            hasTextOverride =
                TryGetEditableText(
                    rect,
                    out textValue);
        }

        private static void ApplyAppearance(
            RectTransform rect,
            bool hasGraphicColor,
            Color graphicColor,
            bool hasTextOverride,
            string textValue)
        {
            if (rect == null)
                return;

            if (hasGraphicColor)
            {
                var graphic =
                    rect.GetComponent<Graphic>();

                if (graphic != null)
                    graphic.color = graphicColor;
            }

            if (hasTextOverride)
            {
                SetEditableText(
                    rect,
                    textValue ?? string.Empty);
            }
        }

        private static bool TryGetEditableText(
            RectTransform rect,
            out string value)
        {
            value = string.Empty;

            if (rect == null)
                return false;

            var tmp =
                rect.GetComponent<TMP_Text>();

            if (tmp != null)
            {
                value =
                    tmp.text ?? string.Empty;
                return true;
            }

            var legacy =
                rect.GetComponent<Text>();

            if (legacy != null)
            {
                value =
                    legacy.text ?? string.Empty;
                return true;
            }

            return false;
        }

        private static bool SetEditableText(
            RectTransform rect,
            string value)
        {
            if (rect == null)
                return false;

            var tmp =
                rect.GetComponent<TMP_Text>();

            if (tmp != null)
            {
                tmp.text =
                    value ?? string.Empty;
                return true;
            }

            var legacy =
                rect.GetComponent<Text>();

            if (legacy != null)
            {
                legacy.text =
                    value ?? string.Empty;
                return true;
            }

            return false;
        }

        private static void CaptureExtendedAppearance(
            RectTransform rect,
            out bool hasOutline,
            out bool outlineEnabled,
            out Color outlineColor,
            out Vector2 outlineDistance,
            out bool outlineUseGraphicAlpha,
            out bool hasTextStyle,
            out float textFontSize,
            out bool textAutoSize,
            out float textFontSizeMin,
            out float textFontSizeMax)
        {
            hasOutline = false;
            outlineEnabled = false;
            outlineColor = Color.black;
            outlineDistance = new Vector2(1f, -1f);
            outlineUseGraphicAlpha = true;
            hasTextStyle = false;
            textFontSize = -1f;
            textAutoSize = false;
            textFontSizeMin = -1f;
            textFontSizeMax = -1f;

            if (rect == null)
                return;

            var outline = rect.GetComponent<Outline>();
            if (outline != null)
            {
                hasOutline = true;
                outlineEnabled = outline.enabled;
                outlineColor = outline.effectColor;
                outlineDistance = outline.effectDistance;
                outlineUseGraphicAlpha = outline.useGraphicAlpha;
            }

            hasTextStyle =
                TryGetTextStyle(
                    rect,
                    out textFontSize,
                    out textAutoSize,
                    out textFontSizeMin,
                    out textFontSizeMax);
        }

        private static void ApplyExtendedAppearance(
            RectTransform rect,
            bool metadataKnown,
            bool hasOutline,
            bool outlineEnabled,
            Color outlineColor,
            Vector2 outlineDistance,
            bool outlineUseGraphicAlpha,
            bool hasTextStyle,
            float textFontSize,
            bool textAutoSize,
            float textFontSizeMin,
            float textFontSizeMax)
        {
            if (rect == null || !metadataKnown)
                return;

            var outline = rect.GetComponent<Outline>();

            if (hasOutline)
            {
                if (outline == null && rect.GetComponent<Graphic>() != null)
                    outline = rect.gameObject.AddComponent<Outline>();

                if (outline != null)
                {
                    outline.enabled = outlineEnabled;
                    outline.effectColor = outlineColor;
                    outline.effectDistance = outlineDistance;
                    outline.useGraphicAlpha = outlineUseGraphicAlpha;
                }
            }
            else if (outline != null)
            {
                outline.enabled = false;
                UnityEngine.Object.Destroy(outline);
            }

            if (hasTextStyle)
            {
                SetTextStyle(
                    rect,
                    textFontSize,
                    textAutoSize,
                    textFontSizeMin,
                    textFontSizeMax);
            }
        }

        private static bool TryGetTextStyle(
            RectTransform rect,
            out float fontSize,
            out bool autoSize,
            out float fontSizeMin,
            out float fontSizeMax)
        {
            fontSize = -1f;
            autoSize = false;
            fontSizeMin = -1f;
            fontSizeMax = -1f;

            if (rect == null)
                return false;

            var tmp = rect.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                fontSize = tmp.fontSize;
                autoSize = tmp.enableAutoSizing;
                fontSizeMin = tmp.fontSizeMin;
                fontSizeMax = tmp.fontSizeMax;
                return true;
            }

            var legacy = rect.GetComponent<Text>();
            if (legacy != null)
            {
                fontSize = legacy.fontSize;
                autoSize = legacy.resizeTextForBestFit;
                fontSizeMin = legacy.resizeTextMinSize;
                fontSizeMax = legacy.resizeTextMaxSize;
                return true;
            }

            return false;
        }

        private static bool SetTextStyle(
            RectTransform rect,
            float fontSize,
            bool autoSize,
            float fontSizeMin,
            float fontSizeMax)
        {
            if (rect == null)
                return false;

            fontSize = Mathf.Clamp(fontSize, 1f, 300f);
            fontSizeMin = Mathf.Clamp(fontSizeMin, 0f, 300f);
            fontSizeMax = Mathf.Clamp(fontSizeMax, 0f, 300f);
            if (fontSizeMax < fontSizeMin)
                fontSizeMax = fontSizeMin;

            var tmp = rect.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.fontSize = fontSize;
                tmp.enableAutoSizing = autoSize;
                tmp.fontSizeMin = fontSizeMin;
                tmp.fontSizeMax = fontSizeMax;
                return true;
            }

            var legacy = rect.GetComponent<Text>();
            if (legacy != null)
            {
                legacy.fontSize = Mathf.RoundToInt(fontSize);
                legacy.resizeTextForBestFit = autoSize;
                legacy.resizeTextMinSize = Mathf.RoundToInt(fontSizeMin);
                legacy.resizeTextMaxSize = Mathf.RoundToInt(fontSizeMax);
                return true;
            }

            return false;
        }

        private static string FormatEditableFloat(float value)
        {
            if (value < 0f)
                return string.Empty;

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryParseEditableFloat(
            string value,
            out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static float GetOutlineWidth(Vector2 distance)
        {
            return Mathf.Max(
                Mathf.Abs(distance.x),
                Mathf.Abs(distance.y));
        }

        private void SetDraftOutlineWidth(float width)
        {
            var xSign =
                Mathf.Abs(_propertyDraftOutlineDistance.x) > .0001f
                    ? Mathf.Sign(_propertyDraftOutlineDistance.x)
                    : 1f;
            var ySign =
                Mathf.Abs(_propertyDraftOutlineDistance.y) > .0001f
                    ? Mathf.Sign(_propertyDraftOutlineDistance.y)
                    : -1f;

            _propertyDraftOutlineDistance =
                new Vector2(xSign * width, ySign * width);
        }

        private static bool VectorsApproximatelyEqual(
            Vector2 a,
            Vector2 b)
        {
            return
                Mathf.Abs(a.x - b.x) < .0001f &&
                Mathf.Abs(a.y - b.y) < .0001f;
        }

        private static bool ColorsApproximatelyEqual(
            Color a,
            Color b)
        {
            return
                Mathf.Abs(a.r - b.r) < .0001f &&
                Mathf.Abs(a.g - b.g) < .0001f &&
                Mathf.Abs(a.b - b.b) < .0001f &&
                Mathf.Abs(a.a - b.a) < .0001f;
        }

        private static string ColorToHex(
            Color color)
        {
            var value =
                (Color32)color;

            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}{3:X2}",
                value.r,
                value.g,
                value.b,
                value.a);
        }

        private static bool TryParseHexColor(
            string value,
            out Color color)
        {
            color = Color.white;

            if (string.IsNullOrEmpty(value))
                return false;

            var normalized =
                value.Trim();

            if (normalized.StartsWith(
                    "#",
                    StringComparison.Ordinal))
            {
                normalized =
                    normalized.Substring(1);
            }

            if (normalized.Length != 6 &&
                normalized.Length != 8)
                return false;

            byte r;
            byte g;
            byte b;
            byte a = 255;

            if (!byte.TryParse(
                    normalized.Substring(0, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out r) ||
                !byte.TryParse(
                    normalized.Substring(2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out g) ||
                !byte.TryParse(
                    normalized.Substring(4, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out b))
                return false;

            if (normalized.Length == 8 &&
                !byte.TryParse(
                    normalized.Substring(6, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out a))
                return false;

            color =
                new Color32(
                    r,
                    g,
                    b,
                    a);

            return true;
        }

        private void OnGUI()
        {
            if (!_designerEnabled)
                return;

            EnsureGuiStyles();

            var oldDepth = GUI.depth;
            var oldColor = GUI.color;

            GUI.depth = -10000;

            DrawHud();

            if (_selected != null)
                DrawSelection();

            if (_propertyEditorOpen)
                DrawPropertyEditor();

            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }

        private void EnsureGuiStyles()
        {
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label);
                _hudStyle.fontSize = 14;
                _hudStyle.fontStyle = FontStyle.Normal;
                _hudStyle.normal.textColor = HudTextColor;
                _hudStyle.alignment = TextAnchor.MiddleLeft;
                _hudStyle.wordWrap = false;
                _hudStyle.clipping = TextClipping.Overflow;
                _hudStyle.padding =
                    new RectOffset(8, 8, 2, 2);
            }

            if (_selectedNameStyle == null)
            {
                _selectedNameStyle =
                    new GUIStyle(GUI.skin.label);

                _selectedNameStyle.fontSize = 14;
                _selectedNameStyle.fontStyle =
                    FontStyle.Normal;

                _selectedNameStyle.normal.textColor =
                    BorderColor;

                _selectedNameStyle.alignment =
                    TextAnchor.MiddleLeft;
                _selectedNameStyle.clipping =
                    TextClipping.Overflow;
            }
        }

        private void DrawHud()
        {
            const float hudX = 8f;
            const float hudY = 8f;
            const float hudHeight = 30f;
            const float pathHeight = 24f;
            const float gap = 2f;

            var hudWidth =
                Mathf.Max(
                    320f,
                    Screen.width - (hudX * 2f));

            var hudRect =
                new Rect(
                    hudX,
                    hudY,
                    hudWidth,
                    hudHeight);

            GUI.color = HudBackgroundColor;
            GUI.DrawTexture(
                hudRect,
                Texture2D.whiteTexture);

            GUI.color = HudTextColor;
            GUI.Label(
                hudRect,
                "UI DESIGNER ON  |  ENTER: propiedades  |  Espacio: usar UI  |  Mayus: eje/proporcion  |  Flechas: 1/5 px  |  RePag/AvPag: delante/detras  |  Alt+clic/arrastrar: cambiar padre  |  Alt+Supr: quitar nodo  |  Ctrl+C/V  |  Ctrl+S  |  Ctrl+Z  |  Supr  |  Ctrl+Alt+F12: salir",
                _hudStyle);

            if (_selected == null)
                return;

            var pathRect =
                new Rect(
                    hudX,
                    hudY + hudHeight + gap,
                    hudWidth,
                    pathHeight);

            GUI.color = HudBackgroundColor;
            GUI.DrawTexture(
                pathRect,
                Texture2D.whiteTexture);

            GUI.color = HudTextColor;

            var nameRect =
                new Rect(
                    hudX + 8f,
                    hudY + hudHeight + gap,
                    hudWidth - 16f,
                    pathHeight);

            GUI.Label(
                nameRect,
                GetHierarchyPath(_selected),
                _selectedNameStyle);
        }

        private void DrawSelection()
        {
            Vector2 bottomLeft;
            Vector2 topLeft;
            Vector2 topRight;
            Vector2 bottomRight;

            if (!TryGetScreenCorners(
                    _selected,
                    out bottomLeft,
                    out topLeft,
                    out topRight,
                    out bottomRight))
                return;

            DrawScreenLine(
                bottomLeft,
                topLeft,
                BorderColor,
                BorderThickness);

            DrawScreenLine(
                topLeft,
                topRight,
                BorderColor,
                BorderThickness);

            DrawScreenLine(
                topRight,
                bottomRight,
                BorderColor,
                BorderThickness);

            DrawScreenLine(
                bottomRight,
                bottomLeft,
                BorderColor,
                BorderThickness);

            DrawHandle(bottomLeft);
            DrawHandle(topLeft);
            DrawHandle(topRight);
            DrawHandle(bottomRight);

            DrawHandle(
                (bottomLeft + topLeft) * 0.5f);

            DrawHandle(
                (topLeft + topRight) * 0.5f);

            DrawHandle(
                (topRight + bottomRight) * 0.5f);

            DrawHandle(
                (bottomRight + bottomLeft) * 0.5f);
        }

        private static void DrawScreenLine(
            Vector2 screenStart,
            Vector2 screenEnd,
            Color color,
            float thickness)
        {
            var start =
                ScreenToGuiPoint(screenStart);

            var end =
                ScreenToGuiPoint(screenEnd);

            var delta = end - start;
            var length = delta.magnitude;

            if (length <= 0.001f)
                return;

            var angle =
                Mathf.Atan2(delta.y, delta.x) *
                Mathf.Rad2Deg;

            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;

            GUI.color = color;

            GUIUtility.RotateAroundPivot(
                angle,
                start);

            GUI.DrawTexture(
                new Rect(
                    start.x,
                    start.y - thickness * 0.5f,
                    length,
                    thickness),
                Texture2D.whiteTexture);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private static void DrawHandle(
            Vector2 screenCenter)
        {
            var guiCenter =
                ScreenToGuiPoint(screenCenter);

            var half = HandleSize * 0.5f;

            var outer =
                new Rect(
                    guiCenter.x - half,
                    guiCenter.y - half,
                    HandleSize,
                    HandleSize);

            var inner =
                new Rect(
                    outer.x + 2f,
                    outer.y + 2f,
                    outer.width - 4f,
                    outer.height - 4f);

            var oldColor = GUI.color;

            GUI.color = HandleBorderColor;
            GUI.DrawTexture(
                outer,
                Texture2D.whiteTexture);

            GUI.color = HandleColor;
            GUI.DrawTexture(
                inner,
                Texture2D.whiteTexture);

            GUI.color = oldColor;
        }

        private static Vector2 ScreenToGuiPoint(
            Vector2 screenPoint)
        {
            return new Vector2(
                screenPoint.x,
                Screen.height - screenPoint.y);
        }

        private static string GetHierarchyPath(
            Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new List<string>();
            var current = transform;

            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join(" / ", names.ToArray());
        }
    }
    // Mientras el diseñador está activo, Escape y ENTER quedan reservados
    // exclusivamente a RuntimeUiDesigner. Cualquier lectura directa del juego
    // recibe false. Cuando el diseñador está desactivado no se altera Input.
    [HarmonyPatch]
    internal static class RuntimeUiDesignerUnityEscapePatch
    {
        private static System.Reflection.MethodBase
            TargetMethod()
        {
            return AccessTools.Method(
                typeof(Input),
                nameof(Input.GetKeyDown),
                new Type[] { typeof(KeyCode) });
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            KeyCode __0,
            ref bool __result)
        {
            var blockEscape =
                __0 == KeyCode.Escape &&
                RuntimeUiDesignerPlugin
                    .BlockEscapeForGame;

            var blockEnter =
                (__0 == KeyCode.Return ||
                 __0 == KeyCode.KeypadEnter) &&
                RuntimeUiDesignerPlugin
                    .BlockEnterForGame;

            if (!blockEscape &&
                !blockEnter)
                return true;

            __result = false;
            return false;
        }
    }

    // El diseñador lee UnityEngine.Input directamente. El juego, en cambio,
    // pasa sus acciones de mapa por InputManager. Mientras el diseñador esta
    // activo, las consultas de teclado y acciones quedan bloqueadas para que
    // flechas, atajos y botones no modifiquen el mapa situado detras de la UI.
    [HarmonyPatch]
    internal static class RuntimeUiDesignerTh20BooleanInputPatch
    {
        private static IEnumerable<System.Reflection.MethodBase>
            TargetMethods()
        {
            var inputType = typeof(TH20.InputManager);
            var keySignature = new Type[] { typeof(KeyCode) };
            var buttonSignature = new Type[] { typeof(int) };

            yield return AccessTools.Method(inputType, "GetKey", keySignature);
            yield return AccessTools.Method(inputType, "GetKeyDown", keySignature);
            yield return AccessTools.Method(inputType, "GetKeyUp", keySignature);
            yield return AccessTools.Method(inputType, "GetButton", buttonSignature);
            yield return AccessTools.Method(inputType, "GetButtonDown", buttonSignature);
            yield return AccessTools.Method(inputType, "GetButtonUp", buttonSignature);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!RuntimeUiDesignerPlugin.BlockGameInput)
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(TH20.InputManager), "GetAxis", new[] { typeof(int) })]
    internal static class RuntimeUiDesignerTh20AxisInputPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref float __result)
        {
            if (!RuntimeUiDesignerPlugin.BlockGameInput)
                return true;

            __result = 0f;
            return false;
        }
    }

    // Esta es la misma senal nativa que emplean el cursor, la seleccion y la
    // rueda para decidir si el puntero pertenece a la UI o a la escena.
    [HarmonyPatch(typeof(TH20.InputManager), "get_IsMouseOverGui")]
    internal static class RuntimeUiDesignerTh20MouseOverUiPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (RuntimeUiDesignerPlugin.BlockGameInput)
                __result = true;
        }
    }

    // Cubre tambien los sistemas que consultan EventSystem directamente en
    // lugar de hacerlo mediante InputManager.
    [HarmonyPatch]
    internal static class RuntimeUiDesignerEventSystemPointerPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(EventSystem),
                nameof(EventSystem.IsPointerOverGameObject),
                Type.EmptyTypes);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (RuntimeUiDesignerPlugin.BlockGameInput)
                __result = true;
        }
    }

}
