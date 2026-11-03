using BepInEx.Configuration;
using HarmonyLib;
using RaftQoL.Core;
using UnityEngine;

namespace RaftQoL.Features.Raft;

// NAV-4: more sails push harder, and engines can move a bigger raft. Raft physics runs on the host only
// (Raft.FixedUpdate returns early elsewhere), so this has an effect when this player hosts.
// Vanilla clamps the summed sail direction to length 1 (Raft.cs:258), so a second sail adds nothing; here every extra
// open sail adds a share of the wind force (each one `falloff` times the previous), up to a cap.
public sealed class SailsAndEngines : Feature
{
    public override string Id => "SailsAndEngines";
    public override string Module => "Raft";
    public override string Name => "Stacking sails, stronger engines";
    public override string Description => "Each extra open sail makes the raft faster, and engines can move more foundations. Works when you host (or play alone).";
    public override Authority Authority => Authority.Host;
    public override bool InVanillaPlus => false;
    public override bool InRelaxed => true;

    private static ConfigEntry<float> falloff;
    private static ConfigEntry<float> maxBonus;
    private static ConfigEntry<float> engineCapacity;

    protected override void BindSettings()
    {
        falloff = Bind("Extra sail share", 0.5f, "Push of the 2nd sail relative to the 1st; each further sail adds this share of the previous one.", new AcceptableValueRange<float>(0f, 1f));
        maxBonus = Bind("Max sail bonus", 1f, "Most extra push from additional sails, as a multiple of one sail (1 = up to twice as fast).", new AcceptableValueRange<float>(0f, 3f));
        engineCapacity = Bind("Engine capacity", 1f, "Multiplier for how many foundations each engine can move (vanilla: 1).", new AcceptableValueRange<float>(1f, 5f));
        engineCapacity.SettingChanged += (_, _) => RaftVelocityManager.RecalculateMotors();
    }

    protected override void OnEnable() => RaftVelocityManager.RecalculateMotors();

    // Patches are already removed here, so this recomputes the vanilla strengths.
    protected override void OnDisable() => RaftVelocityManager.RecalculateMotors();

    internal static float SailBonus(int openSails)
    {
        float bonus = 0f, share = 1f;
        for (int i = 1; i < openSails; i++)
        {
            share *= falloff.Value;
            bonus += share;
        }
        return Mathf.Min(bonus, maxBonus.Value);
    }

    [HarmonyPatch(typeof(global::Raft), nameof(global::Raft.FixedUpdate))]
    private static class StackSails
    {
        // Same conditions as the sail branch of Raft.FixedUpdate: host, not anchored, wind, no engine pushing.
        private static void Postfix(global::Raft __instance)
        {
            if (!Raft_Network.IsHost || __instance.IsAnchored || __instance.speed == 0f || RaftVelocityManager.MotorDirection != Vector3.zero) return;
            if (GameModeValueManager.GetCurrentGameModeValue().raftSpecificVariables.isRaftAlwaysAnchored) return;
            int open = 0;
            foreach (var sail in Sail.AllSails)
                if (sail != null && sail.open) open++;
            if (open < 2) return;
            float bonus = SailBonus(open);
            if (bonus <= 0f) return;
            var direction = Vector3.ClampMagnitude(__instance.moveDirection, 1f);
            __instance.body.AddForce(direction * Mathf.Min(__instance.speed, __instance.maxSpeed) * bonus);
        }
    }

    [HarmonyPatch(typeof(RaftVelocityManager), nameof(RaftVelocityManager.RecalculateMotors))]
    private static class EngineCapacity
    {
        private static void Postfix()
        {
            if (engineCapacity.Value == 1f) return;
            RaftVelocityManager.motorStrength = Mathf.RoundToInt(RaftVelocityManager.motorStrength * engineCapacity.Value);
            RaftVelocityManager.extraMotorStrength = Mathf.RoundToInt(RaftVelocityManager.extraMotorStrength * engineCapacity.Value);
        }
    }
}
