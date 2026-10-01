/** Conflicts reported by the last Apply, each with a Reload action. */

import type { TreeSession } from '../core/TreeSession.ts'
import type { Conflict, ConflictKind } from '../core/types.ts'
import { useCacheNode, useSessionState } from './hooks.ts'

const DESCRIPTIONS: Readonly<Record<ConflictKind, string>> = {
  VersionMismatch: 'was changed by another session',
  Deleted: 'was deleted by another session',
  NotFound: 'no longer exists',
  ParentDeleted: 'cannot be created because its parent was deleted',
  ParentNotFound: 'cannot be created because its parent no longer exists',
  AlreadyExists: 'already exists',
  StaleGeneration: 'belongs to data that was reset',
}

/** Props of the conflict list. */
export interface ConflictListProps {
  readonly session: TreeSession
}

/** Shows the conflicts of the last Apply; reloading a conflicting node rebases its pending value. */
export function ConflictList({ session }: ConflictListProps) {
  const { conflicts } = useSessionState(session)
  if (conflicts.length === 0) {
    return null
  }
  return (
    <section className="conflicts" aria-label="Conflicts">
      <h2>Apply failed. Nothing was saved.</h2>
      <ul>
        {conflicts.map((conflict, index) => (
          <ConflictItem key={`${conflict.id ?? 'set'}:${conflict.kind}:${index}`} session={session} conflict={conflict} />
        ))}
      </ul>
    </section>
  )
}

interface ConflictItemProps {
  readonly session: TreeSession
  readonly conflict: Conflict
}

function ConflictItem({ session, conflict }: ConflictItemProps) {
  const node = useCacheNode(session.cache, conflict.id)
  const label = node?.value ?? conflict.id ?? 'The change set'
  const target = conflict.kind === 'ParentDeleted' || conflict.kind === 'ParentNotFound' ? conflict.parentId : conflict.id
  return (
    <li>
      <span>
        <strong>{label}</strong> {DESCRIPTIONS[conflict.kind]}.
      </span>
      {target !== null && (
        <button
          type="button"
          onClick={() => {
            void session.reloadConflict(conflict)
          }}
        >
          Reload
        </button>
      )}
    </li>
  )
}
