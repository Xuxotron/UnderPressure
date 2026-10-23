// Updated: 2026-10-14 - Agrupacion estructural del Editor desde GitHub beb96a79.
// Archivos originales fusionados SIN alterar sus clases o parches de Harmony.
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using TH20;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

// ===== BEGIN ORIGINAL: ScenarioCharacterViewer.cs =====
// Updated: 2026-10-14 - Materiales y colores nativos del personaje; sin sustitucion por Unlit.
// Destino: UnderPressure Editor/src/ScenarioCharacterViewer.cs
// No genera pacientes reales, no altera el hospital ni el flujo de contratacion.
namespace UnderPressure.Editor
{
    // Se acopla al menu NATIVO del editor, no al menu principal ni a la partida.
    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioCharacterViewerOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level level)
        {
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning) return;
            ScenarioCharacterViewer.Attach(__instance, level);
        }
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Destroy")]
    internal static class ScenarioCharacterViewerClosePatch
    {
        private static void Prefix() => ScenarioCharacterViewer.Detach();
    }

    internal static class ScenarioCharacterViewer
    {
        private const int PreviewLayer = 30;
        private static readonly Vector3 PreviewOrigin = new Vector3(15000f, -15000f, 15000f);
        private static readonly Color PanelColor = new Color(0.135f, 0.165f, 0.195f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.24f, 0.31f, 0.36f, 1f);
        private static readonly Color HighlightColor = new Color(0.24f, 0.46f, 0.49f, 1f);
        private static readonly Color ToggleInactiveColor = new Color(.58f, .68f, .74f, 1f);
        private static readonly Color ToggleActiveColor = new Color(.02f, .55f, .90f, 1f);
        private static readonly string[] CharacterTypeKeys =
        {
            "editor.character.type.random_patient", "editor.character.type.male_patient",
            "editor.character.type.female_patient", "editor.character.type.doctor",
            "editor.character.type.nurse", "editor.character.type.assistant",
            "editor.character.type.janitor"
        };

        private static GameObject _toggleRoot;
        private static GameObject _overlay;
        private static ScenarioCharacterViewerBehaviour _component;
        private static Level _level;
        private static TMP_Text _toggleText;
        private static Font _font;

        internal static bool IsOpen => _overlay != null;
        internal static Button ToggleButton => _toggleRoot == null
            ? null : _toggleRoot.GetComponent<Button>();

        private static string T(string key) => EditorLocalization.Get(key);
        private static string F(string key, params object[] arguments) =>
            EditorLocalization.Format(key, arguments);
        private static string CharacterTypeName(int index) => T(CharacterTypeKeys[index]);

        internal static void Attach(EditHospitalMenu menu, Level level)
        {
            Detach();
            if (menu == null || level == null) return;
            var canvas = menu.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _level = level;
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var template = AccessTools.Field(typeof(EditHospitalMenu), "_quitButton")?
                .GetValue(menu) as Button;
            if (template == null) return;
            var button = UnityEngine.Object.Instantiate(template,
                ScenarioEditorUiHierarchy.ParentFor(canvas), false);
            button.gameObject.name = "UP_CharacterViewerToggle";
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Toggle);
            button.interactable = true;
            button.gameObject.SetActive(true);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(110f, -12f);
            rt.sizeDelta = new Vector2(208f, 40f);
            _toggleRoot = button.gameObject;
            _toggleText = button.GetComponentInChildren<TMP_Text>(true);
            if (_toggleText != null) _toggleText.text = T("editor.character.button");
            RefreshToggleVisual();
            UnderPressureEditorPlugin.Log?.LogInfo("Visor: boton Personajes instalado en el editor.");
        }

        private static void Toggle()
        {
            if (_overlay != null)
            {
                Close();
                return;
            }
            if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning || _level == null)
                return;
            Open();
        }

        private static void Open()
        {
            if (_toggleRoot == null || _overlay != null) return;
            var canvas = _toggleRoot.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            ScenarioFogViewer.Close();
            ScenarioEditorStorageMenu.DeactivateNativeTools();
            ScenarioEditorTerrainPainter.DisableTools();
            try
            {
                _overlay = UI("UP_CharacterViewerOverlay",
                    ScenarioEditorUiHierarchy.ParentFor(canvas), typeof(Image));
                Stretch((RectTransform)_overlay.transform);
                _overlay.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.035f, 0.64f);
                _overlay.transform.SetAsLastSibling();

                var panel = UI("UP_CharacterViewerPanel", _overlay.transform, typeof(Image));
                var pr = (RectTransform)panel.transform;
                pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
                pr.pivot = new Vector2(0.5f, 0.5f);
                pr.anchoredPosition = Vector2.zero;
                pr.sizeDelta = new Vector2(470f, 825f);
                panel.GetComponent<Image>().color = PanelColor;

                CreateText(panel.transform, T("editor.character.title"), 25,
                    new Vector2(0f, -12f), new Vector2(420f, 34f), TextAnchor.MiddleCenter);

                var viewport = UI("UP_PortraitViewport", panel.transform, typeof(RawImage));
                var vr = (RectTransform)viewport.transform;
                vr.anchorMin = vr.anchorMax = new Vector2(0.5f, 1f);
                vr.pivot = new Vector2(0.5f, 1f);
                vr.sizeDelta = new Vector2(414f, 386f);
                vr.anchoredPosition = new Vector2(0f, -50f);
                viewport.GetComponent<RawImage>().color = Color.white;

                CreateText(panel.transform, T("editor.character.controls"), 14,
                    new Vector2(0f, -440f), new Vector2(412f, 25f), TextAnchor.MiddleCenter);

                var typePrevious = CreateButton(panel.transform, "PacienteAnterior", "◀",
                    new Vector2(43f, 37f), ButtonColor, () => _component?.MoveType(-1));
                SetTop(typePrevious.transform, -178f, -481f);
                var typeNext = CreateButton(panel.transform, "PacienteSiguiente", "▶",
                    new Vector2(43f, 37f), ButtonColor, () => _component?.MoveType(1));
                SetTop(typeNext.transform, 178f, -481f);
                var typeButton = CreateButton(panel.transform, "TipoDesplegable",
                    CharacterTypeName(0) + " ▼",
                    new Vector2(302f, 37f), ButtonColor, () => _component?.OpenTypeDropdown());
                SetTop(typeButton.transform, 0f, -481f);
                var typeText = typeButton.GetComponentInChildren<Text>();

                var illnessPrevious = CreateButton(panel.transform, "EnfermedadAnterior", "◀",
                    new Vector2(43f, 37f), ButtonColor, () => _component?.MoveIllness(-1));
                SetTop(illnessPrevious.transform, -178f, -529f);
                var illnessNext = CreateButton(panel.transform, "EnfermedadSiguiente", "▶",
                    new Vector2(43f, 37f), ButtonColor, () => _component?.MoveIllness(1));
                SetTop(illnessNext.transform, 178f, -529f);
                var illnessButton = CreateButton(panel.transform, "EnfermedadDesplegable",
                    T("editor.character.illness.none") + " ▼",
                    new Vector2(302f, 37f), ButtonColor, () => _component?.OpenIllnessDropdown());
                SetTop(illnessButton.transform, 0f, -529f);
                var illnessText = illnessButton.GetComponentInChildren<Text>();

                CreateText(panel.transform, T("editor.character.animation"), 16,
                    new Vector2(0f, -568f), new Vector2(395f, 22f), TextAnchor.MiddleCenter);
                var clipPrevious = CreateButton(panel.transform, "AnimacionAnterior", "◀",
                    new Vector2(43f, 37f), ButtonColor, () => _component?.MoveClip(-1));
                SetTop(clipPrevious.transform, -178f, -590f);
                var clipNext = CreateButton(panel.transform, "AnimacionSiguiente", "▶",
                    new Vector2(43f, 37f), ButtonColor, () => _component?.MoveClip(1));
                SetTop(clipNext.transform, 178f, -590f);
                var clipButton = CreateButton(panel.transform, "AnimacionDesplegable",
                    T("editor.character.animations") + "... ▼",
                    new Vector2(302f, 37f), ButtonColor, () => _component?.OpenClipDropdown());
                SetTop(clipButton.transform, 0f, -590f);
                var clipText = clipButton.GetComponentInChildren<Text>();

                var search = CreateSearchInput(panel.transform,
                    T("editor.character.search_animation"),
                    new Vector2(410f, 36f), new Vector2(0f, -638f));
                search.onValueChanged.AddListener(value => _component?.SetAnimationSearch(value));

                var random = CreateButton(panel.transform, "PersonajeAleatorio",
                    T("editor.character.random"),
                    new Vector2(132f, 42f), ButtonColor, () => _component?.Regenerate());
                SetTop(random.transform, -145f, -686f);
                var play = CreateButton(panel.transform, "Reproducir",
                    T("editor.character.play"),
                    new Vector2(132f, 42f), ButtonColor, () => _component?.Play());
                SetTop(play.transform, 0f, -686f);
                var pause = CreateButton(panel.transform, "Pausar",
                    T("editor.character.pause"),
                    new Vector2(132f, 42f), ButtonColor, () => _component?.Pause());
                SetTop(pause.transform, 145f, -686f);

                var status = CreateText(panel.transform, "", 14,
                    new Vector2(0f, -740f), new Vector2(425f, 55f), TextAnchor.MiddleCenter);

                _component = _overlay.AddComponent<ScenarioCharacterViewerBehaviour>();
                _component.Initialise(_level, panel.transform, viewport.GetComponent<RawImage>(),
                    typeText, illnessText, clipText, status);
                var drag = viewport.AddComponent<ScenarioCharacterViewerDrag>();
                drag.Target = _component;
                _toggleRoot.transform.SetAsLastSibling();
                RefreshToggleVisual();
            }
            catch (Exception e)
            {
                UnderPressureEditorPlugin.Log?.LogError("Visor: no pudo abrirse: " + e);
                Close();
            }
        }

        internal static void Close()
        {
            _component = null;
            if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            if (_toggleText != null) _toggleText.text = T("editor.character.button");
            RefreshToggleVisual();
        }

        private static void RefreshToggleVisual()
        {
            if (_toggleRoot == null) return;
            var image = _toggleRoot.GetComponent<Button>()?.image;
            var color = IsOpen ? ToggleActiveColor : ToggleInactiveColor;
            if (image != null) image.color = color;
        }

        internal static void Detach()
        {
            Close();
            if (_toggleRoot != null) UnityEngine.Object.Destroy(_toggleRoot);
            _toggleRoot = null;
            _toggleText = null;
            _level = null;
        }

        private static GameObject UI(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, components.Length == 0
                ? new[] { typeof(RectTransform) }
                : new[] { typeof(RectTransform) }.Concat(components).ToArray());
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreateButton(Transform parent, string name, string label,
            Vector2 size, Color color, Action action)
        {
            var go = UI(name, parent, typeof(Image), typeof(Button));
            ((RectTransform)go.transform).sizeDelta = size;
            go.GetComponent<Image>().color = color;
            var b = go.GetComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            b.onClick.AddListener(() => action());
            CreateText(go.transform, label, 18, Vector2.zero, size, TextAnchor.MiddleCenter);
            return go;
        }

        private static Text CreateText(Transform parent, string value, int size,
            Vector2 position, Vector2 dimensions, TextAnchor alignment)
        {
            var go = UI("Text", parent, typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = dimensions;
            rt.anchoredPosition = position;
            var t = go.GetComponent<Text>();
            t.text = value;
            t.font = _font;
            t.fontSize = size;
            t.alignment = alignment;
            t.color = new Color(0.96f, 0.97f, 0.98f, 1f);
            t.raycastTarget = false;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 12;
            t.resizeTextMaxSize = size;
            if (parent.GetComponent<Button>() != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            return t;
        }

        private static InputField CreateSearchInput(Transform parent, string placeholder,
            Vector2 size, Vector2 topPosition)
        {
            var root = UI("UP_Search", parent, typeof(Image), typeof(InputField));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = size;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = topPosition;
            var background = root.GetComponent<Image>();
            background.color = new Color(0.86f, 0.89f, 0.92f, 1f);
            var field = root.GetComponent<InputField>();
            field.targetGraphic = background;
            field.characterLimit = 90;
            var value = CreateText(root.transform, "", 17, Vector2.zero, size,
                TextAnchor.MiddleLeft);
            value.color = new Color(0.09f, 0.13f, 0.16f, 1f);
            value.resizeTextForBestFit = false;
            var valueRect = (RectTransform)value.transform;
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = new Vector2(10f, 1f);
            valueRect.offsetMax = new Vector2(-10f, -1f);
            var hint = CreateText(root.transform, placeholder, 16, Vector2.zero,
                size, TextAnchor.MiddleLeft);
            hint.color = new Color(0.37f, 0.43f, 0.47f, 1f);
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.offsetMin = new Vector2(10f, 1f);
            hintRect.offsetMax = new Vector2(-10f, -1f);
            field.textComponent = value;
            field.placeholder = hint;
            return field;
        }

        private static void SetTop(Transform transform, float x, float y)
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal sealed class ScenarioCharacterViewerBehaviour : MonoBehaviour
        {
            private readonly List<AnimationClip> _clips = new List<AnimationClip>();
            private readonly List<int> _visibleClipIndexes = new List<int>();
            private readonly List<IllnessDefinition> _illnesses = new List<IllnessDefinition>();
            private readonly List<CharModule.ModuleInstance> _maskInstances =
                new List<CharModule.ModuleInstance>();
            private readonly Dictionary<Renderer, Material[]> _originalMaterials =
                new Dictionary<Renderer, Material[]>();
            private readonly List<Material> _illnessMaterials = new List<Material>();
            // Materiales EXCLUSIVOS del visor: nunca modificar el original del juego.
            private readonly List<Material> _previewMaterials = new List<Material>();
            private GameObject _maskRoot;
            private GameObject _illnessParticles;
            private IllnessDefinition _currentIllness;
            private CharacterDefinition _currentDefinition;
            private Character.Sex _currentSex;
            private Material _eyes;
            private Material _skin;
            private ModularMeshMaterialBindings _hair;
            private Transform _panel;
            private GameObject _dropdown;
            private ScrollRect _dropdownScroll;
            private RectTransform _dropdownContent;
            private readonly List<Button> _dropdownRows = new List<Button>();
            private readonly List<Text> _dropdownRowTexts = new List<Text>();
            private readonly List<int> _dropdownFiltered = new List<int>();
            private string _animationQuery = string.Empty;
            private string _dropdownQuery = string.Empty;
            private DropdownKind _dropdownKind;
            private int _illnessIndex;
            private float _cameraPitch = 7f;
            private const float DropdownRowHeight = 35f;
            private enum DropdownKind { Type, Illness, Clip }
            private readonly List<CharModule.ModuleInstance> _instances =
                new List<CharModule.ModuleInstance>();
            private Level _level;
            private RawImage _image;
            private Text _typeLabel;
            private Text _illnessLabel;
            private Text _clipLabel;
            private Text _status;
            private RenderTexture _renderTexture;
            private GameObject _model;
            private GameObject _scene;
            private Camera _camera;
            private Animator _animator;
            private RuntimeAnimatorController _baseController;
            private AnimatorOverrideController _overrideController;
            private int _typeIndex;
            private int _clipIndex;
            private bool _playing = true;
            private float _rotation = 180f;
            private float _zoom = 1.35f;

            internal void Initialise(Level level, Transform panel, RawImage image, Text type, Text illness, Text clip, Text status)
            {
                _level = level;
                _panel = panel;
                _image = image;
                _typeLabel = type;
                _illnessLabel = illness;
                _clipLabel = clip;
                _status = status;
                _renderTexture = new RenderTexture(512, 720, 16, RenderTextureFormat.ARGB32)
                {
                    name = "UP_CharacterViewer_RT", antiAliasing = 2,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                _renderTexture.Create();
                _image.texture = _renderTexture;
                BuildStudio();
                ScanIllnesses();
                ScanAnimations();
                Regenerate();
                ApplyClip();
            }

            private void BuildStudio()
            {
                _scene = new GameObject("UP_CharacterPreviewStudio");
                _scene.transform.position = PreviewOrigin;
                var cameraGo = new GameObject("UP_CharacterPreviewCamera");
                cameraGo.transform.SetParent(_scene.transform, false);
                _camera = cameraGo.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.orthographicSize = _zoom;
                _camera.cullingMask = 1 << PreviewLayer;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.14f, 0.20f, 0.25f, 1f);
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = 30f;
                _camera.allowHDR = false;
                _camera.targetTexture = _renderTexture;

                var lightGo = new GameObject("UP_CharacterPreviewLight");
                lightGo.transform.SetParent(_scene.transform, false);
                lightGo.layer = PreviewLayer;
                lightGo.transform.localRotation = Quaternion.Euler(35f, -30f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.55f;
                light.cullingMask = 1 << PreviewLayer;
                light.shadows = LightShadows.None;
                AimCamera();
            }

            private void AimCamera()
            {
                if (_camera == null) return;
                var target = PreviewOrigin + new Vector3(0f, 0.94f, 0f);
                var radians = _cameraPitch * Mathf.Deg2Rad;
                _camera.transform.position = target +
                    new Vector3(0f, Mathf.Sin(radians) * 5.4f, -Mathf.Cos(radians) * 5.4f);
                _camera.transform.LookAt(target);
                _camera.orthographicSize = _zoom;
            }

            private void ScanAnimations()
            {
                _clips.Clear();
                var seen = new HashSet<int>();
                foreach (var clip in Resources.FindObjectsOfTypeAll<AnimationClip>())
                    if (clip != null && !clip.empty && seen.Add(clip.GetInstanceID()))
                        _clips.Add(clip);
                _clips.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                _clipIndex = _clips.FindIndex(c => c.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0);
                if (_clipIndex < 0) _clipIndex = 0;
                SetAnimationSearch(string.Empty);
                UnderPressureEditorPlugin.Log?.LogInfo("Visor: " + _clips.Count + " clips de animacion cargados.");
            }

            private void ScanIllnesses()
            {
                _illnesses.Clear();
                var seen = new HashSet<IllnessDefinition>();
                var levels = new HashSet<LevelConfig>();
                if (_level?.Config != null) levels.Add(_level.Config);

                // La lista anterior solo miraba las enfermedades del hospital abierto.
                // LevelOptions enumera los hospitales originales de todo el juego,
                // incluidos los DLC, independientemente del mapa que se edite.
                var options = _level?.App?.SandboxSettingsConfig?.LevelOptions;
                if (options != null)
                {
                    foreach (var option in options)
                    {
                        var config = option?.Level?.Instance;
                        if (config != null) levels.Add(config);
                    }
                }
                foreach (var levelConfig in levels)
                {
                    try
                    {
                        var config = levelConfig.GetCharacterManagerConfig();
                        if (config == null) continue;
                        if (config._weightedIllnesses != null)
                            foreach (var entry in config._weightedIllnesses)
                                AddVisualIllness(entry?.Definition?.Instance, seen);
                        if (config._weightedIllnessesTimePortal != null)
                            foreach (var entry in config._weightedIllnessesTimePortal)
                                AddVisualIllness(entry?.Definition?.Instance, seen);
                    }
                    catch (Exception ex)
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning(
                            "Visor: no se pudieron leer enfermedades de " +
                            levelConfig.UniqueId + ": " + ex.Message);
                    }
                }

                // Incluir definiciones precargadas que no figuren en la tabla
                // ponderada de ningun hospital (p. ej., tratamientos especiales).
                // Unity solo devuelve aqui assets realmente cargados en memoria.
                var additional = 0;
                try
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(typeof(IllnessDefinition)))
                    {
                        foreach (var asset in Resources.FindObjectsOfTypeAll(typeof(IllnessDefinition)))
                        {
                            var illness = (object)asset as IllnessDefinition;
                            var before = _illnesses.Count;
                            AddVisualIllness(illness, seen);
                            additional += _illnesses.Count - before;
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Visor: exploracion de enfermedades precargadas: " + ex.Message);
                }
                _illnesses.Sort((a, b) => string.Compare(IllnessName(a), IllnessName(b),
                    StringComparison.OrdinalIgnoreCase));
                _illnessIndex = 0;
                RefreshIllnessLabel();
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Visor: " + _illnesses.Count + " enfermedades visuales globales; " +
                    levels.Count + " hospitales examinados, " + additional +
                    " definiciones extra precargadas.");
            }

            private void AddVisualIllness(IllnessDefinition illness,
                HashSet<IllnessDefinition> seen)
            {
                if (HasVisualIllness(illness) && seen.Add(illness)) _illnesses.Add(illness);
                var alternative = illness?.AlternativeIllness?.Instance;
                if (HasVisualIllness(alternative) && seen.Add(alternative))
                    _illnesses.Add(alternative);
            }

            private static bool HasVisualIllness(IllnessDefinition illness)
            {
                if (illness == null) return false;
                var mask = illness.ModularMask == null ? null : illness.ModularMask.Instance;
                return (mask != null && mask.CharacterModule != null) ||
                    illness.SkinSelectionOverride != null || illness.particleFX != null;
            }

            private static string IllnessName(IllnessDefinition illness)
            {
                if (illness == null) return T("editor.character.illness.none");
                var value = illness.Name.Translation;
                return string.IsNullOrEmpty(value) ? illness.ToString() : value;
            }

            private void RefreshIllnessLabel()
            {
                if (_illnessLabel != null)
                    _illnessLabel.text = (_typeIndex > 2 ? T("editor.character.illness.patients_only") :
                        (_illnessIndex == 0 ? T("editor.character.illness.none") :
                         IllnessName(_illnesses[_illnessIndex - 1]))) + " ▼";
            }

            internal void MoveType(int step)
            {
                CloseDropdown();
                _typeIndex = (_typeIndex + step + CharacterTypeKeys.Length) % CharacterTypeKeys.Length;
                Regenerate();
            }

            internal void MoveIllness(int step)
            {
                if (_typeIndex > 2) return;
                CloseDropdown();
                var n = _illnesses.Count + 1;
                _illnessIndex = (_illnessIndex + step + n) % n;
                RefreshIllnessLabel();
                ApplyIllness();
            }

            internal void SetAnimationSearch(string value)
            {
                _animationQuery = (value ?? string.Empty).Trim();
                _visibleClipIndexes.Clear();
                for (var i = 0; i < _clips.Count; i++)
                    if (_clips[i] != null && _clips[i].name.IndexOf(_animationQuery,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                        _visibleClipIndexes.Add(i);
                if (_visibleClipIndexes.Count > 0 && !_visibleClipIndexes.Contains(_clipIndex))
                {
                    _clipIndex = _visibleClipIndexes[0];
                    ApplyClip();
                }
                RefreshClipLabel();
                if (_dropdown != null && _dropdownKind == DropdownKind.Clip)
                    RefreshDropdownFilter();
            }

            internal void MoveClip(int step)
            {
                if (_visibleClipIndexes.Count == 0) return;
                CloseDropdown();
                var position = _visibleClipIndexes.IndexOf(_clipIndex);
                if (position < 0) position = 0;
                position = (position + step + _visibleClipIndexes.Count) % _visibleClipIndexes.Count;
                _clipIndex = _visibleClipIndexes[position];
                RefreshClipLabel();
                ApplyClip();
            }

            private void RefreshClipLabel()
            {
                if (_clipLabel == null) return;
                if (_visibleClipIndexes.Count == 0)
                {
                    _clipLabel.text = T("editor.character.no_results") + " ▼";
                    return;
                }
                var position = _visibleClipIndexes.IndexOf(_clipIndex);
                if (position < 0) position = 0;
                _clipLabel.text = _clips[_clipIndex].name + " (" + (position + 1) + "/" +
                    _visibleClipIndexes.Count + ") ▼";
            }

            internal void OpenTypeDropdown() => OpenDropdown(DropdownKind.Type);
            internal void OpenIllnessDropdown()
            {
                if (_typeIndex <= 2) OpenDropdown(DropdownKind.Illness);
            }
            internal void OpenClipDropdown() => OpenDropdown(DropdownKind.Clip);

            private void OpenDropdown(DropdownKind kind)
            {
                if (_panel == null) return;
                if (_dropdown != null && _dropdownKind == kind) { CloseDropdown(); return; }
                CloseDropdown();
                _dropdownKind = kind;
                _dropdownQuery = string.Empty;
                _dropdown = UI("UP_ViewerDropdown", _panel, typeof(Image));
                var rect = (RectTransform)_dropdown.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(425f, 505f);
                rect.anchoredPosition = new Vector2(0f, -135f);
                _dropdown.GetComponent<Image>().color = new Color(0.09f, 0.13f, 0.16f, 1f);
                _dropdown.transform.SetAsLastSibling();

                var heading = T(kind == DropdownKind.Type ? "editor.character.type" :
                    kind == DropdownKind.Illness ? "editor.character.visible_illnesses" :
                    "editor.character.animations");
                CreateText(_dropdown.transform, heading, 19,
                    new Vector2(-8f, -10f), new Vector2(355f, 30f), TextAnchor.MiddleCenter);
                var close = CreateButton(_dropdown.transform, "CerrarLista", "×",
                    new Vector2(32f, 30f), ButtonColor, CloseDropdown);
                SetTop(close.transform, 191f, -10f);
                var input = CreateSearchInput(_dropdown.transform,
                    T("editor.character.search_list"),
                    new Vector2(387f, 36f), new Vector2(0f, -46f));
                input.onValueChanged.AddListener(text =>
                {
                    _dropdownQuery = text ?? string.Empty;
                    RefreshDropdownFilter();
                });
                var listRoot = UI("UP_DropdownScroll", _dropdown.transform,
                    typeof(Image), typeof(ScrollRect));
                var listRect = (RectTransform)listRoot.transform;
                listRect.anchorMin = listRect.anchorMax = new Vector2(0.5f, 1f);
                listRect.pivot = new Vector2(0.5f, 1f);
                listRect.anchoredPosition = new Vector2(0f, -92f);
                listRect.sizeDelta = new Vector2(387f, 397f);
                listRoot.GetComponent<Image>().color = new Color(0.15f, 0.21f, 0.26f, 1f);
                var view = UI("UP_DropdownViewport", listRoot.transform, typeof(RectMask2D));
                var viewRect = (RectTransform)view.transform;
                Stretch(viewRect);
                var content = UI("UP_DropdownContent", view.transform);
                _dropdownContent = (RectTransform)content.transform;
                _dropdownContent.anchorMin = new Vector2(0f, 1f);
                _dropdownContent.anchorMax = new Vector2(1f, 1f);
                _dropdownContent.pivot = new Vector2(0.5f, 1f);
                _dropdownContent.anchoredPosition = Vector2.zero;
                _dropdownScroll = listRoot.GetComponent<ScrollRect>();
                _dropdownScroll.horizontal = false;
                _dropdownScroll.vertical = true;
                _dropdownScroll.movementType = ScrollRect.MovementType.Clamped;
                _dropdownScroll.scrollSensitivity = 35f;
                _dropdownScroll.viewport = viewRect;
                _dropdownScroll.content = _dropdownContent;
                _dropdownScroll.onValueChanged.AddListener(value => RefreshDropdownRows());
                _dropdownRows.Clear();
                _dropdownRowTexts.Clear();
                for (var i = 0; i < 15; i++)
                {
                    var row = CreateButton(_dropdownContent, "Option" + i, "",
                        new Vector2(380f, 33f), ButtonColor, () => { });
                    var index = i;
                    var rowButton = row.GetComponent<Button>();
                    rowButton.onClick.RemoveAllListeners();
                    rowButton.onClick.AddListener(() => SelectDropdownRow(index));
                    var rowRect = (RectTransform)row.transform;
                    rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
                    rowRect.pivot = new Vector2(0.5f, 1f);
                    _dropdownRows.Add(row.GetComponent<Button>());
                    _dropdownRowTexts.Add(row.GetComponentInChildren<Text>());
                }
                RefreshDropdownFilter();
            }

            private void RefreshDropdownFilter()
            {
                if (_dropdown == null) return;
                _dropdownFiltered.Clear();
                int count = _dropdownKind == DropdownKind.Type ? CharacterTypeKeys.Length :
                    _dropdownKind == DropdownKind.Illness ? _illnesses.Count + 1 : _visibleClipIndexes.Count;
                for (var i = 0; i < count; i++)
                {
                    var label = DropdownOptionText(i);
                    if (label.IndexOf(_dropdownQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                        _dropdownFiltered.Add(i);
                }
                _dropdownContent.sizeDelta = new Vector2(0f,
                    Mathf.Max(397f, _dropdownFiltered.Count * DropdownRowHeight));
                _dropdownScroll.verticalNormalizedPosition = 1f;
                RefreshDropdownRows();
            }

            private string DropdownOptionText(int i)
            {
                if (_dropdownKind == DropdownKind.Type) return CharacterTypeName(i);
                if (_dropdownKind == DropdownKind.Illness)
                    return i == 0 ? T("editor.character.illness.none") :
                        IllnessName(_illnesses[i - 1]);
                return _clips[_visibleClipIndexes[i]].name;
            }

            private void RefreshDropdownRows()
            {
                if (_dropdown == null || _dropdownScroll == null) return;
                var viewHeight = _dropdownScroll.viewport.rect.height;
                var maximum = Mathf.Max(0f, _dropdownContent.rect.height - viewHeight);
                var offset = Mathf.Clamp((1f - _dropdownScroll.verticalNormalizedPosition) * maximum,
                    0f, maximum);
                var first = Mathf.FloorToInt(offset / DropdownRowHeight);
                for (var i = 0; i < _dropdownRows.Count; i++)
                {
                    var index = first + i;
                    var valid = index < _dropdownFiltered.Count;
                    _dropdownRows[i].gameObject.SetActive(valid);
                    if (!valid) continue;
                    _dropdownRows[i].gameObject.name = "Option_" + index;
                    var row = (RectTransform)_dropdownRows[i].transform;
                    row.anchoredPosition = new Vector2(0f, -(index * DropdownRowHeight));
                    _dropdownRowTexts[i].text = DropdownOptionText(_dropdownFiltered[index]);
                }
            }

            private void SelectDropdownRow(int poolIndex)
            {
                if (_dropdown == null || poolIndex >= _dropdownRows.Count) return;
                var viewHeight = _dropdownScroll.viewport.rect.height;
                var max = Mathf.Max(0f, _dropdownContent.rect.height - viewHeight);
                var offset = Mathf.Clamp((1f - _dropdownScroll.verticalNormalizedPosition) * max, 0f, max);
                var index = Mathf.FloorToInt(offset / DropdownRowHeight) + poolIndex;
                if (index < 0 || index >= _dropdownFiltered.Count) return;
                var chosen = _dropdownFiltered[index];
                var kind = _dropdownKind;
                CloseDropdown();
                if (kind == DropdownKind.Type)
                {
                    _typeIndex = chosen;
                    Regenerate();
                }
                else if (kind == DropdownKind.Illness)
                {
                    _illnessIndex = chosen;
                    RefreshIllnessLabel();
                    ApplyIllness();
                }
                else
                {
                    _clipIndex = _visibleClipIndexes[chosen];
                    RefreshClipLabel();
                    ApplyClip();
                }
            }

            private void CloseDropdown()
            {
                if (_dropdown != null) UnityEngine.Object.Destroy(_dropdown);
                _dropdown = null;
                _dropdownScroll = null;
                _dropdownContent = null;
                _dropdownRows.Clear();
                _dropdownRowTexts.Clear();
                _dropdownFiltered.Clear();
            }

            internal void Regenerate()
            {
                if (_level == null) return;
                _typeLabel.text = CharacterTypeName(_typeIndex) + " ▼";
                RefreshIllnessLabel();
                ClearCharacter();
                try
                {
                    CharacterDefinition definition;
                    if (_typeIndex <= 2)
                    {
                        var config = _level.Config.GetCharacterManagerConfig();
                        definition = config == null || config._patientDefinition == null
                            ? null : config._patientDefinition.Instance;
                    }
                    else
                    {
                        var config = _level.Config.GetJobApplicantManagerConfig();
                        var staffDatabase = config == null || config.StaffDatabase == null
                            ? null : config.StaffDatabase.Instance;
                        var staffType = _typeIndex == 3 ? StaffDefinition.Type.Doctor :
                            _typeIndex == 4 ? StaffDefinition.Type.Nurse :
                            _typeIndex == 5 ? StaffDefinition.Type.Assistant : StaffDefinition.Type.Janitor;
                        definition = staffDatabase == null ? null : staffDatabase.GetDefinition(staffType);
                    }
                    if (definition == null || definition.RootModule == null || definition.RigPrefab == null)
                    {
                        SetStatus(T("editor.character.error.definition"));
                        return;
                    }

                    var sex = _typeIndex == 1 ? Character.Sex.Male :
                              _typeIndex == 2 ? Character.Sex.Female :
                              (UnityEngine.Random.value < 0.5f ? Character.Sex.Male : Character.Sex.Female);
                    _model = new GameObject("UP_CharacterPreviewModel");
                    _model.transform.SetParent(_scene.transform, false);
                    _model.transform.localRotation = Quaternion.Euler(0f, _rotation, 0f);
                    var rig = UnityEngine.Object.Instantiate(definition.RigPrefab, _model.transform, false);
                    rig.name = definition.RigPrefab.name;
                    foreach (var old in rig.GetComponentsInChildren<Animator>(true))
                        UnityEngine.Object.DestroyImmediate(old);

                    _currentDefinition = definition;
                    _currentSex = sex;
                    CharModuleUtils.GetCoreRandomAssets(definition.SkinHairMaterialDatabase,
                        definition.EyeMaterialSelection, out _eyes, out _skin, out _hair);
                    var assets = new List<CharModule.CharModuleAssets>();
                    var category = definition.GetModularCategory(sex);
                    if (_level.Config.CharactersCanWearSummerClothes) category |= CharModule.Category.Summer;
                    if (_level.Config.CharactersCanWearWinterClothes) category |= CharModule.Category.Winter;
                    definition.RootModule.GetRandomCharacterData(category, _eyes, _skin, _hair, assets);
                    CharModuleUtils.BuildModularCharacterGameObject(assets, _model.transform,
                        rig.GetComponentsInChildren<Transform>(true), true, null, _instances);
                    DisableRoomLighting(_instances);
                    if (_instances.Count == 0) throw new InvalidOperationException("El generador devolvió un modelo vacío.");
                    foreach (var t in _model.GetComponentsInChildren<Transform>(true))
                        t.gameObject.layer = PreviewLayer;
                    foreach (var renderer in _model.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled = true;
                        if (renderer is SkinnedMeshRenderer skinRenderer)
                            skinRenderer.updateWhenOffscreen = true;
                        // El visor tiene una luz propia. No activar RoomLighting:
                        // en un estudio sin coordenadas de hospital ese shader
                        // puede colorear de naranja el personaje y sus prendas.
                    }
                    // Conservar la apariencia aleatoria producida por CharModuleUtils.
                    // No sustituir shaders ni colores del personaje generado.
                    _animator = _model.AddComponent<Animator>();
                    _animator.avatar = definition._avatar;
                    _animator.applyRootMotion = false;
                    _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                    var controllers = definition._locomotionAnimGraph;
                    _baseController = controllers == null ? null : controllers.FirstOrDefault(c => c != null);
                    if (_baseController == null)
                    {
                        SetStatus(T("editor.character.error.controller"));
                        return;
                    }
                    _animator.runtimeAnimatorController = _baseController;
                    ApplyIllness();
                    ApplyClip();
                    SetStatus(F("editor.character.generated", _clips.Count));
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogError("Visor: fallo generando personaje: " + ex);
                    SetStatus(T("editor.character.error.generate"));
                    ClearCharacter();
                }
            }

            private static void DisableRoomLighting(List<CharModule.ModuleInstance> instances)
            {
                foreach (var instance in instances)
                    foreach (var material in instance.OriginalMaterials ?? new Material[0])
                    {
                        if (material == null || !material.HasProperty("_ApplyRoomLighting")) continue;
                        material.SetFloat("_ApplyRoomLighting", 0f);
                        material.EnableKeyword("_APPLYROOMLIGHTING_OFF");
                    }
            }

            private void ApplyIllness()
            {
                ClearIllness();
                RefreshIllnessLabel();
                if (_typeIndex > 2 || _illnessIndex == 0 || _model == null || _currentDefinition == null)
                    return;
                try
                {
                    var illness = _illnesses[_illnessIndex - 1];
                    _currentIllness = illness;
                    var mask = illness.ModularMask == null ? null : illness.ModularMask.Instance;
                    if (mask != null && mask.CharacterModule != null)
                    {
                        _maskRoot = new GameObject("UP_IllnessVisualMask");
                        _maskRoot.transform.SetParent(_model.transform, false);
                        foreach (var module in _instances)
                        {
                            if (module.Renderer == null) continue;
                            if (mask.Tags == (CharModule.Tags)0 ||
                                (mask.Tags & module.Tags) != (CharModule.Tags)0)
                                module.Renderer.gameObject.SetActive(false);
                        }
                        var data = new List<CharModule.CharModuleAssets>();
                        var category = _currentDefinition.GetModularCategory(_currentSex);
                        if (_level.Config.CharactersCanWearSummerClothes) category |= CharModule.Category.Summer;
                        if (_level.Config.CharactersCanWearWinterClothes) category |= CharModule.Category.Winter;
                        mask.CharacterModule.GetRandomCharacterData(category, _eyes, _skin, _hair, data);
                        var rig = _model.transform.GetChild(0);
                        CharModuleUtils.BuildModularCharacterGameObject(data, _maskRoot.transform,
                            rig.GetComponentsInChildren<Transform>(true), true, null, _maskInstances);
                        DisableRoomLighting(_maskInstances);
                        foreach (var renderer in _maskRoot.GetComponentsInChildren<Renderer>(true))
                        {
                            renderer.gameObject.layer = PreviewLayer;
                            renderer.enabled = true;
                            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                            // Conservar los materiales de las máscaras tal cual:
                            // RoomLighting se resuelve en el mundo, no en este visor.
                        }
                    }
                    if (illness.SkinSelectionOverride != null)
                    {
                        var selection = illness.SkinSelectionOverride.FindMatchingMaterial(_skin);
                        if (selection != null)
                        {
                            var skinMaterial = new Material(selection);
                            _illnessMaterials.Add(skinMaterial);
                            OverrideSkinMaterials(_instances, skinMaterial);
                            OverrideSkinMaterials(_maskInstances, skinMaterial);
                        }
                    }
                    if (illness.particleFX != null)
                    {
                        Transform parent = _model.transform;
                        string bone = illness.particleRoot == IllnessDefinition.ParticleRoot.Head
                            ? CharacterVisual.HeadSocketName :
                            illness.particleRoot == IllnessDefinition.ParticleRoot.Spine
                            ? CharacterVisual.SpineSocketName : "BASE_RIG:TSM3WorldJoint";
                        foreach (var t in _model.GetComponentsInChildren<Transform>(true))
                            if (t.name == bone) { parent = t; break; }
                        _illnessParticles = UnityEngine.Object.Instantiate(illness.particleFX, parent, false);
                        foreach (var t in _illnessParticles.GetComponentsInChildren<Transform>(true))
                            t.gameObject.layer = PreviewLayer;
                        _illnessParticles.SetActive(true);
                    }
                    // Conservar tambien los colores y materiales de las mascaras.
                    _animator?.Rebind();
                    ApplyClip();
                    SetStatus(F("editor.character.illness.visual", IllnessName(illness)));
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning("Visor: enfermedad no compatible: " + ex);
                    SetStatus(T("editor.character.error.illness"));
                }
            }

            private void OverrideSkinMaterials(List<CharModule.ModuleInstance> instances, Material skin)
            {
                foreach (var instance in instances)
                {
                    if (instance.Renderer == null || instance.MaterialModes == null) continue;
                    var original = instance.Renderer.sharedMaterials;
                    var replacement = (Material[])original.Clone();
                    var changed = false;
                    for (var i = 0; i < replacement.Length && i < instance.MaterialModes.Length; i++)
                        if (instance.MaterialModes[i] == CharModule.MaterialMode.Skin)
                        {
                            replacement[i] = skin;
                            changed = true;
                        }
                    if (changed)
                    {
                        _originalMaterials[instance.Renderer] = original;
                        instance.Renderer.sharedMaterials = replacement;
                    }
                }
            }

            private void ClearIllness()
            {
                foreach (var pair in _originalMaterials)
                    if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
                _originalMaterials.Clear();
                if (_illnessParticles != null) UnityEngine.Object.Destroy(_illnessParticles);
                _illnessParticles = null;
                if (_maskInstances.Count > 0)
                {
                    try { CharModuleUtils.DestroyModularInstances(_maskInstances); }
                    catch (Exception ex) { UnderPressureEditorPlugin.Log?.LogWarning("Visor: limpieza mascara: " + ex.Message); }
                    _maskInstances.Clear();
                }
                if (_maskRoot != null) UnityEngine.Object.Destroy(_maskRoot);
                _maskRoot = null;
                foreach (var instance in _instances)
                    if (instance.Renderer != null) instance.Renderer.gameObject.SetActive(true);
                foreach (var material in _illnessMaterials)
                    if (material != null) UnityEngine.Object.Destroy(material);
                _illnessMaterials.Clear();
                _currentIllness = null;
            }

            private void ApplyClip()
            {
                if (_animator == null || _baseController == null || _clips.Count == 0) return;
                try
                {
                    if (_overrideController != null)
                    {
                        _animator.runtimeAnimatorController = null;
                        UnityEngine.Object.Destroy(_overrideController);
                    }
                    // El juego usa Animator (no Animation legacy). Al sobrescribir
                    // los clips del controlador cargado se puede reproducir incluso
                    // un clip externo al controlador sin modificar assets originales.
                    _overrideController = new AnimatorOverrideController(_baseController);
                    var entries = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    _overrideController.GetOverrides(entries);
                    for (var i = 0; i < entries.Count; ++i)
                        entries[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                            entries[i].Key, _clips[_clipIndex]);
                    if (entries.Count == 0)
                    {
                        SetStatus(T("editor.character.error.no_clips"));
                        return;
                    }
                    _overrideController.ApplyOverrides(entries);
                    _animator.runtimeAnimatorController = _overrideController;
                    _animator.Rebind();
                    _animator.Update(0f);
                    _animator.speed = _playing ? 1f : 0f;
                    if (_animator.HasState(0, Animator.StringToHash("Base Layer.Idle")))
                        _animator.Play("Base Layer.Idle", 0, 0f);
                    else if (_animator.HasState(0, Animator.StringToHash("Idle")))
                        _animator.Play("Idle", 0, 0f);
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning("Visor: clip incompatible: " + ex.Message);
                    SetStatus(F("editor.character.error.clip", _clips[_clipIndex].name));
                }
            }

            internal void Play()
            {
                _playing = true;
                if (_animator != null) _animator.speed = 1f;
            }

            internal void Pause()
            {
                _playing = false;
                if (_animator != null) _animator.speed = 0f;
            }

            internal void Rotate(float amount)
            {
                _rotation += amount;
                if (_model != null) _model.transform.localRotation = Quaternion.Euler(0f, _rotation, 0f);
            }

            internal void OrbitVertical(float amount)
            {
                _cameraPitch = Mathf.Clamp(_cameraPitch + amount, -55f, 65f);
                AimCamera();
            }

            internal void Zoom(float amount)
            {
                _zoom = Mathf.Clamp(_zoom - amount * 0.13f, 0.8f, 2.2f);
                AimCamera();
            }

            private void LateUpdate()
            {
                if (!ScenarioEditorSession.Active || !ScenarioEditorFlow.IsRunning)
                {
                    ScenarioCharacterViewer.Detach();
                    return;
                }
                if (_camera != null && _renderTexture != null && _renderTexture.IsCreated())
                    _camera.Render();
            }

            private void SetStatus(string message)
            {
                if (_status != null) _status.text = message;
            }

            private void ClearCharacter()
            {
                CloseDropdown();
                ClearIllness();
                if (_overrideController != null)
                {
                    if (_animator != null) _animator.runtimeAnimatorController = null;
                    UnityEngine.Object.Destroy(_overrideController);
                    _overrideController = null;
                }
                if (_instances.Count > 0)
                {
                    try { CharModuleUtils.DestroyModularInstances(_instances); }
                    catch (Exception ex) { UnderPressureEditorPlugin.Log?.LogWarning("Visor: limpieza modular: " + ex.Message); }
                    _instances.Clear();
                }
                foreach (var mat in _previewMaterials)
                    if (mat != null) UnityEngine.Object.Destroy(mat);
                _previewMaterials.Clear();
                if (_model != null) UnityEngine.Object.Destroy(_model);
                _model = null;
                _animator = null;
                _baseController = null;
                _currentDefinition = null;
            }

            private void OnDestroy()
            {
                ClearCharacter();
                if (_scene != null) UnityEngine.Object.Destroy(_scene);
                if (_renderTexture != null)
                {
                    if (_image != null) _image.texture = null;
                    _renderTexture.Release();
                    UnityEngine.Object.Destroy(_renderTexture);
                }
            }
        }

        internal sealed class ScenarioCharacterViewerDrag : MonoBehaviour, IDragHandler, IScrollHandler
        {
            internal ScenarioCharacterViewerBehaviour Target;
            public void OnDrag(PointerEventData data)
            {
                Target?.Rotate(-data.delta.x * 0.65f);
                Target?.OrbitVertical(-data.delta.y * 0.25f);
            }
            public void OnScroll(PointerEventData data)
            {
                Target?.Zoom(data.scrollDelta.y);
            }
        }
    }
}
// ===== END ORIGINAL: ScenarioCharacterViewer.cs =====
