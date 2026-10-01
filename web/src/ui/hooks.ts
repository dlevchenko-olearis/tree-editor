/** React adapters over the core stores, all built on `useSyncExternalStore`. */

import { useSyncExternalStore } from 'react'
import type { BrowserRow, BrowserStatus, DbNodeView, DbTreeBrowser } from '../core/DbTreeBrowser.ts'
import type { Notification, Notifications } from '../core/Notifications.ts'
import type { CachedNodeView, CacheStatus, TreeCache } from '../core/TreeCache.ts'
import type { SessionState, TreeSession } from '../core/TreeSession.ts'
import type { NodeId, TreeRow } from '../core/types.ts'

interface Subscribable {
  readonly subscribe: (listener: () => void) => () => void
}

/** Reads a memoized value from a store and re-renders only when that value changes. */
export function useStoreValue<T>(store: Subscribable, read: () => T): T {
  return useSyncExternalStore(store.subscribe, read, read)
}

/** Structural rows of the cached tree. */
export function useCacheRows(cache: TreeCache): readonly TreeRow[] {
  return useStoreValue(cache, () => cache.getRows())
}

/** View of one cached node, or undefined when the id is null or not cached. */
export function useCacheNode(cache: TreeCache, id: NodeId | null): CachedNodeView | undefined {
  return useStoreValue(cache, () => (id === null ? undefined : cache.getNodeView(id)))
}

/** Cache-wide toolbar state. */
export function useCacheStatus(cache: TreeCache): CacheStatus {
  return useStoreValue(cache, () => cache.getStatus())
}

/** Structural rows of the database tree. */
export function useBrowserRows(browser: DbTreeBrowser): readonly BrowserRow[] {
  return useStoreValue(browser, () => browser.getRows())
}

/** View of one displayed database node, or undefined when the id is null or not displayed. */
export function useBrowserNode(browser: DbTreeBrowser, id: NodeId | null): DbNodeView | undefined {
  return useStoreValue(browser, () => (id === null ? undefined : browser.getNodeView(id)))
}

/** Browser-wide state. */
export function useBrowserStatus(browser: DbTreeBrowser): BrowserStatus {
  return useStoreValue(browser, () => browser.getStatus())
}

/** Session-wide state. */
export function useSessionState(session: TreeSession): SessionState {
  return useStoreValue(session, () => session.getSnapshot())
}

/** Current notifications. */
export function useNotifications(notifications: Notifications): readonly Notification[] {
  return useStoreValue(notifications, () => notifications.getSnapshot())
}
