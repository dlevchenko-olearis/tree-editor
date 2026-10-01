using TreeEditor.Application.Changes;
using TreeEditor.Application.Tests.Fakes;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Application.Tests.Changes;

public sealed class ApplyChangesHandlerTests
{
    private readonly InMemoryTree _tree = new() { Generation = 3 };

    [Fact]
    public async Task A_stale_generation_is_a_conflict_and_nothing_is_read_or_written()
    {
        var root = _tree.Add(null);

        var outcome = await Handle(Command(generation: 2, deleted: [root.Value]));

        var conflicted = Assert.IsType<ApplyOutcome.Conflicted>(outcome);
        Assert.Equal([Conflict.StaleGeneration()], conflicted.Conflicts);
        Assert.Equal(3, conflicted.Generation);
        Assert.Equal(["generation"], _tree.Calls);
    }

    [Fact]
    public async Task Writes_inserts_then_updates_then_deletions_and_returns_versions_in_request_order()
    {
        var root = _tree.Add(null);
        var edited = _tree.Add(root, "before");
        var removed = _tree.Add(root);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var outcome = await Handle(Command(
            created: [new(first, root.Value, "N1"), new(second, first, "N2")],
            updated: [new(edited.Value, "after", _tree[edited].Version)],
            deleted: [removed.Value]));

        var applied = Assert.IsType<ApplyOutcome.Applied>(outcome);
        Assert.Equal(["generation", "snapshot:2:3", "insert:2", "update:1", "delete:1"], _tree.Calls);
        Assert.Equal([first, second], applied.Created.Select(node => node.Id.Value));
        Assert.Equal(_tree[NodeId.Create(second)].Version, applied.Created[1].Version.Value);
        Assert.Equal(_tree[edited].Version, Assert.Single(applied.Updated).Version.Value);
        Assert.Equal("after", _tree[edited].Value);
        Assert.Equal([removed], applied.Deleted);
        Assert.True(_tree[removed].IsDeletedFlag);
    }

    [Fact]
    public async Task Planner_conflicts_stop_the_apply_before_any_write()
    {
        var root = _tree.Add(null);
        var x = _tree.Add(root);
        var y = _tree.Add(root);

        var outcome = await Handle(Command(updated: [new(x.Value, "x", 999), new(y.Value, "y", 999)]));

        var conflicted = Assert.IsType<ApplyOutcome.Conflicted>(outcome);
        Assert.Equal([x, y], conflicted.Conflicts.Select(conflict => conflict.Id!.Value));
        Assert.DoesNotContain(_tree.Calls, call => call.StartsWith("insert", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_update_that_finds_no_row_with_the_expected_version_is_a_version_mismatch()
    {
        var root = _tree.Add(null);
        var x = _tree.Add(root);
        _tree.SkipOnUpdate.Add(x);

        var outcome = await Handle(Command(updated: [new(x.Value, "x", _tree[x].Version)]));

        var conflict = Assert.Single(Assert.IsType<ApplyOutcome.Conflicted>(outcome).Conflicts);
        Assert.Equal(ConflictKind.VersionMismatch, conflict.Kind);
        Assert.Equal(x, conflict.Id);
        Assert.DoesNotContain("delete:0", _tree.Calls);
    }

    [Fact]
    public async Task An_insert_skipped_because_the_id_appeared_meanwhile_is_already_exists()
    {
        var root = _tree.Add(null);
        var id = NodeId.Create(Guid.CreateVersion7());
        _tree.BeforeInsert = () => _tree.AddWithId(id, root);

        var outcome = await Handle(Command(created: [new(id.Value, root.Value, "mine")]));

        var conflict = Assert.Single(Assert.IsType<ApplyOutcome.Conflicted>(outcome).Conflicts);
        Assert.Equal(Conflict.AlreadyExists(id, root, NodeVersion.Create(_tree[id].Version)), conflict);
        Assert.DoesNotContain(_tree.Calls, call => call.StartsWith("update", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Existing_targets_are_locked_and_creation_references_are_read()
    {
        var root = _tree.Add(null);
        var edited = _tree.Add(root);

        await Handle(Command(
            created: [new(Guid.CreateVersion7(), root.Value, "new")],
            updated: [new(edited.Value, "e", _tree[edited].Version)],
            deleted: [edited.Value]));

        Assert.Contains("snapshot:1:2", _tree.Calls);
    }

    private Task<ApplyOutcome> Handle(ApplyChangesCommand command) =>
        new ApplyChangesHandler(_tree, _tree).HandleAsync(command, CancellationToken.None);

    private static ApplyChangesCommand Command(
        long generation = 3,
        IReadOnlyList<NodeCreationDraft>? created = null,
        IReadOnlyList<NodeEditDraft>? updated = null,
        IReadOnlyList<Guid>? deleted = null)
    {
        Assert.True(ChangeSet.TryCreate(created ?? [], updated ?? [], deleted ?? [], out var changes, out var errors),
            string.Join(", ", errors));
        return new ApplyChangesCommand(Guid.NewGuid(), generation, changes);
    }
}
