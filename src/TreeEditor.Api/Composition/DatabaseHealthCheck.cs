using Microsoft.Extensions.Diagnostics.HealthChecks;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Nodes;

namespace TreeEditor.Api.Composition;

/// <summary>Healthy when the tree generation can be read from the database.</summary>
public sealed class DatabaseHealthCheck(IQueryHandler<GetGenerationQuery, long> generation) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await generation.HandleAsync(new GetGenerationQuery(), cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}
