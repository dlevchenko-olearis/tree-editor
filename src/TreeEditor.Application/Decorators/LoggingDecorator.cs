using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TreeEditor.Application.Abstractions;

namespace TreeEditor.Application.Decorators;

/// <summary>Logs each command with its ids and counts, its outcome, and its duration. Never logs node values.</summary>
internal sealed class LoggingDecorator<TCommand, TOutcome>(
    ICommandHandler<TCommand, TOutcome> inner,
    ILoggerFactory loggerFactory) : ICommandHandler<TCommand, TOutcome>
    where TCommand : ICommand
    where TOutcome : ICommandOutcome
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("TreeEditor.Application.Commands");

    /// <summary>The decorated handler.</summary>
    internal ICommandHandler<TCommand, TOutcome> Inner => inner;

    /// <inheritdoc />
    public async Task<TOutcome> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(command.LogState);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var outcome = await inner.HandleAsync(command, cancellationToken);
            _logger.LogInformation(
                "{Command} finished with {Outcome} in generation {Generation} after {ElapsedMilliseconds} ms",
                typeof(TCommand).Name,
                outcome.GetType().Name,
                outcome.Generation,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return outcome;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "{Command} failed after {ElapsedMilliseconds} ms",
                typeof(TCommand).Name,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
