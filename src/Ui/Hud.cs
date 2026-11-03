using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace RaftQoL.Ui
{
    // The mod's HUD canvas: an overlay with no raycaster (clicks pass through). It is shown only in a world while the
    // game's HUD is visible (so it hides with the HUD toggle key) and the Settings window is closed.
    public static class Hud
    {
        private static Canvas canvas;
        private static Font font;
        private static readonly List<HudText> elements = new List<HudText>();

        internal static Canvas Canvas
        {
            get
            {
                if (canvas == null) Create();
                return canvas;
            }
        }

        // Same font as the game's HUD text when available (it covers the game's languages).
        internal static Font Font
        {
            get
            {
                if (font == null)
                {
                    var helper = ComponentManager<CanvasHelper>.Value;
                    font = helper != null && helper.dropText != null ? helper.dropText.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return font;
            }
        }

        private static void Create()
        {
            var go = new GameObject("RaftQoL_Hud");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // below the Settings window (4)
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        // For a loader that can unload the mod.
        internal static void Remove()
        {
            elements.Clear();
            if (canvas != null) Object.Destroy(canvas.gameObject);
            canvas = null;
            font = null;
        }

        internal static void Register(HudText element) => elements.Add(element);
        internal static void Unregister(HudText element) => elements.Remove(element);

        // Called from Runtime every frame.
        internal static void Update()
        {
            if (elements.Count == 0)
            {
                if (canvas != null && canvas.enabled) canvas.enabled = false;
                return;
            }
            var settings = ComponentManager<Settings>.Value;
            bool visible = Core.Game.InWorld && Core.Game.HudVisible && !(settings != null && settings.IsOpen);
            Canvas.enabled = visible;
            if (!visible)
            {
                font = null; // re-read after the next world load
                return;
            }
            foreach (var element in elements)
                element.Refresh();
        }
    }

    // A text element at a configurable screen position (0..1 from the bottom-left), with its own Show toggle.
    public sealed class HudText
    {
        private readonly ConfigEntry<bool> show;
        private readonly ConfigEntry<float> x;
        private readonly ConfigEntry<float> y;
        private readonly ConfigEntry<int> size;
        private Text text;

        public string Value { get; set; } = "";

        public HudText(ConfigFile config, string section, string name, float defaultX, float defaultY, int defaultSize = 22)
        {
            show = config.Bind(section, $"{name} shown", true, $"Show the {name} on the HUD.");
            x = config.Bind(section, $"{name} X", defaultX, new ConfigDescription("Horizontal position, 0 = left edge, 1 = right edge.", new AcceptableValueRange<float>(0f, 1f)));
            y = config.Bind(section, $"{name} Y", defaultY, new ConfigDescription("Vertical position, 0 = bottom edge, 1 = top edge.", new AcceptableValueRange<float>(0f, 1f)));
            size = config.Bind(section, $"{name} size", defaultSize, new ConfigDescription("Font size.", new AcceptableValueRange<int>(10, 60)));
        }

        public void Attach() => Hud.Register(this);

        public void Detach()
        {
            Hud.Unregister(this);
            if (text != null) Object.Destroy(text.gameObject);
            text = null;
        }

        internal void Refresh()
        {
            if (text == null) Create();
            bool visible = show.Value && !string.IsNullOrEmpty(Value);
            if (text.gameObject.activeSelf != visible) text.gameObject.SetActive(visible);
            if (!visible) return;
            text.text = Value;
            text.fontSize = size.Value;
            var anchor = new Vector2(x.Value, y.Value);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            text.alignment = anchor.x < 0.34f ? TextAnchor.MiddleLeft : anchor.x > 0.66f ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            if (text.font != Hud.Font) text.font = Hud.Font;
        }

        private void Create()
        {
            var go = new GameObject("HudText", typeof(RectTransform));
            go.transform.SetParent(Hud.Canvas.transform, false);
            text = go.AddComponent<Text>();
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            text.rectTransform.sizeDelta = new Vector2(600, 40);
        }
    }
}
