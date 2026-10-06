// Purpose: Replaces proxy shaders imported with the mod by the real shaders already loaded by Two Point Hospital.
// Updated: 2026-10-06
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure.PowerGrid
{
    public static class ShaderConversion
    {
        // Add future proxy -> native shader mappings here.
        // The proxy and native name may be identical; they are still different Shader instances at runtime.
        private static readonly Dictionary<string, string> ShaderMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "TH20 Standard", "TH20 Standard" }
            };

        private static readonly Dictionary<Shader, Shader> ResolvedShaders =
            new Dictionary<Shader, Shader>();

        /// <summary>
        /// Uses the supplied mod materials as anchors. For every registered proxy shader found,
        /// all currently loaded materials that use that exact proxy Shader instance are switched
        /// to the real native shader while preserving the material properties and keywords.
        /// </summary>
        public static int ConvertKnownShaders(params Material[] anchorMaterials)
        {
            if (anchorMaterials == null || anchorMaterials.Length == 0)
                return 0;

            var processedProxyShaders = new HashSet<Shader>();
            var totalConverted = 0;

            foreach (var anchor in anchorMaterials)
            {
                if (anchor == null || anchor.shader == null)
                    continue;

                var proxyShader = anchor.shader;
                if (!processedProxyShaders.Add(proxyShader))
                    continue;

                string nativeShaderName;
                if (!ShaderMap.TryGetValue(proxyShader.name, out nativeShaderName))
                    continue;

                var nativeShader = ResolveNativeShader(proxyShader, nativeShaderName);
                if (nativeShader == null)
                {
                    Debug.LogWarning(
                        "ShaderConversion: no se encontro el shader nativo '" +
                        nativeShaderName + "' para sustituir el proxy '" +
                        proxyShader.name + "'.");
                    continue;
                }

                var converted = ReplaceProxyInstance(proxyShader, nativeShader);
                totalConverted += converted;

                Debug.Log(
                    "ShaderConversion: '" + proxyShader.name + "' -> '" +
                    nativeShader.name + "': " + converted + " materiales convertidos.");
            }

            return totalConverted;
        }

        private static Shader ResolveNativeShader(
            Shader proxyShader,
            string nativeShaderName)
        {
            Shader cached;
            if (ResolvedShaders.TryGetValue(proxyShader, out cached) && cached != null)
                return cached;

            // First try Unity's global shader lookup. It is valid only if it returns
            // a different Shader object from the proxy bundled with the mod.
            var found = Shader.Find(nativeShaderName);
            if (found != null && found != proxyShader)
            {
                ResolvedShaders[proxyShader] = found;
                return found;
            }

            // If Shader.Find resolves to the proxy because both shaders have the same name,
            // inspect materials already loaded by the game and choose the other Shader instance
            // with that name that is referenced by the most materials.
            var referenceCounts = new Dictionary<Shader, int>();
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material == null || material.shader == null)
                    continue;

                var candidate = material.shader;
                if (candidate == proxyShader ||
                    !string.Equals(candidate.name, nativeShaderName, StringComparison.Ordinal))
                    continue;

                int count;
                referenceCounts.TryGetValue(candidate, out count);
                referenceCounts[candidate] = count + 1;
            }

            Shader best = null;
            var bestCount = -1;
            foreach (var pair in referenceCounts)
            {
                if (pair.Value <= bestCount)
                    continue;

                best = pair.Key;
                bestCount = pair.Value;
            }

            // Last fallback: the native shader may already be loaded even when no material
            // currently references it. Only accept a Shader instance different from the proxy.
            if (best == null)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Shader>())
                {
                    if (candidate == null || candidate == proxyShader)
                        continue;

                    if (!string.Equals(candidate.name, nativeShaderName, StringComparison.Ordinal))
                        continue;

                    best = candidate;
                    break;
                }
            }

            if (best != null)
                ResolvedShaders[proxyShader] = best;

            return best;
        }

        private static int ReplaceProxyInstance(
            Shader proxyShader,
            Shader nativeShader)
        {
            if (proxyShader == null || nativeShader == null || proxyShader == nativeShader)
                return 0;

            var converted = 0;
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (material == null || material.shader != proxyShader)
                    continue;

                // Preserve every value configured in Unity before replacing the shader.
                var snapshot = new Material(material);
                try
                {
                    material.shader = nativeShader;
                    material.CopyPropertiesFromMaterial(snapshot);
                    ++converted;
                }
                finally
                {
                    UnityEngine.Object.Destroy(snapshot);
                }
            }

            return converted;
        }
    }
}
