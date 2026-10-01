using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Changes;

/// <summary>A node to create under an existing node or under a node created earlier in the same change set.</summary>
public sealed record NewNode(NodeId Id, NodeId ParentId, NodeValue Value);

/// <summary>A new value for an existing node, based on the version the client last saw.</summary>
public sealed record NodeEdit(NodeId Id, NodeValue Value, NodeVersion ExpectedVersion);
