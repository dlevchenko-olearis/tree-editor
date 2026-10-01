namespace TreeEditor.Application.Nodes;

/// <summary>Number of rows in one page, between <see cref="Min"/> and <see cref="Max"/>.</summary>
public readonly record struct PageLimit
{
    /// <summary>Smallest allowed page size.</summary>
    public const int Min = 1;

    /// <summary>Largest allowed page size.</summary>
    public const int Max = 200;

    /// <summary>Page size used when the client does not ask for one.</summary>
    public const int Default = 20;

    private PageLimit(int value) => Value = value;

    /// <summary>The page size.</summary>
    public int Value { get; }

    /// <summary>Creates a limit when <paramref name="value"/> is within bounds.</summary>
    public static bool TryCreate(int value, out PageLimit limit)
    {
        var isValid = value is >= Min and <= Max;
        limit = isValid ? new PageLimit(value) : default;
        return isValid;
    }

    /// <summary>Creates a limit from a value known to be within bounds.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is out of bounds.</exception>
    public static PageLimit Create(int value) =>
        TryCreate(value, out var limit) ? limit : throw new ArgumentOutOfRangeException(nameof(value));
}
