using System;
using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;

namespace RaftQoL.Features.Storage;

// INV-1 (building): building, upgrading, repairing and reinforcing use items from closed chests near the player,
// after the inventory. Host only for now, like CraftFromChests.
public sealed class BuildFromChests : Feature
{
    public override string Id => "BuildFromChests";
    public override string Module => "Storage";
    public override string Name => "Build from nearby chests";
    public override string Description => "Building, upgrading, repairing and reinforcing use items from closed chests within range, after your inventory. Works when you host (or play alone).";
    public override Authority Authority => Authority.Host;
    public override bool InVanillaPlus => true;

    private static ConfigEntry<float> radius;

    // Set while one of the build drains runs (BlockCreator.CreateBlock, Hammer repair and reinforce), so only their
    // RemoveCostMultiple call is extended.
    private static int building;
    private static bool cheat;

    protected override void BindSettings()
    {
        radius = Bind("Range", 20f, "Chests within this distance (metres) are used.", new AcceptableValueRange<float>(5f, 100f));
    }

    private static bool Applies =>
        Raft_Network.IsHost && Game.LocalPlayer != null &&
        !GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.unlimitedResources;

    public override void Update() => NearbyChests.Track();

    public override void OnWorldUnloaded() => NearbyChests.Forget();

    protected override void OnDisable()
    {
        building = 0;
        cheat = false;
    }

    // Every build check (place, upgrade, repair, reinforce) goes through this overload.
    [HarmonyPatch(typeof(BlockCreator), nameof(BlockCreator.HasEnoughResourcesToBuild), typeof(CostMultiple[]))]
    private static class CheckChests
    {
        private static void Postfix(BlockCreator __instance, CostMultiple[] buildCost, ref bool __result)
        {
            if (__result || buildCost == null || !Applies || __instance.playerNetwork == null || !__instance.playerNetwork.IsLocalPlayer) return;
            __result = NearbyChests.Covers(__instance.playerInventory, buildCost, radius.Value);
        }
    }

    // The cost boxes of the build menu (cursor and panel), so the counts match what can be built.
    [HarmonyPatch(typeof(BuildingUI_CostBox), nameof(BuildingUI_CostBox.SetAmountInInventory))]
    private static class CountChests
    {
        private static void Postfix(BuildingUI_CostBox __instance)
        {
            if (!Applies || !IsBuildBox(__instance)) return;
            var chests = NearbyChests.Around(radius.Value);
            if (chests.Count == 0) return;
            int extra = 0;
            foreach (var item in __instance.items)
                if (item != null) extra += NearbyChests.Count(chests, item);
            if (extra > 0) __instance.SetAmount(__instance.amount + extra);
        }

        private static bool IsBuildBox(BuildingUI_CostBox box)
        {
            var menu = ComponentManager<BuildMenu>.Value;
            if (menu == null) return false;
            var collection = box.GetComponentInParent<CostCollection>();
            return collection != null && (collection == menu.costColletionCursor || collection == menu.costColletionPanel);
        }
    }

    [HarmonyPatch(typeof(BlockCreator), nameof(BlockCreator.CreateBlock))]
    private static class PlaceContext
    {
        private static void Prefix() => building++;
        private static Exception Finalizer(Exception __exception) { building--; return __exception; }
    }

    // The developer cheat (Cheat.cs:1666) places through CreateBlockCheat without a resource check; leave it vanilla.
    [HarmonyPatch(typeof(BlockCreator), nameof(BlockCreator.CreateBlockCheat))]
    private static class CheatContext
    {
        private static void Prefix() => cheat = true;
        private static Exception Finalizer(Exception __exception) { cheat = false; return __exception; }
    }

    [HarmonyPatch(typeof(Hammer), nameof(Hammer.HandleRepairingOfBlock))]
    private static class RepairContext
    {
        private static void Prefix() => building++;
        private static Exception Finalizer(Exception __exception) { building--; return __exception; }
    }

    [HarmonyPatch(typeof(Hammer), nameof(Hammer.ReinforceBlock))]
    private static class ReinforceContext
    {
        private static void Prefix() => building++;
        private static Exception Finalizer(Exception __exception) { building--; return __exception; }
    }

    // Inside a build drain: take from the inventory first, then the chests.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveCostMultiple))]
    private static class Drain
    {
        private static bool Prefix(Inventory __instance, CostMultiple[] costMultiple, bool manipulateCostAmount)
        {
            if (building == 0 || cheat || manipulateCostAmount || !Applies || __instance != Game.LocalPlayer.Inventory) return true;
            var remaining = NearbyChests.Copy(costMultiple);
            __instance.RemoveCostMultiple(remaining, manipulateCostAmount: true); // manipulateCostAmount: not re-entered
            NearbyChests.Drain(remaining, radius.Value);
            return false;
        }
    }
}
