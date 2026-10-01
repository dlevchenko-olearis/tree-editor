using Microsoft.Extensions.DependencyInjection;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Nodes;
using TreeEditor.Infrastructure.Persistence;

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>Helpers that add test data through the infrastructure adapters.</summary>
public static class HarnessExtensions
{
    /// <summary>Inserts a chain of <paramref name="length"/> nodes under <paramref name="parent"/> and returns it top-down.</summary>
    public static async Task<List<NodeId>> AddChainAsync(this InfrastructureHarness harness, NodeId? parent, int length)
    {
        var nodes = new List<SeedNode>(length);
        var current = parent;
        for (var index = 0; index < length; index++)
        {
            var id = NodeId.Create(Guid.CreateVersion7());
            nodes.Add(new SeedNode(id, current, NodeValue.Create($"level {index}")));
            current = id;
        }

        await harness.InsertAsync(nodes);
        return nodes.Select(node => node.Id).ToList();
    }

    /// <summary>Inserts <paramref name="count"/> children under <paramref name="parent"/>.</summary>
    public static async Task<List<NodeId>> AddChildrenAsync(this InfrastructureHarness harness, NodeId parent, int count)
    {
        var nodes = Enumerable.Range(0, count)
            .Select(index => new SeedNode(NodeId.Create(Guid.CreateVersion7()), parent, NodeValue.Create($"child {index}")))
            .ToList();
        await harness.InsertAsync(nodes);
        return nodes.Select(node => node.Id).ToList();
    }

    /// <summary>Sets the deletion flag directly on <paramref name="id"/>.</summary>
    public static Task FlagDeletedAsync(this InfrastructureHarness harness, NodeId id) =>
        harness.Database.ExecuteAsync("UPDATE nodes SET is_deleted = true WHERE id = $1", id.Value);

    private static async Task InsertAsync(this InfrastructureHarness harness, IReadOnlyList<SeedNode> nodes)
    {
        await using var scope = harness.Scope();
        var writer = (PostgresTreeWriter)scope.ServiceProvider.GetRequiredService<ITreeWriter>();
        await writer.InsertSeedAsync(nodes, CancellationToken.None);
    }
}
