using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Ports;

namespace TreeEditor.Application.Decorators;

/// <summary>Runs a command in one transaction, committed only when the outcome is successful.</summary>
internal sealed class TransactionDecorator<TCommand, TOutcome>(
    ICommandHandler<TCommand, TOutcome> inner,
    IUnitOfWork unitOfWork) : ICommandHandler<TCommand, TOutcome>
    where TCommand : ICommand
    where TOutcome : ICommandOutcome
{
    /// <summary>The decorated handler.</summary>
    internal ICommandHandler<TCommand, TOutcome> Inner => inner;

    /// <inheritdoc />
    public async Task<TOutcome> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginAsync(cancellationToken);
        TOutcome outcome;
        try
        {
            outcome = await inner.HandleAsync(command, cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        if (outcome.IsSuccess)
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        else
        {
            await unitOfWork.RollbackAsync(cancellationToken);
        }

        return outcome;
    }
}
