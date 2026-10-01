using System.Diagnostics.CodeAnalysis;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Changes;

/// <summary>
/// A structurally valid set of changes to apply atomically. The factory checks only rules that can be decided
/// without the database: ids and values, parent-first order of new nodes, duplicates, and the size limit.
/// </summary>
public sealed class ChangeSet
{
    /// <summary>Maximum number of operations (creations, edits, and deletions together).</summary>
    public const int MaxOperations = 10_000;

    private ChangeSet(IReadOnlyList<NewNode> created, IReadOnlyList<NodeEdit> updated, IReadOnlyList<NodeId> deleted)
    {
        Created = created;
        Updated = updated;
        Deleted = deleted;
    }

    /// <summary>New nodes, each listed after its parent when the parent is new too.</summary>
    public IReadOnlyList<NewNode> Created { get; }

    /// <summary>Value edits of existing nodes.</summary>
    public IReadOnlyList<NodeEdit> Updated { get; }

    /// <summary>Existing nodes whose subtrees are deleted.</summary>
    public IReadOnlyList<NodeId> Deleted { get; }

    /// <summary>Number of operations in the set.</summary>
    public int OperationCount => Created.Count + Updated.Count + Deleted.Count;

    /// <summary>Validates client drafts and builds a change set, or reports every structural error found.</summary>
    public static bool TryCreate(
        IReadOnlyList<NodeCreationDraft> created,
        IReadOnlyList<NodeEditDraft> updated,
        IReadOnlyList<Guid> deleted,
        [NotNullWhen(true)] out ChangeSet? changeSet,
        out IReadOnlyList<ChangeSetError> errors)
    {
        changeSet = null;
        if (created.Count + updated.Count + deleted.Count > MaxOperations)
        {
            errors = [new ChangeSetError(string.Empty, ChangeSetErrorCode.TooManyOperations)];
            return false;
        }

        var found = new List<ChangeSetError>();
        var createdIds = new HashSet<Guid>(created.Select(draft => draft.Id));
        var newNodes = ReadCreated(created, found);
        var edits = ReadUpdated(updated, createdIds, found);
        var deletions = ReadDeleted(deleted, createdIds, found);

        errors = found;
        if (found.Count > 0)
        {
            return false;
        }

        changeSet = new ChangeSet(newNodes, edits, deletions);
        return true;
    }

    /// <summary>Existing nodes that the set edits or deletes. They are locked before the set is planned.</summary>
    public IReadOnlyList<NodeId> ExistingTargets() =>
        Updated.Select(edit => edit.Id).Concat(Deleted).Distinct().ToList();

    /// <summary>Ids of new nodes and the existing parents they are created under.</summary>
    public IReadOnlyList<NodeId> CreationReferences()
    {
        var newIds = Created.Select(node => node.Id).ToHashSet();
        return Created
            .Select(node => node.Id)
            .Concat(Created.Select(node => node.ParentId).Where(parentId => !newIds.Contains(parentId)))
            .Distinct()
            .ToList();
    }

    private static List<NewNode> ReadCreated(IReadOnlyList<NodeCreationDraft> drafts, List<ChangeSetError> errors)
    {
        var positions = new Dictionary<Guid, int>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            if (!positions.TryAdd(drafts[index].Id, index))
            {
                errors.Add(new ChangeSetError($"created[{index}].id", ChangeSetErrorCode.DuplicateId));
            }
        }

        var nodes = new List<NewNode>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            var path = $"created[{index}]";
            var idIsValid = ReadId(draft.Id, $"{path}.id", errors, out var id);
            var parentIsValid = ReadParent(draft.ParentId, index, positions, $"{path}.parentId", errors, out var parentId);
            var value = ReadValue(draft.Value, $"{path}.value", errors);
            if (idIsValid && parentIsValid && value is not null)
            {
                nodes.Add(new NewNode(id, parentId, value));
            }
        }

        return nodes;
    }

    private static List<NodeEdit> ReadUpdated(
        IReadOnlyList<NodeEditDraft> drafts,
        HashSet<Guid> createdIds,
        List<ChangeSetError> errors)
    {
        var seen = new HashSet<Guid>();
        var edits = new List<NodeEdit>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            var path = $"updated[{index}]";
            var idIsValid = ReadTarget(draft.Id, $"{path}.id", seen, createdIds, errors, out var id);
            var versionIsValid = NodeVersion.TryCreate(draft.ExpectedVersion, out var expectedVersion);
            if (!versionIsValid)
            {
                errors.Add(new ChangeSetError($"{path}.expectedVersion", ChangeSetErrorCode.InvalidVersion));
            }

            var value = ReadValue(draft.Value, $"{path}.value", errors);
            if (idIsValid && versionIsValid && value is not null)
            {
                edits.Add(new NodeEdit(id, value, expectedVersion));
            }
        }

        return edits;
    }

    private static List<NodeId> ReadDeleted(
        IReadOnlyList<Guid> drafts,
        HashSet<Guid> createdIds,
        List<ChangeSetError> errors)
    {
        var seen = new HashSet<Guid>();
        var deletions = new List<NodeId>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            if (ReadTarget(drafts[index], $"deleted[{index}]", seen, createdIds, errors, out var id))
            {
                deletions.Add(id);
            }
        }

        return deletions;
    }

    private static bool ReadTarget(
        Guid raw,
        string path,
        HashSet<Guid> seen,
        HashSet<Guid> createdIds,
        List<ChangeSetError> errors,
        out NodeId id)
    {
        var isValid = ReadId(raw, path, errors, out id);
        if (!seen.Add(raw))
        {
            errors.Add(new ChangeSetError(path, ChangeSetErrorCode.DuplicateId));
            isValid = false;
        }

        if (createdIds.Contains(raw))
        {
            errors.Add(new ChangeSetError(path, ChangeSetErrorCode.CreatedAndChanged));
            isValid = false;
        }

        return isValid;
    }

    private static bool ReadParent(
        Guid? raw,
        int childPosition,
        Dictionary<Guid, int> positions,
        string path,
        List<ChangeSetError> errors,
        out NodeId parentId)
    {
        parentId = default;
        if (raw is not { } parent)
        {
            errors.Add(new ChangeSetError(path, ChangeSetErrorCode.MissingParent));
            return false;
        }

        if (positions.TryGetValue(parent, out var parentPosition) && parentPosition >= childPosition)
        {
            errors.Add(new ChangeSetError(path, ChangeSetErrorCode.ForwardReference));
            return false;
        }

        return ReadId(parent, path, errors, out parentId);
    }

    private static bool ReadId(Guid raw, string path, List<ChangeSetError> errors, out NodeId id)
    {
        if (NodeId.TryCreate(raw, out id))
        {
            return true;
        }

        errors.Add(new ChangeSetError(path, ChangeSetErrorCode.InvalidId));
        return false;
    }

    private static NodeValue? ReadValue(string? text, string path, List<ChangeSetError> errors)
    {
        var error = NodeValue.Validate(text);
        if (error is null)
        {
            return NodeValue.Create(text!);
        }

        errors.Add(new ChangeSetError(path, error switch
        {
            NodeValueError.Empty => ChangeSetErrorCode.EmptyValue,
            NodeValueError.TooLong => ChangeSetErrorCode.ValueTooLong,
            NodeValueError.SurroundingWhitespace => ChangeSetErrorCode.ValueHasSurroundingWhitespace,
            _ => ChangeSetErrorCode.ValueHasNullCharacter,
        }));
        return null;
    }
}
