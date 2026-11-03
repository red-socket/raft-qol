// RML build only. RML has no BepInEx, so this is a small stand-in for the part of BepInEx.Configuration that
// raft-qol uses: binding typed settings, change events, save-on-set, reload, and the same .cfg text format.
// It is an independent implementation of that API surface, not BepInEx code.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BepInEx.Configuration;

public sealed class ConfigDefinition : IEquatable<ConfigDefinition>
{
    public string Section { get; }
    public string Key { get; }

    public ConfigDefinition(string section, string key)
    {
        Section = section;
        Key = key;
    }

    public bool Equals(ConfigDefinition other) => other != null && Section == other.Section && Key == other.Key;
    public override bool Equals(object obj) => Equals(obj as ConfigDefinition);
    public override int GetHashCode() => (Section ?? "").GetHashCode() * 397 ^ (Key ?? "").GetHashCode();
    public override string ToString() => $"{Section}.{Key}";
}

public sealed class ConfigDescription
{
    public string Description { get; }
    public AcceptableValueBase AcceptableValues { get; }

    public ConfigDescription(string description, AcceptableValueBase acceptableValues = null, params object[] tags)
    {
        Description = description ?? "";
        AcceptableValues = acceptableValues;
    }

    public static ConfigDescription Empty { get; } = new ConfigDescription("");
}

public abstract class AcceptableValueBase
{
    public Type ValueType { get; }

    protected AcceptableValueBase(Type valueType) => ValueType = valueType;

    public abstract object Clamp(object value);
    public abstract bool IsValid(object value);
    public abstract string ToDescriptionString();
}

public sealed class AcceptableValueRange<T> : AcceptableValueBase where T : IComparable
{
    public T MinValue { get; }
    public T MaxValue { get; }

    public AcceptableValueRange(T minValue, T maxValue) : base(typeof(T))
    {
        if (maxValue.CompareTo(minValue) < 0) throw new ArgumentException("maxValue must be >= minValue");
        MinValue = minValue;
        MaxValue = maxValue;
    }

    public override object Clamp(object value)
    {
        if (MinValue.CompareTo(value) > 0) return MinValue;
        if (MaxValue.CompareTo(value) < 0) return MaxValue;
        return value;
    }

    public override bool IsValid(object value) => MinValue.CompareTo(value) <= 0 && MaxValue.CompareTo(value) >= 0;

    public override string ToDescriptionString() =>
        $"# Acceptable value range: From {TomlTypeConverter.Serialize(MinValue, typeof(T))} to {TomlTypeConverter.Serialize(MaxValue, typeof(T))}";
}

public sealed class SettingChangedEventArgs : EventArgs
{
    public ConfigEntryBase ChangedSetting { get; }
    public SettingChangedEventArgs(ConfigEntryBase changedSetting) => ChangedSetting = changedSetting;
}

public abstract class ConfigEntryBase
{
    public ConfigFile ConfigFile { get; }
    public ConfigDefinition Definition { get; }
    public ConfigDescription Description { get; }
    public Type SettingType { get; }
    public object DefaultValue { get; }

    public abstract object BoxedValue { get; set; }

    protected ConfigEntryBase(ConfigFile file, ConfigDefinition definition, Type settingType, object defaultValue, ConfigDescription description)
    {
        ConfigFile = file;
        Definition = definition;
        SettingType = settingType;
        DefaultValue = defaultValue;
        Description = description ?? ConfigDescription.Empty;
    }

    public string GetSerializedValue() => TomlTypeConverter.Serialize(BoxedValue, SettingType);

    public void SetSerializedValue(string value)
    {
        try { BoxedValue = TomlTypeConverter.Deserialize(value, SettingType); }
        catch (Exception e) { Debug.LogWarning($"[Raft QoL] Config value of {Definition} could not be read ({e.Message}); keeping {GetSerializedValue()}"); }
    }

    protected object ClampValue(object value) => Description.AcceptableValues != null ? Description.AcceptableValues.Clamp(value) : value;

    protected void OnSettingChanged(object sender) => ConfigFile.OnSettingChanged(sender, this);

    internal void WriteDescription(StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(Description.Description))
            sb.AppendLine("## " + Description.Description.Replace("\n", "\n## "));
        sb.AppendLine("# Setting type: " + SettingType.Name);
        sb.AppendLine("# Default value: " + TomlTypeConverter.Serialize(DefaultValue, SettingType));
        if (Description.AcceptableValues != null)
            sb.AppendLine(Description.AcceptableValues.ToDescriptionString());
        else if (SettingType.IsEnum)
            sb.AppendLine("# Acceptable values: " + string.Join(", ", Enum.GetNames(SettingType)));
    }
}

public sealed class ConfigEntry<T> : ConfigEntryBase
{
    private T typedValue;

    public event EventHandler SettingChanged;

    internal ConfigEntry(ConfigFile file, ConfigDefinition definition, T defaultValue, ConfigDescription description)
        : base(file, definition, typeof(T), defaultValue, description)
    {
        typedValue = (T)ClampValue(defaultValue);
    }

    public T Value
    {
        get => typedValue;
        set
        {
            value = (T)ClampValue(value);
            if (Equals(typedValue, value)) return;
            typedValue = value;
            SettingChanged?.Invoke(this, EventArgs.Empty);
            OnSettingChanged(this);
        }
    }

    public override object BoxedValue
    {
        get => Value;
        set => Value = (T)value;
    }
}

public sealed class ConfigFile : IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>
{
    private readonly List<ConfigEntryBase> entries = new();
    private readonly Dictionary<ConfigDefinition, string> orphans = new();
    private readonly string header;

    public string ConfigFilePath { get; }
    public bool SaveOnConfigSet { get; set; } = true;

    public event EventHandler<SettingChangedEventArgs> SettingChanged;
    public event EventHandler ConfigReloaded;

    public ConfigFile(string configPath, bool saveOnInit, string ownerName = null, string ownerGuid = null, string ownerVersion = null)
    {
        ConfigFilePath = Path.GetFullPath(configPath);
        header = ownerName == null ? "" : $"## Settings file was created by plugin {ownerName} v{ownerVersion}\n## Plugin GUID: {ownerGuid}\n";
        if (File.Exists(ConfigFilePath)) Reload();
        else if (saveOnInit) Save();
    }

    public ConfigEntryBase this[ConfigDefinition key] => entries.First(e => e.Definition.Equals(key));

    public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description) =>
        Bind(section, key, defaultValue, new ConfigDescription(description));

    public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription configDescription = null)
    {
        var definition = new ConfigDefinition(section, key);
        var existing = entries.FirstOrDefault(e => e.Definition.Equals(definition));
        if (existing != null) return (ConfigEntry<T>)existing;

        var entry = new ConfigEntry<T>(this, definition, defaultValue, configDescription);
        entries.Add(entry);
        if (orphans.TryGetValue(definition, out var stored))
        {
            orphans.Remove(definition);
            bool save = SaveOnConfigSet;
            SaveOnConfigSet = false;
            try { entry.SetSerializedValue(stored); }
            finally { SaveOnConfigSet = save; }
        }
        if (SaveOnConfigSet) Save();
        return entry;
    }

    public void Reload()
    {
        orphans.Clear();
        string section = "";
        bool save = SaveOnConfigSet;
        SaveOnConfigSet = false;
        try
        {
            foreach (var raw in File.ReadAllLines(ConfigFilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                var definition = new ConfigDefinition(section, line.Substring(0, eq).Trim());
                var value = line.Substring(eq + 1).Trim();
                var entry = entries.FirstOrDefault(e => e.Definition.Equals(definition));
                if (entry != null) entry.SetSerializedValue(value);
                else orphans[definition] = value;
            }
        }
        finally
        {
            SaveOnConfigSet = save;
        }
        ConfigReloaded?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        var sb = new StringBuilder(header);
        var all = entries.Select(e => (Definition: e.Definition, Entry: e, Value: e.GetSerializedValue()))
            .Concat(orphans.Select(o => (Definition: o.Key, Entry: (ConfigEntryBase)null, Value: o.Value)));
        foreach (var section in all.GroupBy(x => x.Definition.Section).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine().AppendLine($"[{section.Key}]");
            foreach (var item in section)
            {
                sb.AppendLine();
                item.Entry?.WriteDescription(sb);
                sb.AppendLine($"{item.Definition.Key} = {item.Value}");
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath));
        File.WriteAllText(ConfigFilePath, sb.ToString(), new UTF8Encoding(false));
    }

    internal void OnSettingChanged(object sender, ConfigEntryBase entry)
    {
        SettingChanged?.Invoke(sender, new SettingChangedEventArgs(entry));
        if (SaveOnConfigSet) Save();
    }

    public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator() =>
        entries.Select(e => new KeyValuePair<ConfigDefinition, ConfigEntryBase>(e.Definition, e)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public struct KeyboardShortcut
{
    private readonly KeyCode[] keys; // main key first, then modifiers

    public static readonly KeyboardShortcut Empty = new KeyboardShortcut(KeyCode.None);

    public KeyboardShortcut(KeyCode mainKey, params KeyCode[] modifiers)
    {
        keys = new[] { mainKey }.Concat(modifiers ?? Array.Empty<KeyCode>()).ToArray();
    }

    public KeyCode MainKey => keys != null && keys.Length > 0 ? keys[0] : KeyCode.None;
    public IEnumerable<KeyCode> Modifiers => keys == null ? Enumerable.Empty<KeyCode>() : keys.Skip(1);

    // Same rule as BepInEx: the main key went down this frame and every listed modifier is held.
    public bool IsDown() => MainKey != KeyCode.None && Input.GetKeyDown(MainKey) && Modifiers.All(Input.GetKey);
    public bool IsPressed() => MainKey != KeyCode.None && Input.GetKey(MainKey) && Modifiers.All(Input.GetKey);
    public bool IsUp() => MainKey != KeyCode.None && Input.GetKeyUp(MainKey) && Modifiers.All(Input.GetKey);

    public override string ToString() => MainKey == KeyCode.None ? "Not set" : string.Join(" + ", keys.Select(k => k.ToString()));

    public static KeyboardShortcut Deserialize(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "Not set") return Empty;
        var parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (KeyCode)Enum.Parse(typeof(KeyCode), p.Trim(), true)).ToArray();
        return parts.Length == 0 ? Empty : new KeyboardShortcut(parts[0], parts.Skip(1).ToArray());
    }

    public override bool Equals(object obj) => obj is KeyboardShortcut other && MainKey == other.MainKey && Modifiers.SequenceEqual(other.Modifiers);
    public override int GetHashCode() => keys == null ? 0 : keys.Aggregate(17, (h, k) => h * 31 + (int)k);
}

internal static class TomlTypeConverter
{
    public static string Serialize(object value, Type type)
    {
        if (value == null) return "";
        if (type == typeof(bool)) return (bool)value ? "true" : "false";
        if (type == typeof(float)) return ((float)value).ToString("R", CultureInfo.InvariantCulture);
        if (type == typeof(double)) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
        if (type == typeof(KeyboardShortcut)) return ((KeyboardShortcut)value).ToString();
        if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString();
    }

    public static object Deserialize(string text, Type type)
    {
        if (type == typeof(string)) return text;
        if (type == typeof(bool)) return bool.Parse(text);
        if (type == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(KeyboardShortcut)) return KeyboardShortcut.Deserialize(text);
        if (type.IsEnum) return Enum.Parse(type, text, true);
        return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }
}
