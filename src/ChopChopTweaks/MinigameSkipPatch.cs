using HarmonyLib;
using ScriptableObjects;
using Service.MinigameCrafting;
using WorldObjects.Useables;

namespace ChopChopTweaks;

/// <summary>
/// Crafts immediately instead of spawning the crafting minigame.
///
/// <c>MinigameCraftingServiceImpl.SetRecipe</c> normally instantiates the recipe's
/// <c>minigameCraftingPrefab</c>, and the resulting <c>MinigameCraftingObject</c> calls
/// <c>crafter.Craft(recipe)</c> once solved. Skipping the object and calling <c>Craft</c> directly
/// reaches the same end state through the game's own public API, so ingredients are still checked
/// and consumed exactly as normal.
///
/// Callers assume a minigame is now running and open its HUD, so each craft also queues a
/// <see cref="CraftUIRestorer"/> pass to put the recipe list back.
/// </summary>
[HarmonyPatch(typeof(MinigameCraftingServiceImpl), nameof(MinigameCraftingServiceImpl.SetRecipe))]
internal static class MinigameSkipPatch
{
    /// <param name="___crafter">
    /// The service's private crafter field, populated by StartMinigame before any recipe can be
    /// chosen.
    /// </param>
    /// <param name="__instance">The service itself, needed to reopen the recipe list afterwards.</param>
    /// <returns>False to skip the original method.</returns>
    [HarmonyPrefix]
    private static bool Prefix(Recipe recipe, Crafter ___crafter, MinigameCraftingServiceImpl __instance)
    {
        if (!Plugin.SkipCraftingMinigame.Value)
        {
            return true;
        }

        if (recipe == null || ___crafter == null)
        {
            // Fall through to the original so its own error handling reports the problem.
            return true;
        }

        try
        {
            if (___crafter.HasAllIngredients(recipe))
            {
                ___crafter.Craft(recipe);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Instant craft failed, falling back to the minigame: {e}");
            return true;
        }

        // Queued even when the ingredients ran out, because the caller opens the minigame HUD
        // either way and no minigame will ever come along to close it.
        CraftUIRestorer.Request(__instance);
        return false;
    }
}
