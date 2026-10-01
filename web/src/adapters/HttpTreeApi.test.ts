import { HttpTreeApi, parseRetryAfter } from './HttpTreeApi.ts'
import type { ChangeSet } from '../core/types.ts'

const ID = '01890a5d-ac96-774b-bcce-b302099a8057'
const PARENT = '01890a5d-ac96-774b-bcce-b302099a8050'
const KEY = '9b2f7e0c-1c4e-4d8e-9f6a-3f5a7c1b2d3e'

type FetchMock = ReturnType<typeof vi.fn<typeof fetch>>

function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  })
}

function createApi(responses: (Response | Error)[]) {
  const fetchMock: FetchMock = vi.fn<typeof fetch>()
  for (const response of responses) {
    if (response instanceof Error) {
      fetchMock.mockRejectedValueOnce(response)
    } else {
      fetchMock.mockResolvedValueOnce(response)
    }
  }
  const sleep = vi.fn((milliseconds: number) => Promise.resolve(void milliseconds))
  const api = new HttpTreeApi({ fetch: fetchMock, sleep, maxAttempts: 4, baseDelayMs: 100, maxDelayMs: 1000 })
  return { api, fetchMock, sleep }
}

function requestOf(fetchMock: FetchMock, call = 0): { url: string; init: RequestInit; headers: Headers } {
  const args = fetchMock.mock.calls[call]
  if (args === undefined) {
    throw new Error(`No fetch call ${call}`)
  }
  const [url, init = {}] = args
  return { url: String(url), init, headers: new Headers(init.headers) }
}

const changeSet: ChangeSet = {
  generation: 3,
  created: [{ id: ID, parentId: PARENT, value: 'New' }],
  updated: [{ id: PARENT, value: 'Edited', expectedVersion: 7 }],
  deleted: [],
}

describe('HttpTreeApi reads', () => {
  it('maps a roots page and sends the cursor and limit', async () => {
    const { api, fetchMock } = createApi([
      json(200, {
        generation: 4,
        items: [{ id: ID, parentId: null, value: 'Root', isDeleted: false, hasChildren: true }],
        nextAfter: ID,
      }),
    ])
    const result = await api.roots(PARENT, 20)
    expect(result).toEqual({
      kind: 'page',
      generation: 4,
      items: [{ id: ID, parentId: null, value: 'Root', isDeleted: false, hasChildren: true }],
      nextAfter: ID,
    })
    expect(requestOf(fetchMock).url).toBe(`/api/nodes/roots?limit=20&after=${PARENT}`)
    expect(requestOf(fetchMock).init.method).toBe('GET')
  })

  it('maps a children page without a cursor and treats a missing nextAfter as the last page', async () => {
    const { api, fetchMock } = createApi([json(200, { generation: 1, items: [] })])
    expect(await api.children(ID, null, 5)).toEqual({ kind: 'page', generation: 1, items: [], nextAfter: null })
    expect(requestOf(fetchMock).url).toBe(`/api/nodes/${ID}/children?limit=5`)
  })

  it('reports an unknown parent with the generation from the problem body', async () => {
    const { api } = createApi([json(404, { title: 'Not Found', status: 404, generation: 6 })])
    expect(await api.children(ID, null, 5)).toEqual({ kind: 'notFound', generation: 6 })
  })

  it('maps a single node with its ancestor chain', async () => {
    const { api, fetchMock } = createApi([
      json(200, {
        generation: 2,
        id: ID,
        parentId: PARENT,
        value: 'Leaf',
        version: 12,
        isDeleted: true,
        ancestorIds: [PARENT],
      }),
    ])
    expect(await api.loadNode(ID)).toEqual({
      kind: 'found',
      generation: 2,
      node: { id: ID, parentId: PARENT, value: 'Leaf', version: 12, isDeleted: true, ancestorIds: [PARENT] },
    })
    expect(requestOf(fetchMock).url).toBe(`/api/nodes/${ID}`)
  })

  it('reports a missing node and tolerates a problem body without a generation', async () => {
    const { api } = createApi([new Response('', { status: 404 })])
    expect(await api.loadNode(ID)).toEqual({ kind: 'notFound', generation: null })
  })

  it('reports network errors and server errors as failures', async () => {
    const { api } = createApi([new TypeError('offline'), json(500, { title: 'Server error' })])
    expect(await api.loadNode(ID)).toMatchObject({ kind: 'failed' })
    expect(await api.roots(null, 20)).toEqual({ kind: 'failed', message: 'Server error' })
  })
})

describe('HttpTreeApi Apply', () => {
  it('posts JSON with the anti-CSRF header and the idempotency key', async () => {
    const { api, fetchMock } = createApi([
      json(200, { generation: 3, created: [{ id: ID, version: 20 }], updated: [{ id: PARENT, version: 21 }], deleted: [] }),
    ])
    const result = await api.apply(changeSet, KEY)
    expect(result).toEqual({
      kind: 'applied',
      result: { generation: 3, created: [{ id: ID, version: 20 }], updated: [{ id: PARENT, version: 21 }], deleted: [] },
    })
    const { url, init, headers } = requestOf(fetchMock)
    expect(url).toBe('/api/changes')
    expect(init.method).toBe('POST')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('X-Requested-With')).toBe('tree-editor')
    expect(headers.get('Idempotency-Key')).toBe(KEY)
    expect(JSON.parse(String(init.body))).toEqual(changeSet)
  })

  it('parses every conflict of a 409 response', async () => {
    const { api } = createApi([
      json(409, {
        generation: 3,
        conflicts: [
          { id: ID, kind: 'VersionMismatch', currentVersion: 9 },
          { id: ID, kind: 'AlreadyExists', currentVersion: 4, parentId: PARENT },
          { id: null, kind: 'StaleGeneration' },
        ],
      }),
    ])
    expect(await api.apply(changeSet, KEY)).toEqual({
      kind: 'conflicted',
      generation: 3,
      conflicts: [
        { id: ID, kind: 'VersionMismatch', currentVersion: 9, parentId: null },
        { id: ID, kind: 'AlreadyExists', currentVersion: 4, parentId: PARENT },
        { id: null, kind: 'StaleGeneration', currentVersion: null, parentId: null },
      ],
    })
  })

  it('turns 413 into a "change set too large" rejection without retrying', async () => {
    const { api, fetchMock } = createApi([new Response('', { status: 413 })])
    expect(await api.apply(changeSet, KEY)).toEqual({
      kind: 'rejected',
      status: 413,
      message: 'The change set is too large to apply at once.',
      generation: null,
    })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('treats 400 and 422 as definite failures', async () => {
    const { api, fetchMock } = createApi([
      json(400, { title: 'Invalid change set', errors: { 'created[0].value': ['The value must not be empty.'] }, generation: 3 }),
      json(422, { title: 'Key reused', generation: 3 }),
    ])
    expect(await api.apply(changeSet, KEY)).toEqual({
      kind: 'rejected',
      status: 400,
      message: 'Invalid change set The value must not be empty.',
      generation: 3,
    })
    expect(await api.apply(changeSet, KEY)).toMatchObject({ kind: 'rejected', status: 422, generation: 3 })
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('retries network errors, 429, and 503 with the same key, honouring Retry-After', async () => {
    const { api, fetchMock, sleep } = createApi([
      new TypeError('offline'),
      json(429, { title: 'Too many requests' }, { 'Retry-After': '2' }),
      json(503, { title: 'Busy' }, { 'Retry-After': '1' }),
      json(200, { generation: 3, created: [], updated: [], deleted: [] }),
    ])
    expect((await api.apply(changeSet, KEY)).kind).toBe('applied')
    expect(fetchMock).toHaveBeenCalledTimes(4)
    const keys = fetchMock.mock.calls.map((_, call) => requestOf(fetchMock, call).headers.get('Idempotency-Key'))
    expect(new Set(keys)).toEqual(new Set([KEY]))
    expect(sleep.mock.calls.map(([milliseconds]) => milliseconds)).toEqual([100, 2000, 1000])
  })

  it('reports an Apply that was only ever rate limited as not processed', async () => {
    const { api, fetchMock } = createApi([json(429, {}), json(429, {}), json(429, {}), json(429, {})])
    expect(await api.apply(changeSet, KEY)).toMatchObject({ kind: 'notProcessed' })
    expect(fetchMock).toHaveBeenCalledTimes(4)
  })

  it('keeps the outcome unknown when an earlier attempt may have been applied before a 429', async () => {
    const { api } = createApi([new TypeError('offline'), json(429, {}), json(429, {}), json(429, {})])
    expect(await api.apply(changeSet, KEY)).toMatchObject({ kind: 'unknown' })
  })

  it('gives up after the last attempt and reports an unknown outcome', async () => {
    const { api, fetchMock, sleep } = createApi([
      json(503, {}),
      json(502, {}),
      new TypeError('offline'),
      json(500, {}),
    ])
    expect(await api.apply(changeSet, KEY)).toMatchObject({ kind: 'unknown' })
    expect(fetchMock).toHaveBeenCalledTimes(4)
    expect(sleep.mock.calls.map(([milliseconds]) => milliseconds)).toEqual([100, 200, 400])
  })
})

describe('HttpTreeApi Reset', () => {
  it('posts an empty JSON object with the anti-CSRF header and returns the generation', async () => {
    const { api, fetchMock } = createApi([json(200, { generation: 8 })])
    expect(await api.reset()).toEqual({ kind: 'reset', generation: 8 })
    const { url, init, headers } = requestOf(fetchMock)
    expect(url).toBe('/api/reset')
    expect(init.body).toBe('{}')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(headers.get('X-Requested-With')).toBe('tree-editor')
    expect(headers.has('Idempotency-Key')).toBe(false)
  })

  it('reports a failed Reset', async () => {
    const { api } = createApi([json(503, { title: 'Busy' })])
    expect(await api.reset()).toEqual({ kind: 'failed', message: 'Busy' })
  })
})

describe('parseRetryAfter', () => {
  it('reads seconds and HTTP dates', () => {
    expect(parseRetryAfter('3')).toBe(3000)
    expect(parseRetryAfter(null)).toBeNull()
    expect(parseRetryAfter('soon')).toBeNull()
    const now = Date.parse('Wed, 30 Sep 2026 12:00:00 GMT')
    expect(parseRetryAfter('Wed, 30 Sep 2026 12:00:05 GMT', now)).toBe(5000)
  })
})
