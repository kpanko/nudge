using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nudge.Web.Api;

namespace Nudge.Web.Tests.Api;

/// <summary>Which refusals of the database reach the client as a
/// 409, and which are left to be a 500.</summary>
public sealed class DomainTests
{
    private static DbUpdateException Failed(
        string message,
        int code = 19)
    {
        return new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new SqliteException($"SQLite Error {code}: '{message}'.", code));
    }

    [Fact]
    public void ContextNameInUse_IsAConflict_InWordsOfItsOwn()
    {
        var problem = Domain.Refused(
            Failed("UNIQUE constraint failed: Contexts.Name"));

        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.DoesNotContain("SQLite", problem.Detail);
        Assert.DoesNotContain("constraint", problem.Detail);
    }

    [Theory]
    [InlineData("CHECK constraint failed: CK_Items_Recurrence")]
    [InlineData("FOREIGN KEY constraint failed")]
    [InlineData("UNIQUE constraint failed: Items.Id")]
    [InlineData("NOT NULL constraint failed: Items.Title")]
    public void AnyOtherConstraint_IsLeftToBeAnError(string message)
    {
        Assert.Null(Domain.Refused(Failed(message)));
    }

    [Fact]
    public void AnythingButAConstraint_IsLeftToBeAnError()
    {
        Assert.Null(Domain.Refused(Failed("database is locked", 5)));
        Assert.Null(
            Domain.Refused(
                new DbUpdateException(
                    "Something else.",
                    new InvalidOperationException())));
    }
}
