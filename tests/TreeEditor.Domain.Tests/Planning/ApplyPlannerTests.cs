using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Domain.Tests.Planning;

public sealed class ApplyPlannerTests
{
    private readonly List<StoredNode> _rows = [];

    [Fact]
    public void Plans_creations_edits_and_deletions_without_conflicts()
    {
        var root = Store(null);
        var edited = Store(root, version: 5);
        var removed = Store(root);
        var first = NewId();
        var second = NewId();

        var plan = Plan(Changes(
            created: [new(first, root.Value, "N1"), new(second, first, "N2")],
            updated: [new(edited.Value, "edited", 5)],
            deleted: [removed.Value]));

        Assert.Equal([first, second], plan.Inserts.Select(node => node.Id.Value));
        Assert.Equal([edited], plan.Updates.Select(edit => edit.Id));
        Assert.Equal([removed], plan.DeletionRoots);
        Assert.Equal([removed], plan.Deleted);
    }

    [Fact]
    public void Reports_a_version_mismatch_for_every_edited_node_that_changed()
    {
        var root = Store(null);
        var x = Store(root, version: 7);
        var y = Store(root, version: 9);

        var conflicts = Conflicts(Changes(updated: [new(x.Value, "x", 6), new(y.Value, "y", 8)]));

        Assert.Equal(
            [Conflict.VersionMismatch(x, NodeVersion.Create(7)), Conflict.VersionMismatch(y, NodeVersion.Create(9))],
            conflicts);
    }

    [Fact]
    public void Reports_an_edit_under_a_node_another_session_deleted_as_deleted()
    {
        var a = Store(null, deleted: true);
        var b = Store(a);
        var c = Store(b, version: 4);

        var conflicts = Conflicts(Changes(updated: [new(c.Value, "c", 4)]));

        Assert.Equal([Conflict.Deleted(c)], conflicts);
    }

    [Fact]
    public void Reports_missing_edited_and_deleted_nodes_as_not_found()
    {
        var edited = NewId();
        var deleted = NewId();

        var conflicts = Conflicts(Changes(updated: [new(edited, "x", 1)], deleted: [deleted]));

        Assert.Equal(
            [Conflict.NotFound(NodeId.Create(edited)), Conflict.NotFound(NodeId.Create(deleted))],
            conflicts);
    }

    [Fact]
    public void Reports_a_missing_parent_as_parent_not_found()
    {
        var parent = NewId();
        var child = NewId();

        var conflicts = Conflicts(Changes(created: [new(child, parent, "child")]));

        Assert.Equal([Conflict.ParentNotFound(NodeId.Create(child), NodeId.Create(parent))], conflicts);
    }

    [Fact]
    public void Reports_a_parent_inside_a_deleted_branch_as_parent_deleted()
    {
        var a = Store(null, deleted: true);
        var parent = Store(a);
        var first = NewId();
        var second = NewId();

        var conflicts = Conflicts(Changes(created: [new(first, parent.Value, "N1"), new(second, first, "N2")]));

        Assert.Equal([Conflict.ParentDeleted(NodeId.Create(first), parent)], conflicts);
    }

    [Fact]
    public void Reports_an_existing_id_as_already_exists_with_its_parent_and_version()
    {
        var root = Store(null);
        var existing = Store(root, version: 12);

        var conflicts = Conflicts(Changes(created: [new(existing.Value, root.Value, "again")]));

        Assert.Equal([Conflict.AlreadyExists(existing, root, NodeVersion.Create(12))], conflicts);
    }

    [Fact]
    public void Reports_every_conflict_rather_than_only_the_first()
    {
        var root = Store(null);
        var stale = Store(root, version: 2);
        var missingParent = NewId();
        var orphan = NewId();
        var missing = NewId();

        var conflicts = Conflicts(Changes(
            created: [new(orphan, missingParent, "orphan")],
            updated: [new(stale.Value, "stale", 1)],
            deleted: [missing]));

        Assert.Equal(
            [ConflictKind.ParentNotFound, ConflictKind.VersionMismatch, ConflictKind.NotFound],
            conflicts.Select(conflict => conflict.Kind));
    }

    [Fact]
    public void Deleting_an_already_deleted_node_is_a_reported_no_op()
    {
        var a = Store(null, deleted: true);
        var descendant = Store(Store(a));

        var plan = Plan(Changes(deleted: [descendant.Value, a.Value]));

        Assert.Empty(plan.DeletionRoots);
        Assert.Equal([descendant, a], plan.Deleted);
    }

    [Fact]
    public void Deleting_needs_no_version_match()
    {
        var root = Store(null);
        var x = Store(root, version: 40);

        var plan = Plan(Changes(deleted: [x.Value]));

        Assert.Equal([x], plan.DeletionRoots);
    }

    [Fact]
    public void Own_edits_and_additions_inside_a_branch_deleted_by_the_same_change_set_do_not_conflict()
    {
        var a = Store(null);
        var b = Store(a);
        var c = Store(b, version: 3);
        var added = NewId();

        var plan = Plan(Changes(
            created: [new(added, c.Value, "added")],
            updated: [new(c.Value, "edited", 3)],
            deleted: [a.Value]));

        Assert.Equal([a], plan.DeletionRoots);
        Assert.Equal([c], plan.Updates.Select(edit => edit.Id));
        Assert.Equal([added], plan.Inserts.Select(node => node.Id.Value));
    }

    [Fact]
    public void Resolves_effective_deletion_through_a_very_deep_chain_without_recursion()
    {
        var top = Store(null, deleted: true);
        var current = top;
        for (var depth = 0; depth < 10_000; depth++)
        {
            current = Store(current);
        }

        var snapshot = TreeSnapshot.FromAncestry(_rows);

        Assert.True(snapshot[current].IsDeleted);
        Assert.True(snapshot[top].IsDeleted);
    }

    [Fact]
    public void Reports_ids_without_rows_as_missing()
    {
        var snapshot = TreeSnapshot.FromAncestry(_rows);
        var id = NodeId.Create(NewId());

        Assert.Equal(NodeSnapshot.Missing(id), snapshot[id]);
    }

    private NodeId Store(NodeId? parent, long version = 1, bool deleted = false)
    {
        var id = NodeId.Create(NewId());
        _rows.Add(new StoredNode(id, parent, NodeVersion.Create(version), deleted));
        return id;
    }

    private ApplyPlan Plan(ChangeSet changes)
    {
        var planned = ApplyPlanner.TryPlan(changes, TreeSnapshot.FromAncestry(_rows), out var plan, out var conflicts);

        Assert.True(planned, string.Join(", ", conflicts));
        return plan!;
    }

    private IReadOnlyList<Conflict> Conflicts(ChangeSet changes)
    {
        Assert.False(ApplyPlanner.TryPlan(changes, TreeSnapshot.FromAncestry(_rows), out var plan, out var conflicts));
        Assert.Null(plan);
        return conflicts;
    }

    private static ChangeSet Changes(
        IReadOnlyList<NodeCreationDraft>? created = null,
        IReadOnlyList<NodeEditDraft>? updated = null,
        IReadOnlyList<Guid>? deleted = null)
    {
        Assert.True(ChangeSet.TryCreate(created ?? [], updated ?? [], deleted ?? [], out var changes, out var errors),
            string.Join(", ", errors));
        return changes;
    }

    private static Guid NewId() => Guid.CreateVersion7();
}
