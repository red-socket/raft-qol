using System;
using BepInEx.Configuration;

namespace RaftQoL.Core
{
    // Read-only views of game state that are safe to call anywhere (main menu, scene loads, in world).
    public static class Game
    {
        // Set at the very end of LoadSceneManager.Load, after the fade-in. LoadSceneManager.IsLoadingScene is not
        // reliable for this: it is reset by LoadSceneManager.Awake mid-load.
        public static bool InWorld => LoadSceneManager.IsGameSceneLoaded && LocalPlayer != null;

        // The local player. Exists from Network_Player.OnPlayerCreated, a few seconds before the world is playable.
        public static Network_Player LocalPlayer
        {
            get
            {
                var player = ComponentManager<Network_Player>.Value;
                return player != null ? player : null;
            }
        }

        public static bool IsHost => Raft_Network.IsHost;

        public static bool MenuOpen => CanvasHelper.ActiveMenu != MenuType.None || ChatTextFieldController.IsChatWindowSelected;

        public static bool HudVisible
        {
            get
            {
                var canvas = ComponentManager<CanvasHelper>.Value;
                return canvas != null && canvas.UIState;
            }
        }

        // In a world, no menu or chat open, not rebinding a key.
        public static bool CanUseHotkeys => InWorld && !MenuOpen && !KeybindInterface.AssigningKey;

        public static event Action WorldLoaded;
        public static event Action WorldUnloaded;

        internal static void RaiseWorldLoaded() => WorldLoaded?.Invoke();
        internal static void RaiseWorldUnloaded() => WorldUnloaded?.Invoke();

        // True on the frame a hotkey is pressed, only while hotkeys may fire.
        public static bool Pressed(ConfigEntry<KeyboardShortcut> hotkey)
        {
            return CanUseHotkeys && hotkey.Value.MainKey != UnityEngine.KeyCode.None && hotkey.Value.IsDown();
        }
    }
}
