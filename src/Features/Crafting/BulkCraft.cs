using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using UnityEngine;

namespace RaftQoL.Features.Crafting;

// CRF-1: Shift-click Craft makes several at once, Ctrl-click makes as many as the resources allow.
// Each extra craft is a normal CraftItem call, gated by the same cost check as the Craft button (so it also counts
// nearby chests when CraftFromChests is on).
public sealed class BulkCraft : Feature
{
    public override string Id => "BulkCraft";
    public override string Module => "Crafting";
    public override string Name => "Bulk craft";
    public override string Description => "Shift-click Craft to make several at once; Ctrl-click to make as many as you can.";
    public override bool InVanillaPlus => true;

    private static ConfigEntry<int> shiftCount;
    private static ConfigEntry<int> maxCount;
    private static bool crafting;

    protected override void BindSettings()
    {
        shiftCount = Bind("Shift-click amount", 5, "How many crafts a Shift-click makes.", new AcceptableValueRange<int>(2, 20));
        maxCount = Bind("Ctrl-click limit", 50, "Most crafts a Ctrl-click makes.", new AcceptableValueRange<int>(2, 200));
    }

    internal static int Extra()
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return maxCount.Value - 1;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return shiftCount.Value - 1;
        return 0;
    }

    internal static int Repeat(CraftingMenu menu, int extra)
    {
        int made = 0;
        crafting = true;
        try
        {
            var box = menu.selectedRecipeBox;
            for (; made < extra; made++)
            {
                var item = box.ItemToCraft;
                if (item == null) break;
                box.costCollection.ShowCost(item.settings_recipe.NewCost);
                if (!box.costCollection.MeetsRequirements()) break;
                menu.CraftItem();
            }
        }
        finally
        {
            crafting = false;
        }
        return made;
    }

    [HarmonyPatch(typeof(CraftingMenu), nameof(CraftingMenu.CraftItem))]
    private static class Repeated
    {
        private static void Postfix(CraftingMenu __instance)
        {
            if (crafting) return;
            int extra = Extra();
            if (extra > 0) Repeat(__instance, extra);
        }
    }
}
