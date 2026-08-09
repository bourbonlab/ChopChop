using HarmonyLib;
using Service.Money;
using UnityEngine;

namespace ChopChopTweaks;

/// <summary>
/// Scales money in and out. <c>MoneyServiceImpl.Change</c> is the only mutation point, and its
/// sign distinguishes the two cases: selling calls it with a positive amount
/// (<c>OnTrigger_SellInventoryContent</c>, <c>MissionAction_SellInventoryContent</c>), the shop
/// calls it with a negative one (<c>UIShop</c>). So income and costs get separate multipliers
/// rather than one that would make selling lucrative and buying free at the same time.
/// </summary>
[HarmonyPatch(typeof(MoneyServiceImpl), nameof(MoneyServiceImpl.Change))]
internal static class MoneyPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref int amount)
    {
        if (amount == 0)
        {
            return;
        }

        float multiplier = amount > 0 ? Plugin.IncomeMultiplier.Value : Plugin.CostMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f))
        {
            return;
        }

        int scaled = Mathf.RoundToInt(amount * multiplier);

        // Rounding must not silently turn a sale into nothing, or a purchase into a windfall.
        // A zero multiplier is an explicit "free" and is left as zero.
        if (scaled == 0 && !Mathf.Approximately(multiplier, 0f))
        {
            scaled = amount > 0 ? 1 : -1;
        }

        amount = scaled;
    }
}
