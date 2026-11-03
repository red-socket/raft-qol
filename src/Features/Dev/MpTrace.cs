#if DEBUG
using HarmonyLib;
using RaftQoL.Core;
using UnityEngine;

namespace RaftQoL.Features.Dev;

// Developer check for multiplayer tests: logs every storage open/close message this peer receives (with who sent it
// and the chest's state), every local open/close, and every chest snapshot raft-qol pushes. Off by default and
// outside the presets; debug builds only.
public sealed class MpTrace : Feature
{
    public override string Id => "MpTrace";
    public override string Module => "Dev";
    public override string Name => "Multiplayer trace";
    public override string Description => "Developer test: logs chest open/close messages and raft-qol's chest pushes.";
    public override bool InVanillaPlus => false;
    public override bool UsesPresets => false;

    internal static void Log(string text) => Plugin.Log.LogInfo($"[mp {Time.unscaledTime:F2}] {text}");

    private static string Describe(Storage_Small chest)
    {
        if (chest == null) return "chest ?";
        int items = 0;
        foreach (var slot in chest.GetInventoryReference().allSlots)
            if (!slot.IsEmpty) items += slot.itemInstance.Amount;
        return $"chest #{chest.ObjectIndex} open={chest.IsOpen} items={items}";
    }

    [HarmonyPatch(typeof(StorageManager), nameof(StorageManager.Deserialize))]
    private static class Received
    {
        private static void Prefix(StorageManager __instance, Message_NetworkBehaviour msg, Network_UserId remoteID)
        {
            if (msg.Type != Messages.StorageManager_Open && msg.Type != Messages.StorageManager_Close) return;
            var storage = msg as Message_Storage;
            var chest = storage == null ? null : __instance.GetStorageByObjectIndex(storage.storageObjectIndex);
            int snapshot = msg is Message_Storage_Close close && close.slots != null ? close.slots.Length : 0;
            Log($"recv {msg.Type} from {remoteID} via {__instance.playerNetwork?.name} → {Describe(chest)}" +
                (msg.Type == Messages.StorageManager_Close ? $" snapshotSlots={snapshot}" : ""));
        }

        private static void Postfix(Message_NetworkBehaviour msg, bool __result)
        {
            if (msg.Type == Messages.StorageManager_Open || msg.Type == Messages.StorageManager_Close)
                Log($"  → handled, relay={__result}");
        }
    }

    [HarmonyPatch(typeof(Storage_Small), nameof(Storage_Small.Open))]
    private static class Opened
    {
        private static void Postfix(Storage_Small __instance, Network_Player player) =>
            Log($"Open {Describe(__instance)} by {player?.name ?? "(late join)"}");
    }

    [HarmonyPatch(typeof(Storage_Small), nameof(Storage_Small.Close))]
    private static class Closed
    {
        private static void Postfix(Storage_Small __instance, Network_Player player) =>
            Log($"Close {Describe(__instance)} by {player?.name}");
    }

    [HarmonyPatch(typeof(Features.Storage.NearbyChests), nameof(Features.Storage.NearbyChests.Push))]
    private static class Pushed
    {
        private static void Postfix(Storage_Small chest) => Log($"raft-qol pushed {Describe(chest)}");
    }
}
#endif
