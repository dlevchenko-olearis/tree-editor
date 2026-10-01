namespace TreeEditor.Domain.Changes;

/// <summary>Unvalidated request to create a node, as received from a client.</summary>
public sealed record NodeCreationDraft(Guid Id, Guid? ParentId, string? Value);

/// <summary>Unvalidated request to change the value of a node, as received from a client.</summary>
public sealed record NodeEditDraft(Guid Id, string? Value, long ExpectedVersion);
