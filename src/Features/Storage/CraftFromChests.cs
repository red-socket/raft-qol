using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;

namespace RaftQoL.Features.Storage;

// INV-1 (crafting): the crafting menu counts and uses items in closed chests near the player, after the
// inventory and any open chest. Host only for now: the host's copy of a closed chest is authoritative and its
// change reaches every peer through the vanilla close snapshot. As a client the feature does nothing.
public sealed class CraftFromChests : Feature
{
    public override string Id => "CraftFromChests";
    public override string Module => "Storage";
    public override string Name => "Craft from nearby chests";
    public override string Description => "Crafting uses items from closed chests within range, after your inventory. Works when you host (or play alone).";
    public override Authority Authority => Authority.Host;
    public override bool InVanillaPlus => true;

    private static ConfigEntry<float> radius;

    protected override void BindSettings()
    {
        radius = Bind("Range", 20f, "Chests within this distance (metres) are used.", new AcceptableValueRange<float>(5f, 100f));
    }

    private static bool Applies =>
        Raft_Network.IsHost && Game.LocalPlayer != null &&
        !GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources;

    private static readonly HashSet<BuildingUI_CostBox> craftingBoxes = new();

    // Only the selected-recipe panel of the crafting menu; build, paint and cooking costs stay vanilla.
    // Only positive results are cached: GetComponentInParent skips inactive parents.
    private static bool IsCraftingBox(BuildingUI_CostBox box)
    {
        if (craftingBoxes.Contains(box)) return true;
        if (box.GetComponentInParent<SelectedRecipeBox>() == null) return false;
        craftingBoxes.Add(box);
        return true;
    }

    // The counts drive the cost boxes, which decide whether the Craft button is enabled.
    [HarmonyPatch(typeof(BuildingUI_CostBox), nameof(BuildingUI_CostBox.SetAmountInInventory))]
    private static class CountChests
    {
        private static void Postfix(BuildingUI_CostBox __instance)
        {
            if (!Applies || !IsCraftingBox(__instance)) return;
            var chests = NearbyChests.Around(radius.Value);
            if (chests.Count == 0) return;
            int extra = 0;
            foreach (var item in __instance.items)
                if (item != null) extra += NearbyChests.Count(chests, item);
            if (extra > 0) __instance.SetAmount(__instance.amount + extra);
        }
    }

    // CraftingMenu.CraftItem is the only caller. Same order as vanilla (inventory, then the open chest), then the
    // nearby chests, nearest first. Every chest that changed is pushed to the other players.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveCostMultipleIncludeSecondaryInventories))]
    private static class DrainChests
    {
        private static bool Prefix(Inventory __instance, CostMultiple[] costMultiple)
        {
            if (!Applies || __instance != Game.LocalPlayer.Inventory) return true;

            var remaining = NearbyChests.Copy(costMultiple);
            __instance.RemoveCostMultiple(remaining, manipulateCostAmount: true);
            __instance.secondInventory?.RemoveCostMultiple(remaining, manipulateCostAmount: true);
            NearbyChests.Drain(remaining, radius.Value);
            return false;
        }
    }

    public override void Update() => NearbyChests.Track();

    // The crafting UI and the chests belong to the world scene and are rebuilt on every load.
    public override void OnWorldUnloaded()
    {
        craftingBoxes.Clear();
        NearbyChests.Forget();
    }
}
