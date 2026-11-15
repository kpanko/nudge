using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nudge.Web.Api;

/// <summary>
/// Calls into <c>Nudge.Core</c> and turns what it refuses into
/// problems: a value it rejects is a 400 for the input it came from,
/// an operation the item's state does not allow is a 409. Only the
/// call given is covered, so a bug elsewhere still surfaces as a
/// 500 instead of passing for the client's mistake.
/// </summary>
internal static class Domain
{
    /// <param name="field">The input the values came from, named as
    /// the client wrote it.</param>
    public static void Call(
        Action action,
        string field)
    {
        Call(
            () =>
            {
                action();
                return true;
            },
            field);
    }

    /// <inheritdoc cref="Call(Action, string)"/>
    public static T Call<T>(
        Func<T> function,
        string field)
    {
        try
        {
            return function();
        }
        catch (ArgumentException e)
        {
            throw ApiProblem.Invalid(field, Message(e));
        }
        catch (FormatException e)
        {
            throw ApiProblem.Invalid(field, e.Message);
        }
        catch (InvalidOperationException e)
        {
            throw ApiProblem.Conflict(e.Message);
        }
    }

    /// <summary>
    /// The problem for a save the database refused, or null if it
    /// was not a constraint: that would be a bug, not the client's
    /// doing, and is left to surface as a 500.
    /// </summary>
    public static ApiProblem? Refused(DbUpdateException e)
    {
        if (e.InnerException is not SqliteException
            {
                SqliteErrorCode: 19,
            } sqlite)
        {
            return null;
        }

        // The one constraint a correct request can run into.
        if (sqlite.Message.Contains(
                "UNIQUE constraint failed: Contexts.Name",
                StringComparison.Ordinal))
        {
            return ApiProblem.Conflict(
                "Another context already has this name, whatever the"
                + " case of its letters.");
        }

        return ApiProblem.Conflict(
            $"The database refused the change: {sqlite.Message}");
    }

    /// <summary>The message without the "(Parameter 'x')" that names
    /// a C# parameter the client has never heard of.</summary>
    private static string Message(ArgumentException e)
    {
        return e.ParamName is null
            ? e.Message
            : e.Message.Replace(
                $" (Parameter '{e.ParamName}')",
                "",
                StringComparison.Ordinal);
    }
}
