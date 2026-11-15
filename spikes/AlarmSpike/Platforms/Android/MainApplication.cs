using Android.App;
using Android.OS;
using Android.Runtime;

namespace AlarmSpike;

[Application]
public sealed class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <summary>
    /// Runs in every new process, including one Android starts only to
    /// deliver an alarm. The log line shows when that happened and how
    /// long MAUI's start-up took before the receiver could run.
    /// </summary>
    public override void OnCreate()
    {
        base.OnCreate();
        var sinceStart = SystemClock.ElapsedRealtime()
            - Process.StartElapsedRealtime;
        SpikeLog.Write(
            "process-start",
            null,
            $"pid {Process.MyPid()}, {sinceStart} ms after the process "
                + "was forked");
    }
}
