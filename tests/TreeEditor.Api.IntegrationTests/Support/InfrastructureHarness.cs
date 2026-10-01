using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TreeEditor.Application;
using TreeEditor.Infrastructure;
using TreeEditor.Infrastructure.Hosting;

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>Application and infrastructure services wired against a fresh, migrated, and seeded test database.</summary>
public sealed class InfrastructureHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private InfrastructureHarness(ServiceProvider provider, Database database)
    {
        _provider = provider;
        Database = database;
    }

    /// <summary>Direct SQL access to the database.</summary>
    public Database Database { get; }

    /// <summary>Resolved infrastructure options.</summary>
    public InfrastructureOptions Options => _provider.GetRequiredService<InfrastructureOptions>();

    /// <summary>Creates a database, migrates it, and seeds it with the sample tree.</summary>
    public static async Task<InfrastructureHarness> CreateAsync(PostgresContainer postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new InfrastructureOptions { ConnectionString = connectionString });
        services.AddApplication();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var harness = new InfrastructureHarness(provider, new Database(connectionString));
        await harness.Initializer().StartAsync(CancellationToken.None);
        return harness;
    }

    /// <summary>The start-up initializer (migrate, then seed when empty).</summary>
    internal DatabaseInitializer Initializer() =>
        _provider.GetServices<IHostedService>().OfType<DatabaseInitializer>().Single();

    /// <summary>The retention job for idempotency keys.</summary>
    internal IdempotencyRetentionJob RetentionJob() =>
        _provider.GetServices<IHostedService>().OfType<IdempotencyRetentionJob>().Single();

    /// <summary>Opens a scope, as one HTTP request would.</summary>
    public AsyncServiceScope Scope() => _provider.CreateAsyncScope();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
