namespace TreeEditor.Application.Ports;

/// <summary>A database transaction shared by the ports used within one command.</summary>
public interface IUnitOfWork
{
    /// <summary>Starts the transaction.</summary>
    Task BeginAsync(CancellationToken cancellationToken);

    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls the transaction back, if one is active.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
