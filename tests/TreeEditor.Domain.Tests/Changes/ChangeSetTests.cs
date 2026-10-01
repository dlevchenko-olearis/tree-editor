using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Tests.Changes;

public sealed class ChangeSetTests
{
    private static readonly IReadOnlyList<NodeCreationDraft> NoCreations = [];
    private static readonly IReadOnlyList<NodeEditDraft> NoEdits = [];
    private static readonly IReadOnlyList<Guid> NoDeletions = [];

    [Fact]
    public void Accepts_a_chain_of_nested_new_nodes()
    {
        var existing = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var valid = ChangeSet.TryCreate(
            [new(first, existing, "N1"), new(second, first, "N2")],
            NoEdits,
            NoDeletions,
            out var changeSet,
            out var errors);

        Assert.True(valid, string.Join(", ", errors));
        Assert.Collection(
            changeSet!.Created,
            node => Assert.Equal((first, existing, "N1"), (node.Id.Value, node.ParentId.Value, node.Value.Value)),
            node => Assert.Equal((second, first, "N2"), (node.Id.Value, node.ParentId.Value, node.Value.Value)));
    }

    [Fact]
    public void Rejects_a_new_node_without_a_parent()
    {
        var errors = Reject([new(Guid.CreateVersion7(), null, "root")], NoEdits, NoDeletions);

        Assert.Equal([new ChangeSetError("created[0].parentId", ChangeSetErrorCode.MissingParent)], errors);
    }

    [Fact]
    public void Rejects_a_forward_reference_among_new_nodes()
    {
        var existing = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var errors = Reject([new(second, first, "N2"), new(first, existing, "N1")], NoEdits, NoDeletions);

        Assert.Equal([new ChangeSetError("created[0].parentId", ChangeSetErrorCode.ForwardReference)], errors);
    }

    [Fact]
    public void Rejects_a_cycle_between_two_new_nodes()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var errors = Reject([new(a, b, "A"), new(b, a, "B")], NoEdits, NoDeletions);

        Assert.Contains(new ChangeSetError("created[0].parentId", ChangeSetErrorCode.ForwardReference), errors);
    }

    [Fact]
    public void Rejects_a_new_node_that_is_its_own_parent()
    {
        var a = Guid.CreateVersion7();

        var errors = Reject([new(a, a, "A")], NoEdits, NoDeletions);

        Assert.Equal([new ChangeSetError("created[0].parentId", ChangeSetErrorCode.ForwardReference)], errors);
    }

    [Fact]
    public void Rejects_duplicate_ids_among_new_nodes()
    {
        var parent = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        var errors = Reject([new(id, parent, "A"), new(id, parent, "B")], NoEdits, NoDeletions);

        Assert.Equal([new ChangeSetError("created[1].id", ChangeSetErrorCode.DuplicateId)], errors);
    }

    [Fact]
    public void Rejects_duplicate_edits_and_duplicate_deletions()
    {
        var edited = Guid.CreateVersion7();
        var deleted = Guid.CreateVersion7();

        var errors = Reject(NoCreations, [new(edited, "a", 1), new(edited, "b", 1)], [deleted, deleted]);

        Assert.Equal(
            [
                new ChangeSetError("updated[1].id", ChangeSetErrorCode.DuplicateId),
                new ChangeSetError("deleted[1]", ChangeSetErrorCode.DuplicateId),
            ],
            errors);
    }

    [Fact]
    public void Rejects_an_id_that_is_both_created_and_edited()
    {
        var parent = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        var errors = Reject([new(id, parent, "A")], [new(id, "B", 1)], NoDeletions);

        Assert.Equal([new ChangeSetError("updated[0].id", ChangeSetErrorCode.CreatedAndChanged)], errors);
    }

    [Fact]
    public void Rejects_an_id_that_is_both_created_and_deleted()
    {
        var parent = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        var errors = Reject([new(id, parent, "A")], NoEdits, [id]);

        Assert.Equal([new ChangeSetError("deleted[0]", ChangeSetErrorCode.CreatedAndChanged)], errors);
    }

    [Fact]
    public void Accepts_an_edit_and_a_deletion_of_the_same_existing_node()
    {
        var id = Guid.CreateVersion7();

        Assert.True(ChangeSet.TryCreate(NoCreations, [new(id, "A", 3)], [id], out var changeSet, out _));
        Assert.Equal([NodeId.Create(id)], changeSet.ExistingTargets());
    }

    [Fact]
    public void Accepts_exactly_the_maximum_number_of_operations()
    {
        var parent = Guid.CreateVersion7();
        var created = Enumerable.Range(0, 5_000)
            .Select(_ => new NodeCreationDraft(Guid.CreateVersion7(), parent, "x"))
            .ToList();
        var deleted = Enumerable.Range(0, 5_000).Select(_ => Guid.CreateVersion7()).ToList();

        Assert.True(ChangeSet.TryCreate(created, NoEdits, deleted, out var changeSet, out _));
        Assert.Equal(ChangeSet.MaxOperations, changeSet.OperationCount);
    }

    [Fact]
    public void Rejects_more_than_the_maximum_number_of_operations()
    {
        var parent = Guid.CreateVersion7();
        var created = Enumerable.Range(0, 10_001)
            .Select(_ => new NodeCreationDraft(Guid.CreateVersion7(), parent, "x"))
            .ToList();

        var errors = Reject(created, NoEdits, NoDeletions);

        Assert.Equal([new ChangeSetError(string.Empty, ChangeSetErrorCode.TooManyOperations)], errors);
    }

    [Fact]
    public void Rejects_ids_that_are_not_version_7()
    {
        var errors = Reject(
            [new(Guid.NewGuid(), Guid.Empty, "A")],
            [new(Guid.NewGuid(), "B", 1)],
            [Guid.Empty]);

        Assert.Equal(
            [
                new ChangeSetError("created[0].id", ChangeSetErrorCode.InvalidId),
                new ChangeSetError("created[0].parentId", ChangeSetErrorCode.InvalidId),
                new ChangeSetError("updated[0].id", ChangeSetErrorCode.InvalidId),
                new ChangeSetError("deleted[0]", ChangeSetErrorCode.InvalidId),
            ],
            errors);
    }

    [Fact]
    public void Rejects_invalid_values_and_versions()
    {
        var parent = Guid.CreateVersion7();

        var errors = Reject(
            [
                new(Guid.CreateVersion7(), parent, " abc"),
                new(Guid.CreateVersion7(), parent, new string('a', 201)),
                new(Guid.CreateVersion7(), parent, "a" + (char)0 + "b"),
            ],
            [new(Guid.CreateVersion7(), "", 0)],
            NoDeletions);

        Assert.Equal(
            [
                new ChangeSetError("created[0].value", ChangeSetErrorCode.ValueHasSurroundingWhitespace),
                new ChangeSetError("created[1].value", ChangeSetErrorCode.ValueTooLong),
                new ChangeSetError("created[2].value", ChangeSetErrorCode.ValueHasNullCharacter),
                new ChangeSetError("updated[0].expectedVersion", ChangeSetErrorCode.InvalidVersion),
                new ChangeSetError("updated[0].value", ChangeSetErrorCode.EmptyValue),
            ],
            errors);
    }

    [Fact]
    public void Accepts_an_empty_change_set()
    {
        Assert.True(ChangeSet.TryCreate(NoCreations, NoEdits, NoDeletions, out var changeSet, out _));
        Assert.Equal(0, changeSet.OperationCount);
    }

    [Fact]
    public void Lists_new_ids_and_their_existing_parents_as_creation_references()
    {
        var existing = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        ChangeSet.TryCreate(
            [new(first, existing, "N1"), new(second, first, "N2")],
            NoEdits,
            NoDeletions,
            out var changeSet,
            out _);

        Assert.Equal(
            new[] { first, second, existing }.Select(NodeId.Create).ToHashSet(),
            changeSet!.CreationReferences().ToHashSet());
    }

    private static IReadOnlyList<ChangeSetError> Reject(
        IReadOnlyList<NodeCreationDraft> created,
        IReadOnlyList<NodeEditDraft> updated,
        IReadOnlyList<Guid> deleted)
    {
        Assert.False(ChangeSet.TryCreate(created, updated, deleted, out var changeSet, out var errors));
        Assert.Null(changeSet);
        return errors;
    }
}
