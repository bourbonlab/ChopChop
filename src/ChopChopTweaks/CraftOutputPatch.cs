using HarmonyLib;
using UnityEngine;

namespace ChopChopTweaks;

/// <summary>
/// Multiplies what a craft produces.
///
/// <c>Crafter.SpawnRecipeItem</c> spawns one item and sets its <c>Collectable.Amount</c>, and every
/// craft output goes through it. Scaling the <c>amount</c> argument multiplies the yield without
/// spawning a single extra object: recipes flagged <c>spawnAsSingleItem</c> call it once with the
/// full amount, and the rest call it once per unit with 1, so either way the objects stay as they
/// were and only their stack size grows. That matters - the alternative, spawning more items, drops
/// several rigidbodies on one point and lets the physics solver fling them across the map, which is
/// the problem More Wood's ScatterRadius exists to work around.
///
/// Patching the argument rather than <c>recipe.output</c> is also what keeps this safe. Recipe is a
/// ScriptableObject and its output array is shared by every crafter using that recipe, so writing
/// the amounts there would compound on every craft and persist into the asset. Nothing here touches
/// recipe data. No undo is needed either: the boosted value lands on a freshly spawned Collectable
/// rather than being read back and rewritten, so it cannot accumulate the way a stack boost applied
/// at pickup time would.
/// </summary>
[HarmonyPatch(typeof(WorldObjects.Useables.Crafter), "SpawnRecipeItem")]
internal static class CraftOutputPatch
{
    /// <param name="amount">
    /// Stack size for the item about to spawn, scaled in place. Every caller is inside
    /// <c>Crafter.Craft</c>, so this covers hand crafting, the skipped minigame and automated
    /// crafters alike.
    /// </param>
    [HarmonyPrefix]
    private static void Prefix(ref int amount)
    {
        float multiplier = Plugin.CraftOutputMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f) || amount <= 0)
        {
            return;
        }

        amount = Mathf.Min(ScaleAmount(amount, multiplier), Plugin.MaxCraftOutputPerItem.Value);
    }

    /// <summary>
    /// Scales an amount, keeping the fractional part meaningful. A x1.5 multiplier on a single item
    /// cannot give 1.5 of it, so it gives 1 or 2 with the right long-run average instead of silently
    /// rounding away. Mirrors More Wood's SpawnRewriter.ScaleAmount.
    /// </summary>
    private static int ScaleAmount(int amount, float multiplier)
    {
        float scaled = amount * multiplier;
        int whole = Mathf.FloorToInt(scaled);
        float fraction = scaled - whole;
        return (fraction > 0f && Random.value < fraction) ? whole + 1 : whole;
    }
}
