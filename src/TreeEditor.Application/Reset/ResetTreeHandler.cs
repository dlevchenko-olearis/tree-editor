using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Ports;

namespace TreeEditor.Application.Reset;

/// <summary>Replaces the stored tree with the sample tree and returns the new generation.</summary>
public sealed class ResetTreeHandler(ISampleTreeSource source, ITreeWriter writer)
    : ICommandHandler<ResetTreeCommand, ResetOutcome>
{
    /// <inheritdoc />
    public async Task<ResetOutcome> HandleAsync(ResetTreeCommand command, CancellationToken cancellationToken)
    {
        var generation = await writer.ReplaceAllAsync(source.GetNodes(), cancellationToken);
        return new ResetOutcome(generation);
    }
}
