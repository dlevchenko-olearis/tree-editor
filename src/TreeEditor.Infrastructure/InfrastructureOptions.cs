namespace TreeEditor.Infrastructure;

/// <summary>Settings of the PostgreSQL adapters and hosted jobs.</summary>
public sealed record InfrastructureOptions
{
    /// <summary>Npgsql connection string.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Longest time a single statement may run.</summary>
    public TimeSpan StatementTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Longest time a statement may wait for a lock.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long successful Apply requests are remembered for idempotent replays.</summary>
    public TimeSpan IdempotencyRetention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How often expired Apply requests are removed.</summary>
    public TimeSpan RetentionInterval { get; init; } = TimeSpan.FromHours(1);
}
