using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TreeEditor.Api.IntegrationTests.Support;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Nodes;
using TreeEditor.Infrastructure.Persistence;

namespace TreeEditor.Api.IntegrationTests.Infrastructure;

public sealed class TreeReaderTests(PostgresContainer postgres) : IAsyncLifetime
{
    private InfrastructureHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await InfrastructureHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Pages_roots_by_id_with_a_keyset_cursor()
    {
        await using var scope = _harness.Scope();
        var reader = Reader(scope);

        var all = await reader.GetRootsAsync(null, 10, CancellationToken.None);
        var afterFirst = await reader.GetRootsAsync(all.Value[0].Id, 10, CancellationToken.None);

        Assert.Equal(1, all.Generation);
        Assert.Equal(["Company", "Library", "Archive"], all.Value.Select(item => item.Value));
        Assert.Equal([true, true, false], all.Value.Select(item => item.HasChildren));
        Assert.Equal(all.Value.Skip(1), afterFirst.Value);
    }

    [Fact]
    public async Task Reports_children_of_a_deleted_ancestor_as_deleted()
    {
        var chain = await _harness.AddChainAsync(null, 4);
        await _harness.FlagDeletedAsync(chain[0]);
        await using var scope = _harness.Scope();

        var children = await Reader(scope).GetChildrenAsync(chain[2], null, 10, CancellationToken.None);

        var item = Assert.Single(children.Value!);
        Assert.Equal(chain[3], item.Id);
        Assert.True(item.IsDeleted);
        Assert.False(item.HasChildren);
    }

    [Fact]
    public async Task Children_of_an_unknown_node_are_not_found()
    {
        await using var scope = _harness.Scope();

        var children = await Reader(scope).GetChildrenAsync(
            NodeId.Create(Guid.CreateVersion7()),
            null,
            10,
            CancellationToken.None);

        Assert.Null(children.Value);
        Assert.Equal(1, children.Generation);
    }

    [Fact]
    public async Task Loads_a_node_with_its_ancestor_chain_and_effective_state()
    {
        var chain = await _harness.AddChainAsync(null, 5);
        await _harness.FlagDeletedAsync(chain[1]);
        await using var scope = _harness.Scope();

        var node = (await Reader(scope).GetNodeAsync(chain[4], CancellationToken.None)).Value!;

        Assert.Equal([chain[3], chain[2], chain[1], chain[0]], node.AncestorIds);
        Assert.Equal(chain[3], node.ParentId);
        Assert.Equal("level 4", node.Value);
        Assert.True(node.IsDeleted);
    }

    [Fact]
    public async Task Handles_a_chain_ten_thousand_levels_deep()
    {
        var chain = await _harness.AddChainAsync(null, 10_000);
        await _harness.FlagDeletedAsync(chain[0]);
        await using var scope = _harness.Scope();
        var reader = Reader(scope);

        var deepest = (await reader.GetNodeAsync(chain[^1], CancellationToken.None)).Value!;
        var children = await reader.GetChildrenAsync(chain[^2], null, 10, CancellationToken.None);
        var snapshot = await reader.ReadSnapshotAsync([], [chain[^1]], CancellationToken.None);

        Assert.Equal(9_999, deepest.AncestorIds.Count);
        Assert.Equal(chain[0], deepest.AncestorIds[^1]);
        Assert.True(deepest.IsDeleted);
        Assert.True(Assert.Single(children.Value!).IsDeleted);
        Assert.True(snapshot[chain[^1]].IsDeleted);
    }

    [Fact]
    public async Task Snapshot_of_a_thousand_siblings_reads_their_shared_ancestors_once()
    {
        var chain = await _harness.AddChainAsync(null, 3);
        var siblings = await _harness.AddChildrenAsync(chain[^1], 1_000);
        await using var scope = _harness.Scope();

        var rows = await ((PostgresTreeReader)Reader(scope)).ReadAncestryAsync(siblings, CancellationToken.None);

        Assert.Equal(1_003, rows.Count);
        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct().Count());
    }

    [Fact]
    public async Task Snapshot_reports_existence_versions_and_missing_ids()
    {
        var chain = await _harness.AddChainAsync(null, 2);
        var missing = NodeId.Create(Guid.CreateVersion7());
        var version = await _harness.Database.ScalarAsync<long>("SELECT version FROM nodes WHERE id = $1", chain[1].Value);
        await using var scope = _harness.Scope();

        var snapshot = await Reader(scope).ReadSnapshotAsync([chain[1]], [missing], CancellationToken.None);

        Assert.True(snapshot[chain[1]].Exists);
        Assert.Equal(chain[0], snapshot[chain[1]].ParentId);
        Assert.Equal(version, snapshot[chain[1]].Version!.Value.Value);
        Assert.False(snapshot[missing].Exists);
    }

    [Fact]
    public async Task Snapshot_locks_targets_against_writers_but_not_against_new_children()
    {
        var chain = await _harness.AddChainAsync(null, 2);
        await using var scope = _harness.Scope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(CancellationToken.None);
        await Reader(scope).ReadSnapshotAsync([chain[1]], [], CancellationToken.None);

        await using var other = await _harness.Database.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '200ms'", other, transaction))
        {
            await timeout.ExecuteNonQueryAsync();
        }

        await using (var child = new NpgsqlCommand("INSERT INTO nodes (id, parent_id, value) VALUES ($1, $2, 'child')", other, transaction))
        {
            child.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
            child.Parameters.Add(new NpgsqlParameter { Value = chain[1].Value });
            await child.ExecuteNonQueryAsync();
        }

        await using var write = new NpgsqlCommand("UPDATE nodes SET value = 'other' WHERE id = $1", other, transaction);
        write.Parameters.Add(new NpgsqlParameter { Value = chain[1].Value });
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, blocked.SqlState);

        await unitOfWork.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_read_that_overlaps_a_reset_sees_the_tree_after_it_with_the_new_generation()
    {
        await using var resetScope = _harness.Scope();
        var unitOfWork = resetScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sample = resetScope.ServiceProvider.GetRequiredService<ISampleTreeSource>().GetNodes();
        await unitOfWork.BeginAsync(CancellationToken.None);
        await resetScope.ServiceProvider.GetRequiredService<ITreeWriter>().ReplaceAllAsync(sample, CancellationToken.None);

        await using var readScope = _harness.Scope();
        var read = Reader(readScope).GetRootsAsync(null, 10, CancellationToken.None);
        await WaitForLockWaitersAsync();
        await unitOfWork.CommitAsync(CancellationToken.None);
        var roots = await read;

        Assert.Equal(2, roots.Generation);
        Assert.Equal(["Company", "Library", "Archive"], roots.Value.Select(item => item.Value));
    }

    [Fact]
    public async Task Reading_the_generation_in_a_transaction_holds_off_a_reset_until_the_transaction_ends()
    {
        await using var applyScope = _harness.Scope();
        var applyWork = applyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await applyWork.BeginAsync(CancellationToken.None);
        Assert.Equal(1, await Reader(applyScope).GetGenerationAsync(CancellationToken.None));

        await using var resetScope = _harness.Scope();
        var resetWork = resetScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sample = resetScope.ServiceProvider.GetRequiredService<ISampleTreeSource>().GetNodes();
        await resetWork.BeginAsync(CancellationToken.None);
        var blocked = await Assert.ThrowsAnyAsync<Exception>(() =>
            resetScope.ServiceProvider.GetRequiredService<ITreeWriter>().ReplaceAllAsync(sample, CancellationToken.None));
        await resetWork.RollbackAsync(CancellationToken.None);
        await applyWork.RollbackAsync(CancellationToken.None);

        Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(blocked.GetBaseException()).SqlState);
        Assert.Equal(1L, await _harness.Database.ScalarAsync<long>("SELECT generation FROM tree_state"));
    }

    [Fact]
    public async Task Unit_of_work_sets_statement_and_lock_timeouts()
    {
        await using var scope = _harness.Scope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<TreeDbContext>();
        await unitOfWork.BeginAsync(CancellationToken.None);

        var settings = await db.Database.SqlQuery<string>(
                $"""
                SELECT current_setting('statement_timeout') || '/' || current_setting('lock_timeout') AS "Value"
                """)
            .ToListAsync();

        Assert.Equal("5s/2s", Assert.Single(settings));
        await unitOfWork.RollbackAsync(CancellationToken.None);
    }

    private async Task WaitForLockWaitersAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await _harness.Database.ScalarAsync<long>("SELECT count(*) FROM pg_locks AS l JOIN pg_database AS d ON d.oid = l.database WHERE NOT l.granted AND d.datname = current_database()") == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "No session started waiting for a lock.");
            await Task.Delay(20);
        }
    }

    private static ITreeReader Reader(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITreeReader>();
}
