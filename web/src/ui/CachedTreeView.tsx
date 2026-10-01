/** CachedTreeView: virtualized view of the local cache with inline editing and status badges. */

import { memo, useCallback, useRef, useState } from 'react'
import type { TreeCache } from '../core/TreeCache.ts'
import type { NodeId, NodeStatus, TreeRow } from '../core/types.ts'
import { useCacheNode, useCacheRows } from './hooks.ts'
import { COLLAPSED_GLYPH, EXPANDED_GLYPH, indentOf, showsLevel } from './treeRowLayout.ts'
import { VirtualTree } from './VirtualTree.tsx'

/** Props of the cached tree view. */
export interface CachedTreeViewProps {
  readonly cache: TreeCache
  readonly selectedId: NodeId | null
  readonly editingId: NodeId | null
  readonly onSelect: (id: NodeId) => void
  readonly onEditStart: (id: NodeId) => void
  readonly onEditEnd: () => void
}

/** Shows the cached nodes in their hierarchy; rows read their content by id, so edits never rebuild the row list. */
export function CachedTreeView({ cache, selectedId, editingId, onSelect, onEditStart, onEditEnd }: CachedTreeViewProps) {
  const rows = useCacheRows(cache)
  const renderRow = useCallback(
    (row: TreeRow) => (
      <CacheRow
        cache={cache}
        id={row.id}
        depth={row.depth}
        selected={row.id === selectedId}
        editing={row.id === editingId}
        onSelect={onSelect}
        onEditStart={onEditStart}
        onEditEnd={onEditEnd}
      />
    ),
    [cache, selectedId, editingId, onSelect, onEditStart, onEditEnd],
  )
  return (
    <VirtualTree
      label="Cached tree"
      rows={rows}
      rowKey={cacheRowKey}
      renderRow={renderRow}
      empty="The cache is empty. Select a node in the database tree and choose Load."
      pinnedKey={editingId}
    />
  )
}

function cacheRowKey(row: TreeRow): string {
  return row.id
}

const STATUS_LABELS: Readonly<Record<NodeStatus, string>> = {
  unchanged: 'unchanged',
  modified: 'modified',
  new: 'new',
  deleted: 'deleted',
  conflicted: 'conflict',
}

interface CacheRowProps {
  readonly cache: TreeCache
  readonly id: NodeId
  readonly depth: number
  readonly selected: boolean
  readonly editing: boolean
  readonly onSelect: (id: NodeId) => void
  readonly onEditStart: (id: NodeId) => void
  readonly onEditEnd: () => void
}

const CacheRow = memo(function CacheRow({
  cache,
  id,
  depth,
  selected,
  editing,
  onSelect,
  onEditStart,
  onEditEnd,
}: CacheRowProps) {
  const view = useCacheNode(cache, id)
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
      onDoubleClick={() => {
        if (!view.isDeleted) {
          onSelect(id)
          onEditStart(id)
        }
      }}
    >
      {view.hasChildren ? (
        <button
          type="button"
          className="expander"
          aria-label={view.isExpanded ? `Collapse ${view.value}` : `Expand ${view.value}`}
          onClick={(event) => {
            event.stopPropagation()
            cache.toggle(id)
          }}
        >
          {view.isExpanded ? EXPANDED_GLYPH : COLLAPSED_GLYPH}
        </button>
      ) : (
        <span className="expander-placeholder" />
      )}
      {showsLevel(depth) && <span className="level">L{depth + 1}</span>}
      {editing && !view.isDeleted ? (
        <InlineEditor cache={cache} id={id} initialValue={view.value} onDone={onEditEnd} />
      ) : (
        <span className="value">{view.value}</span>
      )}
      <span className={`badge badge-${view.status}`}>{STATUS_LABELS[view.status]}</span>
    </div>
  )
})

interface InlineEditorProps {
  readonly cache: TreeCache
  readonly id: NodeId
  readonly initialValue: string
  readonly onDone: () => void
}

function InlineEditor({ cache, id, initialValue, onDone }: InlineEditorProps) {
  const [text, setText] = useState(initialValue)
  const [error, setError] = useState<string | null>(null)
  const closed = useRef(false)
  const close = () => {
    closed.current = true
    onDone()
  }
  const commit = () => {
    if (closed.current) {
      return
    }
    const result = cache.edit(id, text)
    if (result.ok) {
      close()
    } else {
      setError(result.message)
    }
  }
  return (
    <span className="inline-editor">
      <input
        aria-label="Edit value"
        aria-invalid={error !== null}
        autoFocus
        value={text}
        onClick={(event) => {
          event.stopPropagation()
        }}
        onDoubleClick={(event) => {
          event.stopPropagation()
        }}
        onChange={(event) => {
          setText(event.target.value)
          setError(null)
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault()
            commit()
          } else if (event.key === 'Escape') {
            event.preventDefault()
            close()
          }
        }}
        onBlur={commit}
      />
      {error !== null && (
        <span role="alert" className="validation">
          {error}
        </span>
      )}
    </span>
  )
}
