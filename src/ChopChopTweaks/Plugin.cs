using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Service.MinigameCrafting;
using UnityEngine;
using WorldObjects.Player;

namespace ChopChopTweaks;

/// <summary>
/// Quality-of-life and balance tweaks: axe damage, money scaling, movement speed, crafting
/// minigame skip and an item magnet.
///
/// Every setting defaults to vanilla behaviour - multipliers at 1.0, toggles off, magnet radius
/// at 0 - so installing this plugin changes nothing until it is configured.
/// </summary>
[BepInPlugin(Guid, "Chop Chop Tweaks", "1.0.0")]
[BepInProcess("ChopChopInc.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "chopchopmods.tweaks";

    internal static ManualLogSource Log { get; private set; }

    internal static ConfigEntry<float> AxeDamageMultiplier;

    internal static ConfigEntry<float> IncomeMultiplier;
    internal static ConfigEntry<float> CostMultiplier;

    internal static ConfigEntry<float> WalkSpeedMultiplier;
    internal static ConfigEntry<float> RunSpeedMultiplier;

    internal static ConfigEntry<bool> SkipCraftingMinigame;

    internal static ConfigEntry<float> MagnetRadius;
    internal static ConfigEntry<float> MagnetInterval;
    internal static ConfigEntry<bool> MagnetIncludesActiveCollectables;

    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;

        AxeDamageMultiplier = Config.Bind(
            "1. Axe", "DamageMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies damage dealt to whatever you hit - higher means fewer swings per tree. "
                + "Only scales damage (health going down), never healing or repair, so tools that "
                + "restore health are unaffected. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 50f)));

        IncomeMultiplier = Config.Bind(
            "2. Economy", "IncomeMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies money received from selling. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0f, 50f)));

        CostMultiplier = Config.Bind(
            "2. Economy", "CostMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies money spent in the shop. Below 1.0 makes things cheaper; 0 makes them "
                + "free. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0f, 10f)));

        WalkSpeedMultiplier = Config.Bind(
            "3. Movement", "WalkSpeedMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies walk speed. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 10f)));

        RunSpeedMultiplier = Config.Bind(
            "3. Movement", "RunSpeedMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies sprint speed. Note the game derives ground acceleration from the ratio "
                + "of run to walk speed, so raising this alone also makes sprinting accelerate "
                + "harder. Scale both together to keep the vanilla feel. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 10f)));

        SkipCraftingMinigame = Config.Bind(
            "4. Crafting", "SkipMinigame", false,
            "Craft instantly when a recipe is picked at a minigame crafting station, instead of "
            + "playing the minigame. Ingredients are still required and consumed normally.");

        MagnetRadius = Config.Bind(
            "5. Item magnet", "Radius", 0.0f,
            new ConfigDescription(
                "Collect nearby items automatically within this radius in metres. 0 disables the "
                + "magnet entirely (the default).",
                new AcceptableValueRange<float>(0f, 50f)));

        MagnetInterval = Config.Bind(
            "5. Item magnet", "IntervalSeconds", 0.2f,
            new ConfigDescription(
                "How often the magnet scans. Lower feels snappier but costs more CPU; the scan is "
                + "a physics overlap query, not a per-frame object search.",
                new AcceptableValueRange<float>(0.05f, 2f)));

        MagnetIncludesActiveCollectables = Config.Bind(
            "5. Item magnet", "IncludeActiveCollectables", false,
            "Whether the magnet also grabs items the game intends you to pick up deliberately "
            + "(those with an ActiveCollectable component). Off by default, since hoovering these "
            + "up can bypass intended interactions.");

        VerifyPatchTargets();

        harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);

        // Per-frame behaviours rather than patches, so they need a live GameObject.
        var host = new GameObject("ChopChopTweaks.Behaviours");
        host.transform.SetParent(gameObject.transform);
        host.AddComponent<ItemMagnet>();
        host.AddComponent<CraftUIRestorer>();

        Log.LogInfo(
            $"Chop Chop Tweaks loaded. Axe x{AxeDamageMultiplier.Value}, "
            + $"income x{IncomeMultiplier.Value}, cost x{CostMultiplier.Value}, "
            + $"walk x{WalkSpeedMultiplier.Value}, run x{RunSpeedMultiplier.Value}, "
            + $"skipMinigame={SkipCraftingMinigame.Value}, magnet={MagnetRadius.Value}m.");
    }

    /// <summary>
    /// Most patch targets are referenced through typed <c>nameof</c>, so the compiler catches a
    /// renamed method. These two cannot be: <c>Move.Awake</c> is matched by string, and
    /// <c>MinigameSkipPatch</c> reads the service's private <c>crafter</c> field by name. Check
    /// them up front so a game update produces one clear line instead of a Harmony stack trace.
    /// </summary>
    private static void VerifyPatchTargets()
    {
        if (AccessTools.Method(typeof(Move), "Awake") == null)
        {
            Log.LogWarning("Move.Awake not found - movement speed multipliers will not apply.");
        }

        if (AccessTools.Field(typeof(MinigameCraftingServiceImpl), "crafter") == null)
        {
            Log.LogWarning(
                "MinigameCraftingServiceImpl.crafter not found - minigame skip will not apply.");
        }
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}
