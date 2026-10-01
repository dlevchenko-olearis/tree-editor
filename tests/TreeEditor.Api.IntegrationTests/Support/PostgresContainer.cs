using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(TreeEditor.Api.IntegrationTests.Support.PostgresContainer))]

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>
/// One PostgreSQL 18 container for the whole test run; every test gets its own database in it. Each database keeps
/// its own connection pool alive until the run ends, so the server allows more connections than the default.
/// </summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithCommand("-c", "max_connections=500")
        .Build();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
