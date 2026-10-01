/**
 * Benchmark of the client cache core (web/src/core) with 10,000-node workloads. It runs in Node, which executes
 * the TypeScript sources directly, against the in-memory server the frontend tests use, and prints a Markdown table.
 */

const source = (path) => new URL(`../web/src/${path}`, import.meta.url).href
const { FakeTreeServer, chainSeed, sampleSeed, createTestIds } = await import(source('testing/FakeTreeServer.ts'))
const { SessionClock } = await import(source('core/SessionClock.ts'))
const { TreeCache } = await import(source('core/TreeCache.ts'))

const SIZE = 10_000
const results = []

/** Measures one operation and records its duration with a short description of the outcome. */
async function measure(name, operation) {
  const started = performance.now()
  const outcome = await operation()
  results.push({ name, ms: performance.now() - started, outcome })
}

/** Creates a cache over the given in-memory server. */
function createCache(server) {
  return new TreeCache({
    api: server,
    clock: new SessionClock(),
    notify: () => {},
    createId: createTestIds(1_900_000_000_000),
    createKey: () => crypto.randomUUID(),
  })
}

const chainServer = new FakeTreeServer(chainSeed(SIZE))
const chainIds = Array.from({ length: SIZE }, (_, index) => chainServer.id(index))
const chain = createCache(chainServer)

await measure(`Load a ${SIZE.toLocaleString('en-US')}-node chain one node at a time, deepest first`, async () => {
  for (let index = SIZE - 1; index >= 0; index--) {
    await chain.load(chainIds[index])
  }
  return `parentOf holds ${chain.knownIdCount} ids`
})
await measure(`Build the visible rows of the chain`, async () => {
  const rows = chain.computeRows()
  return `${rows.length} rows, deepest at depth ${rows.at(-1).depth}`
})
await measure('Edit one value and read the rows and the node view again', async () => {
  const rows = chain.getRows()
  chain.edit(chainIds[SIZE - 1], 'Edited')
  return `row list reused: ${chain.getRows() === rows}, status ${chain.getNodeView(chainIds[SIZE - 1]).status}`
})
await measure('Delete the top node and resolve the status of every node', async () => {
  chain.delete(chainIds[0])
  return `${chainIds.filter((id) => chain.getNodeView(id).status === 'deleted').length} deleted`
})

const sampleServer = new FakeTreeServer(sampleSeed())
const flat = createCache(sampleServer)
await flat.load(sampleServer.id(0))
await measure(`Add ${SIZE.toLocaleString('en-US')} new children under one node`, async () => {
  for (let index = 0; index < SIZE; index++) {
    flat.addChild(sampleServer.id(0), `Child ${index}`)
  }
  return ''
})
await measure('Build the change set of those new children', async () => `${flat.getChangeSet().created.length} created`)

const nested = createCache(sampleServer)
await nested.load(sampleServer.id(0))
await measure(`Add a chain of ${SIZE.toLocaleString('en-US')} nested new nodes`, async () => {
  let parentId = sampleServer.id(0)
  for (let index = 0; index < SIZE; index++) {
    parentId = nested.addChild(parentId, `Nested ${index}`).id
  }
  return ''
})
await measure('Build the change set of the nested chain, parent first', async () => `${nested.getChangeSet().created.length} created`)

console.log('| Operation | ms | Outcome |')
console.log('|---|---:|---|')
for (const { name, ms, outcome } of results) {
  console.log(`| ${name} | ${ms.toFixed(1)} | ${outcome} |`)
}
