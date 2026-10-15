// Updated: 2026-10-15 - Prueba aislada: desactivar FadeMeshNearCameraComponent solo durante el editor.
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    [HarmonyPatch(typeof(FadeMeshNearCameraComponent), "UpdateAll")]
    internal static class EditorFadeNearCameraPatch
    {
        private static readonly FieldInfo ActiveComponents = AccessTools.Field(
            typeof(FadeMeshNearCameraComponent), "_activeComponents");
        private static readonly FieldInfo MeshData = AccessTools.Field(
            typeof(FadeMeshNearCameraComponent), "_meshData");
        private static readonly FieldInfo IsDithering = AccessTools.Field(
            typeof(FadeMeshNearCameraComponent), "_isDithering");
        private static readonly MethodInfo AlphaSetter = AccessTools.PropertySetter(
            typeof(FadeMeshNearCameraComponent), "Alpha");

        private static bool Prefix()
        {
            // Fuera del editor conserva completamente el funcionamiento original.
            if (!ScenarioEditorFlow.IsRunning)
                return true;

            // No basta con detener UpdateAll: hay que devolver los materiales opacos
            // a los objetos que ya estaban en modo Dithered.
            var components = ActiveComponents?.GetValue(null) as IEnumerable;
            if (components == null)
                return false;

            foreach (var item in components)
            {
                var component = item as FadeMeshNearCameraComponent;
                if (component == null) continue;

                try
                {
                    if (IsDithering != null && (bool)IsDithering.GetValue(component))
                    {
                        var meshes = MeshData?.GetValue(component) as IEnumerable;
                        if (meshes != null)
                        {
                            foreach (var mesh in meshes)
                            {
                                if (mesh == null) continue;
                                var type = mesh.GetType();
                                var gameObject = type.GetField("GameObject")?.GetValue(mesh) as GameObject;
                                var oldMaterials = type.GetField("OldMaterials")?.GetValue(mesh)
                                    as System.Collections.Generic.List<Material[]>;
                                if (gameObject != null && oldMaterials != null)
                                    MeshUtils.SetGameObjectMaterials(gameObject, ref oldMaterials);
                            }
                        }
                        IsDithering.SetValue(component, false);
                    }

                    AlphaSetter?.Invoke(component, new object[] { 1f });
                }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "No se pudo restaurar un material de FadeMeshNearCamera: " + ex.Message);
                }
            }
            return false;
        }
    }
}
