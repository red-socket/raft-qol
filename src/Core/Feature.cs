using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace RaftQoL.Core;

public enum Authority
{
    // Runs on each player's own machine; safe with any peer.
    Local,
    // Only has an effect when this player hosts the world.
    Host,
}

// One toggleable feature: its own config section, its own Harmony instance, and nothing applied while disabled.
// Patch classes are nested types annotated with [HarmonyPatch]; they are applied on enable and removed on disable.
public abstract class Feature
{
    public static readonly List<Feature> All = new();

    public abstract string Id { get; }
    public abstract string Module { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual Authority Authority => Authority.Local;

    // Whether the feature is on in each preset (Presets.cs).
    public abstract bool InVanillaPlus { get; }
    public virtual bool InRelaxed => InVanillaPlus;
    // False for features the presets never touch (developer checks).
    public virtual bool UsesPresets => true;

    public string Section => $"{Module}.{Id}";
    public ConfigEntry<bool> Enabled { get; private set; }
    public bool Active { get; private set; }

    protected ConfigFile Config { get; private set; }

    internal int PatchedMethodCount => harmony.GetPatchedMethods().Count();

    private Harmony harmony;
    private bool faulted;

    internal static void DiscoverAll(ConfigFile config)
    {
        var types = typeof(Feature).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Feature)) && !t.IsAbstract)
            .OrderBy(t => t.FullName);
        foreach (var type in types)
        {
            var feature = (Feature)Activator.CreateInstance(type);
            feature.Bind(config);
            All.Add(feature);
        }
        Presets.Reconcile();
        foreach (var feature in All)
            feature.Refresh();
        Plugin.Log.LogInfo($"{All.Count} features, {All.Count(f => f.Active)} enabled: {string.Join(", ", All.Where(f => f.Active).Select(f => f.Id))}");
    }

    private void Bind(ConfigFile config)
    {
        Config = config;
        harmony = new Harmony($"{Plugin.Guid}.{Id}");
        Enabled = UsesPresets
            ? Bind("Enabled", InVanillaPlus, InRelaxed, $"{Name}: {Description}")
            : Bind("Enabled", InVanillaPlus, $"{Name}: {Description}");
        Enabled.SettingChanged += (_, _) => Refresh();
        BindSettings();
    }

    // Override to bind the feature's own settings with the Bind helpers below.
    protected virtual void BindSettings() { }

    // A setting that the presets control: changing it by hand switches the preset to Custom.
    protected ConfigEntry<T> Bind<T>(string key, T vanillaPlus, T relaxed, string description, AcceptableValueBase range = null)
    {
        var entry = Config.Bind(Section, key, vanillaPlus, new ConfigDescription(description, range));
        Presets.Track(entry, vanillaPlus, relaxed);
        return entry;
    }

    // A setting outside the presets (hotkeys, HUD positions, cosmetic choices).
    protected ConfigEntry<T> Bind<T>(string key, T value, string description, AcceptableValueBase range = null)
    {
        return Config.Bind(Section, key, value, new ConfigDescription(description, range));
    }

    private void Refresh()
    {
        bool want = Enabled.Value && !faulted;
        if (want == Active) return;
        try
        {
            if (want)
            {
                foreach (var type in PatchTypes())
                    harmony.PatchAll(type);
                Active = true;
                OnEnable();
                if (Game.InWorld) OnWorldLoaded();
            }
            else
            {
                Active = false;
                if (Game.InWorld) OnWorldUnloaded();
                OnDisable();
                harmony.UnpatchSelf();
            }
            Plugin.Log.LogInfo($"{Id} {(Active ? "enabled" : "disabled")}, {PatchedMethodCount} methods patched");
        }
        catch (Exception e)
        {
            Fault(e, want ? "enabling" : "disabling");
        }
    }

    // Stops a broken feature for the rest of the session (its config value is left alone).
    internal void Fault(Exception e, string during)
    {
        Plugin.Log.LogError($"{Id} failed while {during} and is turned off until restart: {e}");
        faulted = true;
        Active = false;
        try { harmony.UnpatchSelf(); }
        catch (Exception inner) { Plugin.Log.LogError($"{Id}: unpatch failed: {inner}"); }
    }

    private IEnumerable<Type> PatchTypes()
    {
        return GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0);
    }

    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }

    // Called every frame while enabled, in the main menu and in a world.
    public virtual void Update() { }

    // Called when a world becomes playable, and when the feature is enabled inside a world.
    public virtual void OnWorldLoaded() { }

    // Called when leaving a world, and when the feature is disabled inside a world.
    public virtual void OnWorldUnloaded() { }
}
