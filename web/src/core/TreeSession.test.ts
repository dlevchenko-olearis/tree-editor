import { deferred } from '../testing/cacheHarness.ts'
import { FakeTreeServer, createTestIds, sampleSeed } from '../testing/FakeTreeServer.ts'
import type { ResetResult } from '../ports/ResetApi.ts'
import { TreeSession } from './TreeSession.ts'

const ROOT = 0
const A = 1
const B = 2
const E = 5

function createSession(server: FakeTreeServer, idStart = 1_800_000_000_000): TreeSession {
  return new TreeSession({
    treeApi: server,
    dbTreeApi: server,
    resetApi: server,
    pageSize: 20,
    createId: createTestIds(idStart),
  })
}

function rootLabels(session: TreeSession): string[] {
  return session.browser.getRows().map((row) => (row.kind === 'node' ? (session.browser.getNodeView(row.id)?.value ?? '?') : 'more'))
}

describe('TreeSession', () => {
  it('starts with the roots only', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.start()
    expect(rootLabels(session)).toEqual(['Root', 'Other root'])
    expect(server.calls.roots).toHaveLength(1)
  })

  it('restores the sample data, clears the cache, and collapses the database view on Reset', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.browser.expand(server.id(ROOT))
    await session.loadIntoCache(server.id(A))
    session.cache.addChild(server.id(A), 'Added')
    session.cache.edit(server.id(A), 'Edited')
    await session.apply()
    await session.loadIntoCache(server.id(E))
    session.cache.edit(server.id(E), 'Pending')
    await session.reset()
    expect(server.row(server.id(A))?.value).toBe('A')
    expect(session.cache.getStatus()).toMatchObject({ nodeCount: 0, canApply: false })
    await vi.waitFor(() => {
      expect(rootLabels(session)).toEqual(['Root', 'Other root'])
    })
    expect(session.notifications.getSnapshot()).toEqual([])
    expect(session.clock.generation).toBe(2)
  })

  it('does not reset while an Apply is in flight', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.loadIntoCache(server.id(A))
    session.cache.edit(server.id(A), 'Edited')
    const release = server.hold()
    const applying = session.apply()
    await session.reset()
    release()
    await applying
    expect(server.calls.reset).toBe(0)
  })

  it('clears a session that notices a Reset made by another session and tells the user', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const first = createSession(server)
    const second = createSession(server, 1_900_000_000_000)
    await first.start()
    await second.start()
    await second.browser.expand(server.id(ROOT))
    await second.loadIntoCache(server.id(A))
    second.cache.edit(server.id(A), 'Pending')
    await first.reset()
    await second.browser.expand(server.id(A))
    await vi.waitFor(() => {
      expect(rootLabels(second)).toEqual(['Root', 'Other root'])
    })
    expect(second.cache.getStatus().nodeCount).toBe(0)
    expect(second.notifications.getSnapshot().map((item) => item.kind)).toEqual(['dataReset'])
  })

  it('rejects a stale edit and a stale deletion after another session reset the tree', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const first = createSession(server)
    const second = createSession(server, 1_900_000_000_000)
    await first.start()
    await second.start()
    await second.loadIntoCache(server.id(A))
    await second.loadIntoCache(server.id(E))
    second.cache.edit(server.id(A), 'Stale edit')
    second.cache.delete(server.id(E))
    await first.reset()
    const outcome = await second.apply()
    expect(outcome.kind).toBe('dropped')
    expect(server.row(server.id(A))?.value).toBe('A')
    expect(server.isEffectivelyDeleted(server.id(E))).toBe(false)
    expect(second.cache.getStatus().nodeCount).toBe(0)
    expect(second.notifications.getSnapshot().map((item) => item.kind)).toEqual(['dataReset'])
  })

  it('removes a node that no longer exists from the database view when loading it', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.browser.expand(server.id(ROOT))
    server.removeDirectly(server.id(A))
    await session.loadIntoCache(server.id(A))
    expect(session.browser.getNodeView(server.id(A))).toBeUndefined()
    expect(session.notifications.getSnapshot().map((item) => item.kind)).toEqual(['noLongerExists'])
  })

  it('keeps the conflicts of the last Apply and removes one after its node is reloaded', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.loadIntoCache(server.id(B))
    session.cache.edit(server.id(B), 'Mine')
    server.editDirectly(server.id(B), 'Theirs')
    await session.apply()
    const [conflict] = session.getSnapshot().conflicts
    expect(conflict).toMatchObject({ id: server.id(B), kind: 'VersionMismatch' })
    if (conflict === undefined) {
      throw new Error('missing conflict')
    }
    await session.reloadConflict(conflict)
    expect(session.getSnapshot().conflicts).toEqual([])
    expect(session.cache.getNodeView(server.id(B))?.status).toBe('modified')
  })

  it('shows the result of an Apply in the expanded database levels', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.browser.expand(server.id(ROOT))
    await session.loadIntoCache(server.id(ROOT))
    session.cache.addChild(server.id(ROOT), 'Brand new')
    session.cache.edit(server.id(ROOT), 'Root edited')
    await session.apply()
    expect(rootLabels(session)).toEqual(['Root edited', 'A', 'E', 'Brand new', 'Other root'])
  })
  it('clears the outcome-unknown notice once the kept Apply is resolved or dropped by Reset', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.loadIntoCache(server.id(A))
    session.cache.edit(server.id(A), 'Edited')
    server.loseNextApplyResponseAfterCommit()
    await session.apply()
    expect(session.notifications.getSnapshot().map((item) => item.kind)).toContain('outcomeUnknown')

    await session.apply()
    expect(session.notifications.getSnapshot().map((item) => item.kind)).not.toContain('outcomeUnknown')

    session.cache.edit(server.id(A), 'Edited again')
    server.loseNextApplyResponseAfterCommit()
    await session.apply()
    await session.reset()
    expect(session.notifications.getSnapshot().map((item) => item.kind)).not.toContain('outcomeUnknown')
  })

  it('clears the outcome-unknown notice when the kept Apply is discarded and keeps the change pending', async () => {
    const server = new FakeTreeServer(sampleSeed())
    const session = createSession(server)
    await session.start()
    await session.loadIntoCache(server.id(A))
    session.cache.edit(server.id(A), 'Edited')
    server.answerNextApply({ kind: 'unknown', message: 'server error' })
    await session.apply()
    session.discardUnresolvedApply()
    expect(session.notifications.getSnapshot().map((item) => item.kind)).not.toContain('outcomeUnknown')
    expect(session.cache.getStatus()).toMatchObject({ hasUnresolvedApply: false, canApply: true })
  })

  it('starts over when the database was recreated and its generation went back', async () => {
    const server = new FakeTreeServer(sampleSeed())
    server.generation = 5
    const session = createSession(server)
    await session.start()
    await session.loadIntoCache(server.id(A))
    server.resetDirectly()
    server.generation = 1
    await session.refresh()
    expect(session.clock.generation).toBe(1)
    expect(session.cache.getStatus().nodeCount).toBe(0)
    await session.loadIntoCache(server.id(A))
    expect(session.cache.has(server.id(A))).toBe(true)
  })

  it("does not reset again when a newer generation arrived during the session's own Reset", async () => {
    const server = new FakeTreeServer(sampleSeed())
    const ownReset = deferred<ResetResult>()
    const session = new TreeSession({ treeApi: server, dbTreeApi: server, resetApi: { reset: () => ownReset.promise } })
    await session.start()
    const resetting = session.reset()
    server.resetDirectly()
    server.resetDirectly()
    await session.refresh()
    ownReset.resolve({ kind: 'reset', generation: 2 })
    await resetting
    expect(session.clock.generation).toBe(3)
  })
})
