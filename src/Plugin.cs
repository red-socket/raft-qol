using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RaftQoL.Core;

namespace RaftQoL;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "raft-qol";
    public const string Name = "Raft QoL";
    public const string Version = "0.0.1";

    internal static ManualLogSource Log;
    // Outlives this component (see Awake).
    internal static ConfigFile ModConfig;

    // Patches are applied per feature (Core/Feature.cs). BepInEx's manager object (and this component) is
    // destroyed when the startup scene unloads, before the main menu, so nothing here may rely on Update or
    // OnDestroy: per-frame work runs in Core/Runtime.cs.
    private void Awake()
    {
        Log = Logger;
        ModConfig = Config;
        Presets.Bind(Config);
        Feature.DiscoverAll(Config);
        Runtime.Install(Config);
        Log.LogInfo($"{Name} {Version} loaded");
    }
}
