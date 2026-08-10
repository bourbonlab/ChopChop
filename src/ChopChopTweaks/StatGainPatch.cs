using HarmonyLib;
using UnityEngine;
using WorldObjects.Player;

namespace ChopChopTweaks;

/// <summary>
/// Multiplies the permanent stat points you earn - the treadmill's stamina and move speed, the
/// weight bench's strength, and any mission that hands out a stat.
///
/// Those upgrades all end up in <c>ChangePlayerStatValue.Use</c> or
/// <c>MissionAction_ChangePlayerStat.Trigger</c>, which raise the stat's ceiling
/// (<c>ChangeMaxStatValue</c>) and then the value itself (<c>ChangeStatValue</c>).
/// <see cref="StatGainMaxPatch"/> and <see cref="StatGainCurrentPatch"/> scale one each, and the
/// game applies them in that order, so a bigger gain has the headroom to land rather than being
/// clamped to the old maximum.
///
/// Gains are still hard-capped by the game: <c>PlayerStats.GetMaxHardcapDelta</c> trims a raise to
/// the headroom left under the stat's <c>minMaxHardCap</c>, so a large multiplier reaches the cap
/// sooner rather than passing through it.
///
/// The minimum is left alone entirely. It is a floor, not a stat point, and multiplying it could
/// push it past the ceiling.
/// </summary>
internal static class StatGain
{
    /// <summary>
    /// Whether this is a permanent gain worth scaling, as opposed to a loss or a timed buff.
    ///
    /// Timed changes - <c>activeTime > 0</c>, the food and drink path - are excluded: they are
    /// consumables rather than progression, and the value is handed back when the timer expires.
    /// <c>PlayerStats</c> clamps a negative <c>activeTime</c> to 0 before using it, so anything at
    /// or below 0 is permanent.
    /// </summary>
    internal static bool ShouldScale(float delta, float activeTime)
    {
        return delta > 0f
            && activeTime <= 0f
            && !Mathf.Approximately(Plugin.StatGainMultiplier.Value, 1f);
    }
}

/// <summary>
/// The stat's ceiling - the "+max stamina" half of an upgrade. Scaled for every stat.
/// </summary>
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.ChangeMaxStatValue))]
internal static class StatGainMaxPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float delta, float activeTime)
    {
        if (StatGain.ShouldScale(delta, activeTime))
        {
            delta *= Plugin.StatGainMultiplier.Value;
        }
    }
}

/// <summary>
/// The stat's current value, scaled for every stat except Stamina.
///
/// Stamina's current value is the bar rather than a stat point: it is what regeneration, sprinting
/// and a snack all move. Its ceiling still scales, so a stamina upgrade gives the multiplied amount
/// of extra bar. Strength and MoveSpeedFactor have no such bar - the game reads their current value
/// directly, as the damage factor in <c>ChangeTargetHealth.Use</c> and the speed factor in
/// <c>CustomCharacterController.UpdateVelocity</c> - so for those the current value is the stat
/// point, and it is scaled.
/// </summary>
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.ChangeStatValue))]
internal static class StatGainCurrentPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerStats.PlayerStat stat, ref float delta, float activeTime)
    {
        if (stat != PlayerStats.PlayerStat.Stamina && StatGain.ShouldScale(delta, activeTime))
        {
            delta *= Plugin.StatGainMultiplier.Value;
        }
    }
}
