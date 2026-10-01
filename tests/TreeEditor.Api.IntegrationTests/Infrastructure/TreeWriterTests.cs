using Microsoft.Extensions.DependencyInjection;
using TreeEditor.Api.IntegrationTests.Support;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Api.IntegrationTests.Infrastructure;

public sealed class TreeWriterTests(PostgresContainer postgres) : IAsyncLifetime
{
    private InfrastructureHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await InfrastructureHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Every_write_assigns_a_fresh_version_from_the_sequence()
    {
        var parent = (await _harness.AddChainAsync(null, 1))[0];
        var highest = await MaxVersionAsync();
        await using var scope = _harness.Scope();
        var writer = Writer(scope);
        var first = NodeId.Create(Guid.CreateVersion7());
        var second = NodeId.Create(Guid.CreateVersion7());

        var inserted = await writer.InsertAsync(
            [new NewNode(first, parent, NodeValue.Create("a")), new NewNode(second, first, NodeValue.Create("b"))],
            CancellationToken.None);
        var updated = await writer.UpdateAsync(
            [new NodeEdit(first, NodeValue.Create("a2"), inserted[0].Version)],
            CancellationToken.None);
        await writer.MarkDeletedAsync([second], CancellationToken.None);
        var deletedVersion = await _harness.Database.ScalarAsync<long>(
            "SELECT version FROM nodes WHERE id = $1",
            second.Value);

        var versions = new[] { inserted[0].Version.Value, inserted[1].Version.Value, updated[0].Version.Value, deletedVersion };
        Assert.All(versions, version => Assert.True(version > highest));
        Assert.Equal(versions.Order(), versions);
        Assert.Equal(versions.Length, versions.Distinct().Count());
    }

    [Fact]
    public async Task Insert_skips_ids_that_already_exist()
    {
        var chain = await _harness.AddChainAsync(null, 2);
        await using var scope = _harness.Scope();
        var fresh = NodeId.Create(Guid.CreateVersion7());

        var inserted = await Writer(scope).InsertAsync(
            [new NewNode(chain[1], chain[0], NodeValue.Create("again")), new NewNode(fresh, chain[0], NodeValue.Create("new"))],
            CancellationToken.None);

        Assert.Equal([fresh], inserted.Select(node => node.Id));
        Assert.Equal("level 1", await _harness.Database.ScalarAsync<string>("SELECT value FROM nodes WHERE id = $1", chain[1].Value));
    }

    [Fact]
    public async Task Update_skips_rows_whose_version_differs()
    {
        var chain = await _harness.AddChainAsync(null, 1);
        await using var scope = _harness.Scope();

        var updated = await Writer(scope).UpdateAsync(
            [new NodeEdit(chain[0], NodeValue.Create("stale"), NodeVersion.Create(1))],
            CancellationToken.None);

        Assert.Empty(updated);
        Assert.Equal("level 0", await _harness.Database.ScalarAsync<string>("SELECT value FROM nodes WHERE id = $1", chain[0].Value));
    }

    [Fact]
    public async Task Deleting_a_subtree_writes_only_its_root()
    {
        var chain = await _harness.AddChainAsync(null, 50);
        await using var scope = _harness.Scope();

        await Writer(scope).MarkDeletedAsync([chain[0]], CancellationToken.None);

        Assert.Equal(1L, await _harness.Database.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE is_deleted AND id = ANY($1)",
            chain.Select(id => id.Value).ToArray()));
        Assert.Equal(50L, await _harness.Database.ScalarAsync<long>(
            "SELECT count(*) FROM node_state WHERE is_deleted_effective AND id = ANY($1)",
            chain.Select(id => id.Value).ToArray()));
    }

    [Fact]
    public async Task Replace_all_restores_the_sample_clears_apply_requests_and_increments_the_generation()
    {
        await _harness.AddChainAsync(null, 3);
        await _harness.Database.ExecuteAsync(
            "INSERT INTO apply_requests (key, request_hash, created_at) VALUES ($1, $2, now())",
            Guid.NewGuid(),
            new byte[] { 1, 2, 3 });
        await using var scope = _harness.Scope();
        var sample = scope.ServiceProvider.GetRequiredService<ISampleTreeSource>().GetNodes();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(CancellationToken.None);

        var generation = await Writer(scope).ReplaceAllAsync(sample, CancellationToken.None);
        await unitOfWork.CommitAsync(CancellationToken.None);

        Assert.Equal(2, generation);
        Assert.Equal(2L, await _harness.Database.ScalarAsync<long>("SELECT generation FROM tree_state"));
        Assert.Equal(0L, await _harness.Database.ScalarAsync<long>("SELECT count(*) FROM apply_requests"));
        Assert.Equal((long)sample.Count, await _harness.Database.ScalarAsync<long>("SELECT count(*) FROM nodes"));
    }

    private Task<long> MaxVersionAsync() => _harness.Database.ScalarAsync<long>("SELECT max(version) FROM nodes");

    private static ITreeWriter Writer(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITreeWriter>();
}
