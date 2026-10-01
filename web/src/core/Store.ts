/** Minimal observable base for stores read through `useSyncExternalStore`. */
export abstract class Store {
  private readonly listeners = new Set<() => void>()

  /** Registers a change listener and returns the function that removes it. */
  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  /** Notifies every listener that the store changed. */
  protected emit(): void {
    for (const listener of [...this.listeners]) {
      listener()
    }
  }
}
