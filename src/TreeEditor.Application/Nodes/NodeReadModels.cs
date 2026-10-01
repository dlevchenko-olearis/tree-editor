using TreeEditor.Domain.Nodes;

namespace TreeEditor.Application.Nodes;

/// <summary>A row of a roots or children page. <see cref="IsDeleted"/> is the effective deletion state.</summary>
public sealed record NodeListItem(NodeId Id, NodeId? ParentId, string Value, bool IsDeleted, bool HasChildren);

/// <summary>A page of nodes ordered by id. <see cref="NextAfter"/> is set only when another page exists.</summary>
public sealed record NodePage(IReadOnlyList<NodeListItem> Items, NodeId? NextAfter);

/// <summary>A single node with its ancestor chain, ordered from the parent up to the root.</summary>
public sealed record NodeDetails(
    NodeId Id,
    NodeId? ParentId,
    string Value,
    NodeVersion Version,
    bool IsDeleted,
    IReadOnlyList<NodeId> AncestorIds);

/// <summary>Data read together with the tree generation it belongs to.</summary>
public sealed record TreeRead<T>(long Generation, T Value);
