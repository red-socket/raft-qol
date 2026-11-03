using BepInEx.Configuration;
using BepInEx.Logging;

namespace RaftQoL
{
    // Shared by both loaders: Plugin.cs is the BepInEx entry, rml/Plugin.Rml.cs the RaftModLoader one.
    public partial class Plugin
    {
        public const string Guid = "raft-qol";
        public const string Name = "Raft QoL";
        public const string Version = "0.1.1";

        internal static ManualLogSource Log;
        // Outlives the loader's entry object (see Plugin.cs).
        internal static ConfigFile ModConfig;
    }
}
