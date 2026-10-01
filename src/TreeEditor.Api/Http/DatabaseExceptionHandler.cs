using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using TreeEditor.Api.Composition;

namespace TreeEditor.Api.Http;

/// <summary>
/// Maps database failures to HTTP responses. Deadlocks, serialization failures, and lock or statement timeouts are
/// transient: the client gets 503 with <c>Retry-After</c> and retries with the same idempotency key. Anything else is
/// left to the default handler, which answers 500.
/// </summary>
public sealed class DatabaseExceptionHandler(IOptions<ApiOptions> options, GenerationProbe probe) : IExceptionHandler
{
    private static readonly HashSet<string> TransientStates =
    [
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.SerializationFailure,
        PostgresErrorCodes.LockNotAvailable,
        PostgresErrorCodes.QueryCanceled,
    ];

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!IsTransient(exception))
        {
            return false;
        }

        httpContext.Response.Headers.RetryAfter =
            options.Value.TransientRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await ApiProblems.WriteAsync(
            httpContext,
            ApiProblems.Create(
                StatusCodes.Status503ServiceUnavailable,
                "The database is busy. Retry the request with the same idempotency key.",
                await probe.TryReadAsync(cancellationToken)));
        return true;
    }

    private static bool IsTransient(Exception exception) =>
        Find<PostgresException>(exception) is { } database
            ? TransientStates.Contains(database.SqlState)
            : Find<NpgsqlException>(exception) is { IsTransient: true } || Find<TimeoutException>(exception) is not null;

    private static T? Find<T>(Exception exception)
        where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
