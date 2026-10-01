namespace TreeEditor.Application.Abstractions;

/// <summary>Handles one kind of read-only query.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Executes <paramref name="query"/>.</summary>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
