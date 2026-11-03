using System.Collections.Generic;
using System.Linq;
using RaftQoL.Core;
using RaftQoL.Ui;
using UnityEngine;

namespace RaftQoL.Features.Hud;

// HUD-1: a line under the crosshair with the state of what you look at: tank level, battery charge, fuel and burn
// time, cooking progress, chest fill. No patches: it uses the game's own look-at raycast (Helper.FindInteractable,
// as Pickup does) and only reads components.
public sealed class LookAtInfo : Feature
{
    public override string Id => "LookAtInfo";
    public override string Module => "HUD";
    public override string Name => "Look-at info";
    public override string Description => "Shows tank levels, battery charge, fuel, cooking time left and chest fill for what you look at.";
    public override bool InVanillaPlus => true;

    private HudText label;
    private float nextUpdate;

    protected override void BindSettings()
    {
        label = new HudText(Config, Section, "Info line", 0.5f, 0.465f, 20); // between the crosshair and the build-cost boxes the hammer shows (about 0.39-0.45)
    }

    protected override void OnEnable() => label.Attach();

    protected override void OnDisable() => label.Detach();

    public override void Update()
    {
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.1f;
        label.Value = Game.InWorld && !Game.MenuOpen ? Describe() : "";
    }

    internal static string Describe()
    {
        var target = Helper.FindInteractable(Player.UseDistance * 1.1f);
        if (target == null) return "";
        var root = target.GetComponentInParent<Block>() is Block block ? block.gameObject : target.gameObject;
        var parts = new List<string>();

        foreach (var tank in root.GetComponentsInChildren<Tank>().Where(t => t.maxCapacity > 0f))
            parts.Add($"{TankName(tank.tankFiltrationType)} {tank.CurrentTankAmount:0.#}/{tank.maxCapacity:0}");

        foreach (var battery in root.GetComponentsInChildren<Battery>().Where(b => !b.BatterySlotIsEmpty))
            parts.Add($"Battery {battery.NormalizedBatteryLeft * 100f:0}%");

        var fuel = root.GetComponentInChildren<Fuel>();
        if (fuel != null && fuel.MaxFuel > 0)
        {
            float seconds = fuel.GetBurnSecondsLeft();
            parts.Add($"Fuel {fuel.fuelCount}/{fuel.MaxFuel}" + (seconds > 0f ? $" ({Clock(seconds)} left)" : ""));
        }

        var cooking = root.GetComponentsInChildren<CookingSlot>()
            .Where(s => !s.FakeSlot && s.CurrentItem != null && s.CurrentItem.settings_cookable?.CookingResult?.item != null)
            .ToList();
        if (cooking.Count > 0)
        {
            int done = cooking.Count(s => s.IsComplete);
            var busy = cooking.Where(s => !s.IsComplete).ToList();
            parts.Add(busy.Count > 0
                ? $"Cooking: {Clock(busy.Max(s => s.CookingTimeLeft))} left" + (done > 0 ? $", {done} done" : "")
                : $"{done} done");
        }

        var storage = root.GetComponent<Storage_Small>();
        if (storage != null && !(storage.GetInventoryReference() is Inventory_Trashcan))
        {
            var slots = storage.GetInventoryReference().allSlots;
            parts.Add($"{slots.Count(s => !s.IsEmpty)}/{slots.Count} slots used" + (storage.IsOpen ? " (open)" : ""));
        }

        return string.Join("   ", parts);
    }

    private static string TankName(FuelValue_FiltrationType type) => type switch
    {
        FuelValue_FiltrationType.FV_WATERTANK => "Water",
        FuelValue_FiltrationType.FV_MOTORTANK => "Fuel",
        FuelValue_FiltrationType.FV_FUELTANK => "Fuel",
        FuelValue_FiltrationType.FV_COMPOSTHONEYTANK => "Honey",
        FuelValue_FiltrationType.FV_COMPOSTCOMPOSTTANK => "Compost",
        _ => "Tank",
    };

    private static string Clock(float seconds) => seconds >= 60f ? $"{(int)seconds / 60}:{(int)seconds % 60:00}" : $"{Mathf.CeilToInt(seconds)}s";
}
