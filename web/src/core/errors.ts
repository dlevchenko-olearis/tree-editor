/** Error helpers shared by the core. */

/** Turns anything a promise rejected with into a message for the user. */
export function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
