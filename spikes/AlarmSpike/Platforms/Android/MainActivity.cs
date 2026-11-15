using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace AlarmSpike;

[Activity(
    Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (savedInstanceState is null)
        {
            HandleIntent(Intent);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    /// <summary>
    /// The notification's Schedule button and its body open the app;
    /// both arrive here. Schedule is an activity intent because
    /// Android 12 and later refuse to start an activity from a
    /// receiver behind a notification.
    /// </summary>
    void HandleIntent(Intent? intent)
    {
        var id = intent?.GetIntExtra(Alarms.ExtraId, -1) ?? -1;
        switch (intent?.Action)
        {
            case Alarms.ActionSchedule:
                Alarms.OnAction(this, Alarms.ActionSchedule, id);
                break;
            case Alarms.ActionOpen when id > 0:
                SpikeLog.Write("tapped", id, "notification body; app opened");
                break;
        }
    }
}
