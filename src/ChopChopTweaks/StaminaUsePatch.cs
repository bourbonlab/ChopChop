using HarmonyLib;
using UnityEngine;
using WorldObjects.Player;

namespace ChopChopTweaks;

/// <summary>
/// Scales how much stamina anything costs, down to nothing at 0.
///
/// Every stamina cost in the game is a negative delta through <c>PlayerStats.ChangeStatValue</c> -
/// sprinting drains it from <c>CustomCharacterController.UpdateVelocity</c>
/// (<c>-staminaDecreasePerSecond * deltaTime</c>), and items and missions spend it through
/// <c>ChangePlayerStatValue</c> / <c>MissionAction_ChangePlayerStat</c>. Patching the one method
/// they all funnel through covers the lot, instead of chasing each drain separately.
///
/// Only the negative side is scaled, so regeneration - the <c>statValueAutoChange</c> entry ticked
/// from <c>PlayerStats.Tick</c>, a positive delta - keeps its vanilla rate. Scaling both would make
/// the multiplier a no-op at the extremes, since draining and refilling half as fast is the same
/// stamina budget.
///
/// The public method is the patch target rather than <c>ChangeStatValueInternal</c> on purpose.
/// Timed effects store the delta they were given and hand back that same stored value when they
/// expire (<c>ChangeStatValueInternal(stat, -reference.delta, -1f)</c>), so scaling on the way in
/// stays symmetrical; scaling the internal method would scale the revert a second time and drift
/// the stat.
/// </summary>
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.ChangeStatValue))]
internal static class StaminaUsePatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerStats.PlayerStat stat, ref float delta)
    {
        if (stat != PlayerStats.PlayerStat.Stamina || delta >= 0f)
        {
            return;
        }

        float multiplier = Plugin.StaminaUseMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f))
        {
            return;
        }

        // 0 lands exactly on 0, which the game handles as a no-op change: nothing moves and no
        // StatValueChanged event fires, so the bar simply stops falling.
        delta *= multiplier;
    }
}
