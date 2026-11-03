using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaftQoL.Ui;

// One-line feedback in the game's own notification style (the "QuestItem" toast, cloned so game toasts are untouched).
// Toasts queue with the game's: one at a time, about 3 s each.
public static class Toast
{
    private const string TextId = "raft-qol.Toast";
    private const string IconId = "raft-qol.ToastIcon";

    private static NotificationManager registeredOn;

    public static void Show(string text, Sprite icon = null)
    {
        var manager = ComponentManager<NotificationManager>.Value;
        if (manager == null)
        {
            Plugin.Log.LogInfo($"(toast, no HUD) {text}");
            return;
        }
        if (registeredOn != manager && !Register(manager))
        {
            Plugin.Log.LogInfo($"(toast, unavailable) {text}");
            return;
        }

#if DEBUG
        Plugin.Log.LogInfo($"Toast: {text}");
#endif
        var toast = (Notification_QuestItem)manager.ShowNotification(icon != null ? IconId : TextId);
        // The constructor prefixes "<amount>x "; overwrite it.
        var info = new Notification_QuestItem_Info("", 0, icon) { itemName = text };
        toast.infoQue.Enqueue(info);
    }

    // For a loader that can unload the mod: takes the cloned notifications back out of the game's manager.
    internal static void Remove()
    {
        if (registeredOn == null) return;
        var ours = registeredOn.notifications.FindAll(n => n.identifier == TextId || n.identifier == IconId);
        // The manager also holds queued and showing toasts; it would touch the destroyed clones on its next Update.
        registeredOn.notificationQue = new Queue<Notification>(registeredOn.notificationQue.Where(n => !ours.Contains(n)));
        if (ours.Contains(registeredOn.currentNotification)) registeredOn.currentNotification = null;
        foreach (var n in ours)
        {
            registeredOn.notifications.Remove(n);
            Object.Destroy(n.gameObject);
        }
        registeredOn = null;
    }

    // The NotificationManager lives in MainScene, so this runs once per world load.
    private static bool Register(NotificationManager manager)
    {
        var template = manager.notifications.Find(n => n.identifier == "QuestItem") as Notification_QuestItem;
        if (template == null)
        {
            Plugin.Log.LogWarning("QuestItem notification not found; toasts are logged instead");
            return false;
        }
        manager.notifications.Add(Clone(template, TextId, showIcon: false));
        manager.notifications.Add(Clone(template, IconId, showIcon: true));
        registeredOn = manager;
        return true;
    }

    // Hides the icon and its frame: the highest ancestor of the icon that doesn't also hold the text
    // (QuestItem: .../ItemBackgroundImage/ItemImage).
    private static void HideIcon(Notification_QuestItem toast)
    {
        var text = toast.text_itemName.transform;
        var branch = toast.image_itemImage.transform;
        while (branch.parent != null && branch.parent != toast.transform && !text.IsChildOf(branch.parent))
            branch = branch.parent;
        branch.gameObject.SetActive(false);
    }

    private static Notification_QuestItem Clone(Notification_QuestItem template, string id, bool showIcon)
    {
        var clone = Object.Instantiate(template, template.transform.parent);
        clone.name = id;
        clone.identifier = id;
        clone.infoQue = new();
        if (!showIcon) HideIcon(clone);
        return clone;
    }
}
