using TreeEditor.Api.Http;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Nodes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Api.Endpoints;

/// <summary>Read endpoints for browsing the tree and loading single nodes.</summary>
public static class NodesEndpoints
{
    /// <summary>Maps <c>/api/nodes</c>.</summary>
    public static IEndpointRouteBuilder MapNodesEndpoints(this IEndpointRouteBuilder routes)
    {
        var nodes = routes.MapGroup("/api/nodes").WithTags("Nodes");
        nodes.MapGet("/roots", GetRootsAsync);
        nodes.MapGet("/{id}/children", GetChildrenAsync);
        nodes.MapGet("/{id}", GetNodeAsync);
        return routes;
    }

    private static async Task<IResult> GetRootsAsync(
        string? after,
        int? limit,
        IQueryHandler<GetRootsQuery, NodePageResult> handler,
        GenerationProbe probe,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var cursor = ReadOptionalId(after, "after", errors);
        var pageLimit = ReadLimit(limit, errors);
        if (errors.Count > 0)
        {
            return await InvalidAsync(errors, probe, cancellationToken);
        }

        var result = await handler.HandleAsync(new GetRootsQuery(cursor, pageLimit), cancellationToken);
        return TypedResults.Ok(ToResponse(result.Generation, result.Page!));
    }

    private static async Task<IResult> GetChildrenAsync(
        string id,
        string? after,
        int? limit,
        IQueryHandler<GetChildrenQuery, NodePageResult> handler,
        GenerationProbe probe,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var parentId = ReadRequiredId(id, "id", errors);
        var cursor = ReadOptionalId(after, "after", errors);
        var pageLimit = ReadLimit(limit, errors);
        if (errors.Count > 0)
        {
            return await InvalidAsync(errors, probe, cancellationToken);
        }

        var result = await handler.HandleAsync(new GetChildrenQuery(parentId, cursor, pageLimit), cancellationToken);
        return result.Page is null
            ? NotFound(result.Generation)
            : TypedResults.Ok(ToResponse(result.Generation, result.Page));
    }

    private static async Task<IResult> GetNodeAsync(
        string id,
        IQueryHandler<GetNodeQuery, NodeResult> handler,
        GenerationProbe probe,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var nodeId = ReadRequiredId(id, "id", errors);
        if (errors.Count > 0)
        {
            return await InvalidAsync(errors, probe, cancellationToken);
        }

        var result = await handler.HandleAsync(new GetNodeQuery(nodeId), cancellationToken);
        if (result.Node is not { } node)
        {
            return NotFound(result.Generation);
        }

        return TypedResults.Ok(new NodeResponse(
            result.Generation,
            node.Id.Value,
            node.ParentId?.Value,
            node.Value,
            node.Version.Value,
            node.IsDeleted,
            node.AncestorIds.Select(ancestor => ancestor.Value).ToList()));
    }

    private static NodePageResponse ToResponse(long generation, NodePage page) =>
        new(
            generation,
            page.Items
                .Select(item => new NodeItemResponse(
                    item.Id.Value,
                    item.ParentId?.Value,
                    item.Value,
                    item.IsDeleted,
                    item.HasChildren))
                .ToList(),
            page.NextAfter?.Value);

    private static NodeId ReadRequiredId(string text, string name, Dictionary<string, string[]> errors) =>
        ReadOptionalId(text, name, errors) ?? default;

    private static NodeId? ReadOptionalId(string? text, string name, Dictionary<string, string[]> errors)
    {
        if (text is null)
        {
            return null;
        }

        if (Guid.TryParse(text, out var value) && NodeId.TryCreate(value, out var id))
        {
            return id;
        }

        errors[name] = ["Must be a version 7 UUID."];
        return null;
    }

    private static PageLimit ReadLimit(int? limit, Dictionary<string, string[]> errors)
    {
        if (PageLimit.TryCreate(limit ?? PageLimit.Default, out var pageLimit))
        {
            return pageLimit;
        }

        errors["limit"] = [$"Must be between {PageLimit.Min} and {PageLimit.Max}."];
        return default;
    }

    private static IResult NotFound(long generation) =>
        ApiProblems.Result(ApiProblems.Create(StatusCodes.Status404NotFound, "The node does not exist.", generation));

    private static async Task<IResult> InvalidAsync(
        Dictionary<string, string[]> errors,
        GenerationProbe probe,
        CancellationToken cancellationToken) =>
        ApiProblems.Result(ApiProblems.Validation(errors, await probe.TryReadAsync(cancellationToken)));
}
