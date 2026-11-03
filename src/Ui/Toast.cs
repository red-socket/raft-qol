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

        var toast = (Notification_QuestItem)manager.ShowNotification(icon != null ? IconId : TextId);
        // The constructor prefixes "<amount>x "; overwrite it.
        var info = new Notification_QuestItem_Info("", 0, icon) { itemName = text };
        toast.infoQue.Enqueue(info);
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
