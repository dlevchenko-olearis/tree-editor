using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TreeEditor.Infrastructure.Persistence;

namespace TreeEditor.Infrastructure.Hosting;

/// <summary>Periodically deletes remembered Apply requests older than the retention period.</summary>
internal sealed class IdempotencyRetentionJob(
    IServiceScopeFactory scopes,
    InfrastructureOptions options,
    ILogger<IdempotencyRetentionJob> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.RetentionInterval);
        do
        {
            try
            {
                var purged = await PurgeAsync(stoppingToken);
                logger.LogInformation("Removed {Count} expired Apply requests", purged);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Removing expired Apply requests failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes Apply requests older than the retention period and returns how many were deleted.</summary>
    internal async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TreeDbContext>();
        var retention = options.IdempotencyRetention;
        return await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM apply_requests WHERE created_at < now() - {retention}
            """,
            cancellationToken);
    }
}
