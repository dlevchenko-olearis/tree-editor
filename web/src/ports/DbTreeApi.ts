/** Read-only port through which the database tree browser pages through roots and children. */

import type { DbListItem, NodeId } from '../core/types.ts'

/** Result of fetching one page of a level. */
export type PageResult =
  | {
      readonly kind: 'page'
      readonly generation: number
      readonly items: readonly DbListItem[]
      readonly nextAfter: NodeId | null
    }
  | { readonly kind: 'notFound'; readonly generation: number | null }
  | { readonly kind: 'failed'; readonly message: string }

/** Largest page the server accepts. */
export const MAX_PAGE_SIZE = 200

/** Paged, read-only access to the database tree. */
export interface DbTreeApi {
  roots(after: NodeId | null, limit: number): Promise<PageResult>
  children(parentId: NodeId, after: NodeId | null, limit: number): Promise<PageResult>
}
