using System.Data;
using Microsoft.EntityFrameworkCore;
using TreeEditor.Application.Nodes;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>
/// Reads the adjacency-list tree. Effective deletion is derived from ancestors with recursive CTEs that use
/// <c>UNION</c>, so every walk visits each row once and always terminates. Reads outside a command run in one
/// read-only, repeatable-read transaction, so the generation and the rows come from the same snapshot. That
/// transaction locks <c>tree_state</c> and <c>nodes</c> in ACCESS SHARE mode, in the order Reset locks them, before
/// its snapshot exists: Reset empties <c>nodes</c> with <c>TRUNCATE</c>, which is not MVCC-safe, so a snapshot taken
/// before a Reset must not see the table after it. Reading the generation holds the ACCESS SHARE lock on
/// <c>tree_state</c> until the transaction ends, which is what makes a Reset wait for it.
/// </summary>
internal sealed class PostgresTreeReader(TreeDbContext db, InfrastructureOptions options) : ITreeReader
{
    /// <inheritdoc />
    public Task<long> GetGenerationAsync(CancellationToken cancellationToken) =>
        ReadGenerationAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TreeRead<IReadOnlyList<NodeListItem>>> GetRootsAsync(
        NodeId? after,
        int take,
        CancellationToken cancellationToken) =>
        ReadConsistentlyAsync(
            async (generation, token) =>
            {
                var cursor = after?.Value ?? Guid.Empty;
                var rows = await db.Database.SqlQuery<PageRow>(
                    $"""
                    SELECT n.id AS "Id", n.parent_id AS "ParentId", n.value AS "Value", n.is_deleted AS "IsDeleted",
                           EXISTS (SELECT 1 FROM nodes AS c WHERE c.parent_id = n.id) AS "HasChildren"
                    FROM nodes AS n
                    WHERE n.parent_id IS NULL AND n.id > {cursor}
                    ORDER BY n.id
                    LIMIT {take}
                    """).ToListAsync(token);
                return new TreeRead<IReadOnlyList<NodeListItem>>(generation, ToItems(rows, false));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<TreeRead<IReadOnlyList<NodeListItem>?>> GetChildrenAsync(
        NodeId parentId,
        NodeId? after,
        int take,
        CancellationToken cancellationToken) =>
        ReadConsistentlyAsync(
            async (generation, token) =>
            {
                var parent = (await db.Database.SqlQuery<ParentStateRow>(
                    $"""
                    WITH RECURSIVE chain (id, parent_id, is_deleted) AS (
                        SELECT n.id, n.parent_id, n.is_deleted FROM nodes AS n WHERE n.id = {parentId.Value}
                        UNION
                        SELECT p.id, p.parent_id, p.is_deleted
                        FROM nodes AS p
                        JOIN chain AS c ON p.id = c.parent_id
                        WHERE NOT c.is_deleted
                    )
                    SELECT count(*) > 0 AS "Exists", coalesce(bool_or(is_deleted), false) AS "IsDeleted"
                    FROM chain
                    """).ToListAsync(token)).Single();
                if (!parent.Exists)
                {
                    return new TreeRead<IReadOnlyList<NodeListItem>?>(generation, null);
                }

                var cursor = after?.Value ?? Guid.Empty;
                var rows = await db.Database.SqlQuery<PageRow>(
                    $"""
                    SELECT n.id AS "Id", n.parent_id AS "ParentId", n.value AS "Value", n.is_deleted AS "IsDeleted",
                           EXISTS (SELECT 1 FROM nodes AS c WHERE c.parent_id = n.id) AS "HasChildren"
                    FROM nodes AS n
                    WHERE n.parent_id = {parentId.Value} AND n.id > {cursor}
                    ORDER BY n.id
                    LIMIT {take}
                    """).ToListAsync(token);
                return new TreeRead<IReadOnlyList<NodeListItem>?>(generation, ToItems(rows, parent.IsDeleted));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<TreeRead<NodeDetails?>> GetNodeAsync(NodeId id, CancellationToken cancellationToken) =>
        ReadConsistentlyAsync(
            async (generation, token) =>
            {
                var node = (await db.Database.SqlQuery<NodeRow>(
                    $"""
                    SELECT n.id AS "Id", n.parent_id AS "ParentId", n.value AS "Value", n.version AS "Version",
                           n.is_deleted AS "IsDeletedFlag"
                    FROM nodes AS n
                    WHERE n.id = {id.Value}
                    """).ToListAsync(token)).SingleOrDefault();
                if (node is null)
                {
                    return new TreeRead<NodeDetails?>(generation, null);
                }

                var chain = await ReadChainAsync(id, token);
                return new TreeRead<NodeDetails?>(generation, Describe(node, chain));
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<TreeSnapshot> ReadSnapshotAsync(
        IReadOnlyCollection<NodeId> lockedIds,
        IReadOnlyCollection<NodeId> otherIds,
        CancellationToken cancellationToken)
    {
        if (lockedIds.Count > 0)
        {
            var ids = lockedIds.Select(id => id.Value).ToArray();
            await db.Database.SqlQuery<Guid>(
                $"""
                SELECT n.id AS "Value"
                FROM nodes AS n
                WHERE n.id = ANY({ids})
                ORDER BY n.id
                FOR NO KEY UPDATE
                """).ToListAsync(cancellationToken);
        }

        var ancestry = await ReadAncestryAsync(lockedIds.Concat(otherIds).Distinct().ToList(), cancellationToken);
        return TreeSnapshot.FromAncestry(ancestry);
    }

    /// <summary>
    /// Reads the given nodes and all their ancestors, each distinct row once. The walk stops above a node whose own
    /// flag is set, because nothing above it can change the result.
    /// </summary>
    internal async Task<List<StoredNode>> ReadAncestryAsync(
        IReadOnlyCollection<NodeId> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var start = ids.Select(id => id.Value).ToArray();
        var rows = await db.Database.SqlQuery<AncestryRow>(
            $"""
            WITH RECURSIVE walk (id, parent_id, version, is_deleted) AS (
                SELECT n.id, n.parent_id, n.version, n.is_deleted FROM nodes AS n WHERE n.id = ANY({start})
                UNION
                SELECT p.id, p.parent_id, p.version, p.is_deleted
                FROM nodes AS p
                JOIN walk AS w ON p.id = w.parent_id
                WHERE NOT w.is_deleted
            )
            SELECT id AS "Id", parent_id AS "ParentId", version AS "Version", is_deleted AS "IsDeletedFlag"
            FROM walk
            """).ToListAsync(cancellationToken);
        return ToStoredNodes(rows);
    }

    private async Task<List<StoredNode>> ReadChainAsync(NodeId id, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<AncestryRow>(
            $"""
            WITH RECURSIVE chain (id, parent_id, version, is_deleted) AS (
                SELECT n.id, n.parent_id, n.version, n.is_deleted FROM nodes AS n WHERE n.id = {id.Value}
                UNION
                SELECT p.id, p.parent_id, p.version, p.is_deleted
                FROM nodes AS p
                JOIN chain AS c ON p.id = c.parent_id
            )
            SELECT id AS "Id", parent_id AS "ParentId", version AS "Version", is_deleted AS "IsDeletedFlag"
            FROM chain
            """).ToListAsync(cancellationToken);
        return ToStoredNodes(rows);
    }

    private static List<StoredNode> ToStoredNodes(List<AncestryRow> rows) =>
        rows
            .Select(row => new StoredNode(
                NodeId.Create(row.Id),
                row.ParentId is { } parentId ? NodeId.Create(parentId) : null,
                NodeVersion.Create(row.Version),
                row.IsDeletedFlag))
            .ToList();

    private async Task<long> ReadGenerationAsync(CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<long>(
            $"""
            SELECT s.generation AS "Value" FROM tree_state AS s WHERE s.id = 1
            """).ToListAsync(cancellationToken)).Single();

    private async Task<T> ReadConsistentlyAsync<T>(
        Func<long, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await read(await ReadGenerationAsync(cancellationToken), cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var generation = await BeginSnapshotAsync(cancellationToken);
        var result = await read(generation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Prepares the new repeatable-read transaction in one round trip: read-only, the timeouts, the share locks, and
    /// then the first query, which takes the snapshot and returns the generation.
    /// </summary>
    private async Task<long> BeginSnapshotAsync(CancellationToken cancellationToken)
    {
        var sql = string.Join(
            ";\n",
            "SET TRANSACTION READ ONLY",
            SessionTimeouts.SetLocal(options),
            "LOCK TABLE tree_state, nodes IN ACCESS SHARE MODE",
            "SELECT s.generation AS \"Value\" FROM tree_state AS s WHERE s.id = 1");
        return (await db.Database.SqlQueryRaw<long>(sql).ToListAsync(cancellationToken)).Single();
    }

    private static List<NodeListItem> ToItems(List<PageRow> rows, bool parentIsDeleted) =>
        rows
            .Select(row => new NodeListItem(
                NodeId.Create(row.Id),
                row.ParentId is { } parentId ? NodeId.Create(parentId) : null,
                row.Value,
                parentIsDeleted || row.IsDeleted,
                row.HasChildren))
            .ToList();

    private static NodeDetails Describe(NodeRow node, List<StoredNode> chain)
    {
        var byId = chain.ToDictionary(row => row.Id);
        var ancestorIds = new List<NodeId>();
        var isDeleted = node.IsDeletedFlag;
        var current = node.ParentId is { } parentId ? NodeId.Create(parentId) : (NodeId?)null;
        while (current is { } ancestorId && byId.TryGetValue(ancestorId, out var ancestor) && ancestorIds.Count < byId.Count)
        {
            ancestorIds.Add(ancestorId);
            isDeleted |= ancestor.IsDeletedFlag;
            current = ancestor.ParentId;
        }

        return new NodeDetails(
            NodeId.Create(node.Id),
            node.ParentId is { } parent ? NodeId.Create(parent) : null,
            node.Value,
            NodeVersion.Create(node.Version),
            isDeleted,
            ancestorIds);
    }

    private sealed class PageRow
    {
        public Guid Id { get; init; }

        public Guid? ParentId { get; init; }

        public string Value { get; init; } = string.Empty;

        public bool IsDeleted { get; init; }

        public bool HasChildren { get; init; }
    }

    private sealed class ParentStateRow
    {
        public bool Exists { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class NodeRow
    {
        public Guid Id { get; init; }

        public Guid? ParentId { get; init; }

        public string Value { get; init; } = string.Empty;

        public long Version { get; init; }

        public bool IsDeletedFlag { get; init; }
    }

    private sealed class AncestryRow
    {
        public Guid Id { get; init; }

        public Guid? ParentId { get; init; }

        public long Version { get; init; }

        public bool IsDeletedFlag { get; init; }
    }
}
