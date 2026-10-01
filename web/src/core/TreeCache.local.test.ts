import { chainApi, createCacheHarness } from '../testing/cacheHarness.ts'
import type { NodeId } from './types.ts'
import { isUuidV7 } from './uuid.ts'

const ROOT = 0
const A = 1
const B = 2
const C = 3
const E = 5

describe('TreeCache local operations', () => {
  it('never calls the server while editing, adding, deleting, expanding, and collapsing', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    await cache.load(server.id(B))
    const loadSpy = vi.spyOn(server, 'loadNode')
    const applySpy = vi.spyOn(server, 'apply')
    const before = server.row(server.id(A))?.value
    cache.edit(server.id(A), 'Edited')
    const added = cache.addChild(server.id(B), 'Child')
    cache.delete(server.id(B))
    cache.collapse(server.id(A))
    cache.expand(server.id(A))
    cache.toggle(server.id(A))
    cache.getRows()
    cache.getChangeSet()
    expect(added.ok).toBe(true)
    expect(loadSpy).not.toHaveBeenCalled()
    expect(applySpy).not.toHaveBeenCalled()
    expect(server.row(server.id(A))?.value).toBe(before)
  })

  describe('editing', () => {
    it('stages an edit and marks the node as modified', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      expect(cache.edit(server.id(A), 'New value')).toEqual({ ok: true })
      expect(cache.getNodeView(server.id(A))).toMatchObject({ value: 'New value', status: 'modified' })
    })

    it('trims whitespace before staging', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      cache.edit(server.id(A), '  abc  ')
      expect(cache.getNodeView(server.id(A))?.value).toBe('abc')
    })

    it('clears the modified state when the value is edited back', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      cache.edit(server.id(A), 'Other')
      cache.edit(server.id(A), 'A')
      expect(cache.getNodeView(server.id(A))?.status).toBe('unchanged')
      expect(cache.getChangeSet()).toBeNull()
    })

    it('rejects an invalid value and keeps the previous one', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      cache.edit(server.id(A), 'Kept')
      expect(cache.edit(server.id(A), '   ')).toMatchObject({ ok: false })
      expect(cache.edit(server.id(A), 'x'.repeat(201))).toMatchObject({ ok: false })
      expect(cache.getNodeView(server.id(A))?.value).toBe('Kept')
    })
  })

  describe('adding a child', () => {
    it('adds a new child with a client-generated UUIDv7 under its parent', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      const result = cache.addChild(server.id(A), ' Child ')
      if (!result.ok) {
        throw new Error(result.message)
      }
      expect(isUuidV7(result.id)).toBe(true)
      expect(cache.getNodeView(result.id)).toMatchObject({ value: 'Child', status: 'new' })
      expect(cache.getRows()).toEqual([
        { id: server.id(A), depth: 0 },
        { id: result.id, depth: 1 },
      ])
    })

    it('nests children of new nodes', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      const n1 = cache.addChild(server.id(A), 'N1')
      const n2 = n1.ok ? cache.addChild(n1.id, 'N2') : n1
      if (!n1.ok || !n2.ok) {
        throw new Error('add failed')
      }
      expect(cache.getRows().map((row) => [row.id, row.depth])).toEqual([
        [server.id(A), 0],
        [n1.id, 1],
        [n2.id, 2],
      ])
      expect(cache.getNodeView(n1.id)?.status).toBe('new')
      expect(cache.getNodeView(n2.id)?.status).toBe('new')
    })

    it('requires a value', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      expect(cache.addChild(server.id(A), '  ')).toMatchObject({ ok: false, message: 'The value must not be empty.' })
      expect(cache.getStatus().nodeCount).toBe(1)
    })
  })

  describe('deleting', () => {
    it('marks the node and its cached descendants as deleted', async () => {
      const { server, cache } = createCacheHarness()
      for (const index of [A, B, C]) {
        await cache.load(server.id(index))
      }
      cache.delete(server.id(A))
      for (const index of [A, B, C]) {
        expect(cache.getNodeView(server.id(index))?.status).toBe('deleted')
      }
    })

    it('marks a descendant across a gap as deleted immediately', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      await cache.load(server.id(C))
      cache.delete(server.id(A))
      expect(cache.getNodeView(server.id(C))?.status).toBe('deleted')
    })

    it('marks a new child of a deleted node as deleted', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      const child = cache.addChild(server.id(A), 'Child')
      cache.delete(server.id(A))
      expect(child.ok && cache.getNodeView(child.id)?.status).toBe('deleted')
    })

    it('leaves siblings and ancestors untouched', async () => {
      const { server, cache } = createCacheHarness()
      for (const index of [ROOT, A, E]) {
        await cache.load(server.id(index))
      }
      cache.delete(server.id(A))
      expect(cache.getNodeView(server.id(ROOT))?.status).toBe('unchanged')
      expect(cache.getNodeView(server.id(E))?.status).toBe('unchanged')
    })

    it('deletes a connected chain of thousands of nodes by deleting its top', async () => {
      const ids: NodeId[] = Array.from({ length: 5_000 }, (_, index) => `n${String(index).padStart(6, '0')}`)
      const { cache } = createCacheHarness(undefined, chainApi(ids))
      for (const id of ids) {
        await cache.load(id)
      }
      cache.delete(ids[0] ?? '')
      expect(cache.getRows().every((row) => cache.getNodeView(row.id)?.status === 'deleted')).toBe(true)
      expect(cache.getChangeSet()?.deleted).toEqual([ids[0]])
    })
  })

  describe('deleted nodes are read-only', () => {
    it('rejects edit, add child, and delete', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      await cache.load(server.id(B))
      cache.delete(server.id(A))
      expect(cache.edit(server.id(B), 'X')).toMatchObject({ ok: false })
      expect(cache.addChild(server.id(B), 'X')).toMatchObject({ ok: false })
      expect(cache.delete(server.id(B))).toMatchObject({ ok: false })
      expect(cache.delete(server.id(A))).toMatchObject({ ok: false })
    })

    it('shows the database value of an edited node inside a deleted branch and drops the edit from the change set', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      await cache.load(server.id(C))
      cache.edit(server.id(C), 'Edited')
      cache.delete(server.id(A))
      expect(cache.getNodeView(server.id(C))).toMatchObject({ status: 'deleted', value: 'C' })
      expect(cache.getChangeSet()).toMatchObject({ updated: [], deleted: [server.id(A)] })
    })
  })

  describe('status and snapshots', () => {
    it('derives every status', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      await cache.load(server.id(E))
      await cache.load(server.id(ROOT))
      cache.edit(server.id(E), 'Changed')
      const added = cache.addChild(server.id(A), 'New')
      expect(cache.getNodeView(server.id(ROOT))?.status).toBe('unchanged')
      expect(cache.getNodeView(server.id(E))?.status).toBe('modified')
      expect(added.ok && cache.getNodeView(added.id)?.status).toBe('new')
      cache.delete(server.id(A))
      expect(cache.getNodeView(server.id(A))?.status).toBe('deleted')
    })

    it('notifies subscribers and returns stable snapshots between changes', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      const listener = vi.fn()
      cache.subscribe(listener)
      const rows = cache.getRows()
      const view = cache.getNodeView(server.id(A))
      const status = cache.getStatus()
      expect(cache.getRows()).toBe(rows)
      expect(cache.getNodeView(server.id(A))).toBe(view)
      expect(cache.getStatus()).toBe(status)
      cache.edit(server.id(A), 'Edited')
      expect(listener).toHaveBeenCalledTimes(1)
      expect(cache.getRows()).toBe(rows)
      expect(cache.getNodeView(server.id(A))).not.toBe(view)
      expect(cache.getStatus()).not.toBe(status)
    })

    it('keeps the row list when only values change and rebuilds it on structural changes', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      await cache.load(server.id(B))
      const rows = cache.getRows()
      const computeRows = vi.spyOn(cache, 'computeRows')
      cache.edit(server.id(B), 'Edited')
      cache.delete(server.id(B))
      expect(cache.getRows()).toBe(rows)
      expect(computeRows).not.toHaveBeenCalled()
      cache.collapse(server.id(A))
      expect(cache.getRows()).toEqual([{ id: server.id(A), depth: 0 }])
      expect(computeRows).toHaveBeenCalledTimes(1)
    })

    it('keeps the view of an unaffected node when another node changes', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(A))
      await cache.load(server.id(E))
      const view = cache.getNodeView(server.id(E))
      cache.edit(server.id(A), 'Edited')
      expect(cache.getNodeView(server.id(E))).toBe(view)
    })
  })
})
