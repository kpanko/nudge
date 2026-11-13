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
}
