/** DBTreeView: lazily loaded, paged view of the database tree. */

import { memo, useCallback } from 'react'
import type { BrowserRow, DbTreeBrowser } from '../core/DbTreeBrowser.ts'
import type { NodeId } from '../core/types.ts'
import { useBrowserNode, useBrowserRows, useBrowserStatus } from './hooks.ts'
import { COLLAPSED_GLYPH, EXPANDED_GLYPH, indentOf, showsLevel } from './treeRowLayout.ts'
import { VirtualTree } from './VirtualTree.tsx'

/** Props of the database tree view. */
export interface DbTreeViewProps {
  readonly browser: DbTreeBrowser
  readonly selectedId: NodeId | null
  readonly onSelect: (id: NodeId) => void
}

/** Shows the database tree; only expanded levels are loaded, one page at a time. */
export function DbTreeView({ browser, selectedId, onSelect }: DbTreeViewProps) {
  const rows = useBrowserRows(browser)
  const status = useBrowserStatus(browser)
  const renderRow = useCallback(
    (row: BrowserRow) =>
      row.kind === 'node' ? (
        <DbRow browser={browser} id={row.id} depth={row.depth} selected={row.id === selectedId} onSelect={onSelect} />
      ) : (
        <MoreRow browser={browser} parentId={row.parentId} depth={row.depth} loading={row.loading} />
      ),
    [browser, selectedId, onSelect],
  )
  return (
    <VirtualTree
      label="Database tree"
      rows={rows}
      rowKey={browserRowKey}
      renderRow={renderRow}
      empty={status.rootsLoaded ? 'The database is empty.' : 'Loading...'}
    />
  )
}

function browserRowKey(row: BrowserRow): string {
  return row.kind === 'node' ? row.id : `more:${row.parentId ?? 'roots'}`
}

interface DbRowProps {
  readonly browser: DbTreeBrowser
  readonly id: NodeId
  readonly depth: number
  readonly selected: boolean
  readonly onSelect: (id: NodeId) => void
}

const DbRow = memo(function DbRow({ browser, id, depth, selected, onSelect }: DbRowProps) {
  const view = useBrowserNode(browser, id)
  if (view === undefined) {
    return null
  }
  const classes = ['tree-row', selected ? 'is-selected' : '', view.isDeleted ? 'is-deleted' : ''].filter(Boolean)
  return (
    <div
      role="treeitem"
      aria-level={depth + 1}
      aria-selected={selected}
      aria-expanded={view.hasChildren ? view.isExpanded : undefined}
      className={classes.join(' ')}
      style={{ paddingLeft: indentOf(depth) }}
      onClick={() => {
        onSelect(id)
      }}
    >
      {view.hasChildren ? (
        <button
          type="button"
          className="expander"
          aria-label={view.isExpanded ? `Collapse ${view.value}` : `Expand ${view.value}`}
          onClick={(event) => {
            event.stopPropagation()
            void browser.toggle(id)
          }}
        >
          {view.isExpanded ? EXPANDED_GLYPH : COLLAPSED_GLYPH}
        </button>
      ) : (
        <span className="expander-placeholder" />
      )}
      {showsLevel(depth) && <span className="level">L{depth + 1}</span>}
      <span className="value">{view.value}</span>
      {view.isDeleted && <span className="badge badge-deleted">deleted</span>}
    </div>
  )
})

interface MoreRowProps {
  readonly browser: DbTreeBrowser
  readonly parentId: NodeId | null
  readonly depth: number
  readonly loading: boolean
}

function MoreRow({ browser, parentId, depth, loading }: MoreRowProps) {
  return (
    <div className="tree-row more-row" style={{ paddingLeft: indentOf(depth) }}>
      <span className="expander-placeholder" />
      {loading ? (
        <span className="muted">Loading...</span>
      ) : (
        <button
          type="button"
          className="link-button"
          onClick={() => {
            void browser.loadMore(parentId)
          }}
        >
          Load more
        </button>
      )}
    </div>
  )
}
