using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Service.MinigameCrafting;
using UnityEngine;
using WorldObjects.Items;
using WorldObjects.Player;
using WorldObjects.Useables;

namespace ChopChopTweaks;

/// <summary>
/// Quality-of-life and balance tweaks: axe damage, money scaling, movement speed, crafting
/// minigame skip, an item magnet and player stat scaling.
///
/// Every setting defaults to vanilla behaviour - multipliers at 1.0, toggles off, magnet radius
/// at 0 - so installing this plugin changes nothing until it is configured.
/// </summary>
[BepInPlugin(Guid, "Chop Chop Tweaks", "1.2.0")]
[BepInProcess("ChopChopInc.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "chopchopmods.tweaks";

    internal static ManualLogSource Log { get; private set; }

    internal static ConfigEntry<float> AxeDamageMultiplier;
    internal static ConfigEntry<float> SwingSpeedMultiplier;

    internal static ConfigEntry<float> IncomeMultiplier;
    internal static ConfigEntry<float> CostMultiplier;

    internal static ConfigEntry<float> WalkSpeedMultiplier;
    internal static ConfigEntry<float> RunSpeedMultiplier;

    internal static ConfigEntry<bool> SkipCraftingMinigame;
    internal static ConfigEntry<float> CraftOutputMultiplier;
    internal static ConfigEntry<int> MaxCraftOutputPerItem;
    internal static ConfigEntry<float> AutoCraftSpeedMultiplier;

    internal static ConfigEntry<float> MagnetRadius;
    internal static ConfigEntry<float> MagnetInterval;
    internal static ConfigEntry<bool> MagnetIncludesActiveCollectables;

    internal static ConfigEntry<float> StaminaUseMultiplier;
    internal static ConfigEntry<float> StatGainMultiplier;

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

        SwingSpeedMultiplier = Config.Bind(
            "1. Axe", "SwingSpeedMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies how fast items swing, and so how fast you can swing again - the game "
                + "gates the next swing on the animation returning to idle, there is no separate "
                + "cooldown. Applies to any item you use, not just the axe. Values much above 5 "
                + "give little extra, and can start dropping hits: the swing has to last long "
                + "enough for the animation event that deals the damage to fire. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 10f)));

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

        // Unlike every other setting here, the two speeds are read once when the player's Move
        // component wakes rather than on each use, so changing them in-game does nothing until the
        // component is rebuilt. Said in the description because that is what the in-game editor
        // shows - without it the sliders look broken.
        WalkSpeedMultiplier = Config.Bind(
            "3. Movement", "WalkSpeedMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies walk speed. 1.0 is vanilla. "
                + "APPLIES ON RESTART - changing this in-game has no effect until you reload a save.",
                new AcceptableValueRange<float>(0.1f, 10f)));

        RunSpeedMultiplier = Config.Bind(
            "3. Movement", "RunSpeedMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies sprint speed. Note the game derives ground acceleration from the ratio "
                + "of run to walk speed, so raising this alone also makes sprinting accelerate "
                + "harder. Scale both together to keep the vanilla feel. 1.0 is vanilla. "
                + "APPLIES ON RESTART - changing this in-game has no effect until you reload a save.",
                new AcceptableValueRange<float>(0.1f, 10f)));

        SkipCraftingMinigame = Config.Bind(
            "4. Crafting", "SkipMinigame", false,
            "Craft instantly when a recipe is picked at a minigame crafting station, instead of "
            + "playing the minigame. Ingredients are still required and consumed normally.");

        CraftOutputMultiplier = Config.Bind(
            "4. Crafting", "OutputMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies what every craft produces, by raising the stack size of the items it "
                + "spawns rather than spawning more of them - so it costs nothing at runtime. "
                + "Ingredient costs are unchanged, so this is also the recipe efficiency knob. "
                + "Applies to automated crafters too, where it compounds unattended. Fractions are "
                + "honoured: 1.5 gives 1 or 2 with the right long-run average. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 50f)));

        MaxCraftOutputPerItem = Config.Bind(
            "4. Crafting", "MaxOutputPerItem", 200,
            new ConfigDescription(
                "Hard cap on the stack size a single craft output can be scaled to. Guards against "
                + "a mistyped multiplier quietly producing an absurd stack.",
                new AcceptableValueRange<int>(1, 10000)));

        AutoCraftSpeedMultiplier = Config.Bind(
            "4. Crafting", "AutoCraftSpeedMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies how fast automated crafters work through their craft timer. Composes "
                + "with the game's own CraftSpeed upgrades rather than replacing them. Hand "
                + "crafting is unaffected - it has no timer to speed up. Note the game crafts at "
                + "most once per tick, so beyond roughly craftTime x tickrate this stops helping. "
                + "1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 50f)));

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

        StaminaUseMultiplier = Config.Bind(
            "6. Player stats", "StaminaUseMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies how much stamina everything costs - sprinting and anything else that "
                + "spends it. 0 stops stamina being consumed at all. Regeneration is left at the "
                + "vanilla rate either way, since scaling drain and refill together would cancel "
                + "out. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0f, 10f)));

        StatGainMultiplier = Config.Bind(
            "6. Player stats", "StatGainMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies the permanent stat points you earn - treadmill stamina and move speed, "
                + "weight bench strength, mission rewards. Raises both the stat and its maximum, "
                + "but not the game's hard cap, so a big multiplier reaches the ceiling sooner "
                + "rather than passing it. Temporary food and drink buffs are not scaled, and "
                + "neither is the stamina bar refilling - only its maximum. 1.0 is vanilla.",
                new AcceptableValueRange<float>(0.1f, 50f)));

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
            + $"swingSpeed x{SwingSpeedMultiplier.Value}, "
            + $"income x{IncomeMultiplier.Value}, cost x{CostMultiplier.Value}, "
            + $"walk x{WalkSpeedMultiplier.Value}, run x{RunSpeedMultiplier.Value}, "
            + $"skipMinigame={SkipCraftingMinigame.Value}, "
            + $"craftOutput x{CraftOutputMultiplier.Value}, "
            + $"autoCraftSpeed x{AutoCraftSpeedMultiplier.Value}, "
            + $"magnet={MagnetRadius.Value}m, "
            + $"staminaUse x{StaminaUseMultiplier.Value}, statGain x{StatGainMultiplier.Value}.");
    }

    /// <summary>
    /// Most patch targets are referenced through typed <c>nameof</c>, so the compiler catches a
    /// renamed method. These cannot be: <c>Move.Awake</c> and <c>Crafter.SpawnRecipeItem</c> are
    /// private and matched by string, and two patches reach private fields by name -
    /// <c>MinigameSkipPatch</c> the service's <c>crafter</c>, <c>SwingSpeedPatch</c> the
    /// <c>animator</c>. Check them up front so a game update produces one clear line instead of a
    /// Harmony stack trace.
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

        if (AccessTools.Field(typeof(AnimationCallbacks), "animator") == null)
        {
            Log.LogWarning(
                "AnimationCallbacks.animator not found - the swing speed multiplier will not apply.");
        }

        if (AccessTools.Method(typeof(Crafter), "SpawnRecipeItem") == null)
        {
            Log.LogWarning(
                "Crafter.SpawnRecipeItem not found - the craft output multiplier will not apply.");
        }
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}
