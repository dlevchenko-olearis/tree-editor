/** Messages shown to the user in the notification area. */

import { Store } from './Store.ts'

/** Categories of user notifications. */
export type NotificationKind = 'noLongerExists' | 'dataReset' | 'outcomeUnknown' | 'error'

/** One notification. */
export interface Notification {
  readonly id: number
  readonly kind: NotificationKind
  readonly message: string
}

/** Sink components use to report something to the user. */
export type Notify = (kind: NotificationKind, message: string) => void

const MAX_NOTIFICATIONS = 5

/** Bounded list of the most recent notifications. */
export class Notifications extends Store {
  private items: readonly Notification[] = []
  private nextId = 1

  /** Adds a notification, dropping the oldest when the list is full. */
  readonly push: Notify = (kind, message) => {
    const notification: Notification = { id: this.nextId++, kind, message }
    this.items = [...this.items, notification].slice(-MAX_NOTIFICATIONS)
    this.emit()
  }

  /** Removes one notification. */
  dismiss(id: number): void {
    this.items = this.items.filter((item) => item.id !== id)
    this.emit()
  }

  /** Removes every notification of one kind, for example when the situation it reported is over. */
  dismissKind(kind: NotificationKind): void {
    if (this.items.some((item) => item.kind === kind)) {
      this.items = this.items.filter((item) => item.kind !== kind)
      this.emit()
    }
  }

  /** Current notifications, oldest first; the array changes only when the list does. */
  getSnapshot(): readonly Notification[] {
    return this.items
  }
}
