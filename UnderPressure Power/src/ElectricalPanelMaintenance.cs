// Purpose: Applies electrical-panel wear, migrates saved maintenance state, and protects animator data.
using System;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.PowerGrid
{
    /// <summary>
    /// Excepción dinámica del cuadro: su desgaste continuo depende de las celdas
    /// de baja tensión que la topología eléctrica haya asignado a esa instancia.
    /// </summary>
    [HarmonyPatch(typeof(RoomItem), "GetAttributeModifierOverTime")]
    internal static class ElectricalPanelMaintenancePatch
    {
        private static void Postfix(RoomItem __instance, string __0, ref float __result)
        {
            if (!EnergyRoomItems.IsPanel(__instance) ||
                !string.Equals(__0, ObjectAttributes.Type.Maintenance.ToString(),
                    StringComparison.OrdinalIgnoreCase)) return;

            var connectedTiles = PowerGridPrototype.ConnectedLowVoltageTiles(__instance);
            __result = connectedTiles * ElectricalPanelSpecialParameters.WearPerConnectedTile;
        }
    }

    /// <summary>
    /// La red debe recalcularse cuando el cuadro cruza el umbral nativo entre
    /// funcional y averiado, tanto al desgastarse como al ser reparado.
    /// </summary>
    [HarmonyPatch(typeof(RoomItem), "MaintenanceLevelChanged")]
    internal static class ElectricalPanelMaintenanceNetworkPatch
    {
        private static void Postfix(RoomItem __instance)
        {
            if (EnergyRoomItems.IsPanel(__instance))
                PowerGridPrototype.NotifyPanelMaintenanceChanged(__instance);
        }
    }

    /// <summary>
    /// Los cuadros guardados antes de incorporar Maintenance conservan _attributes = null.
    /// RoomItem.RestoreFromSave no reconstruye ese campo desde la definición actual, por lo
    /// que se aplica la misma inicialización privada que usa el constructor de un objeto nuevo.
    /// </summary>
    [HarmonyPatch(typeof(RoomItem), "RestoreFromSave")]
    internal static class ElectricalPanelMaintenanceSaveMigrationPatch
    {
        private static readonly MethodInfo CreateAttributesMethod =
            AccessTools.Method(typeof(RoomItem), "CreateAttributes");
        private static readonly MethodInfo SetupMaintenanceCallbacksMethod =
            AccessTools.Method(typeof(RoomItem), "SetupMaintenanceCallbacks");

        private static void Postfix(RoomItem __instance)
        {
            if (!EnergyRoomItems.IsPanel(__instance) || __instance.MaintenanceLevel != null) return;
            if (CreateAttributesMethod == null || SetupMaintenanceCallbacksMethod == null)
            {
                PowerGridPlugin.Log.LogError(
                    "No se pudo migrar el mantenimiento de un cuadro electrico guardado.");
                return;
            }

            CreateAttributesMethod.Invoke(__instance, null);
            SetupMaintenanceCallbacksMethod.Invoke(__instance, new object[] { false });
            PowerGridPlugin.Log.LogInfo(
                "Atributo de mantenimiento restaurado en un cuadro electrico antiguo.");
        }
    }

    /// <summary>
    /// El Animator autónomo forma parte del prefab y se reconstruye con él. Su controlador y
    /// su pose no son estado de juego, por lo que no deben copiarse a los campos temporales
    /// que RoomItem utiliza para serializar animaciones nativas en curso.
    /// </summary>
    [HarmonyPatch(typeof(RoomItem), "FullSerializerSave.fsISerializationCallbacks.OnBeforeSerializeInstance")]
    internal static class ElectricalPanelAnimatorSavePatch
    {
        private static readonly FieldInfo AnimatorStateForSaveField =
            AccessTools.Field(typeof(RoomItem), "_animatorStateForSave");
        private static readonly FieldInfo AnimationGraphForSaveField =
            AccessTools.Field(typeof(RoomItem), "_animationGraphForSave");

        private static void Postfix(RoomItem __instance)
        {
            if (!EnergyRoomItems.IsPanel(__instance)) return;
            AnimatorStateForSaveField?.SetValue(__instance, null);
            AnimationGraphForSaveField?.SetValue(__instance, null);
        }
    }
    // Purpose: Synchronizes electrical-panel repair and idle animations with janitor maintenance work.
    /// <summary>
    /// Controla únicamente la animación propia del cuadro eléctrico.
    ///
    /// La interacción humana sigue usando los AnimGraphs de la Nurse Locker
    /// configurados en EnergyRoomItems.CreatePanelMaintenanceInteraction().
    ///
    /// El controller del prefab debe contener:
    ///   Base Layer.Idle
    ///   Base Layer.Repair
    ///
    /// No hacen falta transiciones entre ambos estados: este código los reproduce
    /// directamente cuando empieza y termina la interacción Maintenance.
    /// </summary>
    internal static class ElectricalPanelRepairAnimation
    {
        private const string PanelIdleStateName = "Base Layer.Idle";
        private const string PanelRepairStateName = "Base Layer.Repair";

        private static readonly int PanelIdleState = Animator.StringToHash(PanelIdleStateName);
        private static readonly int PanelRepairState = Animator.StringToHash(PanelRepairStateName);

        private static bool _missingRepairStateLogged;

        internal static bool IsPanelJanitorInteraction(ObjectInteraction interaction, Character character)
        {
            if (!(character is Staff staff) ||
                staff.Definition == null ||
                staff.Definition._type != StaffDefinition.Type.Janitor)
                return false;

            var panel = interaction?.ParentRoomItem;
            if (panel == null || !EnergyRoomItems.IsPanel(panel))
                return false;

            var definition = interaction.Definition;
            return definition != null &&
                   !definition.Deprecated &&
                   definition.Type == InteractionAttributeModifier.Type.Maintain &&
                   string.Equals(definition.Name, "Maintenance", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Begin(ObjectInteraction interaction, Character character)
        {
            if (!IsPanelJanitorInteraction(interaction, character))
                return;

            var panel = interaction.ParentRoomItem;
            var animator = FindPanelAnimator(panel);
            if (animator == null)
                return;

            if (!animator.HasState(0, PanelRepairState))
            {
                if (!_missingRepairStateLogged)
                {
                    _missingRepairStateLogged = true;
                    PowerGridPlugin.Log.LogError(
                        "El prefab del cuadro no contiene el estado de animacion Base Layer.Repair.");
                }

                return;
            }

            animator.enabled = true;
            animator.speed = 1f;
            animator.Play(PanelRepairState, 0, 0f);
            animator.Update(0f);

            var host = panel.Visual?.GameObject;
            if (host == null)
                return;

            var driver = host.GetComponent<ElectricalPanelRepairAnimationDriver>();
            if (driver == null)
                driver = host.AddComponent<ElectricalPanelRepairAnimationDriver>();

            driver.Initialise(interaction, character, animator);
        }

        internal static void End(ObjectInteraction interaction, Character character)
        {
            if (interaction == null)
                return;

            var panel = interaction.ParentRoomItem;
            if (panel == null || !EnergyRoomItems.IsPanel(panel))
                return;

            var host = panel.Visual?.GameObject;
            var driver = host == null ? null : host.GetComponent<ElectricalPanelRepairAnimationDriver>();

            if (driver != null && driver.Matches(interaction))
            {
                driver.Stop();
                return;
            }

            // Respaldo por si el driver hubiese desaparecido al reconstruirse el visual.
            PlayIdle(FindPanelAnimator(panel));
        }

        internal static void PlayIdle(Animator animator)
        {
            if (animator == null)
                return;

            animator.enabled = true;
            animator.speed = 1f;

            if (animator.HasState(0, PanelIdleState))
            {
                animator.Play(PanelIdleState, 0, 0f);
                animator.Update(0f);
            }
        }

        private static Animator FindPanelAnimator(RoomItem panel)
        {
            var root = panel?.Visual?.GameObject;
            if (root == null)
                return null;

            // El Animator está dentro del prefab visual. Panel_offset puede existir
            // por encima de A_Prop_power_panel_V1 sin afectar a esta búsqueda.
            var animators = root.GetComponentsInChildren<Animator>(true);

            // Preferir expresamente el Animator cuyo controller contiene Repair.
            foreach (var animator in animators)
            {
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;

                if (animator.HasState(0, PanelRepairState))
                    return animator;
            }

            // Si todavía no ha quedado evaluado el controller, devolver el primer
            // Animator válido para permitir un diagnóstico limpio.
            foreach (var animator in animators)
            {
                if (animator != null && animator.runtimeAnimatorController != null)
                    return animator;
            }

            return null;
        }
    }

    /// <summary>
    /// Arranca la animación propia del cuadro únicamente cuando StartInteraction
    /// ha aceptado realmente la interacción de mantenimiento.
    /// </summary>
    [HarmonyPatch(typeof(ObjectInteraction), "StartInteraction")]
    internal static class ElectricalPanelRepairAnimationPatch
    {
        private static void Postfix(ObjectInteraction __instance, Character __0, bool __result)
        {
            if (!__result)
                return;

            ElectricalPanelRepairAnimation.Begin(__instance, __0);
        }
    }

    /// <summary>
    /// Devuelve el cuadro a Idle al terminar o cancelar la reparación.
    /// </summary>
    [HarmonyPatch(typeof(ObjectInteraction), "EndInteractionInner")]
    internal static class ElectricalPanelRepairAnimationCleanupPatch
    {
        private static void Prefix(ObjectInteraction __instance, Character __0)
        {
            ElectricalPanelRepairAnimation.End(__instance, __0);
        }
    }

    /// <summary>
    /// Red de seguridad para interrupciones anómalas de la interacción.
    /// Si el trabajador deja de tener esta interacción sin pasar por el cierre
    /// normal, restaura igualmente el panel a Idle.
    /// </summary>
    internal sealed class ElectricalPanelRepairAnimationDriver : MonoBehaviour
    {
        private ObjectInteraction _interaction;
        private Character _character;
        private Animator _panelAnimator;
        private bool _stopped;

        internal void Initialise(ObjectInteraction interaction, Character character, Animator animator)
        {
            _interaction = interaction;
            _character = character;
            _panelAnimator = animator;
            _stopped = false;
        }

        internal bool Matches(ObjectInteraction interaction)
        {
            return ReferenceEquals(_interaction, interaction);
        }

        internal void Stop()
        {
            if (_stopped)
                return;

            _stopped = true;
            ElectricalPanelRepairAnimation.PlayIdle(_panelAnimator);

            _interaction = null;
            _character = null;
            _panelAnimator = null;

            Destroy(this);
        }

        private void Update()
        {
            if (_stopped)
                return;

            if (_interaction == null ||
                _character == null ||
                !ReferenceEquals(_character.Interaction, _interaction))
            {
                Stop();
            }
        }

        private void OnDestroy()
        {
            if (_stopped)
                return;

            _stopped = true;
            ElectricalPanelRepairAnimation.PlayIdle(_panelAnimator);
        }
    }
}
