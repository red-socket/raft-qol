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
    private static Storage_Small lastChest;
    private static Block lastBlock;

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
                // new <name> [mode] [mp]: creates and hosts a test world named QoL-<name> (open to Steam friends with mp).
                string name = TestPrefix + (args.Length > 1 ? args[1] : "Test");
                var mode = args.Length > 2 && args[2] != "mp" ? (GameMode)Enum.Parse(typeof(GameMode), args[2], true) : GameMode.Normal;
                GameModeValueManager.SelectCurrentGameMode(mode);
                GameManager.IsInNewGame = true;
                GameManager.FriendlyFire = false;
                SaveAndLoad.CurrentGameFileName = name;
                network.HostGame(args.Contains("mp") ? RequestJoinAuthSetting.ALLOW_FRIENDS : RequestJoinAuthSetting.ALLOW_NONE, "");
                Reply($"creating '{name}' ({mode}){(args.Contains("mp") ? " for friends" : "")}");
                break;
            }
            case "load":
            {
                // load <name> [mp]: hosts the latest save of the test world QoL-<name> (open to Steam friends with mp).
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
                network.HostGame(args.Contains("mp") ? RequestJoinAuthSetting.ALLOW_FRIENDS : RequestJoinAuthSetting.ALLOW_NONE, "");
                Reply($"loading '{name}' ({game.mode}){(args.Contains("mp") ? " for friends" : "")}");
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
            case "row":
            {
                // row <row GameObject name, '_' for spaces> <value>: sets a QoL tab widget as a click would.
                var s = ComponentManager<Settings>.Value;
                var panel = s.optionsCanvas.GetComponentsInChildren<Transform>(true).First(t => t.name == "RaftQoL");
                string name = args[1].Replace('_', ' ');
                var row = panel.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
                var toggle = row.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
                var slider = row.GetComponentInChildren<UnityEngine.UI.Slider>(true);
                var dropdown = row.GetComponentInChildren<UnityEngine.UI.Dropdown>(true);
                // Dropdown first: its item template contains a Toggle.
                if (dropdown != null) dropdown.value = int.Parse(args[2]);
                else if (slider != null) slider.value = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                else if (toggle != null) toggle.isOn = bool.Parse(args[2]);
                Reply($"{name} set to {args[2]}");
                break;
            }
            case "give":
                // give <item> <n>: into the local player's inventory
            {
                // One stack at a time: AddItem puts the whole amount into one empty slot, oversizing the stack.
                int stack = ItemManager.GetItemByName(args[1]).settings_Inventory.StackSize;
                for (int left = int.Parse(args[2]); left > 0; left -= stack)
                    Game.LocalPlayer.Inventory.AddItem(args[1], Mathf.Min(stack, left));
                Reply($"gave {args[2]} {args[1]} (stack {stack})");
                break;
            }
            case "chest":
            case "place":
            {
                // chest [dx dz] | place <item> [dx dz]: host only; creates a block next to the player as the local
                // player's own build (the same calls as the host's own placement, so the build cost is drained).
                RequireTestWorld();
                bool chest = args[0] == "chest";
                var item = ItemManager.GetItemByName(chest ? "Placeable_Storage_Small" : args[1]);
                int at = chest ? 1 : 2;
                var pivot = SingletonGeneric<GameManager>.Singleton.lockedPivot;
                var local = pivot.InverseTransformPoint(Game.LocalPlayer.transform.position);
                var offset = args.Length > at + 1 ? new Vector3(float.Parse(args[at]), 0f, float.Parse(args[at + 1])) : new Vector3(1.5f, 0f, 0f);
                // The host's own placement (BlockCreator.cs:412-414), without the build-cursor checks.
                var creator = Game.LocalPlayer.BlockCreator;
                var block = creator.CreateBlock(item, local + offset, Vector3.zero, chest ? DPS.Floor : DPS.Default, -1, replicating: false,
                    SaveAndLoad.GetUniqueObjectIndex(), SaveAndLoad.GetUniqueObjectIndex(), NetworkUpdateManager.GetUniqueBehaviourIndex());
                Game.LocalPlayer.Network.RPC(new Message_BlockCreator_PlaceBlock(Messages.BlockCreator_PlaceBlock, creator, item.UniqueIndex, block.ObjectIndex,
                    block.networkedBehaviour != null ? block.networkedBehaviour.ObjectIndex : 0u, block.networkedBehaviour != null ? block.networkedBehaviour.BehaviourIndex : 0u,
                    block.transform.localPosition, block.transform.localEulerAngles, -1, chest ? DPS.Floor : DPS.Default),
                    Target.Other, Steamworks.EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                lastBlock = block;
                if (chest) lastChest = block as Storage_Small;
                Reply($"placed {item.UniqueName} at {block.transform.position} storages={StorageManager.allStorages.Count}");
                break;
            }
            case "expand":
            {
                // expand <r>: fills a (2r+1)² grid of foundations around the raft's first foundation (aligned to it)
                RequireTestWorld();
                var origin = FirstFoundation().transform.localPosition;
                int r = int.Parse(args[1]);
                var item = ItemManager.GetItemByName("Block_Foundation");
                var taken = new HashSet<Vector2Int>(ComponentManager<RaftBounds>.Value.walkableBlocks.Where(b => b != null)
                    .Select(b => Grid(b.transform.localPosition - origin)));
                int added = 0;
                for (int gx = -r; gx <= r; gx++)
                    for (int gz = -r; gz <= r; gz++)
                    {
                        if (taken.Contains(new Vector2Int(gx, gz))) continue;
                        PlaceBlock(item, origin + new Vector3(gx * 1.5f, 0f, gz * 1.5f), DPS.Default);
                        added++;
                    }
                Reply($"added {added} foundations");
                break;
            }
            case "placeat":
            case "chestat":
            {
                // placeat <item> <gx> <gz> | chestat <gx> <gz>: on the foundation at grid cell (gx, gz), floor height
                RequireTestWorld();
                bool chest = args[0] == "chestat";
                var item = ItemManager.GetItemByName(chest ? "Placeable_Storage_Small" : args[1]);
                int at = chest ? 1 : 2;
                var origin = FirstFoundation().transform.localPosition;
                var pivot = SingletonGeneric<GameManager>.Singleton.lockedPivot;
                var position = new Vector3(origin.x + float.Parse(args[at]) * 1.5f, origin.y, origin.z + float.Parse(args[at + 1]) * 1.5f);
                // Floor height: the top of the foundation under the cell (the player's height floats things).
                var above = pivot.TransformPoint(position) + Vector3.up * 3f;
                if (Physics.Raycast(above, Vector3.down, out var hit, 6f, LayerMasks.MASK_Block))
                    position.y = pivot.InverseTransformPoint(hit.point).y;
                var block = PlaceBlock(item, position, DPS.Floor);
                lastBlock = block;
                if (chest) lastChest = block as Storage_Small;
                Reply($"placed {item.UniqueName} at cell {args[at]},{args[at + 1]}");
                break;
            }
            case "home":
            {
                // Puts the player on the raft's first foundation.
                var first = FirstFoundation();
                Game.LocalPlayer.transform.position = first.transform.position + Vector3.up * 1.5f;
                Reply("player on the raft");
                break;
            }
            case "aim":
            {
                // aim last|<GameMode type name> [dy] [index]: turns the player to look at the last placed block or at
                // the index-th object of a type (MouseLook targets are set too, so the view stays put).
                Component target = args[1] == "last" ? lastBlock
                    : UnityEngine.Object.FindObjectsOfType(typeof(Player).Assembly.GetType(args[1]))
                        .OfType<Component>().OrderBy(c => (c.transform.position - Game.LocalPlayer.transform.position).sqrMagnitude)
                        .ElementAtOrDefault(args.Length > 3 ? int.Parse(args[3]) : 0);
                if (target == null) { Reply("aim: nothing to aim at"); break; }
                Aim(target.transform.position + Vector3.up * (args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.5f));
                Reply($"aiming at {target.name}");
                break;
            }
            case "aimdir":
            {
                // aimdir <yaw> <pitch>: absolute view direction in degrees (pitch up is negative)
                var cam = Camera.main.transform;
                var dir = Quaternion.Euler(float.Parse(args[2]), float.Parse(args[1]), 0f) * Vector3.forward;
                Aim(cam.position + dir * 10f);
                Reply($"aiming {args[1]} {args[2]}");
                break;
            }
            case "aimhome":
                Aim(FirstFoundation().transform.position + Vector3.up * 0.5f);
                Reply("aiming at the raft");
                break;
            case "cook":
            {
                // cook <item> [n]: puts raw food into the last placed (or nearest) cooking stand and adds fuel
                var stand = (lastBlock != null ? lastBlock.GetComponent<Block_CookingStand>() : null) ?? UnityEngine.Object.FindObjectOfType<Block_CookingStand>();
                var item = ItemManager.GetItemByName(args[1]);
                int n = args.Length > 2 ? int.Parse(args[2]) : 1;
                for (int i = 0; i < n; i++)
                {
                    var slots = stand.GetCookingSlotsForItem(item);
                    if (slots == null || slots.Length == 0) break;
                    slots[0].StartCooking(item, slots);
                }
                stand.fuel.AddFuel(3);
                Reply($"cooking {n} {args[1]} on {stand.name}");
                break;
            }
            case "desc":
                Game.LocalPlayer.Inventory.SetItemDescription(ItemManager.GetItemByName(args[1]));
                Reply($"description of {args[1]}");
                break;
            case "select":
            {
                var inventory = Game.LocalPlayer.Inventory;
                inventory.hotbar.SetSelectedSlotIndex(int.Parse(args[1]));
                inventory.hotbar.ReselectCurrentSlot();
                Reply($"selected hotbar slot {args[1]}");
                break;
            }
            case "canbuild":
            {
                var item = ItemManager.GetItemByName(args[1]);
                Reply($"can build {args[1]}: {Game.LocalPlayer.BlockCreator.HasEnoughResourcesToBuild(item.settings_recipe.NewCost)}");
                break;
            }
            case "reinforce":
            {
                var hammer = Game.LocalPlayer.GetComponentInChildren<Hammer>(true);
                // Hammer.Start assigns playerNetwork only once the hammer has been held.
                if (hammer.playerNetwork == null) hammer.playerNetwork = Game.LocalPlayer;
                Reply($"reinforce {lastBlock?.name}: {hammer.ReinforceBlock(lastBlock)}");
                break;
            }
            case "axe":
            {
                // Removes the last placed block as the local player's axe does (Axe.DestroyBlock refunds, then removes).
                var axe = Game.LocalPlayer.GetComponentInChildren<Axe>(true);
                Reply($"axed {lastBlock?.name}: {axe.DestroyBlock(lastBlock, updateRaftBounds: true)}");
                break;
            }
            case "bulk":
            {
                // bulk <n>: n more crafts of the selected recipe, as a held Shift/Ctrl would add.
                int made = Features.Crafting.BulkCraft.Repeat(ComponentManager<CraftingMenu>.Value, int.Parse(args[1]));
                Reply($"bulk crafted {made}");
                break;
            }
            case "eat":
            {
                // eat <item>: Stats.Consume (as ConsumeComponent.OnUse on the host) plus the slot's use, as when eating.
                var player = Game.LocalPlayer;
                var slot = player.Inventory.allSlots.First(x => !x.IsEmpty && x.itemInstance.UniqueName == args[1]);
                var item = slot.itemInstance.baseItem;
                player.Stats.Consume(item);
                slot.IncrementUses(-1);
                Reply($"ate {args[1]}");
                break;
            }
            case "hooks":
                foreach (var hook in Game.LocalPlayer.GetComponentsInChildren<Hook>(true))
                    Reply($"hook {hook.name}: pullSpeed={hook.pullSpeed} gatherTime={hook.gatherTime}");
                break;
            case "engines": Features.Raft.RaftControls.ToggleEngines(); Reply(RaftState()); break;
            case "reverse": Features.Raft.RaftControls.ReverseEngines(); Reply(RaftState()); break;
            case "anchors": Features.Raft.RaftControls.ToggleAnchors(); Reply(RaftState()); break;
            case "sails": Features.Raft.RaftControls.ToggleSails(); Reply(RaftState()); break;
            case "raft": Reply(RaftState()); break;
            case "velocity":
            {
                var raft = ComponentManager<global::Raft>.Value;
                Reply($"raft speed={raft.body.velocity.magnitude:F3} wind={raft.speed:F2} motorStrength={RaftVelocityManager.motorStrength}+{RaftVelocityManager.extraMotorStrength} weight={RaftWeightManager.FoundationWeight} state={RaftVelocityManager.MotorWheelWeightStrength}");
                break;
            }
            case "wheel":
            {
                // wheel [degrees]: turn every placed wheel (host Rotate, as a player turning it), then report angles
                var wheels = UnityEngine.Object.FindObjectsOfType<SteeringWheel>();
                if (args.Length > 1) foreach (var w in wheels) w.Rotate(float.Parse(args[1]));
                Reply("wheels: " + string.Join(", ", wheels.Select(w => w.targetLocalEulerZ.ToString("F1"))));
                break;
            }
            case "lookinfo":
            {
                // Points the camera at the last placed block for this frame and reads the look-at line.
                var target = lastBlock.transform.position + Vector3.up * float.Parse(args.Length > 1 ? args[1] : "0.5");
                Camera.main.transform.LookAt(target);
                Reply($"look-at: '{Features.Hud.LookAtInfo.Describe()}'");
                break;
            }
            case "tooltip":
                Reply($"{args[1]}: '{Features.Hud.ItemTooltips.Details(ItemManager.GetItemByName(args[1]))}'");
                break;
            case "fuelengines":
                foreach (var motor in RaftVelocityManager.Motors.Where(m => m != null))
                    motor.FuelTank.ModifyTank(null, float.Parse(args.Length > 1 ? args[1] : "10"));
                Reply(RaftState());
                break;
            case "tp":
            {
                // tp <dx> <dz>: moves the local player by an offset (world axes)
                var player = Game.LocalPlayer;
                player.transform.position += new Vector3(float.Parse(args[1]), 0f, float.Parse(args[2]));
                Reply($"player at {player.transform.position}");
                break;
            }
            case "put":
            {
                // put <slot index> <item> <n>: sets one player slot directly (hotbar is 0..9)
                var inventory = Game.LocalPlayer.Inventory;
                var slot = inventory.allSlots[int.Parse(args[1])];
                slot.SetItem(ItemManager.GetItemByName(args[2]), int.Parse(args[3]));
                inventory.hotbar.ReselectCurrentSlot();
                Reply($"slot {args[1]} = {args[3]} {args[2]} (selected={inventory.hotbar.IsSelectedHotSlot(slot)})");
                break;
            }
            case "take":
                // take <item> <n>: Inventory.RemoveItem on the player (the multi-slot removal quests and vending use)
                Game.LocalPlayer.Inventory.RemoveItem(args[1], int.Parse(args[2]));
                Reply($"took {args[2]} {args[1]}, player has {Game.LocalPlayer.Inventory.GetItemCount(args[1])}");
                break;
            case "slots":
            {
                var inventory = Game.LocalPlayer.Inventory;
                Reply("slots: " + string.Join(" ", inventory.allSlots.Select((x, i) => x.IsEmpty ? null : $"{i}:{x.itemInstance.UniqueName}x{x.itemInstance.Amount}").Where(x => x != null)));
                break;
            }
            case "clearinv":
                Game.LocalPlayer.Inventory.Clear();
                Reply("inventory cleared");
                break;
            case "chestfill":
                // chestfill <item> <n>: into the last placed chest (host copy, then pushed like a close)
                lastChest.GetInventoryReference().AddItem(args[1], int.Parse(args[2]));
                Reply($"chest has {lastChest.GetInventoryReference().GetItemCount(args[1])} {args[1]}");
                break;
            case "count":
            {
                var name = args[1];
                int inChests = StorageManager.allStorages.Sum(s => s.GetInventoryReference().GetItemCount(name));
                Reply($"{name}: player={Game.LocalPlayer.Inventory.GetItemCount(name)} chests={inChests}");
                break;
            }
            case "recipe":
            {
                var menu = ComponentManager<CraftingMenu>.Value;
                menu.SelectRecipe(ItemManager.GetItemByName(args[1]), forceSelect: true);
                Reply($"recipe {args[1]} selected");
                break;
            }
            case "craftbtn":
                Reply($"craft button interactable={ComponentManager<CraftingMenu>.Value.selectedRecipeBox.craftButton.interactable}");
                break;
            case "craft":
                ComponentManager<CraftingMenu>.Value.CraftItem();
                Reply("crafted");
                break;
            case "respawn":
            {
                // respawn: the bed respawn a death leads to (BedManager.cs:96), with clearInventory true
                var player = Game.LocalPlayer;
                player.PlayerScript.StartRespawn(BedManager.FindClosestBedToPlayer(player), clearInventory: true);
                Reply("respawned");
                break;
            }
            case "inv":
            {
                var inventory = Game.LocalPlayer.Inventory;
                int Items(IEnumerable<Slot> slots) => slots.Where(s => !s.IsEmpty).Sum(s => s.itemInstance.Amount);
                Reply($"hotbar={Items(inventory.allSlots.Take(inventory.hotslotCount))} rest={Items(inventory.allSlots.Skip(inventory.hotslotCount))} " +
                      $"hotslotCount={inventory.hotslotCount} slots={inventory.allSlots.Count} types={string.Join(",", inventory.allSlots.GroupBy(s => s.slotType).Select(g => g.Key + ":" + g.Count()))} " +
                      $"equipment={inventory.equipSlots.Count(s => !s.IsEmpty)} mode={GameManager.GameMode}");
                break;
            }
            case "deposit":
                // The QuickDeposit hotkey action, without the key.
                Feature.All.OfType<Features.Storage.QuickDeposit>().First().Deposit();
                Reply("deposit run");
                break;
            case "alloff":
            case "allon":
            {
                // alloff: every feature off (vanilla check); allon: back to the VanillaPlus preset
                if (args[0] == "allon") Presets.Apply(Preset.VanillaPlus);
                else foreach (var f in Feature.All) f.Enabled.Value = false;
                Reply($"enabled: {string.Join(", ", Feature.All.Where(f => f.Active).Select(f => f.Id))}; methods patched by raft-qol: " +
                      Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Any(o => o.StartsWith(Plugin.Guid))));
                break;
            }
            case "cfg":
            {
                // cfg <section> <key, '_' for spaces> <value>
                var definition = new BepInEx.Configuration.ConfigDefinition(args[1], args[2].Replace('_', ' '));
                var entry = Plugin.ModConfig[definition];
                entry.SetSerializedValue(args[3]);
                Reply($"[{args[1]}] {definition.Key} = {entry.BoxedValue}");
                break;
            }
            case "hud":
                ComponentManager<CanvasHelper>.Value.SetUIState(args.Length < 2 || args[1] == "on");
                Reply($"HUD visible={Game.HudVisible}");
                break;
            case "players":
            {
                var players = UnityEngine.Object.FindObjectsOfType<Network_Player>();
                Reply($"players: {string.Join(", ", players.Select(p => $"{p.name} local={p.IsLocalPlayer}"))}");
                break;
            }
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

    private static string RaftState()
    {
        var engines = RaftVelocityManager.Motors.Where(m => m != null).ToList();
        var anchors = UnityEngine.Object.FindObjectsOfType<Anchor_Stationary>().Where(a => a.hasBeenPlaced).ToList();
        return $"engines on={engines.Count(m => m.engineSwitchOn)}/{engines.Count} reversed={engines.Count(m => m.rotatesForward == false)} " +
               $"sails open={Sail.AllSails.Count(x => x != null && x.open)}/{Sail.AllSails.Count} " +
               $"anchors down={anchors.Count(a => a.AtBottom)}/{anchors.Count} dropping={anchors.Count(a => a.DroppingAnchor)} raft anchored={ComponentManager<Raft>.Value.IsAnchored}";
    }

    // Points the view at a world position: body yaw (mouseLookXScript) and camera pitch (mouseLookYScript), both the
    // current and the target rotation, so mouse look keeps it.
    private static void Aim(Vector3 target)
    {
        var script = Game.LocalPlayer.PlayerScript;
        var cam = Camera.main.transform;
        var dir = target - cam.position;
        var x = script.mouseLookXScript;
        var flat = x.isChild && x.transform.parent != null ? x.transform.parent.InverseTransformDirection(dir) : dir;
        float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        x.targetRotX = yaw;
        x.rotX = yaw;
        var y = script.mouseLookYScript;
        float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
        y.targetRotY = Mathf.Clamp(pitch, y.minimumY, y.maximumY);
        y.rotY = y.targetRotY;
    }

    private static Block FirstFoundation() =>
        ComponentManager<RaftBounds>.Value.walkableBlocks.Where(b => b != null).OrderBy(b => b.ObjectIndex).First();

    private static Vector2Int Grid(Vector3 offset) => new Vector2Int(Mathf.RoundToInt(offset.x / 1.5f), Mathf.RoundToInt(offset.z / 1.5f));

    // The host's own placement (BlockCreator.cs:412-414), without the build-cursor checks.
    private static Block PlaceBlock(Item_Base item, Vector3 localPosition, DPS dps)
    {
        var creator = Game.LocalPlayer.BlockCreator;
        var block = creator.CreateBlock(item, localPosition, Vector3.zero, dps, -1, replicating: false,
            SaveAndLoad.GetUniqueObjectIndex(), SaveAndLoad.GetUniqueObjectIndex(), NetworkUpdateManager.GetUniqueBehaviourIndex());
        Game.LocalPlayer.Network.RPC(new Message_BlockCreator_PlaceBlock(Messages.BlockCreator_PlaceBlock, creator, item.UniqueIndex, block.ObjectIndex,
            block.networkedBehaviour != null ? block.networkedBehaviour.ObjectIndex : 0u, block.networkedBehaviour != null ? block.networkedBehaviour.BehaviourIndex : 0u,
            block.transform.localPosition, block.transform.localEulerAngles, -1, dps),
            Target.Other, Steamworks.EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        return block;
    }

    private static bool IsTestWorld() => SaveAndLoad.CurrentGameFileName?.StartsWith(TestPrefix) == true;

    private static void RequireTestWorld()
    {
        if (!IsTestWorld()) throw new InvalidOperationException($"'{SaveAndLoad.CurrentGameFileName}' is not a {TestPrefix} test world");
    }
}
#endif
