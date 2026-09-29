using System;
using HarmonyLib;
using TH20;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.PowerGrid
{
    internal sealed class ElectricityCatalogIcon : MonoBehaviour
    {
        private const string SpriteName = "T_UI_InGame_EnergyManagement_iconEnergy";
        private const float Scale = 0.6f;
        private static Sprite _sprite;
        private static bool _missingLogged;
        private Image _image;

        internal void Configure(bool visible)
        {
            if (!visible)
            {
                if (_image != null) _image.gameObject.SetActive(false);
                return;
            }

            var sprite = FindSprite();
            if (sprite == null)
            {
                if (!_missingLogged)
                {
                    _missingLogged = true;
                    PowerGridPlugin.Log.LogWarning("No se encontro el icono electrico " + SpriteName + ".");
                }
                return;
            }

            if (_image == null) CreateImage(sprite);
            _image.overrideSprite = sprite;
            _image.gameObject.SetActive(true);
            _image.transform.SetAsLastSibling();
        }

        private void CreateImage(Sprite sprite)
        {
            var iconObject = new GameObject("UnderPressure Electricity Icon", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)iconObject.transform;
            var background = GetComponent<RibbonItemRow>()?.BackgroundImage;
            rect.SetParent(background != null ? background.rectTransform : transform, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-5f, -5f);
            rect.sizeDelta = sprite.rect.size;
            rect.localScale = Vector3.one * Scale;

            _image = iconObject.GetComponent<Image>();
            _image.preserveAspect = true;
            _image.raycastTarget = false;
        }

        private static Sprite FindSprite()
        {
            if (_sprite != null) return _sprite;
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || !string.Equals(sprite.name, SpriteName,
                        StringComparison.Ordinal)) continue;
                _sprite = sprite;
                break;
            }
            return _sprite;
        }
    }

    [HarmonyPatch(typeof(RibbonItemRow), nameof(RibbonItemRow.Setup))]
    internal static class ElectricityCatalogIconPatch
    {
        private static void Postfix(RibbonItemRow __instance, IRoomItemDefinition __0)
        {
            if (__instance == null) return;
            var icon = __instance.GetComponent<ElectricityCatalogIcon>() ??
                       __instance.gameObject.AddComponent<ElectricityCatalogIcon>();
            icon.Configure(ElectricityGameplay.ShowsElectricityIcon(__0));
        }
    }
}
