using HarmonyLib;
using UnityEngine;
using WorldObjects.Items;

namespace ChopChopTweaks;

/// <summary>
/// Scales how fast items swing, and with it how fast you can swing again.
///
/// There is no cooldown to shorten - the rate limit is the animation itself.
/// <c>Item.Use</c> only fires when <c>AnimationCallbacks.CanUse</c> is true, and that is
/// <c>animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")</c>, so the next swing has to wait for
/// the Use clip to finish and hand back to Idle. The hit lands on an animation event partway
/// through, which calls <c>Item._Use</c> and runs the item's actions.
///
/// So <c>animator.speed</c> is the whole feature: it scales the windup, the hit event and the
/// recovery together, which is what makes it a true attack speed rather than just an early hit.
/// The first-person hands follow for free - <c>FPSHandVisuals</c> does not animate the swing
/// itself, it snaps the hands to <c>currentItem.GetPivot()</c> every LateUpdate, and that pivot is
/// the item's animated transform.
///
/// Applied per swing rather than once at Awake, so changing the setting in-game takes effect on the
/// next swing with no reload - unlike the movement multipliers.
/// </summary>
[HarmonyPatch(typeof(AnimationCallbacks), nameof(AnimationCallbacks.PlayUseAnimation))]
internal static class SwingSpeedPatch
{
    /// <param name="___animator">
    /// The component's private animator, the one whose Idle state gates the next swing.
    /// </param>
    [HarmonyPrefix]
    private static void Prefix(Animator ___animator)
    {
        if (___animator == null)
        {
            return;
        }

        // Assigned unconditionally rather than only when it differs from 1, so lowering the
        // multiplier back down in-game restores the vanilla rate instead of leaving the last
        // value stuck on the animator.
        ___animator.speed = Plugin.SwingSpeedMultiplier.Value;
    }
}
