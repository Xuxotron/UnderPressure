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
            if (!IsPanelJanitorInteraction(__instance, __0)) return;
            if (!__result || Drivers.ContainsKey(__instance)) return;
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

        internal static bool IsOpening(ObjectInteraction interaction)
        {
            return interaction != null && Drivers.TryGetValue(interaction, out var driver) &&
                   driver != null && !driver.OpeningComplete;
        }
    }

    [HarmonyPatch(typeof(ObjectInteraction), "HasFinished")]
    internal static class ElectricalPanelOpeningCompletionPatch
    {
        private static void Postfix(ObjectInteraction __instance, ref bool __result)
        {
            if (__result && ElectricalPanelRepairAnimationPatch.IsOpening(__instance)) __result = false;
        }
    }

    internal sealed class ElectricalPanelRepairAnimationDriver : MonoBehaviour
    {
        private static readonly int PanelIdleState = Animator.StringToHash("Base Layer.Idle");
        private static readonly int PanelRepairState = Animator.StringToHash("Base Layer.Repair");
        private Character _character;
        private ObjectInteraction _interaction;
        private RuntimeAnimatorController _repairGraph;
        private Animator _panelAnimator;
        private bool _panelAnimationComplete;
        private bool _repairGraphPushed;

        internal bool OpeningComplete => _repairGraphPushed;

        internal void Initialise(Character character, ObjectInteraction interaction,
            RuntimeAnimatorController repairGraph)
        {
            _character = character;
            _interaction = interaction;
            _repairGraph = repairGraph;
            _panelAnimator = interaction.ParentRoomItem?.Visual?.Animator;
            if (_panelAnimator == null || !_panelAnimator.HasState(0, PanelRepairState))
            {
                _panelAnimationComplete = true;
                PowerGridPlugin.Log.LogError(
                    "El prefab del cuadro no contiene el estado de animación Base Layer.Repair.");
                return;
            }

            _panelAnimator.Play(PanelRepairState, 0, 0f);
            _panelAnimator.Update(0f);
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
            UpdatePanelAnimationCompletion();
            if (animator == null || !_panelAnimationComplete ||
                !AnimationUtils.IsInState(animator, "Exit", 0)) return;

            _character.PushAnimationGraph(_repairGraph, 0.15f, null);
            animator = _character.Animator;
            if (animator != null && AnimationUtils.HasParameter(animator, "Exit"))
                animator.SetBool("Exit", false);
            _repairGraphPushed = true;
        }

        private void UpdatePanelAnimationCompletion()
        {
            if (_panelAnimationComplete || _panelAnimator == null) return;
            var state = _panelAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != PanelRepairState || state.normalizedTime < 1f) return;
            _panelAnimationComplete = true;
            _panelAnimator.Play(PanelIdleState, 0, 0f);
            _panelAnimator.Update(0f);
        }

        internal void RestoreOpeningGraph(Character character)
        {
            if (_panelAnimator != null && _panelAnimator.HasState(0, PanelIdleState))
            {
                _panelAnimator.Play(PanelIdleState, 0, 0f);
                _panelAnimator.Update(0f);
            }
            if (_repairGraphPushed && character != null && _repairGraph != null)
            {
                character.PopAnimationGraph(_repairGraph, 0f, false);
                _repairGraphPushed = false;
            }
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
