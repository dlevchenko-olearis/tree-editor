using Npgsql;
using TreeEditor.Api.IntegrationTests.Support;

namespace TreeEditor.Api.IntegrationTests.Infrastructure;

public sealed class MigrationTests(PostgresContainer postgres) : IAsyncLifetime
{
    private InfrastructureHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await InfrastructureHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Creates_the_tables_the_view_and_the_sequence()
    {
        var relations = await _harness.Database.QueryAsync(
            """
            SELECT c.relname || ':' || c.relkind::text
            FROM pg_class AS c
            JOIN pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
            """,
            reader => reader.GetString(0));

        Assert.Contains("nodes:r", relations);
        Assert.Contains("tree_state:r", relations);
        Assert.Contains("apply_requests:r", relations);
        Assert.Contains("node_state:v", relations);
        Assert.Contains("node_version_seq:S", relations);
        Assert.Contains("ix_nodes_parent_id_id:i", relations);
        Assert.Contains("ix_nodes_roots:i", relations);
        Assert.Contains("ix_apply_requests_created_at:i", relations);
    }

    [Fact]
    public async Task Tree_state_holds_exactly_one_row_starting_at_generation_one()
    {
        Assert.Equal(1L, await _harness.Database.ScalarAsync<long>("SELECT generation FROM tree_state"));
        await Assert.ThrowsAsync<PostgresException>(() =>
            _harness.Database.ExecuteAsync("INSERT INTO tree_state (id, generation) VALUES (2, 1)"));
    }

    [Fact]
    public async Task A_node_cannot_be_its_own_parent_or_reference_a_missing_parent()
    {
        var id = Guid.CreateVersion7();

        var selfReference = await Assert.ThrowsAsync<PostgresException>(() => _harness.Database.ExecuteAsync(
            "INSERT INTO nodes (id, parent_id, value) VALUES ($1, $1, 'x')",
            id));
        var missingParent = await Assert.ThrowsAsync<PostgresException>(() => _harness.Database.ExecuteAsync(
            "INSERT INTO nodes (id, parent_id, value) VALUES ($1, $2, 'x')",
            id,
            Guid.CreateVersion7()));

        Assert.Equal(PostgresErrorCodes.CheckViolation, selfReference.SqlState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, missingParent.SqlState);
    }

    [Fact]
    public async Task Starting_again_neither_migrates_nor_seeds_twice()
    {
        var before = await _harness.Database.ScalarAsync<long>("SELECT count(*) FROM nodes");

        await _harness.Initializer().StartAsync(CancellationToken.None);

        Assert.Equal(before, await _harness.Database.ScalarAsync<long>("SELECT count(*) FROM nodes"));
        Assert.Equal(1L, await _harness.Database.ScalarAsync<long>("SELECT count(*) FROM \"__EFMigrationsHistory\""));
    }
}
