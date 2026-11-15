using System.Text;
using Microsoft.Maui.Layouts;
using Android.App.Usage;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace AlarmSpike;

/// <summary>
/// The spike's only page: buttons to schedule test alarms, what the
/// phone allows, the pending alarms and the event log. It only reads
/// and writes the files; the receivers never need it.
/// </summary>
public sealed class MainPage : ContentPage
{
    const int ShownLogLines = 300;

    readonly Label device = Text();
    readonly Label pending = Text();
    readonly Label log = Text(mono: true);
    readonly Entry note = new() { Placeholder = "Note, e.g. step 3" };
    readonly Button exactSettings;
    bool visible;

    static Context Droid => Platform.AppContext;

    public MainPage()
    {
        Title = "Nudge alarm spike";

        var schedule = new FlexLayout { Wrap = FlexWrap.Wrap };
        // 3 h is for the Doze step: Android does not enter deep Doze
        // when an alarm clock is due within the next hour.
        foreach (var minutes in new[] { 1, 5, 15, 60, 180 })
        {
            schedule.Add(Act(
                minutes < 60 ? $"In {minutes} min" : $"In {minutes / 60} h",
                () => Alarms.ScheduleNew(
                    Droid,
                    TimeSpan.FromMinutes(minutes))));
        }

        exactSettings = Act("Allow exact alarms", OpenExactAlarmSettings);

        var tools = new FlexLayout { Wrap = FlexWrap.Wrap };
        tools.Add(Act("Add note", AddNote));
        tools.Add(Act("Copy log", CopyLog));
        tools.Add(Act("Share log", ShareLog));
        tools.Add(Act(
            "Re-register all",
            () => Alarms.RestoreAll(Droid, "button")));
        tools.Add(Act("Cancel all", () => Alarms.CancelAll(Droid)));
        tools.Add(Act(
            "Notification settings",
            OpenNotificationSettings));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 8,
                Children =
                {
                    Heading("Nudge alarm spike"),
                    device,
                    exactSettings,
                    Heading("Schedule a test alarm"),
                    schedule,
                    Heading("Pending"),
                    pending,
                    Heading("Log (newest first)"),
                    note,
                    tools,
                    log,
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        visible = true;
        Refresh();
        Dispatcher.StartTimer(TimeSpan.FromSeconds(2), () =>
        {
            if (visible)
            {
                Refresh();
            }

            return visible;
        });

        try
        {
            await AskForNotifications();
        }
        catch (Exception e)
        {
            SpikeLog.Write("error", null, $"permission request: {e}");
        }

        Refresh();
    }

    protected override void OnDisappearing()
    {
        visible = false;
        base.OnDisappearing();
    }

    static async Task AskForNotifications()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        var status = await Permissions
            .CheckStatusAsync<Permissions.PostNotifications>();
        if (status == PermissionStatus.Granted)
        {
            return;
        }

        status = await Permissions
            .RequestAsync<Permissions.PostNotifications>();
        SpikeLog.Write("permission", null, $"notifications: {status}");
    }

    void Refresh()
    {
        var context = Droid;
        var canExact = Alarms.CanScheduleExact(context);
        var info = new StringBuilder();
        info.AppendLine(
            $"Android {Build.VERSION.Release} "
                + $"(API {(int)Build.VERSION.SdkInt})");
        info.AppendLine($"{Build.Manufacturer} {Build.Model}");
        info.AppendLine($"Exact alarms allowed: {Alarms.YesNo(canExact)}");
        info.AppendLine(
            "Notifications enabled: "
                + Alarms.YesNo(Alarms.NotificationsEnabled(context)));
        info.AppendLine(
            "Exempt from battery optimisation: "
                + Alarms.YesNo(
                    Alarms.IgnoringBatteryOptimisations(context)));
        info.AppendLine($"Standby bucket: {StandbyBucket(context)}");
        info.AppendLine(
            "System's next alarm clock: "
                + Alarms.NextSystemAlarm(context));
        info.Append($"Log file: {SpikeLog.FilePath}");
        device.Text = info.ToString();
        exactSettings.IsVisible = !canExact;

        var alarms = SpikeStore.All();
        pending.Text = alarms.Count == 0
            ? "None"
            : string.Join(
                "\n",
                alarms.Select(a =>
                    $"#{a.Id} {a.State}, due {Clock.Format(a.DueUtcMs)} "
                        + $"({a.Reason})"));

        var lines = SpikeLog.ReadAll()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        log.Text = string.Join(
            "\n",
            lines.Reverse().Take(ShownLogLines));
    }

    static string StandbyBucket(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return "n/a";
        }

        var usage = (UsageStatsManager)context.GetSystemService(
            Context.UsageStatsService)!;
        return usage.AppStandbyBucket.ToString();
    }

    void AddNote()
    {
        var text = note.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SpikeLog.Write("note", null, text);
        note.Text = "";
    }

    static async Task CopyLog()
    {
        await Clipboard.Default.SetTextAsync(SpikeLog.ReadAll());
        SpikeLog.Write("log-copied", null, "to the clipboard");
    }

    static async Task ShareLog()
    {
        var copy = Path.Combine(FileSystem.CacheDirectory, "events.log");
        File.Copy(SpikeLog.FilePath, copy, overwrite: true);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Alarm spike log",
            File = new ShareFile(copy, "text/plain"),
        });
    }

    static void OpenExactAlarmSettings()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return;
        }

        StartSettings(new Intent(
            Settings.ActionRequestScheduleExactAlarm,
            global::Android.Net.Uri.Parse(
                "package:" + Droid.PackageName)));
    }

    static void OpenNotificationSettings()
    {
        StartSettings(new Intent(Settings.ActionAppNotificationSettings)
            .PutExtra(Settings.ExtraAppPackage, Droid.PackageName));
    }

    static void StartSettings(Intent intent)
    {
        intent.AddFlags(ActivityFlags.NewTask);
        Droid.StartActivity(intent);
    }

    Button Act(string text, Action action) =>
        Act(text, () =>
        {
            action();
            return Task.CompletedTask;
        });

    Button Act(string text, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            Margin = new Thickness(0, 0, 8, 8),
        };
        button.Clicked += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                SpikeLog.Write("error", null, $"{text}: {e}");
            }

            Refresh();
        };
        return button;
    }

    static Label Heading(string text) =>
        new()
        {
            Text = text,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
        };

    static Label Text(bool mono = false) =>
        new()
        {
            FontSize = mono ? 11 : 14,
            FontFamily = mono ? "monospace" : null,
            LineBreakMode = LineBreakMode.WordWrap,
        };
}
