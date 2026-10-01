using TreeEditor.Application.Abstractions;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Application.Changes;

/// <summary>A node written by an Apply, with the version it received.</summary>
public sealed record WrittenNode(NodeId Id, NodeVersion Version);

/// <summary>Result of an Apply. Only <see cref="Applied"/> is committed.</summary>
public abstract record ApplyOutcome : ICommandOutcome
{
    private ApplyOutcome(long generation) => Generation = generation;

    /// <inheritdoc />
    public long Generation { get; }

    /// <inheritdoc />
    public bool IsSuccess => this is Applied;

    /// <summary>Every change was persisted. <see cref="Deleted"/> also lists deletions that were no-ops.</summary>
    public sealed record Applied(
        long Generation,
        IReadOnlyList<WrittenNode> Created,
        IReadOnlyList<WrittenNode> Updated,
        IReadOnlyList<NodeId> Deleted) : ApplyOutcome(Generation);

    /// <summary>The change set does not fit the database state; nothing was persisted.</summary>
    public sealed record Conflicted(long Generation, IReadOnlyList<Conflict> Conflicts) : ApplyOutcome(Generation);

    /// <summary>The change set is structurally invalid; nothing was persisted.</summary>
    public sealed record Invalid(long Generation, IReadOnlyList<ChangeSetError> Errors) : ApplyOutcome(Generation);

    /// <summary>The idempotency key was already used for a different change set.</summary>
    public sealed record KeyMismatch(long Generation) : ApplyOutcome(Generation);
}
