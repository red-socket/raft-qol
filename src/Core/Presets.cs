using System.Collections.Generic;
using BepInEx.Configuration;

namespace RaftQoL.Core;

public enum Preset
{
    // Convenience only; balance stays vanilla.
    VanillaPlus,
    // Vanilla+ plus opt-in balance changes (refunds, keep inventory, faster hooks...).
    Relaxed,
    // Whatever the player set by hand.
    Custom,
}

// Choosing a preset writes every tracked setting; editing a tracked setting by hand switches to Custom.
public static class Presets
{
    private sealed class Tracked
    {
        public ConfigEntryBase Entry;
        public object VanillaPlus;
        public object Relaxed;

        public object ValueFor(Preset preset) => preset == Preset.Relaxed ? Relaxed : VanillaPlus;
    }

    private static readonly List<Tracked> tracked = new();
    private static ConfigFile config;
    private static bool applying;

    public static ConfigEntry<Preset> Active { get; private set; }

    // While true, changes are not reconciled one by one (Reconcile runs afterwards).
    internal static bool Suspended;

    internal static void Bind(ConfigFile file)
    {
        config = file;
        Active = file.Bind("General", "Preset", Preset.VanillaPlus,
            "VanillaPlus: convenience features only, balance stays vanilla. Relaxed: adds the balance changes. " +
            "Custom: your own choice. Picking VanillaPlus or Relaxed overwrites the feature settings it controls.");
        Active.SettingChanged += (_, _) =>
        {
            if (!applying && !Suspended) Apply(Active.Value);
        };
    }

    internal static void Track<T>(ConfigEntry<T> entry, T vanillaPlus, T relaxed)
    {
        tracked.Add(new Tracked { Entry = entry, VanillaPlus = vanillaPlus, Relaxed = relaxed });
        entry.SettingChanged += (_, _) =>
        {
            if (applying || Suspended || Active.Value == Preset.Custom) return;
            if (!Equals(entry.BoxedValue, Find(entry).ValueFor(Active.Value)))
                SetActive(Preset.Custom);
        };
    }

    public static void Apply(Preset preset)
    {
        if (preset == Preset.Custom) return;
        applying = true;
        bool save = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try
        {
            if (Active.Value != preset) Active.Value = preset;
            foreach (var t in tracked)
                t.Entry.BoxedValue = t.ValueFor(preset);
        }
        finally
        {
            config.SaveOnConfigSet = save;
            applying = false;
        }
        config.Save();
        Plugin.Log.LogInfo($"Preset {preset} applied");
    }

    // After startup or a config reload: if the stored values don't match the stored preset, the preset is Custom.
    internal static void Reconcile()
    {
        if (Active.Value == Preset.Custom) return;
        foreach (var t in tracked)
        {
            if (Equals(t.Entry.BoxedValue, t.ValueFor(Active.Value))) continue;
            Plugin.Log.LogInfo($"[{t.Entry.Definition.Section}] {t.Entry.Definition.Key} differs from preset {Active.Value}; preset is now Custom");
            SetActive(Preset.Custom);
            return;
        }
    }

    private static void SetActive(Preset preset)
    {
        applying = true;
        try { Active.Value = preset; }
        finally { applying = false; }
    }

    private static Tracked Find(ConfigEntryBase entry) => tracked.Find(t => t.Entry == entry);
}
