#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using RaftQoL.Core;
using RaftQoL.Ui;
using UnityEngine;

namespace RaftQoL.Dev;

// Debug builds only: drive the game from outside. Lines written to BepInEx/config/qol-dev-commands.txt run one by one
// ("wait N" pauses N seconds); replies go to the BepInEx log as "[dev]". World commands only touch worlds whose
// folder starts with "QoL-", so the player's own saves are never loaded or overwritten.
internal static class DevRig
{
    private const string TestPrefix = "QoL-";
    private static readonly string CommandFile = Path.Combine(Paths.ConfigPath, "qol-dev-commands.txt");
    private static readonly Queue<string> queue = new();
    private static float resumeAt;
    private static float nextPoll;

    internal static void Update()
    {
        float now = Time.unscaledTime;
        if (now < resumeAt) return;
        if (queue.Count == 0)
        {
            if (now < nextPoll) return;
            nextPoll = now + 0.5f;
            Read();
        }
        if (queue.Count == 0) return;

        string line = queue.Dequeue();
        Reply("> " + line);
        string[] args = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        try { Run(args); }
        catch (Exception e) { Plugin.Log.LogError($"[dev] {line}: {e}"); }
    }

    private static void Read()
    {
        if (!File.Exists(CommandFile)) return;
        try
        {
            foreach (string line in File.ReadAllLines(CommandFile))
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                    queue.Enqueue(line.Trim());
            File.Delete(CommandFile);
        }
        catch (IOException)
        {
            // Still being written; next poll.
        }
    }

    private static void Reply(string text) => Plugin.Log.LogInfo("[dev] " + text);

    private static void Run(string[] args)
    {
        var network = ComponentManager<Raft_Network>.Value;
        switch (args[0])
        {
            case "wait":
                resumeAt = Time.unscaledTime + (args.Length > 1 ? float.Parse(args[1]) : 1f);
                break;
            case "new":
            {
                // new <name> [mode]: creates and hosts a single-player test world named QoL-<name>.
                string name = TestPrefix + (args.Length > 1 ? args[1] : "Test");
                var mode = args.Length > 2 ? (GameMode)Enum.Parse(typeof(GameMode), args[2], true) : GameMode.Normal;
                GameModeValueManager.SelectCurrentGameMode(mode);
                GameManager.IsInNewGame = true;
                GameManager.FriendlyFire = false;
                SaveAndLoad.CurrentGameFileName = name;
                network.HostGame(RequestJoinAuthSetting.ALLOW_NONE, "");
                Reply($"creating '{name}' ({mode})");
                break;
            }
            case "load":
            {
                // load <name>: hosts the latest save of the test world QoL-<name>.
                string name = TestPrefix + (args.Length > 1 ? args[1] : "Test");
                var dir = new DirectoryInfo(Path.Combine(SaveAndLoad.WorldPath, name));
                var game = dir.Exists ? SaveAndLoad.GetLatestRGDGameFromBackupFolders(dir, out _) : null;
                if (game == null)
                {
                    Reply($"no loadable world '{name}' in {SaveAndLoad.WorldPath}");
                    break;
                }
                SaveAndLoad.WorldToLoad = game;
                SaveAndLoad.CurrentGameFileName = name;
                GameModeValueManager.SelectCurrentGameMode(game.mode);
                GameManager.IsInNewGame = false;
                GameManager.FriendlyFire = false;
                network.HostGame(RequestJoinAuthSetting.ALLOW_NONE, "");
                Reply($"loading '{name}' ({game.mode})");
                break;
            }
            case "save":
                RequireTestWorld();
                ComponentManager<SaveAndLoad>.Value.SaveGame(true);
                Reply("saved");
                break;
            case "menu":
                network.LeaveGame(DisconnectReason.SelfDisconnected, SceneName.Lobby, saveGame: IsTestWorld() && !args.Contains("nosave"));
                Reply("leaving to the main menu");
                break;
            case "quit":
                Reply("quitting");
                if (Raft_Network.WorldHasBeenRecieved)
                    network.LeaveGame(DisconnectReason.SelfDisconnected, SceneName.Exit, saveGame: IsTestWorld() && !args.Contains("nosave"));
                else
                    Application.Quit();
                break;
            case "shot":
            {
                // shot [name]: screenshot into BepInEx/config/qol-shots/ (written at the end of the frame).
                string dir = Path.Combine(Paths.ConfigPath, "qol-shots");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, (args.Length > 1 ? args[1] : DateTime.Now.ToString("HHmmss")) + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Reply("screenshot " + path);
                break;
            }
            case "toast":
                Toast.Show(args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "Test toast");
                Reply("toast queued");
                break;
            case "icontoast":
            {
                var item = ItemManager.GetItemByName(args.Length > 1 ? args[1] : "Plank");
                Toast.Show($"{item?.settings_Inventory.DisplayName ?? "?"} toast", item?.settings_Inventory.Sprite);
                Reply("icon toast queued");
                break;
            }
            case "preset":
                Presets.Active.Value = (Preset)Enum.Parse(typeof(Preset), args[1], true);
                Reply($"preset {Presets.Active.Value}");
                break;
            case "feature":
            {
                // feature <id> on|off
                var feature = Feature.All.First(f => string.Equals(f.Id, args[1], StringComparison.OrdinalIgnoreCase));
                feature.Enabled.Value = args[2] == "on";
                Reply($"{feature.Id} enabled={feature.Enabled.Value} active={feature.Active}");
                break;
            }
            case "openmenu":
                ComponentManager<CanvasHelper>.Value.OpenMenuCloseOther((MenuType)Enum.Parse(typeof(MenuType), args[1], true));
                Reply($"ActiveMenu={CanvasHelper.ActiveMenu}");
                break;
            case "closemenus":
                ComponentManager<CanvasHelper>.Value.CloseAllMenus();
                Reply($"ActiveMenu={CanvasHelper.ActiveMenu}");
                break;
            case "settings":
            {
                // settings open [tab] | close | tab <n> | scroll <0..1>
                var s = ComponentManager<Settings>.Value;
                switch (args.Length > 1 ? args[1] : "open")
                {
                    case "open":
                        if (!s.IsOpen) s.Open();
                        if (args.Length > 2) s.tabGroup.SelectTab(int.Parse(args[2]));
                        break;
                    case "close":
                        if (s.IsOpen) s.Close();
                        break;
                    case "tab":
                        // The same path as a mouse click (which also un-highlights the previous tab).
                        s.tabGroup.tabButtons.First(b => b.tabIndex == int.Parse(args[2])).OnPointerClick();
                        break;
                    case "scroll":
                        s.tabGroup.SelectedTabButton.Tab.GetComponent<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = float.Parse(args[2]);
                        break;
                }
                Reply($"settings open={s.IsOpen} tab={s.tabGroup.SelectedTabButton?.tabIndex} buttons={s.tabGroup.tabButtons?.Length}");
                break;
            }
            case "hud":
                ComponentManager<CanvasHelper>.Value.SetUIState(args.Length < 2 || args[1] == "on");
                Reply($"HUD visible={Game.HudVisible}");
                break;
            case "status":
                Reply($"InWorld={Game.InWorld} host={Game.IsHost} menu={CanvasHelper.ActiveMenu} hotkeys={Game.CanUseHotkeys} hud={Game.HudVisible} " +
                      $"preset={Presets.Active.Value} world='{SaveAndLoad.CurrentGameFileName}'");
                foreach (var f in Feature.All)
                    Reply($"  {f.Section}: enabled={f.Enabled.Value} active={f.Active} patched={f.PatchedMethodCount}");
                Reply($"  methods patched by raft-qol: {Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Any(o => o.StartsWith(Plugin.Guid)))}");
                break;
            default:
                Reply("unknown command " + args[0]);
                break;
        }
    }

    private static bool IsTestWorld() => SaveAndLoad.CurrentGameFileName?.StartsWith(TestPrefix) == true;

    private static void RequireTestWorld()
    {
        if (!IsTestWorld()) throw new InvalidOperationException($"'{SaveAndLoad.CurrentGameFileName}' is not a {TestPrefix} test world");
    }
}
#endif
