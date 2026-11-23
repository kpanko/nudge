namespace Nudge.Web.UiTests;

/// <summary>
/// A test that drives a real browser. It runs only when
/// <c>NUDGE_PLAYWRIGHT=1</c>, set in CI after Chromium is installed,
/// and is reported as skipped otherwise, so <c>dotnet test</c> on a
/// machine without the browser stays green and says why.
/// </summary>
public sealed class PlaywrightFactAttribute : FactAttribute
{
    public const string Variable = "NUDGE_PLAYWRIGHT";

    public PlaywrightFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Set {Variable}=1 to run the browser tests; install"
                + " Chromium first with the built project's playwright.ps1"
                + " install chromium.";
        }
    }
}
