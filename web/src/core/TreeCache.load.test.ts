import { chainApi, createCacheHarness } from '../testing/cacheHarness.ts'
import type { LoadNodeResult, TreeApi } from '../ports/TreeApi.ts'
import type { TreeCache } from './TreeCache.ts'
import type { NodeId } from './types.ts'

const ROOT = 0
const A = 1
const B = 2
const C = 3
const D = 4
const F = 6

function rowsOf(cache: TreeCache): string[] {
  return cache.getRows().map((row) => `${'  '.repeat(row.depth)}${cache.getNodeView(row.id)?.value ?? '?'}`)
}

describe('TreeCache loading', () => {
  it('shows nodes from different branches as separate top-level entries', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(C))
    await cache.load(server.id(F))
    expect(rowsOf(cache)).toEqual(['C', 'F'])
  })

  it('nests a child loaded before its parent and expands the parent', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(C))
    await cache.load(server.id(B))
    expect(rowsOf(cache)).toEqual(['B', '  C'])
    expect(cache.getNodeView(server.id(B))?.isExpanded).toBe(true)
  })

  it('keeps nodes behind a gap at the top level until the gap is filled', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    await cache.load(server.id(C))
    expect(rowsOf(cache)).toEqual(['A', 'C'])
    await cache.load(server.id(B))
    expect(rowsOf(cache)).toEqual(['A', '  B', '    C'])
  })

  it('assembles the hierarchy whatever the load order', async () => {
    const orders = [
      [ROOT, A, B, C, D],
      [D, C, B, A, ROOT],
      [B, D, ROOT, C, A],
    ]
    for (const order of orders) {
      const { server, cache } = createCacheHarness()
      for (const index of order) {
        await cache.load(server.id(index))
      }
      expect(rowsOf(cache)).toEqual(['Root', '  A', '    B', '      C', '        D'])
    }
  })

  it('adds only the loaded node, not its descendants', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    expect(cache.getStatus().nodeCount).toBe(1)
    expect(cache.has(server.id(B))).toBe(false)
  })

  it('keeps a pending edit when a newer database value is loaded', async () => {
    const { server, cache } = createCacheHarness()
    const id = server.id(B)
    await cache.load(id)
    cache.edit(id, 'Mine')
    server.editDirectly(id, 'Theirs')
    await cache.load(id)
    expect(cache.getNodeView(id)).toMatchObject({ value: 'Mine', status: 'modified' })
    expect(cache.getChangeSet()?.updated).toEqual([{ id, value: 'Mine', expectedVersion: server.row(id)?.version }])
  })

  it('is no longer modified when the new database value equals the pending value', async () => {
    const { server, cache } = createCacheHarness()
    const id = server.id(B)
    await cache.load(id)
    cache.edit(id, 'Same')
    server.editDirectly(id, 'Same')
    await cache.load(id)
    expect(cache.getNodeView(id)?.status).toBe('unchanged')
    expect(cache.getChangeSet()).toBeNull()
  })

  it('updates an unedited node to the newer database value', async () => {
    const { server, cache } = createCacheHarness()
    const id = server.id(B)
    await cache.load(id)
    server.editDirectly(id, 'Newer')
    await cache.load(id)
    expect(cache.getNodeView(id)).toMatchObject({ value: 'Newer', status: 'unchanged' })
  })

  it('sends a single request for a double click and adds the node once', async () => {
    const { server, cache } = createCacheHarness()
    const release = server.hold()
    const first = cache.load(server.id(C))
    const second = cache.load(server.id(C))
    release()
    await Promise.all([first, second])
    expect(server.calls.loadNode).toHaveLength(1)
    expect(cache.getRows()).toHaveLength(1)
  })

  it('shows a node deleted in the database meanwhile as deleted and discards its edit', async () => {
    const { server, cache } = createCacheHarness()
    const id = server.id(C)
    await cache.load(id)
    cache.edit(id, 'Edited')
    server.deleteDirectly(id)
    await cache.load(id)
    expect(cache.getNodeView(id)).toMatchObject({ status: 'deleted', value: 'C' })
    expect(cache.getChangeSet()).toBeNull()
  })

  it('accepts deletion through an ancestor deleted by another session although the version is unchanged', async () => {
    const { server, cache } = createCacheHarness()
    const id = server.id(C)
    await cache.load(id)
    cache.edit(id, 'Edited')
    const version = server.row(id)?.version
    server.deleteDirectly(server.id(A))
    expect(server.row(id)?.version).toBe(version)
    await cache.load(id)
    expect(cache.getNodeView(id)).toMatchObject({ status: 'deleted', value: 'C' })
    expect(cache.getChangeSet()).toBeNull()
  })

  it('treats deletion as one-way', async () => {
    const { server, cache } = createCacheHarness()
    const id = server.id(C)
    server.deleteDirectly(id)
    await cache.load(id)
    const row = server.row(id)
    if (row !== undefined) {
      row.isDeleted = false
    }
    server.editDirectly(id, 'Revived')
    await cache.load(id)
    expect(cache.getNodeView(id)?.status).toBe('deleted')
  })

  it('shows a node loaded under an ancestor deleted in the cache as deleted', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    cache.delete(server.id(A))
    await cache.load(server.id(D))
    expect(cache.getNodeView(server.id(D))?.status).toBe('deleted')
  })

  it('shows a node loaded under an ancestor deleted in the database as deleted', async () => {
    const { server, cache } = createCacheHarness()
    server.deleteDirectly(server.id(A))
    await cache.load(server.id(D))
    expect(cache.getNodeView(server.id(D))?.status).toBe('deleted')
  })

  it('drops a load response that arrives after a Reset', async () => {
    const { server, clock, cache } = createCacheHarness()
    await cache.load(server.id(A))
    const release = server.hold()
    const pending = cache.load(server.id(C))
    clock.reset(5, 'local')
    release()
    expect(await pending).toBe('dropped')
    expect(cache.getRows()).toEqual([])
    expect(cache.getStatus().nodeCount).toBe(0)
  })

  it('runs the Reset handling when a load reports a newer generation', async () => {
    const { server, clock, cache } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'Pending')
    server.resetDirectly()
    expect(await cache.load(server.id(C))).toBe('dropped')
    expect(clock.generation).toBe(2)
    expect(cache.getStatus().nodeCount).toBe(0)
    expect(cache.getChangeSet()).toBeNull()
  })

  it('tells the user when a node does not exist and marks a cached one as deleted', async () => {
    let answer: LoadNodeResult = {
      kind: 'found',
      generation: 1,
      node: { id: 'x', parentId: null, value: 'X', version: 1, isDeleted: false, ancestorIds: [] },
    }
    const api: TreeApi = {
      loadNode: () => Promise.resolve(answer),
      apply: () => Promise.reject(new Error('not used')),
    }
    const { cache, notifications } = createCacheHarness(undefined, api)
    await cache.load('x')
    cache.edit('x', 'Edited')
    answer = { kind: 'notFound', generation: 1 }
    expect(await cache.load('x')).toBe('notFound')
    expect(cache.getNodeView('x')).toMatchObject({ status: 'deleted', value: 'X' })
    expect(cache.getChangeSet()).toBeNull()
    expect(notifications).toEqual([{ kind: 'noLongerExists', message: 'The node no longer exists.' }])
    expect(await cache.load('y')).toBe('notFound')
    expect(notifications).toHaveLength(2)
    expect(cache.has('y')).toBe(false)
  })

  it('records each ancestor once while loading a 10,000-node chain from the bottom up', async () => {
    const ids: NodeId[] = Array.from({ length: 10_000 }, (_, index) => `n${String(index).padStart(6, '0')}`)
    const api = chainApi(ids)
    const { cache } = createCacheHarness(undefined, api)
    for (let index = ids.length - 1; index >= 0; index--) {
      await cache.load(ids[index] ?? '')
      expect(cache.knownIdCount).toBe(ids.length)
    }
    const rows = cache.getRows()
    expect(rows).toHaveLength(10_000)
    expect(rows[0]).toEqual({ id: ids[0], depth: 0 })
    expect(rows.at(-1)).toEqual({ id: ids.at(-1), depth: 9_999 })
    cache.delete(ids[0] ?? '')
    expect(cache.getNodeView(ids.at(-1) ?? '')?.status).toBe('deleted')
  })
})
