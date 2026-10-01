/** Builders shared by the core tests. */

import { SessionClock } from '../core/SessionClock.ts'
import { TreeCache } from '../core/TreeCache.ts'
import type { NodeId } from '../core/types.ts'
import type { LoadNodeResult, TreeApi } from '../ports/TreeApi.ts'
import { FakeTreeServer, createTestIds, sampleSeed } from './FakeTreeServer.ts'
import type { SeedNode } from './FakeTreeServer.ts'

/** A cache wired to a fake server. */
export interface CacheHarness {
  readonly server: FakeTreeServer
  readonly clock: SessionClock
  readonly cache: TreeCache
  readonly notifications: { kind: string; message: string }[]
  readonly keys: string[]
}

/** Creates a cache over a fake server; new client ids sort after every seed id. */
export function createCacheHarness(seed: readonly SeedNode[] = sampleSeed(), api?: TreeApi): CacheHarness {
  const server = new FakeTreeServer(seed)
  const clock = new SessionClock()
  const notifications: { kind: string; message: string }[] = []
  const keys: string[] = []
  let keyCounter = 0
  const cache = new TreeCache({
    api: api ?? server,
    clock,
    notify: (kind, message) => notifications.push({ kind, message }),
    createId: createTestIds(1_800_000_000_000),
    createKey: () => {
      const key = `00000000-0000-4000-8000-${String(++keyCounter).padStart(12, '0')}`
      keys.push(key)
      return key
    },
  })
  return { server, clock, cache, notifications, keys }
}

/** Promise whose settlement the test controls. */
export interface Deferred<T> {
  readonly promise: Promise<T>
  resolve(value: T): void
}

/** Creates a deferred promise. */
export function deferred<T>(): Deferred<T> {
  let resolve: (value: T) => void = () => {}
  const promise = new Promise<T>((settle) => {
    resolve = settle
  })
  return { promise, resolve }
}

/** TreeApi stub for a chain of ids where node i is the parent of node i + 1; ancestors are computed lazily per load. */
export function chainApi(ids: readonly NodeId[], generation = 1): TreeApi & { loads: number } {
  const indexOf = new Map(ids.map((id, index) => [id, index]))
  const api = {
    loads: 0,
    loadNode(id: NodeId): Promise<LoadNodeResult> {
      api.loads++
      const index = indexOf.get(id)
      if (index === undefined) {
        return Promise.resolve({ kind: 'notFound', generation })
      }
      const ancestorIds: NodeId[] = []
      for (let ancestor = index - 1; ancestor >= 0; ancestor--) {
        ancestorIds.push(ids[ancestor] ?? '')
      }
      return Promise.resolve({
        kind: 'found',
        generation,
        node: {
          id,
          parentId: index === 0 ? null : (ids[index - 1] ?? null),
          value: `Level ${index}`,
          version: index + 1,
          isDeleted: false,
          ancestorIds,
        },
      })
    },
    apply(): never {
      throw new Error('not used')
    },
  }
  return api
}
