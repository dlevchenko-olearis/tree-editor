import { screen, waitFor, within } from '@testing-library/react'
import { renderApp } from '../testing/renderApp.tsx'

const ROOT = 0

describe('App shell', () => {
  it('shows the sample roots collapsed next to an empty cache, with the whole toolbar', async () => {
    const { server, dbTree, cacheTree, button } = await renderApp()
    expect(within(dbTree).getAllByRole('treeitem').map((row) => row.textContent)).toEqual([
      expect.stringContaining('Root'),
      expect.stringContaining('Other root'),
    ])
    expect(server.calls.children).toHaveLength(0)
    expect(within(cacheTree).getByText(/cache is empty/i)).toBeInTheDocument()
    for (const name of ['Load', 'Edit', 'Add child', 'Delete', 'Apply', 'Reset', 'Refresh']) {
      expect(button(name)).toBeInTheDocument()
    }
    expect(button('Load')).toBeDisabled()
    expect(button('Apply')).toBeDisabled()
  })

  it('tells the user when another session reset the data', async () => {
    const { server, expandInDb } = await renderApp()
    server.resetDirectly()
    await expandInDb('Root')
    expect(await screen.findByText('The data was reset by another session.')).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Collapse Root' })).not.toBeInTheDocument()
    })
    expect(server.calls.children.map((call) => call.parentId)).toEqual([server.id(ROOT)])
  })

  it('tells the user when a node no longer exists and dismisses the message', async () => {
    const { server, user, dbRow, button, expandInDb, dbTree } = await renderApp()
    await expandInDb('Root')
    await within(dbTree).findByText('A')
    server.removeDirectly(server.id(1))
    await user.click(dbRow('A'))
    await user.click(button('Load'))
    expect(await screen.findByText('The node no longer exists.')).toBeInTheDocument()
    expect(within(dbTree).queryByText('A')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText('The node no longer exists.')).not.toBeInTheDocument()
  })
})
