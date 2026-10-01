import { isOnMarkedPath, visibleRows, walkUp } from './traversal.ts'
import type { NodeId } from './types.ts'

function chain(length: number): { ids: NodeId[]; parentOf: Map<NodeId, NodeId | null> } {
  const ids = Array.from({ length }, (_, index) => `n${String(index).padStart(6, '0')}`)
  const parentOf = new Map<NodeId, NodeId | null>()
  ids.forEach((id, index) => parentOf.set(id, index === 0 ? null : (ids[index - 1] ?? null)))
  return { ids, parentOf }
}

describe('walkUp', () => {
  it('yields the start and every known ancestor up to the root', () => {
    const { ids, parentOf } = chain(4)
    expect([...walkUp(parentOf, 'n000003')]).toEqual([...ids].reverse())
  })

  it('stops at an id whose parent is unknown', () => {
    const parentOf = new Map<NodeId, NodeId | null>([['c', 'b']])
    expect([...walkUp(parentOf, 'c')]).toEqual(['c', 'b'])
  })

  it('walks a 10,000-level chain without overflowing the stack', () => {
    const { parentOf } = chain(10_000)
    let count = 0
    for (const id of walkUp(parentOf, 'n009999')) {
      expect(id).toBeTruthy()
      count++
    }
    expect(count).toBe(10_000)
  })
})

describe('visibleRows', () => {
  const children: Record<string, NodeId[]> = { a: ['b', 'c'], b: ['d'], c: [], d: [] }

  it('lists rows depth-first and only under expanded nodes', () => {
    const rows = [...visibleRows(['a', 'x'], (id) => children[id] ?? [], (id) => id === 'a' || id === 'b')]
    expect(rows).toEqual([
      { id: 'a', depth: 0 },
      { id: 'b', depth: 1 },
      { id: 'd', depth: 2 },
      { id: 'c', depth: 1 },
      { id: 'x', depth: 0 },
    ])
  })

  it('skips collapsed subtrees', () => {
    const rows = [...visibleRows(['a'], (id) => children[id] ?? [], () => false)]
    expect(rows).toEqual([{ id: 'a', depth: 0 }])
  })

  it('renders a fully expanded 10,000-level chain without overflowing the stack', () => {
    const { ids } = chain(10_000)
    const next = new Map(ids.map((id, index) => [id, ids[index + 1]]))
    const rows = [...visibleRows([ids[0] ?? ''], (id) => [next.get(id)].filter((child) => child !== undefined), () => true)]
    expect(rows).toHaveLength(10_000)
    expect(rows.at(-1)).toEqual({ id: 'n009999', depth: 9_999 })
  })
})

describe('isOnMarkedPath', () => {
  it('finds a marked ancestor across ids that are known only through parentOf', () => {
    const { parentOf } = chain(5)
    const memo = new Map<NodeId, boolean>()
    expect(isOnMarkedPath('n000004', parentOf, new Set(['n000001']), memo)).toBe(true)
    expect(isOnMarkedPath('n000000', parentOf, new Set(['n000001']), memo)).toBe(false)
  })

  it('memoizes every id on the walked path', () => {
    const { parentOf } = chain(10_000)
    const memo = new Map<NodeId, boolean>()
    expect(isOnMarkedPath('n009999', parentOf, new Set(), memo)).toBe(false)
    expect(memo.size).toBe(10_000)
    const lookups = new Map(parentOf)
    const spy = vi.spyOn(lookups, 'get')
    expect(isOnMarkedPath('n009998', lookups, new Set(), memo)).toBe(false)
    expect(spy).toHaveBeenCalledTimes(0)
  })
})
