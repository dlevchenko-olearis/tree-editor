/** Core domain types shared by the cache, the database browser, and the ports. */

/** Lowercase UUIDv7 string identifying a node. */
export type NodeId = string

/** Last known database state of a cached node. */
export interface NodeBase {
  readonly value: string
  readonly version: number
}

/** Node held in the local cache; `base` is null for nodes created locally and not yet applied. */
export interface CachedNode {
  readonly id: NodeId
  readonly parentId: NodeId | null
  value: string
  base: NodeBase | null
}

/** Derived status of a cached node. */
export type NodeStatus = 'unchanged' | 'modified' | 'new' | 'deleted' | 'conflicted'

/** Structural row of a rendered tree: which node and how deep it sits. */
export interface TreeRow {
  readonly id: NodeId
  readonly depth: number
}

/** Single node returned by the server together with its ancestor chain (parent first, root last). */
export interface LoadedNode {
  readonly id: NodeId
  readonly parentId: NodeId | null
  readonly value: string
  readonly version: number
  readonly isDeleted: boolean
  readonly ancestorIds: readonly NodeId[]
}

/** Row of a roots or children page in the database tree. */
export interface DbListItem {
  readonly id: NodeId
  readonly parentId: NodeId | null
  readonly value: string
  readonly isDeleted: boolean
  readonly hasChildren: boolean
}

/** New node sent in a change set. */
export interface CreatedNode {
  readonly id: NodeId
  readonly parentId: NodeId
  readonly value: string
}

/** Value edit sent in a change set. */
export interface UpdatedNode {
  readonly id: NodeId
  readonly value: string
  readonly expectedVersion: number
}

/** Body of an Apply request. */
export interface ChangeSet {
  readonly generation: number
  readonly created: readonly CreatedNode[]
  readonly updated: readonly UpdatedNode[]
  readonly deleted: readonly NodeId[]
}

/** Identifier paired with the version the server assigned to it. */
export interface VersionedId {
  readonly id: NodeId
  readonly version: number
}

/** Successful Apply response. */
export interface AppliedChanges {
  readonly generation: number
  readonly created: readonly VersionedId[]
  readonly updated: readonly VersionedId[]
  readonly deleted: readonly NodeId[]
}

/** Kinds of conflict the server reports for an Apply. */
export type ConflictKind =
  | 'VersionMismatch'
  | 'Deleted'
  | 'NotFound'
  | 'ParentDeleted'
  | 'ParentNotFound'
  | 'AlreadyExists'
  | 'StaleGeneration'

/** Single conflict of a rejected Apply. */
export interface Conflict {
  readonly id: NodeId | null
  readonly kind: ConflictKind
  readonly currentVersion: number | null
  readonly parentId: NodeId | null
}

/** Compares node ids the same way PostgreSQL orders UUIDv7 values. */
export function compareIds(a: NodeId, b: NodeId): number {
  return a < b ? -1 : a > b ? 1 : 0
}
