using TreeEditor.Application.Nodes;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Application.Ports;

/// <summary>Reads the stored tree.</summary>
public interface ITreeReader
{
    /// <summary>
    /// Reads the current tree generation. Inside a transaction the generation cannot change until that transaction
    /// ends: a Reset waits for it instead of replacing the tree under it.
    /// </summary>
    Task<long> GetGenerationAsync(CancellationToken cancellationToken);

    /// <summary>Reads up to <paramref name="take"/> roots with ids greater than <paramref name="after"/>.</summary>
    Task<TreeRead<IReadOnlyList<NodeListItem>>> GetRootsAsync(
        NodeId? after,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="take"/> children of <paramref name="parentId"/> with ids greater than
    /// <paramref name="after"/>, or <c>null</c> when the parent does not exist.
    /// </summary>
    Task<TreeRead<IReadOnlyList<NodeListItem>?>> GetChildrenAsync(
        NodeId parentId,
        NodeId? after,
        int take,
        CancellationToken cancellationToken);

    /// <summary>Reads a node with its ancestor chain, or <c>null</c> when it does not exist.</summary>
    Task<TreeRead<NodeDetails?>> GetNodeAsync(NodeId id, CancellationToken cancellationToken);

    /// <summary>
    /// Locks <paramref name="lockedIds"/> for the rest of the transaction, then reads the state of those nodes and
    /// of <paramref name="otherIds"/>, including effective deletion.
    /// </summary>
    Task<TreeSnapshot> ReadSnapshotAsync(
        IReadOnlyCollection<NodeId> lockedIds,
        IReadOnlyCollection<NodeId> otherIds,
        CancellationToken cancellationToken);
}
