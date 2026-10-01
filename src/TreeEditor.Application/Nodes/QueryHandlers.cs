using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Ports;

namespace TreeEditor.Application.Nodes;

/// <summary>Returns a page of root nodes.</summary>
public sealed class GetRootsHandler(ITreeReader reader) : IQueryHandler<GetRootsQuery, NodePageResult>
{
    /// <inheritdoc />
    public async Task<NodePageResult> HandleAsync(GetRootsQuery query, CancellationToken cancellationToken)
    {
        var read = await reader.GetRootsAsync(query.After, PageAssembler.RowsToFetch(query.Limit), cancellationToken);
        return new NodePageResult(read.Generation, PageAssembler.Assemble(read.Value, query.Limit));
    }
}

/// <summary>Returns a page of the direct children of a node, or no page when the node does not exist.</summary>
public sealed class GetChildrenHandler(ITreeReader reader) : IQueryHandler<GetChildrenQuery, NodePageResult>
{
    /// <inheritdoc />
    public async Task<NodePageResult> HandleAsync(GetChildrenQuery query, CancellationToken cancellationToken)
    {
        var read = await reader.GetChildrenAsync(
            query.ParentId,
            query.After,
            PageAssembler.RowsToFetch(query.Limit),
            cancellationToken);
        var page = read.Value is null ? null : PageAssembler.Assemble(read.Value, query.Limit);
        return new NodePageResult(read.Generation, page);
    }
}

/// <summary>Returns one node with its ancestor chain, or no node when it does not exist.</summary>
public sealed class GetNodeHandler(ITreeReader reader) : IQueryHandler<GetNodeQuery, NodeResult>
{
    /// <inheritdoc />
    public async Task<NodeResult> HandleAsync(GetNodeQuery query, CancellationToken cancellationToken)
    {
        var read = await reader.GetNodeAsync(query.Id, cancellationToken);
        return new NodeResult(read.Generation, read.Value);
    }
}

/// <summary>Returns the current tree generation.</summary>
public sealed class GetGenerationHandler(ITreeReader reader) : IQueryHandler<GetGenerationQuery, long>
{
    /// <inheritdoc />
    public Task<long> HandleAsync(GetGenerationQuery query, CancellationToken cancellationToken) =>
        reader.GetGenerationAsync(cancellationToken);
}
