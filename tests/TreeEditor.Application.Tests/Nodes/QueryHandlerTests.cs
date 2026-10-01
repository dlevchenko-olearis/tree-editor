using TreeEditor.Application.Nodes;
using TreeEditor.Application.Tests.Fakes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Application.Tests.Nodes;

public sealed class QueryHandlerTests
{
    private readonly InMemoryTree _tree = new() { Generation = 4 };

    [Fact]
    public async Task Roots_page_fetches_one_extra_row_and_sets_the_cursor_when_more_exist()
    {
        var roots = Enumerable.Range(0, 3)
            .Select(_ => _tree.Add(null))
            .OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .ToList();
        var handler = new GetRootsHandler(_tree);

        var first = await handler.HandleAsync(new GetRootsQuery(null, PageLimit.Create(2)), CancellationToken.None);
        var second = await handler.HandleAsync(
            new GetRootsQuery(first.Page!.NextAfter, PageLimit.Create(2)),
            CancellationToken.None);

        Assert.Equal(4, first.Generation);
        Assert.Equal(roots.Take(2), first.Page.Items.Select(item => item.Id));
        Assert.Equal(roots[1], first.Page.NextAfter);
        Assert.Equal([roots[2]], second.Page!.Items.Select(item => item.Id));
        Assert.Null(second.Page.NextAfter);
        Assert.Equal(["roots:3", "roots:3"], _tree.Calls);
    }

    [Fact]
    public async Task A_page_that_holds_every_remaining_row_has_no_cursor()
    {
        _tree.Add(null);
        _tree.Add(null);
        var handler = new GetRootsHandler(_tree);

        var page = await handler.HandleAsync(new GetRootsQuery(null, PageLimit.Create(2)), CancellationToken.None);

        Assert.Equal(2, page.Page!.Items.Count);
        Assert.Null(page.Page.NextAfter);
    }

    [Fact]
    public async Task Children_page_reports_effective_deletion_and_children()
    {
        var root = _tree.Add(null, deleted: true);
        var child = _tree.Add(root);
        _tree.Add(child);
        var handler = new GetChildrenHandler(_tree);

        var result = await handler.HandleAsync(
            new GetChildrenQuery(root, null, PageLimit.Create(PageLimit.Default)),
            CancellationToken.None);

        var item = Assert.Single(result.Page!.Items);
        Assert.Equal(child, item.Id);
        Assert.True(item.IsDeleted);
        Assert.True(item.HasChildren);
    }

    [Fact]
    public async Task Children_of_an_unknown_node_are_not_found_but_carry_the_generation()
    {
        var handler = new GetChildrenHandler(_tree);

        var result = await handler.HandleAsync(
            new GetChildrenQuery(NodeId.Create(Guid.CreateVersion7()), null, PageLimit.Create(20)),
            CancellationToken.None);

        Assert.Null(result.Page);
        Assert.Equal(4, result.Generation);
    }

    [Fact]
    public async Task Node_is_returned_with_its_ancestor_chain_from_the_parent_up()
    {
        var root = _tree.Add(null);
        var middle = _tree.Add(root);
        var leaf = _tree.Add(middle, "leaf");
        var handler = new GetNodeHandler(_tree);

        var result = await handler.HandleAsync(new GetNodeQuery(leaf), CancellationToken.None);

        Assert.Equal("leaf", result.Node!.Value);
        Assert.Equal([middle, root], result.Node.AncestorIds);
        Assert.Equal(4, result.Generation);
    }

    [Fact]
    public async Task Unknown_node_is_not_found_but_carries_the_generation()
    {
        var handler = new GetNodeHandler(_tree);

        var result = await handler.HandleAsync(
            new GetNodeQuery(NodeId.Create(Guid.CreateVersion7())),
            CancellationToken.None);

        Assert.Null(result.Node);
        Assert.Equal(4, result.Generation);
    }

    [Fact]
    public async Task Generation_query_returns_the_current_generation()
    {
        var handler = new GetGenerationHandler(_tree);

        Assert.Equal(4, await handler.HandleAsync(new GetGenerationQuery(), CancellationToken.None));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(0, false)]
    [InlineData(201, false)]
    [InlineData(-5, false)]
    public void Page_limit_is_bounded(int value, bool accepted)
    {
        Assert.Equal(accepted, PageLimit.TryCreate(value, out _));
    }
}
