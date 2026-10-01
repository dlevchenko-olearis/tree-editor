namespace TreeEditor.Api.Composition;

/// <summary>HTTP-level limits of the API, bound from the <c>Api</c> configuration section.</summary>
public sealed class ApiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string Section = "Api";

    /// <summary>Largest accepted request body; nginx uses the same limit.</summary>
    public long MaxRequestBodyBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Mutating requests allowed per client IP within one window.</summary>
    public int MutationPermitLimit { get; set; } = 60;

    /// <summary>Length of the rate-limiting window in seconds.</summary>
    public int MutationWindowSeconds { get; set; } = 60;

    /// <summary>Seconds a client should wait before retrying after a transient database failure.</summary>
    public int TransientRetryAfterSeconds { get; set; } = 1;
}
