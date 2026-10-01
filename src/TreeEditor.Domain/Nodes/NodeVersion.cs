namespace TreeEditor.Domain.Nodes;

/// <summary>Version of a stored node. Every write assigns a new, never repeated, positive version.</summary>
public readonly record struct NodeVersion
{
    private NodeVersion(long value) => Value = value;

    /// <summary>The version number.</summary>
    public long Value { get; }

    /// <summary>Creates a version when <paramref name="value"/> is positive.</summary>
    public static bool TryCreate(long value, out NodeVersion version)
    {
        version = value > 0 ? new NodeVersion(value) : default;
        return value > 0;
    }

    /// <summary>Creates a version from a value known to be positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public static NodeVersion Create(long value) =>
        TryCreate(value, out var version) ? version : throw new ArgumentOutOfRangeException(nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
