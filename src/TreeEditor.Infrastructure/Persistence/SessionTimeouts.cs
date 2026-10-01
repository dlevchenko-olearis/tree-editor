using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>Applies the statement and lock timeouts to the current transaction only.</summary>
internal static class SessionTimeouts
{
    /// <summary>Runs <see cref="SetLocal"/> in the current transaction.</summary>
    public static Task ApplyAsync(TreeDbContext db, InfrastructureOptions options, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(SetLocal(options), cancellationToken);

    /// <summary>
    /// <c>SET LOCAL</c> statements for both timeouts. The values are whole milliseconds formatted from the options,
    /// so the text needs no parameters, and unlike a <c>SELECT set_config(...)</c> it does not take a snapshot.
    /// </summary>
    public static string SetLocal(InfrastructureOptions options) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"SET LOCAL statement_timeout = {Milliseconds(options.StatementTimeout)}; SET LOCAL lock_timeout = {Milliseconds(options.LockTimeout)}");

    private static long Milliseconds(TimeSpan timeout) => (long)timeout.TotalMilliseconds;
}
