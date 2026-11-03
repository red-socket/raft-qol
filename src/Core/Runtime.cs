using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaftQoL.Core;

// The mod's own persistent GameObject. BepInEx's manager object (and the plugin component on it) is destroyed
// when Raft's startup scene unloads, so per-frame work lives here, created once the first scene has loaded.
internal class Runtime : MonoBehaviour
{
    private static Runtime instance;
    private static ConfigFile config;

    private bool wasInWorld;
    private DateTime configWriteTime;
    private float nextConfigCheck;

    // createNow: the loader starts the mod after the first scene has loaded (RML loads mods in the main menu).
    internal static void Install(ConfigFile file, bool createNow = false)
    {
        config = file;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (createNow) Create();
    }

    // For a loader that can unload the mod.
    internal static void Uninstall()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance != null) Destroy(instance.gameObject);
        instance = null;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Create();

    private static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("RaftQoL");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Runtime>();
        // Features already ran OnWorldLoaded when they were enabled inside a world (the mod loaded mid-game).
        instance.wasInWorld = Game.InWorld;
    }

    private void Start()
    {
        configWriteTime = ConfigWriteTime();
    }

    private void Update()
    {
        // An uninstalled Runtime may still get one more Update before Destroy takes effect.
        if (instance != this) return;
        WatchConfigFile();

        bool inWorld = Game.InWorld;
        if (inWorld != wasInWorld)
        {
            wasInWorld = inWorld;
            Plugin.Log.LogInfo(inWorld ? "World loaded" : "World unloaded");
            foreach (var feature in Feature.All)
            {
                if (!feature.Active) continue;
                try
                {
                    if (inWorld) feature.OnWorldLoaded();
                    else feature.OnWorldUnloaded();
                }
                catch (Exception e)
                {
                    feature.Fault(e, inWorld ? "loading the world" : "unloading the world");
                }
            }
            if (inWorld) Game.RaiseWorldLoaded();
            else Game.RaiseWorldUnloaded();
        }

        foreach (var feature in Feature.All)
        {
            if (!feature.Active) continue;
            try { feature.Update(); }
            catch (Exception e) { feature.Fault(e, "updating"); }
        }

        Ui.Hud.Update();
        Ui.SettingsTab.Update();
#if DEBUG
        Dev.DevRig.Update();
#endif
    }

    // Edits to raft-qol.cfg made while the game runs (by hand or by another tool) apply live.
    private void WatchConfigFile()
    {
        if (Time.unscaledTime < nextConfigCheck) return;
        nextConfigCheck = Time.unscaledTime + 1f;
        var writeTime = ConfigWriteTime();
        if (writeTime == configWriteTime) return;
        configWriteTime = writeTime;

        var presetBefore = Presets.Active.Value;
        Presets.Suspended = true;
        try { config.Reload(); }
        catch (Exception e) { Plugin.Log.LogError($"Reloading {config.ConfigFilePath} failed: {e.Message}"); }
        finally { Presets.Suspended = false; }

        if (Presets.Active.Value != presetBefore && Presets.Active.Value != Preset.Custom)
            Presets.Apply(Presets.Active.Value);
        else
            Presets.Reconcile();
        configWriteTime = ConfigWriteTime();
    }

    private static DateTime ConfigWriteTime()
    {
        try { return File.GetLastWriteTimeUtc(config.ConfigFilePath); }
        catch { return default; }
    }
}
