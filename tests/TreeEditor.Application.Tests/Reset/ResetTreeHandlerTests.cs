using TreeEditor.Application.Ports;
using TreeEditor.Application.Reset;
using TreeEditor.Application.Tests.Fakes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Application.Tests.Reset;

public sealed class ResetTreeHandlerTests
{
    [Fact]
    public async Task Replaces_the_tree_with_the_sample_and_returns_the_new_generation()
    {
        var tree = new InMemoryTree { Generation = 7 };
        tree.Add(null, "user data");
        var root = NodeId.Create(Guid.CreateVersion7());
        var sample = new FixedSource(
        [
            new SeedNode(root, null, NodeValue.Create("root")),
            new SeedNode(NodeId.Create(Guid.CreateVersion7()), root, NodeValue.Create("child")),
        ]);

        var outcome = await new ResetTreeHandler(sample, tree).HandleAsync(new ResetTreeCommand(), CancellationToken.None);

        Assert.Equal(8, outcome.Generation);
        Assert.True(outcome.IsSuccess);
        Assert.Same(sample.Nodes, tree.Replacement);
    }

    private sealed class FixedSource(IReadOnlyList<SeedNode> nodes) : ISampleTreeSource
    {
        public IReadOnlyList<SeedNode> Nodes { get; } = nodes;

        public IReadOnlyList<SeedNode> GetNodes() => Nodes;
    }
}
