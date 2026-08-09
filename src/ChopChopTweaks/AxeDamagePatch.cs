using HarmonyLib;
using UnityEngine;
using WorldObjects.Items.ItemActions;

namespace ChopChopTweaks;

/// <summary>
/// Scales the damage an item deals. <c>Health.ChangeHealth</c> does <c>CurrentHealth += delta</c>,
/// so damage is a negative delta and healing is positive - only the negative side is scaled, which
/// keeps repair and healing items at vanilla strength.
///
/// <c>delta</c> is a field on a component instance, so it is restored in a Finalizer rather than
/// written permanently.
/// </summary>
[HarmonyPatch(typeof(ChangeTargetHealth), nameof(ChangeTargetHealth.Use))]
internal static class AxeDamagePatch
{
    private const int Untouched = int.MinValue;

    [HarmonyPrefix]
    private static void Prefix(ChangeTargetHealth __instance, out int __state)
    {
        __state = Untouched;

        float multiplier = Plugin.AxeDamageMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f))
        {
            return;
        }

        int original = __instance.delta;

        // Positive delta heals; leave those alone regardless of the multiplier.
        if (original >= 0)
        {
            return;
        }

        int scaled = Mathf.RoundToInt(original * multiplier);

        // A multiplier above 1 must never round a hit down to zero damage.
        if (scaled >= 0)
        {
            scaled = -1;
        }

        if (scaled == original)
        {
            return;
        }

        __state = original;
        __instance.delta = scaled;
    }

    [HarmonyFinalizer]
    private static void Finalizer(ChangeTargetHealth __instance, int __state)
    {
        if (__state != Untouched && __instance != null)
        {
            __instance.delta = __state;
        }
    }
}
