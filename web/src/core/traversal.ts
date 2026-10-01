/** Iterative tree traversals; nothing here recurses, so depth is bounded only by memory. */

import type { NodeId, TreeRow } from './types.ts'

/** Yields `start`, then its parent, grandparent and so on, until the root or the first id without a known parent. */
export function* walkUp(parentOf: ReadonlyMap<NodeId, NodeId | null>, start: NodeId): Generator<NodeId> {
  let current: NodeId | null | undefined = start
  let steps = 0
  const limit = parentOf.size + 1
  while (current !== null && current !== undefined && steps <= limit) {
    yield current
    current = parentOf.get(current)
    steps++
  }
}

/** Yields the visible rows of a forest in depth-first order, descending only into expanded nodes. */
export function* visibleRows(
  roots: readonly NodeId[],
  childrenOf: (id: NodeId) => readonly NodeId[],
  isExpanded: (id: NodeId) => boolean,
): Generator<TreeRow> {
  const stack: TreeRow[] = []
  pushReversed(stack, roots, 0)
  let row = stack.pop()
  while (row !== undefined) {
    yield row
    if (isExpanded(row.id)) {
      pushReversed(stack, childrenOf(row.id), row.depth + 1)
    }
    row = stack.pop()
  }
}

function pushReversed(stack: TreeRow[], ids: readonly NodeId[], depth: number): void {
  for (let index = ids.length - 1; index >= 0; index--) {
    const id = ids[index]
    if (id !== undefined) {
      stack.push({ id, depth })
    }
  }
}

/**
 * Tells whether `start` or any id on its path to the root is in `marked`, memoizing results per id so that
 * each known id is visited at most once per memo.
 */
export function isOnMarkedPath(
  start: NodeId,
  parentOf: ReadonlyMap<NodeId, NodeId | null>,
  marked: ReadonlySet<NodeId>,
  memo: Map<NodeId, boolean>,
): boolean {
  const path: NodeId[] = []
  let result = false
  for (const id of walkUp(parentOf, start)) {
    const known = memo.get(id)
    if (known !== undefined) {
      result = known
      break
    }
    if (marked.has(id)) {
      result = true
      memo.set(id, true)
      break
    }
    path.push(id)
  }
  for (const id of path) {
    memo.set(id, result)
  }
  return result
}
