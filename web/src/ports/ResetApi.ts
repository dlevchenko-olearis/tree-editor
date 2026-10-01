/** Port for restoring the database to the sample data. */

/** Result of a Reset request. */
export type ResetResult =
  | { readonly kind: 'reset'; readonly generation: number }
  | { readonly kind: 'failed'; readonly message: string }

/** Server operation that resets the tree. */
export interface ResetApi {
  reset(): Promise<ResetResult>
}
