using TreeEditor.Application.Abstractions;
using TreeEditor.Domain.Changes;

namespace TreeEditor.Application.Changes;

/// <summary>Applies a validated change set built on tree generation <paramref name="Generation"/>.</summary>
public sealed record ApplyChangesCommand(Guid IdempotencyKey, long Generation, ChangeSet ChangeSet) : ICommand
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> LogState => new Dictionary<string, object?>
    {
        ["IdempotencyKey"] = IdempotencyKey,
        ["Generation"] = Generation,
        ["Created"] = ChangeSet.Created.Count,
        ["Updated"] = ChangeSet.Updated.Count,
        ["Deleted"] = ChangeSet.Deleted.Count,
    };
}
