// Updated: 2026-10-14 - Silenciar exclusivamente efectos de compra/cambio de parcela, sin mutear el editor.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    // No usar AudioListener.volume: eso silenció todo el juego mientras se editaba.
    // El sonido no deseado se dispara al comprar/abrir/cerrar parcelas. Se
    // bloquea sólo su reproducción, sin alterar ni el volumen ni las preferencias.
    internal static class ScenarioEditorSound
    {
        private static int _plotActionDepth;
        internal static bool DuringPlotAction => _plotActionDepth > 0 &&
            ScenarioEditorFlow.IsRunning;

        internal static void BeginPlotAction()
        {
            if (ScenarioEditorFlow.IsRunning)
                ++_plotActionDepth;
        }

        internal static void EndPlotAction()
        {
            if (_plotActionDepth > 0) --_plotActionDepth;
        }

        internal static bool IsPlotPurchaseSound(object source, object[] parameters)
        {
            if (!ScenarioEditorFlow.IsRunning) return false;
            if (DuringPlotAction) return true;
            // Hay variantes en las que el efecto se lanza después de la compra,
            // y el nombre del clip permite bloquearlo sin tocar otros sonidos.
            var clip = parameters != null && parameters.Length > 0 &&
                parameters[0] is UnityEngine.Object ? parameters[0] : null;
            if (clip == null && source != null)
            {
                try
                {
                    var property = AccessTools.Property(source.GetType(), "clip");
                    clip = property?.GetValue(source, null);
                }
                catch { return false; }
            }
            var name = (clip as UnityEngine.Object)?.name;
            if (string.IsNullOrEmpty(name)) return false;
            var text = name.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            return ((text.Contains("plot") || text.Contains("land") || text.Contains("tile")) &&
                (text.Contains("buy") || text.Contains("purchase") ||
                 text.Contains("unlock") || text.Contains("activate")));
        }
    }

    // Delimitar solamente las acciones que generan el sonido de compra de parcela.
    // TargetMethods evita errores si un método cambia entre versiones del juego.
    [HarmonyPatch]
    internal static class ScenarioEditorPlotSoundScopePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[] { "BuyAndBuildImmediately", "Buy", "Open", "Close" };
            foreach (var name in names)
            {
                var method = AccessTools.Method(typeof(HospitalPlot), name);
                if (method != null) yield return method;
            }
        }

        private static void Prefix(out bool __state)
        {
            __state = ScenarioEditorFlow.IsRunning;
            if (__state) ScenarioEditorSound.BeginPlotAction();
        }

        private static void Postfix(bool __state)
        {
            if (__state) ScenarioEditorSound.EndPlotAction();
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && __exception != null) ScenarioEditorSound.EndPlotAction();
            return __exception;
        }
    }

    // El sonido puede emitirse mediante AudioSource.Play o PlayOneShot.
    // Solo se interceptan esas llamadas; todos los demás sonidos siguen activos.
    [HarmonyPatch]
    internal static class ScenarioEditorPlotSoundPlaybackPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("UnityEngine.AudioSource") ??
                Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
            if (type == null) yield break;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly))
            {
                if (method.Name != "Play" && method.Name != "PlayOneShot" &&
                    method.Name != "PlayDelayed" && method.Name != "PlayScheduled") continue;
                try
                {
                    if (method.GetMethodBody() == null) continue;
                }
                catch { continue; }
                yield return method;
            }
        }

        private static bool Prefix(object __instance, object[] __args) =>
            !ScenarioEditorSound.IsPlotPurchaseSound(__instance, __args);
    }
}
