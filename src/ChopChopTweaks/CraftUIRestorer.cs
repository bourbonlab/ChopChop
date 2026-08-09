using Service.MinigameCrafting;
using UnityEngine;

namespace ChopChopTweaks;

/// <summary>
/// Returns to the recipe list after an instant craft.
///
/// The Craft Now button runs three steps: <c>SetRecipe</c>, open the minigame HUD, then close the
/// recipe list. <see cref="MinigameSkipPatch"/> replaces only the first, so with the minigame
/// skipped the HUD opens over nothing and nothing ever closes it - the minigame object that would
/// normally close it on completion was never spawned. The only ways out are the stop button and
/// escape, both of which leave the crafting station entirely.
///
/// The other two steps run after the patch returns, so the repair has to happen later in the same
/// frame. uGUI raises button clicks from EventSystem's Update, and every Update runs before any
/// LateUpdate, so restoring here lands after the click handler has finished but still before the
/// frame is drawn: the HUD is never visible and the recipe list stays up for the next click.
/// </summary>
internal class CraftUIRestorer : MonoBehaviour
{
    /// <summary>
    /// The service to restore at the end of this frame, or null when nothing is pending. Static
    /// because the requesting Harmony patch is static; only ever touched on the main thread.
    /// </summary>
    private static IMinigameCraftingService pending;

    /// <summary>Queues a return to the recipe list at the end of the current frame.</summary>
    internal static void Request(IMinigameCraftingService service)
    {
        pending = service;
    }

    private void LateUpdate()
    {
        if (pending == null)
        {
            return;
        }

        // Cleared before the call so a throw cannot leave the request stuck, retrying every frame.
        IMinigameCraftingService service = pending;
        pending = null;

        try
        {
            // Closes the minigame HUD and reopens the recipe list, keeping the previously chosen
            // recipe selected. This is the same call the game makes when a station is first used.
            service.ShowRecipeSelection();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Could not reopen the recipe list after an instant craft: {e}");
        }
    }
}
