using System.Collections.Generic;
using UnityEngine;
using WorldObjects.Items;
using WorldObjects.Useables;

namespace ChopChopTweaks;

/// <summary>
/// Collects nearby items automatically.
///
/// This is a behaviour rather than a patch because there is no game code path to hook - nothing
/// polls for nearby pickups. Collection itself still goes through the game's public
/// <see cref="Collectable.TryToCollect"/>, which performs its own player, inventory and
/// player-stat checks, so the magnet cannot pick up anything the player could not have collected
/// by walking into it.
///
/// It does not follow that any moment is a safe time to collect. The game only ever calls
/// TryToCollect from a trigger on an active player, so scanning pauses whenever the player is
/// deactivated - see the check in <see cref="Update"/>.
/// </summary>
internal class ItemMagnet : MonoBehaviour
{
    /// <summary>Reused across scans; OverlapSphereNonAlloc avoids per-scan garbage.</summary>
    private readonly Collider[] buffer = new Collider[256];

    /// <summary>One object can own several colliders, so collect the distinct set per scan.</summary>
    private readonly HashSet<Collectable> found = new();

    private WorldObjects.Player.Player player;
    private float timer;

    private void Update()
    {
        float radius = Plugin.MagnetRadius.Value;
        if (radius <= 0f)
        {
            return;
        }

        timer += Time.deltaTime;
        if (timer < Plugin.MagnetInterval.Value)
        {
            return;
        }

        timer = 0f;

        // Unity's fake-null makes a destroyed player compare equal to null, so this also re-acquires
        // across save loads and respawns.
        if (player == null)
        {
            player = Object.FindFirstObjectByType<WorldObjects.Player.Player>();
            if (player == null)
            {
                return;
            }
        }

        // Fake-null covers a destroyed player but not a deactivated one, and any UI whose
        // HidePlayer returns true - the crafting screens among them - has UIServiceImpl call
        // SetActive(false) on the player while it is open. Collecting into a deactivated player is
        // actively harmful rather than merely useless: a wieldable item auto-equips through
        // CurrentItem.SetItem, which parents the new item to the inactive player, so the item's
        // Awake never runs and the services the game injects there stay null. The next equip then
        // throws inside Item.DestroyItem, and because the collectable was never consumed the magnet
        // finds it again on every scan.
        if (!player.gameObject.activeInHierarchy)
        {
            return;
        }

        Scan(radius);
    }

    private void Scan(float radius)
    {
        GameObject collector = player.gameObject;

        // Pickup colliders are triggers, which the default query setting would filter out.
        int count = Physics.OverlapSphereNonAlloc(
            collector.transform.position, radius, buffer, ~0, QueryTriggerInteraction.Collide);

        found.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider hit = buffer[i];
            if (hit == null)
            {
                continue;
            }

            Collectable collectable = hit.GetComponentInParent<Collectable>();
            if (collectable == null)
            {
                continue;
            }

            // Items the game wants picked up deliberately are left alone unless opted in.
            if (!Plugin.MagnetIncludesActiveCollectables.Value
                && collectable.GetComponent<ActiveCollectable>() != null)
            {
                continue;
            }

            found.Add(collectable);
        }

        foreach (Collectable collectable in found)
        {
            // TryToCollect destroys the object, and earlier iterations may already have destroyed
            // this one as a child of something else.
            if (collectable == null)
            {
                continue;
            }

            try
            {
                collectable.TryToCollect(collector);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Magnet failed to collect {collectable.name}: {e}");
            }
        }
    }
}
