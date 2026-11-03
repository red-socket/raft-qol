// RML build only: stand-in for the part of BepInEx.Logging raft-qol uses. Writes to Unity's log, which RML's
// console (F10) and Player.log show.
using UnityEngine;

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public string SourceName { get; }

        public ManualLogSource(string sourceName) => SourceName = sourceName;

        public void LogInfo(object data) => Debug.Log($"[{SourceName}] {data}");
        public void LogMessage(object data) => Debug.Log($"[{SourceName}] {data}");
        public void LogDebug(object data) => Debug.Log($"[{SourceName}] {data}");
        public void LogWarning(object data) => Debug.LogWarning($"[{SourceName}] {data}");
        public void LogError(object data) => Debug.LogError($"[{SourceName}] {data}");
        public void LogFatal(object data) => Debug.LogError($"[{SourceName}] {data}");
    }
}
