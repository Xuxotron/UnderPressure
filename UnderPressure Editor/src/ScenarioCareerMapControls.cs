// Updated: 2026-10-13 — mueve SOLO el contenedor nativo, zoom lejano 1.60x y hospitales visibles.
// Recoloca la botonera nativa sin cambiar el tamaño de sus tres botones.
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
        private bool _nativeGroupPositioned;
        private Vector2 _lastCanvasSize;
        private RectTransform _nativeMenuRect;
        private Vector3 _originalMenuPosition;

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
            // Los botones originales nunca cambian de padre, posicion relativa,
            // tamano, estado ni listeners. Mover exclusivamente su menu nativo.
            _nativeMenuRect = menu.transform as RectTransform;
            if (_nativeMenuRect != null)
                _originalMenuPosition = _nativeMenuRect.anchoredPosition3D;
            _originalFarClip = map.CameraLogic?.CameraComponent != null
                ? map.CameraLogic.CameraComponent.farClipPlane : 0f;
            _current = this;
            _initialised = true;
            try
            {
                // El prefab existente sirve como origen visual: no se inventan sprites ni shaders.
                _zoomButton = CreateNativeButton(options, "UP_MapMoreZoom", "More Zoom", ZoomPressed);
                _hospitalsButton = CreateNativeButton(options, "UP_MapAllHospitals", "All Hospitals", HospitalsPressed);
                ArrangeExtraButtons();
                UnderPressureEditorPlugin.Log?.LogInfo("Minimapa: botones nativos recolocados y opciones visibles.");
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
            // Justo encima del panel gris inferior derecho. No ocupa su contenido.
            const float right = 14f;
            const float bottom = 145f;
            const float gap = 8f;
            if (_hospitalsButton != null)
            {
                var r = _hospitalsButton.transform as RectTransform;
                r.anchoredPosition = new Vector2(-right, bottom);
            }
            if (_zoomButton != null)
            {
                var r = _zoomButton.transform as RectTransform;
                var rightButton = _hospitalsButton?.transform as RectTransform;
                var width = rightButton == null ? r.rect.width : rightButton.rect.width;
                r.anchoredPosition = new Vector2(-right - width - gap, bottom);
            }
        }

        private void LateUpdate()
        {
            if (!_initialised || _canvasRect == null || _nativeMenu == null) return;
            // Sólo los dos botones añadidos siguen el menú de pausa.
            // Carrera y Marcadores conservan íntegramente la lógica nativa.
            var hud = _map == null ? null : _map.HUD;
            bool pauseOpen = hud != null && hud.IsOptionsMenuOpen;
            bool visible = !pauseOpen && _nativeMenu.gameObject.activeInHierarchy;
            if (_zoomButton != null && _zoomButton.gameObject.activeSelf != visible)
                _zoomButton.gameObject.SetActive(visible);
            if (_hospitalsButton != null && _hospitalsButton.gameObject.activeSelf != visible)
                _hospitalsButton.gameObject.SetActive(visible);
            // No usar SetActive sobre los tres botones originales: de ello se
            // encarga MetagameButtonsMenu (también cuando abre Carrera/Marcadores).
            if (!_nativeMenu.gameObject.activeInHierarchy) return;
            // Los cambios de hover nunca deben mover el grupo: solo se coloca una vez
            // (o cuando realmente cambia el tamaño de la ventana).
            if ((_lastCanvasSize - _canvasRect.rect.size).sqrMagnitude > 4f)
                _nativeGroupPositioned = false;
            if (!_nativeGroupPositioned) MoveNativeButtonGroup();
        }

        private static bool IsVisible(AnimatedMenuBase menu)
        {
            return menu != null && !menu.IsClosed() && !menu.IsClosing();
        }

        private void MoveNativeButtonGroup()
        {
            if (_nativeButtons == null || _nativeMenuRect == null ||
                _nativeMenuRect.parent == null || _canvasRect == null) return;

            // Tomar las esquinas VISIBLES de los tres botones, no el rect
            // completo del menu (que puede incluir zonas transparentes).
            float rightMost = float.NegativeInfinity;
            float topMost = float.NegativeInfinity;
            var corners = new Vector3[4];
            foreach (var button in _nativeButtons)
            {
                if (button == null) continue;
                var rect = button.transform as RectTransform;
                if (rect == null) continue;
                rect.GetWorldCorners(corners);
                for (int i = 0; i < corners.Length; ++i)
                {
                    var p = _canvasRect.InverseTransformPoint(corners[i]);
                    rightMost = Mathf.Max(rightMost, p.x);
                    topMost = Mathf.Max(topMost, p.y);
                }
            }
            if (float.IsNegativeInfinity(rightMost) ||
                float.IsNegativeInfinity(topMost)) return;

            // SOLO se traslada MetagameButtonsMenu completo. De esta manera
            // el juego controla sus 3 botones, el hover, la apertura y cierre.
            var targetRight = _canvasRect.rect.xMax - 105f;
            var targetTop = _canvasRect.rect.yMax - 17f;
            var deltaCanvas = new Vector3(targetRight - rightMost,
                targetTop - topMost, 0f);
            var deltaWorld = _canvasRect.TransformVector(deltaCanvas);
            var deltaParent = _nativeMenuRect.parent.InverseTransformVector(deltaWorld);
            _nativeMenuRect.anchoredPosition3D += deltaParent;
            _nativeGroupPositioned = true;
            _lastCanvasSize = _canvasRect.rect.size;
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
            if (_current != this) return;
            _allHospitals = false;
            _moreZoom = false;
            if (_map?.CameraLogic?.CameraComponent != null && _originalFarClip > 0f)
                _map.CameraLogic.CameraComponent.farClipPlane = _originalFarClip;
            _current = null;
            // Restaurar solo la posicion del contenedor original.
            if (_nativeMenuRect != null)
                _nativeMenuRect.anchoredPosition3D = _originalMenuPosition;
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
