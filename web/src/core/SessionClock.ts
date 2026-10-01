/** Tree generation and request epoch shared by every component of one browser session. */

/** Why a session-wide reset happened: this session's own Reset, or a newer generation seen in a response. */
export type ResetReason = 'local' | 'remote'

/** Decides which responses may still change the session, and runs the Reset handling when the tree was reset. */
export class SessionClock {
  private currentGeneration: number | null = null
  private currentEpoch = 0
  private readonly listeners = new Set<(reason: ResetReason) => void>()

  /** Last tree generation this session knows, or null before the first response. */
  get generation(): number | null {
    return this.currentGeneration
  }

  /** Counter that changes on every reset; requests remember it and their responses are dropped once it moves on. */
  get epoch(): number {
    return this.currentEpoch
  }

  /** Registers a Reset handler and returns the function that removes it. */
  onReset(listener: (reason: ResetReason) => void): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  /**
   * Tells whether a response to a request sent in `epoch` may be applied. Responses to requests from an earlier
   * epoch are dropped. Within one epoch every generation change goes through a reset, so any other generation means
   * the tree was reset elsewhere, or the database was recreated when it is older: the Reset handling runs and the
   * response is dropped.
   */
  accept(epoch: number, generation: number | null): boolean {
    if (epoch !== this.currentEpoch) {
      return false
    }
    if (generation === null) {
      return true
    }
    if (this.currentGeneration === null || generation === this.currentGeneration) {
      this.currentGeneration = generation
      return true
    }
    this.reset(generation, 'remote')
    return false
  }

  /**
   * Adopts the generation of this session's own Reset, sent in `epoch`. When a generation at least as new was
   * adopted while that Reset was in flight, the session is already clear, and going back would reset it again.
   */
  resetLocally(epoch: number, generation: number): void {
    const newerSeen = epoch !== this.currentEpoch && this.currentGeneration !== null && this.currentGeneration >= generation
    if (!newerSeen) {
      this.reset(generation, 'local')
    }
  }

  /** Adopts a new generation, invalidates requests in flight, and runs every Reset handler. */
  reset(generation: number, reason: ResetReason): void {
    this.currentEpoch++
    this.currentGeneration = generation
    for (const listener of [...this.listeners]) {
      listener(reason)
    }
  }
}
