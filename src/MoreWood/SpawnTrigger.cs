using HarmonyLib;
using WorldObjects;

namespace MoreWood;

/// <summary>What caused the current call into <c>SpawnOnDestroy.Spawn</c>.</summary>
public enum SpawnTrigger
{
    /// <summary>Reached Spawn by a path we do not recognise; treated as OnDestroy.</summary>
    Unknown,

    /// <summary>A <c>Destructible</c> hit zero health - tree became logs, log became wood.</summary>
    Destroyed,

    /// <summary>A <c>SpawnOnChangeHealth</c> tick - drops that fire on each axe hit.</summary>
    Damaged,

    /// <summary>A timed <c>Spawner</c> regrowing a world resource.</summary>
    Respawner,
}

/// <summary>
/// <c>SpawnOnDestroy.Spawn</c> is static and shared by three unrelated callers, and its arguments
/// carry no hint about which one is running. These patches wrap each caller so the rewrite in
/// <see cref="SpawnRewriter"/> can apply a different multiplier per trigger.
///
/// The value is restored by a Finalizer rather than a Postfix so it unwinds correctly even if the
/// game's own code throws mid-spawn.
/// </summary>
internal static class SpawnTriggerTracker
{
    /// <summary>
    /// Unity's gameplay loop is single-threaded, so a plain static is sufficient and avoids the
    /// per-access cost of [ThreadStatic].
    /// </summary>
    internal static SpawnTrigger Current = SpawnTrigger.Unknown;

    [HarmonyPatch(typeof(SpawnOnDestroy), "OnWorldObjectDestroy")]
    private static class Destroyed
    {
        private static void Prefix(out SpawnTrigger __state)
        {
            __state = Current;
            Current = SpawnTrigger.Destroyed;
        }

        private static void Finalizer(SpawnTrigger __state) => Current = __state;
    }

    [HarmonyPatch(typeof(SpawnOnChangeHealth), "OnHealthChanged")]
    private static class Damaged
    {
        private static void Prefix(out SpawnTrigger __state)
        {
            __state = Current;
            Current = SpawnTrigger.Damaged;
        }

        private static void Finalizer(SpawnTrigger __state) => Current = __state;
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.Tick))]
    private static class Respawner
    {
        private static void Prefix(out SpawnTrigger __state)
        {
            __state = Current;
            Current = SpawnTrigger.Respawner;
        }

        private static void Finalizer(SpawnTrigger __state) => Current = __state;
    }
}
