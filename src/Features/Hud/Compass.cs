using BepInEx.Configuration;
using RaftQoL.Core;
using RaftQoL.Ui;
using UnityEngine;

namespace RaftQoL.Features.Hud
{
    // NAV-5: a compass heading at the top of the screen; when you are away from the raft, how far and which way it is
    // ("away" = farther than a few metres from the nearest walkable raft block, RaftBounds.walkableBlocks); and, while a
    // receiver is powered and tuned to an island, the distance and direction to that island.
    public sealed class Compass : Feature
    {
        public override string Id => "Compass";
        public override string Module => "HUD";
        public override string Name => "Compass and raft marker";
        public override string Description => "Shows your heading, the distance and direction to the raft when you are away from it, and to the island your receiver is tuned to.";
        public override bool InVanillaPlus => true;

        private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private HudText heading;
        private HudText marker;
        private HudText destination;
        private ConfigEntry<float> awayDistance;
        private float nextUpdate;

        protected override void BindSettings()
        {
            heading = new HudText(Config, Section, "Compass", 0.5f, 0.86f, 22); // below the oxygen dial shown while swimming
            marker = new HudText(Config, Section, "Raft marker", 0.5f, 0.825f, 22);
            destination = new HudText(Config, Section, "Receiver destination", 0.5f, 0.79f, 22);
            awayDistance = Bind("Away distance", 6f, "Show the raft marker when you are farther than this (metres) from the raft.", new AcceptableValueRange<float>(2f, 50f));
        }

        protected override void OnEnable()
        {
            heading.Attach();
            marker.Attach();
            destination.Attach();
        }

        protected override void OnDisable()
        {
            heading.Detach();
            marker.Detach();
            destination.Detach();
        }

        public override void Update()
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 0.1f;
            var camera = Camera.main;
            if (!Game.InWorld || camera == null)
            {
                heading.Value = marker.Value = destination.Value = "";
                return;
            }
            float yaw = Mathf.Repeat(camera.transform.eulerAngles.y, 360f);
            heading.Value = $"{Points[Mathf.RoundToInt(yaw / 45f) % 8]}  {yaw:0}°";
            marker.Value = RaftMarker(camera.transform);
            destination.Value = ReceiverDestination(camera.transform);
        }

        // The island an active receiver is tuned to (the same chunk point its radar shows), from where you are.
        private static string ReceiverDestination(Transform view)
        {
            var chunks = ComponentManager<ChunkManager>.Value;
            if (chunks == null) return "";
            foreach (var receiver in Reciever.AllRecievers)
            {
                if (receiver == null || !receiver.active || receiver.CurrentFrequency == null) continue;
                var type = receiver.CurrentFrequency.chunkPointType;
                if (type == ChunkPointType.None || !receiver.CurrentFrequency.IsValid()) continue;
                var points = chunks.GetChunkPointsFromPointType(type);
                if (points == null || points.Count == 0) continue;
                var target = points[0].worldPosition;
                return $"{Pretty(type.ToString())}  {Horizontal(target - view.position).magnitude:0} m  {Direction(view, target)}";
            }
            return "";
        }

        private static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static string Direction(Transform view, Vector3 target)
        {
            float angle = Vector3.SignedAngle(Horizontal(view.forward), Horizontal(target - view.position), Vector3.up);
            return Mathf.Abs(angle) < 15f ? "ahead"
                : Mathf.Abs(angle) > 165f ? "behind"
                : $"{Mathf.Abs(angle):0}° {(angle < 0f ? "left" : "right")}";
        }

        // "Landmark_RadioTower" -> "Radio Tower"
        private static string Pretty(string name)
        {
            if (name.StartsWith("Landmark_")) name = name.Substring("Landmark_".Length);
            var chars = new System.Collections.Generic.List<char>();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) chars.Add(' ');
                chars.Add(name[i] == '_' ? ' ' : name[i]);
            }
            return new string(chars.ToArray());
        }

        private string RaftMarker(Transform view)
        {
            var bounds = ComponentManager<RaftBounds>.Value;
            if (bounds == null || bounds.walkableBlocks.Count == 0) return "";
            var position = Game.LocalPlayer.transform.position;
            Vector3 nearest = Vector3.zero;
            float best = float.MaxValue;
            foreach (var block in bounds.walkableBlocks)
            {
                if (block == null) continue;
                var offset = block.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < best)
                {
                    best = offset.sqrMagnitude;
                    nearest = block.transform.position;
                }
            }
            float distance = Mathf.Sqrt(best);
            if (best == float.MaxValue || distance < awayDistance.Value) return "";

            string direction = Direction(view, nearest);
            return $"Raft  {distance:0} m  {direction}";
        }
    }
}
