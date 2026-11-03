using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

// The file RaftModLoader compiles. Raft QoL itself is the precompiled RaftQoL.dll packed next to it in the .rmod
// (built from the same source as the BepInEx plugin); this loads it and hands over.
public class RaftQoLMod : Mod
{
    // Unity finds a MonoBehaviour's script by assembly and class name, so a second assembly called RaftQoL could not
    // create Raft QoL's components. A reload in the same game session reuses the first one, kept in the AppDomain.
    private const string AssemblyKey = "raft-qol.assembly";
    private const string HashKey = "raft-qol.assembly-sha1";

    private Assembly core;

    public void Start()
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            var bytes = GetEmbeddedFileBytes("RaftQoL.dll");
            string hash;
            using (var sha = SHA1.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes));
            core = AppDomain.CurrentDomain.GetData(AssemblyKey) as Assembly;
            if (core == null)
            {
                core = Assembly.Load(bytes);
                AppDomain.CurrentDomain.SetData(AssemblyKey, core);
                AppDomain.CurrentDomain.SetData(HashKey, hash);
            }
            else if ((string)AppDomain.CurrentDomain.GetData(HashKey) != hash)
            {
                Debug.LogWarning("[Raft QoL] The mod file changed since the game started; restart Raft to use the new version.");
            }
            // mods/ModData/raft-qol/raft-qol.cfg
            var configPath = System.IO.Path.Combine(DataFolder, "raft-qol.cfg");
            core.GetType("RaftQoL.Plugin").GetMethod("RmlStart").Invoke(null, new object[] { configPath });
        }
        catch (Exception e)
        {
            Debug.LogError("[Raft QoL] failed to start: " + e);
        }
    }

    public void OnModUnload()
    {
        try { core?.GetType("RaftQoL.Plugin").GetMethod("RmlStop").Invoke(null, null); }
        catch (Exception e) { Debug.LogError("[Raft QoL] failed to unload cleanly: " + e); }
        AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
    }

    private Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (name == "RaftQoL") return core;
        // 0Harmony and the game assemblies: whatever the game and RML have already loaded.
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
    }
}
