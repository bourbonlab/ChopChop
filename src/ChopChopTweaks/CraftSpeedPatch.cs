using HarmonyLib;
using UnityEngine;
using WorldObjects.Useables;

namespace ChopChopTweaks;

/// <summary>
/// Speeds up automated crafters.
///
/// <c>Crafter.Tick</c> advances <c>CurrentCraftTimer += deltaTime * craftSpeedFactor</c> and crafts
/// once the timer passes the recipe's craftTime. Scaling the incoming deltaTime is the whole patch:
/// it is the only thing Tick uses deltaTime for, the other branch merely resets the timer.
///
/// Scaling the argument rather than the <c>craftSpeedFactor</c> field is deliberate. That field is
/// owned by the game's upgrade system - it starts at 1, and <c>OnCraftSpeedUpgrade</c> does
/// <c>craftSpeedFactor += delta</c> as the player buys CraftSpeed upgrades. Writing it would be
/// overwritten by the next upgrade and would compound if applied twice. Leaving it alone means this
/// multiplier composes with upgrades instead of fighting them.
///
/// This only affects crafters running unattended: hand crafting goes through
/// <c>Useable.UseUseable -&gt; CraftDefault</c>, which crafts immediately and never consults the timer.
/// </summary>
[HarmonyPatch(typeof(Crafter), nameof(Crafter.Tick))]
internal static class CraftSpeedPatch
{
    /// <param name="deltaTime">
    /// Scaled in place. Tick applies it only to the craft timer, so nothing else drifts.
    /// </param>
    [HarmonyPrefix]
    private static void Prefix(ref float deltaTime)
    {
        float multiplier = Plugin.AutoCraftSpeedMultiplier.Value;
        if (!Mathf.Approximately(multiplier, 1f))
        {
            deltaTime *= multiplier;
        }
    }
}
