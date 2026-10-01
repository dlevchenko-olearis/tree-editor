namespace TreeEditor.Domain.Changes;

/// <summary>Reasons a change set is structurally invalid.</summary>
public enum ChangeSetErrorCode
{
    TooManyOperations,
    InvalidId,
    MissingParent,
    ForwardReference,
    DuplicateId,
    CreatedAndChanged,
    EmptyValue,
    ValueTooLong,
    ValueHasSurroundingWhitespace,
    ValueHasNullCharacter,
    InvalidVersion,
}

/// <summary>One structural problem of a change set, located by a JSON-like path such as <c>created[2].parentId</c>.</summary>
public sealed record ChangeSetError(string Path, ChangeSetErrorCode Code);
