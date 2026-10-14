// Updated: 2026-10-13 - Ubicacion correcta de botones nativos arriba; extras 5 px sobre el panel.
// Conserva la jerarquia y los eventos nativos; no mueve el menu animado.
// Los botones nuevos sólo alteran la cámara y la representación de hospitales;
// no conceden estrellas, desbloqueos ni licencias DLC.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using I2.Loc;
using TH20;
using TH20.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed class ScenarioCareerMapControls : MonoBehaviour
    {
        private static readonly FieldInfo MenuButtonField =
            AccessTools.Field(typeof(MetagameButtonsMenu), "_optionsButton");
        private static readonly FieldInfo CareerButtonField =
            AccessTools.Field(typeof(MetagameButtonsMenu), "_careerGoalsButton");
        private static readonly FieldInfo LeaderboardButtonField =
            AccessTools.Field(typeof(MetagameButtonsMenu), "_leaderboardButton");
        private static readonly MethodInfo UpdateBoundsMethod =
            AccessTools.Method(typeof(MetagameMapUI), "SetCameraBoundsToMapPinBounds");
        private static readonly FieldInfo MeshField =
            AccessTools.Field(typeof(MapPinHospital), "_meshIcon");
        private static readonly FieldInfo HoverField =
            AccessTools.Field(typeof(MapPinHospital), "_cursorOver");
        private static readonly FieldInfo LockedField =
            AccessTools.Field(typeof(MapPinHospital), "_materialLocked");
        private static readonly FieldInfo LockedHoverField =
            AccessTools.Field(typeof(MapPinHospital), "_materialLockedHighlighted");
        private static readonly FieldInfo StarsField =
            AccessTools.Field(typeof(MapPinHospital), "_stars");
        private static readonly FieldInfo RecordField =
            AccessTools.Field(typeof(MapPinHospital), "_hospitalRecord");
        private static readonly FieldInfo MetagameField =
            AccessTools.Field(typeof(MapPinHospital), "_metagame");
        private static readonly FieldInfo CameraConfigField =
            AccessTools.Field(typeof(TopDownCameraLogic), "_config");
        private static readonly FieldInfo VisibilityMetagameField =
            AccessTools.Field(typeof(MapVisualsActivation), "_metagame");
        private static readonly FieldInfo VisibilityRecordField =
            AccessTools.Field(typeof(MapVisualsActivation), "_hospitalRecord");
        private static readonly FieldInfo VisibilityLevelField =
            AccessTools.Field(typeof(MapVisualsActivation), "_levelConfig");

        private static ScenarioCareerMapControls _current;
        private MetagameMap _map;
        private MetagameButtonsMenu _nativeMenu;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private DynamicButton[] _nativeButtons;
        private DynamicButton _zoomButton;
        private DynamicButton _hospitalsButton;
        private bool _moreZoom;
        private bool _allHospitals;
        private float _originalFarClip;
        private bool _initialised;
        // Distancias fijas: el hover nativo no reordena los botones.
        private readonly float[] _nativeWidths = new float[3];
        private readonly float[] _nativeHeights = new float[3];
        private bool _nativeMeasuresReady;
        private bool _canvasCallbackRegistered;

        internal static bool ShowingAllHospitals => _current != null && _current._allHospitals;

        internal static bool ExtraZoomFor(TopDownCameraLogic camera, out float scalar)
        {
            scalar = 0f;
            if (_current == null || !_current._moreZoom || _current._map == null ||
                _current._map.CameraLogic != camera)
                return false;
            var config = CameraConfigField?.GetValue(camera) as TopDownCameraLogic.Config;
            var max = config == null ? 1f : Mathf.Max(0.001f, config.MaxZoomScalar);
            scalar = Mathf.Clamp01(camera.CurrentZoomScalar / max);
            return true;
        }

        internal void Initialise(MetagameMap map, MetagameButtonsMenu menu)
        {
            if (_initialised || map == null || menu == null) return;
            _map = map;
            _nativeMenu = menu;
            _canvas = menu.GetComponentInParent<Canvas>();
            if (_canvas == null) return;
            _canvas = _canvas.rootCanvas;
            _canvasRect = _canvas.transform as RectTransform;
            if (_canvasRect == null) return;

            var options = MenuButtonField?.GetValue(menu) as DynamicButton;
            var career = CareerButtonField?.GetValue(menu) as DynamicButton;
            var leaderboard = LeaderboardButtonField?.GetValue(menu) as DynamicButton;
            if (options == null || career == null || leaderboard == null)
            {
                UnderPressureEditorPlugin.Log?.LogWarning(
                    "Minimapa: no se localizaron los tres botones nativos; no se recolocan.");
                return;
            }
            _nativeButtons = new[] { options, career, leaderboard };
            // Los tres botones se mantienen dentro del MetagameButtonsMenu original.
            // No mover su raiz: el Animator del juego restaura su transform.
            _originalFarClip = map.CameraLogic?.CameraComponent != null
                ? map.CameraLogic.CameraComponent.farClipPlane : 0f;
            _current = this;
            _initialised = true;
            try
            {
                // El prefab existente sirve como origen visual: no se inventan sprites ni shaders.
                _zoomButton = CreateNativeButton(options, "UP_MapMoreZoom",
                    EditorLocalization.Get("career.map.more_zoom"), ZoomPressed);
                _hospitalsButton = CreateNativeButton(options, "UP_MapAllHospitals",
                    EditorLocalization.Get("career.map.all_hospitals"), HospitalsPressed);
                ArrangeExtraButtons();
                Canvas.willRenderCanvases += PositionNativeButtonsBeforeRender;
                _canvasCallbackRegistered = true;
                UnderPressureEditorPlugin.Log?.LogInfo(
                    "Minimapa: posicionamiento visual nativo, sin reparentar ni alterar callbacks.");
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("Minimapa: error al crear botones: " + exception);
            }
        }

        private DynamicButton CreateNativeButton(DynamicButton original, string name, string text, Action callback)
        {
            var clone = Instantiate(original.gameObject, _canvasRect, false);
            clone.name = name;
            clone.SetActive(true);
            foreach (var l in clone.GetComponentsInChildren<Localize>(true))
                l.enabled = false;
            var button = clone.GetComponent<DynamicButton>();
            if (button == null)
            {
                Destroy(clone);
                return null;
            }
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => callback());
            button.interactable = true;
            button.SetTMPText(text);
            var r = clone.transform as RectTransform;
            if (r != null)
            {
                // Sólo las COPIAS tienen nueva ubicación; la medida original se conserva.
                var source = original.transform as RectTransform;
                if (source != null)
                    r.sizeDelta = source.rect.size;
                r.anchorMin = new Vector2(1f, 0f);
                r.anchorMax = new Vector2(1f, 0f);
                r.pivot = new Vector2(1f, 0f);
                r.localRotation = Quaternion.identity;
                r.localScale = Vector3.one;
            }
            // Si la plantilla incluía listeners de herramientas, se sustituyen arriba.
            var tip = clone.GetComponentInChildren<TooltipSpawner>(true);
            if (tip != null) tip.enabled = false;
            return button;
        }

        private void ArrangeExtraButtons()
        {
            if (_canvasRect == null) return;
            // Los botones adicionales van 5 px por encima del panel de fundacion.
            // Si la raiz del panel nativo incluye margen invisible, se usa una
            // distancia segura al borde inferior como alternativa.
            const float right = 14f;
            const float gap = 8f;
            float bottom = 164f;
            var foundation = _map?.HUD?.FindMenu<FoundationStatusMenu>(false);
            var frame = foundation == null ? null : foundation.transform as RectTransform;
            if (frame != null && frame.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                frame.GetWorldCorners(corners);
                float top = float.NegativeInfinity;
                float minX = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    var point = _canvasRect.InverseTransformPoint(corners[i]);
                    top = Mathf.Max(top, point.y);
                    minX = Mathf.Min(minX, point.x);
                }
                var bounds = _canvasRect.rect;
                if (top > bounds.yMin + 60f &&
                    top < bounds.yMin + bounds.height * 0.48f &&
                    minX > bounds.xMin + bounds.width * 0.45f)
                    bottom = top - bounds.yMin + 5f;
            }
            if (_hospitalsButton != null)
            {
                var r = _hospitalsButton.transform as RectTransform;
                if (r != null) r.anchoredPosition = new Vector2(-right, bottom);
            }
            if (_zoomButton != null)
            {
                var r = _zoomButton.transform as RectTransform;
                var rightButton = _hospitalsButton?.transform as RectTransform;
                if (r != null)
                {
                    float width = rightButton == null ? r.rect.width : rightButton.rect.width;
                    r.anchoredPosition = new Vector2(-right - width - gap, bottom);
                }
            }
        }

        private void LateUpdate()
        {
            if (!_initialised || _canvasRect == null || _nativeMenu == null) return;
            // SOLO los dos botones nuevos desaparecen con el menu de pausa.
            // Los tres nativos conservan el control de visibilidad del juego.
            var hud = _map == null ? null : _map.HUD;
            bool pauseOpen = hud != null && hud.IsOptionsMenuOpen;
            bool visible = !pauseOpen && _nativeMenu.gameObject.activeInHierarchy;
            if (_zoomButton != null && _zoomButton.gameObject.activeSelf != visible)
                _zoomButton.gameObject.SetActive(visible);
            if (_hospitalsButton != null && _hospitalsButton.gameObject.activeSelf != visible)
                _hospitalsButton.gameObject.SetActive(visible);
        }

        private void PositionNativeButtonsBeforeRender()
        {
            if (!_initialised || _canvasRect == null || _nativeMenu == null ||
                !_nativeMenu.gameObject.activeInHierarchy ||
                _nativeMenu.IsClosed() || _nativeMenu.IsClosing()) return;

            // El menu tiene animacion nativa: mover su RectTransform no funciona
            // porque el Animator lo devuelve al borde inferior. Se desplazan
            // exclusivamente los botones, manteniendo cada uno su padre original,
            // listeners, escala, layout y comportamientos de apertura/cierre.
            var corners = new Vector3[4];
            if (!_nativeMeasuresReady)
            {
                for (int i = 0; i < _nativeButtons.Length; i++)
                {
                    var rect = _nativeButtons[i]?.transform as RectTransform;
                    if (rect == null) return;
                    rect.GetWorldCorners(corners);
                    var a = _canvasRect.InverseTransformPoint(corners[0]);
                    var b = _canvasRect.InverseTransformPoint(corners[2]);
                    _nativeWidths[i] = Mathf.Abs(b.x - a.x);
                    _nativeHeights[i] = Mathf.Abs(b.y - a.y);
                    if (_nativeWidths[i] < 24f || _nativeHeights[i] < 12f) return;
                }
                _nativeMeasuresReady = true;
            }

            // Orden original visual: Menu, Carrera, Marcadores; el ultimo queda
            // a la izquierda del boton nativo de incidencias.
            float right = _canvasRect.rect.xMax - 96f;
            float top = _canvasRect.rect.yMax - 17f;
            const float gap = 8f;
            for (int i = _nativeButtons.Length - 1; i >= 0; i--)
            {
                var rect = _nativeButtons[i]?.transform as RectTransform;
                if (rect == null) continue;
                float width = _nativeWidths[i];
                float height = _nativeHeights[i];
                float centerX = right - width * 0.5f;
                float centerY = top - height * 0.5f;
                right -= width + gap;
                if (!rect.gameObject.activeInHierarchy) continue;

                rect.GetWorldCorners(corners);
                var a = _canvasRect.InverseTransformPoint(corners[0]);
                var b = _canvasRect.InverseTransformPoint(corners[2]);
                var displacement = new Vector3(centerX - (a.x + b.x) * 0.5f,
                    centerY - (a.y + b.y) * 0.5f, 0f);
                if (displacement.sqrMagnitude > 0.01f)
                    rect.position += _canvasRect.TransformVector(displacement);
            }
            // No se usa SetParent, SetActive ni se modifica onPrimaryDown de
            // ninguno de los botones nativos.
        }

        private void ZoomPressed()
        {
            _moreZoom = !_moreZoom;
            if (_map?.CameraLogic?.CameraComponent == null) return;
            // Evita recortar la escena al permitir alejar tres veces más la cámara.
            _map.CameraLogic.CameraComponent.farClipPlane = _moreZoom
                ? Mathf.Max(_originalFarClip, 1500f)
                : _originalFarClip;
        }

        private void HospitalsPressed()
        {
            _allHospitals = !_allHospitals;
            RefreshHospitals();
        }

        private void RefreshHospitals()
        {
            var scene = _map?.App?.MetagameMapScene;
            if (scene == null) return;
            foreach (var pin in scene.MapPins)
            {
                if (pin is MapPinHospital hospital)
                    hospital.Refresh(true);
            }
            foreach (var visual in scene.MapVisuals)
            {
                if (visual != null) visual.Refresh();
            }
            try { UpdateBoundsMethod?.Invoke(_map.MapUI, null); }
            catch (Exception e)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Minimapa: límites de cámara: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            if (_canvasCallbackRegistered)
            {
                Canvas.willRenderCanvases -= PositionNativeButtonsBeforeRender;
                _canvasCallbackRegistered = false;
            }
            if (_current != this) return;
            _allHospitals = false;
            _moreZoom = false;
            if (_map?.CameraLogic?.CameraComponent != null && _originalFarClip > 0f)
                _map.CameraLogic.CameraComponent.farClipPlane = _originalFarClip;
            _current = null;
        }

        internal static void RevealHospital(MapPinHospital pin)
        {
            if (!ShowingAllHospitals || pin == null || pin.LevelConfig == null) return;
            var mesh = MeshField?.GetValue(pin) as MeshRenderer;
            if (mesh == null) return;
            mesh.gameObject.SetActive(true);

            // La apariencia bloqueada se calcula con IsPlayable, que NO se modifica.
            var level = pin.LevelConfig;
            var metagame = MetagameField?.GetValue(pin) as Metagame;
            var record = RecordField?.GetValue(pin) as MetagameHospitalRecord;
            bool playable = metagame != null && DLCUtils.IsDLCInstalled(level.GetRequiredDlcPack()) &&
                (level.IsPlayable(metagame) || (record != null && record.IsPlayable()));
            if (!playable)
            {
                // Los hospitales revelados mantienen el icono gris nativo,
                // sin estrellas, premios ni marcas de hospital jugable.
                var stars = StarsField?.GetValue(pin) as GameObject[];
                if (stars != null)
                    foreach (var star in stars)
                        if (star != null && star.activeSelf) star.SetActive(false);
                // Desactivar también la insignia Remix azul en hospitales bloqueados.
                var remixBadge = AccessTools.Field(typeof(MapPinHospital), "_remixBadgeIcon")
                    ?.GetValue(pin) as Component;
                if (remixBadge != null) remixBadge.gameObject.SetActive(false);
                var hovered = HoverField?.GetValue(pin) is bool value && value;
                var material = (hovered ? LockedHoverField : LockedField)?.GetValue(pin) as Material;
                if (material != null) mesh.material = material;
            }
        }

        internal static void RevealVisuals(MapVisualsActivation visuals)
        {
            if (!ShowingAllHospitals || visuals == null) return;
            // Muestra el hospital en su estado NATIVO bloqueado o jugable.
            // No altera estrellas, progreso, dinero, DLC o prerequisitos.
            var metagame = VisibilityMetagameField?.GetValue(visuals) as Metagame;
            var record = VisibilityRecordField?.GetValue(visuals) as MetagameHospitalRecord;
            var shared = VisibilityLevelField?.GetValue(visuals);
            LevelConfig level = null;
            if (shared != null)
            {
                var instance = AccessTools.Property(shared.GetType(), "Instance");
                if (instance != null) level = instance.GetValue(shared, null) as LevelConfig;
            }
            if (level == null || metagame == null) return;
            bool playable = DLCUtils.IsDLCInstalled(level.GetRequiredDlcPack()) &&
                (level.IsPlayable(metagame) || (record != null && record.IsPlayable()));
            visuals.SetLevelPlayable(playable);
        }
    }

    [HarmonyPatch(typeof(MetagameMapCareerUI), "Setup")]
    internal static class ScenarioCareerMapControlsSetupPatch
    {
        private static void Postfix(MetagameMapCareerUI __instance, MetagameMap metagameMap)
        {
            if (__instance == null || __instance.ButtonsMenu == null) return;
            var controls = __instance.GetComponent<ScenarioCareerMapControls>() ??
                __instance.gameObject.AddComponent<ScenarioCareerMapControls>();
            controls.Initialise(metagameMap, __instance.ButtonsMenu);
        }
    }

    [HarmonyPatch(typeof(TopDownCameraLogic), "GetOffset")]
    internal static class ScenarioCareerMapControlsZoomPatch
    {
        private static void Postfix(TopDownCameraLogic __instance, ref Vector3 __result)
        {
            float zoom;
            if (!ScenarioCareerMapControls.ExtraZoomFor(__instance, out zoom)) return;
            // Zoom 0 conserva exactamente la distancia minima nativa.
            // Sólo aumenta la distancia del tramo de alejamiento, máximo ~1.60x.
            // No se modifica la velocidad ni el incremento de la rueda.
            // No altera el acercamiento: empieza a ampliar sólo desde el
            // 45% del recorrido hacia el extremo lejano. Máximo ~1,60x.
            var farFactor = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.45f, 1f, zoom));
            __result *= Mathf.Lerp(1f, 1.60f, farFactor);
        }
    }

    [HarmonyPatch(typeof(MapPinHospital), "Refresh")]
    internal static class ScenarioCareerMapControlsPinPatch
    {
        private static void Postfix(MapPinHospital __instance) =>
            ScenarioCareerMapControls.RevealHospital(__instance);
    }

    [HarmonyPatch(typeof(MapPinHospital), "OnCursorOver")]
    internal static class ScenarioCareerMapControlsHoverPatch
    {
        private static void Postfix(MapPinHospital __instance) =>
            ScenarioCareerMapControls.RevealHospital(__instance);
    }

    [HarmonyPatch(typeof(MapPinHospital), "IsPinUnlocked")]
    internal static class ScenarioCareerMapControlsAvailabilityPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ScenarioCareerMapControls.ShowingAllHospitals) __result = true;
        }
    }

    [HarmonyPatch(typeof(MapVisualsActivation), "Refresh")]
    internal static class ScenarioCareerMapControlsVisualPatch
    {
        private static void Postfix(MapVisualsActivation __instance) =>
            ScenarioCareerMapControls.RevealVisuals(__instance);
    }
}

namespace UnderPressure.Editor
{
    // Deja una franja libre para los tres botones de la cabecera sin alterar
    // el tamaño ni el estilo del panel de Marcadores.
    [HarmonyPatch(typeof(MetagameLeaderboardsMenu), "Setup")]
    internal static class ScenarioCareerLowerLeaderboardsPatch
    {
        private static void Postfix(MetagameLeaderboardsMenu __instance)
        {
            if (__instance == null || __instance.GetComponent<ScenarioLeaderboardsMoved>() != null)
                return;
            var rect = __instance.transform as RectTransform;
            if (rect == null) return;
            rect.anchoredPosition += new Vector2(0f, -78f);
            __instance.gameObject.AddComponent<ScenarioLeaderboardsMoved>();
        }
    }

    internal sealed class ScenarioLeaderboardsMoved : MonoBehaviour { }
}
