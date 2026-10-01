/** Toolbar with every user action; each button is enabled only when its action is available. */

import type { TreeSession } from '../core/TreeSession.ts'
import type { NodeId } from '../core/types.ts'
import { useBrowserNode, useCacheNode, useCacheStatus, useSessionState } from './hooks.ts'

/** Props of the toolbar. */
export interface ToolbarProps {
  readonly session: TreeSession
  readonly selectedDbId: NodeId | null
  readonly selectedCacheId: NodeId | null
  readonly onEdit: (id: NodeId) => void
  readonly onAddChild: (id: NodeId) => void
}

/** Load, Edit, Add child, Delete, Apply, Discard unconfirmed (only while an Apply is unconfirmed), Reset, and Refresh. */
export function Toolbar({ session, selectedDbId, selectedCacheId, onEdit, onAddChild }: ToolbarProps) {
  const dbNode = useBrowserNode(session.browser, selectedDbId)
  const cacheNode = useCacheNode(session.cache, selectedCacheId)
  const cacheStatus = useCacheStatus(session.cache)
  const { resetInFlight } = useSessionState(session)
  const canLoad = dbNode !== undefined && !dbNode.isDeleted && !resetInFlight
  const canChange = cacheNode !== undefined && !cacheNode.isDeleted && !resetInFlight

  return (
    <div className="toolbar" role="toolbar" aria-label="Actions">
      <button
        type="button"
        disabled={!canLoad}
        onClick={() => {
          if (dbNode !== undefined) {
            void session.loadIntoCache(dbNode.id)
          }
        }}
      >
        Load
      </button>
      <span className="toolbar-separator" />
      <button
        type="button"
        disabled={!canChange}
        onClick={() => {
          if (cacheNode !== undefined) {
            onEdit(cacheNode.id)
          }
        }}
      >
        Edit
      </button>
      <button
        type="button"
        disabled={!canChange}
        onClick={() => {
          if (cacheNode !== undefined) {
            onAddChild(cacheNode.id)
          }
        }}
      >
        Add child
      </button>
      <button
        type="button"
        disabled={!canChange}
        onClick={() => {
          if (cacheNode !== undefined) {
            session.cache.delete(cacheNode.id)
          }
        }}
      >
        Delete
      </button>
      <span className="toolbar-separator" />
      <button
        type="button"
        className="primary"
        disabled={!cacheStatus.canApply || resetInFlight}
        onClick={() => {
          void session.apply()
        }}
      >
        {cacheStatus.applyInFlight ? 'Applying...' : 'Apply'}
      </button>
      {cacheStatus.hasUnresolvedApply && (
        <button
          type="button"
          disabled={cacheStatus.applyInFlight || resetInFlight}
          onClick={() => {
            session.discardUnresolvedApply()
          }}
        >
          Discard unconfirmed
        </button>
      )}
      <button
        type="button"
        disabled={cacheStatus.applyInFlight || resetInFlight}
        onClick={() => {
          void session.reset()
        }}
      >
        {resetInFlight ? 'Resetting...' : 'Reset'}
      </button>
      <button
        type="button"
        disabled={resetInFlight}
        onClick={() => {
          void session.refresh()
        }}
      >
        Refresh
      </button>
    </div>
  )
}
