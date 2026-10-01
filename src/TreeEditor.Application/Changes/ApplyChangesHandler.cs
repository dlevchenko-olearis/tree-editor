using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;
using TreeEditor.Domain.Planning;

namespace TreeEditor.Application.Changes;

/// <summary>
/// Applies a change set inside the current transaction: checks the generation, which then cannot change until the
/// transaction ends, locks and reads the touched nodes, plans, then writes inserts, updates, and deletions in that order.
/// </summary>
public sealed class ApplyChangesHandler(ITreeReader reader, ITreeWriter writer)
    : ICommandHandler<ApplyChangesCommand, ApplyOutcome>
{
    /// <inheritdoc />
    public async Task<ApplyOutcome> HandleAsync(ApplyChangesCommand command, CancellationToken cancellationToken)
    {
        var generation = await reader.GetGenerationAsync(cancellationToken);
        if (generation != command.Generation)
        {
            return new ApplyOutcome.Conflicted(generation, [Conflict.StaleGeneration()]);
        }

        var changes = command.ChangeSet;
        var snapshot = await reader.ReadSnapshotAsync(
            changes.ExistingTargets(),
            changes.CreationReferences(),
            cancellationToken);
        if (!ApplyPlanner.TryPlan(changes, snapshot, out var plan, out var conflicts))
        {
            return new ApplyOutcome.Conflicted(generation, conflicts);
        }

        var created = await writer.InsertAsync(plan.Inserts, cancellationToken);
        if (created.Count != plan.Inserts.Count)
        {
            return await ConflictsForSkippedInserts(generation, plan.Inserts, created, cancellationToken);
        }

        var updated = await writer.UpdateAsync(plan.Updates, cancellationToken);
        if (updated.Count != plan.Updates.Count)
        {
            return await ConflictsForSkippedUpdates(generation, plan.Updates, updated, cancellationToken);
        }

        await writer.MarkDeletedAsync(plan.DeletionRoots, cancellationToken);
        return new ApplyOutcome.Applied(
            generation,
            InRequestOrder(plan.Inserts.Select(node => node.Id), created),
            InRequestOrder(plan.Updates.Select(edit => edit.Id), updated),
            plan.Deleted);
    }

    private async Task<ApplyOutcome> ConflictsForSkippedInserts(
        long generation,
        IReadOnlyList<NewNode> requested,
        IReadOnlyList<WrittenNode> written,
        CancellationToken cancellationToken)
    {
        var skipped = Skipped(requested.Select(node => node.Id), written);
        var snapshot = await reader.ReadSnapshotAsync([], skipped, cancellationToken);
        var conflicts = skipped
            .Select(id => snapshot[id])
            .Select(existing => Conflict.AlreadyExists(existing.Id, existing.ParentId, existing.Version))
            .ToList();
        return new ApplyOutcome.Conflicted(generation, conflicts);
    }

    private async Task<ApplyOutcome> ConflictsForSkippedUpdates(
        long generation,
        IReadOnlyList<NodeEdit> requested,
        IReadOnlyList<WrittenNode> written,
        CancellationToken cancellationToken)
    {
        var skipped = Skipped(requested.Select(edit => edit.Id), written);
        var snapshot = await reader.ReadSnapshotAsync([], skipped, cancellationToken);
        var conflicts = skipped
            .Select(id => snapshot[id])
            .Select(current => current switch
            {
                { Exists: false } => Conflict.NotFound(current.Id),
                { IsDeleted: true } => Conflict.Deleted(current.Id),
                _ => Conflict.VersionMismatch(current.Id, current.Version!.Value),
            })
            .ToList();
        return new ApplyOutcome.Conflicted(generation, conflicts);
    }

    private static List<NodeId> Skipped(IEnumerable<NodeId> requested, IReadOnlyList<WrittenNode> written)
    {
        var writtenIds = written.Select(node => node.Id).ToHashSet();
        return requested.Where(id => !writtenIds.Contains(id)).ToList();
    }

    private static List<WrittenNode> InRequestOrder(IEnumerable<NodeId> requested, IReadOnlyList<WrittenNode> written)
    {
        var versions = written.ToDictionary(node => node.Id, node => node.Version);
        return requested.Select(id => new WrittenNode(id, versions[id])).ToList();
    }
}
