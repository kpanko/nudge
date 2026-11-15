using Android.App;
using Android.Content;

namespace AlarmSpike;

/// <summary>
/// Receives the alarm itself and the notification's Complete, Snooze
/// and swipe intents. Not exported: only this app's own pending
/// intents reach it.
/// </summary>
[BroadcastReceiver(
    Name = "nudge.spike.alarms.AlarmReceiver",
    Exported = false)]
public sealed class AlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not { } action)
        {
            return;
        }

        var id = intent.GetIntExtra(Alarms.ExtraId, -1);
        try
        {
            if (action == Alarms.ActionFire)
            {
                Alarms.Fire(context, id, "alarm");
            }
            else
            {
                Alarms.OnAction(context, action, id);
            }
        }
        catch (Exception e)
        {
            SpikeLog.Write("error", id, $"{action}: {e}");
        }
    }
}

/// <summary>
/// Re-registers the alarms after a reboot, which clears them all.
/// After an update of the app it does the same; stock Android keeps
/// the alarms then, so this only matters where a manufacturer does
/// not, and it is harmless otherwise. QUICKBOOT_POWERON is what some
/// manufacturers send instead of BOOT_COMPLETED after a fast reboot.
/// </summary>
[BroadcastReceiver(
    Name = "nudge.spike.alarms.BootReceiver",
    Exported = true)]
[IntentFilter([
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    "android.intent.action.QUICKBOOT_POWERON",
])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not { } action)
        {
            return;
        }

        try
        {
            var name = action[(action.LastIndexOf('.') + 1)..];
            SpikeLog.Write("boot-broadcast", null, name);
            Alarms.RestoreAll(context, name);
        }
        catch (Exception e)
        {
            SpikeLog.Write("error", null, $"{action}: {e}");
        }
    }
}
