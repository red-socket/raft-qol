using System;
using HMLLibrary;
using UnityEngine;

// RaftModLoader entry. RML compiles every .cs file in the .rmod into one assembly: this class, the shared source
// from src/ and the RML-only files from rml/.
public class RaftQoLMod : Mod
{
    public void Start()
    {
        try
        {
            // mods/ModData/raft-qol/raft-qol.cfg
            RaftQoL.Plugin.RmlStart(System.IO.Path.Combine(DataFolder, "raft-qol.cfg"));
        }
        catch (Exception e)
        {
            Debug.LogError("[Raft QoL] failed to start: " + e);
        }
    }

    public void OnModUnload()
    {
        try { RaftQoL.Plugin.RmlStop(); }
        catch (Exception e) { Debug.LogError("[Raft QoL] failed to unload cleanly: " + e); }
    }
}
