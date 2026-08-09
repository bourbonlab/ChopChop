using HarmonyLib;
using UnityEngine;
using WorldObjects.Player;

namespace ChopChopTweaks;

/// <summary>
/// Scales movement speed by rewriting <c>Move.Data</c> once per component, in Awake.
///
/// Unity deserializes a fresh <c>Data</c> instance per component, so this is per-player state and
/// not a shared asset - unlike the spawn tables and ScriptableObjects elsewhere in this codebase,
/// writing it is safe. Instances are tracked anyway so a re-patch or a second Awake cannot compound
/// the multiplier.
/// </summary>
[HarmonyPatch(typeof(Move), "Awake")]
internal static class MoveSpeedPatch
{
    /// <summary>
    /// Conditional weak table rather than a HashSet: entries disappear with the component, so
    /// reloading a save does not leak Move instances.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Move, object> Applied = new();

    [HarmonyPostfix]
    private static void Postfix(Move __instance)
    {
        float walk = Plugin.WalkSpeedMultiplier.Value;
        float run = Plugin.RunSpeedMultiplier.Value;

        if (Mathf.Approximately(walk, 1f) && Mathf.Approximately(run, 1f))
        {
            return;
        }

        if (__instance?.data == null || Applied.TryGetValue(__instance, out _))
        {
            return;
        }

        Applied.Add(__instance, null);

        float oldWalk = __instance.data.walkSpeed;
        float oldRun = __instance.data.runSpeed;

        __instance.data.walkSpeed = oldWalk * walk;
        __instance.data.runSpeed = oldRun * run;

        Plugin.Log.LogInfo(
            $"Move speed: walk {oldWalk:0.##} -> {__instance.data.walkSpeed:0.##}, "
            + $"run {oldRun:0.##} -> {__instance.data.runSpeed:0.##}");
    }
}
