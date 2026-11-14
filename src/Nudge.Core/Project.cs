namespace Nudge.Core;

/// <summary>
/// A group of items working towards one outcome. Projects nest to any
/// depth; a project with no parent is at the top level.
/// </summary>
public sealed class Project
{
    private string _name;

    public Project(
        string name,
        Project? parent = null)
    {
        Id = Guid.CreateVersion7();
        _name = Core.Name.Clean(name, nameof(name));
        Parent = parent;
    }

    // For the persistence layer. It fills in Parent itself after
    // creating the project, so it cannot use a constructor that
    // takes one.
    private Project(string name)
        : this(name, null)
    {
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => _name = Core.Name.Clean(value, nameof(value));
    }

    public Project? Parent { get; private set; }

    /// <summary>When the project was deleted, or null if it has not
    /// been. A deleted project is kept, so that deleting it can be
    /// undone.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>This project's parent, grandparent and so on, nearest
    /// first.</summary>
    public IEnumerable<Project> Ancestors()
    {
        for (var p = Parent; p is not null; p = p.Parent)
        {
            yield return p;
        }
    }

    /// <summary>
    /// Moves this project, with everything below it, under
    /// <paramref name="parent"/>, or to the top level if null.
    /// </summary>
    /// <exception cref="InvalidOperationException">The new parent is
    /// this project or one of its own descendants, which would make
    /// a loop.</exception>
    public void MoveUnder(Project? parent)
    {
        if (parent is not null
            && (parent == this || parent.Ancestors().Contains(this)))
        {
            throw new InvalidOperationException(
                "A project cannot be moved under itself.");
        }

        Parent = parent;
    }

    /// <summary>
    /// Deletes the project by marking it. This marks the project
    /// alone: a project does not know what is under it. Whoever
    /// deletes one has to delete the projects and items under it in
    /// the same change, and the persistence layer has a method that
    /// does exactly that.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is
    /// already deleted.</exception>
    public void Delete(DateTimeOffset at)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException(
                "The project is already deleted.");
        }

        DeletedAt = at;
    }
}
