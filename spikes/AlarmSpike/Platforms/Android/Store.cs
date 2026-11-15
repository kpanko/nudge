using System.Globalization;
using System.Text;

namespace AlarmSpike;

public enum AlarmState
{
    /// <summary>Registered with AlarmManager, waiting to fire.</summary>
    Pending,

    /// <summary>Fired; its notification is up and not answered.</summary>
    Showing,
}

/// <summary>One test alarm, as kept on the device.</summary>
public sealed record SpikeAlarm(
    int Id,
    long DueUtcMs,
    AlarmState State,
    string Reason);

/// <summary>
/// Times as the log writes them: local time with the offset and
/// milliseconds, so a line is unambiguous on its own.
/// </summary>
public static class Clock
{
    public static long NowMs =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static string Format(long utcMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(utcMs)
            .ToLocalTime()
            .ToString(
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture);
}

/// <summary>
/// The pending alarms, in a small tab-separated file in the app's
/// private storage. Every receiver reads it afresh, so nothing
/// depends on a page or service being alive.
/// </summary>
public static class SpikeStore
{
    static readonly object Gate = new();

    static string Folder =>
        Android.App.Application.Context.FilesDir!.AbsolutePath;

    static string AlarmsPath => Path.Combine(Folder, "alarms.tsv");

    static string NextIdPath => Path.Combine(Folder, "next-id.txt");

    public static IReadOnlyList<SpikeAlarm> All()
    {
        lock (Gate)
        {
            return Read();
        }
    }

    public static SpikeAlarm? Get(int id)
    {
        lock (Gate)
        {
            return Read().FirstOrDefault(a => a.Id == id);
        }
    }

    public static void Put(SpikeAlarm alarm)
    {
        lock (Gate)
        {
            var alarms = Read().Where(a => a.Id != alarm.Id).ToList();
            alarms.Add(alarm);
            Write(alarms);
        }
    }

    public static void Remove(int id)
    {
        lock (Gate)
        {
            Write(Read().Where(a => a.Id != id).ToList());
        }
    }

    public static int NextId()
    {
        lock (Gate)
        {
            var next = 1;
            if (File.Exists(NextIdPath))
            {
                next = int.Parse(
                    File.ReadAllText(NextIdPath).Trim(),
                    CultureInfo.InvariantCulture);
            }

            WriteAtomically(
                NextIdPath,
                (next + 1).ToString(CultureInfo.InvariantCulture));
            return next;
        }
    }

    static List<SpikeAlarm> Read()
    {
        if (!File.Exists(AlarmsPath))
        {
            return [];
        }

        var alarms = new List<SpikeAlarm>();
        foreach (var line in File.ReadAllLines(AlarmsPath))
        {
            var parts = line.Split('\t');
            if (parts.Length != 4)
            {
                continue;
            }

            alarms.Add(new SpikeAlarm(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                long.Parse(parts[1], CultureInfo.InvariantCulture),
                Enum.Parse<AlarmState>(parts[2]),
                parts[3]));
        }

        return alarms.OrderBy(a => a.DueUtcMs).ToList();
    }

    static void Write(List<SpikeAlarm> alarms)
    {
        var text = new StringBuilder();
        foreach (var a in alarms)
        {
            text.Append(CultureInfo.InvariantCulture, $"{a.Id}\t");
            text.Append(CultureInfo.InvariantCulture, $"{a.DueUtcMs}\t");
            text.Append(CultureInfo.InvariantCulture, $"{a.State}\t");
            text.Append(a.Reason.Replace('\t', ' ')).Append('\n');
        }

        WriteAtomically(AlarmsPath, text.ToString());
    }

    static void WriteAtomically(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// The event log: the evidence for the checklist. One line per event,
/// appended and never rewritten. It lives in the app's folder on
/// shared storage so that <c>adb pull</c> can read it from a Release
/// build.
/// </summary>
public static class SpikeLog
{
    static readonly object Gate = new();

    public static string FilePath
    {
        get
        {
            var context = Android.App.Application.Context;
            var folder = context.GetExternalFilesDir(null)
                ?? context.FilesDir!;
            return Path.Combine(folder.AbsolutePath, "events.log");
        }
    }

    /// <summary>Appends one line: time | event | alarm | details.</summary>
    public static void Write(string evt, int? alarmId, string details)
    {
        var id = alarmId is { } n
            ? "#" + n.ToString(CultureInfo.InvariantCulture)
            : "-";
        var line = $"{Clock.Format(Clock.NowMs)} | {evt} | {id} | "
            + details.ReplaceLineEndings(" ") + "\n";
        lock (Gate)
        {
            File.AppendAllText(FilePath, line);
        }

        Android.Util.Log.Info("AlarmSpike", line.TrimEnd());
    }

    public static string ReadAll()
    {
        lock (Gate)
        {
            return File.Exists(FilePath)
                ? File.ReadAllText(FilePath)
                : "";
        }
    }
}
