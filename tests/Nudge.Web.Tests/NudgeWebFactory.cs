using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nudge.Web.Tests;

/// <summary>
/// Runs the real app against a database file of its own in the
/// temporary folder, deleted afterwards, so tests neither share data
/// nor leave a database behind in the build output. The owner's time
/// zone is set to one with daylight saving. The app keeps the real
/// clock unless <see cref="Clock"/> is given before it starts.
/// </summary>
public sealed class NudgeWebFactory : WebApplicationFactory<Program>
{
    /// <summary>The owner's time zone in tests.</summary>
    public const string TimeZone = "America/New_York";

    public string DatabasePath { get; } = Path.Combine(
        Path.GetTempPath(),
        $"nudge-test-{Guid.NewGuid():N}.db");

    /// <summary>A clock for the app to take the time from instead
    /// of the real one, if set before the app starts.</summary>
    internal FixedClock? Clock { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:Nudge",
            $"Data Source={DatabasePath}");
        builder.UseSetting(OwnerTimeZone.Setting, TimeZone);
        if (Clock is { } clock)
        {
            builder.ConfigureTestServices(
                services => services.Replace(
                    ServiceDescriptor.Singleton<TimeProvider>(clock)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        // Closed connections stay open in the pool and would keep
        // the file locked on Windows.
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(DatabasePath + suffix);
            }
            catch (IOException)
            {
                // Still in use; the temporary folder gets cleaned.
            }
        }
    }
}
