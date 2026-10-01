import { screen, waitFor, within } from '@testing-library/react'
import { renderApp } from '../testing/renderApp.tsx'
import type { SeedNode } from '../testing/FakeTreeServer.ts'

function wideSeed(): SeedNode[] {
  return [
    { value: 'Root', parent: null },
    ...Array.from({ length: 25 }, (_, index) => ({ value: `Child ${String(index + 1).padStart(2, '0')}`, parent: 0 })),
    { value: 'Grandchild', parent: 1 },
  ]
}

describe('DbTreeView', () => {
  it('expands a node, shows leaves without an expander, and pages with "load more"', async () => {
    const { user, dbTree, expandInDb } = await renderApp(wideSeed())
    await expandInDb('Root')
    await within(dbTree).findByText('Child 20')
    expect(within(dbTree).queryByText('Child 21')).not.toBeInTheDocument()
    expect(within(dbTree).getByRole('button', { name: 'Expand Child 01' })).toBeInTheDocument()
    expect(within(dbTree).queryByRole('button', { name: 'Expand Child 02' })).not.toBeInTheDocument()
    await user.click(within(dbTree).getByRole('button', { name: 'Load more' }))
    await within(dbTree).findByText('Child 25')
    const values = within(dbTree)
      .getAllByRole('treeitem')
      .map((row) => row.querySelector('.value')?.textContent)
    expect(values).toEqual(['Root', ...Array.from({ length: 25 }, (_, index) => `Child ${String(index + 1).padStart(2, '0')}`)])
    expect(within(dbTree).queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument()
  })

  it('marks deleted nodes and disables Load for them', async () => {
    const { server, user, dbTree, dbRow, button, expandInDb } = await renderApp()
    server.deleteDirectly(server.id(1))
    await expandInDb('Root')
    await within(dbTree).findByText('A')
    expect(dbRow('A')).toHaveClass('is-deleted')
    expect(within(dbRow('A')).getByText('deleted')).toBeInTheDocument()
    await user.click(dbRow('A'))
    expect(button('Load')).toBeDisabled()
    await user.click(dbRow('E'))
    expect(button('Load')).toBeEnabled()
  })

  it('loads the selected node, and only that node, into the cache', async () => {
    const { server, cacheTree, expandInDb, loadIntoCache, dbTree } = await renderApp()
    await expandInDb('Root')
    await within(dbTree).findByText('A')
    await loadIntoCache('A')
    expect(within(cacheTree).getAllByRole('treeitem')).toHaveLength(1)
    expect(server.calls.loadNode).toEqual([server.id(1)])
  })

  it('shows children added by another session after Refresh', async () => {
    const { server, user, dbTree, button, expandInDb } = await renderApp()
    await expandInDb('Root')
    await within(dbTree).findByText('A')
    server.addDirectly(server.id(0), 'ffffffff-0000-7000-8000-000000000000', 'Added elsewhere')
    expect(within(dbTree).queryByText('Added elsewhere')).not.toBeInTheDocument()
    await user.click(button('Refresh'))
    expect(await within(dbTree).findByText('Added elsewhere')).toBeInTheDocument()
  })

  it('collapses a node again', async () => {
    const { user, dbTree, expandInDb } = await renderApp()
    await expandInDb('Root')
    await within(dbTree).findByText('A')
    await user.click(within(dbTree).getByRole('button', { name: 'Collapse Root' }))
    await waitFor(() => {
      expect(within(dbTree).queryByText('A')).not.toBeInTheDocument()
    })
    expect(screen.getByRole('button', { name: 'Expand Root' })).toBeInTheDocument()
  })
})
