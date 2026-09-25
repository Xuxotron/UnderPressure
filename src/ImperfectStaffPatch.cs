using System;
using System.Collections.Generic;
using HarmonyLib;
using TH20;

namespace UnderPressure
{
    // Only JobApplicant calls GenerateRandomTraits. Qualifications are generated separately.
    [HarmonyPatch(typeof(CharacterTraitsManager), "GenerateRandomTraits")]
    internal static class ImperfectStaffPatch
    {
        private static readonly Random Random = new Random();
        private static readonly HashSet<string> Positive = new HashSet<string>
        {
            "Trait_Charming", "Trait_MovementSpeed_Higher", "Trait_Happiness_Higher",
            "Trait_Training_Learn_Fast", "Trait_Training_Teach_Fast", "Trait_Hygiene_High",
            "Trait_Entertainer", "Trait_GreenFingers", "Trait_Energy_Higher",
            "Trait_Funny", "Trait_Healer", "Trait_Inspiring", "Trait_Pay_Low"
        };
        private static readonly HashSet<string> Negative = new HashSet<string>
        {
            "Trait_MovementSpeed_Lower", "Trait_Happiness_Lower", "Trait_Training_Learn_Slow",
            "Trait_WeakBladder", "Trait_Hygiene_Low", "Trait_Litterer", "Trait_Nasty",
            "Trait_ShortTemper", "Trait_Hangry", "Trait_Narcolepsy", "Trait_Boring",
            "Trait_Dirty", "Trait_Energy_Lower", "Trait_Evil", "Trait_NauseaInducing",
            "Trait_Pay_High", "Trait_ToiletRage"
        };

        private static void Postfix(CharacterTraitsManager __instance, StaffDefinition.Type __0,
            ref CharacterTraits __result)
        {
            if (!UnderPressurePlugin.ShouldUseImperfectStaff || __result == null) return;

            var chosen = new List<CharacterTraitDefinition>(3);
            var all = __instance.AllTraits.List;
            if (!Choose(all, Negative, __0, chosen) ||
                !Choose(all, Positive, __0, chosen) ||
                !Choose(all, Positive, __0, chosen))
            {
                UnderPressurePlugin.Log.LogWarning("No se pudo generar un candidato con 2 rasgos buenos y 1 malo.");
                return;
            }

            var flavour = (string)AccessTools.Field(typeof(CharacterTraits), "_flavourTraits")
                .GetValue(__result);
            __result = new CharacterTraits(chosen, flavour);
        }

        private static bool Choose(Dictionary<CharacterTraitDefinition, int> all,
            HashSet<string> category, StaffDefinition.Type staffType,
            List<CharacterTraitDefinition> chosen)
        {
            var candidates = new List<KeyValuePair<CharacterTraitDefinition, int>>();
            var total = 0;
            foreach (var pair in all)
            {
                var trait = pair.Key;
                var term = trait.ShortNameLocalisedMale.Term;
                var start = term == null ? -1 : term.IndexOf("Trait_", StringComparison.Ordinal);
                var end = term == null ? -1 : term.IndexOf("_ShortName", StringComparison.Ordinal);
                if (start < 0 || end <= start ||
                    !category.Contains(term.Substring(start, end - start)) ||
                    !trait.IsValidFor(staffType) || !trait.CanAdd(chosen) || pair.Value <= 0)
                    continue;
                candidates.Add(pair);
                total += pair.Value;
            }
            if (total <= 0) return false;
            var roll = Random.Next(total);
            foreach (var pair in candidates)
            {
                roll -= pair.Value;
                if (roll < 0)
                {
                    chosen.Add(pair.Key);
                    return true;
                }
            }
            return false;
        }
    }
}
