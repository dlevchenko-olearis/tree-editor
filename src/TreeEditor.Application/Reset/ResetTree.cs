using TreeEditor.Application.Abstractions;

namespace TreeEditor.Application.Reset;

/// <summary>Restores the sample tree and starts a new tree generation.</summary>
public sealed record ResetTreeCommand : ICommand
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> LogState { get; } = new Dictionary<string, object?>();
}

/// <summary>The tree was reset; <see cref="Generation"/> is the new generation.</summary>
public sealed record ResetOutcome(long Generation) : ICommandOutcome
{
    /// <inheritdoc />
    public bool IsSuccess => true;
}
