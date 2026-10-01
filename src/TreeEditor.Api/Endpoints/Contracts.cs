using TreeEditor.Domain.Planning;

namespace TreeEditor.Api.Endpoints;

/// <summary>A row of a roots or children page; <see cref="IsDeleted"/> is the effective state.</summary>
public sealed record NodeItemResponse(Guid Id, Guid? ParentId, string Value, bool IsDeleted, bool HasChildren);

/// <summary>A page of nodes; <see cref="NextAfter"/> is the cursor of the next page, if any.</summary>
public sealed record NodePageResponse(long Generation, IReadOnlyList<NodeItemResponse> Items, Guid? NextAfter);

/// <summary>A single node with its ancestors ordered from the parent up to the root.</summary>
public sealed record NodeResponse(
    long Generation,
    Guid Id,
    Guid? ParentId,
    string Value,
    long Version,
    bool IsDeleted,
    IReadOnlyList<Guid> AncestorIds);

/// <summary>Apply request body. New nodes are listed parent first.</summary>
public sealed record ApplyRequest(
    long? Generation,
    IReadOnlyList<CreatedNodeRequest>? Created,
    IReadOnlyList<UpdatedNodeRequest>? Updated,
    IReadOnlyList<Guid>? Deleted);

/// <summary>A node to create.</summary>
public sealed record CreatedNodeRequest(Guid Id, Guid? ParentId, string? Value);

/// <summary>A value edit based on <see cref="ExpectedVersion"/>.</summary>
public sealed record UpdatedNodeRequest(Guid Id, string? Value, long? ExpectedVersion);

/// <summary>A written node and its new version.</summary>
public sealed record VersionResponse(Guid Id, long Version);

/// <summary>Successful Apply; <see cref="Deleted"/> echoes every requested deletion, no-ops included.</summary>
public sealed record ApplyResponse(
    long Generation,
    IReadOnlyList<VersionResponse> Created,
    IReadOnlyList<VersionResponse> Updated,
    IReadOnlyList<Guid> Deleted);

/// <summary>One conflict; <see cref="Id"/> is null for <see cref="ConflictKind.StaleGeneration"/>.</summary>
public sealed record ConflictItemResponse(Guid? Id, ConflictKind Kind, long? CurrentVersion, Guid? ParentId);

/// <summary>Rejected Apply with every conflict found.</summary>
public sealed record ConflictResponse(long Generation, IReadOnlyList<ConflictItemResponse> Conflicts);

/// <summary>Result of a Reset.</summary>
public sealed record ResetResponse(long Generation);
