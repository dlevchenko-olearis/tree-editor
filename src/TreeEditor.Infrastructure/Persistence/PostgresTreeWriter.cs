using Microsoft.EntityFrameworkCore;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>
/// Set-based writes to the tree. Every written row takes a new version from <c>node_version_seq</c>, which is never
/// restarted, so a version is never reused, even across Resets.
/// </summary>
internal sealed class PostgresTreeWriter(TreeDbContext db) : ITreeWriter
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<WrittenNode>> InsertAsync(
        IReadOnlyList<NewNode> nodes,
        CancellationToken cancellationToken)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        var ids = nodes.Select(node => node.Id.Value).ToArray();
        var parentIds = nodes.Select(node => node.ParentId.Value).ToArray();
        var values = nodes.Select(node => node.Value.Value).ToArray();
        var rows = await db.Database.SqlQuery<WrittenRow>(
            $"""
            INSERT INTO nodes (id, parent_id, value, is_deleted, version)
            SELECT u.id, u.parent_id, u.value, false, nextval('node_version_seq')
            FROM unnest({ids}, {parentIds}, {values}) WITH ORDINALITY AS u (id, parent_id, value, position)
            ORDER BY u.position
            ON CONFLICT (id) DO NOTHING
            RETURNING id AS "Id", version AS "Version"
            """).ToListAsync(cancellationToken);
        return ToWrittenNodes(rows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WrittenNode>> UpdateAsync(
        IReadOnlyList<NodeEdit> edits,
        CancellationToken cancellationToken)
    {
        if (edits.Count == 0)
        {
            return [];
        }

        var ids = edits.Select(edit => edit.Id.Value).ToArray();
        var values = edits.Select(edit => edit.Value.Value).ToArray();
        var expectedVersions = edits.Select(edit => edit.ExpectedVersion.Value).ToArray();
        var rows = await db.Database.SqlQuery<WrittenRow>(
            $"""
            UPDATE nodes AS n
            SET value = u.value, version = nextval('node_version_seq')
            FROM unnest({ids}, {values}, {expectedVersions}) AS u (id, value, expected_version)
            WHERE n.id = u.id AND n.version = u.expected_version
            RETURNING n.id AS "Id", n.version AS "Version"
            """).ToListAsync(cancellationToken);
        return ToWrittenNodes(rows);
    }

    /// <inheritdoc />
    public async Task MarkDeletedAsync(IReadOnlyList<NodeId> roots, CancellationToken cancellationToken)
    {
        if (roots.Count == 0)
        {
            return;
        }

        var ids = roots.Select(id => id.Value).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE nodes
            SET is_deleted = true, version = nextval('node_version_seq')
            WHERE id = ANY({ids}) AND NOT is_deleted
            """,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The ACCESS EXCLUSIVE locks are taken in the order every other transaction takes its locks:
    /// <c>apply_requests</c>, <c>tree_state</c>, then <c>nodes</c>. The lock on <c>tree_state</c> waits for every
    /// transaction that has read the generation, so an Apply that passed its generation check finishes first.
    /// </remarks>
    public async Task<long> ReplaceAllAsync(IReadOnlyList<SeedNode> nodes, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"LOCK TABLE apply_requests, tree_state, nodes IN ACCESS EXCLUSIVE MODE",
            cancellationToken);
        await db.Database.ExecuteSqlAsync($"TRUNCATE apply_requests, nodes", cancellationToken);
        await InsertSeedAsync(nodes, cancellationToken);
        return (await db.Database.SqlQuery<long>(
            $"""
            UPDATE tree_state SET generation = generation + 1 WHERE id = 1 RETURNING generation AS "Value"
            """).ToListAsync(cancellationToken)).Single();
    }

    /// <summary>Inserts sample nodes, roots included, in the given parent-first order.</summary>
    internal Task InsertSeedAsync(IReadOnlyList<SeedNode> nodes, CancellationToken cancellationToken)
    {
        var ids = nodes.Select(node => node.Id.Value).ToArray();
        var parentIds = nodes.Select(node => node.ParentId?.Value).ToArray();
        var values = nodes.Select(node => node.Value.Value).ToArray();
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO nodes (id, parent_id, value, is_deleted, version)
            SELECT u.id, u.parent_id, u.value, false, nextval('node_version_seq')
            FROM unnest({ids}, {parentIds}, {values}) WITH ORDINALITY AS u (id, parent_id, value, position)
            ORDER BY u.position
            """,
            cancellationToken);
    }

    private static List<WrittenNode> ToWrittenNodes(List<WrittenRow> rows) =>
        rows.Select(row => new WrittenNode(NodeId.Create(row.Id), NodeVersion.Create(row.Version))).ToList();

    private sealed class WrittenRow
    {
        public Guid Id { get; init; }

        public long Version { get; init; }
    }
}
