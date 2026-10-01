import { FakeTreeServer } from '../testing/FakeTreeServer.ts'
import type { SeedNode } from '../testing/FakeTreeServer.ts'
import { DbTreeBrowser } from './DbTreeBrowser.ts'
import { SessionClock } from './SessionClock.ts'
import type { ChangeSet } from './types.ts'

const ROOT = 0
const N1 = 1
const N2 = 2
const N7 = 7
const N8 = 8
const OTHER = 11

function seed(): SeedNode[] {
  return [
    { value: 'Root', parent: null },
    ...Array.from({ length: 7 }, (_, index) => ({ value: `Child ${index + 1}`, parent: 0 })),
    { value: 'Grandchild 8', parent: 1 },
    { value: 'Grandchild 9', parent: 1 },
    { value: 'Great-grandchild 10', parent: 8 },
    { value: 'Other root', parent: null },
  ]
}

function createBrowser(pageSize = 3) {
  const server = new FakeTreeServer(seed())
  const clock = new SessionClock()
  const notifications: { kind: string; message: string }[] = []
  const browser = new DbTreeBrowser({
    api: server,
    clock,
    notify: (kind, message) => notifications.push({ kind, message }),
    pageSize,
  })
  const labels = () =>
    browser.getRows().map((row) => {
      const indent = '  '.repeat(row.depth)
      if (row.kind === 'more') {
        return `${indent}[more]`
      }
      const view = browser.getNodeView(row.id)
      return `${indent}${view?.value ?? '?'}${view?.isDeleted === true ? ' (deleted)' : ''}`
    })
  return { server, clock, browser, notifications, labels }
}

async function applyThroughServer(server: FakeTreeServer, changeSet: Omit<ChangeSet, 'generation'>) {
  const outcome = await server.apply({ generation: server.generation, ...changeSet }, crypto.randomUUID())
  if (outcome.kind !== 'applied') {
    throw new Error(`Apply failed: ${outcome.kind}`)
  }
  return { changeSet: { generation: server.generation, ...changeSet }, result: outcome.result }
}

describe('DbTreeBrowser', () => {
  it('shows only the roots, collapsed, and requests no children', async () => {
    const { server, browser, labels } = createBrowser()
    await browser.loadRoots()
    expect(labels()).toEqual(['Root', 'Other root'])
    expect(server.calls.children).toHaveLength(0)
    expect(browser.getNodeView(server.id(ROOT))).toMatchObject({ isExpanded: false, hasChildren: true })
    expect(browser.getNodeView(server.id(OTHER))?.hasChildren).toBe(false)
  })

  it('fetches only the direct children of an expanded node', async () => {
    const { server, browser, labels } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(N1 - 1))
    expect(server.calls.children).toEqual([{ parentId: server.id(ROOT), after: null, limit: 10 }])
    expect(labels()).toEqual([
      'Root',
      ...Array.from({ length: 7 }, (_, index) => `  Child ${index + 1}`),
      'Other root',
    ])
  })

  it('does not expand a leaf', async () => {
    const { server, browser } = createBrowser()
    await browser.loadRoots()
    await browser.expand(server.id(OTHER))
    expect(server.calls.children).toHaveLength(0)
  })

  it('pages children in order without duplicates and hides "load more" on the last page', async () => {
    const { server, browser, labels } = createBrowser(3)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    expect(labels()).toEqual(['Root', '  Child 1', '  Child 2', '  Child 3', '  [more]', 'Other root'])
    await browser.loadMore(server.id(ROOT))
    await browser.loadMore(server.id(ROOT))
    expect(labels()).toEqual([
      'Root',
      ...Array.from({ length: 7 }, (_, index) => `  Child ${index + 1}`),
      'Other root',
    ])
    expect(server.calls.children.map((call) => call.after)).toEqual([null, server.id(3), server.id(6)])
  })

  it('shows every child of a deleted node as deleted', async () => {
    const { server, browser, labels } = createBrowser(10)
    server.deleteDirectly(server.id(N1))
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    await browser.expand(server.id(N8))
    expect(labels().slice(1, 6)).toEqual([
      '  Child 1 (deleted)',
      '    Grandchild 8 (deleted)',
      '      Great-grandchild 10 (deleted)',
      '    Grandchild 9 (deleted)',
      '  Child 2',
    ])
  })

  it('refreshes only the levels an Apply affected, with limit = loaded', async () => {
    const { server, browser, labels } = createBrowser(3)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    await browser.expand(server.id(N8))
    server.calls.children.length = 0
    server.calls.roots.length = 0
    const created = crypto.randomUUID()
    const applied = await applyThroughServer(server, {
      created: [{ id: created, parentId: server.id(N8), value: 'New under 8' }],
      updated: [{ id: server.id(N2), value: 'Child 2 edited', expectedVersion: server.row(server.id(N2))?.version ?? 0 }],
      deleted: [],
    })
    await browser.refreshAfterApply(applied.changeSet, applied.result)
    expect(server.calls.roots).toHaveLength(0)
    expect(server.calls.children).toEqual(
      expect.arrayContaining([
        { parentId: server.id(N8), after: null, limit: 2 },
        { parentId: server.id(ROOT), after: null, limit: 3 },
      ]),
    )
    expect(server.calls.children).toHaveLength(2)
    expect(labels()).toContain('      New under 8')
    expect(labels()).toContain('  Child 2 edited')
  })

  it('does not request an expanded level that the Apply did not affect', async () => {
    const { server, browser } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    server.calls.children.length = 0
    const applied = await applyThroughServer(server, {
      created: [],
      updated: [
        { id: server.id(OTHER), value: 'Other root edited', expectedVersion: server.row(server.id(OTHER))?.version ?? 0 },
      ],
      deleted: [],
    })
    await browser.refreshAfterApply(applied.changeSet, applied.result)
    expect(server.calls.children).toHaveLength(0)
    expect(server.calls.roots).toHaveLength(2)
  })

  it('shows a deleted node and its visible descendants as deleted after an Apply', async () => {
    const { server, browser, labels } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    await browser.expand(server.id(N8))
    server.calls.children.length = 0
    const applied = await applyThroughServer(server, { created: [], updated: [], deleted: [server.id(N1)] })
    await browser.refreshAfterApply(applied.changeSet, applied.result)
    expect(server.calls.children.map((call) => call.parentId).sort()).toEqual(
      [server.id(ROOT), server.id(N1), server.id(N8)].sort(),
    )
    expect(labels().slice(1, 5)).toEqual([
      '  Child 1 (deleted)',
      '    Grandchild 8 (deleted)',
      '      Great-grandchild 10 (deleted)',
      '    Grandchild 9 (deleted)',
    ])
  })

  it('refreshes the level of a collapsed leaf that received its first child', async () => {
    const { server, browser } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    server.calls.children.length = 0
    const applied = await applyThroughServer(server, {
      created: [{ id: crypto.randomUUID(), parentId: server.id(N7), value: 'First child' }],
      updated: [],
      deleted: [],
    })
    await browser.refreshAfterApply(applied.changeSet, applied.result)
    expect(server.calls.children).toEqual([{ parentId: server.id(ROOT), after: null, limit: 7 }])
    expect(browser.getNodeView(server.id(N7))?.hasChildren).toBe(true)
  })

  it('re-fetches every expanded level on Refresh and shows children added by another session', async () => {
    const { server, browser, labels } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    server.calls.children.length = 0
    server.addDirectly(server.id(N1), '01900000-0000-7000-8000-000000000000'.replace('0190', 'ffff'), 'Added elsewhere')
    await browser.refresh()
    expect(server.calls.children.map((call) => call.parentId).sort()).toEqual([server.id(ROOT), server.id(N1)].sort())
    expect(server.calls.roots).toHaveLength(2)
    expect(labels()).toContain('    Added elsewhere')
  })

  it('fetches a whole page on Refresh when the first load of a level failed', async () => {
    const server = new FakeTreeServer(seed())
    const clock = new SessionClock()
    const roots = vi
      .fn<typeof server.roots>()
      .mockResolvedValueOnce({ kind: 'failed', message: 'down' })
      .mockImplementation((after, limit) => server.roots(after, limit))
    const browser = new DbTreeBrowser({
      api: { roots, children: (parentId, after, limit) => server.children(parentId, after, limit) },
      clock,
      notify: () => {},
      pageSize: 3,
    })
    await browser.loadRoots()
    expect(browser.getStatus().rootsLoaded).toBe(false)
    await browser.refresh()
    expect(roots).toHaveBeenLastCalledWith(null, 3)
    expect(browser.getStatus().rootsLoaded).toBe(true)
  })

  it('removes a node that no longer exists and tells the user', async () => {
    const { server, browser, notifications, labels } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    await browser.collapse(server.id(N1))
    server.removeDirectly(server.id(N1))
    await browser.expand(server.id(N1))
    expect(labels()).not.toContain('  Child 1')
    expect(notifications).toEqual([{ kind: 'noLongerExists', message: 'The node no longer exists.' }])
  })

  it('collapses to the roots when a response shows that the tree was reset', async () => {
    const { server, clock, browser, labels } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    server.resetDirectly()
    server.calls.roots.length = 0
    await browser.expand(server.id(N1))
    await vi.waitFor(() => {
      expect(labels()).toEqual(['Root', 'Other root'])
    })
    expect(clock.generation).toBe(2)
    expect(server.calls.roots).toHaveLength(1)
  })

  it('collapses to the roots on a local Reset and drops late responses', async () => {
    const { server, clock, browser, labels } = createBrowser(10)
    await browser.loadRoots()
    const release = server.hold()
    const expanding = browser.expand(server.id(ROOT))
    server.resetDirectly()
    clock.reset(server.generation, 'local')
    release()
    await expanding
    await vi.waitFor(() => {
      expect(labels()).toEqual(['Root', 'Other root'])
    })
  })

  it('collapsing forgets the levels below and re-expanding fetches again', async () => {
    const { server, browser, labels } = createBrowser(10)
    await browser.loadRoots()
    await browser.expand(server.id(ROOT))
    await browser.expand(server.id(N1))
    browser.collapse(server.id(ROOT))
    expect(labels()).toEqual(['Root', 'Other root'])
    await browser.expand(server.id(ROOT))
    expect(browser.getNodeView(server.id(N1))?.isExpanded).toBe(false)
    expect(server.calls.children.filter((call) => call.parentId === server.id(ROOT))).toHaveLength(2)
  })

  it('browses a chain thousands of levels deep', async () => {
    const chain = Array.from({ length: 3_000 }, (_, index) => ({ value: `L${index}`, parent: index === 0 ? null : index - 1 }))
    const server = new FakeTreeServer(chain)
    const browser = new DbTreeBrowser({ api: server, clock: new SessionClock(), notify: () => {}, pageSize: 5 })
    await browser.loadRoots()
    for (let index = 0; index < 200; index++) {
      await browser.expand(server.id(index))
    }
    const rows = browser.getRows()
    expect(rows).toHaveLength(201)
    expect(rows.at(-1)).toMatchObject({ kind: 'node', depth: 200 })
  })
})
