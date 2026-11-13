using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Nudge.Web.Tests;

/// <summary>
/// Runs the real app against a database file of its own in the
/// temporary folder, deleted afterwards, so tests neither share data
/// nor leave a database behind in the build output.
/// </summary>
public sealed class NudgeWebFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(
        Path.GetTempPath(),
        $"nudge-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:Nudge",
            $"Data Source={DatabasePath}");
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
