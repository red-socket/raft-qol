using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaftQoL.Features.Crafting;

// CRF-3/4: holding Interact repeats the action on tanks, grills, smelters, purifiers and cooking pots: fill or empty a
// tank item by item, insert raw food or fuel, collect cooked items, take every portion from a pot. Only the
// "was Interact pressed this frame" checks in those methods are swapped for one that also fires repeatedly while the
// key is held; everything after the check is vanilla, so each repeat sends the same network message as a press.
// Repeats only happen when this player hosts; as a guest each press still does one item.
public sealed class HoldToFill : Feature
{
    public override string Id => "HoldToFill";
    public override string Module => "Crafting";
    public override string Name => "Hold to fill";
    public override string Description => "Hold Interact on tanks, grills, smelters, purifiers and cooking pots to keep adding or collecting items. Works when you host (or play alone).";
    public override Authority Authority => Authority.Host;
    public override bool InVanillaPlus => true;

    private static ConfigEntry<float> delay;
    private static ConfigEntry<float> interval;

    private static int frame = -1;
    private static bool pressed, repeat, armed;
    private static float lastFire;

    protected override void BindSettings()
    {
        delay = Bind("Hold delay", 0.4f, "Seconds to hold Interact before it starts repeating.", new AcceptableValueRange<float>(0.1f, 2f));
        interval = Bind("Repeat interval", 0.15f, "Seconds between repeats while held.", new AcceptableValueRange<float>(0.05f, 1f));
    }

    // Replaces CustomInputConfig.WasPressedThisFrame(action, key) at the patched call sites.
    public static bool PressedOrRepeated(CustomInputConfig input, InputAction action, string key)
    {
        Evaluate(input, action, key);
        return pressed || repeat;
    }

    // One evaluation per frame, so several call sites in one frame agree (as the vanilla check does).
    // Repeats need a fresh press on one of these objects first, and stop when the key is released or the player looks
    // away (a frame without any check). Only the host repeats: a client's inventory changes only when the host's echo
    // arrives, so a client repeating faster than the round trip would insert or use the same item twice.
    private static void Evaluate(CustomInputConfig input, InputAction action, string key)
    {
        if (frame == Time.frameCount) return;
        bool continuous = frame == Time.frameCount - 1;
        frame = Time.frameCount;
        float now = Time.unscaledTime;
        pressed = input.WasPressedThisFrame(action, key);
        repeat = false;
        if (pressed)
        {
            armed = true;
            lastFire = now + delay.Value - interval.Value; // first repeat after the hold delay
            return;
        }
        if (!armed || !continuous || !input.IsPressed(action, key) || !Raft_Network.IsHost)
        {
            armed = false;
            return;
        }
        if (now - lastFire < interval.Value) return;
        lastFire = now;
        repeat = true;
    }

    private static readonly MethodInfo Vanilla = AccessTools.Method(typeof(CustomInputConfig), nameof(CustomInputConfig.WasPressedThisFrame), new[] { typeof(InputAction), typeof(string) });
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(HoldToFill), nameof(PressedOrRepeated));

    internal static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        int swapped = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(Vanilla))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = Replacement;
                swapped++;
            }
            yield return instruction;
        }
        if (swapped == 0) Plugin.Log.LogWarning($"HoldToFill: no Interact check found in {original.DeclaringType?.Name}.{original.Name}");
    }

    [HarmonyPatch]
    private static class Sites
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Tank), nameof(Tank.HandleAddFuel));
            yield return AccessTools.Method(typeof(Tank), nameof(Tank.HandleTakeFuel));
            yield return AccessTools.Method(typeof(Block_CookingStand), nameof(Block_CookingStand.OnIsRayed));
            yield return AccessTools.Method(typeof(Block_CookingStand), nameof(Block_CookingStand.CheckForCollecting));
            // The purifier collects (fills the held bottle) through its own override.
            yield return AccessTools.Method(typeof(Block_CookingStand_Purifier), nameof(Block_CookingStand_Purifier.CheckForCollecting));
            yield return AccessTools.Method(typeof(CookingTable), nameof(CookingTable.HandlePickupFood));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            Swap(instructions, original);
    }

    // Taking from a tank (refiner to jerrycan, recycler and purifier outputs) goes through the tank's output button.
    // InteractableButton.HandleOnIsRayed serves every button in the game, so it is not transpiled: a fresh press
    // already ran ButtonPressed there, and this adds the repeats for tank buttons only.
    [HarmonyPatch(typeof(InteractableButton), nameof(InteractableButton.HandleOnIsRayed))]
    private static class TankButton
    {
        private static void Postfix(InteractableButton __instance)
        {
            if (!(__instance is InteractableButton_Tank)) return;
            Evaluate(SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance, PlayerInput.GetPlayerByIndex(0).actions["Interact"], __instance.keybindName);
            if (repeat) __instance.ButtonPressed();
        }
    }
}
