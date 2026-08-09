using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using WorldObjects;

namespace MoreWood;

/// <summary>
/// Turns the game's spawn table into a multiplied copy.
///
/// The incoming <c>SpawnObjectData[]</c> belongs to a live component and, for prefab-backed
/// objects, is shared by every instance of that prefab. Mutating it in place would compound on
/// every chop, so entries that actually change are cloned and the original is left alone.
/// </summary>
internal static class SpawnRewriter
{
    private static Dictionary<string, float> overrides;
    private static KeyValuePair<string, float>[] overridesByLength;
    private static string[] whitelist;

    internal static void InvalidateOverrides()
    {
        overrides = null;
        overridesByLength = null;
        whitelist = null;
    }

    internal static SpawnOnDestroy.SpawnObjectData[] Rewrite(SpawnOnDestroy.SpawnObjectData[] spawns)
    {
        if (spawns == null || spawns.Length == 0 || !Plugin.Enabled.Value)
        {
            return spawns;
        }

        SpawnTrigger trigger = SpawnTriggerTracker.Current;
        float categoryMultiplier = MultiplierFor(trigger);
        int cap = Plugin.MaxAmountPerEntry.Value;
        float scatter = Plugin.ScatterRadius.Value;
        bool verbose = Plugin.LogSpawns.Value;

        SpawnOnDestroy.SpawnObjectData[] result = null;

        for (int i = 0; i < spawns.Length; i++)
        {
            SpawnOnDestroy.SpawnObjectData entry = spawns[i];
            if (entry?.prefab == null || entry.amount <= 0)
            {
                continue;
            }

            string prefabName = entry.prefab.name;
            float multiplier = ResolveMultiplier(prefabName, categoryMultiplier);
            int amount = Mathf.Clamp(ScaleAmount(entry.amount, multiplier), 0, cap);

            if (verbose)
            {
                Plugin.Log.LogInfo(
                    $"[{trigger}] {prefabName}: {entry.amount} -> {amount} (x{multiplier:0.##})");
            }

            if (amount == entry.amount)
            {
                continue;
            }

            // First change in this table: copy the array so we can swap entries safely.
            result ??= (SpawnOnDestroy.SpawnObjectData[])spawns.Clone();

            SpawnOnDestroy.SpawnObjectData clone = Clone(entry);
            clone.amount = amount;

            // Drops with no built-in randomness all land on one point. That is fine for the single
            // object the game intended, but a stack of them interpenetrates and the physics solver
            // launches them across the map. Spread the extras out horizontally instead.
            if (scatter > 0f && amount > 1 && clone.randomOffset.sqrMagnitude <= 0.0001f)
            {
                clone.randomOffset = new Vector3(scatter, 0f, scatter);
            }

            result[i] = clone;
        }

        return result ?? spawns;
    }

    /// <summary>
    /// Stack multiplier for a pickup, or 1 when it should be left alone.
    ///
    /// Deliberately gated on <c>OnlyThesePrefabs</c> alone: an empty whitelist means no stacking
    /// at all, rather than stacking everything. Stack size is credited straight into the player's
    /// inventory, so a mis-scoped multiplier here is far harder to notice than extra objects on
    /// the ground.
    /// </summary>
    internal static float StackMultiplierFor(string prefabName)
    {
        if (!Plugin.Enabled.Value)
        {
            return 1f;
        }

        float multiplier = Plugin.StackMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f))
        {
            return 1f;
        }

        EnsureParsed();
        return (whitelist.Length > 0 && MatchesWhitelist(prefabName)) ? multiplier : 1f;
    }

    private static float MultiplierFor(SpawnTrigger trigger) => trigger switch
    {
        SpawnTrigger.Damaged => Plugin.MultiplierOnDamage.Value,
        SpawnTrigger.Respawner => Plugin.MultiplierRespawner.Value,
        // Unknown call paths are far more likely to be a destruction drop than a respawn tick.
        _ => Plugin.MultiplierOnDestroy.Value,
    };

    private static float ResolveMultiplier(string prefabName, float fallback)
    {
        EnsureParsed();

        if (overrides.Count > 0)
        {
            if (overrides.TryGetValue(prefabName, out float exact))
            {
                return exact;
            }

            // Sorted longest-key-first, so a prefab matching several fragments takes the most
            // specific one. Without the ordering this depends on dictionary iteration order:
            // 'Log_Stump_01' against both Log=5 and Stump=1 would pick arbitrarily.
            foreach (KeyValuePair<string, float> pair in overridesByLength)
            {
                if (prefabName.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return pair.Value;
                }
            }
        }

        if (whitelist.Length > 0 && !MatchesWhitelist(prefabName))
        {
            return 1f;
        }

        return fallback;
    }

    private static bool MatchesWhitelist(string prefabName)
    {
        foreach (string fragment in whitelist)
        {
            if (prefabName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scales an amount, keeping the fractional part meaningful. A x1.5 multiplier on a single
    /// drop cannot give 1.5 objects, so it gives 1 or 2 with the right long-run average instead of
    /// silently rounding away.
    /// </summary>
    private static int ScaleAmount(int amount, float multiplier)
    {
        float scaled = amount * multiplier;
        int whole = Mathf.FloorToInt(scaled);
        float fraction = scaled - whole;
        return (fraction > 0f && UnityEngine.Random.value < fraction) ? whole + 1 : whole;
    }

    private static SpawnOnDestroy.SpawnObjectData Clone(SpawnOnDestroy.SpawnObjectData source) => new()
    {
        prefab = source.prefab,
        amount = source.amount,
        spawnPosition = source.spawnPosition,
        useRotation = source.useRotation,
        useScale = source.useScale,
        randomOffset = source.randomOffset,
        localspaceRandomOffset = source.localspaceRandomOffset,
        randomRotation = source.randomRotation,
        localspaceRandomRotation = source.localspaceRandomRotation,
        raycast = source.raycast,
        raycastLayers = source.raycastLayers,
        moveAlongSpline = source.moveAlongSpline,
    };

    private static void EnsureParsed()
    {
        if (overrides == null)
        {
            overrides = ParseOverrides(Plugin.PrefabOverrides.Value);

            var byLength = new List<KeyValuePair<string, float>>(overrides);
            byLength.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            overridesByLength = byLength.ToArray();
        }

        whitelist ??= ParseWhitelist(Plugin.PrefabWhitelist.Value);
    }

    private static Dictionary<string, float> ParseOverrides(string raw)
    {
        var parsed = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return parsed;
        }

        foreach (string chunk in raw.Split(','))
        {
            string entry = chunk.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            int split = entry.LastIndexOf('=');
            if (split <= 0)
            {
                Plugin.Log.LogWarning($"PrefabOverrides: skipping '{entry}', expected Name=Multiplier.");
                continue;
            }

            string name = entry.Substring(0, split).Trim();
            string value = entry.Substring(split + 1).Trim();

            if (name.Length == 0 ||
                !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier))
            {
                Plugin.Log.LogWarning($"PrefabOverrides: skipping '{entry}', '{value}' is not a number.");
                continue;
            }

            parsed[name] = Mathf.Max(0f, multiplier);
        }

        return parsed;
    }

    private static string[] ParseWhitelist(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var fragments = new List<string>();
        foreach (string chunk in raw.Split(','))
        {
            string fragment = chunk.Trim();
            if (fragment.Length > 0)
            {
                fragments.Add(fragment);
            }
        }

        return fragments.ToArray();
    }
}
