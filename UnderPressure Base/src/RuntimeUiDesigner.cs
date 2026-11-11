// Updated: 2026-11-11 - MAYUS arrastra hijos al redimensionar; estilos de TMP_InputField/Dropdown se guardan en V8.
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
    /// - Ctrl + V: pegarlo como copia independiente en el menú actual.
    /// - Ctrl + S: guardar layouts, copias, textos y colores en un fichero externo.
    /// - Ctrl + L: descartar cambios posteriores y recargar el ultimo layout guardado.
    /// - ENTER: abrir/cerrar el editor de propiedades del elemento seleccionado.
    /// - Copiar tamaño / Pegar tamaño: reutilizar ancho y alto entre elementos.
    /// - ENTER permite editar color, texto, tamaño/autoajuste, negrita y contorno.
    /// - El contorno puede activarse/desactivarse en cualquier Graphic seleccionado.
    /// - Probeta: copiar el color exacto de otro elemento UI.
    /// - Mantener Espacio: dejar pasar temporalmente los clics al juego para navegar.
    /// - Con el diseñador activo, flechas y rueda quedan reservadas a la UI; el mapa no recibe esos inputs. Al salir, vuelven al mapa.
    /// - Mayus al mover: restringir el movimiento al eje dominante.
    /// - Mayus al redimensionar: mantener la proporcion y no empujar/recolocar los hijos.
    /// - RePag/Page Up y AvPag/Page Down: cambian orden visual; con Alt recorren padres candidatos bajo el cursor.
    /// - Mantener Alt: muestra en rosa el padre candidato, incluidos RectTransform sin Graphic.
    /// - Alt + clic o Alt + arrastrar: cambiar al padre rosa seleccionado.
    /// - Ctrl + Z: deshacer mover, redimensionar, pegar o eliminar.
    /// - Supr/Delete: eliminar el elemento seleccionado.
    /// - Alt + Supr/Delete: eliminar solo el nodo seleccionado y subir sus hijos al padre.
    /// - Los overrides editados/guardados tienen prioridad y se reaplican si la UI los reconstruye, sin barrer toda la escena.
    /// - Escape con el diseñador activo: cancelar/deseleccionar; nunca llega al juego.
    ///
    /// No modifica prefabs ni assets originales del juego. Los cambios se guardan
    /// como overrides/recetas de copia de UnderPressure y pueden revertirse
    /// eliminando el fichero de layouts.
    /// </summary>
    public sealed class RuntimeUiDesignerPlugin : MonoBehaviour
    {
        private const float HandleSize = 14f;
        private const float BorderThickness = 2f;
        private const float MinimumWidth = 12f;
        private const float MinimumHeight = 12f;
        private const float AutoApplyInterval = 0.35f;
        private const float WorkingStateGuardInterval = 0.05f;
        private const int AutoApplyPassesPerScene = 6;
        private const string LayoutFolderName = "UnderPressure";
        private const string LayoutFileName = "ui_layout_overrides.txt";

        private static readonly Color BorderColor =
            new Color(0.15f, 1f, 0.25f, 1f);

        private static readonly Color ParentCandidateColor =
            new Color(1f, 0.20f, 0.65f, 1f);

        private const float ParentCandidateThickness = 3f;

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
        private RectTransform _reparentCandidate;
        private readonly List<RectTransform> _reparentCandidates =
            new List<RectTransform>();
        private readonly List<RectTransform> _reparentCandidateUniverse =
            new List<RectTransform>();
        private Canvas _reparentCandidateRootCanvas;
        private int _reparentCandidateIndex = -1;
        private Vector2 _reparentCandidatePointer;
        private bool _reparentCandidatePointerValid;
        private int _reparentCandidateSelectedInstanceId;
        private DragMode _dragMode;

        private GameObject _clipboardRoot;
        private GameObject _clipboardTemplate;
        private string _clipboardSourcePath;
        private string _clipboardOriginalName;
        private bool _clipboardOriginalActive;

        private bool _sizeClipboardValid;
        private Vector2 _sizeClipboard;
        private string _propertyWidthText = string.Empty;
        private string _propertyHeightText = string.Empty;

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

        // Estado DESEADO de la sesion. Se carga desde disco al iniciar/Ctrl+L
        // y se actualiza solo por acciones explicitas del diseñador. Nunca se
        // sobreescribe leyendo el estado vivo de una UI reconstruida.
        private readonly Dictionary<string, RectLayoutRecord> _savedLayouts =
            new Dictionary<string, RectLayoutRecord>(StringComparer.Ordinal);

        // Estado ORIGINAL generado por el codigo, capturado antes de aplicar
        // cualquier override. Permite guardar solo diferencias reales.
        private readonly Dictionary<string, RectLayoutRecord> _originalLayouts =
            new Dictionary<string, RectLayoutRecord>(StringComparer.Ordinal);

        // Enlaces al objeto vivo actual. Si un modulo destruye y recrea el
        // control, el enlace cae y se resuelve otra vez por la ruta original.
        private readonly Dictionary<string, RectTransform> _workingLayoutBindings =
            new Dictionary<string, RectTransform>(StringComparer.Ordinal);

        // Estado anterior a la primera edicion posterior al ultimo guardado/carga.
        // Se usa para que Ctrl+L pueda volver tambien al estado de codigo de un
        // elemento que todavia no existia en el fichero guardado.
        private readonly Dictionary<string, ReloadBaselineRecord> _reloadBaselines =
            new Dictionary<string, ReloadBaselineRecord>(StringComparer.Ordinal);

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
        private float _nextWorkingStateGuardTime;
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
        private readonly List<ResizeChildSnapshot> _resizeChildSnapshots =
            new List<ResizeChildSnapshot>();
        private bool _resizeKeepChildrenFixed;

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
        private bool _propertyOriginalBold;
        private bool _propertyDraftBold;
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

            // V8: cada grupo indica si esa propiedad difiere del estado
            // original generado por el codigo. Los grupos no marcados no se
            // serializan ni se reaplican.
            public bool rectTransformOverride;
            public bool hierarchyOverride;
            public bool layoutElementOverride;
            public bool contentSizeFitterOverride;
            public bool graphicColorOverride;
            public bool textContentOverride;
            public bool outlineOverride;
            public bool textStyleOverride;

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
            public bool textBoldKnown;
            public bool textBold;
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
            public bool textBoldKnown;
            public bool textBold;
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

        private sealed class DependentLayoutUndo
        {
            public RectTransform target;
            public RectLayoutRecord layout;
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
            public List<DependentLayoutUndo> dependentLayouts;
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

        private sealed class ResizeChildSnapshot
        {
            internal RectTransform Rect;
            internal Vector3 WorldPosition;
            internal float Width;
            internal float Height;
            internal string Path;
            internal RectLayoutRecord OriginalLayout;
        }

        private sealed class ReloadBaselineRecord
        {
            internal RectTransform Target;
            internal RectLayoutRecord Layout;
            internal bool ActiveSelf;
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
            _originalLayouts.Clear();
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

            if (instance == null)
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

            CaptureOriginalLayouts(rectsByPath);

            CacheSavedCloneSources(
                rectsByPath);

            RecreateSavedClones(
                rectsByPath);

            AddCachedPersistenceRects(
                rectsByPath,
                scopeRoot);

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
                    string.IsNullOrEmpty(record.path))
                    continue;

                RectTransform rect;

                if (!rectsByPath.TryGetValue(
                        record.path,
                        out rect) ||
                    rect == null)
                    continue;

                if (record.hierarchyOverride &&
                    !string.IsNullOrEmpty(
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

                // No se reaplica solo la jerarquia: si un modulo acaba de
                // reconstruir el control, recuperamos tambien posicion, tamano,
                // texto, color, negrita, contorno y metadatos de layout.
                ApplyLayout(
                    rect,
                    record);

                if (record.hierarchyOverride &&
                    record.siblingIndex >= 0 &&
                    rect.parent != null)
                {
                    rect.SetSiblingIndex(
                        Mathf.Clamp(
                            record.siblingIndex,
                            0,
                            rect.parent.childCount - 1));
                }

                _workingLayoutBindings[
                    record.path] = rect;
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
                if (record.hierarchyOverride)
                {
                    AddResolvedPersistencePath(
                        result, record.parentPath, scopeRoot);
                }
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

            AddCachedPersistenceRects(
                result,
                scopeRoot);

            return result;
        }

        private void AddCachedPersistenceRects(
            Dictionary<string, RectTransform> result,
            Transform scopeRoot)
        {
            if (result == null)
                return;

            foreach (var pair in _persistentOriginalPaths)
            {
                var rect = pair.Key;
                var path = pair.Value;

                if (rect == null ||
                    string.IsNullOrEmpty(path))
                    continue;

                if (scopeRoot != null &&
                    rect != scopeRoot &&
                    !rect.IsChildOf(scopeRoot) &&
                    !scopeRoot.IsChildOf(rect))
                    continue;

                result[path] = rect;
            }
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

            if (ClipboardShortcutPressed(KeyCode.L))
            {
                SetTemporaryPassThrough(false);
                ReloadSavedLayoutsFromDisk();
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
                ClearReparentCandidatePreview();
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
                ClearReparentCandidatePreview();
                SetTemporaryPassThrough(true);
                return;
            }

            SetTemporaryPassThrough(false);
            EnsureCurrentEventSystemDisabled();

            if (AltHeld() && _selected != null)
                UpdateReparentCandidatePreview((Vector2)Input.mousePosition);
            else
                ClearReparentCandidatePreview();

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
            ClearReparentCandidatePreview();

            if (_selected != null)
                BeginDrag(DragMode.Move, mousePosition);
        }

        private void LateUpdate()
        {
            // Se protege al final del frame, despues de que los modulos del
            // editor hayan podido reconstruir paneles o recalcular su UI.
            // GuardWorkingState nunca captura el estado vivo: solo reaplica
            // el ultimo estado decidido por el usuario.
            if (_dragMode == DragMode.None &&
                !_propertyEditorOpen)
            {
                GuardWorkingState();

                // Los pases programados son necesarios tambien con el
                // diseñador ACTIVO. Antes solo se ejecutaban al cerrarlo,
                // por eso Ctrl+L podia dejar una restauracion a medias.
                if (_autoApplyPassesRemaining > 0)
                    TryAutoApplySavedLayouts();
            }
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

        internal static bool BlockWorldEditingForGame
        {
            get
            {
                return
                    _activeInstance != null &&
                    _activeInstance._designerEnabled &&
                    !_activeInstance._temporaryPassThrough;
            }
        }

        internal static bool BlockWorldPointerClickForGame
        {
            get
            {
                if (!BlockWorldEditingForGame)
                    return false;

                return
                    Input.GetMouseButton(0) ||
                    Input.GetMouseButton(1) ||
                    Input.GetMouseButtonDown(0) ||
                    Input.GetMouseButtonDown(1);
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
            _resizeChildSnapshots.Clear();
            ClearReparentCandidatePreview();

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
                _resizeChildSnapshots.Clear();
                ClearReparentCandidatePreview();
                return;
            }

            if (!_selected.gameObject.activeInHierarchy)
            {
                _selected = null;
                _dragMode = DragMode.None;
                _resizeChildSnapshots.Clear();
                ClearReparentCandidatePreview();
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

            // Desde este punto la copia se comporta como cualquier otro
            // RectTransform editable. El registro interno solo conserva la
            // receta necesaria para reconstruirla al volver a cargar.
            MarkRectDirty(cloneRect);
            InvalidateReparentCandidateUniverse();

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
            EnsureReloadBaseline(target);

            string cloneId;
            RectTransform cloneRoot;

            // La raiz de una copia necesita retirar tambien su receta de
            // reconstruccion. Cualquier hijo de la copia se trata como un
            // elemento normal y usa su ruta persistente propia.
            if (_runtimeCloneOwnerByInstanceId.TryGetValue(
                    target.gameObject.GetInstanceID(),
                    out cloneId) &&
                _runtimeCloneRoots.TryGetValue(
                    cloneId,
                    out cloneRoot) &&
                cloneRoot != null &&
                cloneRoot == target)
            {
                CloneRecord cloneRecord = null;

                if (_savedClones.TryGetValue(
                        cloneId,
                        out cloneRecord))
                {
                    cloneRecord.name =
                        cloneRoot.gameObject.name;
                    cloneRecord.targetParentPath =
                        ResolvePersistentTargetPath(
                            cloneRoot.parent as RectTransform);
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
                    "UP Deleted Copy " + cloneId;
                cloneRoot.gameObject.SetActive(false);
                InvalidateReparentCandidateUniverse();
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
            InvalidateReparentCandidateUniverse();
            _selected = null;
        }

        private void DeleteSelectedPreservingChildren()
        {
            if (_selected == null)
                return;

            // La raiz de una copia conserva su receta de reconstruccion y se
            // elimina como unidad. Sus hijos, en cambio, ya son elementos
            // persistentes normales y pueden promoverse como cualquier otro.
            if (IsRuntimeCopyRoot(_selected))
            {
                DeleteSelected();
                return;
            }

            var target = _selected;
            EnsureReloadBaseline(target);

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
                    GetPersistentOriginalPath(child);

                if (string.IsNullOrEmpty(childPath))
                    continue;

                EnsureReloadBaseline(child);

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

                    MarkRectDirty(
                        undo.target);

                    if (undo.dependentLayouts != null)
                    {
                        for (var index = 0;
                             index < undo.dependentLayouts.Count;
                             ++index)
                        {
                            var dependent =
                                undo.dependentLayouts[index];

                            if (dependent == null ||
                                dependent.target == null ||
                                dependent.layout == null)
                                continue;

                            ApplyLayout(
                                dependent.target,
                                dependent.layout);

                            MarkRectDirty(
                                dependent.target);
                        }
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

                    string ownerCopyId;

                    if (!_runtimeCloneOwnerByInstanceId.TryGetValue(
                            instanceId,
                            out ownerCopyId) ||
                        !string.Equals(
                            ownerCopyId,
                            cloneId,
                            StringComparison.Ordinal))
                        continue;

                    _runtimeCloneInstanceIds.Remove(
                        instanceId);
                    _runtimeCloneOwnerByInstanceId.Remove(
                        instanceId);

                    var rect = current as RectTransform;
                    if (rect != null)
                        _persistentOriginalPaths.Remove(rect);
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
            MarkRectDirty(_selected);
        }

        private void MarkRectDirty(
            RectTransform rect)
        {
            if (rect == null)
                return;

            _dirtyRects.Add(rect);

            var instanceId =
                rect.gameObject.GetInstanceID();

            string copyId;
            var belongsToCopy =
                _runtimeCloneOwnerByInstanceId.TryGetValue(
                    instanceId,
                    out copyId);

            if (!belongsToCopy)
            {
                var path =
                    GetPersistentOriginalPath(rect);

                if (!string.IsNullOrEmpty(path))
                {
                    var current =
                        CapturePersistentLayout(
                            rect,
                            path);

                    RectLayoutRecord original;
                    _originalLayouts.TryGetValue(
                        path,
                        out original);

                    var delta =
                        BuildDeltaLayout(
                            original,
                            current);

                    if (delta == null)
                        _savedLayouts.Remove(path);
                    else
                        _savedLayouts[path] = delta;

                    _workingLayoutBindings[path] = rect;
                }
            }

            if (belongsToCopy)
            {
                RectTransform copyRoot;
                CloneRecord copyRecord;

                if (_runtimeCloneRoots.TryGetValue(
                        copyId,
                        out copyRoot) &&
                    copyRoot != null &&
                    _savedClones.TryGetValue(
                        copyId,
                        out copyRecord) &&
                    copyRecord != null)
                {
                    copyRecord.name =
                        copyRoot.gameObject.name;
                    copyRecord.targetParentPath =
                        ResolvePersistentTargetPath(
                            copyRoot.parent as RectTransform);
                    copyRecord.siblingIndex =
                        copyRoot.GetSiblingIndex();
                    copyRecord.activeSelf =
                        copyRoot.gameObject.activeSelf;
                    copyRecord.rects =
                        CaptureCloneLayouts(copyRoot);
                }
            }
        }

        private RectLayoutRecord CapturePersistentLayout(
            RectTransform rect,
            string path)
        {
            if (rect == null)
                return null;

            var record =
                CaptureLayout(rect, path);

            record.parentPath =
                ResolvePersistentTargetPath(
                    rect.parent as RectTransform);
            record.siblingIndex =
                rect.GetSiblingIndex();

            return record;
        }

        private void EnsureOriginalLayout(
            RectTransform rect)
        {
            if (rect == null ||
                _runtimeCloneOwnerByInstanceId.ContainsKey(
                    rect.gameObject.GetInstanceID()))
                return;

            var path =
                GetPersistentOriginalPath(rect);

            if (string.IsNullOrEmpty(path) ||
                _originalLayouts.ContainsKey(path))
                return;

            var original =
                CapturePersistentLayout(
                    rect,
                    path);

            if (original != null)
                _originalLayouts[path] = original;
        }

        private void CaptureOriginalLayouts(
            Dictionary<string, RectTransform> rectsByPath)
        {
            if (rectsByPath == null)
                return;

            foreach (var pair in rectsByPath)
            {
                var path = pair.Key;
                var rect = pair.Value;

                if (rect == null ||
                    string.IsNullOrEmpty(path) ||
                    _originalLayouts.ContainsKey(path) ||
                    _runtimeCloneOwnerByInstanceId.ContainsKey(
                        rect.gameObject.GetInstanceID()))
                    continue;

                var original =
                    CapturePersistentLayout(
                        rect,
                        path);

                if (original != null)
                    _originalLayouts[path] = original;
            }
        }

        private static RectLayoutRecord BuildDeltaLayout(
            RectLayoutRecord original,
            RectLayoutRecord current)
        {
            if (current == null)
                return null;

            if (original == null)
            {
                MarkAllOverrideGroups(current);
                return current;
            }

            current.rectTransformOverride =
                !RectTransformGroupEquals(original, current);
            current.hierarchyOverride =
                !HierarchyGroupEquals(original, current);
            current.layoutElementOverride =
                !LayoutElementGroupEquals(original, current);
            current.contentSizeFitterOverride =
                !ContentSizeFitterGroupEquals(original, current);
            current.graphicColorOverride =
                !GraphicColorGroupEquals(original, current);
            current.textContentOverride =
                !TextContentGroupEquals(original, current);
            current.outlineOverride =
                !OutlineGroupEquals(original, current);
            current.textStyleOverride =
                !TextStyleGroupEquals(original, current);

            current.componentMetadataKnown =
                current.layoutElementOverride ||
                current.contentSizeFitterOverride;
            current.extendedAppearanceKnown =
                current.outlineOverride ||
                current.textStyleOverride;

            return HasAnyOverride(current)
                ? current
                : null;
        }

        private static void MarkAllOverrideGroups(
            RectLayoutRecord record)
        {
            if (record == null)
                return;

            record.rectTransformOverride = true;
            record.hierarchyOverride = true;
            record.layoutElementOverride =
                record.componentMetadataKnown;
            record.contentSizeFitterOverride =
                record.componentMetadataKnown;
            record.graphicColorOverride =
                record.hasGraphicColor;
            record.textContentOverride =
                record.hasTextOverride;
            record.outlineOverride =
                record.extendedAppearanceKnown;
            record.textStyleOverride =
                record.extendedAppearanceKnown &&
                record.hasTextStyle;
        }

        private static bool HasAnyOverride(
            RectLayoutRecord record)
        {
            return record != null &&
                (record.rectTransformOverride ||
                 record.hierarchyOverride ||
                 record.layoutElementOverride ||
                 record.contentSizeFitterOverride ||
                 record.graphicColorOverride ||
                 record.textContentOverride ||
                 record.outlineOverride ||
                 record.textStyleOverride);
        }

        private static bool RectTransformGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            return left != null && right != null &&
                Approximately(left.anchorMin, right.anchorMin) &&
                Approximately(left.anchorMax, right.anchorMax) &&
                Approximately(left.pivot, right.pivot) &&
                Approximately(left.anchoredPosition, right.anchoredPosition) &&
                Approximately(left.sizeDelta, right.sizeDelta);
        }

        private static bool HierarchyGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            return left != null && right != null &&
                string.Equals(
                    left.parentPath ?? string.Empty,
                    right.parentPath ?? string.Empty,
                    StringComparison.Ordinal) &&
                left.siblingIndex == right.siblingIndex;
        }

        private static bool LayoutElementGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null ||
                left.hasLayoutElement != right.hasLayoutElement)
                return false;

            if (!left.hasLayoutElement)
                return true;

            return
                Mathf.Approximately(left.layoutMinWidth, right.layoutMinWidth) &&
                Mathf.Approximately(left.layoutMinHeight, right.layoutMinHeight) &&
                Mathf.Approximately(left.layoutPreferredWidth, right.layoutPreferredWidth) &&
                Mathf.Approximately(left.layoutPreferredHeight, right.layoutPreferredHeight) &&
                Mathf.Approximately(left.layoutFlexibleWidth, right.layoutFlexibleWidth) &&
                Mathf.Approximately(left.layoutFlexibleHeight, right.layoutFlexibleHeight) &&
                left.layoutIgnore == right.layoutIgnore;
        }

        private static bool ContentSizeFitterGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null ||
                left.hasContentSizeFitter != right.hasContentSizeFitter)
                return false;

            if (!left.hasContentSizeFitter)
                return true;

            return
                left.contentHorizontalFit == right.contentHorizontalFit &&
                left.contentVerticalFit == right.contentVerticalFit;
        }

        private static bool GraphicColorGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null ||
                left.hasGraphicColor != right.hasGraphicColor)
                return false;

            return !left.hasGraphicColor ||
                Approximately(left.graphicColor, right.graphicColor);
        }

        private static bool TextContentGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null ||
                left.hasTextOverride != right.hasTextOverride)
                return false;

            return !left.hasTextOverride ||
                string.Equals(
                    left.textValue ?? string.Empty,
                    right.textValue ?? string.Empty,
                    StringComparison.Ordinal);
        }

        private static bool OutlineGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null ||
                left.hasOutline != right.hasOutline)
                return false;

            if (!left.hasOutline)
                return true;

            return
                left.outlineEnabled == right.outlineEnabled &&
                Approximately(left.outlineColor, right.outlineColor) &&
                Approximately(left.outlineDistance, right.outlineDistance) &&
                left.outlineUseGraphicAlpha == right.outlineUseGraphicAlpha;
        }

        private static bool TextStyleGroupEquals(
            RectLayoutRecord left,
            RectLayoutRecord right)
        {
            if (left == null || right == null ||
                left.hasTextStyle != right.hasTextStyle)
                return false;

            if (!left.hasTextStyle)
                return true;

            return
                Mathf.Approximately(left.textFontSize, right.textFontSize) &&
                left.textAutoSize == right.textAutoSize &&
                Mathf.Approximately(left.textFontSizeMin, right.textFontSizeMin) &&
                Mathf.Approximately(left.textFontSizeMax, right.textFontSizeMax) &&
                left.textBoldKnown == right.textBoldKnown &&
                (!left.textBoldKnown || left.textBold == right.textBold);
        }

        private void EnsureReloadBaseline(
            RectTransform rect)
        {
            if (rect == null)
                return;

            EnsureOriginalLayout(rect);

            // Las copias se restauran desde su CloneRecord guardado. Una copia
            // creada despues del ultimo guardado se elimina al pulsar Ctrl+L.
            if (_runtimeCloneOwnerByInstanceId.ContainsKey(
                    rect.gameObject.GetInstanceID()))
                return;

            var path =
                GetPersistentOriginalPath(rect);

            if (string.IsNullOrEmpty(path) ||
                _reloadBaselines.ContainsKey(path))
                return;

            _reloadBaselines[path] =
                new ReloadBaselineRecord
                {
                    Target = rect,
                    Layout =
                        CapturePersistentLayout(
                            rect,
                            path),
                    ActiveSelf =
                        rect.gameObject.activeSelf
                };
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

            RegisterRuntimeCopyPersistentPaths(
                cloneId,
                root);
        }

        private void RegisterRuntimeCopyPersistentPaths(
            string copyId,
            RectTransform root)
        {
            if (string.IsNullOrEmpty(copyId) ||
                root == null)
                return;

            var rootPath =
                BuildRuntimeCopyPersistentRootPath(copyId);

            _persistentOriginalPaths[root] =
                rootPath;

            var rects =
                root.GetComponentsInChildren<RectTransform>(true);

            for (var index = 0;
                 index < rects.Length;
                 ++index)
            {
                var rect = rects[index];
                if (rect == null || rect == root)
                    continue;

                string ownerCopyId;

                if (!_runtimeCloneOwnerByInstanceId.TryGetValue(
                        rect.gameObject.GetInstanceID(),
                        out ownerCopyId) ||
                    !string.Equals(
                        ownerCopyId,
                        copyId,
                        StringComparison.Ordinal))
                    continue;

                var relativePath =
                    BuildRelativeTransformPath(
                        root,
                        rect);

                if (string.IsNullOrEmpty(relativePath))
                    continue;

                _persistentOriginalPaths[rect] =
                    rootPath + "/" + relativePath;
            }
        }

        private static string BuildRuntimeCopyPersistentRootPath(
            string copyId)
        {
            return "<UP_COPY:" + copyId + ">";
        }

        private bool IsRuntimeCopyRoot(
            RectTransform rect)
        {
            if (rect == null)
                return false;

            string copyId;
            RectTransform root;

            return
                _runtimeCloneOwnerByInstanceId.TryGetValue(
                    rect.gameObject.GetInstanceID(),
                    out copyId) &&
                _runtimeCloneRoots.TryGetValue(
                    copyId,
                    out root) &&
                root == rect;
        }

        private void CachePersistentPathsRecursive(
            RectTransform root)
        {
            if (root == null)
                return;

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

                GetPersistentOriginalPath(rect);
            }
        }

        private string ResolvePersistentSourcePath(
            RectTransform rect)
        {
            return GetPersistentOriginalPath(rect);
        }

        private string ResolvePersistentTargetPath(
            RectTransform rect)
        {
            return GetPersistentOriginalPath(rect);
        }

        private string GetPersistentOriginalPath(
            RectTransform rect)
        {
            if (rect == null)
                return string.Empty;

            string path;

            if (_persistentOriginalPaths.TryGetValue(
                    rect,
                    out path) &&
                !string.IsNullOrEmpty(path))
                return path;

            string copyId;
            RectTransform copyRoot;

            if (_runtimeCloneOwnerByInstanceId.TryGetValue(
                    rect.gameObject.GetInstanceID(),
                    out copyId) &&
                _runtimeCloneRoots.TryGetValue(
                    copyId,
                    out copyRoot) &&
                copyRoot != null)
            {
                RegisterRuntimeCopyPersistentPaths(
                    copyId,
                    copyRoot);

                if (_persistentOriginalPaths.TryGetValue(
                        rect,
                        out path) &&
                    !string.IsNullOrEmpty(path))
                    return path;
            }

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
            var deadRects =
                new List<RectTransform>();

            foreach (var rect in _dirtyRects)
            {
                if (rect == null)
                {
                    deadRects.Add(rect);
                    continue;
                }

                if (_runtimeCloneOwnerByInstanceId.ContainsKey(
                        rect.gameObject.GetInstanceID()))
                    continue;

                var path =
                    GetPersistentOriginalPath(rect);

                if (string.IsNullOrEmpty(path))
                    continue;

                var current =
                    CapturePersistentLayout(
                        rect,
                        path);

                RectLayoutRecord original;
                _originalLayouts.TryGetValue(
                    path,
                    out original);

                var delta =
                    BuildDeltaLayout(
                        original,
                        current);

                if (delta == null)
                    _savedLayouts.Remove(path);
                else
                    _savedLayouts[path] = delta;

                _workingLayoutBindings[path] = rect;
            }

            for (var index = 0;
                 index < deadRects.Count;
                 ++index)
            {
                _dirtyRects.Remove(
                    deadRects[index]);
            }

            // Convierte tambien E antiguos/capturas completas ya cargadas a
            // V8 y elimina cualquier entrada que haya vuelto exactamente al
            // estado original del codigo, aunque el cambio se deshiciera.
            PruneSavedLayoutsAgainstOriginals();

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
                record.targetParentPath =
                    ResolvePersistentTargetPath(
                        root.parent as RectTransform);
                record.siblingIndex =
                    root.GetSiblingIndex();
                record.activeSelf =
                    root.gameObject.activeSelf;
                record.rects =
                    CaptureCloneLayouts(root);
            }

            if (WriteSavedLayouts())
            {
                _dirtyRects.Clear();
                _reloadBaselines.Clear();
            }
        }

        private void PruneSavedLayoutsAgainstOriginals()
        {
            if (_savedLayouts.Count == 0 ||
                _originalLayouts.Count == 0)
                return;

            var paths =
                new List<string>(
                    _savedLayouts.Keys);

            for (var index = 0;
                 index < paths.Count;
                 ++index)
            {
                var path = paths[index];

                RectLayoutRecord original;
                if (!_originalLayouts.TryGetValue(
                        path,
                        out original) ||
                    original == null)
                    continue;

                RectTransform rect;
                if (!_workingLayoutBindings.TryGetValue(
                        path,
                        out rect) ||
                    rect == null)
                {
                    if (!TryGetCachedPersistentRect(
                            path,
                            null,
                            out rect) &&
                        !TryResolveStablePath(
                            path,
                            null,
                            out rect))
                        continue;
                }

                if (rect == null ||
                    _runtimeCloneOwnerByInstanceId.ContainsKey(
                        rect.gameObject.GetInstanceID()))
                    continue;

                var current =
                    CapturePersistentLayout(
                        rect,
                        path);

                var delta =
                    BuildDeltaLayout(
                        original,
                        current);

                if (delta == null)
                    _savedLayouts.Remove(path);
                else
                    _savedLayouts[path] = delta;

                _workingLayoutBindings[path] = rect;
            }
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
            bool textBold;

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
                out textFontSizeMax,
                out textBold);

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

                rectTransformOverride = true,
                hierarchyOverride = true,
                layoutElementOverride = true,
                contentSizeFitterOverride = true,
                graphicColorOverride = hasGraphicColor,
                textContentOverride = hasTextOverride,
                outlineOverride = true,
                textStyleOverride = hasTextStyle,

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
                textFontSizeMax = textFontSizeMax,
                textBoldKnown = hasTextStyle,
                textBold = textBold
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
                bool textBold;

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
                    out textFontSizeMax,
                    out textBold);

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
                        textFontSizeMax = textFontSizeMax,
                        textBoldKnown = hasTextStyle,
                        textBold = textBold
                    });
            }

            return result;
        }

        private void LoadSavedLayouts()
        {
            _savedLayouts.Clear();
            _savedClones.Clear();
            _savedDeletedPaths.Clear();
            _workingLayoutBindings.Clear();

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
                     lines[0] != "UP_UI_LAYOUT_V6" &&
                     lines[0] != "UP_UI_LAYOUT_V7" &&
                     lines[0] != "UP_UI_LAYOUT_V8"))
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

        private bool WriteSavedLayouts()
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
                lines.Add("UP_UI_LAYOUT_V8");

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

                    if (record == null ||
                        _savedDeletedPaths.Contains(
                            record.path))
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

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[UnderPressure UI Designer] " +
                    "No se pudo guardar el layout: " +
                    exception.Message);

                return false;
            }
        }

        private static string SerializeElementRecord(
            RectLayoutRecord record)
        {
            var fields = new List<string>
            {
                "E",
                EncodeText(record.path)
            };

            if (record.rectTransformOverride)
            {
                fields.Add("R");
                fields.Add(FormatFloat(record.anchorMin.x));
                fields.Add(FormatFloat(record.anchorMin.y));
                fields.Add(FormatFloat(record.anchorMax.x));
                fields.Add(FormatFloat(record.anchorMax.y));
                fields.Add(FormatFloat(record.pivot.x));
                fields.Add(FormatFloat(record.pivot.y));
                fields.Add(FormatFloat(record.anchoredPosition.x));
                fields.Add(FormatFloat(record.anchoredPosition.y));
                fields.Add(FormatFloat(record.sizeDelta.x));
                fields.Add(FormatFloat(record.sizeDelta.y));
            }

            if (record.hierarchyOverride)
            {
                fields.Add("H");
                fields.Add(EncodeText(record.parentPath));
                fields.Add(record.siblingIndex.ToString(
                    CultureInfo.InvariantCulture));
            }

            if (record.layoutElementOverride)
            {
                fields.Add("L");
                fields.Add(record.hasLayoutElement ? "1" : "0");
                fields.Add(FormatFloat(record.layoutMinWidth));
                fields.Add(FormatFloat(record.layoutMinHeight));
                fields.Add(FormatFloat(record.layoutPreferredWidth));
                fields.Add(FormatFloat(record.layoutPreferredHeight));
                fields.Add(FormatFloat(record.layoutFlexibleWidth));
                fields.Add(FormatFloat(record.layoutFlexibleHeight));
                fields.Add(record.layoutIgnore ? "1" : "0");
            }

            if (record.contentSizeFitterOverride)
            {
                fields.Add("F");
                fields.Add(record.hasContentSizeFitter ? "1" : "0");
                fields.Add(record.contentHorizontalFit.ToString(
                    CultureInfo.InvariantCulture));
                fields.Add(record.contentVerticalFit.ToString(
                    CultureInfo.InvariantCulture));
            }

            if (record.graphicColorOverride)
            {
                fields.Add("G");
                fields.Add(record.hasGraphicColor ? "1" : "0");
                fields.Add(FormatFloat(record.graphicColor.r));
                fields.Add(FormatFloat(record.graphicColor.g));
                fields.Add(FormatFloat(record.graphicColor.b));
                fields.Add(FormatFloat(record.graphicColor.a));
            }

            if (record.textContentOverride)
            {
                fields.Add("T");
                fields.Add(record.hasTextOverride ? "1" : "0");
                fields.Add(EncodeText(record.textValue));
            }

            if (record.outlineOverride)
            {
                fields.Add("O");
                fields.Add(record.hasOutline ? "1" : "0");
                fields.Add(record.outlineEnabled ? "1" : "0");
                fields.Add(FormatFloat(record.outlineColor.r));
                fields.Add(FormatFloat(record.outlineColor.g));
                fields.Add(FormatFloat(record.outlineColor.b));
                fields.Add(FormatFloat(record.outlineColor.a));
                fields.Add(FormatFloat(record.outlineDistance.x));
                fields.Add(FormatFloat(record.outlineDistance.y));
                fields.Add(record.outlineUseGraphicAlpha ? "1" : "0");
            }

            if (record.textStyleOverride)
            {
                fields.Add("S");
                fields.Add(record.hasTextStyle ? "1" : "0");
                fields.Add(FormatFloat(record.textFontSize));
                fields.Add(record.textAutoSize ? "1" : "0");
                fields.Add(FormatFloat(record.textFontSizeMin));
                fields.Add(FormatFloat(record.textFontSizeMax));
                fields.Add(record.textBoldKnown ? "1" : "0");
                fields.Add(record.textBold ? "1" : "0");
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
                if (record.textBoldKnown)
                    fields.Add(record.textBold ? "1" : "0");
            }

            return string.Join("|", fields.ToArray());
        }

        private static bool IsV8ElementTag(string value)
        {
            return value == "R" || value == "H" ||
                   value == "L" || value == "F" ||
                   value == "G" || value == "T" ||
                   value == "O" || value == "S";
        }

        private static bool TryParseV8ElementRecord(
            string[] parts,
            out RectLayoutRecord record)
        {
            record = null;

            if (parts == null || parts.Length < 3)
                return false;

            var parsed =
                new RectLayoutRecord
                {
                    path = DecodeText(parts[1])
                };

            var index = 2;

            while (index < parts.Length)
            {
                var tag = parts[index++];

                if (tag == "R")
                {
                    if (index + 10 > parts.Length)
                        return false;

                    float a0, a1, a2, a3, p0, p1, x, y, w, h;
                    if (!float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out a0) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out a1) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out a2) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out a3) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out p0) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out p1) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out w) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out h))
                        return false;

                    parsed.anchorMin = new Vector2(a0, a1);
                    parsed.anchorMax = new Vector2(a2, a3);
                    parsed.pivot = new Vector2(p0, p1);
                    parsed.anchoredPosition = new Vector2(x, y);
                    parsed.sizeDelta = new Vector2(w, h);
                    parsed.rectTransformOverride = true;
                    continue;
                }

                if (tag == "H")
                {
                    if (index + 2 > parts.Length)
                        return false;

                    parsed.parentPath = DecodeText(parts[index++]);
                    if (!int.TryParse(parts[index++], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsed.siblingIndex))
                        return false;
                    parsed.hierarchyOverride = true;
                    continue;
                }

                if (tag == "L")
                {
                    if (index + 8 > parts.Length)
                        return false;

                    float minW, minH, prefW, prefH, flexW, flexH;
                    if (!TryParseBool01(parts[index++], out parsed.hasLayoutElement) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out minW) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out minH) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out prefW) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out prefH) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out flexW) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out flexH) ||
                        !TryParseBool01(parts[index++], out parsed.layoutIgnore))
                        return false;

                    parsed.layoutMinWidth = minW;
                    parsed.layoutMinHeight = minH;
                    parsed.layoutPreferredWidth = prefW;
                    parsed.layoutPreferredHeight = prefH;
                    parsed.layoutFlexibleWidth = flexW;
                    parsed.layoutFlexibleHeight = flexH;
                    parsed.layoutElementOverride = true;
                    parsed.componentMetadataKnown = true;
                    continue;
                }

                if (tag == "F")
                {
                    if (index + 3 > parts.Length)
                        return false;

                    if (!TryParseBool01(parts[index++], out parsed.hasContentSizeFitter) ||
                        !int.TryParse(parts[index++], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed.contentHorizontalFit) ||
                        !int.TryParse(parts[index++], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed.contentVerticalFit))
                        return false;

                    parsed.contentSizeFitterOverride = true;
                    parsed.componentMetadataKnown = true;
                    continue;
                }

                if (tag == "G")
                {
                    if (index + 5 > parts.Length)
                        return false;

                    float r, g, b, a;
                    if (!TryParseBool01(parts[index++], out parsed.hasGraphicColor) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out r) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out g) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out b) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                        return false;

                    parsed.graphicColor = new Color(r, g, b, a);
                    parsed.graphicColorOverride = true;
                    continue;
                }

                if (tag == "T")
                {
                    if (index + 2 > parts.Length)
                        return false;

                    if (!TryParseBool01(parts[index++], out parsed.hasTextOverride))
                        return false;
                    parsed.textValue = DecodeText(parts[index++]);
                    parsed.textContentOverride = true;
                    continue;
                }

                if (tag == "O")
                {
                    if (index + 9 > parts.Length)
                        return false;

                    float r, g, b, a, x, y;
                    if (!TryParseBool01(parts[index++], out parsed.hasOutline) ||
                        !TryParseBool01(parts[index++], out parsed.outlineEnabled) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out r) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out g) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out b) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out a) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                        !TryParseBool01(parts[index++], out parsed.outlineUseGraphicAlpha))
                        return false;

                    parsed.outlineColor = new Color(r, g, b, a);
                    parsed.outlineDistance = new Vector2(x, y);
                    parsed.outlineOverride = true;
                    parsed.extendedAppearanceKnown = true;
                    continue;
                }

                if (tag == "S")
                {
                    if (index + 7 > parts.Length)
                        return false;

                    float size, min, max;
                    if (!TryParseBool01(parts[index++], out parsed.hasTextStyle) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out size) ||
                        !TryParseBool01(parts[index++], out parsed.textAutoSize) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out min) ||
                        !float.TryParse(parts[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out max) ||
                        !TryParseBool01(parts[index++], out parsed.textBoldKnown) ||
                        !TryParseBool01(parts[index++], out parsed.textBold))
                        return false;

                    parsed.textFontSize = size;
                    parsed.textFontSizeMin = min;
                    parsed.textFontSizeMax = max;
                    parsed.textStyleOverride = true;
                    parsed.extendedAppearanceKnown = true;
                    continue;
                }

                return false;
            }

            record = parsed;
            return !string.IsNullOrEmpty(record.path) &&
                HasAnyOverride(record);
        }

        private static bool TryParseElementRecord(
            string[] parts,
            out RectLayoutRecord record)
        {
            record = null;

            if (parts != null &&
                parts.Length >= 3 &&
                IsV8ElementTag(parts[2]))
            {
                return TryParseV8ElementRecord(
                    parts,
                    out record);
            }

            if (parts == null ||
                (parts.Length != 12 &&
                 parts.Length != 14 &&
                 parts.Length != 25 &&
                 parts.Length != 32 &&
                 parts.Length != 46 &&
                 parts.Length != 47))
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
                        parts.Length == 46 ||
                        parts.Length == 47
                };

            if (parts.Length == 25 ||
                parts.Length == 32 ||
                (parts.Length == 46 || parts.Length == 47))
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
                (parts.Length == 46 || parts.Length == 47))
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

            if (parts.Length == 46 || parts.Length == 47)
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
                if (parts.Length == 47)
                {
                    if (!TryParseBool01(parts[46], out parsed.textBold))
                        return false;
                    parsed.textBoldKnown = true;
                }
            }

            parsed.rectTransformOverride = true;
            parsed.hierarchyOverride = parts.Length >= 14;
            parsed.layoutElementOverride =
                parsed.componentMetadataKnown;
            parsed.contentSizeFitterOverride =
                parsed.componentMetadataKnown &&
                parsed.hasContentSizeFitter;
            parsed.graphicColorOverride =
                parsed.hasGraphicColor;
            parsed.textContentOverride =
                parsed.hasTextOverride;
            parsed.outlineOverride =
                parsed.extendedAppearanceKnown;
            parsed.textStyleOverride =
                parsed.extendedAppearanceKnown &&
                parsed.hasTextStyle;

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
                 parts.Length != 34 &&
                 parts.Length != 35))
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
                (parts.Length == 34 || parts.Length == 35))
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

            if (parts.Length == 34 || parts.Length == 35)
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
                if (parts.Length == 35)
                {
                    if (!TryParseBool01(parts[34], out parsed.textBold))
                        return false;
                    parsed.textBoldKnown = true;
                }
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

        private void GuardWorkingState()
        {
            if ((_savedLayouts.Count == 0 &&
                 _savedClones.Count == 0 &&
                 _savedDeletedPaths.Count == 0) ||
                Time.unscaledTime <
                    _nextWorkingStateGuardTime)
                return;

            _nextWorkingStateGuardTime =
                Time.unscaledTime +
                WorkingStateGuardInterval;

            var needsBatchReapply = false;

            // Nunca corregimos elemento a elemento mientras resolvemos rutas:
            // reordenar/reparentar uno puede cambiar los ordinales de los
            // siguientes. Primero detectamos si algo se ha perdido y despues
            // resolvemos TODO el estado conocido en un solo mapa estable.
            foreach (var path in _savedDeletedPaths)
            {
                RectTransform deleted;

                if ((!TryGetCachedPersistentRect(
                         path,
                         null,
                         out deleted) &&
                     !TryResolveStablePath(
                         path,
                         null,
                         out deleted)) ||
                    deleted == null)
                    continue;

                if (deleted.gameObject.activeSelf)
                {
                    needsBatchReapply = true;
                    break;
                }
            }

            if (!needsBatchReapply)
            {
                foreach (var pair in _savedClones)
                {
                    RectTransform copyRoot;

                    if (!_runtimeCloneRoots.TryGetValue(
                            pair.Key,
                            out copyRoot) ||
                        copyRoot == null)
                    {
                        needsBatchReapply = true;
                        break;
                    }
                }
            }

            if (!needsBatchReapply)
            {
                foreach (var pair in _savedLayouts)
                {
                    var record = pair.Value;

                    if (record == null ||
                        string.IsNullOrEmpty(record.path))
                        continue;

                    RectTransform rect;

                    if (!_workingLayoutBindings.TryGetValue(
                            record.path,
                            out rect) ||
                        rect == null)
                    {
                        if (!TryGetCachedPersistentRect(
                                record.path,
                                null,
                                out rect) &&
                            !TryResolveStablePath(
                                record.path,
                                null,
                                out rect))
                        {
                            // El control puede estar temporalmente ausente
                            // mientras se reconstruye. En cuanto reaparezca
                            // volvera a resolverse por su ruta original.
                            continue;
                        }
                    }

                    if (rect == null)
                        continue;

                    if (rect == _selected &&
                        (_dragMode != DragMode.None ||
                         _propertyEditorOpen))
                        continue;

                    if (!WorkingLayoutMatches(
                            rect,
                            record))
                    {
                        needsBatchReapply = true;
                        break;
                    }

                    _workingLayoutBindings[
                        record.path] = rect;
                }
            }

            if (!needsBatchReapply)
                return;

            ReapplyPersistentStructure(null);
        }

        private bool WorkingLayoutMatches(
            RectTransform rect,
            RectLayoutRecord record)
        {
            if (rect == null || record == null)
                return false;

            if (record.hierarchyOverride)
            {
                var currentParentPath =
                    ResolvePersistentTargetPath(
                        rect.parent as RectTransform);

                if (!string.Equals(
                        currentParentPath,
                        record.parentPath ?? string.Empty,
                        StringComparison.Ordinal))
                    return false;

                if (record.siblingIndex >= 0 &&
                    rect.parent != null &&
                    rect.GetSiblingIndex() !=
                        Mathf.Clamp(
                            record.siblingIndex,
                            0,
                            rect.parent.childCount - 1))
                    return false;
            }

            var current =
                CaptureLayout(
                    rect,
                    string.Empty);

            if (record.rectTransformOverride &&
                !RectTransformGroupEquals(current, record))
                return false;

            if (record.layoutElementOverride &&
                !LayoutElementGroupEquals(current, record))
                return false;

            if (record.contentSizeFitterOverride &&
                !ContentSizeFitterGroupEquals(current, record))
                return false;

            if (record.graphicColorOverride &&
                !GraphicColorGroupEquals(current, record))
                return false;

            if (record.textContentOverride &&
                !TextContentGroupEquals(current, record))
                return false;

            if (record.outlineOverride &&
                !OutlineGroupEquals(current, record))
                return false;

            if (record.textStyleOverride &&
                ShouldPersistTextStyle(rect) &&
                !TextStyleGroupEquals(current, record))
                return false;

            return true;
        }

        private void ApplyWorkingRecord(
            RectTransform rect,
            RectLayoutRecord record)
        {
            if (rect == null || record == null)
                return;

            if (record.hierarchyOverride &&
                !string.IsNullOrEmpty(
                    record.parentPath))
            {
                RectTransform targetParent;

                if ((TryGetCachedPersistentRect(
                         record.parentPath,
                         null,
                         out targetParent) ||
                     TryResolveStablePath(
                         record.parentPath,
                         null,
                         out targetParent)) &&
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

            if (record.hierarchyOverride &&
                record.siblingIndex >= 0 &&
                rect.parent != null)
            {
                rect.SetSiblingIndex(
                    Mathf.Clamp(
                        record.siblingIndex,
                        0,
                        rect.parent.childCount - 1));
            }

            if (!string.IsNullOrEmpty(
                    record.path))
            {
                _workingLayoutBindings[
                    record.path] = rect;
                _persistentOriginalPaths[rect] =
                    record.path;
            }
        }

        private static bool Approximately(
            Color left,
            Color right)
        {
            return
                Mathf.Approximately(left.r, right.r) &&
                Mathf.Approximately(left.g, right.g) &&
                Mathf.Approximately(left.b, right.b) &&
                Mathf.Approximately(left.a, right.a);
        }

        private void ReloadSavedLayoutsFromDisk()
        {
            if (_propertyEditorOpen)
                CancelPropertyEditor();

            if (_dragMode != DragMode.None)
            {
                RestoreDragStartValues();
                _dragUndoLayout = null;
                _dragMode = DragMode.None;
                _resizeChildSnapshots.Clear();
            }

            ClearReparentCandidatePreview();
            _selected = null;

            var baselines =
                new Dictionary<string, ReloadBaselineRecord>(
                    _reloadBaselines,
                    StringComparer.Ordinal);

            DiscardRuntimeClonesForReload();

            _workingLayoutBindings.Clear();
            _dirtyRects.Clear();
            _undoStack.Clear();

            // Esta llamada vuelve a leer fisicamente ui_layout_overrides.txt.
            // Por tanto, todo lo posterior al ultimo Ctrl+S desaparece del
            // estado de trabajo antes de volver a aplicarlo.
            LoadSavedLayouts();

            ReapplyPersistentStructure(null);

            // Si un elemento nunca habia sido guardado, no existe un E record
            // que pueda restaurarlo. Recuperamos entonces la captura anterior
            // a su primera edicion desde el ultimo Ctrl+S/Ctrl+L.
            foreach (var pair in baselines)
            {
                if (_savedDeletedPaths.Contains(
                        pair.Key))
                    continue;

                var baseline = pair.Value;

                if (baseline == null ||
                    baseline.Layout == null)
                    continue;

                var rect = baseline.Target;

                if (rect == null &&
                    !TryResolveStablePath(
                        pair.Key,
                        null,
                        out rect))
                    continue;

                if (rect == null)
                    continue;

                if (!_savedLayouts.ContainsKey(
                        pair.Key))
                {
                    ApplyWorkingRecord(
                        rect,
                        baseline.Layout);
                }

                // Un borrado no guardado deja el GameObject inactivo. Aunque
                // exista un E guardado, Ctrl+L debe recuperar tambien su
                // visibilidad anterior.
                rect.gameObject.SetActive(
                    baseline.ActiveSelf);
            }

            // Un primer ForceUpdate ejecuta cualquier layout pendiente que
            // pudiera intentar restaurar valores del codigo. Despues se
            // reaplica el estado del fichero una segunda vez, ya sobre el
            // arbol estabilizado. Los pases programados cubren reconstrucciones
            // que terminen uno o varios frames despues.
            Canvas.ForceUpdateCanvases();
            ReapplyPersistentStructure(null);

            _reloadBaselines.Clear();
            _nextWorkingStateGuardTime = 0f;
            ScheduleAutoApply();
        }

        private void DiscardRuntimeClonesForReload()
        {
            if (_runtimeCloneRoots.Count == 0)
                return;

            var ids =
                new List<string>(
                    _runtimeCloneRoots.Keys);

            for (var index = 0;
                 index < ids.Count;
                 ++index)
            {
                var copyId = ids[index];
                RectTransform root;

                if (!_runtimeCloneRoots.TryGetValue(
                        copyId,
                        out root) ||
                    root == null)
                    continue;

                UnregisterRuntimeClone(
                    copyId,
                    root);

                root.gameObject.name =
                    "UP Reload Discarded Copy " +
                    copyId;
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
            }

            _runtimeCloneRoots.Clear();
            _runtimeCloneOwnerByInstanceId.Clear();
            _runtimeCloneInstanceIds.Clear();
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

            CaptureOriginalLayouts(rectsByPath);

            // Las copias se reconstruyen primero para que puedan resolverse
            // como padres reales de otros elementos en los E records.
            CacheSavedCloneSources(
                rectsByPath);

            RecreateSavedClones(
                rectsByPath);

            AddCachedPersistenceRects(
                rectsByPath,
                null);

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

                if (record.hierarchyOverride &&
                    !string.IsNullOrEmpty(
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

                _workingLayoutBindings[
                    record.path] = rect;

                if (record.hierarchyOverride &&
                    record.siblingIndex >= 0 &&
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
            if (rectsByPath == null ||
                _savedClones.Count == 0)
                return;

            var pending =
                new List<string>(
                    _savedClones.Keys);

            var maxPasses =
                Mathf.Max(1, pending.Count + 1);

            for (var pass = 0;
                 pass < maxPasses && pending.Count > 0;
                 ++pass)
            {
                var madeProgress = false;

                for (var index = pending.Count - 1;
                     index >= 0;
                     --index)
                {
                    var copyId = pending[index];
                    CloneRecord record;

                    if (!_savedClones.TryGetValue(
                            copyId,
                            out record) ||
                        record == null)
                    {
                        pending.RemoveAt(index);
                        continue;
                    }

                    RectTransform existingRoot;

                    if (_runtimeCloneRoots.TryGetValue(
                            copyId,
                            out existingRoot) &&
                        existingRoot != null)
                    {
                        RectTransform existingParent;

                        if (!string.IsNullOrEmpty(
                                record.targetParentPath) &&
                            rectsByPath.TryGetValue(
                                record.targetParentPath,
                                out existingParent) &&
                            existingParent != null &&
                            existingParent != existingRoot &&
                            !existingParent.IsChildOf(
                                existingRoot) &&
                            existingRoot.parent != existingParent)
                        {
                            existingRoot.SetParent(
                                existingParent,
                                false);
                        }

                        existingRoot.gameObject.name =
                            string.IsNullOrEmpty(record.name)
                                ? "UI Element Copy"
                                : record.name;

                        if (existingRoot.parent != null)
                        {
                            existingRoot.SetSiblingIndex(
                                Mathf.Clamp(
                                    record.siblingIndex,
                                    0,
                                    existingRoot.parent.childCount - 1));
                        }

                        ApplyCloneLayouts(
                            existingRoot,
                            record.rects);

                        existingRoot.gameObject.SetActive(
                            record.activeSelf);

                        RegisterRuntimeCopyPersistentPaths(
                            copyId,
                            existingRoot);
                        AddCachedPersistenceRects(
                            rectsByPath,
                            null);
                        pending.RemoveAt(index);
                        madeProgress = true;
                        continue;
                    }

                    if (string.IsNullOrEmpty(
                            record.sourcePath) ||
                        string.IsNullOrEmpty(
                            record.targetParentPath))
                        continue;

                    if (!_sourceTemplateCache.ContainsKey(
                            record.sourcePath))
                    {
                        RectTransform liveSource;

                        if (rectsByPath.TryGetValue(
                                record.sourcePath,
                                out liveSource) &&
                            liveSource != null)
                        {
                            CacheSourceTemplate(
                                record.sourcePath,
                                liveSource);
                        }
                    }

                    GameObject sourceTemplate;
                    RectTransform targetParent;

                    if (!_sourceTemplateCache.TryGetValue(
                            record.sourcePath,
                            out sourceTemplate) ||
                        sourceTemplate == null ||
                        !rectsByPath.TryGetValue(
                            record.targetParentPath,
                            out targetParent) ||
                        targetParent == null)
                        continue;

                    var copyObject =
                        Instantiate(
                            sourceTemplate,
                            targetParent,
                            false);

                    copyObject.name =
                        string.IsNullOrEmpty(
                            record.name)
                            ? "UI Element Copy"
                            : record.name;

                    var copyRect =
                        copyObject.transform as
                        RectTransform;

                    if (copyRect == null)
                    {
                        Destroy(copyObject);
                        pending.RemoveAt(index);
                        continue;
                    }

                    copyRect.SetSiblingIndex(
                        Mathf.Clamp(
                            record.siblingIndex,
                            0,
                            targetParent.childCount - 1));

                    ApplyCloneLayouts(
                        copyRect,
                        record.rects);

                    copyObject.SetActive(
                        record.activeSelf);

                    RegisterRuntimeClone(
                        copyId,
                        copyRect);

                    AddCachedPersistenceRects(
                        rectsByPath,
                        null);

                    pending.RemoveAt(index);
                    madeProgress = true;
                }

                if (!madeProgress)
                    break;
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
                    true,
                    true,
                    record.hasOutline,
                    record.outlineEnabled,
                    record.outlineColor,
                    record.outlineDistance,
                    record.outlineUseGraphicAlpha,
                    record.hasTextStyle,
                    record.textFontSize,
                    record.textAutoSize,
                    record.textFontSizeMin,
                    record.textFontSizeMax,
                    record.textBoldKnown,
                    record.textBold);

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

            if (record.rectTransformOverride)
            {
                rect.anchorMin = record.anchorMin;
                rect.anchorMax = record.anchorMax;
                rect.pivot = record.pivot;
                rect.anchoredPosition =
                    record.anchoredPosition;
                rect.sizeDelta = record.sizeDelta;
            }

            ApplyAppearance(
                rect,
                record.graphicColorOverride &&
                    record.hasGraphicColor,
                record.graphicColor,
                record.textContentOverride &&
                    record.hasTextOverride,
                record.textValue);

            ApplyExtendedAppearance(
                rect,
                record.extendedAppearanceKnown,
                record.outlineOverride,
                record.textStyleOverride,
                record.hasOutline,
                record.outlineEnabled,
                record.outlineColor,
                record.outlineDistance,
                record.outlineUseGraphicAlpha,
                record.hasTextStyle,
                record.textFontSize,
                record.textAutoSize,
                record.textFontSizeMin,
                record.textFontSizeMax,
                record.textBoldKnown,
                record.textBold);

            if (record.layoutElementOverride)
            {
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
            }

            if (record.contentSizeFitterOverride)
            {
                var contentSizeFitter =
                    rect.GetComponent<ContentSizeFitter>();

                if (record.hasContentSizeFitter)
                {
                    if (contentSizeFitter == null)
                    {
                        contentSizeFitter =
                            rect.gameObject
                                .AddComponent<ContentSizeFitter>();
                    }

                    contentSizeFitter.horizontalFit =
                        (ContentSizeFitter.FitMode)
                            record.contentHorizontalFit;
                    contentSizeFitter.verticalFit =
                        (ContentSizeFitter.FitMode)
                            record.contentVerticalFit;
                }
                else if (contentSizeFitter != null)
                {
                    UnityEngine.Object.Destroy(
                        contentSizeFitter);
                }
            }
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
                    ClearReparentCandidatePreview();
                    return;
                }

                parent = parent.parent;
            }
        }
        private bool TryReadPageNavigation(out int direction)
        {
            var pageUpHeld = Input.GetKey(KeyCode.PageUp);
            var pageDownHeld = Input.GetKey(KeyCode.PageDown);
            var pageUpPressed = pageUpHeld && !_pageUpWasHeld;
            var pageDownPressed = pageDownHeld && !_pageDownWasHeld;

            _pageUpWasHeld = pageUpHeld;
            _pageDownWasHeld = pageDownHeld;

            if (pageUpPressed)
            {
                direction = 1;
                return true;
            }

            if (pageDownPressed)
            {
                direction = -1;
                return true;
            }

            direction = 0;
            return false;
        }

        private bool TryChangeSiblingOrder()
        {
            int direction;
            if (!TryReadPageNavigation(out direction))
                return false;

            if (_selected == null)
                return true;

            if (AltHeld())
            {
                UpdateReparentCandidatePreview((Vector2)Input.mousePosition);
                CycleReparentCandidate(direction);
                return true;
            }

            if (_selected.parent == null)
                return true;

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

            EnsureReloadBaseline(_selected);

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

            string copyId;

            if (_runtimeCloneOwnerByInstanceId
                    .TryGetValue(
                        rect.gameObject
                            .GetInstanceID(),
                        out copyId))
            {
                RectTransform copyRoot;
                CloneRecord copyRecord;

                if (_runtimeCloneRoots.TryGetValue(
                        copyId,
                        out copyRoot) &&
                    copyRoot == rect &&
                    _savedClones.TryGetValue(
                        copyId,
                        out copyRecord) &&
                    copyRecord != null)
                {
                    var parent =
                        rect.parent as
                        RectTransform;

                    copyRecord.targetParentPath =
                        ResolvePersistentTargetPath(
parent);
                    copyRecord.siblingIndex =
                        rect.GetSiblingIndex();
                    copyRecord.rects =
                        CaptureCloneLayouts(
                            rect);
                }

                MarkRectDirty(rect);
                InvalidateReparentCandidateUniverse();
                return;
            }

            MarkRectDirty(rect);
            InvalidateReparentCandidateUniverse();
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

            UpdateReparentCandidatePreview(screenPoint);
            var targetParent = _reparentCandidate;

            if (targetParent == null ||
                targetParent == _selected ||
                targetParent.IsChildOf(
                    _selected))
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

            // Congelamos las rutas originales de todo el subarbol antes
            // de cambiar de padre. Si solo se guardaba la raiz, un hijo
            // editado despues heredaba una ruta basada en la jerarquia nueva
            // y ya no podia encontrarse tras una reconstruccion.
            CachePersistentPathsRecursive(
                _selected);

            GetPersistentOriginalPath(
                targetParent);

            EnsureReloadBaseline(_selected);

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

            ClearReparentCandidatePreview();
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
            EnsureReloadBaseline(_selected);

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

            if (mode == DragMode.Move)
            {
                _resizeChildSnapshots.Clear();
                _resizeKeepChildrenFixed = false;
            }
            else
            {
                CaptureResizeChildSnapshots();
                _resizeKeepChildrenFixed = ShiftHeld();
            }

            GetPersistentOriginalPath(
                _selected);
        }

        private void CaptureResizeChildSnapshots()
        {
            _resizeChildSnapshots.Clear();

            if (_selected == null)
                return;

            for (var index = 0;
                 index < _selected.childCount;
                 ++index)
            {
                var child =
                    _selected.GetChild(index)
                        as RectTransform;

                if (child == null)
                    continue;

                // Mantener al hijo quieto en pantalla obliga a cambiar sus
                // coordenadas locales cuando cambia el rect del padre. Por
                // tanto el hijo forma parte de la MISMA operacion de resize:
                // tiene baseline propio, undo propio y se persiste junto al
                // padre. Antes solo se guardaba el padre y al reconstruir la
                // UI los hijos saltaban.
                EnsureReloadBaseline(child);

                var childPath =
                    GetPersistentOriginalPath(child);

                var childLayout =
                    !string.IsNullOrEmpty(childPath)
                        ? CapturePersistentLayout(
                            child,
                            childPath)
                        : CaptureLayout(
                            child,
                            string.Empty);

                _resizeChildSnapshots.Add(
                    new ResizeChildSnapshot
                    {
                        Rect = child,
                        WorldPosition = child.position,
                        Width = child.rect.width,
                        Height = child.rect.height,
                        Path = childPath,
                        OriginalLayout = childLayout
                    });
            }
        }

        private void RestoreResizeChildSnapshots()
        {
            if (_selected == null ||
                _resizeChildSnapshots.Count == 0)
                return;

            for (var index = 0;
                 index < _resizeChildSnapshots.Count;
                 ++index)
            {
                var snapshot =
                    _resizeChildSnapshots[index];

                if (snapshot == null ||
                    snapshot.Rect == null ||
                    snapshot.Rect.parent != _selected)
                    continue;

                snapshot.Rect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    snapshot.Width);

                snapshot.Rect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    snapshot.Height);

                snapshot.Rect.position =
                    snapshot.WorldPosition;
            }
        }

        private List<DependentLayoutUndo>
            BuildResizeDependentUndo()
        {
            if (_resizeChildSnapshots.Count == 0)
                return null;

            var result =
                new List<DependentLayoutUndo>();

            for (var index = 0;
                 index < _resizeChildSnapshots.Count;
                 ++index)
            {
                var snapshot =
                    _resizeChildSnapshots[index];

                if (snapshot == null ||
                    snapshot.Rect == null ||
                    snapshot.OriginalLayout == null)
                    continue;

                result.Add(
                    new DependentLayoutUndo
                    {
                        target = snapshot.Rect,
                        layout = snapshot.OriginalLayout
                    });
            }

            return result.Count > 0
                ? result
                : null;
        }

        private void MarkResizeChildrenDirty()
        {
            for (var index = 0;
                 index < _resizeChildSnapshots.Count;
                 ++index)
            {
                var snapshot =
                    _resizeChildSnapshots[index];

                if (snapshot == null ||
                    snapshot.Rect == null)
                    continue;

                MarkRectDirty(snapshot.Rect);
            }
        }

        private void RestoreResizeChildOriginalLayouts()
        {
            for (var index = 0;
                 index < _resizeChildSnapshots.Count;
                 ++index)
            {
                var snapshot =
                    _resizeChildSnapshots[index];

                if (snapshot == null ||
                    snapshot.Rect == null ||
                    snapshot.OriginalLayout == null)
                    continue;

                ApplyLayout(
                    snapshot.Rect,
                    snapshot.OriginalLayout);
            }
        }

        private void UpdateActiveDrag()
        {
            if (_selected == null)
            {
                _dragUndoLayout = null;
                _dragMode = DragMode.None;
                ClearReparentCandidatePreview();
                return;
            }

            int pageDirection;
            var pageNavigationPressed =
                TryReadPageNavigation(out pageDirection);

            if (AltHeld())
            {
                UpdateReparentCandidatePreview((Vector2)Input.mousePosition);
                if (pageNavigationPressed)
                    CycleReparentCandidate(pageDirection);
            }
            else
            {
                ClearReparentCandidatePreview();
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
                                layout = _dragUndoLayout,
                                dependentLayouts =
                                    _dragMode == DragMode.Move ||
                                    !_resizeKeepChildrenFixed
                                        ? null
                                        : BuildResizeDependentUndo()
                            });
                    }

                    MarkSelectedDirty();

                    if (_dragMode != DragMode.Move &&
                        _resizeKeepChildrenFixed)
                    {
                        MarkResizeChildrenDirty();
                    }
                }

                _dragUndoLayout = null;
                _dragStartParent = null;
                _dragStartSiblingIndex = 0;
                _resizeChildSnapshots.Clear();
                _resizeKeepChildrenFixed = false;
                _dragMode = DragMode.None;
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                _dragUndoLayout = null;
                _dragStartParent = null;
                _dragStartSiblingIndex = 0;
                _resizeChildSnapshots.Clear();
                _resizeKeepChildrenFixed = false;
                _dragMode = DragMode.None;
                ClearReparentCandidatePreview();
                return;
            }

            var parentRect = _selected.parent as RectTransform;
            if (parentRect == null)
            {
                _dragUndoLayout = null;
                _resizeChildSnapshots.Clear();
                _resizeKeepChildrenFixed = false;
                _dragMode = DragMode.None;
                ClearReparentCandidatePreview();
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
            RestoreResizeChildOriginalLayouts();
            _resizeChildSnapshots.Clear();
            _resizeKeepChildrenFixed = false;
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

            var parentControlsResizedAxis =
                widthControlled ||
                heightControlled ||
                genericLayoutControlled;

            var needsLayoutElement =
                layoutElement != null ||
                parentControlsResizedAxis;

            var changed = false;
            var releasedFromParentLayout = false;

            if (needsLayoutElement)
            {
                if (layoutElement == null)
                {
                    layoutElement =
                        _selected.gameObject
                            .AddComponent<LayoutElement>();

                    layoutElement.minWidth = _dragStartWidth;
                    layoutElement.minHeight = _dragStartHeight;
                    layoutElement.preferredWidth = _dragStartWidth;
                    layoutElement.preferredHeight = _dragStartHeight;
                    layoutElement.flexibleWidth = 0f;
                    layoutElement.flexibleHeight = 0f;
                    changed = true;
                }

                // Si el padre gobierna el eje que el usuario esta cambiando,
                // preferredWidth/preferredHeight no bastan: el LayoutGroup
                // vuelve a imponer su calculo al final del frame y produce el
                // efecto de "presion"/rebote. Un resize manual pasa a ser una
                // geometria explicita y se excluye del calculo del padre.
                if (parentControlsResizedAxis &&
                    !layoutElement.ignoreLayout)
                {
                    layoutElement.ignoreLayout = true;
                    layoutElement.flexibleWidth = 0f;
                    layoutElement.flexibleHeight = 0f;
                    changed = true;
                    releasedFromParentLayout = true;
                }
                else if (!layoutElement.ignoreLayout)
                {
                    if (resizeWidth)
                    {
                        var width = Mathf.Max(MinimumWidth, targetWidth);
                        layoutElement.minWidth = width;
                        layoutElement.preferredWidth = width;
                        layoutElement.flexibleWidth = 0f;
                        changed = true;
                    }

                    if (resizeHeight)
                    {
                        var height = Mathf.Max(MinimumHeight, targetHeight);
                        layoutElement.minHeight = height;
                        layoutElement.preferredHeight = height;
                        layoutElement.flexibleHeight = 0f;
                        changed = true;
                    }
                }
            }

            // ContentSizeFitter puede reescribir el RectTransform aunque el
            // padre no tenga LayoutGroup. Se libera solo el eje editado.
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

            if (parentRect != null)
            {
                if (releasedFromParentLayout)
                {
                    // Se ejecuta en el mismo frame para que el padre deje de
                    // conducir al seleccionado antes de aplicar sus offsets.
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
                }
                else if (changed)
                {
                    LayoutRebuilder.MarkLayoutForRebuild(parentRect);
                }
            }

            return changed;
        }

        private void ApplyResize(Vector2 delta)
        {
            if (_selected == null)
                return;

            var dragChildren = ShiftHeld();

            // Si MAYUS se pulsa a mitad del arrastre, tomamos el estado local
            // actual de los hijos como referencia desde ese instante.
            if (dragChildren &&
                !_resizeKeepChildrenFixed)
            {
                CaptureResizeChildSnapshots();
            }

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
                left || right;

            var resizeHeight =
                top || bottom;

            var targetWidth =
                _dragStartWidth;

            if (left)
                targetWidth -= resizeDelta.x;
            else if (right)
                targetWidth += resizeDelta.x;

            var targetHeight =
                _dragStartHeight;

            if (bottom)
                targetHeight -= resizeDelta.y;
            else if (top)
                targetHeight += resizeDelta.y;

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

            _selected.offsetMin = offsetMin;
            _selected.offsetMax = offsetMax;

            // SIN MAYUS: se conserva exactamente el comportamiento normal del
            // resize; anchors/layout pueden reajustar el interior.
            //
            // CON MAYUS: NO se escala proporcionalmente el padre. Se recuperan
            // las coordenadas LOCALES de los hijos tomadas al iniciar MAYUS.
            // De este modo los hijos son arrastrados por el nuevo rect del
            // padre en vez de quedar congelados en su posicion mundial.
            if (dragChildren)
            {
                RestoreResizeChildOriginalLayouts();
            }

            _resizeKeepChildrenFixed = dragChildren;
        }

        private void InvalidateReparentCandidateUniverse()
        {
            _reparentCandidateUniverse.Clear();
            _reparentCandidateRootCanvas = null;
            ClearReparentCandidatePreview();
        }

        private void UpdateReparentCandidatePreview(
            Vector2 screenPoint)
        {
            if (_selected == null)
            {
                ClearReparentCandidatePreview();
                return;
            }

            var selectedId = _selected.gameObject.GetInstanceID();
            var pointerChanged =
                !_reparentCandidatePointerValid ||
                (screenPoint - _reparentCandidatePointer).sqrMagnitude > 1f ||
                _reparentCandidateSelectedInstanceId != selectedId;

            if (!pointerChanged &&
                _reparentCandidate != null &&
                _reparentCandidate.gameObject.activeInHierarchy)
return;

            RebuildReparentCandidates(screenPoint);
        }

        private void RebuildReparentCandidates(
            Vector2 screenPoint)
        {
            _reparentCandidates.Clear();
            _reparentCandidate = null;
            _reparentCandidateIndex = -1;
            _reparentCandidatePointer = screenPoint;
            _reparentCandidatePointerValid = true;
            _reparentCandidateSelectedInstanceId =
                _selected != null
                    ? _selected.gameObject.GetInstanceID()
                    : 0;

            if (_selected == null)
                return;

            var selectedCanvas =
                _selected.GetComponentInParent<Canvas>();
            if (selectedCanvas == null)
                return;

            var rootCanvas =
                selectedCanvas.rootCanvas != null
                    ? selectedCanvas.rootCanvas
                    : selectedCanvas;

            EnsureReparentCandidateUniverse(rootCanvas);

            for (var index = 0;
                 index < _reparentCandidateUniverse.Count;
                 ++index)
            {
                var rect = _reparentCandidateUniverse[index];
                if (rect == null ||
                    !rect.gameObject.activeInHierarchy ||
                    rect == _selected ||
                    rect == _selected.parent ||
                    rect.IsChildOf(_selected))
                    continue;

                var canvas = rect.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.rootCanvas != rootCanvas)
                    continue;

                if (!RectTransformUtility.RectangleContainsScreenPoint(
                        rect,
                        screenPoint,
                        GetCanvasCamera(canvas)))
                    continue;

                _reparentCandidates.Add(rect);
            }

            _reparentCandidates.Sort(CompareReparentCandidates);

            if (_reparentCandidates.Count == 0)
                return;

            _reparentCandidateIndex = 0;
            _reparentCandidate = _reparentCandidates[0];
        }


        private void EnsureReparentCandidateUniverse(
            Canvas rootCanvas)
        {
            if (rootCanvas == null)
            {
                _reparentCandidateUniverse.Clear();
                _reparentCandidateRootCanvas = null;
                return;
            }

            if (_reparentCandidateRootCanvas == rootCanvas &&
                _reparentCandidateUniverse.Count > 0)
                return;

            _reparentCandidateUniverse.Clear();
            _reparentCandidateRootCanvas = rootCanvas;

            var rects =
                rootCanvas.GetComponentsInChildren<RectTransform>(false);

            for (var index = 0; index < rects.Length; ++index)
            {
                var rect = rects[index];
                if (rect != null)
                    _reparentCandidateUniverse.Add(rect);
            }
        }

        private static int CompareReparentCandidates(
            RectTransform left,
            RectTransform right)
        {
            if (left == right)
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            var leftDepth = GetTransformHierarchyDepth(left);
            var rightDepth = GetTransformHierarchyDepth(right);
            if (leftDepth != rightDepth)
                return rightDepth.CompareTo(leftDepth);

            var leftGraphic = left.GetComponent<Graphic>();
            var rightGraphic = right.GetComponent<Graphic>();
            var leftVisualDepth =
                leftGraphic != null && leftGraphic.canvasRenderer != null
                    ? leftGraphic.canvasRenderer.absoluteDepth
                    : int.MinValue;
            var rightVisualDepth =
                rightGraphic != null && rightGraphic.canvasRenderer != null
                    ? rightGraphic.canvasRenderer.absoluteDepth
                    : int.MinValue;
            if (leftVisualDepth != rightVisualDepth)
                return rightVisualDepth.CompareTo(leftVisualDepth);

            var siblingComparison =
                right.GetSiblingIndex().CompareTo(left.GetSiblingIndex());
            if (siblingComparison != 0)
                return siblingComparison;

            return string.Compare(
                left.name,
                right.name,
                StringComparison.Ordinal);
        }

        private static int GetTransformHierarchyDepth(Transform transform)
        {
            var depth = 0;
            var current = transform;
            while (current != null)
            {
                ++depth;
                current = current.parent;
            }
            return depth;
        }

        private void CycleReparentCandidate(int direction)
        {
            if (_reparentCandidates.Count == 0)
            {
                UpdateReparentCandidatePreview((Vector2)Input.mousePosition);
                if (_reparentCandidates.Count == 0)
                    return;
            }

            var step = direction >= 0 ? 1 : -1;
            _reparentCandidateIndex =
                (_reparentCandidateIndex + step + _reparentCandidates.Count) %
                _reparentCandidates.Count;
            _reparentCandidate =
                _reparentCandidates[_reparentCandidateIndex];
        }

        private void ClearReparentCandidatePreview()
        {
            _reparentCandidate = null;
            _reparentCandidates.Clear();
            _reparentCandidateIndex = -1;
            _reparentCandidatePointerValid = false;
            _reparentCandidateSelectedInstanceId = 0;
            _reparentCandidateUniverse.Clear();
            _reparentCandidateRootCanvas = null;
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

            ClearReparentCandidatePreview();

            var graphic =
                _selected.GetComponent<Graphic>();

            string textValue;
            var hasText =
                TryGetEditableText(
                    _selected,
                    out textValue);

            EnsureReloadBaseline(_selected);

            _propertyTarget = _selected;
            _propertyUndoLayout =
                CaptureLayout(
                    _propertyTarget,
                    string.Empty);

            _propertyWidthText =
                FormatEditableFloat(
                    Mathf.Max(MinimumWidth, _propertyTarget.rect.width));
            _propertyHeightText =
                FormatEditableFloat(
                    Mathf.Max(MinimumHeight, _propertyTarget.rect.height));

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
                    out _propertyOriginalFontSizeMax,
                    out _propertyOriginalBold);

            _propertyDraftFontSize = _propertyOriginalFontSize;
            _propertyDraftAutoSize = _propertyOriginalAutoSize;
            _propertyDraftFontSizeMin = _propertyOriginalFontSizeMin;
            _propertyDraftFontSizeMax = _propertyOriginalFontSizeMax;
            _propertyDraftBold = _propertyOriginalBold;
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

                    MarkRectDirty(target);
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
                if (_propertyUndoLayout != null)
                    ApplyLayout(_propertyTarget, _propertyUndoLayout);

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
                        _propertyOriginalFontSizeMax,
                        true,
                        _propertyOriginalBold);
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
            _propertyWidthText = string.Empty;
            _propertyHeightText = string.Empty;
            _propertyHasColor = false;
            _propertyHasText = false;
            _propertyOriginalText = string.Empty;
            _propertyDraftText = string.Empty;
            _propertyHasTextStyle = false;
            _propertyOriginalBold = false;
            _propertyDraftBold = false;
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
            if (_propertyTarget != null &&
                _propertyUndoLayout != null)
            {
                var currentLayout =
                    CaptureLayout(
                        _propertyTarget,
                        string.Empty);

                if (!LayoutEquals(
                        _propertyUndoLayout,
                        currentLayout))
                    return true;
            }

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
                 !Mathf.Approximately(_propertyOriginalFontSizeMax, _propertyDraftFontSizeMax) ||
                 _propertyOriginalBold != _propertyDraftBold))
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
                    _propertyDraftFontSizeMax,
                    true,
                    _propertyDraftBold);
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

        private void CopyPropertyTargetSize()
        {
            if (_propertyTarget == null)
                return;

            _sizeClipboard =
                new Vector2(
                    Mathf.Max(MinimumWidth, _propertyTarget.rect.width),
                    Mathf.Max(MinimumHeight, _propertyTarget.rect.height));

            _sizeClipboardValid = true;
        }

        private void PastePropertyTargetSize()
        {
            if (_propertyTarget == null ||
                !_sizeClipboardValid)
                return;

            ApplyPropertyTargetSize(
                _sizeClipboard.x,
                _sizeClipboard.y);
        }

        private void ApplyPropertyTargetSize(
            float width,
            float height)
        {
            if (_propertyTarget == null)
                return;

            width = Mathf.Max(MinimumWidth, width);
            height = Mathf.Max(MinimumHeight, height);

            var previousSelected = _selected;
            _selected = _propertyTarget;

            _dragStartWidth =
                Mathf.Max(MinimumWidth, _propertyTarget.rect.width);
            _dragStartHeight =
                Mathf.Max(MinimumHeight, _propertyTarget.rect.height);

            ApplyManagedLayoutSize(
                true,
                true,
                width,
                height);

            _propertyTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                width);
            _propertyTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                height);

            var parentRect =
                _propertyTarget.parent as RectTransform;

            if (parentRect != null)
                LayoutRebuilder.MarkLayoutForRebuild(parentRect);

            _propertyWidthText = FormatEditableFloat(width);
            _propertyHeightText = FormatEditableFloat(height);

            _selected = previousSelected;
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
            var height = 478f;

            if (_propertyHasText)
                height += 58f;
            if (_propertyHasTextStyle)
                height += 76f;
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

            GUI.Label(new Rect(x, y, 54f, 22f), "Ancho");
            var nextWidthText =
                GUI.TextField(
                    new Rect(x + 56f, y, 82f, 24f),
                    _propertyWidthText ?? string.Empty);

            if (nextWidthText != _propertyWidthText)
            {
                _propertyWidthText = nextWidthText;
                float parsedWidth;
                if (TryParseEditableFloat(nextWidthText, out parsedWidth))
                {
                    float currentHeight;
                    if (!TryParseEditableFloat(_propertyHeightText, out currentHeight))
                        currentHeight = Mathf.Max(MinimumHeight, _propertyTarget.rect.height);

                    ApplyPropertyTargetSize(parsedWidth, currentHeight);
                }
            }

            GUI.Label(new Rect(x + 156f, y, 42f, 22f), "Alto");
            var nextHeightText =
                GUI.TextField(
                    new Rect(x + 198f, y, 82f, 24f),
                    _propertyHeightText ?? string.Empty);

            if (nextHeightText != _propertyHeightText)
            {
                _propertyHeightText = nextHeightText;
                float parsedHeight;
                if (TryParseEditableFloat(nextHeightText, out parsedHeight))
                {
                    float currentWidth;
                    if (!TryParseEditableFloat(_propertyWidthText, out currentWidth))
                        currentWidth = Mathf.Max(MinimumWidth, _propertyTarget.rect.width);

                    ApplyPropertyTargetSize(currentWidth, parsedHeight);
                }
            }

            y += 34f;

            if (GUI.Button(
                    new Rect(x, y, 148f, 24f),
                    "Copiar tamaño"))
            {
                CopyPropertyTargetSize();
            }

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _sizeClipboardValid;

            if (GUI.Button(
                    new Rect(x + 158f, y, 148f, 24f),
                    "Pegar tamaño"))
            {
                PastePropertyTargetSize();
            }

            GUI.enabled = previousEnabled;

            GUI.Label(
                new Rect(x + 318f, y, innerWidth - 318f, 24f),
                _sizeClipboardValid
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.#} x {1:0.#}",
                        _sizeClipboard.x,
                        _sizeClipboard.y)
                    : "Sin tamaño copiado");

            y += 36f;

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

                y += 30f;

                var nextBold =
                    GUI.Toggle(
                        new Rect(x + 64f, y, 110f, 24f),
                        _propertyDraftBold,
                        "Negrita");
                if (nextBold != _propertyDraftBold)
                {
                    _propertyDraftBold = nextBold;
                    ApplyPropertyDraft();
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
                var nextOutlineWidthText =
                    GUI.TextField(
                        new Rect(x + 210f, y, 56f, 24f),
                        _propertyOutlineWidthText ?? string.Empty);
                if (nextOutlineWidthText != _propertyOutlineWidthText)
                {
                    _propertyOutlineWidthText = nextOutlineWidthText;
                    float parsed;
                    if (TryParseEditableFloat(nextOutlineWidthText, out parsed))
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
                ShouldPersistTextContent(rect) &&
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

            if (hasTextOverride &&
                ShouldPersistTextContent(rect))
            {
                SetEditableText(
                    rect,
                    textValue ?? string.Empty);
            }
        }

        private static bool ShouldPersistTextContent(
            RectTransform rect)
        {
            if (rect == null)
                return false;

            for (var current = rect;
                 current != null;
                 current = current.parent as RectTransform)
            {
                if (current.GetComponent<TMP_InputField>() != null ||
                    current.GetComponent<TMP_Dropdown>() != null ||
                    current.GetComponent<InputField>() != null ||
                    current.GetComponent<Dropdown>() != null)
                    return false;
            }

            return true;
        }

        private static bool ShouldPersistTextStyle(
            RectTransform rect)
        {
            // El CONTENIDO de InputField/Dropdown sigue siendo dinamico y no se
            // guarda como override. Su ESTILO si es una edicion visual real:
            // tamano, autoajuste y negrita deben persistir aunque el control se
            // destruya y reconstruya al cambiar de pestana.
            return rect != null;
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
            out float textFontSizeMax,
            out bool textBold)
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
            textBold = false;

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
                ShouldPersistTextStyle(rect) &&
                TryGetTextStyle(
                    rect,
                    out textFontSize,
                    out textAutoSize,
                    out textFontSizeMin,
                    out textFontSizeMax,
                    out textBold);
        }

        private static void ApplyExtendedAppearance(
            RectTransform rect,
            bool metadataKnown,
            bool applyOutline,
            bool applyTextStyle,
            bool hasOutline,
            bool outlineEnabled,
            Color outlineColor,
            Vector2 outlineDistance,
            bool outlineUseGraphicAlpha,
            bool hasTextStyle,
            float textFontSize,
            bool textAutoSize,
            float textFontSizeMin,
            float textFontSizeMax,
            bool textBoldKnown,
            bool textBold)
        {
            if (rect == null || !metadataKnown)
                return;

            var outline = rect.GetComponent<Outline>();

            if (applyOutline && hasOutline)
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
            else if (applyOutline && outline != null)
            {
                outline.enabled = false;
                UnityEngine.Object.Destroy(outline);
            }

            if (applyTextStyle &&
                hasTextStyle &&
                ShouldPersistTextStyle(rect))
            {
                SetTextStyle(
                    rect,
                    textFontSize,
                    textAutoSize,
                    textFontSizeMin,
                    textFontSizeMax,
                    textBoldKnown,
                    textBold);
            }
        }

        private static bool TryGetTextStyle(
            RectTransform rect,
            out float fontSize,
            out bool autoSize,
            out float fontSizeMin,
            out float fontSizeMax,
            out bool bold)
        {
            fontSize = -1f;
            autoSize = false;
            fontSizeMin = -1f;
            fontSizeMax = -1f;
            bold = false;

            if (rect == null)
                return false;

            var tmp = rect.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                fontSize = tmp.fontSize;
                autoSize = tmp.enableAutoSizing;
                fontSizeMin = tmp.fontSizeMin;
                fontSizeMax = tmp.fontSizeMax;
                bold = (tmp.fontStyle & FontStyles.Bold) != 0;
                return true;
            }

            var legacy = rect.GetComponent<Text>();
            if (legacy != null)
            {
                fontSize = legacy.fontSize;
                autoSize = legacy.resizeTextForBestFit;
                fontSizeMin = legacy.resizeTextMinSize;
                fontSizeMax = legacy.resizeTextMaxSize;
                bold = legacy.fontStyle == FontStyle.Bold ||
                       legacy.fontStyle == FontStyle.BoldAndItalic;
                return true;
            }

            return false;
        }

        private static bool SetTextStyle(
            RectTransform rect,
            float fontSize,
            bool autoSize,
            float fontSizeMin,
            float fontSizeMax,
            bool boldKnown,
            bool bold)
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
                if (boldKnown)
                {
                    if (bold)
                        tmp.fontStyle |= FontStyles.Bold;
                    else
                        tmp.fontStyle &= ~FontStyles.Bold;
                }
                return true;
            }

            var legacy = rect.GetComponent<Text>();
            if (legacy != null)
            {
                legacy.fontSize = Mathf.RoundToInt(fontSize);
                legacy.resizeTextForBestFit = autoSize;
                legacy.resizeTextMinSize = Mathf.RoundToInt(fontSizeMin);
                legacy.resizeTextMaxSize = Mathf.RoundToInt(fontSizeMax);
                if (boldKnown)
                {
                    var italic =
                        legacy.fontStyle == FontStyle.Italic ||
                        legacy.fontStyle == FontStyle.BoldAndItalic;
                    legacy.fontStyle = bold
                        ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                        : (italic ? FontStyle.Italic : FontStyle.Normal);
                }
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

            if (AltHeld() && _reparentCandidate != null)
                DrawReparentCandidate();

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
                "UI DESIGNER ON  |  ENTER: propiedades  |  Espacio: usar UI  |  Mayus: eje/arrastrar hijos + no empujar interior  |  RePag/AvPag: delante/detras  |  Alt+clic/arrastrar: cambiar padre  |  Alt+Supr: quitar nodo  |  Ctrl+C/V  |  Ctrl+S  |  Ctrl+L: cargar  |  Ctrl+Z  |  Supr  |  Ctrl+Alt+F12: salir",
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

        private void DrawReparentCandidate()
        {
            Vector2 bottomLeft;
            Vector2 topLeft;
            Vector2 topRight;
            Vector2 bottomRight;

            if (!TryGetScreenCorners(
                    _reparentCandidate,
                    out bottomLeft,
                    out topLeft,
                    out topRight,
                    out bottomRight))
                return;

            DrawScreenLine(bottomLeft, topLeft, ParentCandidateColor, ParentCandidateThickness);
            DrawScreenLine(topLeft, topRight, ParentCandidateColor, ParentCandidateThickness);
            DrawScreenLine(topRight, bottomRight, ParentCandidateColor, ParentCandidateThickness);
            DrawScreenLine(bottomRight, bottomLeft, ParentCandidateColor, ParentCandidateThickness);
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

    // Cobertura adicional para el InputManager de TPH. Se parchea
    // exclusivamente GetKeyDown; GetKey y GetKeyUp quedan nativos para evitar
    // coste continuo en todas las consultas de teclado del juego.
    [HarmonyPatch]
    internal static class RuntimeUiDesignerTh20EscapePatch
    {
        private static System.Reflection.MethodBase
            TargetMethod()
        {
            return AccessTools.Method(
                typeof(TH20.InputManager),
                "GetKeyDown",
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

    // Mientras RuntimeUiDesigner esta activo, el juego no recibe entradas de
    // teclado/botones/ejes. RuntimeUiDesigner lee UnityEngine.Input directamente,
    // por lo que su menu sigue funcionando. Esto bloquea coger/seleccionar
    // objetos, movimiento con flechas y zoom con la rueda. Mantener Espacio
    // conserva el modo temporal de paso al juego.
    [HarmonyPatch]
    internal static class RuntimeUiDesignerTh20BooleanInputPatch
    {
        private static IEnumerable<System.Reflection.MethodBase>
            TargetMethods()
        {
            var inputType = typeof(TH20.InputManager);
            var keySignature = new Type[] { typeof(KeyCode) };
            var buttonSignature = new Type[] { typeof(int) };

            var methods =
                new System.Reflection.MethodBase[]
                {
                    AccessTools.Method(inputType, "GetKey", keySignature),
                    AccessTools.Method(inputType, "GetKeyDown", keySignature),
                    AccessTools.Method(inputType, "GetKeyUp", keySignature),
                    AccessTools.Method(inputType, "GetButton", buttonSignature),
                    AccessTools.Method(inputType, "GetButtonDown", buttonSignature),
                    AccessTools.Method(inputType, "GetButtonUp", buttonSignature)
                };

            for (var index = 0; index < methods.Length; ++index)
            {
                if (methods[index] != null)
                    yield return methods[index];
            }
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!RuntimeUiDesignerPlugin.BlockWorldEditingForGame)
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
            if (!RuntimeUiDesignerPlugin.BlockWorldEditingForGame)
                return true;

            __result = 0f;
            return false;
        }
    }

    // Los sistemas de seleccion propios del editor consultan estas dos senales.
    // Se fuerzan a UI mientras RuntimeUiDesigner esta activo para impedir que
    // un clic del diseñador seleccione o recoja algo situado detras.
    [HarmonyPatch(typeof(TH20.InputManager), "get_IsMouseOverGui")]
    internal static class RuntimeUiDesignerTh20MouseOverUiPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (RuntimeUiDesignerPlugin.BlockWorldEditingForGame)
                __result = true;
        }
    }

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
            if (RuntimeUiDesignerPlugin.BlockWorldEditingForGame)
                __result = true;
        }
    }

}
