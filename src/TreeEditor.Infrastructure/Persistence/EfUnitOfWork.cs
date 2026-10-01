using System.Data;
using Microsoft.EntityFrameworkCore;
using TreeEditor.Application.Ports;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>A READ COMMITTED transaction on the scoped context, with statement and lock timeouts.</summary>
internal sealed class EfUnitOfWork(TreeDbContext db, InfrastructureOptions options) : IUnitOfWork
{
    /// <inheritdoc />
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await SessionTimeouts.ApplyAsync(db, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken) =>
        db.Database.CommitTransactionAsync(cancellationToken);

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is null
            ? Task.CompletedTask
            : db.Database.RollbackTransactionAsync(cancellationToken);
}
