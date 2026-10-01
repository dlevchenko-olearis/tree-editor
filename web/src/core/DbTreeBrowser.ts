/** Lazy, paged view model of the database tree: roots, expanded levels, paging, and targeted refreshes. */

import { describeError } from './errors.ts'
import type { Notify } from './Notifications.ts'
import type { SessionClock } from './SessionClock.ts'
import { Store } from './Store.ts'
import type { AppliedChanges, ChangeSet, DbListItem, NodeId } from './types.ts'
import { MAX_PAGE_SIZE } from '../ports/DbTreeApi.ts'
import type { DbTreeApi, PageResult } from '../ports/DbTreeApi.ts'

/** Structural row of the database tree: a node, or the paging control of a level. */
export type BrowserRow =
  | { readonly kind: 'node'; readonly id: NodeId; readonly depth: number }
  | { readonly kind: 'more'; readonly parentId: NodeId | null; readonly depth: number; readonly loading: boolean }

/** Everything a row needs to render one database node. */
export interface DbNodeView {
  readonly id: NodeId
  readonly value: string
  readonly isDeleted: boolean
  readonly hasChildren: boolean
  readonly isExpanded: boolean
}

/** Browser-wide state. */
export interface BrowserStatus {
  readonly version: number
  readonly rootsLoaded: boolean
}

/** Dependencies of the browser. */
export interface DbTreeBrowserOptions {
  readonly api: DbTreeApi
  readonly clock: SessionClock
  readonly notify: Notify
  readonly pageSize?: number
}

/** Default number of children requested per page. */
export const DEFAULT_PAGE_SIZE = 20

const ROOT = ''

type LevelKey = NodeId | typeof ROOT

interface Level {
  childIds: NodeId[]
  nextAfter: NodeId | null
  loading: boolean
  loaded: boolean
  request: number
}

/** Browses the database tree without ever loading a whole subtree; only expanded nodes have loaded levels. */
export class DbTreeBrowser extends Store {
  private readonly api: DbTreeApi
  private readonly clock: SessionClock
  private readonly notify: Notify
  private readonly pageSize: number

  private readonly items = new Map<NodeId, DbListItem>()
  private readonly levels = new Map<LevelKey, Level>()
  private readonly expanded = new Set<NodeId>()

  private version = 0
  private rowsMemo: { readonly version: number; readonly rows: readonly BrowserRow[] } | null = null
  private readonly viewMemo = new Map<NodeId, DbNodeView>()
  private statusMemo: BrowserStatus | null = null

  constructor(options: DbTreeBrowserOptions) {
    super()
    this.api = options.api
    this.clock = options.clock
    this.notify = options.notify
    this.pageSize = Math.min(MAX_PAGE_SIZE, options.pageSize ?? DEFAULT_PAGE_SIZE)
    this.clock.onReset(() => {
      void this.collapseToRoots()
    })
  }

  /** Loads the first page of roots; nothing below them is requested. */
  loadRoots(): Promise<void> {
    this.levels.set(ROOT, newLevel())
    this.touch()
    return this.fill(ROOT, this.pageSize)
  }

  /** Expands a node that has children and fetches the first page of its direct children. */
  async expand(id: NodeId): Promise<void> {
    const item = this.items.get(id)
    if (item === undefined || !item.hasChildren || this.expanded.has(id)) {
      return
    }
    this.expanded.add(id)
    this.levels.set(id, newLevel())
    this.touch()
    await this.fill(id, this.pageSize)
  }

  /** Collapses a node and forgets the levels loaded below it. */
  collapse(id: NodeId): void {
    if (!this.expanded.has(id)) {
      return
    }
    this.dropLevelsBelow(id)
    this.touch()
  }

  /** Expands a collapsed node or collapses an expanded one. */
  async toggle(id: NodeId): Promise<void> {
    if (this.expanded.has(id)) {
      this.collapse(id)
    } else {
      await this.expand(id)
    }
  }

  /** Appends the next page of a level; `parentId` null means the roots. */
  async loadMore(parentId: NodeId | null): Promise<void> {
    const key: LevelKey = parentId ?? ROOT
    const level = this.levels.get(key)
    if (level === undefined || level.loading || level.nextAfter === null) {
      return
    }
    const request = ++level.request
    level.loading = true
    this.touch()
    const epoch = this.clock.epoch
    const result = await this.fetchPage(key, level.nextAfter, this.pageSize)
    if (!this.isCurrent(key, level, request, epoch)) {
      return
    }
    const page = this.acceptPage(key, level, epoch, result)
    if (page === null) {
      return
    }
    const known = new Set(level.childIds)
    for (const item of page.items) {
      this.items.set(item.id, item)
      if (!known.has(item.id)) {
        known.add(item.id)
        level.childIds.push(item.id)
      }
    }
    level.nextAfter = page.nextAfter
    level.loading = false
    this.touch()
  }

  /** Re-fetches the roots and every expanded level, so changes made by other sessions become visible. */
  async refresh(): Promise<void> {
    const keys = [...this.levels.keys()]
    await Promise.all(keys.map((key) => this.refreshLevel(key, this.pageSize)))
  }

  /**
   * Re-fetches only the levels a successful Apply affected: expanded parents of created nodes, the levels holding
   * edited or deleted rows, and the expanded levels inside deleted subtrees.
   */
  async refreshAfterApply(changeSet: ChangeSet, result: AppliedChanges): Promise<void> {
    const targets = new Map<LevelKey, number>()
    const include = (key: LevelKey, added: number) => {
      if (this.levels.has(key)) {
        targets.set(key, (targets.get(key) ?? 0) + added)
      }
    }
    const confirmed = new Set(result.created.map((created) => created.id))
    for (const created of changeSet.created) {
      if (!confirmed.has(created.id)) {
        continue
      }
      const parent = this.items.get(created.parentId)
      if (this.levels.has(created.parentId)) {
        include(created.parentId, 1)
      } else if (parent !== undefined && !parent.hasChildren) {
        include(parent.parentId ?? ROOT, 0)
      }
    }
    for (const { id } of result.updated) {
      this.includeContainingLevel(id, include)
    }
    for (const id of result.deleted) {
      this.includeContainingLevel(id, include)
      for (const key of this.levelsWithin(id)) {
        include(key, 0)
      }
    }
    await Promise.all([...targets].map(([key, added]) => this.refreshLevel(key, added)))
  }

  /** Removes a node that no longer exists, together with the levels loaded below it. */
  removeNode(id: NodeId): void {
    const item = this.items.get(id)
    if (item === undefined) {
      return
    }
    const parentLevel = this.levels.get(item.parentId ?? ROOT)
    if (parentLevel !== undefined) {
      parentLevel.childIds = parentLevel.childIds.filter((childId) => childId !== id)
    }
    this.dropLevelsBelow(id)
    this.forget(id)
    this.touch()
  }

  /** Forgets everything and shows the first page of roots, collapsed. */
  collapseToRoots(): Promise<void> {
    this.items.clear()
    this.viewMemo.clear()
    this.levels.clear()
    this.expanded.clear()
    return this.loadRoots()
  }

  /** Visible rows, recomputed only when the browser changed; the array is stable otherwise. */
  getRows(): readonly BrowserRow[] {
    if (this.rowsMemo?.version !== this.version) {
      this.rowsMemo = { version: this.version, rows: this.computeRows() }
    }
    return this.rowsMemo.rows
  }

  /** View of one displayed node; the same object is returned while nothing it shows has changed. */
  getNodeView(id: NodeId): DbNodeView | undefined {
    const item = this.items.get(id)
    if (item === undefined) {
      return undefined
    }
    const view: DbNodeView = {
      id,
      value: item.value,
      isDeleted: item.isDeleted,
      hasChildren: item.hasChildren,
      isExpanded: this.expanded.has(id),
    }
    const previous = this.viewMemo.get(id)
    if (
      previous !== undefined &&
      previous.value === view.value &&
      previous.isDeleted === view.isDeleted &&
      previous.hasChildren === view.hasChildren &&
      previous.isExpanded === view.isExpanded
    ) {
      return previous
    }
    this.viewMemo.set(id, view)
    return view
  }

  /** Browser-wide state, memoized per version. */
  getStatus(): BrowserStatus {
    if (this.statusMemo?.version !== this.version) {
      this.statusMemo = { version: this.version, rootsLoaded: this.levels.get(ROOT)?.loaded ?? false }
    }
    return this.statusMemo
  }

  private async refreshLevel(key: LevelKey, addedWhenComplete: number): Promise<void> {
    const level = this.levels.get(key)
    if (level === undefined) {
      return
    }
    if (!level.loaded) {
      await this.fill(key, this.pageSize)
      return
    }
    const loaded = Math.max(level.childIds.length, 1)
    const target = level.nextAfter === null ? loaded + addedWhenComplete : loaded
    await this.fill(key, target)
  }

  private async fill(key: LevelKey, target: number): Promise<void> {
    const level = this.levels.get(key)
    if (level === undefined) {
      return
    }
    const request = ++level.request
    const epoch = this.clock.epoch
    level.loading = true
    const collected: DbListItem[] = []
    const seen = new Set<NodeId>()
    let nextAfter: NodeId | null = null
    do {
      const limit = Math.min(MAX_PAGE_SIZE, Math.max(1, target - collected.length))
      const result = await this.fetchPage(key, nextAfter, limit)
      if (!this.isCurrent(key, level, request, epoch)) {
        return
      }
      const page = this.acceptPage(key, level, epoch, result)
      if (page === null) {
        return
      }
      for (const item of page.items) {
        if (!seen.has(item.id)) {
          seen.add(item.id)
          collected.push(item)
        }
      }
      nextAfter = page.nextAfter
    } while (nextAfter !== null && collected.length < target)

    for (const item of collected) {
      this.items.set(item.id, item)
    }
    for (const childId of level.childIds) {
      if (!seen.has(childId)) {
        this.dropLevelsBelow(childId)
        this.forget(childId)
      }
    }
    level.childIds = collected.map((item) => item.id)
    level.nextAfter = nextAfter
    level.loading = false
    level.loaded = true
    this.touch()
  }

  private acceptPage(
    key: LevelKey,
    level: Level,
    epoch: number,
    result: PageResult,
  ): Extract<PageResult, { kind: 'page' }> | null {
    if (result.kind === 'failed') {
      level.loading = false
      this.notify('error', `Loading the tree failed: ${result.message}`)
      this.touch()
      return null
    }
    if (!this.clock.accept(epoch, result.generation)) {
      if (this.levels.get(key) === level) {
        level.loading = false
        this.touch()
      }
      return null
    }
    if (result.kind === 'notFound') {
      this.notify('noLongerExists', 'The node no longer exists.')
      this.removeNode(key)
      return null
    }
    return result
  }

  private isCurrent(key: LevelKey, level: Level, request: number, epoch: number): boolean {
    return epoch === this.clock.epoch && this.levels.get(key) === level && level.request === request
  }

  private fetchPage(key: LevelKey, after: NodeId | null, limit: number): Promise<PageResult> {
    const request = key === ROOT ? this.api.roots(after, limit) : this.api.children(key, after, limit)
    return request.catch((error: unknown): PageResult => ({ kind: 'failed', message: describeError(error) }))
  }

  private includeContainingLevel(id: NodeId, include: (key: LevelKey, added: number) => void): void {
    const item = this.items.get(id)
    if (item !== undefined) {
      include(item.parentId ?? ROOT, 0)
    }
  }

  private levelsWithin(id: NodeId): NodeId[] {
    const found: NodeId[] = []
    const stack = [id]
    let current = stack.pop()
    while (current !== undefined) {
      const level = this.levels.get(current)
      if (level !== undefined) {
        found.push(current)
        stack.push(...level.childIds)
      }
      current = stack.pop()
    }
    return found
  }

  private dropLevelsBelow(id: NodeId): void {
    const stack = [id]
    let current = stack.pop()
    while (current !== undefined) {
      this.expanded.delete(current)
      const level = this.levels.get(current)
      if (level !== undefined) {
        this.levels.delete(current)
        for (const childId of level.childIds) {
          if (childId !== id) {
            this.forget(childId)
          }
          stack.push(childId)
        }
      }
      current = stack.pop()
    }
  }

  private computeRows(): BrowserRow[] {
    const rows: BrowserRow[] = []
    const stack: BrowserRow[] = []
    this.pushLevel(stack, ROOT, 0)
    let row = stack.pop()
    while (row !== undefined) {
      rows.push(row)
      if (row.kind === 'node' && this.expanded.has(row.id)) {
        this.pushLevel(stack, row.id, row.depth + 1)
      }
      row = stack.pop()
    }
    return rows
  }

  private pushLevel(stack: BrowserRow[], key: LevelKey, depth: number): void {
    const level = this.levels.get(key)
    if (level === undefined) {
      return
    }
    if (level.loading || level.nextAfter !== null) {
      stack.push({ kind: 'more', parentId: key === ROOT ? null : key, depth, loading: level.loading })
    }
    for (let index = level.childIds.length - 1; index >= 0; index--) {
      const id = level.childIds[index]
      if (id !== undefined) {
        stack.push({ kind: 'node', id, depth })
      }
    }
  }

  private forget(id: NodeId): void {
    this.items.delete(id)
    this.viewMemo.delete(id)
  }

  private touch(): void {
    this.version++
    this.emit()
  }
}

function newLevel(): Level {
  return { childIds: [], nextAfter: null, loading: false, loaded: false, request: 0 }
}
