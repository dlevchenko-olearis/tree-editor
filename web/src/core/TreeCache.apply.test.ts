import { createCacheHarness } from '../testing/cacheHarness.ts'
import type { AddChildResult, TreeCache } from './TreeCache.ts'
import type { NodeId } from './types.ts'

const ROOT = 0
const A = 1
const B = 2
const C = 3
const E = 5

function idOf(result: AddChildResult): NodeId {
  if (!result.ok) {
    throw new Error(result.message)
  }
  return result.id
}

function pending(cache: TreeCache): boolean {
  return cache.getChangeSet() !== null
}

describe('change set building', () => {
  it('disables Apply when nothing is pending', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    expect(cache.getStatus().canApply).toBe(false)
    expect(await cache.apply()).toEqual({ kind: 'nothing' })
    expect(server.calls.apply).toHaveLength(0)
  })

  it('disables Apply when the only change is a new node that was deleted again', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    const child = idOf(cache.addChild(server.id(A), 'Temp'))
    expect(cache.getStatus().canApply).toBe(true)
    cache.delete(child)
    expect(cache.getStatus().canApply).toBe(false)
  })

  it('lists new nodes parent first, edits with their base version, and only top-most deletions', async () => {
    const { server, cache } = createCacheHarness()
    for (const index of [ROOT, A, B, C, E]) {
      await cache.load(server.id(index))
    }
    const n1 = idOf(cache.addChild(server.id(E), 'N1'))
    const n2 = idOf(cache.addChild(n1, 'N2'))
    const n3 = idOf(cache.addChild(n1, 'N3'))
    const n4 = idOf(cache.addChild(server.id(ROOT), 'N4'))
    cache.edit(server.id(E), 'E2')
    cache.edit(server.id(C), 'C2')
    cache.delete(server.id(C))
    cache.delete(server.id(A))
    expect(cache.getChangeSet()).toEqual({
      generation: 1,
      created: [
        { id: n1, parentId: server.id(E), value: 'N1' },
        { id: n2, parentId: n1, value: 'N2' },
        { id: n3, parentId: n1, value: 'N3' },
        { id: n4, parentId: server.id(ROOT), value: 'N4' },
      ],
      updated: [{ id: server.id(E), value: 'E2', expectedVersion: server.row(server.id(E))?.version }],
      deleted: [server.id(A)],
    })
  })

  it('leaves out new nodes inside a deleted branch', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    const n1 = idOf(cache.addChild(server.id(A), 'N1'))
    idOf(cache.addChild(n1, 'N2'))
    cache.delete(n1)
    expect(cache.getChangeSet()).toBeNull()
  })
})

describe('Apply', () => {
  it('persists creations, edits, and deletions and shows the nodes as unchanged or deleted', async () => {
    const { server, cache } = createCacheHarness()
    for (const index of [A, B, E]) {
      await cache.load(server.id(index))
    }
    const child = idOf(cache.addChild(server.id(B), 'Child'))
    cache.edit(server.id(A), 'A2')
    cache.delete(server.id(E))
    const outcome = await cache.apply()
    expect(outcome.kind).toBe('applied')
    expect(server.row(child)).toMatchObject({ parentId: server.id(B), value: 'Child' })
    expect(server.row(server.id(A))?.value).toBe('A2')
    expect(server.isEffectivelyDeleted(server.id(E))).toBe(true)
    expect(cache.getNodeView(child)?.status).toBe('unchanged')
    expect(cache.getNodeView(server.id(A))).toMatchObject({ status: 'unchanged', value: 'A2' })
    expect(cache.getNodeView(server.id(E))?.status).toBe('deleted')
    expect(cache.getStatus().canApply).toBe(false)
  })

  it('updates only the nodes named in the response', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    await cache.load(server.id(E))
    const untouched = cache.getNodeView(server.id(E))
    cache.edit(server.id(A), 'A2')
    await cache.apply()
    expect(cache.getNodeView(server.id(E))).toBe(untouched)
    cache.edit(server.id(A), 'A3')
    expect(cache.getChangeSet()?.updated).toEqual([
      { id: server.id(A), value: 'A3', expectedVersion: server.row(server.id(A))?.version },
    ])
  })

  it('keeps edits made while the Apply was in flight', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'First')
    const release = server.hold()
    const applying = cache.apply()
    expect(cache.getStatus()).toMatchObject({ applyInFlight: true, canApply: false })
    expect(await cache.apply()).toEqual({ kind: 'busy' })
    cache.edit(server.id(A), 'Second')
    release()
    await applying
    expect(server.row(server.id(A))?.value).toBe('First')
    expect(cache.getNodeView(server.id(A))).toMatchObject({ value: 'Second', status: 'modified' })
    expect(cache.getChangeSet()?.updated[0]?.value).toBe('Second')
  })

  it('keeps nodes created while the Apply was in flight pending', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'First')
    const release = server.hold()
    const applying = cache.apply()
    const child = idOf(cache.addChild(server.id(A), 'Late'))
    release()
    await applying
    expect(cache.getNodeView(child)?.status).toBe('new')
    expect(cache.getChangeSet()?.created).toEqual([{ id: child, parentId: server.id(A), value: 'Late' }])
  })

  it('clears a covered deletion after success', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    await cache.load(server.id(C))
    cache.delete(server.id(C))
    cache.delete(server.id(A))
    expect(cache.getChangeSet()?.deleted).toEqual([server.id(A)])
    await cache.apply()
    expect(cache.getChangeSet()).toBeNull()
    expect(cache.getStatus().canApply).toBe(false)
    expect(cache.getNodeView(server.id(C))?.status).toBe('deleted')
  })

  it('clears a new node that was created and deleted, and never sends it', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    const temp = idOf(cache.addChild(server.id(A), 'Temp'))
    cache.delete(temp)
    cache.edit(server.id(A), 'A2')
    await cache.apply()
    expect(server.row(temp)).toBeUndefined()
    expect(pending(cache)).toBe(false)
  })

  it('clears the pending deletion of a node that was already deleted by another session', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(C))
    cache.delete(server.id(C))
    server.deleteDirectly(server.id(A))
    const version = server.row(server.id(C))?.version
    const outcome = await cache.apply()
    expect(outcome.kind === 'applied' && outcome.result.deleted).toEqual([server.id(C)])
    expect(server.row(server.id(C))?.version).toBe(version)
    expect(pending(cache)).toBe(false)
  })

  it('sends a kept request again with the same key before any new change set', async () => {
    const { server, cache, keys, notifications } = createCacheHarness()
    await cache.load(server.id(A))
    const child = idOf(cache.addChild(server.id(A), 'Child'))
    cache.edit(server.id(A), 'A2')
    server.loseNextApplyResponseAfterCommit()
    expect(await cache.apply()).toEqual({ kind: 'unknown' })
    expect(notifications.at(-1)?.kind).toBe('outcomeUnknown')
    expect(cache.getStatus()).toMatchObject({ hasUnresolvedApply: true, canApply: true })
    cache.edit(server.id(A), 'A3')
    const outcome = await cache.apply()
    expect(outcome.kind).toBe('applied')
    expect(server.calls.apply.map((call) => call.key)).toEqual([keys[0], keys[0]])
    expect(server.calls.apply[1]?.changeSet).toEqual(server.calls.apply[0]?.changeSet)
    expect(cache.getNodeView(child)?.status).toBe('unchanged')
    expect(cache.getNodeView(server.id(A))).toMatchObject({ value: 'A3', status: 'modified' })
    const next = await cache.apply()
    expect(next.kind).toBe('applied')
    expect(server.row(server.id(A))?.value).toBe('A3')
    expect(keys).toHaveLength(2)
  })

  it('discards a kept request that keeps failing, and the next Apply sends the pending changes with a new key', async () => {
    const { server, cache, keys } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'A2')
    server.answerNextApply({ kind: 'unknown', message: 'server error' })
    expect(await cache.apply()).toEqual({ kind: 'unknown' })
    server.answerNextApply({ kind: 'unknown', message: 'server error' })
    expect(await cache.apply()).toEqual({ kind: 'unknown' })
    cache.discardUnresolvedApply()
    expect(cache.getStatus()).toMatchObject({ hasUnresolvedApply: false, canApply: true })
    expect((await cache.apply()).kind).toBe('applied')
    expect(server.calls.apply.map((call) => call.key)).toEqual([keys[0], keys[0], keys[1]])
    expect(server.row(server.id(A))?.value).toBe('A2')
  })

  it('reports an edit that a discarded request did apply as a version conflict on the next Apply', async () => {
    const { server, cache } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'A2')
    server.loseNextApplyResponseAfterCommit()
    await cache.apply()
    cache.discardUnresolvedApply()
    const outcome = await cache.apply()
    expect(outcome.kind === 'conflicted' && outcome.conflicts.map((conflict) => conflict.kind)).toEqual(['VersionMismatch'])
    await cache.load(server.id(A))
    expect(cache.getNodeView(server.id(A))).toMatchObject({ value: 'A2', status: 'unchanged' })
  })

  it('does not keep a new request that the server turned away without processing it', async () => {
    const { server, cache, notifications } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'A2')
    server.answerNextApply({ kind: 'notProcessed', message: 'Busy.' })
    expect(await cache.apply()).toEqual({ kind: 'rejected', message: 'Busy.' })
    expect(notifications.at(-1)).toEqual({ kind: 'error', message: 'Busy.' })
    expect(cache.getStatus()).toMatchObject({ hasUnresolvedApply: false, canApply: true })
  })

  it('keeps a request whose outcome is unknown when its resend is turned away without processing', async () => {
    const { server, cache, keys } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'A2')
    server.loseNextApplyResponseAfterCommit()
    await cache.apply()
    server.answerNextApply({ kind: 'notProcessed', message: 'Busy.' })
    expect(await cache.apply()).toEqual({ kind: 'unknown' })
    expect(cache.getStatus().hasUnresolvedApply).toBe(true)
    expect((await cache.apply()).kind).toBe('applied')
    expect(server.calls.apply.map((call) => call.key)).toEqual([keys[0], keys[0], keys[0]])
  })

  it('drops an Apply response that arrives after a Reset', async () => {
    const { server, clock, cache } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'A2')
    const release = server.hold()
    const applying = cache.apply()
    clock.reset(9, 'local')
    release()
    expect(await applying).toEqual({ kind: 'dropped' })
    expect(cache.getStatus()).toMatchObject({ nodeCount: 0, applyInFlight: false, hasUnresolvedApply: false })
  })

  describe('conflicts', () => {
    it('marks a version conflict, keeps the edit, and lets a reload resolve it', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(B))
      await cache.load(server.id(E))
      cache.edit(server.id(B), 'Mine')
      cache.edit(server.id(E), 'Mine too')
      server.editDirectly(server.id(B), 'Theirs')
      server.editDirectly(server.id(E), 'Theirs too')
      const outcome = await cache.apply()
      expect(outcome).toMatchObject({
        kind: 'conflicted',
        conflicts: [
          { id: server.id(B), kind: 'VersionMismatch' },
          { id: server.id(E), kind: 'VersionMismatch' },
        ],
      })
      expect(server.row(server.id(B))?.value).toBe('Theirs')
      expect(cache.getNodeView(server.id(B))).toMatchObject({ status: 'conflicted', value: 'Mine' })
      await cache.load(server.id(B))
      await cache.load(server.id(E))
      expect(cache.getNodeView(server.id(B))).toMatchObject({ status: 'modified', value: 'Mine' })
      expect((await cache.apply()).kind).toBe('applied')
      expect(server.row(server.id(B))?.value).toBe('Mine')
    })

    it('shows an edited node deleted by another session as deleted without its edit', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(C))
      cache.edit(server.id(C), 'Mine')
      server.deleteDirectly(server.id(A))
      const outcome = await cache.apply()
      expect(outcome).toMatchObject({ kind: 'conflicted', conflicts: [{ id: server.id(C), kind: 'Deleted' }] })
      expect(cache.getNodeView(server.id(C))).toMatchObject({ status: 'deleted', value: 'C' })
      expect(pending(cache)).toBe(false)
    })

    it('marks a node reported as not found as deleted', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(C))
      cache.edit(server.id(C), 'Mine')
      server.answerNextApply({
        kind: 'conflicted',
        generation: 1,
        conflicts: [{ id: server.id(C), kind: 'NotFound', currentVersion: null, parentId: null }],
      })
      await cache.apply()
      expect(cache.getNodeView(server.id(C))?.status).toBe('deleted')
      expect(pending(cache)).toBe(false)
    })

    it.each(['ParentDeleted', 'ParentNotFound'] as const)(
      'marks the parent of a new node as deleted on %s',
      async (kind) => {
        const { server, cache } = createCacheHarness()
        await cache.load(server.id(B))
        const child = idOf(cache.addChild(server.id(B), 'Child'))
        server.answerNextApply({
          kind: 'conflicted',
          generation: 1,
          conflicts: [{ id: child, kind, currentVersion: null, parentId: server.id(B) }],
        })
        await cache.apply()
        expect(cache.getNodeView(server.id(B))?.status).toBe('deleted')
        expect(cache.getNodeView(child)?.status).toBe('deleted')
        expect(pending(cache)).toBe(false)
      },
    )

    it('reports ParentDeleted when the parent was deleted by another session', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(B))
      const child = idOf(cache.addChild(server.id(B), 'Child'))
      server.deleteDirectly(server.id(A))
      const outcome = await cache.apply()
      expect(outcome).toMatchObject({ conflicts: [{ id: child, kind: 'ParentDeleted', parentId: server.id(B) }] })
      expect(cache.getNodeView(child)?.status).toBe('deleted')
    })

    it('adopts a node that already exists under the same parent and keeps the difference as a pending edit', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(B))
      const child = idOf(cache.addChild(server.id(B), 'Mine'))
      server.addDirectly(server.id(B), child, 'Stored')
      const outcome = await cache.apply()
      expect(outcome).toMatchObject({ conflicts: [{ id: child, kind: 'AlreadyExists', parentId: server.id(B) }] })
      await vi.waitFor(() => {
        expect(server.calls.loadNode).toContain(child)
      })
      await vi.waitFor(() => {
        expect(cache.getNodeView(child)).toMatchObject({ status: 'modified', value: 'Mine' })
      })
      expect(cache.getChangeSet()).toMatchObject({
        created: [],
        updated: [{ id: child, value: 'Mine', expectedVersion: server.row(child)?.version }],
      })
    })

    it('adopts an existing node without a pending edit when the stored value matches', async () => {
      const { server, cache } = createCacheHarness()
      await cache.load(server.id(B))
      const child = idOf(cache.addChild(server.id(B), 'Same'))
      server.addDirectly(server.id(B), child, 'Same')
      await cache.apply()
      await vi.waitFor(() => {
        expect(cache.getNodeView(child)?.status).toBe('unchanged')
      })
      expect(pending(cache)).toBe(false)
    })

    it('runs the Reset handling on StaleGeneration', async () => {
      const { server, clock, cache } = createCacheHarness()
      await cache.load(server.id(A))
      cache.edit(server.id(A), 'Stale')
      cache.delete(server.id(A))
      server.resetDirectly()
      expect(await cache.apply()).toEqual({ kind: 'dropped' })
      expect(clock.generation).toBe(2)
      expect(cache.getStatus()).toMatchObject({ nodeCount: 0, canApply: false })
      expect(server.row(server.id(A))).toMatchObject({ value: 'A', isDeleted: false })
    })
  })

  it('reports a rejected change set and clears the kept request', async () => {
    const { server, cache, notifications } = createCacheHarness()
    await cache.load(server.id(A))
    cache.edit(server.id(A), 'A2')
    server.answerNextApply({ kind: 'rejected', status: 413, message: 'The change set is too large.', generation: null })
    expect(await cache.apply()).toEqual({ kind: 'rejected', message: 'The change set is too large.' })
    expect(notifications.at(-1)).toEqual({ kind: 'error', message: 'The change set is too large.' })
    expect(cache.getStatus()).toMatchObject({ hasUnresolvedApply: false, canApply: true })
  })
})
