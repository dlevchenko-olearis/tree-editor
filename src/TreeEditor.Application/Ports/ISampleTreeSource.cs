using TreeEditor.Domain.Nodes;

namespace TreeEditor.Application.Ports;

/// <summary>A node of the sample tree.</summary>
public sealed record SeedNode(NodeId Id, NodeId? ParentId, NodeValue Value);

/// <summary>Provides the sample tree used for seeding and Reset.</summary>
public interface ISampleTreeSource
{
    /// <summary>The sample nodes, each listed after its parent.</summary>
    IReadOnlyList<SeedNode> GetNodes();
}
