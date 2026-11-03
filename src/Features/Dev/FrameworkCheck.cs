#if DEBUG
using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using RaftQoL.Ui;
using UnityEngine;

namespace RaftQoL.Features.Dev;

// Developer check for the framework: a patch that logs, hotkeys for both toast styles, and a HUD label.
// Off by default and outside the presets; debug builds only.
public sealed class FrameworkCheck : Feature
{
    public override string Id => "FrameworkCheck";
    public override string Module => "Dev";
    public override string Name => "Framework check";
    public override string Description => "Developer test: logs menu opens, shows test toasts (F8, Shift+F8) and a HUD status label.";
    public override bool InVanillaPlus => false;
    public override bool UsesPresets => false;

    private ConfigEntry<KeyboardShortcut> toastKey;
    private ConfigEntry<KeyboardShortcut> iconToastKey;
    private ConfigEntry<int> presetProbe;
    private HudText label;
    private int toasts;

    protected override void BindSettings()
    {
        toastKey = Bind("Toast hotkey", new KeyboardShortcut(KeyCode.F8), "Shows a text toast.");
        iconToastKey = Bind("Icon toast hotkey", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftShift), "Shows a toast with an item icon.");
        presetProbe = Bind("Preset probe", 1, 5, "Follows the preset (Vanilla+ = 1, Relaxed = 5); editing it switches the preset to Custom.",
            new AcceptableValueRange<int>(0, 10));
        label = new HudText(Config, Section, "Status label", 0.02f, 0.6f);
    }

    protected override void OnEnable() => label.Attach();

    protected override void OnDisable() => label.Detach();

    public override void OnWorldLoaded()
    {
        Plugin.Log.LogInfo($"[FrameworkCheck] world loaded: host={Game.IsHost} player='{Game.LocalPlayer?.name}'");
    }

    public override void OnWorldUnloaded()
    {
        Plugin.Log.LogInfo("[FrameworkCheck] world unloaded");
    }

    public override void Update()
    {
        if (!Game.InWorld) return;
        var p = Game.LocalPlayer.transform.position;
        label.Value = $"Raft QoL framework check | host={Game.IsHost} menu={CanvasHelper.ActiveMenu} preset={Presets.Active.Value} probe={presetProbe.Value} | {p.x:F0}, {p.y:F0}, {p.z:F0}";

        if (Game.Pressed(iconToastKey))
        {
            var plank = ItemManager.GetItemByName("Plank");
            Toast.Show($"Icon toast #{++toasts}", plank != null ? plank.settings_Inventory.Sprite : null);
        }
        else if (Game.Pressed(toastKey))
        {
            Toast.Show($"Deposited 34 items into 3 chests (test #{++toasts})");
        }
    }

    [HarmonyPatch(typeof(CanvasHelper), nameof(CanvasHelper.OpenMenuCloseOther))]
    private static class LogMenuOpen
    {
        private static void Postfix(MenuType menuType, bool __result)
        {
            Plugin.Log.LogInfo($"[FrameworkCheck] OpenMenuCloseOther({menuType}) -> {__result}");
        }
    }
}
#endif
