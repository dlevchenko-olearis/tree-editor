using System.Diagnostics.CodeAnalysis;
using TreeEditor.Domain.Changes;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Planning;

/// <summary>
/// Decides whether a change set fits the database state and plans its writes. Every change is judged against the
/// state before the Apply and deletions run last, so a change set never conflicts with its own deletions.
/// </summary>
public static class ApplyPlanner
{
    /// <summary>Plans <paramref name="changes"/> or reports every conflict found.</summary>
    public static bool TryPlan(
        ChangeSet changes,
        TreeSnapshot snapshot,
        [NotNullWhen(true)] out ApplyPlan? plan,
        out IReadOnlyList<Conflict> conflicts)
    {
        var found = new List<Conflict>();
        CheckCreations(changes, snapshot, found);
        CheckEdits(changes, snapshot, found);
        var deletionRoots = CheckDeletions(changes, snapshot, found);

        conflicts = found;
        plan = found.Count == 0
            ? new ApplyPlan(changes.Created, changes.Updated, deletionRoots, changes.Deleted)
            : null;
        return plan is not null;
    }

    private static void CheckCreations(ChangeSet changes, TreeSnapshot snapshot, List<Conflict> conflicts)
    {
        var newIds = changes.Created.Select(node => node.Id).ToHashSet();
        foreach (var node in changes.Created)
        {
            var existing = snapshot[node.Id];
            if (existing.Exists)
            {
                conflicts.Add(Conflict.AlreadyExists(node.Id, existing.ParentId, existing.Version));
            }

            if (newIds.Contains(node.ParentId))
            {
                continue;
            }

            var parent = snapshot[node.ParentId];
            if (!parent.Exists)
            {
                conflicts.Add(Conflict.ParentNotFound(node.Id, node.ParentId));
            }
            else if (parent.IsDeleted)
            {
                conflicts.Add(Conflict.ParentDeleted(node.Id, node.ParentId));
            }
        }
    }

    private static void CheckEdits(ChangeSet changes, TreeSnapshot snapshot, List<Conflict> conflicts)
    {
        foreach (var edit in changes.Updated)
        {
            var current = snapshot[edit.Id];
            if (!current.Exists)
            {
                conflicts.Add(Conflict.NotFound(edit.Id));
            }
            else if (current.IsDeleted)
            {
                conflicts.Add(Conflict.Deleted(edit.Id));
            }
            else if (current.Version != edit.ExpectedVersion)
            {
                conflicts.Add(Conflict.VersionMismatch(edit.Id, current.Version!.Value));
            }
        }
    }

    private static List<NodeId> CheckDeletions(ChangeSet changes, TreeSnapshot snapshot, List<Conflict> conflicts)
    {
        var roots = new List<NodeId>();
        foreach (var id in changes.Deleted)
        {
            var current = snapshot[id];
            if (!current.Exists)
            {
                conflicts.Add(Conflict.NotFound(id));
            }
            else if (!current.IsDeleted)
            {
                roots.Add(id);
            }
        }

        return roots;
    }
}
