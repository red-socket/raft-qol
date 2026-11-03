using System.Collections.Generic;
using BepInEx.Configuration;
using RaftQoL.Core;

namespace RaftQoL.Features.Gathering;

// GAT-1: faster hook. Scales Hook.pullSpeed and divides Hook.gatherTime on the local player's hooks (one component
// per hook tier). No patches: the fields are set on world load and on every setting change, and restored when the
// feature is turned off.
public sealed class HookSpeed : Feature
{
    public override string Id => "HookSpeed";
    public override string Module => "Gathering";
    public override string Name => "Faster hook";
    public override string Description => "The hook pulls in faster and grabs floating items sooner.";
    public override bool InVanillaPlus => false;
    public override bool InRelaxed => true;

    private ConfigEntry<float> pull;
    private ConfigEntry<float> gather;
    private ConfigEntry<bool> instant;

    private readonly Dictionary<Hook, (float pull, float gather)> originals = new();

    protected override void BindSettings()
    {
        pull = Bind("Pull speed", 2f, "Multiplier for how fast the hook is reeled in (vanilla: 1).", new AcceptableValueRange<float>(1f, 5f));
        gather = Bind("Grab speed", 2f, "Multiplier for how fast the hook grabs debris (vanilla: 1).", new AcceptableValueRange<float>(1f, 10f));
        instant = Bind("Instant grab", false, "Grab debris immediately.");
        pull.SettingChanged += (_, _) => Apply();
        gather.SettingChanged += (_, _) => Apply();
        instant.SettingChanged += (_, _) => Apply();
    }

    public override void OnWorldLoaded() => Apply();

    // Also runs first when the feature is turned off inside a world (Feature.Refresh), so it restores.
    public override void OnWorldUnloaded() => Restore();

    protected override void OnDisable() => Restore();

    private void Apply()
    {
        if (!Active || !Game.InWorld) return;
        foreach (var hook in Game.LocalPlayer.GetComponentsInChildren<Hook>(true))
        {
            if (!originals.TryGetValue(hook, out var o))
                originals[hook] = o = (hook.pullSpeed, hook.gatherTime);
            hook.pullSpeed = o.pull * pull.Value;
            hook.gatherTime = instant.Value ? 0.01f : o.gather / gather.Value;
        }
    }

    private void Restore()
    {
        foreach (var pair in originals)
        {
            if (pair.Key == null) continue;
            pair.Key.pullSpeed = pair.Value.pull;
            pair.Key.gatherTime = pair.Value.gather;
        }
        originals.Clear();
    }
}
