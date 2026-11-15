namespace Nudge.Core;

/// <summary>Shared check for titles and names.</summary>
internal static class Name
{
    /// <summary>The text without surrounding white space.</summary>
    /// <exception cref="ArgumentException">Nothing is left.</exception>
    public static string Clean(
        string? text,
        string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, paramName);
        return text.Trim();
    }
}
