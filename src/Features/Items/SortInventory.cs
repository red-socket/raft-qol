using System.Collections.Generic;
using System.Linq;
using RaftQoL.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RaftQoL.Features.Items
{
    // Part of INV-5: a sort button on the player inventory, a copy of the one chests have (Inventory.sortButton, which the
    // player inventory leaves empty). It merges stacks the way Inventory.Sort does for chests, then orders items by
    // item. Only the main and backpack slots move: Inventory.Sort itself would Clear and re-add the whole inventory,
    // hotbar included. Player inventories are local and saved by each player, so nothing is sent.
    public sealed class SortInventory : Feature
    {
        public override string Id => "SortInventory";
        public override string Module => "Inventory";
        public override string Name => "Inventory sort button";
        public override string Description => "A sort button on your inventory, like the one on chests. It stacks and orders the items in your inventory and backpack; the hotbar stays as it is.";
        public override bool InVanillaPlus => true;

        private const string ButtonName = "RaftQoL Sort button";

        private static GameObject button;
        private static float nextTry;

        public override void Update()
        {
            if (!Game.InWorld) return;
            if (button == null && Time.unscaledTime >= nextTry)
            {
                nextTry = Time.unscaledTime + 1f;
                button = CreateButton();
            }
            // As Inventory.Update does for the chest buttons: mouse and keyboard only.
            if (button != null) button.SetActive(!SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance.Gamepad);
        }

        public override void OnWorldUnloaded() => DestroyButton();

        protected override void OnDisable() => DestroyButton();

        private static void DestroyButton()
        {
            if (button != null) Object.Destroy(button);
            button = null;
        }

        // The chest prefabs (Storage_Small.inventoryPrefab) are loaded with the items, so their sort button can be copied
        // before any chest is opened. Returns null, and is tried again a second later, if none is found.
        private static GameObject CreateButton()
        {
            var inventory = ComponentManager<PlayerInventory>.Value;
            var header = inventory != null ? inventory.transform.Find("DescriptionBox") : null;
            if (header == null) return null;
            var template = Resources.FindObjectsOfTypeAll<global::Inventory>()
                .Where(i => i.GetType() == typeof(global::Inventory) && i.sortButton != null)
                .Select(i => i.sortButton)
                .FirstOrDefault();
            if (template == null) return null;

            var copy = Object.Instantiate(template, header, false);
            copy.name = ButtonName;
            var rect = (RectTransform)copy.transform;
            // The right end of the header row, level with the "Inventory" title, where chests have theirs.
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-33f, -29f);
            // The prefab's click calls Sort on the prefab's own chest inventory; replace it.
            var click = copy.GetComponent<Button>();
            click.onClick = new Button.ButtonClickedEvent();
            click.onClick.AddListener(() => Sort(ComponentManager<PlayerInventory>.Value));
            return copy;
        }

        // Stacks and orders the main and backpack slots. Items keep their amount, durability and exclusive string (pet
        // names): only unused stackable items are merged, as in Inventory.Sort, and nothing is added through AddItem.
        internal static void Sort(PlayerInventory inventory)
        {
            if (inventory == null) return;
            // An item on the cursor is still in its slot until it is dropped.
            if (global::Inventory.fromSlot != null && !global::Inventory.fromSlot.IsEmpty) return;
            var slots = inventory.allSlots
                .Where(s => s != null && (s.slotType == SlotType.Normal || s.slotType == SlotType.Backpack) && s.active && !s.locked)
                .ToList();

            var merged = new Dictionary<int, int>();
            var kept = new List<ItemInstance>();
            foreach (var slot in slots)
            {
                if (!slot.HasValidItemInstance()) continue;
                var item = slot.itemInstance;
                bool mergeable = item.HasMaxUses && item.Amount <= item.baseItem.settings_Inventory.StackSize && string.IsNullOrEmpty(item.exclusiveString);
                if (!mergeable)
                    kept.Add(item.Clone());
                else
                {
                    merged.TryGetValue(item.UniqueIndex, out int amount);
                    merged[item.UniqueIndex] = amount + item.Amount;
                }
            }

            var items = new List<ItemInstance>(kept);
            foreach (var pair in merged)
            {
                var item = ItemManager.GetItemByIndex(pair.Key);
                int stack = Mathf.Max(1, item.settings_Inventory.StackSize);
                for (int left = pair.Value; left > 0; left -= stack)
                    items.Add(new ItemInstance(item, Mathf.Min(stack, left), item.MaxUses));
            }
            // By item, then fuller stacks and less worn tools first.
            items = items.OrderBy(i => i.UniqueIndex).ThenByDescending(i => i.Amount).ThenByDescending(i => i.Uses).ToList();
            if (items.Count > slots.Count) return;

            for (int i = 0; i < slots.Count; i++)
            {
                if (i < items.Count) slots[i].SetItem(items[i]);
                else slots[i].Reset();
            }
        }
    }
}
