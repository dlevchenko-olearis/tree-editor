using Microsoft.Extensions.DependencyInjection;
using TreeEditor.Api.IntegrationTests.Support;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Api.IntegrationTests.Infrastructure;

public sealed class IdempotencyStoreTests(PostgresContainer postgres) : IAsyncLifetime
{
    private InfrastructureHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await InfrastructureHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task A_concurrent_claim_of_the_same_key_waits_and_then_replays_the_committed_outcome()
    {
        var command = Command();
        var outcome = new ApplyOutcome.Applied(1, [], [], [NodeId.Create(Guid.CreateVersion7())]);
        await using var first = _harness.Scope();
        await using var second = _harness.Scope();
        await Begin(first);
        await Begin(second);

        Assert.IsType<IdempotencyClaim.Claimed>(await Store(first).ClaimAsync(command, CancellationToken.None));
        var waiting = Store(second).ClaimAsync(command, CancellationToken.None);
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);

        await Store(first).SaveAsync(command.IdempotencyKey, outcome, CancellationToken.None);
        await Commit(first);

        var replay = Assert.IsType<IdempotencyClaim.Replay>(await waiting);
        Assert.Equal(outcome.Deleted, replay.Outcome.Deleted);
        Assert.Equal(outcome.Generation, replay.Outcome.Generation);
        await Commit(second);
    }

    [Fact]
    public async Task A_concurrent_claim_gets_the_key_when_the_first_claim_rolls_back()
    {
        var command = Command();
        await using var first = _harness.Scope();
        await using var second = _harness.Scope();
        await Begin(first);
        await Begin(second);

        await Store(first).ClaimAsync(command, CancellationToken.None);
        var waiting = Store(second).ClaimAsync(command, CancellationToken.None);
        await Task.Delay(300);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().RollbackAsync(CancellationToken.None);

        Assert.IsType<IdempotencyClaim.Claimed>(await waiting);
        await Commit(second);
    }

    [Fact]
    public async Task A_key_reused_with_another_change_set_is_a_mismatch()
    {
        var original = Command();
        await ClaimAndCommit(original, new ApplyOutcome.Applied(1, [], [], []));
        await using var scope = _harness.Scope();
        await Begin(scope);

        var claim = await Store(scope).ClaimAsync(original with { ChangeSet = Command().ChangeSet }, CancellationToken.None);

        Assert.IsType<IdempotencyClaim.Mismatch>(claim);
    }

    [Fact]
    public async Task A_key_reused_with_another_generation_is_a_mismatch()
    {
        var original = Command();
        await ClaimAndCommit(original, new ApplyOutcome.Applied(1, [], [], []));
        await using var scope = _harness.Scope();
        await Begin(scope);

        var claim = await Store(scope).ClaimAsync(original with { Generation = 2 }, CancellationToken.None);

        Assert.IsType<IdempotencyClaim.Mismatch>(claim);
    }

    [Fact]
    public async Task Retention_job_removes_requests_older_than_the_retention_period()
    {
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();
        await _harness.Database.ExecuteAsync(
            "INSERT INTO apply_requests (key, request_hash, created_at) VALUES ($1, $3, now() - interval '25 hours'), ($2, $3, now() - interval '23 hours')",
            old,
            recent,
            new byte[] { 1 });

        var purged = await _harness.RetentionJob().PurgeAsync(CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Equal([recent], await _harness.Database.QueryAsync("SELECT key FROM apply_requests", reader => reader.GetGuid(0)));
    }

    private async Task ClaimAndCommit(ApplyChangesCommand command, ApplyOutcome.Applied outcome)
    {
        await using var scope = _harness.Scope();
        await Begin(scope);
        await Store(scope).ClaimAsync(command, CancellationToken.None);
        await Store(scope).SaveAsync(command.IdempotencyKey, outcome, CancellationToken.None);
        await Commit(scope);
    }

    private static Task Begin(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(CancellationToken.None);

    private static Task Commit(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);

    private static IIdempotencyStore Store(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

    private static ApplyChangesCommand Command()
    {
        ChangeSet.TryCreate([], [], [Guid.CreateVersion7()], out var changes, out _);
        return new ApplyChangesCommand(Guid.NewGuid(), 1, changes!);
    }
}
