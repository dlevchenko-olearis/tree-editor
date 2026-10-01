using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Planning;

/// <summary>
/// Snapshots of the nodes touched by a change set. Ids without a row are reported as missing.
/// </summary>
public sealed class TreeSnapshot
{
    private readonly Dictionary<NodeId, NodeSnapshot> _nodes;

    private TreeSnapshot(Dictionary<NodeId, NodeSnapshot> nodes) => _nodes = nodes;

    /// <summary>The snapshot of <paramref name="id"/>, or a missing snapshot when no row exists.</summary>
    public NodeSnapshot this[NodeId id] => _nodes.TryGetValue(id, out var node) ? node : NodeSnapshot.Missing(id);

    /// <summary>Builds snapshots from snapshots whose effective deletion state is already known.</summary>
    public static TreeSnapshot Of(IEnumerable<NodeSnapshot> nodes) =>
        new(nodes.ToDictionary(node => node.Id));

    /// <summary>
    /// Builds snapshots from stored rows that include every ancestor of the touched nodes. A node is effectively
    /// deleted when its own flag or the flag of any ancestor is set. Each row is resolved at most once.
    /// </summary>
    public static TreeSnapshot FromAncestry(IEnumerable<StoredNode> rows)
    {
        var stored = rows.ToDictionary(row => row.Id);
        var deleted = new Dictionary<NodeId, bool>(stored.Count);
        foreach (var id in stored.Keys)
        {
            ResolveDeletion(id, stored, deleted);
        }

        return new TreeSnapshot(stored.Values.ToDictionary(
            row => row.Id,
            row => NodeSnapshot.Existing(row.Id, row.ParentId, row.Version, deleted[row.Id])));
    }

    private static void ResolveDeletion(
        NodeId start,
        Dictionary<NodeId, StoredNode> stored,
        Dictionary<NodeId, bool> resolved)
    {
        var path = new List<NodeId>();
        var isDeleted = false;
        NodeId? current = start;
        while (current is { } id && path.Count <= stored.Count)
        {
            if (resolved.TryGetValue(id, out var known))
            {
                isDeleted = known;
                break;
            }

            if (!stored.TryGetValue(id, out var row))
            {
                break;
            }

            path.Add(id);
            if (row.IsDeletedFlag)
            {
                isDeleted = true;
                break;
            }

            current = row.ParentId;
        }

        foreach (var id in path)
        {
            resolved[id] = isDeleted;
        }
    }
}
