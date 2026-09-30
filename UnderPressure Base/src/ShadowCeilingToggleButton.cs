using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;

namespace UnderPressure
{
    [HarmonyPatch(typeof(MessagesMenu), "Setup")]
    internal static class ShadowCeilingToggleButtonPatch
    {
        private static void Postfix(MessagesMenu __instance)
        {
            if (__instance == null) return;
            var controller = __instance.GetComponent<ShadowCeilingToggleButton>();
            if (controller == null)
                controller = __instance.gameObject.AddComponent<ShadowCeilingToggleButton>();
            controller.Initialise(__instance);
        }
    }

    internal sealed class ShadowCeilingToggleButton : MonoBehaviour
    {
        private static readonly System.Reflection.FieldInfo InboxButtonField =
            AccessTools.Field(typeof(MessagesMenu), "_inboxButton");

        private DynamicButton _button;
        private ButtonAnimator _animator;
        private bool _lastRoofState;
        private bool _lastInteractable;

        internal void Initialise(MessagesMenu menu)
        {
            if (_button != null || menu == null) return;
            var inboxButton = InboxButtonField?.GetValue(menu) as DynamicButton;
            if (inboxButton == null || inboxButton.transform.parent == null) return;

            _button = Instantiate(inboxButton, inboxButton.transform.parent);
            _button.name = "Under Pressure - Shadow Ceiling Toggle";
            _button.onPrimaryDown.RemoveAllListeners();
            _button.onPrimaryDown.AddListener(ToggleRoof);

            var sourceRect = inboxButton.transform as RectTransform;
            var buttonRect = _button.transform as RectTransform;
            if (sourceRect != null && buttonRect != null)
            {
                var verticalSpacing = Mathf.Max(sourceRect.rect.height, 44f) + 8f;
                buttonRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.up * verticalSpacing;
            }

            var tooltip = _button.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null)
                tooltip.SetDataProvider(value => value.Text =
                    HospitalLightingPrototype.ShadowCeilingVisible
                        ? "Ocultar malla del falso techo"
                        : "Mostrar malla del falso techo");

            _animator = _button.GetComponentInChildren<ButtonAnimator>(true);
            RefreshVisual(true);
        }

        private void ToggleRoof()
        {
            HospitalLightingPrototype.ToggleShadowCeilingVisibility();
            RefreshVisual(true);
        }

        private void Update()
        {
            RefreshVisual(false);
        }

        private void RefreshVisual(bool force)
        {
            if (_button == null) return;
            var roofState = HospitalLightingPrototype.ShadowCeilingVisible;
            var interactable = HospitalLightingPrototype.Enabled;
            if (!force && roofState == _lastRoofState && interactable == _lastInteractable) return;
            _lastRoofState = roofState;
            _lastInteractable = interactable;
            _button.interactable = interactable;
            if (_animator != null)
                _animator.CurrentState = roofState && interactable
                    ? ButtonAnimator.State.Selected
                    : ButtonAnimator.State.Selectable;
        }
    }
}
