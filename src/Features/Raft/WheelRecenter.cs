using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using RaftQoL.Ui;
using Steamworks;
using UnityEngine;

namespace RaftQoL.Features.Raft;

// NAV-3: a released steering wheel eases back to straight, unless the lock key has locked it. The wheel's target
// angle is host-authoritative: the host applies every turn (its own and clients' requests, SteeringWheel.Rotate) and
// sends clients the absolute angle, so recentering runs on the host and uses that same message.
public sealed class WheelRecenter : Feature
{
    public override string Id => "WheelRecenter";
    public override string Module => "Raft";
    public override string Name => "Steering wheel recenter";
    public override string Description => "A released steering wheel returns to straight. The lock key keeps it where it is. Works when you host (or play alone).";
    public override Authority Authority => Authority.Host;
    public override bool InVanillaPlus => true;

    private ConfigEntry<float> delay;
    private ConfigEntry<float> speed;
    private ConfigEntry<KeyboardShortcut> lockKey;
    private bool locked;

    private static readonly Dictionary<SteeringWheel, float> lastTurned = new();
    private SteeringWheel[] wheels = new SteeringWheel[0];
    private float nextScan, nextSend;

    protected override void BindSettings()
    {
        delay = Bind("Delay", 1f, "Seconds after letting go before the wheel starts to straighten.", new AcceptableValueRange<float>(0f, 10f));
        speed = Bind("Speed", 40f, "Degrees per second the wheel turns back.", new AcceptableValueRange<float>(5f, 160f));
        lockKey = Bind("Lock key", new KeyboardShortcut(KeyCode.U), "Locks or unlocks the wheel at its current angle.");
    }

    public override void OnWorldUnloaded()
    {
        lastTurned.Clear();
        wheels = new SteeringWheel[0];
        locked = false;
    }

    public override void Update()
    {
        if (Game.Pressed(lockKey))
        {
            locked = !locked;
            Toast.Show(locked ? "Steering wheel locked" : "Steering wheel recenters");
        }
        if (!Raft_Network.IsHost || locked || !Game.InWorld) return;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 2f;
            wheels = Object.FindObjectsOfType<SteeringWheel>();
        }
        bool send = Time.time >= nextSend;
        foreach (var wheel in wheels)
        {
            if (wheel == null || !wheel.hasBeenPlaced || Mathf.Approximately(wheel.targetLocalEulerZ, 0f)) continue;
            if (lastTurned.TryGetValue(wheel, out float t) && Time.time - t < delay.Value) continue;
            wheel.targetLocalEulerZ = Mathf.MoveTowards(wheel.targetLocalEulerZ, 0f, speed.Value * Time.deltaTime);
            if (send || wheel.targetLocalEulerZ == 0f) Broadcast(wheel);
        }
        if (send) nextSend = Time.time + 0.1f;
    }

    // As the host does at the end of SteeringWheel.Rotate: clients set targetLocalEulerZ to the sent angle.
    private static void Broadcast(SteeringWheel wheel)
    {
        var message = new Message_SteeringWheel_Rotate(Messages.SteeringWheelRotate, wheel.network.NetworkIDManager, wheel.ObjectIndex, wheel.targetLocalEulerZ);
        wheel.network.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
    }

    [HarmonyPatch(typeof(SteeringWheel), nameof(SteeringWheel.Rotate))]
    private static class Turned
    {
        private static void Postfix(SteeringWheel __instance) => lastTurned[__instance] = Time.time;
    }
}
