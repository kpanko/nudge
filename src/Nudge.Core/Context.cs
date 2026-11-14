namespace Nudge.Core;

/// <summary>
/// A GTD context such as "phone" or "errands": where or with what an
/// item can be done. Items carry any number of them, like labels.
/// </summary>
public sealed class Context
{
    private string _name;

    public Context(string name)
    {
        Id = Guid.CreateVersion7();
        _name = Core.Name.Clean(name, nameof(name));
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => _name = Core.Name.Clean(value, nameof(value));
    }

    /// <summary>When the context was deleted, or null if it has not
    /// been. A deleted context is kept, so that deleting it can be
    /// undone.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Deletes the context by marking it. The items that carry it
    /// are not changed: they stop showing it, and show it again if
    /// the deletion is taken back.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context is
    /// already deleted.</exception>
    public void Delete(DateTimeOffset at)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException(
                "The context is already deleted.");
        }

        DeletedAt = at;
    }
}
