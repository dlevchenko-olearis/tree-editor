using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Ports;

namespace TreeEditor.Application.Decorators;

/// <summary>
/// Claims the idempotency key inside the Apply transaction. A repeated request gets the stored outcome, a reused
/// key with other content gets <see cref="ApplyOutcome.KeyMismatch"/>, and only applied outcomes are stored, so a
/// rollback releases the key.
/// </summary>
internal sealed class IdempotencyDecorator(
    ICommandHandler<ApplyChangesCommand, ApplyOutcome> inner,
    IIdempotencyStore store,
    ITreeReader reader) : ICommandHandler<ApplyChangesCommand, ApplyOutcome>
{
    /// <summary>The decorated handler.</summary>
    internal ICommandHandler<ApplyChangesCommand, ApplyOutcome> Inner => inner;

    /// <inheritdoc />
    public async Task<ApplyOutcome> HandleAsync(ApplyChangesCommand command, CancellationToken cancellationToken)
    {
        switch (await store.ClaimAsync(command, cancellationToken))
        {
            case IdempotencyClaim.Replay replay:
                return replay.Outcome;
            case IdempotencyClaim.Mismatch:
                return new ApplyOutcome.KeyMismatch(await reader.GetGenerationAsync(cancellationToken));
        }

        var outcome = await inner.HandleAsync(command, cancellationToken);
        if (outcome is ApplyOutcome.Applied applied)
        {
            await store.SaveAsync(command.IdempotencyKey, applied, cancellationToken);
        }

        return outcome;
    }
}
