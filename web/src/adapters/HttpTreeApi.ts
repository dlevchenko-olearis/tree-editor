/** Fetch-based adapter implementing every server port over the JSON HTTP API. */

import type {
  AppliedChanges,
  ChangeSet,
  Conflict,
  ConflictKind,
  DbListItem,
  LoadedNode,
  NodeId,
  VersionedId,
} from '../core/types.ts'
import type { DbTreeApi, PageResult } from '../ports/DbTreeApi.ts'
import type { ResetApi, ResetResult } from '../ports/ResetApi.ts'
import type { ApplyResult, LoadNodeResult, TreeApi } from '../ports/TreeApi.ts'

/** Header value the server requires on every request that changes data. */
export const REQUESTED_WITH = 'tree-editor'

/** Retry and transport settings; every member has a production default. */
export interface HttpTreeApiOptions {
  readonly baseUrl?: string
  readonly fetch?: typeof fetch
  readonly sleep?: (milliseconds: number) => Promise<void>
  readonly maxAttempts?: number
  readonly baseDelayMs?: number
  readonly maxDelayMs?: number
  readonly maxRetryAfterMs?: number
}

interface PageDto {
  readonly generation: number
  readonly items: readonly DbListItem[]
  readonly nextAfter?: NodeId | null
}

interface NodeDto {
  readonly generation: number
  readonly id: NodeId
  readonly parentId: NodeId | null
  readonly value: string
  readonly version: number
  readonly isDeleted: boolean
  readonly ancestorIds: readonly NodeId[]
}

interface AppliedDto {
  readonly generation: number
  readonly created?: readonly VersionedId[]
  readonly updated?: readonly VersionedId[]
  readonly deleted?: readonly NodeId[]
}

interface ConflictDto {
  readonly id?: NodeId | null
  readonly kind: ConflictKind
  readonly currentVersion?: number | null
  readonly parentId?: NodeId | null
}

interface ConflictsDto {
  readonly generation: number
  readonly conflicts: readonly ConflictDto[]
}

interface ProblemDto {
  readonly title?: string
  readonly detail?: string
  readonly generation?: number
  readonly errors?: Readonly<Record<string, readonly string[]>>
}

const TOO_LARGE_MESSAGE = 'The change set is too large to apply at once.'
const KEY_REUSED_MESSAGE = 'The Apply request was rejected because its key was reused with different changes.'
const RATE_LIMITED_MESSAGE = 'The server is busy and did not process the Apply. Try again in a moment.'

/** Talks to `/api`: pages and single nodes with GET, Apply and Reset with JSON POSTs carrying the anti-CSRF header. */
export class HttpTreeApi implements TreeApi, DbTreeApi, ResetApi {
  private readonly baseUrl: string
  private readonly fetchFn: typeof fetch
  private readonly sleep: (milliseconds: number) => Promise<void>
  private readonly maxAttempts: number
  private readonly baseDelayMs: number
  private readonly maxDelayMs: number
  private readonly maxRetryAfterMs: number

  constructor(options: HttpTreeApiOptions = {}) {
    this.baseUrl = options.baseUrl ?? ''
    this.fetchFn = options.fetch ?? globalThis.fetch.bind(globalThis)
    this.sleep =
      options.sleep ??
      ((milliseconds) =>
        new Promise((resolve) => {
          setTimeout(resolve, milliseconds)
        }))
    this.maxAttempts = Math.max(1, options.maxAttempts ?? 4)
    this.baseDelayMs = options.baseDelayMs ?? 500
    this.maxDelayMs = options.maxDelayMs ?? 8000
    this.maxRetryAfterMs = options.maxRetryAfterMs ?? 30000
  }

  roots(after: NodeId | null, limit: number): Promise<PageResult> {
    return this.getPage(`/api/nodes/roots${pageQuery(after, limit)}`)
  }

  children(parentId: NodeId, after: NodeId | null, limit: number): Promise<PageResult> {
    return this.getPage(`/api/nodes/${encodeURIComponent(parentId)}/children${pageQuery(after, limit)}`)
  }

  async loadNode(id: NodeId): Promise<LoadNodeResult> {
    const response = await this.send(`/api/nodes/${encodeURIComponent(id)}`, { method: 'GET' })
    if (response === null) {
      return { kind: 'failed', message: 'The server could not be reached.' }
    }
    if (response.ok) {
      const body = await readJson<NodeDto>(response)
      if (body === null) {
        return { kind: 'failed', message: 'The server sent an unreadable response.' }
      }
      const node: LoadedNode = {
        id: body.id,
        parentId: body.parentId,
        value: body.value,
        version: body.version,
        isDeleted: body.isDeleted,
        ancestorIds: body.ancestorIds,
      }
      return { kind: 'found', generation: body.generation, node }
    }
    if (response.status === 404) {
      return { kind: 'notFound', generation: generationOf(await readJson<ProblemDto>(response)) }
    }
    return { kind: 'failed', message: await problemMessage(response) }
  }

  async apply(changeSet: ChangeSet, idempotencyKey: string): Promise<ApplyResult> {
    const body = JSON.stringify(changeSet)
    let mayHaveApplied = false
    for (let attempt = 1; ; attempt++) {
      const response = await this.send('/api/changes', {
        method: 'POST',
        headers: mutationHeaders({ 'Idempotency-Key': idempotencyKey }),
        body,
      })
      if (response !== null && !isTransient(response.status)) {
        return parseApply(response)
      }
      mayHaveApplied ||= response?.status !== 429
      if (attempt >= this.maxAttempts) {
        if (!mayHaveApplied) {
          return { kind: 'notProcessed', message: RATE_LIMITED_MESSAGE }
        }
        return {
          kind: 'unknown',
          message: response === null ? 'The server could not be reached.' : `The server answered ${response.status}.`,
        }
      }
      await this.sleep(this.delayBeforeRetry(attempt, response))
    }
  }

  async reset(): Promise<ResetResult> {
    const response = await this.send('/api/reset', { method: 'POST', headers: mutationHeaders({}), body: '{}' })
    if (response === null) {
      return { kind: 'failed', message: 'The server could not be reached.' }
    }
    if (!response.ok) {
      return { kind: 'failed', message: await problemMessage(response) }
    }
    const body = await readJson<{ generation: number }>(response)
    return body === null
      ? { kind: 'failed', message: 'The server sent an unreadable response.' }
      : { kind: 'reset', generation: body.generation }
  }

  private async getPage(path: string): Promise<PageResult> {
    const response = await this.send(path, { method: 'GET' })
    if (response === null) {
      return { kind: 'failed', message: 'The server could not be reached.' }
    }
    if (response.ok) {
      const body = await readJson<PageDto>(response)
      if (body === null) {
        return { kind: 'failed', message: 'The server sent an unreadable response.' }
      }
      return {
        kind: 'page',
        generation: body.generation,
        items: body.items.map((item) => ({
          id: item.id,
          parentId: item.parentId,
          value: item.value,
          isDeleted: item.isDeleted,
          hasChildren: item.hasChildren,
        })),
        nextAfter: body.nextAfter ?? null,
      }
    }
    if (response.status === 404) {
      return { kind: 'notFound', generation: generationOf(await readJson<ProblemDto>(response)) }
    }
    return { kind: 'failed', message: await problemMessage(response) }
  }

  private async send(path: string, init: RequestInit): Promise<Response | null> {
    try {
      return await this.fetchFn(`${this.baseUrl}${path}`, { ...init, headers: { Accept: 'application/json', ...init.headers } })
    } catch {
      return null
    }
  }

  private delayBeforeRetry(attempt: number, response: Response | null): number {
    const retryAfter = response === null ? null : parseRetryAfter(response.headers.get('Retry-After'))
    if (retryAfter !== null) {
      return Math.min(retryAfter, this.maxRetryAfterMs)
    }
    return Math.min(this.maxDelayMs, this.baseDelayMs * 2 ** (attempt - 1))
  }
}

/** Parses a `Retry-After` header given in seconds or as an HTTP date, in milliseconds. */
export function parseRetryAfter(value: string | null, now: number = Date.now()): number | null {
  if (value === null || value.trim() === '') {
    return null
  }
  const seconds = Number(value)
  if (Number.isFinite(seconds)) {
    return Math.max(0, seconds * 1000)
  }
  const date = Date.parse(value)
  return Number.isNaN(date) ? null : Math.max(0, date - now)
}

function pageQuery(after: NodeId | null, limit: number): string {
  const query = new URLSearchParams({ limit: String(limit) })
  if (after !== null) {
    query.set('after', after)
  }
  return `?${query.toString()}`
}

function mutationHeaders(extra: Record<string, string>): Record<string, string> {
  return { 'Content-Type': 'application/json', 'X-Requested-With': REQUESTED_WITH, ...extra }
}

function isTransient(status: number): boolean {
  return status === 429 || status >= 500
}

async function parseApply(response: Response): Promise<ApplyResult> {
  const body = await readJson<unknown>(response)
  if (response.ok) {
    if (body === null) {
      return { kind: 'unknown', message: 'The server sent an unreadable response.' }
    }
    const applied = body as AppliedDto
    const result: AppliedChanges = {
      generation: applied.generation,
      created: applied.created ?? [],
      updated: applied.updated ?? [],
      deleted: applied.deleted ?? [],
    }
    return { kind: 'applied', result }
  }
  const conflicts = body as ConflictsDto | null
  if (response.status === 409 && conflicts !== null && Array.isArray(conflicts.conflicts)) {
    return {
      kind: 'conflicted',
      generation: conflicts.generation,
      conflicts: conflicts.conflicts.map(
        (conflict): Conflict => ({
          id: conflict.id ?? null,
          kind: conflict.kind,
          currentVersion: conflict.currentVersion ?? null,
          parentId: conflict.parentId ?? null,
        }),
      ),
    }
  }
  const problem = body as ProblemDto | null
  const generation = generationOf(problem)
  if (response.status === 413) {
    return { kind: 'rejected', status: 413, message: TOO_LARGE_MESSAGE, generation }
  }
  if (response.status === 422) {
    return { kind: 'rejected', status: 422, message: KEY_REUSED_MESSAGE, generation }
  }
  return { kind: 'rejected', status: response.status, message: describeProblem(response.status, problem), generation }
}

async function readJson<T>(response: Response): Promise<T | null> {
  try {
    return (await response.json()) as T
  } catch {
    return null
  }
}

function generationOf(problem: ProblemDto | null): number | null {
  return typeof problem?.generation === 'number' ? problem.generation : null
}

async function problemMessage(response: Response): Promise<string> {
  return describeProblem(response.status, await readJson<ProblemDto>(response))
}

function describeProblem(status: number, problem: ProblemDto | null): string {
  const details = problem?.errors === undefined ? [] : Object.values(problem.errors).flat()
  const parts = [problem?.title, problem?.detail, ...details].filter(
    (part): part is string => typeof part === 'string' && part.length > 0,
  )
  return parts.length > 0 ? parts.join(' ') : `The server answered ${status}.`
}
