namespace TreeEditor.Domain.Nodes;

/// <summary>
/// Value of a node. It is validated but never transformed: non-empty, at most <see cref="MaxLength"/> UTF-16 code
/// units, without a leading or trailing character from <see cref="SharedWhitespace"/>, and without U+0000, which
/// PostgreSQL text cannot store.
/// </summary>
public sealed record NodeValue
{
    /// <summary>Maximum length in UTF-16 code units.</summary>
    public const int MaxLength = 200;

    private const char NullCharacter = (char)0x0000;

    private NodeValue(string value) => Value = value;

    /// <summary>The text exactly as received.</summary>
    public string Value { get; }

    /// <summary>Returns why <paramref name="value"/> is invalid, or <c>null</c> when it is valid.</summary>
    public static NodeValueError? Validate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return NodeValueError.Empty;
        }

        if (value.Length > MaxLength)
        {
            return NodeValueError.TooLong;
        }

        if (SharedWhitespace.Contains(value[0]) || SharedWhitespace.Contains(value[^1]))
        {
            return NodeValueError.SurroundingWhitespace;
        }

        if (value.Contains(NullCharacter, StringComparison.Ordinal))
        {
            return NodeValueError.NullCharacter;
        }

        return null;
    }

    /// <summary>Creates a value from text known to be valid.</summary>
    /// <exception cref="ArgumentException">The text is not a valid node value.</exception>
    public static NodeValue Create(string value) =>
        Validate(value) is { } error
            ? throw new ArgumentException($"Invalid node value: {error}.", nameof(value))
            : new NodeValue(value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
