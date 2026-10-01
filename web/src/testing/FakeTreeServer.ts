/** In-memory stand-in for the HTTP API with the same semantics, used by tests only. */

import { compareIds } from '../core/types.ts'
import type { ChangeSet, Conflict, DbListItem, NodeId } from '../core/types.ts'
import { createUuidV7Generator } from '../core/uuid.ts'
import type { DbTreeApi, PageResult } from '../ports/DbTreeApi.ts'
import type { ResetApi, ResetResult } from '../ports/ResetApi.ts'
import type { ApplyResult, LoadNodeResult, TreeApi } from '../ports/TreeApi.ts'

/** Node of the fake database. */
export interface FakeRow {
  readonly id: NodeId
  readonly parentId: NodeId | null
  value: string
  version: number
  isDeleted: boolean
}

/** Seed entry: a value and the index of the parent entry, or null for a root. */
export interface SeedNode {
  readonly value: string
  readonly parent: number | null
}

/** Recorded calls, so tests can assert which requests were sent. */
export interface FakeCalls {
  readonly loadNode: NodeId[]
  readonly roots: { after: NodeId | null; limit: number }[]
  readonly children: { parentId: NodeId; after: NodeId | null; limit: number }[]
  readonly apply: { changeSet: ChangeSet; key: string }[]
  reset: number
}

/** Deterministic id generator starting at a fixed instant. */
export function createTestIds(start = 1_700_000_000_000): () => NodeId {
  let tick = 0
  return createUuidV7Generator(
    () => start + tick++,
    (bytes) => {
      bytes.fill(7)
    },
  )
}

/** Fake server implementing every port, with switches to hold responses and to change data behind the client's back. */
export class FakeTreeServer implements TreeApi, DbTreeApi, ResetApi {
  generation = 1
  readonly calls: FakeCalls = { loadNode: [], roots: [], children: [], apply: [], reset: 0 }
  readonly ids: NodeId[] = []

  private readonly rows = new Map<NodeId, FakeRow>()
  private readonly seed: readonly SeedNode[]
  private readonly idempotency = new Map<string, { body: string; result: ApplyResult }>()
  private nextVersion = 1
  private gate: Promise<void> | null = null
  private failNextApply: ApplyResult | null = null
  private loseNextApplyResponse = false

  constructor(seed: readonly SeedNode[]) {
    this.seed = seed
    const createId = createTestIds()
    for (let index = 0; index < seed.length; index++) {
      this.ids.push(createId())
    }
    this.restoreSeed()
  }

  /** Delays every response until the returned function is called. */
  hold(): () => void {
    let release = () => {}
    this.gate = new Promise((resolve) => {
      release = () => {
        this.gate = null
        resolve()
      }
    })
    return release
  }

  /** Makes the next Apply answer with the given result without touching the data. */
  answerNextApply(result: ApplyResult): void {
    this.failNextApply = result
  }

  /** Makes the next Apply commit its changes but report an unknown outcome, as if the response was lost. */
  loseNextApplyResponseAfterCommit(): void {
    this.loseNextApplyResponse = true
  }

  /** Row of the fake database. */
  row(id: NodeId): FakeRow | undefined {
    return this.rows.get(id)
  }

  /** Seed id by index. */
  id(index: number): NodeId {
    const id = this.ids[index]
    if (id === undefined) {
      throw new Error(`No seed node ${index}`)
    }
    return id
  }

  /** Another session edits a node. */
  editDirectly(id: NodeId, value: string): void {
    const row = this.requireRow(id)
    row.value = value
    row.version = this.nextVersion++
  }

  /** Another session deletes a node. */
  deleteDirectly(id: NodeId): void {
    const row = this.requireRow(id)
    row.isDeleted = true
    row.version = this.nextVersion++
  }

  /** Another session adds a child. */
  addDirectly(parentId: NodeId, id: NodeId, value: string): void {
    this.rows.set(id, { id, parentId, value, version: this.nextVersion++, isDeleted: false })
  }

  /** Removes a row outright, as if it never existed in the current generation. */
  removeDirectly(id: NodeId): void {
    this.rows.delete(id)
  }

  /** Another session resets the tree. */
  resetDirectly(): void {
    this.restoreSeed()
    this.idempotency.clear()
    this.generation++
  }

  /** Tells whether the node or an ancestor is flagged as deleted. */
  isEffectivelyDeleted(id: NodeId): boolean {
    let current: NodeId | null = id
    while (current !== null) {
      const row = this.rows.get(current)
      if (row === undefined) {
        return false
      }
      if (row.isDeleted) {
        return true
      }
      current = row.parentId
    }
    return false
  }

  async loadNode(id: NodeId): Promise<LoadNodeResult> {
    this.calls.loadNode.push(id)
    const row = this.rows.get(id)
    const result: LoadNodeResult =
      row === undefined
        ? { kind: 'notFound', generation: this.generation }
        : {
            kind: 'found',
            generation: this.generation,
            node: {
              id: row.id,
              parentId: row.parentId,
              value: row.value,
              version: row.version,
              isDeleted: this.isEffectivelyDeleted(row.id),
              ancestorIds: this.ancestorsOf(row.id),
            },
          }
    await this.gate
    return result
  }

  async roots(after: NodeId | null, limit: number): Promise<PageResult> {
    this.calls.roots.push({ after, limit })
    const result = this.page(null, after, limit)
    await this.gate
    return result
  }

  async children(parentId: NodeId, after: NodeId | null, limit: number): Promise<PageResult> {
    this.calls.children.push({ parentId, after, limit })
    const result: PageResult = this.rows.has(parentId)
      ? this.page(parentId, after, limit)
      : { kind: 'notFound', generation: this.generation }
    await this.gate
    return result
  }

  async apply(changeSet: ChangeSet, key: string): Promise<ApplyResult> {
    this.calls.apply.push({ changeSet, key })
    const result = this.applyNow(changeSet, key)
    await this.gate
    if (this.loseNextApplyResponse) {
      this.loseNextApplyResponse = false
      return { kind: 'unknown', message: 'lost' }
    }
    return result
  }

  async reset(): Promise<ResetResult> {
    this.calls.reset++
    this.resetDirectly()
    const result: ResetResult = { kind: 'reset', generation: this.generation }
    await this.gate
    return result
  }

  private applyNow(changeSet: ChangeSet, key: string): ApplyResult {
    if (this.failNextApply !== null) {
      const forced = this.failNextApply
      this.failNextApply = null
      return forced
    }
    const body = JSON.stringify(changeSet)
    const stored = this.idempotency.get(key)
    if (stored !== undefined) {
      return stored.body === body
        ? stored.result
        : { kind: 'rejected', status: 422, message: 'key reused', generation: this.generation }
    }
    if (changeSet.generation !== this.generation) {
      return {
        kind: 'conflicted',
        generation: this.generation,
        conflicts: [{ id: null, kind: 'StaleGeneration', currentVersion: null, parentId: null }],
      }
    }
    const conflicts = this.plan(changeSet)
    if (conflicts.length > 0) {
      return { kind: 'conflicted', generation: this.generation, conflicts }
    }
    const created = changeSet.created.map((node) => {
      const version = this.nextVersion++
      this.rows.set(node.id, { id: node.id, parentId: node.parentId, value: node.value, version, isDeleted: false })
      return { id: node.id, version }
    })
    const updated = changeSet.updated.map((edit) => {
      const row = this.requireRow(edit.id)
      row.value = edit.value
      row.version = this.nextVersion++
      return { id: edit.id, version: row.version }
    })
    for (const id of changeSet.deleted) {
      if (!this.isEffectivelyDeleted(id)) {
        this.deleteDirectly(id)
      }
    }
    const result: ApplyResult = {
      kind: 'applied',
      result: { generation: this.generation, created, updated, deleted: [...changeSet.deleted] },
    }
    this.idempotency.set(key, { body, result })
    return result
  }

  private plan(changeSet: ChangeSet): Conflict[] {
    const conflicts: Conflict[] = []
    const created = new Set(changeSet.created.map((node) => node.id))
    for (const node of changeSet.created) {
      const existing = this.rows.get(node.id)
      if (existing !== undefined) {
        conflicts.push({
          id: node.id,
          kind: 'AlreadyExists',
          currentVersion: existing.version,
          parentId: existing.parentId,
        })
      } else if (!created.has(node.parentId)) {
        if (!this.rows.has(node.parentId)) {
          conflicts.push({ id: node.id, kind: 'ParentNotFound', currentVersion: null, parentId: node.parentId })
        } else if (this.isEffectivelyDeleted(node.parentId)) {
          conflicts.push({ id: node.id, kind: 'ParentDeleted', currentVersion: null, parentId: node.parentId })
        }
      }
    }
    for (const edit of changeSet.updated) {
      const row = this.rows.get(edit.id)
      if (row === undefined) {
        conflicts.push({ id: edit.id, kind: 'NotFound', currentVersion: null, parentId: null })
      } else if (this.isEffectivelyDeleted(edit.id)) {
        conflicts.push({ id: edit.id, kind: 'Deleted', currentVersion: row.version, parentId: null })
      } else if (row.version !== edit.expectedVersion) {
        conflicts.push({ id: edit.id, kind: 'VersionMismatch', currentVersion: row.version, parentId: null })
      }
    }
    for (const id of changeSet.deleted) {
      if (!this.rows.has(id)) {
        conflicts.push({ id, kind: 'NotFound', currentVersion: null, parentId: null })
      }
    }
    return conflicts
  }

  private page(parentId: NodeId | null, after: NodeId | null, limit: number): PageResult {
    const parentDeleted = parentId !== null && this.isEffectivelyDeleted(parentId)
    const siblings = [...this.rows.values()]
      .filter((row) => row.parentId === parentId && (after === null || row.id > after))
      .sort((a, b) => compareIds(a.id, b.id))
    const page = siblings.slice(0, limit)
    const items: DbListItem[] = page.map((row) => ({
      id: row.id,
      parentId: row.parentId,
      value: row.value,
      isDeleted: parentDeleted || row.isDeleted,
      hasChildren: [...this.rows.values()].some((candidate) => candidate.parentId === row.id),
    }))
    const last = page.at(-1)
    return {
      kind: 'page',
      generation: this.generation,
      items,
      nextAfter: siblings.length > limit && last !== undefined ? last.id : null,
    }
  }

  private ancestorsOf(id: NodeId): NodeId[] {
    const ancestors: NodeId[] = []
    let current = this.rows.get(id)?.parentId ?? null
    while (current !== null) {
      ancestors.push(current)
      current = this.rows.get(current)?.parentId ?? null
    }
    return ancestors
  }

  private restoreSeed(): void {
    this.rows.clear()
    this.seed.forEach((node, index) => {
      const id = this.id(index)
      const parentId = node.parent === null ? null : this.id(node.parent)
      this.rows.set(id, { id, parentId, value: node.value, version: this.nextVersion++, isDeleted: false })
    })
  }

  private requireRow(id: NodeId): FakeRow {
    const row = this.rows.get(id)
    if (row === undefined) {
      throw new Error(`Unknown node ${id}`)
    }
    return row
  }
}

/** Seed of a chain `length` nodes deep: node i is the parent of node i + 1. */
export function chainSeed(length: number): SeedNode[] {
  return Array.from({ length }, (_, index) => ({ value: `Level ${index}`, parent: index === 0 ? null : index - 1 }))
}

/**
 * Small sample tree: 0 Root, 1 A (child of 0), 2 B (child of 1), 3 C (child of 2), 4 D (child of 3),
 * 5 E (child of 0), 6 F (child of 5), 7 Other root.
 */
export function sampleSeed(): SeedNode[] {
  return [
    { value: 'Root', parent: null },
    { value: 'A', parent: 0 },
    { value: 'B', parent: 1 },
    { value: 'C', parent: 2 },
    { value: 'D', parent: 3 },
    { value: 'E', parent: 0 },
    { value: 'F', parent: 5 },
    { value: 'Other root', parent: null },
  ]
}
