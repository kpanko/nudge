namespace Nudge.Core;

/// <summary>Which GTD list an open item is on.</summary>
public enum ItemState
{
    /// <summary>Captured but not yet processed.</summary>
    Inbox,

    /// <summary>Something the owner can act on.</summary>
    NextAction,

    /// <summary>Blocked on someone or something else.</summary>
    WaitingFor,

    /// <summary>Not committed to; looked at in the weekly review.</summary>
    Someday,
}
