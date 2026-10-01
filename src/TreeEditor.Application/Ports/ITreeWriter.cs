using TreeEditor.Application.Changes;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Application.Ports;

/// <summary>Writes the stored tree. Every written row receives a new version.</summary>
public interface ITreeWriter
{
    /// <summary>Inserts nodes in the given parent-first order and returns those inserted; existing ids are skipped.</summary>
    Task<IReadOnlyList<WrittenNode>> InsertAsync(IReadOnlyList<NewNode> nodes, CancellationToken cancellationToken);

    /// <summary>Updates values whose stored version equals the expected one and returns the updated rows.</summary>
    Task<IReadOnlyList<WrittenNode>> UpdateAsync(IReadOnlyList<NodeEdit> edits, CancellationToken cancellationToken);

    /// <summary>Sets the deletion flag on the roots of deleted subtrees.</summary>
    Task MarkDeletedAsync(IReadOnlyList<NodeId> roots, CancellationToken cancellationToken);

    /// <summary>Replaces the whole tree with <paramref name="nodes"/> and returns the new generation.</summary>
    Task<long> ReplaceAllAsync(IReadOnlyList<SeedNode> nodes, CancellationToken cancellationToken);
}
