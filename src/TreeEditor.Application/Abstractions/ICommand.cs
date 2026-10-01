namespace TreeEditor.Application.Abstractions;

/// <summary>A request that changes the tree.</summary>
public interface ICommand
{
    /// <summary>Ids and counts that describe the command in logs. Never contains node values.</summary>
    IReadOnlyDictionary<string, object?> LogState { get; }
}
