using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RaftQoL;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "raft-qol";
    public const string Name = "Raft QoL";
    public const string Version = "0.0.1";

    internal static ManualLogSource Log;

    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;
        harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"{Name} {Version} loaded");
    }

    // BepInEx's manager object (and this component) is destroyed when the startup scene unloads,
    // before the main menu. Don't unpatch in OnDestroy, or every patch is gone for the session.
}
