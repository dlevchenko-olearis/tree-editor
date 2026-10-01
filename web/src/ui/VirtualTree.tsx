/** Virtualized list of fixed-height rows shared by both tree views. */

import { defaultRangeExtractor, useVirtualizer } from '@tanstack/react-virtual'
import type { Range } from '@tanstack/react-virtual'
import { useCallback, useMemo, useRef } from 'react'
import type { ReactNode } from 'react'
import { OVERSCAN, ROW_HEIGHT } from './treeRowLayout.ts'

/** Props of the virtualized list. */
export interface VirtualTreeProps<Row> {
  readonly label: string
  readonly rows: readonly Row[]
  readonly rowKey: (row: Row) => string
  readonly renderRow: (row: Row) => ReactNode
  readonly empty: ReactNode
  readonly pinnedKey?: string | null
}

/**
 * Renders only the rows in view; the row list is structural and each row reads its own content. The row with
 * `pinnedKey`, such as one being edited, stays mounted while scrolled out of view, so its state survives.
 */
export function VirtualTree<Row>({ label, rows, rowKey, renderRow, empty, pinnedKey = null }: VirtualTreeProps<Row>) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const pinnedIndex = useMemo(
    () => (pinnedKey === null ? -1 : rows.findIndex((row) => rowKey(row) === pinnedKey)),
    [rows, rowKey, pinnedKey],
  )
  const rangeExtractor = useCallback(
    (range: Range) => {
      const indexes = defaultRangeExtractor(range)
      return pinnedIndex < 0 || indexes.includes(pinnedIndex) ? indexes : [...indexes, pinnedIndex].sort((a, b) => a - b)
    },
    [pinnedIndex],
  )
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: OVERSCAN,
    rangeExtractor,
    getItemKey: (index) => {
      const row = rows[index]
      return row === undefined ? index : rowKey(row)
    },
  })

  return (
    <div ref={scrollRef} className="tree-scroll" role="tree" aria-label={label}>
      {rows.length === 0 ? (
        <p className="tree-empty">{empty}</p>
      ) : (
        <div className="tree-canvas" style={{ height: virtualizer.getTotalSize() }}>
          {virtualizer.getVirtualItems().map((item) => {
            const row = rows[item.index]
            return row === undefined ? null : (
              <div
                key={item.key}
                className="tree-slot"
                style={{ height: ROW_HEIGHT, transform: `translateY(${item.start}px)` }}
              >
                {renderRow(row)}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
