namespace TreeEditor.Application.Abstractions;

/// <summary>Result of a command. Only successful outcomes are committed.</summary>
public interface ICommandOutcome
{
    /// <summary>Tells whether the work done by the command must be committed.</summary>
    bool IsSuccess { get; }

    /// <summary>Tree generation observed by the command.</summary>
    long Generation { get; }
}
