namespace TreeEditor.Domain.Nodes;

/// <summary>Identifier of a node: a version 7 UUID with the RFC 9562 variant, never nil.</summary>
public readonly record struct NodeId
{
    private NodeId(Guid value) => Value = value;

    /// <summary>The underlying UUID.</summary>
    public Guid Value { get; }

    /// <summary>Tells whether <paramref name="value"/> is a version 7 UUID with the RFC variant.</summary>
    public static bool IsValid(Guid value) => value.Version == 7 && (value.Variant & 0b1100) == 0b1000;

    /// <summary>Creates an identifier when <paramref name="value"/> is valid.</summary>
    public static bool TryCreate(Guid value, out NodeId id)
    {
        id = IsValid(value) ? new NodeId(value) : default;
        return IsValid(value);
    }

    /// <summary>Creates an identifier from a value known to be valid.</summary>
    /// <exception cref="ArgumentException">The value is not a version 7 UUID.</exception>
    public static NodeId Create(Guid value) =>
        TryCreate(value, out var id) ? id : throw new ArgumentException("A node id must be a version 7 UUID.", nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
