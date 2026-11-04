// Updated: 2026-11-03 - Bloqueo global del mundo bajo cualquier UI del Editor.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    /// <summary>
    /// Regla global del Editor:
    /// mientras el puntero este sobre cualquier superficie visible de la UI
    /// del Editor, el mundo no recibe seleccion, clic izquierdo, clic derecho,
    /// pintura, movimiento ni resaltado.
    ///
    /// Este fichero no depende de paneles concretos. Los paneles nuevos que
    /// cuelguen de "UP Scenario Editor UI" quedan cubiertos automaticamente.
    /// </summary>
    internal static class ScenarioEditorGlobalUiBlock
    {
        private const string EditorUiRootName = "UP Scenario Editor UI";

        private static readonly List<RaycastResult> RaycastResults =
            new List<RaycastResult>();

        private static readonly List<Graphic> EditorGraphics =
            new List<Graphic>();

        private static GameObject _editorUiRoot;
        private static int _cachedFrame = -1;
        private static Vector2 _cachedPointer;
        private static bool _cachedResult;

        internal static bool PointerBlocksWorld()
        {
            if (!ScenarioEditorFlow.IsRunning ||
                !ScenarioEditorSession.Active)
                return false;

            var pointer = (Vector2)Input.mousePosition;

            if (_cachedFrame == Time.frameCount &&
                _cachedPointer == pointer)
                return _cachedResult;

            _cachedFrame = Time.frameCount;
            _cachedPointer = pointer;

            var root = ResolveEditorUiRoot();

            _cachedResult =
                PointerOverInteractiveUi(pointer, root) ||
                PointerOverVisibleEditorSurface(pointer, root);

            return _cachedResult;
        }

        private static GameObject ResolveEditorUiRoot()
        {
            if (_editorUiRoot != null &&
                _editorUiRoot.activeInHierarchy)
                return _editorUiRoot;

            _editorUiRoot =
                GameObject.Find(EditorUiRootName);

            return _editorUiRoot;
        }

        private static bool PointerOverInteractiveUi(
            Vector2 pointer,
            GameObject editorRoot)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            var eventData =
                new PointerEventData(eventSystem)
                {
                    position = pointer
                };

            RaycastResults.Clear();
            eventSystem.RaycastAll(
                eventData,
                RaycastResults);

            for (var index = 0;
                 index < RaycastResults.Count;
                 ++index)
            {
                var hit =
                    RaycastResults[index].gameObject;

                if (hit == null)
                    continue;

                // Toda la UI propia del Editor bloquea el mundo.
                if (editorRoot != null &&
                    (hit == editorRoot ||
                     hit.transform.IsChildOf(
                         editorRoot.transform)))
                {
                    RaycastResults.Clear();
                    return true;
                }

                // Tambien cubrir controles nativos reales del HUD.
                // No se consideran simples imagenes transparentes a pantalla
                // completa porque esas existen en el HUD original.
                if (hit.GetComponentInParent<Selectable>() != null ||
                    hit.GetComponentInParent<ScrollRect>() != null)
                {
                    RaycastResults.Clear();
                    return true;
                }
            }

            RaycastResults.Clear();
            return false;
        }

        private static bool PointerOverVisibleEditorSurface(
            Vector2 pointer,
            GameObject editorRoot)
        {
            if (editorRoot == null ||
                !editorRoot.activeInHierarchy)
                return false;

            EditorGraphics.Clear();

            editorRoot.GetComponentsInChildren(
                false,
                EditorGraphics);

            for (var index = 0;
                 index < EditorGraphics.Count;
                 ++index)
            {
                var graphic = EditorGraphics[index];

                if (graphic == null ||
                    !graphic.enabled ||
                    !graphic.gameObject.activeInHierarchy)
                    continue;

                var rect = graphic.rectTransform;
                if (rect == null)
                    continue;

                // Ignorar superficies completamente invisibles.
                var alpha = graphic.color.a;
                if (graphic.canvasRenderer != null)
                    alpha *= graphic.canvasRenderer.GetAlpha();

                if (alpha <= 0.001f)
                    continue;

                var canvas = graphic.canvas;
                Camera eventCamera = null;

                if (canvas != null &&
                    canvas.renderMode !=
                    RenderMode.ScreenSpaceOverlay)
                {
                    eventCamera = canvas.worldCamera;
                }

                if (!RectTransformUtility
                        .RectangleContainsScreenPoint(
                            rect,
                            pointer,
                            eventCamera))
                    continue;

                EditorGraphics.Clear();
                return true;
            }

            EditorGraphics.Clear();
            return false;
        }
    }

    /// <summary>
    /// Hace que cualquier codigo existente que consulte
    /// EventSystem.IsPointerOverGameObject() reciba el resultado global.
    /// </summary>
    [HarmonyPatch]
    internal static class ScenarioEditorGlobalEventSystemPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(EventSystem),
                "IsPointerOverGameObject",
                Type.EmptyTypes);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            ref bool __result)
        {
            if (!__result &&
                ScenarioEditorGlobalUiBlock
                    .PointerBlocksWorld())
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Hace lo mismo para el InputManager nativo de TPH.
    /// </summary>
    [HarmonyPatch(
        typeof(InputManager),
        "get_IsMouseOverGui")]
    internal static class ScenarioEditorGlobalNativeGuiPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            ref bool __result)
        {
            if (!__result &&
                ScenarioEditorGlobalUiBlock
                    .PointerBlocksWorld())
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Bloquea los cursores nativos que pueden tocar el mapa
    /// aunque una ruta concreta no consulte IsMouseOverGui.
    /// </summary>
    [HarmonyPatch]
    internal static class ScenarioEditorGlobalCursorBlockPatch
    {
        private static IEnumerable<MethodBase>
            TargetMethods()
        {
            var methods =
                new HashSet<MethodBase>();

            Add(
                methods,
                typeof(CursorEditHospitalBuild),
                "CursorUpdate");

            Add(
                methods,
                typeof(CursorEditHospitalMovePlot),
                "CursorUpdate");

            Add(
                methods,
                typeof(CursorEditHospitalItem),
                "CursorUpdate");

            Add(
                methods,
                typeof(CursorRoomItem),
                "CursorUpdate");

            Add(
                methods,
                typeof(CursorRoomMove),
                "CursorUpdate");

            return methods;
        }

        private static void Add(
            HashSet<MethodBase> methods,
            Type type,
            string methodName)
        {
            if (methods == null ||
                type == null)
                return;

            var method =
                AccessTools.Method(
                    type,
                    methodName);

            if (method != null)
                methods.Add(method);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !ScenarioEditorGlobalUiBlock
                .PointerBlocksWorld();
        }
    }

    /// <summary>
    /// Bloquea las rutas propias del Editor que realizan picking
    /// directamente y no dependen de los cursores nativos:
    /// objetos, capa escenario, paisaje exterior y terreno.
    /// </summary>
    [HarmonyPatch]
    internal static class ScenarioEditorGlobalCustomWorldBlockPatch
    {
        private static IEnumerable<MethodBase>
            TargetMethods()
        {
            var methods =
                new HashSet<MethodBase>();

            Add(
                methods,
                "UnderPressure.Editor." +
                "ScenarioEditorObjectInteraction",
                "Update");

            Add(
                methods,
                "UnderPressure.Editor." +
                "ScenarioEditorScene",
                "UpdateEditor");

            Add(
                methods,
                "UnderPressure.Editor." +
                "ScenarioLandscape",
                "UpdateSelection");

            Add(
                methods,
                "UnderPressure.Editor." +
                "ScenarioEditorTerrainPainter",
                "Update");

            return methods;
        }

        private static void Add(
            HashSet<MethodBase> methods,
            string typeName,
            string methodName)
        {
            if (methods == null ||
                string.IsNullOrEmpty(typeName) ||
                string.IsNullOrEmpty(methodName))
                return;

            var type =
                AccessTools.TypeByName(typeName);

            if (type == null)
                return;

            var method =
                AccessTools.Method(
                    type,
                    methodName);

            if (method != null)
                methods.Add(method);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !ScenarioEditorGlobalUiBlock
                .PointerBlocksWorld();
        }
    }

    /// <summary>
    /// Aunque algun sistema conserve un highlight anterior,
    /// no se renderiza mientras el puntero este sobre UI.
    /// </summary>
    [HarmonyPatch(
        typeof(HighlightRenderer),
        "Render")]
    internal static class ScenarioEditorGlobalHighlightBlockPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !ScenarioEditorGlobalUiBlock
                .PointerBlocksWorld();
        }
    }
}
