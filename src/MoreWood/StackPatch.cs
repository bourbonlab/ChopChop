using HarmonyLib;
using UnityEngine;
using WorldObjects.Items;

namespace MoreWood;

/// <summary>
/// Multiplies how much a single pickup is worth, instead of spawning more physical objects.
/// One trunk worth 5 costs nothing at runtime; five trunks are five rigidbodies interpenetrating
/// on one spawn point.
///
/// The boost is applied around <see cref="Collectable.TryToCollect"/> and undone in a Finalizer,
/// so <c>saveData.amount</c> on disk is never touched. That matters: the game itself does
/// read-modify-write on this value (<c>OnTrigger_SpawnShopOrders</c> runs
/// <c>component.Amount *= order.count</c>), so a boost left in place would compound into the
/// save and keep growing.
/// </summary>
[HarmonyPatch(typeof(Collectable), nameof(Collectable.TryToCollect))]
internal static class StackPatch
{
    /// <summary>Sentinel meaning "this pickup was not modified, leave it alone".</summary>
    private const int Untouched = int.MinValue;

    [HarmonyPrefix]
    private static void Prefix(Collectable __instance, out int __state)
    {
        __state = Untouched;

        try
        {
            // Instantiated objects carry Unity's "(Clone)" suffix, which the whitelist's
            // substring match handles without the caller needing to know.
            float multiplier = SpawnRewriter.StackMultiplierFor(__instance.gameObject.name);
            if (Mathf.Approximately(multiplier, 1f))
            {
                return;
            }

            int original = __instance.Amount;
            if (original <= 0)
            {
                return;
            }

            int boosted = Mathf.Clamp(
                Mathf.RoundToInt(original * multiplier), 1, Plugin.MaxAmountPerEntry.Value);

            if (boosted == original)
            {
                return;
            }

            __state = original;
            __instance.Amount = boosted;

            if (Plugin.LogSpawns.Value)
            {
                Plugin.Log.LogInfo(
                    $"[Stack] {__instance.gameObject.name}: {original} -> {boosted} (x{multiplier:0.##})");
            }
        }
        catch (System.Exception e)
        {
            __state = Untouched;
            Plugin.Log.LogError($"Stack boost failed, collecting vanilla amount: {e}");
        }
    }

    /// <summary>
    /// Runs even if the game's own collect logic throws, so a boosted amount can never be left
    /// behind on an object that survived the pickup attempt.
    /// </summary>
    [HarmonyFinalizer]
    private static void Finalizer(Collectable __instance, int __state)
    {
        if (__state == Untouched || __instance == null)
        {
            return;
        }

        try
        {
            __instance.Amount = __state;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Failed to restore stack amount: {e}");
        }
    }
}
