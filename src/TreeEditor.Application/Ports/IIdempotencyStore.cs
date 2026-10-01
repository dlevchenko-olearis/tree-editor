using TreeEditor.Application.Changes;

namespace TreeEditor.Application.Ports;

/// <summary>Result of claiming an idempotency key.</summary>
public abstract record IdempotencyClaim
{
    private IdempotencyClaim()
    {
    }

    /// <summary>The key is new; the request must be executed.</summary>
    public sealed record Claimed : IdempotencyClaim;

    /// <summary>The same request already succeeded; its stored outcome must be returned.</summary>
    public sealed record Replay(ApplyOutcome.Applied Outcome) : IdempotencyClaim;

    /// <summary>The key was already used for a different request.</summary>
    public sealed record Mismatch : IdempotencyClaim;
}

/// <summary>Remembers successful Apply requests by idempotency key, inside the Apply transaction.</summary>
public interface IIdempotencyStore
{
    /// <summary>Claims the key of <paramref name="command"/>, waiting for a concurrent request with the same key.</summary>
    Task<IdempotencyClaim> ClaimAsync(ApplyChangesCommand command, CancellationToken cancellationToken);

    /// <summary>Stores the outcome of the request that claimed <paramref name="key"/>.</summary>
    Task SaveAsync(Guid key, ApplyOutcome.Applied outcome, CancellationToken cancellationToken);
}
