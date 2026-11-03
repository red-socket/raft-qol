using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using I2.Loc;
using RaftQoL.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaftQoL.Ui;

// A "QoL" tab in Raft's own Settings window (main menu and pause menu), built from clones of the vanilla rows.
// Every row edits a BepInEx ConfigEntry live. Built once, before the window is first opened; the recipe and its
// pitfalls are in docs/systems/ui-input-settings.md §7.6.
internal static class SettingsTab
{
    private const float RowHeight = 56.8f;
    private const int TabIndex = 4;

    private static Settings builtFor;
    // What Build added to the vanilla window, so Remove can take it out again.
    private static GameObject addedPanel, addedButton;
    private static OptionsMenuBox addedBox;
    private static EventTrigger[] addedTriggers = Array.Empty<EventTrigger>();
    private static bool tabStripControlledWidth;

    // Called from Runtime every frame; builds once per Settings instance (it is a DontDestroyOnLoad singleton).
    internal static void Update()
    {
        var settings = ComponentManager<Settings>.Value;
        if (settings == null || settings == builtFor) return;
        builtFor = settings;
        try
        {
            new Builder(settings).Build();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Building the QoL settings tab failed; use raft-qol.cfg instead: {e}");
        }
    }

    // For a loader that can unload the mod: restores the vanilla Settings window.
    internal static void Remove()
    {
        var settings = builtFor;
        builtFor = null;
        if (settings == null || addedPanel == null) return;
        try
        {
            if (addedButton != null)
            {
                var tabButton = addedButton.GetComponent<TabButton>();
                var group = settings.tabGroup;
                // The window reopens on the last selected tab; with ours gone it would open blank.
                if (group.SelectedTabButton == tabButton) group.SelectTab(0);
                if (group.tabButtons != null) group.tabButtons = group.tabButtons.Where(b => b != tabButton).ToArray();
                addedButton.transform.parent.GetComponent<HorizontalLayoutGroup>().childControlWidth = tabStripControlledWidth;
                Object.Destroy(addedButton);
            }
            settings.optionMenuboxes = settings.optionMenuboxes.Where(b => b != addedBox).ToArray();
            settings.dropdownTriggers = settings.dropdownTriggers.Where(t => !addedTriggers.Contains(t)).ToArray();
            Object.Destroy(addedPanel);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Removing the QoL settings tab failed: {e}");
        }
        addedPanel = addedButton = null;
        addedBox = null;
        addedTriggers = Array.Empty<EventTrigger>();
    }

    private sealed class Builder
    {
        private readonly Settings settings;
        private readonly Transform tabContent;
        private readonly GameObject toggleTemplate, sliderTemplate, dropdownTemplate, keybindTemplate;
        private readonly List<Selectable> selectables = new();
        private readonly List<EventTrigger> triggers = new();
        private Transform content;
        private QoLSettingsBox box;
        private EventSensitiveScrollRect scroll;

        public Builder(Settings settings)
        {
            this.settings = settings;
            tabContent = Find(settings.optionsCanvas.transform, "TabContent");
            toggleTemplate = Template("General/Viewport/Content/Quick build");
            sliderTemplate = Template("Audio/Viewport/Content/MasterVolume");
            dropdownTemplate = Template("Audio/Viewport/Content/MusicPlayRate");
            keybindTemplate = Template("Controls/Viewport/Content/Keybindings/SortIntoStorage");
        }

        private GameObject Template(string path)
        {
            var t = tabContent.Find(path);
            if (t == null) throw new InvalidOperationException("template not found: TabContent/" + path);
            return t.gameObject;
        }

        public void Build()
        {
            if (tabContent.Find("RaftQoL") != null) return;

            // Clone General inside an inactive holder so none of the clone's Awake methods run before its vanilla
            // box is gone (GeneralSettingsBox.Awake would wire the clone's toggles to the game's own flags).
            var holder = new GameObject("RaftQoL_Holder");
            holder.SetActive(false);
            var panel = Object.Instantiate(tabContent.Find("General").gameObject, holder.transform, false);
            panel.name = "RaftQoL";
            Object.DestroyImmediate(panel.GetComponent<GeneralSettingsBox>());
            scroll = panel.GetComponent<EventSensitiveScrollRect>();
            content = panel.transform.Find("Viewport/Content");
            for (int i = content.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(content.GetChild(i).gameObject);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            box = panel.AddComponent<QoLSettingsBox>();
            box.gamepadObjects = Array.Empty<GameObject>();
            box.keyboardObjects = Array.Empty<GameObject>();

            AddRows();
            ChainNavigation();

            panel.transform.SetParent(tabContent, false);
            panel.SetActive(false);
            Object.Destroy(holder);

            AddTabButton(panel);
            settings.dropdownTriggers = settings.dropdownTriggers.Concat(triggers).ToArray();
            settings.optionMenuboxes = settings.optionMenuboxes.Append(box).ToArray();
            addedPanel = panel;
            addedBox = box;
            addedTriggers = triggers.ToArray();
            Plugin.Log.LogInfo($"QoL settings tab added ({content.childCount} rows)");
        }

        private void AddRows()
        {
            AddDropdown("Preset", Presets.Active, indent: false);
            foreach (var module in Feature.All.GroupBy(f => f.Module))
            {
                AddHeader(module.Key);
                foreach (var feature in module)
                {
                    AddToggle(feature.Name, feature.Enabled, indent: false);
                    foreach (var entry in Plugin.ModConfig.Where(e => e.Key.Section == feature.Section && e.Value != feature.Enabled))
                        AddSetting(entry.Value);
                }
            }
        }

        private void AddSetting(ConfigEntryBase entry)
        {
            string label = entry.Definition.Key;
            switch (entry)
            {
                case ConfigEntry<bool> b:
                    AddToggle(label, b, indent: true);
                    break;
                case ConfigEntry<KeyboardShortcut> k:
                    AddKeybind(label, k);
                    break;
                case ConfigEntry<int> or ConfigEntry<float> when entry.Description.AcceptableValues != null:
                    AddSlider(label, entry);
                    break;
                default:
                    if (entry.SettingType.IsEnum)
                        AddDropdown(label, entry, indent: true);
                    else
                        Plugin.Log.LogWarning($"No settings row for [{entry.Definition.Section}] {label} ({entry.SettingType.Name}); edit it in raft-qol.cfg");
                    break;
            }
        }

        private GameObject Row(GameObject template, string name, string labelPath, string label)
        {
            var row = Object.Instantiate(template, content, false);
            row.name = name;
            SetLabel(row.transform.Find(labelPath).GetComponent<Text>(), label);
            return row;
        }

        private void AddHeader(string title)
        {
            var row = Row(toggleTemplate, "Header " + title, "Text", title.ToUpperInvariant());
            row.transform.Find("Toggle").gameObject.SetActive(false);
            var image = row.GetComponent<Image>();
            image.color = new Color(image.color.r * 0.6f, image.color.g * 0.6f, image.color.b * 0.6f, image.color.a);
        }

        private void AddToggle(string label, ConfigEntry<bool> entry, bool indent)
        {
            var row = Row(toggleTemplate, "Toggle " + label, "Text", Indent(label, indent));
            var toggle = row.transform.Find("Toggle").GetComponent<Toggle>();
            toggle.onValueChanged = new Toggle.ToggleEvent();
            Track(toggle, () => toggle.SetIsOnWithoutNotify(entry.Value), entry);
            toggle.onValueChanged.AddListener(v => entry.Value = v);
        }

        private void AddSlider(string label, ConfigEntryBase entry)
        {
            var row = Row(sliderTemplate, "Slider " + label, "Text", Indent(label, true));
            var uiSlider = row.GetComponent<UISlider>();
            var slider = uiSlider.Slider;
            slider.onValueChanged = new Slider.SliderEvent();
            bool whole = entry.SettingType == typeof(int);
            uiSlider.showPercent = false;
            uiSlider.decimalCount = whole ? 0 : 2;
            uiSlider.updateText = true;
            var range = entry.Description.AcceptableValues;
            slider.wholeNumbers = whole;
            slider.minValue = Convert.ToSingle(range.GetType().GetProperty("MinValue").GetValue(range));
            slider.maxValue = Convert.ToSingle(range.GetType().GetProperty("MaxValue").GetValue(range));
            Track(slider, () => slider.SetValueWithoutNotify(Convert.ToSingle(entry.BoxedValue)), entry);
            slider.onValueChanged.AddListener(v => entry.BoxedValue = whole ? Mathf.RoundToInt(v) : (object)(float)Math.Round(v, 2));
        }

        private void AddDropdown(string label, ConfigEntryBase entry, bool indent)
        {
            var row = Row(dropdownTemplate, "Dropdown " + label, "Text", Indent(label, indent));
            foreach (var c in row.GetComponentsInChildren<LocalizeDropdownSemih>(true)) Object.DestroyImmediate(c);
            foreach (var c in row.GetComponentsInChildren<SetLanguageDropdown>(true)) Object.DestroyImmediate(c);
            var dropdown = row.GetComponentInChildren<Dropdown>(true);
            dropdown.onValueChanged = new Dropdown.DropdownEvent();
            var values = Enum.GetValues(entry.SettingType).Cast<object>().ToList();
            dropdown.ClearOptions();
            dropdown.AddOptions(values.Select(v => Pretty(v.ToString())).ToList());
            Track(dropdown, () => dropdown.SetValueWithoutNotify(values.IndexOf(entry.BoxedValue)), entry);
            dropdown.onValueChanged.AddListener(i => entry.BoxedValue = values[i]);
        }

        private void AddKeybind(string label, ConfigEntry<KeyboardShortcut> entry)
        {
            var modifiers = entry.Value.Modifiers.ToArray();
            string suffix = modifiers.Length > 0 ? $" ({string.Join(" + ", modifiers.Select(m => Pretty(m.ToString())))} +)" : "";
            var row = Row(keybindTemplate, "Keybind " + label, "BG Image/Identifier", Indent(label + suffix, true));
            var keybind = row.GetComponent<KeybindInterface>();
            keybind.identifier = "raft-qol." + label;
            keybind.Initialize(settings.controls.colorblock);
            keybind.altKey.button.gameObject.SetActive(false);
            Action refresh = () => keybind.Set(new Keybind(keybind.identifier, entry.Value.MainKey));
            refresh();
            box.Watch(entry, refresh);
            // KeybindInterface rebinds in place; copy the result back while the tab is open (modifiers are kept).
            box.Polls.Add(() =>
            {
                if (KeybindInterface.AssigningKey || keybind.Keybind.MainKey == entry.Value.MainKey) return;
                entry.Value = new KeyboardShortcut(keybind.Keybind.MainKey, entry.Value.Modifiers.ToArray());
            });
            box.Refreshes.Add(refresh);
            selectables.Add(keybind.mainKey.button);
        }

        // Shows the entry's value now, whenever it changes elsewhere (presets, config reload), and on every open.
        private void Track(Selectable widget, Action refresh, ConfigEntryBase entry)
        {
            refresh();
            box.Refreshes.Add(refresh);
            box.Watch(entry, refresh);
            Clean(widget);
        }

        // Remove the vanilla wiring that came with the clone: navigation into vanilla rows and EventTrigger entries
        // that close the window or scroll a vanilla tab. Re-add the two the game uses with a gamepad.
        private void Clean(Selectable widget)
        {
            selectables.Add(widget);
            var trigger = widget.GetComponent<EventTrigger>();
            if (trigger == null) return;
            trigger.triggers.Clear();
            AddTrigger(trigger, EventTriggerType.Cancel, _ => settings.Close());
            AddTrigger(trigger, EventTriggerType.UpdateSelected, e => scroll.OnUpdateSelected(e));
            triggers.Add(trigger);
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(e => action(e));
            trigger.triggers.Add(entry);
        }

        private void ChainNavigation()
        {
            for (int i = 0; i < selectables.Count; i++)
            {
                selectables[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? selectables[i - 1] : null,
                    selectOnDown = i < selectables.Count - 1 ? selectables[i + 1] : null,
                };
            }
            box.First = selectables.FirstOrDefault();
        }

        private void AddTabButton(GameObject panel)
        {
            var container = Find(settings.optionsCanvas.transform, "TabContainer");
            var button = Object.Instantiate(container.Find("AudioTab").gameObject, container, false);
            button.name = "RaftQoLTab";
            button.transform.SetAsLastSibling();
            var tabButton = button.GetComponent<TabButton>();
            tabButton.tabIndex = TabIndex;
            tabButton.tab = panel;
            tabButton.tabConsole = null;
            SetLabel(tabButton.text, "QoL");
            tabButton.OnPointerExit(forceExit: true);
            addedButton = button;
            // Five fixed-width buttons overflow the strip; let the layout share the width instead (~108 px each).
            var strip = container.GetComponent<HorizontalLayoutGroup>();
            tabStripControlledWidth = strip.childControlWidth;
            strip.childControlWidth = true;
            var group = settings.tabGroup;
            if (group.tabButtons != null && group.tabButtons.Length > 0 && !group.tabButtons.Contains(tabButton))
                group.tabButtons = group.tabButtons.Append(tabButton).ToArray();
        }

        // A primary term of "-" keeps the text as set, while the secondary (font) term still swaps in the CJK and
        // Cyrillic fonts.
        private static void SetLabel(Text text, string label)
        {
            var localize = text.GetComponent<Localize>();
            if (localize != null) localize.SetTerm("-");
            text.text = label;
        }

        private static string Indent(string label, bool indent) => indent ? "    " + label : label;

        // "VanillaPlus" -> "Vanilla Plus", "LeftShift" -> "Left Shift"
        private static string Pretty(string name)
        {
            var chars = new List<char>();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) chars.Add(' ');
                chars.Add(name[i]);
            }
            return new string(chars.ToArray());
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new InvalidOperationException(name + " not found under " + root.name);
        }
    }
}

// The tab's OptionsMenuBox: Settings calls SetFocus on whichever tab is selected, so the panel must carry one.
internal class QoLSettingsBox : OptionsMenuBox
{
    internal readonly List<Action> Refreshes = new();
    internal readonly List<Action> Polls = new();
    internal Selectable First;
    private readonly Dictionary<ConfigEntryBase, List<Action>> watched = new();

    internal void Watch(ConfigEntryBase entry, Action refresh)
    {
        if (watched.Count == 0) Plugin.ModConfig.SettingChanged += OnSettingChanged;
        if (!watched.TryGetValue(entry, out var list)) watched[entry] = list = new();
        list.Add(refresh);
    }

    private void OnSettingChanged(object sender, SettingChangedEventArgs e)
    {
        if (this == null)
        {
            Plugin.ModConfig.SettingChanged -= OnSettingChanged;
            return;
        }
        if (watched.TryGetValue(e.ChangedSetting, out var list))
            foreach (var refresh in list) refresh();
    }

    // Replaces OptionsMenuBox.OnEnable (Unity calls the most derived one).
    private new void OnEnable()
    {
        RefreshAll();
        SetFocus(SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance.Gamepad);
    }

    public override void SetFocus(bool gamepad)
    {
        if (gamepad && First != null) First.Select();
    }

    public override void Load(RGD_Settings _) => RefreshAll();

    // Settings saves every box when the window closes; the mod's settings live in raft-qol.cfg, never in RGD_Settings.
    public override void Save(RGD_Settings _) => Plugin.ModConfig.Save();

    public override void Update()
    {
        base.Update();
        foreach (var poll in Polls) poll();
    }

    private void RefreshAll()
    {
        foreach (var refresh in Refreshes) refresh();
    }
}
