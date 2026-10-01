using TreeEditor.Application.Changes;
using TreeEditor.Application.Decorators;
using TreeEditor.Application.Reset;
using TreeEditor.Application.Tests.Fakes;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Application.Tests.Decorators;

public sealed class DecoratorTests
{
    private const string SecretValue = "do-not-log-this-value";

    private readonly RecordingUnitOfWork _unitOfWork = new();
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly InMemoryTree _tree = new() { Generation = 5 };

    [Fact]
    public async Task Logging_writes_ids_and_counts_but_never_values()
    {
        var loggers = new CapturingLoggerFactory();
        var command = Command();
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => Applied());
        var decorator = new LoggingDecorator<ApplyChangesCommand, ApplyOutcome>(inner, loggers);

        await decorator.HandleAsync(command, CancellationToken.None);

        var scope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.Single(loggers.Scopes));
        Assert.Equal(command.IdempotencyKey, scope["IdempotencyKey"]);
        Assert.Equal(1, scope["Created"]);
        Assert.Equal(1, scope["Updated"]);
        Assert.Contains(loggers.Messages, message => message.Contains("Applied", StringComparison.Ordinal));
        Assert.DoesNotContain(
            loggers.Messages.Concat(scope.Values.Select(value => value?.ToString() ?? string.Empty)),
            text => text.Contains(SecretValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logging_rethrows_failures()
    {
        var loggers = new CapturingLoggerFactory();
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => throw new TimeoutException());
        var decorator = new LoggingDecorator<ApplyChangesCommand, ApplyOutcome>(inner, loggers);

        await Assert.ThrowsAsync<TimeoutException>(() => decorator.HandleAsync(Command(), CancellationToken.None));
        Assert.Contains(loggers.Messages, message => message.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transaction_commits_an_applied_outcome()
    {
        await Transaction(Applied()).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(["begin", "commit"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Transaction_commits_a_reset()
    {
        var inner = new StubHandler<ResetTreeCommand, ResetOutcome>(() => new ResetOutcome(6));
        var decorator = new TransactionDecorator<ResetTreeCommand, ResetOutcome>(inner, _unitOfWork);

        await decorator.HandleAsync(new ResetTreeCommand(), CancellationToken.None);

        Assert.Equal(["begin", "commit"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Transaction_commits_a_replayed_outcome()
    {
        var command = Command();
        _store.Remember(command, Applied());
        var handler = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => throw new InvalidOperationException());
        var decorator = new TransactionDecorator<ApplyChangesCommand, ApplyOutcome>(
            new IdempotencyDecorator(handler, _store, _tree),
            _unitOfWork);

        await decorator.HandleAsync(command, CancellationToken.None);

        Assert.Equal(["begin", "commit"], _unitOfWork.Calls);
        Assert.Equal(0, handler.CallCount);
    }

    public static TheoryData<string> FailedOutcomes => ["conflicted", "invalid", "mismatch"];

    [Theory]
    [MemberData(nameof(FailedOutcomes))]
    public async Task Transaction_rolls_back_every_other_outcome(string kind)
    {
        ApplyOutcome outcome = kind switch
        {
            "conflicted" => new ApplyOutcome.Conflicted(5, [Conflict.StaleGeneration()]),
            "invalid" => new ApplyOutcome.Invalid(5, []),
            _ => new ApplyOutcome.KeyMismatch(5),
        };

        await Transaction(outcome).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(["begin", "rollback"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Transaction_rolls_back_and_rethrows_on_failure()
    {
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => throw new TimeoutException());
        var decorator = new TransactionDecorator<ApplyChangesCommand, ApplyOutcome>(inner, _unitOfWork);

        await Assert.ThrowsAsync<TimeoutException>(() => decorator.HandleAsync(Command(), CancellationToken.None));
        Assert.Equal(["begin", "rollback"], _unitOfWork.Calls);
    }

    [Fact]
    public async Task Idempotency_executes_a_new_key_and_stores_the_applied_outcome()
    {
        var command = Command();
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => Applied());

        await new IdempotencyDecorator(inner, _store, _tree).HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, inner.CallCount);
        Assert.Equal([command.IdempotencyKey], _store.Saved);
    }

    [Fact]
    public async Task Idempotency_replays_the_stored_outcome_for_the_same_request()
    {
        var command = Command();
        var stored = Applied();
        _store.Remember(command, stored);
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => Applied());

        var outcome = await new IdempotencyDecorator(inner, _store, _tree).HandleAsync(command, CancellationToken.None);

        Assert.Same(stored, outcome);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task Idempotency_rejects_a_key_reused_for_another_change_set()
    {
        var original = Command();
        _store.Remember(original, Applied());
        var reused = original with { ChangeSet = Command().ChangeSet };
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => Applied());

        var outcome = await new IdempotencyDecorator(inner, _store, _tree).HandleAsync(reused, CancellationToken.None);

        Assert.Equal(new ApplyOutcome.KeyMismatch(5), outcome);
        Assert.Equal(0, inner.CallCount);
    }

    [Theory]
    [MemberData(nameof(FailedOutcomes))]
    public async Task Idempotency_stores_nothing_for_a_conflict_or_an_invalid_request(string kind)
    {
        ApplyOutcome outcome = kind == "invalid"
            ? new ApplyOutcome.Invalid(5, [])
            : new ApplyOutcome.Conflicted(5, [Conflict.StaleGeneration()]);
        var inner = new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => outcome);

        await new IdempotencyDecorator(inner, _store, _tree).HandleAsync(Command(), CancellationToken.None);

        Assert.Empty(_store.Saved);
    }

    private TransactionDecorator<ApplyChangesCommand, ApplyOutcome> Transaction(ApplyOutcome outcome) =>
        new(new StubHandler<ApplyChangesCommand, ApplyOutcome>(() => outcome), _unitOfWork);

    private static ApplyOutcome.Applied Applied() => new(5, [], [], []);

    private static ApplyChangesCommand Command()
    {
        var parent = Guid.CreateVersion7();
        ChangeSet.TryCreate(
            [new(Guid.CreateVersion7(), parent, SecretValue)],
            [new(Guid.CreateVersion7(), SecretValue, 1)],
            [],
            out var changes,
            out _);
        return new ApplyChangesCommand(Guid.NewGuid(), 5, changes!);
    }
}
