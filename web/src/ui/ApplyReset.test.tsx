import { screen, waitFor, within } from '@testing-library/react'
import { renderApp } from '../testing/renderApp.tsx'

async function appWithCachedNodes() {
  const app = await renderApp()
  await app.expandInDb('Root')
  await within(app.dbTree).findByText('A')
  await app.loadIntoCache('A')
  await app.loadIntoCache('E')
  return app
}

async function editInCache(app: Awaited<ReturnType<typeof appWithCachedNodes>>, from: string, to: string) {
  await app.user.dblClick(app.cacheRow(from))
  const editor = within(app.cacheTree).getByRole('textbox', { name: 'Edit value' })
  await app.user.clear(editor)
  await app.user.type(editor, `${to}{Enter}`)
}

describe('Apply and Reset', () => {
  it('enables Apply only when there is something to send', async () => {
    const app = await appWithCachedNodes()
    expect(app.button('Apply')).toBeDisabled()
    await editInCache(app, 'A', 'A2')
    expect(app.button('Apply')).toBeEnabled()
    await editInCache(app, 'A2', 'A')
    expect(app.button('Apply')).toBeDisabled()
  })

  it('keeps editing available during an Apply and disables Apply and Reset until it finishes', async () => {
    const app = await appWithCachedNodes()
    await editInCache(app, 'A', 'A2')
    const release = app.server.hold()
    await app.user.click(app.button('Apply'))
    expect(await screen.findByRole('button', { name: 'Applying...' })).toBeDisabled()
    expect(app.button('Reset')).toBeDisabled()
    await editInCache(app, 'E', 'E2')
    expect(app.cacheRow('E2')).toBeInTheDocument()
    release()
    await waitFor(() => {
      expect(app.button('Apply')).toBeEnabled()
    })
    expect(app.button('Reset')).toBeEnabled()
    expect(app.server.row(app.server.id(1))?.value).toBe('A2')
    expect(app.cacheRow('A2').querySelector('.badge')?.textContent).toBe('unchanged')
    expect(app.cacheRow('E2').querySelector('.badge')?.textContent).toBe('modified')
  })

  it('lists conflicts and resolves one through Reload', async () => {
    const app = await appWithCachedNodes()
    await editInCache(app, 'A', 'Mine')
    app.server.editDirectly(app.server.id(1), 'Theirs')
    await app.user.click(app.button('Apply'))
    const conflicts = await screen.findByRole('region', { name: 'Conflicts' })
    expect(conflicts).toHaveTextContent('Mine was changed by another session.')
    await app.user.click(within(conflicts).getByRole('button', { name: 'Reload' }))
    await waitFor(() => {
      expect(screen.queryByRole('region', { name: 'Conflicts' })).not.toBeInTheDocument()
    })
    expect(app.cacheRow('Mine').querySelector('.badge')?.textContent).toBe('modified')
    await app.user.click(app.button('Apply'))
    await waitFor(() => {
      expect(app.server.row(app.server.id(1))?.value).toBe('Mine')
    })
  })

  it('shows the result of an Apply in the expanded database levels', async () => {
    const app = await appWithCachedNodes()
    await editInCache(app, 'A', 'A renamed')
    await app.user.click(app.cacheRow('E'))
    await app.user.click(app.button('Delete'))
    await app.user.click(app.button('Apply'))
    expect(await within(app.dbTree).findByText('A renamed')).toBeInTheDocument()
    await waitFor(() => {
      expect(app.dbRow('E')).toHaveClass('is-deleted')
    })
  })

  it('clears the cache and collapses the database tree on Reset', async () => {
    const app = await appWithCachedNodes()
    await editInCache(app, 'A', 'Pending')
    await app.user.click(app.button('Reset'))
    await waitFor(() => {
      expect(within(app.cacheTree).getByText(/cache is empty/i)).toBeInTheDocument()
    })
    await waitFor(() => {
      expect(within(app.dbTree).getAllByRole('treeitem')).toHaveLength(2)
    })
    expect(within(app.dbTree).queryByText('A')).not.toBeInTheDocument()
    expect(app.button('Apply')).toBeDisabled()
    expect(app.server.row(app.server.id(1))?.value).toBe('A')
    expect(screen.queryByText('The data was reset by another session.')).not.toBeInTheDocument()
  })

  it('tells the user when the outcome of an Apply is unknown and lets them discard it', async () => {
    const app = await appWithCachedNodes()
    expect(screen.queryByRole('button', { name: 'Discard unconfirmed' })).not.toBeInTheDocument()
    await editInCache(app, 'A', 'A2')
    app.server.answerNextApply({ kind: 'unknown', message: 'lost' })
    await app.user.click(app.button('Apply'))
    expect(await screen.findByText(/The outcome of the last Apply is unknown\./)).toBeInTheDocument()
    expect(app.button('Apply')).toBeEnabled()

    await app.user.click(app.button('Discard unconfirmed'))
    expect(screen.queryByText(/The outcome of the last Apply is unknown\./)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Discard unconfirmed' })).not.toBeInTheDocument()
    expect(app.button('Apply')).toBeEnabled()
    expect(app.cacheRow('A2').querySelector('.badge')?.textContent).toBe('modified')
  })
})
