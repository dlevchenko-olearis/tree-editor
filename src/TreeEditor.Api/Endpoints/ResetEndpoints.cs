using TreeEditor.Api.Composition;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Reset;

namespace TreeEditor.Api.Endpoints;

/// <summary>The Reset endpoint, which restores the sample tree and starts a new generation.</summary>
public static class ResetEndpoints
{
    /// <summary>Maps <c>POST /api/reset</c>. The body is the empty JSON object <c>{}</c>.</summary>
    public static IEndpointRouteBuilder MapResetEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/reset", ResetAsync)
            .WithTags("Reset")
            .RequireRateLimiting(RateLimiting.MutationsPolicy);
        return routes;
    }

    private static async Task<IResult> ResetAsync(
        ICommandHandler<ResetTreeCommand, ResetOutcome> handler,
        CancellationToken cancellationToken)
    {
        var outcome = await handler.HandleAsync(new ResetTreeCommand(), cancellationToken);
        return TypedResults.Ok(new ResetResponse(outcome.Generation));
    }
}
