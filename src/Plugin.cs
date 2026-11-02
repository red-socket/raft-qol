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

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}
