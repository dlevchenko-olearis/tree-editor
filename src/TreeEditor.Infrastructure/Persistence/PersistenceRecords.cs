namespace TreeEditor.Infrastructure.Persistence;

/// <summary>Row of the <c>nodes</c> table.</summary>
internal sealed class NodeRecord
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string Value { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public long Version { get; set; }
}

/// <summary>The single row of the <c>tree_state</c> table.</summary>
internal sealed class TreeStateRecord
{
    public int Id { get; set; }

    public long Generation { get; set; }
}

/// <summary>Row of the <c>apply_requests</c> table, keyed by idempotency key.</summary>
internal sealed class ApplyRequestRecord
{
    public Guid Key { get; set; }

    public byte[] RequestHash { get; set; } = [];

    public string? Response { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
