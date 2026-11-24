// Purpose: Reutiliza el botón nativo de bandeja para alternar los dos planos del hospital.
using HarmonyLib;
using TH20;
using TH20.UI;
using UnityEngine;

namespace UnderPressure.Storey
{
    [HarmonyPatch(typeof(MessagesMenu), "Setup")]
    internal static class StoreyToggleButtonPatch
    {
        private static void Postfix(MessagesMenu __instance)
        {
            if (__instance == null) return;
            var controller = __instance.GetComponent<StoreyToggleButton>();
            if (controller == null)
                controller = __instance.gameObject.AddComponent<StoreyToggleButton>();
            controller.Initialise(__instance);
        }
    }

    internal sealed class StoreyToggleButton : MonoBehaviour
    {
        private static readonly System.Reflection.FieldInfo InboxButtonField =
            AccessTools.Field(typeof(MessagesMenu), "_inboxButton");
        private static readonly System.Reflection.FieldInfo LevelField =
            AccessTools.Field(typeof(MessagesMenu), "_level");

        private MessagesMenu _menu;
        private Level _level;
        private DynamicButton _button;
        private ButtonAnimator _animator;
        private int _lastStorey = -1;

        internal void Initialise(MessagesMenu menu)
        {
            _menu = menu;
            _level = LevelField?.GetValue(menu) as Level;
            TryCreateButton();
        }

        private void Update()
        {
            if (_level == null && _menu != null)
                _level = LevelField?.GetValue(_menu) as Level;
            TryCreateButton();
            RefreshVisual(false);
        }

        private void TryCreateButton()
        {
            if (_button != null || _menu == null || _level == null ||
                !StoreyApi.HasUpperStorey(_level)) return;
            var inboxButton = InboxButtonField?.GetValue(_menu) as DynamicButton;
            if (inboxButton == null || inboxButton.transform.parent == null) return;

            _button = Instantiate(inboxButton, inboxButton.transform.parent);
            _button.name = "Under Pressure - Storey Toggle";
            _button.onPrimaryDown.RemoveAllListeners();
            _button.onPrimaryDown.AddListener(ToggleStorey);

            var sourceRect = inboxButton.transform as RectTransform;
            var buttonRect = _button.transform as RectTransform;
            if (sourceRect != null && buttonRect != null)
            {
                var verticalSpacing = Mathf.Max(sourceRect.rect.height, 44f) + 8f;
                buttonRect.anchoredPosition = sourceRect.anchoredPosition +
                                              Vector2.up * verticalSpacing;
            }

            var tooltip = _button.GetComponentInChildren<TooltipSpawner>(true);
            if (tooltip != null)
                tooltip.SetDataProvider(value => value.Text =
                    StoreyApi.GetActiveStorey(_level) == StoreyApi.UpperStorey
                        ? ModLocalization.Get("storey.view_lower")
                        : ModLocalization.Get("storey.view_upper"));

            _animator = _button.GetComponentInChildren<ButtonAnimator>(true);
            RefreshVisual(true);
        }

        private void ToggleStorey()
        {
            if (_level == null) return;
            var next = StoreyApi.GetActiveStorey(_level) == StoreyApi.UpperStorey
                ? StoreyApi.LowerStorey
                : StoreyApi.UpperStorey;
            StoreyApi.SetActiveStorey(_level, next);
            RefreshVisual(true);
        }

        private void RefreshVisual(bool force)
        {
            if (_button == null || _level == null) return;
            var storey = StoreyApi.GetActiveStorey(_level);
            if (!force && storey == _lastStorey) return;
            _lastStorey = storey;
            _button.interactable = StoreyApi.HasUpperStorey(_level);
            if (_animator != null)
                _animator.CurrentState = storey == StoreyApi.UpperStorey
                    ? ButtonAnimator.State.Selected
                    : ButtonAnimator.State.Selectable;
        }
    }
}
