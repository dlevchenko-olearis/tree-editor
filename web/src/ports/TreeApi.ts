/** Port through which the cache reaches the server: loading one node and applying a change set, nothing else. */

import type { AppliedChanges, ChangeSet, Conflict, LoadedNode, NodeId } from '../core/types.ts'

/** Result of loading a single node. */
export type LoadNodeResult =
  | { readonly kind: 'found'; readonly generation: number; readonly node: LoadedNode }
  | { readonly kind: 'notFound'; readonly generation: number | null }
  | { readonly kind: 'failed'; readonly message: string }

/**
 * Final result of an Apply after the adapter's own retries. `notProcessed` means every attempt was turned away
 * before the server handled it, for example by rate limiting, so none of them applied anything; `unknown` means at
 * least one attempt may have been applied without its answer arriving.
 */
export type ApplyResult =
  | { readonly kind: 'applied'; readonly result: AppliedChanges }
  | { readonly kind: 'conflicted'; readonly generation: number; readonly conflicts: readonly Conflict[] }
  | { readonly kind: 'rejected'; readonly status: number; readonly message: string; readonly generation: number | null }
  | { readonly kind: 'notProcessed'; readonly message: string }
  | { readonly kind: 'unknown'; readonly message: string }

/** Server operations available to the cache. */
export interface TreeApi {
  loadNode(id: NodeId): Promise<LoadNodeResult>
  apply(changeSet: ChangeSet, idempotencyKey: string): Promise<ApplyResult>
}
