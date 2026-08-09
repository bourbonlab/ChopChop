using HarmonyLib;
using Service.WorldObject;
using UnityEngine;
using WorldObjects;

namespace MoreWood;

/// <summary>
/// The single chokepoint. <c>SpawnOnDestroy.Spawn</c> is static and every drop in the game routes
/// through it, so rewriting its <c>spawns</c> argument covers trees, logs and respawners at once
/// without touching the loop that does the actual instantiation.
/// </summary>
[HarmonyPatch(typeof(SpawnOnDestroy), nameof(SpawnOnDestroy.Spawn))]
internal static class SpawnPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static void Prefix(ref SpawnOnDestroy.SpawnObjectData[] spawns)
    {
        // A throwing prefix would swallow the drop entirely and leave the player with nothing,
        // so failures degrade to vanilla behaviour instead.
        try
        {
            spawns = SpawnRewriter.Rewrite(spawns);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Failed to rewrite spawn table, falling back to vanilla amounts: {e}");
        }
    }

    /// <summary>
    /// Reference to the original signature so the build breaks loudly if a game update changes it,
    /// rather than the patch silently failing to apply at runtime.
    /// </summary>
    private static void SignatureGuard(IWorldObjectService s, SpawnOnDestroy.SpawnObjectData[] d, GameObject g)
        => SpawnOnDestroy.Spawn(s, d, g);
}
