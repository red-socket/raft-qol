using BepInEx;
using RaftQoL.Core;

namespace RaftQoL
{
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
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
}
