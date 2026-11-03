using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using UnityEngine;

namespace RaftQoL.Features.Crafting;

// CRF-2: removing a foundation, floor, wall or other non-placeable block gives back a configurable share of its cost
// (vanilla: half, RemovePlaceables.cs:370-381), plus optionally the foundation armor. Placeables already return
// themselves in full and stay vanilla. The refund goes into the remover's own inventory.
public sealed class FullRefund : Feature
{
    public override string Id => "FullRefund";
    public override string Module => "Crafting";
    public override string Name => "Full refund on removal";
    public override string Description => "Removing a built block gives back all of its materials instead of half.";
    public override bool InVanillaPlus => false;
    public override bool InRelaxed => true;

    private const int FoundationArmorIndex = 96;

    private static ConfigEntry<int> percent;
    private static ConfigEntry<bool> armor;

    protected override void BindSettings()
    {
        percent = Bind("Refund percent", 100, "Share of the build cost given back (vanilla: 50).", new AcceptableValueRange<int>(0, 100));
        armor = Bind("Refund foundation armor", true, "Also give back the armor of a reinforced foundation.");
    }

    [HarmonyPatch(typeof(RemovePlaceables), nameof(RemovePlaceables.ReturnItemsFromBlock))]
    private static class Refund
    {
        private static bool Prefix(Block block, Network_Player player, bool giveItems, ref bool __result)
        {
            // Placeables (itemToReturnOnDestroy set) keep their own handling; creative mode refunds nothing.
            if (block.itemToReturnOnDestroy != null || GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources)
                return true;
            __result = true;
            if (!giveItems) return false;

            Give(player, block.buildableItem.settings_recipe.NewCost);
            if (armor.Value && block.Reinforced)
                Give(player, ItemManager.GetItemByIndex(FoundationArmorIndex)?.settings_recipe.NewCost);
            return false;
        }

        // Like vanilla: the first item of each cost, rounded up.
        private static void Give(Network_Player player, CostMultiple[] costs)
        {
            if (costs == null) return;
            foreach (var cost in costs)
            {
                int amount = Mathf.CeilToInt(cost.amount * percent.Value / 100f);
                if (amount > 0 && cost.items.Length > 0 && cost.items[0] != null)
                    player.Inventory.AddItem(cost.items[0].UniqueName, amount);
            }
        }
    }
}
