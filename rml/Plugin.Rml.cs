using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using RaftQoL.Core;

namespace RaftQoL;

// RaftModLoader entry, called by rml/RaftQoLMod.cs (the .cs file RML compiles) after it has loaded this assembly.
// RML loads mods in the main menu and can unload them again from its mod manager.
public partial class Plugin
{
    // The folder holding raft-qol.cfg (mods/ModData/raft-qol).
    internal static string DataDir;

    public static void RmlStart(string configPath)
    {
        DataDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
        Log = new ManualLogSource(Name);
        try
        {
            ModConfig = new ConfigFile(configPath, saveOnInit: true, Name, Guid, Version);
            Presets.Bind(ModConfig);
            Feature.DiscoverAll(ModConfig);
            Runtime.Install(ModConfig, createNow: true);
        }
        catch
        {
            // Leave nothing half applied.
            RmlStop();
            throw;
        }
        Log.LogInfo($"{Name} {Version} loaded (RML)");
    }

    public static void RmlStop()
    {
        Runtime.Uninstall();
        Feature.ShutdownAll();
        Ui.SettingsTab.Remove();
        Ui.Hud.Remove();
        Ui.Toast.Remove();
        Log.LogInfo($"{Name} unloaded");
    }
}
