using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RaftQoL.Core;
using RaftQoL.Ui;
using UnityEngine;

namespace RaftQoL.Features.Storage
{
    // INV-2: one key moves items from the player's inventory into every nearby chest that already holds that item.
    // Host only for now, like CraftFromChests: the host changes closed chests and pushes the vanilla close snapshot.
    public sealed class QuickDeposit : Feature
    {
        public override string Id => "QuickDeposit";
        public override string Module => "Storage";
        public override string Name => "Quick deposit";
        public override string Description => "A key moves your items into nearby chests that already hold the same item. Works when you host (or play alone).";
        public override Authority Authority => Authority.Host;
        public override bool InVanillaPlus => true;

        private ConfigEntry<KeyboardShortcut> key;
        private ConfigEntry<float> radius;
        private ConfigEntry<bool> includeHotbar;
        private ConfigEntry<bool> showToast;

        protected override void BindSettings()
        {
            key = Bind("Key", new KeyboardShortcut(KeyCode.G), "Deposits into nearby chests.");
            radius = Bind("Range", 20f, "Chests within this distance (metres) are used.", new AcceptableValueRange<float>(5f, 100f));
            includeHotbar = Bind("Include hotbar", false, "Also deposit items from the hotbar.");
            showToast = Bind("Show summary", true, "Show how many items went into how many chests.");
        }

        public override void Update()
        {
            NearbyChests.Track();
            if (!Game.Pressed(key)) return;
            if (!Raft_Network.IsHost)
            {
                Toast.Show("Quick deposit works only when you host the world");
                return;
            }
            Deposit();
        }

        public override void OnWorldUnloaded() => NearbyChests.Forget();

        internal void Deposit()
        {
            // Deposited hotbar stacks must not be refilled from the backpack (SmartInventory).
            Items.SmartInventory.Suppressed++;
            try { DepositNow(); }
            finally { Items.SmartInventory.Suppressed--; }
        }

        private void DepositNow()
        {
            var inventory = Game.LocalPlayer.Inventory;
            // Everything after the hotbar (the first hotslotCount slots, as in Hotbar.ContainsSlot), plus the hotbar if
            // asked. Equipment is not in allSlots.
            var sources = inventory.allSlots.Skip(includeHotbar.Value ? 0 : inventory.hotslotCount).ToList();
            int moved = 0, chests = 0;
            foreach (var chest in NearbyChests.Around(radius.Value))
            {
                var chestInventory = chest.GetInventoryReference();
                var held = new HashSet<int>(chestInventory.allSlots.Where(s => !s.IsEmpty && Movable(s)).Select(s => s.itemInstance.UniqueIndex));
                if (held.Count == 0) continue;
                int before = moved;
                foreach (var slot in sources)
                {
                    if (slot.IsEmpty || !Movable(slot) || !held.Contains(slot.itemInstance.UniqueIndex)) continue;
                    int amount = slot.itemInstance.Amount;
                    int left = chestInventory.AddItem(slot.itemInstance.UniqueName, amount);
                    if (amount - left <= 0) continue;
                    slot.RemoveItem(amount - left);
                    moved += amount - left;
                }
                if (moved == before) continue;
                chests++;
                NearbyChests.Push(chest);
            }
            NearbyChests.InvalidateCache();
            if (moved > 0) inventory.hotbar.ReselectCurrentSlot();
            if (showToast.Value)
                Toast.Show(moved > 0 ? $"Deposited {moved} items into {chests} {(chests == 1 ? "chest" : "chests")}" : "Nothing to deposit nearby");
        }

        // Plain stackable resources only: never tools, weapons, equipment or anything with durability.
        private static bool Movable(Slot slot)
        {
            var item = slot.itemInstance;
            return item.settings_Inventory.Stackable && item.BaseItemMaxUses <= 1 && slot.slotType != SlotType.Equipment;
        }
    }
}
