using TreeEditor.Domain.Nodes;

namespace TreeEditor.Application.Nodes;

/// <summary>Asks for a page of root nodes after the <paramref name="After"/> cursor.</summary>
public sealed record GetRootsQuery(NodeId? After, PageLimit Limit);

/// <summary>Asks for a page of the direct children of <paramref name="ParentId"/>.</summary>
public sealed record GetChildrenQuery(NodeId ParentId, NodeId? After, PageLimit Limit);

/// <summary>Asks for one node with its ancestor chain.</summary>
public sealed record GetNodeQuery(NodeId Id);

/// <summary>Asks for the current tree generation.</summary>
public sealed record GetGenerationQuery;

/// <summary>A page, or <c>null</c> when the parent does not exist, with the generation it was read in.</summary>
public sealed record NodePageResult(long Generation, NodePage? Page);

/// <summary>A node, or <c>null</c> when it does not exist, with the generation it was read in.</summary>
public sealed record NodeResult(long Generation, NodeDetails? Node);
