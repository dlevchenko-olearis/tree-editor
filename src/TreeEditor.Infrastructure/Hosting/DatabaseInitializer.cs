using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TreeEditor.Application.Ports;
using TreeEditor.Infrastructure.Persistence;

namespace TreeEditor.Infrastructure.Hosting;

/// <summary>Applies pending migrations on start, then seeds the sample tree when the database has no nodes.</summary>
internal sealed class DatabaseInitializer(
    IServiceScopeFactory scopes,
    ISampleTreeSource sampleTree,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TreeDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"LOCK TABLE nodes IN EXCLUSIVE MODE", cancellationToken);
        var hasNodes = (await db.Database.SqlQuery<bool>(
            $"""
            SELECT EXISTS (SELECT 1 FROM nodes) AS "Value"
            """).ToListAsync(cancellationToken)).Single();
        if (!hasNodes)
        {
            var nodes = sampleTree.GetNodes();
            await new PostgresTreeWriter(db).InsertSeedAsync(nodes, cancellationToken);
            logger.LogInformation("Seeded the sample tree with {Count} nodes", nodes.Count);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
