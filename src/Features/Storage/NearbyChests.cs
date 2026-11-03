using System.Collections.Generic;
using RaftQoL.Core;
using Steamworks;
using UnityEngine;

namespace RaftQoL.Features.Storage
{
    // Chests the local player may draw from or deposit into, and the vanilla way to sync a changed chest.
    internal static class NearbyChests
    {
        private static readonly List<Storage_Small> cache = new List<Storage_Small>();
        private static int cachedFrame = -1;
        private static float cachedRadius;

        // A client that walks away from an open chest sends a Close every frame until the host's echo arrives, and the
        // host applies each one (StorageManager.cs:73-84, :190). A chest changed between two of those would get the
        // client's old contents back, so a chest that closed in the last few seconds is left alone.
        private const float SettleSeconds = 3f;
        private static readonly HashSet<Storage_Small> wasOpen = new HashSet<Storage_Small>();
        private static readonly Dictionary<Storage_Small, float> closedAt = new Dictionary<Storage_Small, float>();

        // Call every frame while a feature that uses chests is enabled.
        public static void Track()
        {
            foreach (var chest in StorageManager.allStorages)
            {
                if (chest == null) continue;
                // Only a remote opener (or an unknown one: late-join Open(null)) can send duplicate Closes.
                if (chest.IsOpen && (chest.currentPlayer == null || !chest.currentPlayer.IsLocalPlayer)) wasOpen.Add(chest);
                else if (!chest.IsOpen && wasOpen.Remove(chest)) closedAt[chest] = Time.unscaledTime;
            }
        }

        public static void Forget()
        {
            wasOpen.Clear();
            closedAt.Clear();
            cachedFrame = -1;
        }

        private static bool Settling(Storage_Small chest) =>
            closedAt.TryGetValue(chest, out float t) && Time.unscaledTime - t < SettleSeconds;

        // Closed, settled, non-trash storages within the radius of the local player, nearest first. Cached per frame.
        // An open chest is skipped: its opener's copy is the live one and ours is stale.
        public static List<Storage_Small> Around(float radius)
        {
            if (cachedFrame == Time.frameCount && cachedRadius == radius) return cache;
            cachedFrame = Time.frameCount;
            cachedRadius = radius;
            cache.Clear();
            var player = Game.LocalPlayer;
            if (player == null) return cache;
            var origin = player.transform.position;
            float maxSqr = radius * radius;
            foreach (var chest in StorageManager.allStorages)
            {
                if (chest == null || chest.IsOpen || Settling(chest)) continue;
                var inventory = chest.GetInventoryReference();
                if (inventory == null || inventory is Inventory_Trashcan) continue;
                if ((chest.transform.position - origin).sqrMagnitude > maxSqr) continue;
                cache.Add(chest);
            }
            cache.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));
            return cache;
        }

        // True when the inventory and the chests together cover every cost.
        public static bool Covers(Inventory inventory, CostMultiple[] costs, float radius)
        {
            var chests = Around(radius);
            foreach (var cost in costs)
            {
                int have = 0;
                foreach (var item in cost.items)
                    if (item != null) have += inventory.GetItemCount(item.UniqueName) + Count(chests, item);
                if (have < cost.amount) return false;
            }
            return true;
        }

        // Takes what is still owed (amounts are reduced in place) from the chests, nearest first, and pushes every chest
        // that changed.
        public static void Drain(CostMultiple[] remaining, float radius)
        {
            foreach (var chest in Around(radius))
            {
                if (System.Array.TrueForAll(remaining, c => c.amount <= 0)) break;
                var inventory = chest.GetInventoryReference();
                var owed = System.Array.FindAll(remaining, c => c.amount > 0 && System.Array.Exists(c.items, i => i != null && inventory.GetItemCount(i.UniqueName) > 0));
                if (owed.Length == 0) continue;
                inventory.RemoveCostMultiple(owed, manipulateCostAmount: true);
                Push(chest);
            }
            InvalidateCache();
        }

        public static CostMultiple[] Copy(CostMultiple[] costs) => System.Array.ConvertAll(costs, c => new CostMultiple(c.items, c.amount));

        public static int Count(List<Storage_Small> chests, Item_Base item)
        {
            int total = 0;
            foreach (var chest in chests)
                total += chest.GetInventoryReference().GetItemCount(item.UniqueName);
            return total;
        }

        // Host only: a closed chest's host copy is authoritative, so after changing it locally the host pushes the
        // vanilla close snapshot. Clients apply it (SetSlotsFromRGD) and their CloseStorage returns false (not open).
        public static void Push(Storage_Small chest)
        {
            var player = Game.LocalPlayer;
            if (player == null || !Raft_Network.IsHost) return;
            var message = new Message_Storage_Close(Messages.StorageManager_Close, player.StorageManager, chest);
            player.Network.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }

        public static void InvalidateCache() => cachedFrame = -1;
    }
}
