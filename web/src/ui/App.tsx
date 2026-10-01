/** Application shell: toolbar, notifications, conflicts, and the two tree views side by side. */

import { useCallback, useEffect, useState } from 'react'
import type { TreeSession } from '../core/TreeSession.ts'
import type { NodeId } from '../core/types.ts'
import { AddChildPrompt } from './AddChildPrompt.tsx'
import { CachedTreeView } from './CachedTreeView.tsx'
import { ConflictList } from './ConflictList.tsx'
import { DbTreeView } from './DbTreeView.tsx'
import { NotificationArea } from './NotificationArea.tsx'
import { Toolbar } from './Toolbar.tsx'
import './app.css'

/** Props of the application shell. */
export interface AppProps {
  readonly session: TreeSession
}

/** Root component; all state lives in the session, only selection and open editors live in React. */
export function App({ session }: AppProps) {
  const [selectedDbId, setSelectedDbId] = useState<NodeId | null>(null)
  const [selectedCacheId, setSelectedCacheId] = useState<NodeId | null>(null)
  const [editingId, setEditingId] = useState<NodeId | null>(null)
  const [addingParentId, setAddingParentId] = useState<NodeId | null>(null)

  useEffect(() => {
    void session.start()
  }, [session])

  useEffect(
    () =>
      session.clock.onReset(() => {
        setSelectedDbId(null)
        setSelectedCacheId(null)
        setEditingId(null)
        setAddingParentId(null)
      }),
    [session],
  )

  const endEdit = useCallback(() => {
    setEditingId(null)
  }, [])

  return (
    <div className="app">
      <header className="app-header">
        <h1>Tree Editor</h1>
        <Toolbar
          session={session}
          selectedDbId={selectedDbId}
          selectedCacheId={selectedCacheId}
          onEdit={setEditingId}
          onAddChild={setAddingParentId}
        />
      </header>
      <NotificationArea notifications={session.notifications} />
      <ConflictList session={session} />
      {addingParentId !== null && (
        <AddChildPrompt
          key={addingParentId}
          cache={session.cache}
          parentId={addingParentId}
          onDone={(createdId) => {
            setAddingParentId(null)
            if (createdId !== null) {
              setSelectedCacheId(createdId)
            }
          }}
        />
      )}
      <main className="panels">
        <section className="panel" aria-labelledby="db-heading">
          <h2 id="db-heading">Database</h2>
          <DbTreeView browser={session.browser} selectedId={selectedDbId} onSelect={setSelectedDbId} />
        </section>
        <section className="panel" aria-labelledby="cache-heading">
          <h2 id="cache-heading">Cache</h2>
          <CachedTreeView
            cache={session.cache}
            selectedId={selectedCacheId}
            editingId={editingId}
            onSelect={setSelectedCacheId}
            onEditStart={setEditingId}
            onEditEnd={endEdit}
          />
        </section>
      </main>
    </div>
  )
}
