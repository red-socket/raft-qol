using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;

namespace RaftQoL.Features.Hud
{
    // HUD-2: the inventory's item description gets a line of numbers: hunger and thirst from food and drink,
    // maximum durability, and what an item cooks into.
    public sealed class ItemTooltips : Feature
    {
        public override string Id => "ItemTooltips";
        public override string Module => "HUD";
        public override string Name => "Item details";
        public override string Description => "Item descriptions show food and water values, durability and what an item cooks into.";
        public override bool InVanillaPlus => true;

        private static ConfigEntry<bool> food;
        private static ConfigEntry<bool> durability;
        private static ConfigEntry<bool> cooking;

        protected override void BindSettings()
        {
            food = Bind("Food values", true, "Show hunger and thirst restored.");
            durability = Bind("Durability", true, "Show maximum durability of tools and equipment.");
            cooking = Bind("Cooking result", true, "Show what an item cooks into and how long it takes.");
        }

        internal static string Details(Item_Base item)
        {
            var parts = new List<string>();
            var eat = item.settings_consumeable;
            if (food.Value && eat != null && (eat.HungerYield != 0f || eat.ThirstYield != 0f))
            {
                if (eat.HungerYield != 0f) parts.Add($"Food {Number(eat.HungerYield)}{Bonus(eat.BonusHungerYield)}");
                if (eat.ThirstYield != 0f) parts.Add($"Water {Number(eat.ThirstYield)}{Bonus(eat.BonusThirstYield)}");
                if (eat.IsRaw) parts.Add("raw");
            }
            if (durability.Value && item.MaxUses > 1 && !item.settings_Inventory.Stackable)
                parts.Add($"Durability {item.MaxUses}");
            var cook = item.settings_cookable;
            if (cooking.Value && cook != null && cook.CookingResult?.item != null)
                parts.Add($"Cooks into {cook.CookingResult.item.settings_Inventory.DisplayName} ({cook.CookingTime:0}s)");
            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
        }

        private static string Number(float value) => value.ToString(value % 1f == 0f ? "0" : "0.#");
        private static string Bonus(float value) => value > 0f ? $" (+{Number(value)})" : "";

        [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.SetItemDescription))]
        private static class AppendDetails
        {
            private static void Postfix(PlayerInventory __instance, Item_Base item)
            {
                if (item == null) return;
                string details = Details(item);
                if (details != null) __instance.itemDescriptionText.text += "\n\n" + details;
            }
        }
    }
}
