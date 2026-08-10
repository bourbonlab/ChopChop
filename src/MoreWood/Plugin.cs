using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MoreWood;

/// <summary>
/// Multiplies how much stuff trees and logs drop when they are chopped.
///
/// Every drop in the game funnels through the static
/// <c>WorldObjects.SpawnOnDestroy.Spawn(IWorldObjectService, SpawnObjectData[], GameObject)</c>,
/// so a single prefix there covers trees dropping logs, logs dropping wood, and
/// timed resource respawners. See <see cref="SpawnRewriter"/> for the actual rewrite.
/// </summary>
[BepInPlugin(Guid, "More Wood", "1.1.1")]
[BepInProcess("ChopChopInc.exe")]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "chopchopmods.morewood";

    internal static ManualLogSource Log { get; private set; }

    // Multipliers, one per spawn trigger.
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> MultiplierOnDestroy;
    internal static ConfigEntry<float> MultiplierOnDamage;
    internal static ConfigEntry<float> MultiplierRespawner;

    // Stack size - more per pickup rather than more objects.
    internal static ConfigEntry<float> StackMultiplier;

    // Targeting.
    internal static ConfigEntry<string> PrefabOverrides;
    internal static ConfigEntry<string> PrefabWhitelist;

    // Safety + feel.
    internal static ConfigEntry<int> MaxAmountPerEntry;
    internal static ConfigEntry<float> ScatterRadius;

    // Diagnostics.
    internal static ConfigEntry<bool> LogSpawns;

    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;

        Enabled = Config.Bind(
            "1. General", "Enabled", true,
            "Master switch. Turn off to leave drop amounts completely untouched.");

        MultiplierOnDestroy = Config.Bind(
            "2. Multipliers", "OnDestroy", 2.0f,
            new ConfigDescription(
                "Multiplier applied when an object is destroyed - a tree finishing its fall into logs, "
                + "or a log being chopped through into wood. This is the one you usually want. "
                + "Fractional values work: 1.5 gives every drop a 50% chance of one extra.",
                new AcceptableValueRange<float>(0f, 50f)));

        MultiplierOnDamage = Config.Bind(
            "2. Multipliers", "OnDamage", 1.0f,
            new ConfigDescription(
                "Multiplier for drops that fire on each hit rather than on destruction (SpawnOnChangeHealth). "
                + "This is often woodchips and debris, so raising it can spam particles - leave at 1.0 unless "
                + "'LogSpawns' shows something you actually want more of.",
                new AcceptableValueRange<float>(0f, 50f)));

        MultiplierRespawner = Config.Bind(
            "2. Multipliers", "Respawner", 1.0f,
            new ConfigDescription(
                "Multiplier for timed spawners that regrow world resources. Raising this stacks multiple "
                + "objects on one spawn point, so keep it at 1.0 unless you know the spawner scatters.",
                new AcceptableValueRange<float>(0f, 50f)));

        StackMultiplier = Config.Bind(
            "2b. Stack size", "StackMultiplier", 1.0f,
            new ConfigDescription(
                "Multiplies how much a single pickup is worth, instead of spawning more objects. "
                + "One trunk worth 5 is free at runtime; five separate trunks are five rigidbodies "
                + "landing on the same spot. Prefer this over raising OnDestroy.\n"
                + "ONLY applies to prefabs matched by 'OnlyThesePrefabs' below - if that is empty, "
                + "no stacking happens at all. Default 1.0 leaves pickups untouched.",
                new AcceptableValueRange<float>(1f, 50f)));

        PrefabWhitelist = Config.Bind(
            "3. Targeting", "OnlyThesePrefabs", "p_Trunk_",
            "Comma-separated prefab name fragments. When set, only prefabs whose name contains one of "
            + "these (case-insensitive) get multiplied. Empty means everything - which also disables "
            + "'StackMultiplier' entirely, so the default scopes both to tree trunks. "
            + "Example: Log,Wood");

        PrefabOverrides = Config.Bind(
            "3. Targeting", "PrefabOverrides", "",
            "Per-prefab multipliers that win over the category multiplier. "
            + "Format: Name=Multiplier, comma-separated. Exact name match is tried first, then "
            + "a case-insensitive 'contains' match. Example: Log_Pine=4, Wood_Small=3");

        MaxAmountPerEntry = Config.Bind(
            "4. Safety", "MaxAmountPerEntry", 200,
            new ConfigDescription(
                "Hard cap on how many objects a single spawn entry may produce, no matter the multiplier. "
                + "Guards against a typo turning one tree into a physics bomb.",
                new AcceptableValueRange<int>(1, 5000)));

        ScatterRadius = Config.Bind(
            "4. Safety", "ScatterRadius", 0.35f,
            new ConfigDescription(
                "When the game spawns drops at one exact point with no built-in randomness, extra copies "
                + "would land inside each other and get flung apart by physics. This adds a horizontal "
                + "random offset (in metres) to those stacks. Set to 0 to disable.",
                new AcceptableValueRange<float>(0f, 5f)));

        LogSpawns = Config.Bind(
            "5. Debug", "LogSpawns", false,
            "Log every drop to the BepInEx console: prefab name, trigger, original amount, new amount. "
            + "Turn this on once to learn the prefab names for 'OnlyThesePrefabs' and 'PrefabOverrides'.");

        // Overrides are parsed lazily and re-parsed whenever the config file changes on disk.
        PrefabOverrides.SettingChanged += (_, _) => SpawnRewriter.InvalidateOverrides();
        PrefabWhitelist.SettingChanged += (_, _) => SpawnRewriter.InvalidateOverrides();

        VerifyPatchTargets();

        harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"More Wood loaded. OnDestroy x{MultiplierOnDestroy.Value}, "
                    + $"OnDamage x{MultiplierOnDamage.Value}, Respawner x{MultiplierRespawner.Value}.");
    }

    /// <summary>
    /// The per-trigger patches in <see cref="SpawnTriggerTracker"/> target private methods by name,
    /// so the compiler cannot catch a game update that renames them. Check them up front to turn
    /// what would otherwise be a confusing Harmony stack trace into one actionable line.
    /// </summary>
    private static void VerifyPatchTargets()
    {
        (string type, string method)[] targets =
        {
            ("WorldObjects.SpawnOnDestroy", "OnWorldObjectDestroy"),
            ("WorldObjects.SpawnOnChangeHealth", "OnHealthChanged"),
            ("WorldObjects.Spawner", "Tick"),
        };

        foreach ((string type, string method) in targets)
        {
            if (AccessTools.Method(AccessTools.TypeByName(type), method) == null)
            {
                Log.LogWarning(
                    $"Could not find {type}.{method} - the game was probably updated. "
                    + "Per-trigger multipliers may fall back to the OnDestroy value.");
            }
        }
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}
