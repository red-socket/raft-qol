using BepInEx.Configuration;
using RaftQoL.Core;
using RaftQoL.Ui;
using UnityEngine;

namespace RaftQoL.Features.Hud;

// NAV-5: a compass heading at the top of the screen, and, when you are away from the raft, how far and which way it
// is. "Away" means farther than a few metres from the nearest walkable raft block (RaftBounds.walkableBlocks).
public sealed class Compass : Feature
{
    public override string Id => "Compass";
    public override string Module => "HUD";
    public override string Name => "Compass and raft marker";
    public override string Description => "Shows your heading, and the distance and direction to the raft when you are away from it.";
    public override bool InVanillaPlus => true;

    private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    private HudText heading;
    private HudText marker;
    private ConfigEntry<float> awayDistance;
    private float nextUpdate;

    protected override void BindSettings()
    {
        heading = new HudText(Config, Section, "Compass", 0.5f, 0.86f, 22); // below the oxygen dial shown while swimming
        marker = new HudText(Config, Section, "Raft marker", 0.5f, 0.825f, 22);
        awayDistance = Bind("Away distance", 6f, "Show the raft marker when you are farther than this (metres) from the raft.", new AcceptableValueRange<float>(2f, 50f));
    }

    protected override void OnEnable()
    {
        heading.Attach();
        marker.Attach();
    }

    protected override void OnDisable()
    {
        heading.Detach();
        marker.Detach();
    }

    public override void Update()
    {
        if (Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.1f;
        var camera = Camera.main;
        if (!Game.InWorld || camera == null)
        {
            heading.Value = marker.Value = "";
            return;
        }
        float yaw = Mathf.Repeat(camera.transform.eulerAngles.y, 360f);
        heading.Value = $"{Points[Mathf.RoundToInt(yaw / 45f) % 8]}  {yaw:0}°";
        marker.Value = RaftMarker(camera.transform);
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

        var to = nearest - view.position;
        to.y = 0f;
        var forward = view.forward;
        forward.y = 0f;
        float angle = Vector3.SignedAngle(forward, to, Vector3.up);
        string direction = Mathf.Abs(angle) < 15f ? "ahead"
            : Mathf.Abs(angle) > 165f ? "behind"
            : $"{Mathf.Abs(angle):0}° {(angle < 0f ? "left" : "right")}";
        return $"Raft  {distance:0} m  {direction}";
    }
}
