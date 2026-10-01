/** Notification area for "no longer exists", "data was reset", "outcome unknown", and errors. */

import type { Notifications } from '../core/Notifications.ts'
import { useNotifications } from './hooks.ts'

/** Props of the notification area. */
export interface NotificationAreaProps {
  readonly notifications: Notifications
}

/** Lists the most recent notifications, each dismissible. */
export function NotificationArea({ notifications }: NotificationAreaProps) {
  const items = useNotifications(notifications)
  return (
    <div className="notifications" role="status" aria-live="polite">
      {items.map((item) => (
        <div key={item.id} className={`notification notification-${item.kind}`}>
          <span>{item.message}</span>
          <button
            type="button"
            className="link-button"
            aria-label="Dismiss"
            onClick={() => {
              notifications.dismiss(item.id)
            }}
          >
            Dismiss
          </button>
        </div>
      ))}
    </div>
  )
}
