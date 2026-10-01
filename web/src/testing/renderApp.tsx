/** Renders the whole application over a fake server for component tests. */

import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TreeSession } from '../core/TreeSession.ts'
import { App } from '../ui/App.tsx'
import { FakeTreeServer, createTestIds, sampleSeed } from './FakeTreeServer.ts'
import type { SeedNode } from './FakeTreeServer.ts'

/** Renders the app and waits for the database roots. */
export async function renderApp(seed: readonly SeedNode[] = sampleSeed(), pageSize = 20) {
  const server = new FakeTreeServer(seed)
  const session = new TreeSession({
    treeApi: server,
    dbTreeApi: server,
    resetApi: server,
    pageSize,
    createId: createTestIds(1_800_000_000_000),
  })
  const user = userEvent.setup()
  const view = render(<App session={session} />)
  const dbTree = screen.getByRole('tree', { name: 'Database tree' })
  const cacheTree = screen.getByRole('tree', { name: 'Cached tree' })
  const firstSeed = seed[0]
  if (firstSeed !== undefined) {
    await within(dbTree).findByText(firstSeed.value)
  }
  const rowIn = (tree: HTMLElement, text: string): HTMLElement => {
    const row = within(tree).getByText(text, { selector: '.value' }).closest<HTMLElement>('[role="treeitem"]')
    if (row === null) {
      throw new Error(`No row ${text}`)
    }
    return row
  }
  const button = (name: string) => screen.getByRole('button', { name })
  return {
    server,
    session,
    user,
    view,
    dbTree,
    cacheTree,
    dbRow: (text: string) => rowIn(dbTree, text),
    cacheRow: (text: string) => rowIn(cacheTree, text),
    button,
    expandInDb: async (text: string) => {
      await user.click(within(dbTree).getByRole('button', { name: `Expand ${text}` }))
    },
    loadIntoCache: async (text: string) => {
      await user.click(rowIn(dbTree, text))
      await user.click(button('Load'))
      await within(cacheTree).findByText(text, { selector: '.value' })
    },
  }
}
