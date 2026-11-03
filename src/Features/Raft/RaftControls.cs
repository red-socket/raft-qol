using System.Linq;
using BepInEx.Configuration;
using RaftQoL.Core;
using RaftQoL.Ui;
using Steamworks;
using UnityEngine;

namespace RaftQoL.Features.Raft
{
    // NAV-1: hotkeys that work from anywhere on the raft: all engines on/off, reverse all engines, raise/drop all
    // stationary anchors, open/close all sails. Each object gets the same vanilla request its own interaction sends, so
    // the host handles it as usual and vanilla players see the change; it works as host or as a client.
    public sealed class RaftControls : Feature
    {
        public override string Id => "RaftControls";
        public override string Module => "Raft";
        public override string Name => "Raft controls";
        public override string Description => "Keys to switch all engines, reverse them, raise or drop all anchors and open or close all sails.";
        public override bool InVanillaPlus => true;

        private ConfigEntry<KeyboardShortcut> enginesKey;
        private ConfigEntry<KeyboardShortcut> reverseKey;
        private ConfigEntry<KeyboardShortcut> anchorsKey;
        private ConfigEntry<KeyboardShortcut> sailsKey;

        protected override void BindSettings()
        {
            enginesKey = Bind("Engines key", new KeyboardShortcut(KeyCode.K), "Turns all engines on, or off when they are all on.");
            reverseKey = Bind("Reverse key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftShift), "Reverses the direction of all engines.");
            anchorsKey = Bind("Anchors key", new KeyboardShortcut(KeyCode.J), "Raises all stationary anchors, or drops them when they are all up.");
            sailsKey = Bind("Sails key", new KeyboardShortcut(KeyCode.L), "Opens all sails, or closes them when they are all open.");
        }

        public override void Update()
        {
            // Check the Shift combination first: the plain key also counts as pressed while Shift is held.
            if (Game.Pressed(reverseKey)) ReverseEngines();
            else if (Game.Pressed(enginesKey)) ToggleEngines();
            if (Game.Pressed(anchorsKey)) ToggleAnchors();
            if (Game.Pressed(sailsKey)) ToggleSails();
        }

        internal static void ToggleEngines()
        {
            var engines = RaftVelocityManager.Motors.Where(m => m != null).ToList();
            if (engines.Count == 0) { Toast.Show("No engines on this raft"); return; }
            bool turnOn = engines.Any(m => !m.engineSwitchOn);
            foreach (var engine in engines.Where(m => m.engineSwitchOn != turnOn))
                engine.OnButtonPressed_ToggleEngine();
            Toast.Show($"{engines.Count} {Plural(engines.Count, "engine")} {(turnOn ? "on" : "off")}");
        }

        internal static void ReverseEngines()
        {
            var engines = RaftVelocityManager.Motors.Where(m => m != null).ToList();
            if (engines.Count == 0) { Toast.Show("No engines on this raft"); return; }
            foreach (var engine in engines)
                engine.OnButtonPressed_ChangeDirection();
            Toast.Show($"{engines.Count} {Plural(engines.Count, "engine")} reversed");
        }

        // Anchors driven by an Engine Controls block follow that block (Anchor_Stationary.Update), so they are left to it.
        internal static void ToggleAnchors()
        {
            var anchors = Object.FindObjectsOfType<Anchor_Stationary>()
                .Where(a => a.hasBeenPlaced && !(a.remoteControlled && EngineControls.AllEngineControls.Count > 0))
                .ToList();
            if (anchors.Count == 0) { Toast.Show("No anchors to control"); return; }
            bool raise = anchors.Any(a => a.AtBottom);
            int moved = 0;
            foreach (var anchor in anchors.Where(a => a.CanUse && a.AtBottom == raise))
            {
                // Same as Anchor_Stationary.OnIsRayed.
                var message = new Message_NetworkBehaviour(Messages.StationaryAnchorUse, anchor);
                if (Raft_Network.IsHost)
                {
                    anchor.network.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                    anchor.Use();
                }
                else
                {
                    anchor.network.SendP2P(anchor.network.HostID, message, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                }
                moved++;
            }
            Toast.Show(moved == 0 ? "Anchors are busy" : $"{(raise ? "Raising" : "Dropping")} {moved} {Plural(moved, "anchor")}");
        }

        internal static void ToggleSails()
        {
            var sails = Sail.AllSails.Where(s => s != null).ToList();
            if (sails.Count == 0) { Toast.Show("No sails on this raft"); return; }
            bool open = sails.Any(s => !s.open);
            foreach (var sail in sails.Where(s => s.open != open))
            {
                // Same as Sail.OnIsRayed.
                var message = new Message_NetworkBehaviour(open ? Messages.Sail_Open : Messages.Sail_Close, sail);
                if (Raft_Network.IsHost)
                {
                    if (open) sail.Open();
                    else sail.Close();
                    sail.network.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                }
                else
                {
                    sail.network.SendP2P(sail.network.HostID, message, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
                }
            }
            Toast.Show($"{sails.Count} {Plural(sails.Count, "sail")} {(open ? "opened" : "closed")}");
        }

        private static string Plural(int n, string word) => n == 1 ? word : word + "s";
    }
}
