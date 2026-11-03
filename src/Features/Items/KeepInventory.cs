using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using UnityEngine;

namespace RaftQoL.Features.Items
{
    // INV-8: keep your inventory when you respawn after dying. Each player's inventory is their own, so this only
    // changes what happens to the local player.
    public sealed class KeepInventory : Feature
    {
        public enum KeepMode
        {
            // Nothing is lost.
            KeepAll,
            // The hotbar and equipment are kept; the rest of the inventory loses items as in vanilla.
            KeepHotbarAndEquipment,
        }

        public override string Id => "KeepInventory";
        public override string Module => "Inventory";
        public override string Name => "Keep inventory on death";
        public override string Description => "Respawning after death keeps your items instead of losing them.";
        public override bool InVanillaPlus => false;
        public override bool InRelaxed => true;

        private static ConfigEntry<KeepMode> mode;
        private static ConfigEntry<bool> inHardcore;

        protected override void BindSettings()
        {
            mode = Bind("Keep", KeepMode.KeepAll, "What survives a death: everything, or the hotbar and equipment only.");
            inHardcore = Bind("Also in Hardcore", false, "Apply in Hardcore worlds too.");
        }

        // Shared by both respawn paths: decide whether vanilla may clear, and do the partial clear ourselves.
        private static void BeforeRespawn(Player player, ref bool clearInventory, bool leaveSome)
        {
            if (!clearInventory || !player.playerNetwork.IsLocalPlayer) return;
            if (GameManager.GameMode == GameMode.Hardcore && !inHardcore.Value) return;
            if (!GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.clearInventoryOnSurrender) return;

            clearInventory = false;
            if (mode.Value == KeepMode.KeepHotbarAndEquipment)
                LoseBackpack(player.playerNetwork.Inventory, leaveSome);
        }

        // Vanilla's loss rules (PlayerInventory.Clear / ClearInventoryLeaveSome) applied to everything but the hotbar
        // and equipment. Equipment is not in allSlots, and the hotbar is its first hotslotCount slots (the game's own
        // test, Hotbar.ContainsSlot); slotType does not tell hotbar and main grid apart reliably.
        private static void LoseBackpack(PlayerInventory inventory, bool leaveSome)
        {
            for (int i = inventory.hotslotCount; i < inventory.allSlots.Count; i++)
            {
                var slot = inventory.allSlots[i];
                if (slot == null || slot.IsEmpty) continue;
                if (!leaveSome)
                {
                    slot.Reset();
                    continue;
                }
                var item = slot.itemInstance;
                if (item.baseItem.settings_Inventory.Stackable)
                {
                    int keep = Mathf.FloorToInt(item.Amount / 3f);
                    if (keep <= 0) slot.Reset();
                    else slot.SetItem(item.baseItem, keep);
                }
                else if (item.BaseItemMaxUses > 1)
                {
                    int uses = item.Uses - Mathf.FloorToInt(item.BaseItemMaxUses * 0.5f);
                    if (uses <= 0) slot.Reset();
                    else slot.SetUses(uses);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.StartRespawn))]
        private static class BedRespawn
        {
            private static void Prefix(Player __instance, ref bool clearInventory) =>
                BeforeRespawn(__instance, ref clearInventory, GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.leaveSomeItems);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RespawnWithoutBed))]
        private static class RespawnPoint
        {
            private static void Prefix(Player __instance, ref bool clearInventory) =>
                BeforeRespawn(__instance, ref clearInventory, leaveSome: false);
        }
    }
}
