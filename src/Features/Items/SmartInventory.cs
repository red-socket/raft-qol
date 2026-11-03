using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using RaftQoL.Features.Storage;

namespace RaftQoL.Features.Items
{
    // INV-6: picked-up materials go into the backpack before the hotbar, and the item in your hand is replaced when its
    // stack runs out or the tool breaks: from the rest of the inventory, and (as host) from a nearby chest, which also
    // keeps fuel coming while you refuel an engine or a grill. Player inventories are local; chest refills use the same
    // host-side push as CraftFromChests.
    public sealed class SmartInventory : Feature
    {
        public override string Id => "SmartInventory";
        public override string Module => "Inventory";
        public override string Name => "Smarter inventory";
        public override string Description => "Materials you pick up go to the backpack before the hotbar, and the item in your hand is refilled when it runs out or breaks.";
        public override bool InVanillaPlus => false;
        public override bool InRelaxed => true;

        private static ConfigEntry<bool> backpackFirst;
        private static ConfigEntry<bool> refill;
        private static ConfigEntry<bool> refillFromChests;
        private static ConfigEntry<float> radius;

        // Inventory.RemoveItem(string, int) and RemoveItemUses walk allSlots from the hotbar onwards; refilling an
        // already-visited slot mid-walk would charge less than asked. Refills wait until the outermost removal is done.
        private static int removing;
        private static Slot pendingSlot;
        private static Item_Base pendingItem;

        // Set by features that empty hotbar slots on purpose (QuickDeposit), so the slot is not refilled behind them.
        internal static int Suppressed;

        protected override void BindSettings()
        {
            backpackFirst = Bind("Backpack first", true, "Materials go into the backpack before empty hotbar slots. Tools, weapons, food and buildables still go to the hotbar.");
            refill = Bind("Refill hand", true, "When the item in your hand runs out or breaks, move the same item from your inventory into that slot.");
            refillFromChests = Bind("Refill from chests", true, "If your inventory has none left, take it from a nearby chest instead. Works when you host (or play alone).");
            radius = Bind("Chest range", 20f, "Chests within this distance (metres) are used for refills.", new AcceptableValueRange<float>(5f, 100f));
        }

        public override void Update() => NearbyChests.Track();

        public override void OnWorldUnloaded() => NearbyChests.Forget();

        // Plain materials only; anything you would hold and use stays eligible for the hotbar.
        private static bool IsMaterial(Item_Base item)
        {
            if (!item.settings_Inventory.Stackable || item.MaxUses > 1) return false;
            var food = item.settings_consumeable;
            if (food != null && (food.HungerYield != 0f || food.ThirstYield != 0f)) return false;
            if (item.settings_buildable != null && item.settings_buildable.Placeable) return false;
            if (item.settings_usable != null && item.settings_usable.IsUsable()) return false;
            return true;
        }

        [HarmonyPatch(typeof(global::Inventory), nameof(global::Inventory.FindSuitableSlot), typeof(Item_Base))]
        private static class BackpackFirst
        {
            // Vanilla returns a non-full stack of the same item if there is one, else the first empty slot, and the
            // hotbar comes first in allSlots. Only that last case is redirected.
            private static void Postfix(global::Inventory __instance, Item_Base stackableItem, ref Slot __result)
            {
                if (!backpackFirst.Value || stackableItem == null || __result == null || !__result.IsEmpty) return;
                if (!(__instance is PlayerInventory player) || player != Game.LocalPlayer?.Inventory) return;
                int index = player.allSlots.IndexOf(__result);
                if (index < 0 || index >= player.hotslotCount || !IsMaterial(stackableItem)) return;
                for (int i = player.hotslotCount; i < player.allSlots.Count; i++)
                {
                    var slot = player.allSlots[i];
                    if (slot.IsEmpty && slot.active)
                    {
                        __result = slot;
                        return;
                    }
                }
            }
        }

        // The selected hotbar slot emptied by use: eating, a tool breaking, placing or inserting the last item.
        // Menus (dragging, crafting) are left alone; dropping with Q empties the slot through SetItem and never gets here.
        private static void AfterUse(Slot slot, Item_Base used)
        {
            if (!refill.Value || used == null || !slot.IsEmpty || Game.MenuOpen || Suppressed > 0) return;
            var player = Game.LocalPlayer;
            if (player == null || slot.inventory != player.Inventory || !player.Inventory.hotbar.IsSelectedHotSlot(slot)) return;
            if (removing > 0)
            {
                pendingSlot = slot;
                pendingItem = used;
                return;
            }

            var inventory = player.Inventory;
            for (int i = inventory.hotslotCount; i < inventory.allSlots.Count; i++)
            {
                var source = inventory.allSlots[i];
                if (source.IsEmpty || source.itemInstance.UniqueIndex != used.UniqueIndex) continue;
                Move(source, slot);
                inventory.hotbar.ReselectCurrentSlot();
                return;
            }

            if (!refillFromChests.Value || !Raft_Network.IsHost) return;
            foreach (var chest in NearbyChests.Around(radius.Value))
            {
                foreach (var source in chest.GetInventoryReference().allSlots)
                {
                    if (source.IsEmpty || source.itemInstance.UniqueIndex != used.UniqueIndex) continue;
                    Move(source, slot);
                    NearbyChests.Push(chest);
                    NearbyChests.InvalidateCache();
                    inventory.hotbar.ReselectCurrentSlot();
                    return;
                }
            }
        }

        private static void Move(Slot from, Slot to)
        {
            to.SetItem(from.itemInstance); // SetItem clones the instance (uses and amount kept)
            from.Reset();
        }

        [HarmonyPatch(typeof(Slot), nameof(Slot.RemoveItem))]
        private static class Removed
        {
            private static void Prefix(Slot __instance, out Item_Base __state) => __state = __instance.IsEmpty ? null : __instance.itemInstance.baseItem;
            private static void Postfix(Slot __instance, Item_Base __state) => AfterUse(__instance, __state);
        }

        [HarmonyPatch(typeof(Slot), nameof(Slot.IncrementUses))]
        private static class UsedUp
        {
            private static void Prefix(Slot __instance, out Item_Base __state) => __state = __instance.IsEmpty ? null : __instance.itemInstance.baseItem;
            private static void Postfix(Slot __instance, Item_Base __state) => AfterUse(__instance, __state);
        }

        [HarmonyPatch]
        private static class MultiSlotRemoval
        {
            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(global::Inventory), nameof(global::Inventory.RemoveItem), new[] { typeof(string), typeof(int) });
                yield return AccessTools.Method(typeof(global::Inventory), nameof(global::Inventory.RemoveItemUses));
            }

            private static void Prefix() => removing++;

            private static System.Exception Finalizer(System.Exception __exception)
            {
                if (--removing == 0 && pendingSlot != null)
                {
                    var slot = pendingSlot;
                    var item = pendingItem;
                    pendingSlot = null;
                    pendingItem = null;
                    AfterUse(slot, item);
                }
                return __exception;
            }
        }
    }
}
