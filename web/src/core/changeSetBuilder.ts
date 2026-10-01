/** Builds the change set Apply would send, looking only at pending ids, never at the whole cache. */

import { compareIds } from './types.ts'
import type { CachedNode, ChangeSet, CreatedNode, NodeId, UpdatedNode } from './types.ts'

/** Read-only view of the cache state the builder needs. */
export interface ChangeSetSource {
  readonly generation: number
  readonly dirty: ReadonlySet<NodeId>
  readonly pendingDeletes: ReadonlySet<NodeId>
  readonly parentOf: ReadonlyMap<NodeId, NodeId | null>
  node(id: NodeId): CachedNode | undefined
  childrenOf(id: NodeId): Iterable<NodeId>
  isDeleted(id: NodeId): boolean
}

/**
 * Builds the change set: new nodes that are not effectively deleted in parent-first order, edits that differ from
 * the database value outside deleted branches, and only the top-most pending deletions of existing nodes.
 */
export function buildChangeSet(source: ChangeSetSource): ChangeSet {
  return {
    generation: source.generation,
    created: collectCreated(source),
    updated: collectUpdated(source),
    deleted: collectDeleted(source),
  }
}

/** Tells whether a change set carries no operation. */
export function isEmptyChangeSet(changeSet: ChangeSet): boolean {
  return changeSet.created.length === 0 && changeSet.updated.length === 0 && changeSet.deleted.length === 0
}

function isNew(node: CachedNode | undefined): node is CachedNode {
  return node !== undefined && node.base === null
}

function collectCreated(source: ChangeSetSource): CreatedNode[] {
  const forestRoots: NodeId[] = []
  for (const id of source.dirty) {
    const node = source.node(id)
    if (!isNew(node) || node.parentId === null || source.isDeleted(id)) {
      continue
    }
    if (!isNew(source.node(node.parentId))) {
      forestRoots.push(id)
    }
  }
  forestRoots.sort(compareIds).reverse()

  const created: CreatedNode[] = []
  const stack = forestRoots
  let id = stack.pop()
  while (id !== undefined) {
    const node = source.node(id)
    if (node !== undefined && node.parentId !== null) {
      created.push({ id: node.id, parentId: node.parentId, value: node.value })
      const children = [...source.childrenOf(id)]
        .filter((childId) => isNew(source.node(childId)) && !source.isDeleted(childId))
        .sort(compareIds)
      for (let index = children.length - 1; index >= 0; index--) {
        const childId = children[index]
        if (childId !== undefined) {
          stack.push(childId)
        }
      }
    }
    id = stack.pop()
  }
  return created
}

function collectUpdated(source: ChangeSetSource): UpdatedNode[] {
  const updated: UpdatedNode[] = []
  for (const id of source.dirty) {
    const node = source.node(id)
    if (node === undefined || node.base === null || node.value === node.base.value || source.isDeleted(id)) {
      continue
    }
    updated.push({ id, value: node.value, expectedVersion: node.base.version })
  }
  return updated.sort((a, b) => compareIds(a.id, b.id))
}

function collectDeleted(source: ChangeSetSource): NodeId[] {
  const deleted: NodeId[] = []
  for (const id of source.pendingDeletes) {
    const node = source.node(id)
    if (node === undefined || node.base === null) {
      continue
    }
    const parentId = source.parentOf.get(id) ?? null
    if (parentId !== null && source.isDeleted(parentId)) {
      continue
    }
    deleted.push(id)
  }
  return deleted.sort(compareIds)
}
