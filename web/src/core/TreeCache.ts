/** Local cache of individually loaded nodes: hierarchy assembly across gaps, local edits, and the Apply flow. */

import { buildChangeSet, isEmptyChangeSet } from './changeSetBuilder.ts'
import type { ChangeSetSource } from './changeSetBuilder.ts'
import { describeError } from './errors.ts'
import { prepareValue } from './nodeValue.ts'
import type { Notify } from './Notifications.ts'
import type { SessionClock } from './SessionClock.ts'
import { Store } from './Store.ts'
import { isOnMarkedPath, visibleRows } from './traversal.ts'
import { compareIds } from './types.ts'
import type {
  AppliedChanges,
  CachedNode,
  ChangeSet,
  Conflict,
  LoadedNode,
  NodeId,
  NodeStatus,
  TreeRow,
} from './types.ts'
import { createUuidV7 } from './uuid.ts'
import type { ApplyResult, LoadNodeResult, TreeApi } from '../ports/TreeApi.ts'

/** Outcome of a local operation that validates input. */
export type LocalResult = { readonly ok: true } | { readonly ok: false; readonly message: string }

/** Outcome of adding a child. */
export type AddChildResult = { readonly ok: true; readonly id: NodeId } | { readonly ok: false; readonly message: string }

/** Outcome of loading a node into the cache. */
export type LoadOutcome = 'loaded' | 'notFound' | 'failed' | 'dropped'

/** Outcome of an Apply as seen by the rest of the application. */
export type ApplyOutcome =
  | { readonly kind: 'nothing' }
  | { readonly kind: 'busy' }
  | { readonly kind: 'applied'; readonly changeSet: ChangeSet; readonly result: AppliedChanges }
  | { readonly kind: 'conflicted'; readonly conflicts: readonly Conflict[] }
  | { readonly kind: 'rejected'; readonly message: string }
  | { readonly kind: 'unknown' }
  | { readonly kind: 'dropped' }

/** Everything a row needs to render one cached node. */
export interface CachedNodeView {
  readonly id: NodeId
  readonly value: string
  readonly status: NodeStatus
  readonly isDeleted: boolean
  readonly hasChildren: boolean
  readonly isExpanded: boolean
}

/** Cache-wide state for the toolbar. */
export interface CacheStatus {
  readonly version: number
  readonly nodeCount: number
  readonly canApply: boolean
  readonly applyInFlight: boolean
  readonly hasUnresolvedApply: boolean
}

/** Dependencies of the cache. */
export interface TreeCacheOptions {
  readonly api: TreeApi
  readonly clock: SessionClock
  readonly notify: Notify
  readonly createId?: () => NodeId
  readonly createKey?: () => string
}

interface Submission {
  readonly key: string
  readonly changeSet: ChangeSet
}

const NOT_CACHED = { ok: false, message: 'The node is not in the cache.' } as const
const READ_ONLY = { ok: false, message: 'Deleted nodes are read-only.' } as const
const OUTCOME_UNKNOWN_MESSAGE =
  'The outcome of the last Apply is unknown. The next Apply checks it; Discard unconfirmed forgets it and keeps your changes pending.'

/** Client-side cache of individually loaded nodes, their pending changes, and the Apply flow. */
export class TreeCache extends Store {
  private readonly api: TreeApi
  private readonly clock: SessionClock
  private readonly notify: Notify
  private readonly createId: () => NodeId
  private readonly createKey: () => string

  private readonly nodes = new Map<NodeId, CachedNode>()
  private readonly parentOf = new Map<NodeId, NodeId | null>()
  private readonly children = new Map<NodeId, Set<NodeId>>()
  private readonly roots = new Set<NodeId>()
  private readonly deleted = new Set<NodeId>()
  private readonly pendingDeletes = new Set<NodeId>()
  private readonly dirty = new Set<NodeId>()
  private readonly conflicted = new Set<NodeId>()
  private readonly expanded = new Set<NodeId>()
  private readonly adopting = new Set<NodeId>()
  private readonly inFlightLoads = new Map<NodeId, Promise<LoadOutcome>>()
  private unresolvedApply: Submission | null = null
  private applyInFlight = false

  private version = 0
  private structureVersion = 0
  private deletionMemo = new Map<NodeId, boolean>()
  private rowsMemo: { readonly structureVersion: number; readonly rows: readonly TreeRow[] } | null = null
  private readonly viewMemo = new Map<NodeId, { version: number; view: CachedNodeView }>()
  private changeSetMemo: { readonly version: number; readonly changeSet: ChangeSet | null } | null = null
  private statusMemo: CacheStatus | null = null

  constructor(options: TreeCacheOptions) {
    super()
    this.api = options.api
    this.clock = options.clock
    this.notify = options.notify
    this.createId = options.createId ?? createUuidV7
    this.createKey = options.createKey ?? (() => crypto.randomUUID())
    this.clock.onReset(() => {
      this.clear()
    })
  }

  /** Loads one node; repeated calls while its load is in flight share a single request. */
  load(id: NodeId): Promise<LoadOutcome> {
    const pending = this.inFlightLoads.get(id)
    if (pending !== undefined) {
      return pending
    }
    const epoch = this.clock.epoch
    const promise = this.api
      .loadNode(id)
      .catch((error: unknown): LoadNodeResult => ({ kind: 'failed', message: describeError(error) }))
      .then((result) => this.completeLoad(id, epoch, result))
      .finally(() => {
        if (this.inFlightLoads.get(id) === promise) {
          this.inFlightLoads.delete(id)
        }
      })
    this.inFlightLoads.set(id, promise)
    return promise
  }

  /** Stages a new value for a cached node that is not effectively deleted. */
  edit(id: NodeId, input: string): LocalResult {
    const node = this.nodes.get(id)
    if (node === undefined) {
      return NOT_CACHED
    }
    if (this.isDeleted(id)) {
      return READ_ONLY
    }
    const prepared = prepareValue(input)
    if (!prepared.ok) {
      return { ok: false, message: prepared.message }
    }
    if (node.value !== prepared.value) {
      node.value = prepared.value
      this.updateDirty(node)
      this.touch(false, false)
    }
    return { ok: true }
  }

  /** Creates a new child with a client-generated UUIDv7 under a cached node that is not effectively deleted. */
  addChild(parentId: NodeId, input: string): AddChildResult {
    if (!this.nodes.has(parentId)) {
      return NOT_CACHED
    }
    if (this.isDeleted(parentId)) {
      return READ_ONLY
    }
    const prepared = prepareValue(input)
    if (!prepared.ok) {
      return { ok: false, message: prepared.message }
    }
    const id = this.createId()
    this.nodes.set(id, { id, parentId, value: prepared.value, base: null })
    this.parentOf.set(id, parentId)
    this.childSet(parentId).add(id)
    this.dirty.add(id)
    this.expanded.add(parentId)
    this.touch(true, false)
    return { ok: true, id }
  }

  /** Marks a cached node, and with it every descendant, as deleted in O(1). */
  delete(id: NodeId): LocalResult {
    if (!this.nodes.has(id)) {
      return NOT_CACHED
    }
    if (this.isDeleted(id)) {
      return READ_ONLY
    }
    this.deleted.add(id)
    this.pendingDeletes.add(id)
    this.touch(false, true)
    return { ok: true }
  }

  /** Expands a cached node. */
  expand(id: NodeId): void {
    if (this.nodes.has(id) && !this.expanded.has(id)) {
      this.expanded.add(id)
      this.touch(true, false)
    }
  }

  /** Collapses a cached node. */
  collapse(id: NodeId): void {
    if (this.expanded.delete(id)) {
      this.touch(true, false)
    }
  }

  /** Expands a collapsed node or collapses an expanded one. */
  toggle(id: NodeId): void {
    if (this.expanded.has(id)) {
      this.collapse(id)
    } else {
      this.expand(id)
    }
  }

  /** Tells whether a known id is effectively deleted: it or an id on its path to the root is in `deleted`. */
  isDeleted(id: NodeId): boolean {
    return isOnMarkedPath(id, this.parentOf, this.deleted, this.deletionMemo)
  }

  /** Tells whether the node is in the cache. */
  has(id: NodeId): boolean {
    return this.nodes.has(id)
  }

  /** Number of distinct ids whose parent is known: cached nodes and their ancestors. */
  get knownIdCount(): number {
    return this.parentOf.size
  }

  /** Visible rows, recomputed only after structural changes; the array is stable otherwise. */
  getRows(): readonly TreeRow[] {
    if (this.rowsMemo?.structureVersion !== this.structureVersion) {
      this.rowsMemo = { structureVersion: this.structureVersion, rows: this.computeRows() }
    }
    return this.rowsMemo.rows
  }

  /** Walks the cached forest depth-first over expanded nodes. */
  computeRows(): readonly TreeRow[] {
    const roots = [...this.roots].sort(compareIds)
    return [
      ...visibleRows(
        roots,
        (id) => this.sortedChildren(id),
        (id) => this.expanded.has(id),
      ),
    ]
  }

  /** View of one cached node; the same object is returned while nothing it shows has changed. */
  getNodeView(id: NodeId): CachedNodeView | undefined {
    const node = this.nodes.get(id)
    if (node === undefined) {
      return undefined
    }
    const memo = this.viewMemo.get(id)
    if (memo?.version === this.version) {
      return memo.view
    }
    const view = this.computeView(node)
    if (memo !== undefined && sameView(memo.view, view)) {
      memo.version = this.version
      return memo.view
    }
    this.viewMemo.set(id, { version: this.version, view })
    return view
  }

  /** Toolbar state, memoized per cache version. */
  getStatus(): CacheStatus {
    if (this.statusMemo?.version !== this.version) {
      this.statusMemo = {
        version: this.version,
        nodeCount: this.nodes.size,
        canApply: this.canApply(),
        applyInFlight: this.applyInFlight,
        hasUnresolvedApply: this.unresolvedApply !== null,
      }
    }
    return this.statusMemo
  }

  /** The change set Apply would send now, or null when nothing can be sent; memoized per cache version. */
  getChangeSet(): ChangeSet | null {
    if (this.changeSetMemo?.version !== this.version) {
      this.changeSetMemo = { version: this.version, changeSet: this.buildChangeSet() }
    }
    return this.changeSetMemo.changeSet
  }

  /**
   * Sends the kept request of an Apply whose outcome is unknown, with its original key, or else the pending change
   * set with a new key, and merges the answer into the cache.
   */
  async apply(): Promise<ApplyOutcome> {
    if (this.applyInFlight) {
      return { kind: 'busy' }
    }
    if (this.unresolvedApply !== null) {
      return this.send(this.unresolvedApply)
    }
    const changeSet = this.getChangeSet()
    if (changeSet === null) {
      return { kind: 'nothing' }
    }
    return this.send({ key: this.createKey(), changeSet })
  }

  /**
   * Forgets the kept request of an Apply whose outcome is unknown. Its changes stay pending, and the next Apply sends
   * them with a new key; an edit the forgotten request did apply comes back there as a version conflict.
   */
  discardUnresolvedApply(): void {
    if (this.unresolvedApply === null || this.applyInFlight) {
      return
    }
    this.unresolvedApply = null
    this.touch(false, false)
  }

  /** Drops every cached node and pending change, including a kept Apply request. */
  clear(): void {
    this.nodes.clear()
    this.parentOf.clear()
    this.children.clear()
    this.roots.clear()
    this.deleted.clear()
    this.pendingDeletes.clear()
    this.dirty.clear()
    this.conflicted.clear()
    this.expanded.clear()
    this.adopting.clear()
    this.inFlightLoads.clear()
    this.viewMemo.clear()
    this.unresolvedApply = null
    this.touch(true, true)
  }

  private completeLoad(id: NodeId, epoch: number, result: LoadNodeResult): LoadOutcome {
    if (result.kind === 'failed') {
      if (epoch === this.clock.epoch) {
        this.notify('error', `Loading the node failed: ${result.message}`)
      }
      return epoch === this.clock.epoch ? 'failed' : 'dropped'
    }
    if (!this.clock.accept(epoch, result.generation)) {
      return 'dropped'
    }
    if (result.kind === 'notFound') {
      this.markGone(id)
      this.notify('noLongerExists', 'The node no longer exists.')
      return 'notFound'
    }
    this.merge(result.node)
    return 'loaded'
  }

  private merge(loaded: LoadedNode): void {
    this.recordChain(loaded)
    const cached = this.nodes.get(loaded.id)
    const adopting = this.adopting.delete(loaded.id)
    if (cached === undefined) {
      this.insert(loaded)
      return
    }
    const base = cached.base
    if (adopting || base === null) {
      cached.base = { value: loaded.value, version: loaded.version }
      this.conflicted.delete(loaded.id)
      if (loaded.isDeleted) {
        this.markDeletedByServer(cached)
      } else {
        this.updateDirty(cached)
      }
      this.touch(false, true)
      return
    }
    if (loaded.version < base.version) {
      return
    }
    if (loaded.isDeleted) {
      if (loaded.version > base.version) {
        cached.base = { value: loaded.value, version: loaded.version }
      }
      this.conflicted.delete(loaded.id)
      this.markDeletedByServer(cached)
      this.touch(false, true)
      return
    }
    this.conflicted.delete(loaded.id)
    if (loaded.version > base.version) {
      const hasPendingEdit = cached.value !== base.value
      cached.base = { value: loaded.value, version: loaded.version }
      if (!hasPendingEdit) {
        cached.value = loaded.value
      }
      this.updateDirty(cached)
    }
    this.touch(false, false)
  }

  private insert(loaded: LoadedNode): void {
    this.nodes.set(loaded.id, {
      id: loaded.id,
      parentId: loaded.parentId,
      value: loaded.value,
      base: { value: loaded.value, version: loaded.version },
    })
    if (loaded.isDeleted) {
      this.deleted.add(loaded.id)
    }
    if (loaded.parentId !== null) {
      this.childSet(loaded.parentId).add(loaded.id)
    }
    if (loaded.parentId === null || !this.nodes.has(loaded.parentId)) {
      this.roots.add(loaded.id)
    } else {
      this.expanded.add(loaded.parentId)
    }
    const waiting = this.children.get(loaded.id)
    if (waiting !== undefined && waiting.size > 0) {
      for (const childId of waiting) {
        this.roots.delete(childId)
      }
      this.expanded.add(loaded.id)
    }
    this.touch(true, true)
  }

  private recordChain(loaded: LoadedNode): void {
    if (this.parentOf.has(loaded.id)) {
      return
    }
    this.parentOf.set(loaded.id, loaded.parentId)
    let child = loaded.parentId
    let index = 1
    while (child !== null && !this.parentOf.has(child)) {
      const parent = loaded.ancestorIds[index] ?? null
      this.parentOf.set(child, parent)
      child = parent
      index++
    }
  }

  private markGone(id: NodeId): void {
    const node = this.nodes.get(id)
    if (node === undefined) {
      return
    }
    this.conflicted.delete(id)
    this.markDeletedByServer(node)
    this.touch(false, true)
  }

  private markDeletedByServer(node: CachedNode): void {
    this.deleted.add(node.id)
    this.pendingDeletes.delete(node.id)
    this.discardEdit(node)
  }

  private discardEdit(node: CachedNode): void {
    if (node.base !== null) {
      node.value = node.base.value
      this.dirty.delete(node.id)
    }
  }

  private updateDirty(node: CachedNode): void {
    if (node.base === null || node.value !== node.base.value) {
      this.dirty.add(node.id)
    } else {
      this.dirty.delete(node.id)
    }
  }

  private canApply(): boolean {
    if (this.applyInFlight) {
      return false
    }
    return this.unresolvedApply !== null || this.getChangeSet() !== null
  }

  private buildChangeSet(): ChangeSet | null {
    const generation = this.clock.generation
    if (generation === null || (this.dirty.size === 0 && this.pendingDeletes.size === 0)) {
      return null
    }
    const source: ChangeSetSource = {
      generation,
      dirty: this.dirty,
      pendingDeletes: this.pendingDeletes,
      parentOf: this.parentOf,
      node: (id) => this.nodes.get(id),
      childrenOf: (id) => this.children.get(id) ?? [],
      isDeleted: (id) => this.isDeleted(id),
    }
    const changeSet = buildChangeSet(source)
    return isEmptyChangeSet(changeSet) ? null : changeSet
  }

  private async send(submission: Submission): Promise<ApplyOutcome> {
    const epoch = this.clock.epoch
    const resent = this.unresolvedApply === submission
    this.applyInFlight = true
    this.unresolvedApply = submission
    this.touch(false, false)
    let result: ApplyResult
    try {
      result = await this.api.apply(submission.changeSet, submission.key)
    } catch (error) {
      result = { kind: 'unknown', message: describeError(error) }
    }
    this.applyInFlight = false
    const outcome = this.completeApply(submission, epoch, result, resent)
    this.touch(false, false)
    return outcome
  }

  private completeApply(submission: Submission, epoch: number, result: ApplyResult, resent: boolean): ApplyOutcome {
    if (epoch !== this.clock.epoch) {
      return { kind: 'dropped' }
    }
    switch (result.kind) {
      case 'applied':
        if (!this.clock.accept(epoch, result.result.generation)) {
          return { kind: 'dropped' }
        }
        this.unresolvedApply = null
        this.mergeApplied(submission.changeSet, result.result)
        return { kind: 'applied', changeSet: submission.changeSet, result: result.result }
      case 'conflicted':
        if (!this.clock.accept(epoch, result.generation)) {
          return { kind: 'dropped' }
        }
        this.unresolvedApply = null
        if (result.conflicts.some((conflict) => conflict.kind === 'StaleGeneration')) {
          this.clock.reset(result.generation, 'remote')
          return { kind: 'dropped' }
        }
        this.mergeConflicts(result.conflicts)
        return { kind: 'conflicted', conflicts: result.conflicts }
      case 'rejected':
        if (!this.clock.accept(epoch, result.generation)) {
          return { kind: 'dropped' }
        }
        this.unresolvedApply = null
        this.notify('error', result.message)
        return { kind: 'rejected', message: result.message }
      case 'notProcessed':
        this.notify('error', result.message)
        if (resent) {
          return { kind: 'unknown' }
        }
        this.unresolvedApply = null
        return { kind: 'rejected', message: result.message }
      case 'unknown':
        this.notify('outcomeUnknown', OUTCOME_UNKNOWN_MESSAGE)
        return { kind: 'unknown' }
    }
  }

  private mergeApplied(changeSet: ChangeSet, result: AppliedChanges): void {
    const sentValues = new Map<NodeId, string>()
    for (const created of changeSet.created) {
      sentValues.set(created.id, created.value)
    }
    for (const updated of changeSet.updated) {
      sentValues.set(updated.id, updated.value)
    }
    for (const { id, version } of [...result.created, ...result.updated]) {
      const node = this.nodes.get(id)
      const value = sentValues.get(id)
      if (node === undefined || value === undefined || (node.base !== null && node.base.version >= version)) {
        continue
      }
      node.base = { value, version }
      this.conflicted.delete(id)
      this.updateDirty(node)
    }
    for (const id of result.deleted) {
      this.pendingDeletes.delete(id)
      this.deleted.add(id)
    }
    this.deletionMemo = new Map()
    this.prune()
    this.touch(false, true)
  }

  private prune(): void {
    for (const id of [...this.pendingDeletes]) {
      const node = this.nodes.get(id)
      const parentId = this.parentOf.get(id) ?? null
      const covered = parentId !== null && this.isDeleted(parentId)
      if (node === undefined || node.base === null || covered) {
        this.pendingDeletes.delete(id)
      }
    }
    for (const id of [...this.dirty]) {
      const node = this.nodes.get(id)
      if (node === undefined) {
        this.dirty.delete(id)
      } else if (this.isDeleted(id)) {
        this.dirty.delete(id)
        this.discardEdit(node)
      }
    }
  }

  private mergeConflicts(conflicts: readonly Conflict[]): void {
    const adoptions: NodeId[] = []
    for (const conflict of conflicts) {
      const node = conflict.id === null ? undefined : this.nodes.get(conflict.id)
      switch (conflict.kind) {
        case 'VersionMismatch':
          if (node !== undefined) {
            this.conflicted.add(node.id)
          }
          break
        case 'Deleted':
        case 'NotFound':
          if (node !== undefined) {
            this.conflicted.delete(node.id)
            this.markDeletedByServer(node)
          }
          break
        case 'ParentDeleted':
        case 'ParentNotFound':
          if (conflict.parentId !== null) {
            const parent = this.nodes.get(conflict.parentId)
            if (parent === undefined) {
              this.deleted.add(conflict.parentId)
            } else {
              this.conflicted.delete(parent.id)
              this.markDeletedByServer(parent)
            }
          }
          break
        case 'AlreadyExists':
          if (node !== undefined && node.base === null && node.parentId === conflict.parentId) {
            node.base = { value: node.value, version: conflict.currentVersion ?? 0 }
            this.dirty.delete(node.id)
            adoptions.push(node.id)
          } else {
            this.notify('error', 'A new node could not be created because its identifier is already in use.')
          }
          break
        case 'StaleGeneration':
          break
      }
    }
    this.touch(false, true)
    for (const id of adoptions) {
      this.adopting.add(id)
      void this.load(id)
    }
  }

  private childSet(parentId: NodeId): Set<NodeId> {
    let set = this.children.get(parentId)
    if (set === undefined) {
      set = new Set()
      this.children.set(parentId, set)
    }
    return set
  }

  private sortedChildren(id: NodeId): readonly NodeId[] {
    const set = this.children.get(id)
    return set === undefined ? [] : [...set].sort(compareIds)
  }

  private computeView(node: CachedNode): CachedNodeView {
    const isDeleted = this.isDeleted(node.id)
    return {
      id: node.id,
      value: isDeleted ? (node.base?.value ?? node.value) : node.value,
      status: this.statusOf(node, isDeleted),
      isDeleted,
      hasChildren: (this.children.get(node.id)?.size ?? 0) > 0,
      isExpanded: this.expanded.has(node.id),
    }
  }

  private statusOf(node: CachedNode, isDeleted: boolean): NodeStatus {
    if (isDeleted) {
      return 'deleted'
    }
    if (this.conflicted.has(node.id)) {
      return 'conflicted'
    }
    if (node.base === null) {
      return 'new'
    }
    return node.value === node.base.value ? 'unchanged' : 'modified'
  }

  private touch(structural: boolean, deletion: boolean): void {
    this.version++
    if (structural) {
      this.structureVersion++
    }
    if (deletion) {
      this.deletionMemo = new Map()
    }
    this.emit()
  }
}

function sameView(a: CachedNodeView, b: CachedNodeView): boolean {
  return (
    a.value === b.value &&
    a.status === b.status &&
    a.isDeleted === b.isDeleted &&
    a.hasChildren === b.hasChildren &&
    a.isExpanded === b.isExpanded
  )
}
