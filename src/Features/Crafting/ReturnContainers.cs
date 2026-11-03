using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;

namespace RaftQoL.Features.Crafting;

// CRF-5: eating a bowl or plate dish gives the clay bowl back, drinking a juice gives the glass back. Vanilla only
// returns cups, bottles, canteens and the milk bucket (ItemInstance_Consumeable.ItemAfterUse). The item data is
// left alone: Slot.IncrementUses swaps the whole remaining stack for ItemAfterUse, which would eat stacked dishes.
public sealed class ReturnContainers : Feature
{
    public override string Id => "ReturnContainers";
    public override string Module => "Crafting";
    public override string Name => "Return containers";
    public override string Description => "Eating from a clay bowl or plate gives the bowl back; drinking a juice gives the glass back.";
    public override bool InVanillaPlus => true;

    private static ConfigEntry<bool> bowls;
    private static ConfigEntry<bool> glasses;

    protected override void BindSettings()
    {
        bowls = Bind("Bowls and plates", true, "Return a clay bowl after eating a bowl or plate dish (cooking pot dishes are served in the clay bowl).");
        glasses = Bind("Drinking glasses", true, "Return a drinking glass after a juice.");
    }

    private static string ContainerFor(Item_Base item)
    {
        if (item.settings_consumeable.ItemAfterUse?.item != null) return null; // vanilla already returns something
        string name = item.UniqueName;
        if (bowls.Value && (name.StartsWith("Claybowl_") || name.StartsWith("ClayPlate_")) && name != "Claybowl_Empty") return "Claybowl_Empty";
        if (glasses.Value && name.StartsWith("DrinkingGlass_")) return "DrinkingGlass";
        return null;
    }

    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.Consume))]
    private static class GiveBack
    {
        // Consume runs on every peer (for the sound); only the eater's own machine gets the container.
        private static void Postfix(PlayerStats __instance, Item_Base edibleItem)
        {
            if (edibleItem == null || __instance.playerNetwork == null || !__instance.playerNetwork.IsLocalPlayer) return;
            string container = ContainerFor(edibleItem);
            if (container != null) __instance.playerNetwork.Inventory.AddItem(container, 1);
        }
    }
}
