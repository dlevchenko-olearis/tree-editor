/** One browser session: the cache, the database browser, notifications, and the actions that span them. */

import { DbTreeBrowser } from './DbTreeBrowser.ts'
import { describeError } from './errors.ts'
import { Notifications } from './Notifications.ts'
import { SessionClock } from './SessionClock.ts'
import { Store } from './Store.ts'
import { TreeCache } from './TreeCache.ts'
import type { ApplyOutcome } from './TreeCache.ts'
import type { Conflict, NodeId } from './types.ts'
import type { DbTreeApi } from '../ports/DbTreeApi.ts'
import type { ResetApi } from '../ports/ResetApi.ts'
import type { TreeApi } from '../ports/TreeApi.ts'

/** Session-wide state that belongs to neither view. */
export interface SessionState {
  readonly resetInFlight: boolean
  readonly conflicts: readonly Conflict[]
}

/** Dependencies of a session. */
export interface TreeSessionOptions {
  readonly treeApi: TreeApi
  readonly dbTreeApi: DbTreeApi
  readonly resetApi: ResetApi
  readonly pageSize?: number
  readonly createId?: () => NodeId
  readonly createKey?: () => string
}

/** Composes the cache and the browser around one generation clock and runs the cross-cutting actions. */
export class TreeSession extends Store {
  readonly clock = new SessionClock()
  readonly notifications = new Notifications()
  readonly cache: TreeCache
  readonly browser: DbTreeBrowser

  private readonly resetApi: ResetApi
  private state: SessionState = { resetInFlight: false, conflicts: [] }
  private started = false

  constructor(options: TreeSessionOptions) {
    super()
    this.resetApi = options.resetApi
    this.cache = new TreeCache({
      api: options.treeApi,
      clock: this.clock,
      notify: this.notifications.push,
      createId: options.createId,
      createKey: options.createKey,
    })
    this.browser = new DbTreeBrowser({
      api: options.dbTreeApi,
      clock: this.clock,
      notify: this.notifications.push,
      pageSize: options.pageSize,
    })
    this.clock.onReset((reason) => {
      if (reason === 'remote' && !this.state.resetInFlight) {
        this.notifications.push('dataReset', 'The data was reset by another session.')
      }
      this.notifications.dismissKind('outcomeUnknown')
      this.setState({ conflicts: [] })
    })
  }

  /** Loads the first page of roots once. */
  async start(): Promise<void> {
    if (this.started) {
      return
    }
    this.started = true
    await this.browser.loadRoots()
  }

  /** Loads a node into the cache and removes it from the database view when it no longer exists. */
  async loadIntoCache(id: NodeId): Promise<void> {
    const outcome = await this.cache.load(id)
    if (outcome === 'notFound') {
      this.browser.removeNode(id)
    }
  }

  /** Applies the pending changes and refreshes only the database levels the Apply affected. */
  async apply(): Promise<ApplyOutcome> {
    this.setState({ conflicts: [] })
    const outcome = await this.cache.apply()
    if (!this.cache.getStatus().hasUnresolvedApply) {
      this.notifications.dismissKind('outcomeUnknown')
    }
    if (outcome.kind === 'applied') {
      await this.browser.refreshAfterApply(outcome.changeSet, outcome.result)
    } else if (outcome.kind === 'conflicted') {
      this.setState({ conflicts: outcome.conflicts })
    }
    return outcome
  }

  /** Forgets an Apply whose outcome is unknown, so the next Apply sends the pending changes with a new key. */
  discardUnresolvedApply(): void {
    this.cache.discardUnresolvedApply()
    if (!this.cache.getStatus().hasUnresolvedApply) {
      this.notifications.dismissKind('outcomeUnknown')
    }
  }

  /** Reloads the node a conflict refers to and removes the conflict from the list. */
  async reloadConflict(conflict: Conflict): Promise<void> {
    const target = conflict.kind === 'ParentDeleted' || conflict.kind === 'ParentNotFound' ? conflict.parentId : conflict.id
    if (target === null) {
      return
    }
    await this.loadIntoCache(target)
    this.setState({ conflicts: this.state.conflicts.filter((entry) => entry !== conflict) })
  }

  /** Restores the sample data, clears the cache, and collapses the database view; unavailable during an Apply. */
  async reset(): Promise<void> {
    if (this.state.resetInFlight || this.cache.getStatus().applyInFlight) {
      return
    }
    this.setState({ resetInFlight: true })
    const epoch = this.clock.epoch
    const result = await this.resetApi
      .reset()
      .catch((error: unknown) => ({ kind: 'failed' as const, message: describeError(error) }))
    this.setState({ resetInFlight: false })
    if (result.kind === 'reset') {
      this.clock.resetLocally(epoch, result.generation)
    } else {
      this.notifications.push('error', `Reset failed: ${result.message}`)
    }
  }

  /** Re-fetches every expanded level of the database view. */
  refresh(): Promise<void> {
    return this.browser.refresh()
  }

  /** Current session state; the object changes only when the state does. */
  getSnapshot(): SessionState {
    return this.state
  }

  private setState(patch: Partial<SessionState>): void {
    this.state = { ...this.state, ...patch }
    this.emit()
  }
}
