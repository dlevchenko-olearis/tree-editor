using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Nodes;

namespace TreeEditor.Api.Http;

/// <summary>Reads the tree generation for error responses, without letting a database failure hide the error.</summary>
public sealed class GenerationProbe(IServiceScopeFactory scopes, ILogger<GenerationProbe> logger)
{
    /// <summary>The current generation, or <c>null</c> when it cannot be read.</summary>
    public async Task<long?> TryReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetGenerationQuery, long>>();
            return await handler.HandleAsync(new GetGenerationQuery(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The tree generation could not be read for an error response");
            return null;
        }
    }
}
