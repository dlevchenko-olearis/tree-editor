using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Planning;

/// <summary>
/// Writes that apply a conflict-free change set, in execution order: inserts (parent first), versioned updates,
/// then deletion flags on <see cref="DeletionRoots"/>. <see cref="Deleted"/> lists every requested deletion,
/// including those that are no-ops because the node is already deleted.
/// </summary>
public sealed record ApplyPlan(
    IReadOnlyList<NewNode> Inserts,
    IReadOnlyList<NodeEdit> Updates,
    IReadOnlyList<NodeId> DeletionRoots,
    IReadOnlyList<NodeId> Deleted);
