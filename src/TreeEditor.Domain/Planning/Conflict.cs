using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Planning;

/// <summary>Why a change does not fit the current database state.</summary>
public enum ConflictKind
{
    VersionMismatch,
    Deleted,
    NotFound,
    ParentDeleted,
    ParentNotFound,
    AlreadyExists,
    StaleGeneration,
}

/// <summary>
/// A change that does not fit the database state. <see cref="Id"/> is null only for
/// <see cref="ConflictKind.StaleGeneration"/>, which concerns the whole change set.
/// </summary>
public sealed record Conflict(NodeId? Id, ConflictKind Kind, NodeVersion? CurrentVersion, NodeId? ParentId)
{
    /// <summary>An edited node has another version than the one the edit was based on.</summary>
    public static Conflict VersionMismatch(NodeId id, NodeVersion currentVersion) =>
        new(id, ConflictKind.VersionMismatch, currentVersion, null);

    /// <summary>An edited node is effectively deleted.</summary>
    public static Conflict Deleted(NodeId id) => new(id, ConflictKind.Deleted, null, null);

    /// <summary>An edited or deleted node does not exist.</summary>
    public static Conflict NotFound(NodeId id) => new(id, ConflictKind.NotFound, null, null);

    /// <summary>The existing parent of a new node is effectively deleted.</summary>
    public static Conflict ParentDeleted(NodeId id, NodeId parentId) =>
        new(id, ConflictKind.ParentDeleted, null, parentId);

    /// <summary>The parent of a new node does not exist.</summary>
    public static Conflict ParentNotFound(NodeId id, NodeId parentId) =>
        new(id, ConflictKind.ParentNotFound, null, parentId);

    /// <summary>The id of a new node is already used by the described row.</summary>
    public static Conflict AlreadyExists(NodeId id, NodeId? existingParentId, NodeVersion? existingVersion) =>
        new(id, ConflictKind.AlreadyExists, existingVersion, existingParentId);

    /// <summary>The change set was built on an older tree generation.</summary>
    public static Conflict StaleGeneration() => new(null, ConflictKind.StaleGeneration, null, null);
}
