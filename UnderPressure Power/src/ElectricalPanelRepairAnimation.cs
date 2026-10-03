using System.Collections.Generic;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.PowerGrid
{
    [HarmonyPatch(typeof(ObjectInteraction), "StartInteraction")]
    internal static class ElectricalPanelRepairAnimationPatch
    {
        private static readonly Dictionary<ObjectInteraction, ElectricalPanelRepairAnimationDriver> Drivers =
            new Dictionary<ObjectInteraction, ElectricalPanelRepairAnimationDriver>();

        private static void Postfix(ObjectInteraction __instance, Character __0, bool __result)
        {
            if (!__result || !IsPanelJanitorInteraction(__instance, __0) || Drivers.ContainsKey(__instance)) return;
            var repairGraph = EnergyRoomItems.GetPanelRepairAnimationGraph(__0);
            var host = __0.Visual?.CharacterGameObject;
            if (repairGraph == null || host == null) return;

            var driver = host.AddComponent<ElectricalPanelRepairAnimationDriver>();
            driver.Initialise(__0, __instance, repairGraph);
            Drivers.Add(__instance, driver);
            // La taquilla nativa se usa con una interacción auto-finalizable, que activa Exit
            // al comenzar. El trabajo de mantenimiento no lo hace, así que se solicita aquí
            // únicamente para que el one-shot complete apertura y cierre.
            __instance.RequestExit();
        }

        private static bool IsPanelJanitorInteraction(ObjectInteraction interaction, Character character)
        {
            if (!(character is Staff staff) || staff.Definition == null ||
                staff.Definition._type != StaffDefinition.Type.Janitor) return false;
            var item = interaction?.ParentRoomItem;
            return item != null && EnergyRoomItems.IsPanel(item);
        }

        internal static void BeforeInteractionEnds(ObjectInteraction interaction, Character character)
        {
            if (interaction == null || !Drivers.TryGetValue(interaction, out var driver)) return;
            Drivers.Remove(interaction);
            if (driver == null) return;
            driver.RestoreOpeningGraph(character);
            Object.Destroy(driver);
        }

        internal static void Forget(ObjectInteraction interaction, ElectricalPanelRepairAnimationDriver driver)
        {
            if (interaction != null && Drivers.TryGetValue(interaction, out var current) && current == driver)
                Drivers.Remove(interaction);
        }
    }

    internal sealed class ElectricalPanelRepairAnimationDriver : MonoBehaviour
    {
        private Character _character;
        private ObjectInteraction _interaction;
        private RuntimeAnimatorController _repairGraph;
        private bool _repairGraphPushed;

        internal void Initialise(Character character, ObjectInteraction interaction,
            RuntimeAnimatorController repairGraph)
        {
            _character = character;
            _interaction = interaction;
            _repairGraph = repairGraph;
        }

        private void LateUpdate()
        {
            if (_character == null || _interaction == null || _character.Interaction != _interaction)
            {
                RestoreOpeningGraph(_character);
                ElectricalPanelRepairAnimationPatch.Forget(_interaction, this);
                Destroy(this);
                return;
            }

            if (_repairGraphPushed) return;
            var animator = _character.Animator;
            var objectAnimator = _interaction.ParentRoomItem?.Visual?.Animator;
            if (animator == null || objectAnimator == null ||
                !AnimationUtils.IsInState(animator, "Exit", 0) ||
                !AnimationUtils.IsInState(objectAnimator, "Exit", 0)) return;

            _character.PushAnimationGraph(_repairGraph, 0.15f, null);
            animator = _character.Animator;
            if (animator != null && AnimationUtils.HasParameter(animator, "Exit"))
                animator.SetBool("Exit", false);
            _repairGraphPushed = true;
        }

        internal void RestoreOpeningGraph(Character character)
        {
            if (!_repairGraphPushed || character == null || _repairGraph == null) return;
            character.PopAnimationGraph(_repairGraph, 0f, false);
            _repairGraphPushed = false;
        }
    }

    [HarmonyPatch(typeof(ObjectInteraction), "EndInteractionInner")]
    internal static class ElectricalPanelRepairAnimationCleanupPatch
    {
        private static void Prefix(ObjectInteraction __instance, Character __0)
        {
            ElectricalPanelRepairAnimationPatch.BeforeInteractionEnds(__instance, __0);
        }
    }
}
