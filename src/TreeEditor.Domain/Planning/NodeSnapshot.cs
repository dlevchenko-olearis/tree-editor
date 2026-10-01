using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Planning;

/// <summary>Database state of one node touched by a change set, taken before the change set is applied.</summary>
public sealed record NodeSnapshot(NodeId Id, bool Exists, NodeId? ParentId, NodeVersion? Version, bool IsDeleted)
{
    /// <summary>Snapshot of an id that has no row.</summary>
    public static NodeSnapshot Missing(NodeId id) => new(id, false, null, null, false);

    /// <summary>Snapshot of a stored node; <paramref name="isDeleted"/> is the effective deletion state.</summary>
    public static NodeSnapshot Existing(NodeId id, NodeId? parentId, NodeVersion version, bool isDeleted) =>
        new(id, true, parentId, version, isDeleted);
}
