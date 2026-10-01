/**
 * HTTP benchmark of the composed application. It reads the ids of the large seeded tree from the
 * TREE_EDITOR_BENCH_IDS environment variable (JSON printed by benchmark-ids.sql), measures reads, then writes,
 * then the destructive operations (subtree deletion and Reset), and prints the results as a Markdown table.
 */

import { randomBytes, randomUUID } from 'node:crypto'

const baseUrl = `${process.env.TREE_EDITOR_URL ?? 'http://localhost:8080'}/api`
const ids = JSON.parse(process.env.TREE_EDITOR_BENCH_IDS ?? '{}')
const results = []
const WRITERS = 16

/** Returns a random UUIDv7 with the current timestamp. */
function uuid7() {
  const bytes = randomBytes(16)
  const now = BigInt(Date.now())
  for (let index = 0; index < 6; index++) {
    bytes[index] = Number((now >> BigInt(8 * (5 - index))) & 0xffn)
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x70
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = bytes.toString('hex')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

/** Sends a GET request and returns the parsed body and its size in bytes. */
async function get(path) {
  const response = await fetch(baseUrl + path)
  const text = await response.text()
  if (!response.ok) {
    throw new Error(`GET ${path} answered ${response.status}: ${text.slice(0, 200)}`)
  }
  return { json: JSON.parse(text), bytes: Buffer.byteLength(text) }
}

/** Sends a POST request with the headers the browser client sends and expects a 200. */
async function post(path, body) {
  const payload = JSON.stringify(body)
  const response = await fetch(baseUrl + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'tree-editor', 'Idempotency-Key': randomUUID() },
    body: payload,
  })
  const text = await response.text()
  if (response.status !== 200) {
    throw new Error(`POST ${path} answered ${response.status}: ${text.slice(0, 300)}`)
  }
  return { json: JSON.parse(text), bytes: Buffer.byteLength(payload) }
}

/** Returns the value at percentile `p` of an ascending array. */
function percentile(sorted, p) {
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))]
}

/**
 * Runs `task` for a number of iterations or seconds with the given concurrency after a warm-up, and records the
 * latency percentiles, the throughput, and the size of the last request or response.
 */
async function bench(name, task, { concurrency = 1, iterations = 0, seconds = 0, warmup = 5 } = {}) {
  for (let index = 0; index < warmup; index++) {
    await task(index, -1)
  }
  const latencies = []
  let bytes = 0
  let next = 0
  const deadline = performance.now() + seconds * 1000
  const started = performance.now()
  const worker = async (workerIndex) => {
    for (;;) {
      const index = next++
      if (iterations > 0 ? index >= iterations : performance.now() >= deadline) {
        return
      }
      const before = performance.now()
      const outcome = await task(index, workerIndex)
      latencies.push(performance.now() - before)
      bytes = outcome.bytes
    }
  }
  await Promise.all(Array.from({ length: concurrency }, (_, workerIndex) => worker(workerIndex)))
  const elapsedSeconds = (performance.now() - started) / 1000
  latencies.sort((a, b) => a - b)
  const row = {
    name,
    clients: concurrency,
    requests: latencies.length,
    perSecond: Math.round(latencies.length / elapsedSeconds),
    p50: percentile(latencies, 50),
    p95: percentile(latencies, 95),
    p99: percentile(latencies, 99),
    bytes,
  }
  results.push(row)
  console.error(`${name}: p50 ${row.p50.toFixed(1)} ms, p95 ${row.p95.toFixed(1)} ms, ${row.perSecond}/s`)
}

/** Measures one request that changes data and cannot be repeated. */
async function once(name, task) {
  await bench(name, task, { iterations: 1, warmup: 0 })
}

/** Formats the results as a Markdown table. */
function table() {
  const format = (value) => value.toFixed(1)
  const size = (bytes) => (bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`)
  const statistics = (row) =>
    row.requests === 1
      ? `- | ${format(row.p50)} | - | -`
      : `${row.perSecond} | ${format(row.p50)} | ${format(row.p95)} | ${format(row.p99)}`
  const lines = [
    '| Scenario | Clients | Requests | Per second | p50, ms | p95, ms | p99, ms | Size |',
    '|---|---:|---:|---:|---:|---:|---:|---:|',
    ...results.map((row) =>
      `| ${row.name} | ${row.clients} | ${row.requests} | ${statistics(row)} | ${size(row.bytes)} |`),
  ]
  return lines.join('\n')
}

const groups = (await get(`/nodes/${ids.bulk}/children?limit=200`)).json.items.map((item) => item.id)

await bench('Roots page (20)', () => get('/nodes/roots?limit=20'), { iterations: 300 })
await bench('Children page (20), first of 1,000', () => get(`/nodes/${ids.group500}/children?limit=20`), { iterations: 300 })
await bench('Children page (20), last of 1,000', () => get(`/nodes/${ids.group500}/children?limit=20&after=${ids.group500LastCursor}`), { iterations: 300 })
await bench('Children page (200)', () => get(`/nodes/${ids.group500}/children?limit=200`), { iterations: 200 })
await bench('Children page (200) of nodes with children', () => get(`/nodes/${ids.bulk}/children?limit=200`), { iterations: 200 })
await bench('Node at depth 10', () => get(`/nodes/${ids.chain10}`), { iterations: 200 })
await bench('Node at depth 1,000', () => get(`/nodes/${ids.chain1000}`), { iterations: 100 })
await bench('Node at depth 10,000', () => get(`/nodes/${ids.chain10000}`), { iterations: 50 })
await bench('Children page at depth 10,000', () => get(`/nodes/${ids.chain9999}/children?limit=20`), { iterations: 50 })
await bench('Children pages of different nodes', (index) => get(`/nodes/${groups[index % groups.length]}/children?limit=20`), { concurrency: 16, seconds: 10 })
await bench('Roots page', () => get('/nodes/roots?limit=20'), { concurrency: 16, seconds: 10 })

const generation = (await get('/nodes/roots?limit=1')).json.generation
const versions = new Map()
for (const id of ids.items) {
  versions.set(id, (await get(`/nodes/${id}`)).json.version)
}
const editOwnNode = async (index, workerIndex) => {
  const id = ids.items[workerIndex < 0 ? WRITERS : workerIndex]
  const outcome = await post('/changes', { generation, updated: [{ id, value: `Edited ${index}`, expectedVersion: versions.get(id) }] })
  versions.set(id, outcome.json.updated[0].version)
  return outcome
}
const createNodes = (count, value) => () =>
  post('/changes', {
    generation,
    created: Array.from({ length: count }, () => ({ id: uuid7(), parentId: ids.archive, value })),
  })

await bench('Apply, 1 edit', editOwnNode, { iterations: 200 })
await bench('Apply, 1 edit, each client on its own node', editOwnNode, { concurrency: WRITERS, seconds: 10 })
await bench('Apply, 100 new nodes', createNodes(100, 'Batch node'), { iterations: 30, warmup: 2 })
await bench('Apply, 1,000 new nodes', createNodes(1000, 'Batch node'), { iterations: 10, warmup: 1 })
await bench('Apply, 10,000 new nodes of 200 characters', createNodes(10_000, 'v'.repeat(200)), { iterations: 5, warmup: 1 })
await once('Apply, delete a leaf', () => post('/changes', { generation, deleted: [ids.leaf] }))
await once('Apply, delete a subtree of 1,001,000 nodes', () => post('/changes', { generation, deleted: [ids.bulk] }))
await bench('Children page under the deleted subtree', () => get(`/nodes/${ids.group500}/children?limit=20`), { iterations: 100 })
await once(`Reset with ${ids.nodes.toLocaleString('en-US')} rows`, () => post('/reset', {}))

console.log(`Database: ${ids.nodes.toLocaleString('en-US')} nodes, ${ids.tableSize}\n`)
console.log(table())
