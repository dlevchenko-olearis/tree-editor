using TreeEditor.Application.Changes;
using TreeEditor.Application.Nodes;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Application.Tests.Fakes;

/// <summary>In-memory stand-in for the stored tree that records the calls made to it.</summary>
internal sealed class InMemoryTree : ITreeReader, ITreeWriter
{
    private readonly Dictionary<NodeId, Row> _rows = [];
    private long _lastVersion;

    public long Generation { get; set; } = 1;

    public List<string> Calls { get; } = [];

    public Action? BeforeInsert { get; set; }

    public HashSet<NodeId> SkipOnUpdate { get; } = [];

    public IReadOnlyList<SeedNode>? Replacement { get; private set; }

    public NodeId Add(NodeId? parentId, string value = "value", bool deleted = false) =>
        AddWithId(NodeId.Create(Guid.CreateVersion7()), parentId, value, deleted);

    public NodeId AddWithId(NodeId id, NodeId? parentId, string value = "value", bool deleted = false)
    {
        _rows[id] = new Row(id, parentId, value, NextVersion(), deleted);
        return id;
    }

    public Row this[NodeId id] => _rows[id];

    public void Touch(NodeId id) => _rows[id] = _rows[id] with { Version = NextVersion() };

    public Task<long> GetGenerationAsync(CancellationToken cancellationToken)
    {
        Calls.Add("generation");
        return Task.FromResult(Generation);
    }

    public Task<TreeRead<IReadOnlyList<NodeListItem>>> GetRootsAsync(
        NodeId? after,
        int take,
        CancellationToken cancellationToken)
    {
        Calls.Add($"roots:{take}");
        return Task.FromResult(new TreeRead<IReadOnlyList<NodeListItem>>(Generation, Page(null, false, after, take)));
    }

    public Task<TreeRead<IReadOnlyList<NodeListItem>?>> GetChildrenAsync(
        NodeId parentId,
        NodeId? after,
        int take,
        CancellationToken cancellationToken)
    {
        Calls.Add($"children:{take}");
        IReadOnlyList<NodeListItem>? page = _rows.ContainsKey(parentId)
            ? Page(parentId, IsEffectivelyDeleted(parentId), after, take)
            : null;
        return Task.FromResult(new TreeRead<IReadOnlyList<NodeListItem>?>(Generation, page));
    }

    public Task<TreeRead<NodeDetails?>> GetNodeAsync(NodeId id, CancellationToken cancellationToken)
    {
        Calls.Add("node");
        NodeDetails? details = null;
        if (_rows.TryGetValue(id, out var row))
        {
            details = new NodeDetails(
                id,
                row.ParentId,
                row.Value,
                NodeVersion.Create(row.Version),
                IsEffectivelyDeleted(id),
                Ancestors(id).ToList());
        }

        return Task.FromResult(new TreeRead<NodeDetails?>(Generation, details));
    }

    public Task<TreeSnapshot> ReadSnapshotAsync(
        IReadOnlyCollection<NodeId> lockedIds,
        IReadOnlyCollection<NodeId> otherIds,
        CancellationToken cancellationToken)
    {
        Calls.Add($"snapshot:{lockedIds.Count}:{otherIds.Count}");
        var nodes = lockedIds.Concat(otherIds)
            .Where(_rows.ContainsKey)
            .Select(id => _rows[id])
            .Select(row => NodeSnapshot.Existing(
                row.Id,
                row.ParentId,
                NodeVersion.Create(row.Version),
                IsEffectivelyDeleted(row.Id)));
        return Task.FromResult(TreeSnapshot.Of(nodes));
    }

    public Task<IReadOnlyList<WrittenNode>> InsertAsync(
        IReadOnlyList<NewNode> nodes,
        CancellationToken cancellationToken)
    {
        Calls.Add($"insert:{nodes.Count}");
        BeforeInsert?.Invoke();
        var written = new List<WrittenNode>();
        foreach (var node in nodes.Where(node => !_rows.ContainsKey(node.Id)))
        {
            var row = new Row(node.Id, node.ParentId, node.Value.Value, NextVersion(), false);
            _rows[node.Id] = row;
            written.Add(new WrittenNode(node.Id, NodeVersion.Create(row.Version)));
        }

        return Task.FromResult<IReadOnlyList<WrittenNode>>(written);
    }

    public Task<IReadOnlyList<WrittenNode>> UpdateAsync(
        IReadOnlyList<NodeEdit> edits,
        CancellationToken cancellationToken)
    {
        Calls.Add($"update:{edits.Count}");
        var written = new List<WrittenNode>();
        foreach (var edit in edits.Where(edit => !SkipOnUpdate.Contains(edit.Id)))
        {
            var row = _rows[edit.Id] with { Value = edit.Value.Value, Version = NextVersion() };
            _rows[edit.Id] = row;
            written.Add(new WrittenNode(edit.Id, NodeVersion.Create(row.Version)));
        }

        return Task.FromResult<IReadOnlyList<WrittenNode>>(written);
    }

    public Task MarkDeletedAsync(IReadOnlyList<NodeId> roots, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{roots.Count}");
        foreach (var id in roots)
        {
            _rows[id] = _rows[id] with { IsDeletedFlag = true, Version = NextVersion() };
        }

        return Task.CompletedTask;
    }

    public Task<long> ReplaceAllAsync(IReadOnlyList<SeedNode> nodes, CancellationToken cancellationToken)
    {
        Calls.Add($"replace:{nodes.Count}");
        Replacement = nodes;
        _rows.Clear();
        foreach (var node in nodes)
        {
            _rows[node.Id] = new Row(node.Id, node.ParentId, node.Value.Value, NextVersion(), false);
        }

        Generation++;
        return Task.FromResult(Generation);
    }

    public bool IsEffectivelyDeleted(NodeId id) =>
        _rows[id].IsDeletedFlag || Ancestors(id).Any(ancestor => _rows[ancestor].IsDeletedFlag);

    private IEnumerable<NodeId> Ancestors(NodeId id)
    {
        var current = _rows[id].ParentId;
        while (current is { } ancestor)
        {
            yield return ancestor;
            current = _rows[ancestor].ParentId;
        }
    }

    private List<NodeListItem> Page(NodeId? parentId, bool parentDeleted, NodeId? after, int take) =>
        _rows.Values
            .Where(row => row.ParentId == parentId)
            .Where(row => after is null || Compare(row.Id, after.Value) > 0)
            .OrderBy(row => row.Id.ToString(), StringComparer.Ordinal)
            .Take(take)
            .Select(row => new NodeListItem(
                row.Id,
                row.ParentId,
                row.Value,
                parentDeleted || row.IsDeletedFlag,
                _rows.Values.Any(child => child.ParentId == row.Id)))
            .ToList();

    private static int Compare(NodeId left, NodeId right) =>
        string.CompareOrdinal(left.ToString(), right.ToString());

    private long NextVersion() => ++_lastVersion;

    internal sealed record Row(NodeId Id, NodeId? ParentId, string Value, long Version, bool IsDeletedFlag);
}
