using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>
/// Stores successful Apply requests in <c>apply_requests</c>. The key is claimed with
/// <c>INSERT ... ON CONFLICT DO NOTHING</c>, which waits for a concurrent request holding the same key, and the
/// request is identified by a SHA-256 hash of its canonical form.
/// </summary>
internal sealed class PostgresIdempotencyStore(TreeDbContext db) : IIdempotencyStore
{
    private const int ClaimAttempts = 2;

    /// <inheritdoc />
    public async Task<IdempotencyClaim> ClaimAsync(ApplyChangesCommand command, CancellationToken cancellationToken)
    {
        var key = command.IdempotencyKey;
        var hash = Hash(command);
        for (var attempt = 0; attempt < ClaimAttempts; attempt++)
        {
            var inserted = await db.Database.SqlQuery<Guid>(
                $"""
                INSERT INTO apply_requests (key, request_hash, response, created_at)
                VALUES ({key}, {hash}, NULL, now())
                ON CONFLICT (key) DO NOTHING
                RETURNING key AS "Value"
                """).ToListAsync(cancellationToken);
            if (inserted.Count == 1)
            {
                return new IdempotencyClaim.Claimed();
            }

            var stored = (await db.Database.SqlQuery<StoredRequestRow>(
                $"""
                SELECT r.request_hash AS "RequestHash", r.response::text AS "Response"
                FROM apply_requests AS r
                WHERE r.key = {key}
                """).ToListAsync(cancellationToken)).SingleOrDefault();
            if (stored is null)
            {
                continue;
            }

            if (!CryptographicOperations.FixedTimeEquals(stored.RequestHash, hash))
            {
                return new IdempotencyClaim.Mismatch();
            }

            return new IdempotencyClaim.Replay(Deserialize(
                stored.Response ?? throw new InvalidOperationException("A committed Apply request has no response.")));
        }

        throw new InvalidOperationException("The idempotency key could not be claimed.");
    }

    /// <inheritdoc />
    public Task SaveAsync(Guid key, ApplyOutcome.Applied outcome, CancellationToken cancellationToken)
    {
        var response = Serialize(outcome);
        return db.Database.ExecuteSqlAsync(
            $"""
            UPDATE apply_requests SET response = CAST({response} AS jsonb) WHERE key = {key}
            """,
            cancellationToken);
    }

    /// <summary>SHA-256 of the generation and every operation, in request order, with length-prefixed fields.</summary>
    internal static byte[] Hash(ApplyChangesCommand command)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var changes = command.ChangeSet;
        AppendInt64(hash, command.Generation);
        AppendInt64(hash, changes.Created.Count);
        foreach (var node in changes.Created)
        {
            AppendId(hash, node.Id);
            AppendId(hash, node.ParentId);
            AppendText(hash, node.Value);
        }

        AppendInt64(hash, changes.Updated.Count);
        foreach (var edit in changes.Updated)
        {
            AppendId(hash, edit.Id);
            AppendInt64(hash, edit.ExpectedVersion.Value);
            AppendText(hash, edit.Value);
        }

        AppendInt64(hash, changes.Deleted.Count);
        foreach (var id in changes.Deleted)
        {
            AppendId(hash, id);
        }

        return hash.GetHashAndReset();
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendId(IncrementalHash hash, NodeId id)
    {
        Span<byte> buffer = stackalloc byte[16];
        id.Value.TryWriteBytes(buffer, bigEndian: true, out _);
        hash.AppendData(buffer);
    }

    private static void AppendText(IncrementalHash hash, NodeValue value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.Value);
        AppendInt64(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static string Serialize(ApplyOutcome.Applied outcome) =>
        JsonSerializer.Serialize(new StoredResponse(
            outcome.Generation,
            outcome.Created.Select(node => new StoredVersion(node.Id.Value, node.Version.Value)).ToList(),
            outcome.Updated.Select(node => new StoredVersion(node.Id.Value, node.Version.Value)).ToList(),
            outcome.Deleted.Select(id => id.Value).ToList()));

    private static ApplyOutcome.Applied Deserialize(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredResponse>(json)
            ?? throw new InvalidOperationException("A stored Apply response is empty.");
        return new ApplyOutcome.Applied(
            stored.Generation,
            stored.Created.Select(ToWrittenNode).ToList(),
            stored.Updated.Select(ToWrittenNode).ToList(),
            stored.Deleted.Select(NodeId.Create).ToList());
    }

    private static WrittenNode ToWrittenNode(StoredVersion node) =>
        new(NodeId.Create(node.Id), NodeVersion.Create(node.Version));

    private sealed record StoredResponse(
        long Generation,
        List<StoredVersion> Created,
        List<StoredVersion> Updated,
        List<Guid> Deleted);

    private sealed record StoredVersion(Guid Id, long Version);

    private sealed class StoredRequestRow
    {
        public byte[] RequestHash { get; init; } = [];

        public string? Response { get; init; }
    }
}
