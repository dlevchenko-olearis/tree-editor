namespace TreeEditor.Application.Abstractions;

/// <summary>Handles one kind of command.</summary>
public interface ICommandHandler<in TCommand, TOutcome>
    where TCommand : ICommand
    where TOutcome : ICommandOutcome
{
    /// <summary>Executes <paramref name="command"/>.</summary>
    Task<TOutcome> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
