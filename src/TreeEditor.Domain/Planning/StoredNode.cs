using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Planning;

/// <summary>
/// A stored row as it is on disk: <paramref name="IsDeletedFlag"/> is set only on explicitly deleted nodes,
/// so it is not the effective deletion state.
/// </summary>
public sealed record StoredNode(NodeId Id, NodeId? ParentId, NodeVersion Version, bool IsDeletedFlag);
