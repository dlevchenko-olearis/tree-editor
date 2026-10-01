using Microsoft.Extensions.Logging;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Ports;

namespace TreeEditor.Application.Tests.Fakes;

/// <summary>Unit of work that records its calls in order.</summary>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public List<string> Calls { get; } = [];

    public Task BeginAsync(CancellationToken cancellationToken)
    {
        Calls.Add("begin");
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Calls.Add("commit");
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        Calls.Add("rollback");
        return Task.CompletedTask;
    }
}

/// <summary>Idempotency store keeping claims and outcomes in memory.</summary>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<Guid, (ApplyChangesCommand Command, ApplyOutcome.Applied? Outcome)> _entries = [];

    public List<Guid> Saved { get; } = [];

    public void Remember(ApplyChangesCommand command, ApplyOutcome.Applied outcome) =>
        _entries[command.IdempotencyKey] = (command, outcome);

    public Task<IdempotencyClaim> ClaimAsync(ApplyChangesCommand command, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(command.IdempotencyKey, out var entry))
        {
            _entries[command.IdempotencyKey] = (command, null);
            return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Claimed());
        }

        IdempotencyClaim claim = ReferenceEquals(entry.Command, command) && entry.Outcome is not null
            ? new IdempotencyClaim.Replay(entry.Outcome)
            : new IdempotencyClaim.Mismatch();
        return Task.FromResult(claim);
    }

    public Task SaveAsync(Guid key, ApplyOutcome.Applied outcome, CancellationToken cancellationToken)
    {
        Saved.Add(key);
        _entries[key] = (_entries[key].Command, outcome);
        return Task.CompletedTask;
    }
}

/// <summary>Command handler returning a fixed outcome or throwing, and counting its calls.</summary>
internal sealed class StubHandler<TCommand, TOutcome>(Func<TOutcome> outcome) : ICommandHandler<TCommand, TOutcome>
    where TCommand : ICommand
    where TOutcome : ICommandOutcome
{
    public int CallCount { get; private set; }

    public Task<TOutcome> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(outcome());
    }
}

/// <summary>Logger factory that keeps every message and scope state written through it.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public List<string> Messages { get; } = [];

    public List<object?> Scopes { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerFactory owner) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            owner.Scopes.Add(state);
            return new Scope();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Messages.Add(formatter(state, exception));
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
