using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;

namespace AlarmSpike;

/// <summary>
/// Everything an alarm goes through: scheduling with
/// <c>AlarmManager.SetAlarmClock</c>, firing, the notification and
/// its actions, swipes and the restore after a reboot. Only Android
/// APIs and the files in <see cref="SpikeStore"/> are used, so the
/// receivers work in a process that has just been started for them.
/// </summary>
public static class Alarms
{
    public const string ActionFire = "nudge.spike.alarms.FIRE";
    public const string ActionComplete = "nudge.spike.alarms.COMPLETE";
    public const string ActionSnooze = "nudge.spike.alarms.SNOOZE";
    public const string ActionSwiped = "nudge.spike.alarms.SWIPED";
    public const string ActionSchedule = "nudge.spike.alarms.SCHEDULE";
    public const string ActionOpen = "nudge.spike.alarms.OPEN";
    public const string ExtraId = "alarm_id";

    const string ChannelId = "spike_alarms";

    public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan RenotifyAfter =
        TimeSpan.FromSeconds(30);

    /// <summary>A fire later than this is marked LATE in the log.</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromSeconds(10);

    public static void ScheduleNew(Context context, TimeSpan fromNow)
    {
        var minutes = fromNow.TotalMinutes.ToString(
            CultureInfo.InvariantCulture);
        var alarm = new SpikeAlarm(
            SpikeStore.NextId(),
            Clock.NowMs + (long)fromNow.TotalMilliseconds,
            AlarmState.Pending,
            $"test, {minutes} min");
        SpikeStore.Put(alarm);
        Register(context, alarm, "scheduled");
    }

    /// <summary>Hands the alarm to AlarmManager and logs it.</summary>
    static void Register(Context context, SpikeAlarm alarm, string evt)
    {
        var manager = AlarmManagerOf(context);
        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && !manager.CanScheduleExactAlarms())
        {
            SpikeLog.Write(
                "schedule-failed",
                alarm.Id,
                "exact alarms are not allowed for this app");
            return;
        }

        try
        {
            var info = new AlarmManager.AlarmClockInfo(
                alarm.DueUtcMs,
                OpenAppIntent(context, ActionOpen, 0));
            manager.SetAlarmClock(info, FireIntent(context, alarm.Id));
            SpikeLog.Write(
                evt,
                alarm.Id,
                $"due {Clock.Format(alarm.DueUtcMs)} ({alarm.Reason})");
        }
        catch (Java.Lang.SecurityException e)
        {
            SpikeLog.Write("schedule-failed", alarm.Id, e.Message ?? "");
        }
    }

    /// <summary>The alarm went off (or was overdue at boot).</summary>
    public static void Fire(Context context, int id, string via)
    {
        var alarm = SpikeStore.Get(id);
        if (alarm is null)
        {
            SpikeLog.Write("fired-unknown", id, $"via {via}; not stored");
            return;
        }

        var delay = Clock.NowMs - alarm.DueUtcMs;
        var late = delay > LateAfter.TotalMilliseconds ? " LATE" : "";
        SpikeLog.Write(
            "fired",
            id,
            $"due {Clock.Format(alarm.DueUtcMs)}, delay "
                + delay.ToString("+0;-0", CultureInfo.InvariantCulture)
                + $" ms{late}, via {via} ({alarm.Reason}); "
                + DeviceState(context));

        var showing = alarm with { State = AlarmState.Showing };
        SpikeStore.Put(showing);
        Show(context, showing, "shown");
    }

    static void Show(Context context, SpikeAlarm alarm, string evt)
    {
        var notifications = NotificationManagerOf(context);
        EnsureChannel(notifications);

        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcLockIdleAlarm)
            .SetContentTitle($"Spike test alarm #{alarm.Id}")
            .SetContentText(
                "Invented test reminder, due "
                + Clock.Format(alarm.DueUtcMs)[11..19])
            .SetCategory(Notification.CategoryReminder)
            .SetWhen(alarm.DueUtcMs)
            .SetShowWhen(true)
            .SetAutoCancel(false)
            .SetContentIntent(OpenAppIntent(context, ActionOpen, alarm.Id))
            .SetDeleteIntent(ActionIntent(context, ActionSwiped, alarm.Id))
            .AddAction(Button(
                "Complete",
                ActionIntent(context, ActionComplete, alarm.Id)))
            .AddAction(Button(
                "Schedule",
                OpenAppIntent(context, ActionSchedule, alarm.Id)))
            .AddAction(Button(
                "Snooze",
                ActionIntent(context, ActionSnooze, alarm.Id)))
            .Build();
        notifications.Notify(alarm.Id, notification);

        var channel = notifications.GetNotificationChannel(ChannelId);
        SpikeLog.Write(
            evt,
            alarm.Id,
            "posted; notifications enabled: "
                + YesNo(notifications.AreNotificationsEnabled())
                + $", channel importance: {channel?.Importance}");
    }

    /// <summary>Complete, Snooze, swipe, or Schedule (from the app).</summary>
    public static void OnAction(Context context, string action, int id)
    {
        var alarm = SpikeStore.Get(id);
        var name = action[(action.LastIndexOf('.') + 1)..]
            .ToLowerInvariant();
        if (alarm is null || alarm.State != AlarmState.Showing)
        {
            SpikeLog.Write(
                name + "-ignored",
                id,
                alarm is null ? "not stored" : "not showing");
            return;
        }

        var notifications = NotificationManagerOf(context);
        switch (action)
        {
            case ActionComplete:
                notifications.Cancel(id);
                SpikeStore.Remove(id);
                SpikeLog.Write("complete", id, "dismissed and removed");
                break;

            case ActionSchedule:
                notifications.Cancel(id);
                SpikeStore.Remove(id);
                SpikeLog.Write(
                    "schedule",
                    id,
                    "app opened; dismissed and removed (the real app "
                        + "would ask for a new time here)");
                break;

            case ActionSnooze:
                notifications.Cancel(id);
                SpikeLog.Write(
                    "snooze",
                    id,
                    $"dismissed; again in {SnoozeFor.TotalMinutes} min");
                Requeue(context, alarm, SnoozeFor, "snoozed");
                break;

            case ActionSwiped:
                SpikeLog.Write(
                    "swiped",
                    id,
                    "a swipe is not an answer; again in "
                        + $"{RenotifyAfter.TotalSeconds} s");
                Requeue(context, alarm, RenotifyAfter, "re-notify");
                break;
        }
    }

    static void Requeue(
        Context context,
        SpikeAlarm alarm,
        TimeSpan after,
        string reason)
    {
        var again = alarm with
        {
            DueUtcMs = Clock.NowMs + (long)after.TotalMilliseconds,
            State = AlarmState.Pending,
            Reason = reason,
        };
        SpikeStore.Put(again);
        Register(context, again, "scheduled");
    }

    /// <summary>
    /// After a reboot or an update: registers every pending alarm
    /// again, fires at once those whose time has passed, and puts back
    /// the notifications that were up and unanswered.
    /// </summary>
    public static void RestoreAll(Context context, string why)
    {
        var alarms = SpikeStore.All();
        SpikeLog.Write("restore", null, $"{why}; {alarms.Count} stored");
        foreach (var alarm in alarms)
        {
            if (alarm.State == AlarmState.Showing)
            {
                Show(context, alarm, "re-shown");
            }
            else if (alarm.DueUtcMs <= Clock.NowMs)
            {
                Fire(context, alarm.Id, why);
            }
            else
            {
                Register(context, alarm, "re-registered");
            }
        }
    }

    public static void CancelAll(Context context)
    {
        var manager = AlarmManagerOf(context);
        var notifications = NotificationManagerOf(context);
        foreach (var alarm in SpikeStore.All())
        {
            manager.Cancel(FireIntent(context, alarm.Id));
            notifications.Cancel(alarm.Id);
            SpikeStore.Remove(alarm.Id);
            SpikeLog.Write("cancelled", alarm.Id, "by the Cancel button");
        }
    }

    public static bool CanScheduleExact(Context context) =>
        !OperatingSystem.IsAndroidVersionAtLeast(31)
        || AlarmManagerOf(context).CanScheduleExactAlarms();

    public static bool NotificationsEnabled(Context context)
    {
        var notifications = NotificationManagerOf(context);
        var channel = notifications.GetNotificationChannel(ChannelId);
        return notifications.AreNotificationsEnabled()
            && channel?.Importance != NotificationImportance.None;
    }

    /// <summary>The next alarm clock the system knows, from any app.</summary>
    public static string NextSystemAlarm(Context context)
    {
        var next = AlarmManagerOf(context).NextAlarmClock;
        if (next is null)
        {
            return "none";
        }

        var owner = next.ShowIntent?.CreatorPackage == context.PackageName
            ? "this app"
            : next.ShowIntent?.CreatorPackage ?? "unknown app";
        return $"{Clock.Format(next.TriggerTime)} ({owner})";
    }

    public static string YesNo(bool value) => value ? "yes" : "no";

    static void EnsureChannel(NotificationManager notifications)
    {
        if (notifications.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(
            ChannelId,
            "Test alarms",
            NotificationImportance.High)
        {
            Description = "Alarms scheduled from the spike's page",
        };
        notifications.CreateNotificationChannel(channel);
    }

    static Notification.Action Button(string title, PendingIntent intent) =>
        new Notification.Action.Builder(null, title, intent).Build();

    static PendingIntent FireIntent(Context context, int id) =>
        ActionIntent(context, ActionFire, id);

    static PendingIntent ActionIntent(Context context, string action, int id)
    {
        var intent = new Intent(context, typeof(AlarmReceiver))
            .SetAction(action)
            .PutExtra(ExtraId, id);
        return PendingIntent.GetBroadcast(
            context,
            id,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    static PendingIntent OpenAppIntent(
        Context context,
        string action,
        int id)
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetAction(action)
            .PutExtra(ExtraId, id)
            .AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(
            context,
            id,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    static AlarmManager AlarmManagerOf(Context context) =>
        (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    static NotificationManager NotificationManagerOf(Context context) =>
        (NotificationManager)context.GetSystemService(
            Context.NotificationService)!;

    public static bool IgnoringBatteryOptimisations(Context context) =>
        PowerManagerOf(context)
            .IsIgnoringBatteryOptimizations(context.PackageName);

    /// <summary>
    /// What the phone was doing when the alarm arrived, so the log
    /// shows whether it really was in Doze, on screen or saving power.
    /// </summary>
    static string DeviceState(Context context)
    {
        var power = PowerManagerOf(context);
        var light = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? YesNo(power.IsDeviceLightIdleMode)
            : "n/a";
        return $"doze: {YesNo(power.IsDeviceIdleMode)}, "
            + $"light doze: {light}, "
            + $"screen on: {YesNo(power.IsInteractive)}, "
            + $"battery saver: {YesNo(power.IsPowerSaveMode)}";
    }

    static PowerManager PowerManagerOf(Context context) =>
        (PowerManager)context.GetSystemService(Context.PowerService)!;
}
