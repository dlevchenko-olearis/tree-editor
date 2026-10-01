import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { renderApp } from '../testing/renderApp.tsx'
import { ROW_HEIGHT } from './treeRowLayout.ts'

async function appWithCachedA() {
  const app = await renderApp()
  await app.expandInDb('Root')
  await within(app.dbTree).findByText('A')
  await app.loadIntoCache('A')
  return app
}

function badgeOf(row: HTMLElement): string | null | undefined {
  return row.querySelector('.badge')?.textContent
}

describe('CachedTreeView', () => {
  it('shows a status badge for every state', async () => {
    const { server, user, cacheRow, button, loadIntoCache, cacheTree } = await appWithCachedA()
    await loadIntoCache('E')
    expect(badgeOf(cacheRow('A'))).toBe('unchanged')

    await user.dblClick(cacheRow('A'))
    const editor = within(cacheTree).getByRole('textbox', { name: 'Edit value' })
    await user.clear(editor)
    await user.type(editor, 'A edited{Enter}')
    expect(badgeOf(cacheRow('A edited'))).toBe('modified')

    await user.click(button('Add child'))
    await user.type(screen.getByRole('textbox', { name: 'Child value' }), 'Fresh{Enter}')
    expect(badgeOf(await waitFor(() => cacheRow('Fresh')))).toBe('new')

    server.editDirectly(server.id(5), 'E theirs')
    await user.dblClick(cacheRow('E'))
    await user.type(within(cacheTree).getByRole('textbox', { name: 'Edit value' }), ' mine{Enter}')
    await user.click(button('Apply'))
    await waitFor(() => {
      expect(badgeOf(cacheRow('E mine'))).toBe('conflict')
    })

    await user.click(cacheRow('A edited'))
    await user.click(button('Delete'))
    expect(badgeOf(cacheRow('A'))).toBe('deleted')
    expect(cacheRow('A')).toHaveClass('is-deleted')
  })

  it('does not recompute the row list while typing or committing an edit', async () => {
    const { session, user, cacheRow, cacheTree } = await appWithCachedA()
    const rows = session.cache.getRows()
    const computeRows = vi.spyOn(session.cache, 'computeRows')
    await user.dblClick(cacheRow('A'))
    const editor = within(cacheTree).getByRole('textbox', { name: 'Edit value' })
    await user.type(editor, ' and more text')
    expect(session.cache.getNodeView(session.cache.getRows()[0]?.id ?? '')?.value).toBe('A')
    await user.keyboard('{Enter}')
    expect(cacheRow('A and more text')).toBeInTheDocument()
    expect(computeRows).not.toHaveBeenCalled()
    expect(session.cache.getRows()).toBe(rows)
  })

  it('keeps the editor open with a validation message for an invalid value', async () => {
    const { user, cacheRow, cacheTree } = await appWithCachedA()
    await user.dblClick(cacheRow('A'))
    const editor = within(cacheTree).getByRole('textbox', { name: 'Edit value' })
    await user.clear(editor)
    await user.type(editor, '   {Enter}')
    expect(within(cacheTree).getByRole('alert')).toHaveTextContent('The value must not be empty.')
    expect(editor).toHaveAttribute('aria-invalid', 'true')
    await user.keyboard('{Escape}')
    expect(cacheRow('A')).toBeInTheDocument()
    expect(badgeOf(cacheRow('A'))).toBe('unchanged')
  })

  it('keeps the editor and its text while the edited row is scrolled out of view', async () => {
    const { session, server, user, cacheRow, cacheTree } = await appWithCachedA()
    act(() => {
      for (let index = 0; index < 100; index++) {
        session.cache.addChild(server.id(1), `Child ${String(index).padStart(3, '0')}`)
      }
    })
    await user.dblClick(cacheRow('A'))
    await user.type(within(cacheTree).getByRole('textbox', { name: 'Edit value' }), ' kept')
    fireEvent.scroll(cacheTree, { target: { scrollTop: 100 * ROW_HEIGHT } })
    await waitFor(() => {
      expect(within(cacheTree).queryByText('Child 000')).not.toBeInTheDocument()
    })
    const editor = within(cacheTree).getByRole('textbox', { name: 'Edit value' })
    expect(editor).toHaveValue('A kept')
    await user.type(editor, '{Enter}')
    expect(session.cache.getNodeView(server.id(1))?.value).toBe('A kept')
  })

  it('trims the value and commits on blur', async () => {
    const { user, cacheRow, cacheTree } = await appWithCachedA()
    await user.dblClick(cacheRow('A'))
    const editor = within(cacheTree).getByRole('textbox', { name: 'Edit value' })
    await user.clear(editor)
    await user.type(editor, '  Trimmed  ')
    fireEvent.blur(editor)
    expect(cacheRow('Trimmed')).toBeInTheDocument()
    expect(within(cacheTree).queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('asks for the value of a new child and rejects an empty one', async () => {
    const { user, button, cacheRow } = await appWithCachedA()
    await user.click(cacheRow('A'))
    await user.click(button('Add child'))
    const prompt = screen.getByRole('form', { name: 'Add child' })
    await user.click(within(prompt).getByRole('button', { name: 'Add' }))
    expect(within(prompt).getByRole('alert')).toHaveTextContent('The value must not be empty.')
    await user.type(within(prompt).getByRole('textbox', { name: 'Child value' }), 'Child one{Enter}')
    expect(screen.queryByRole('form', { name: 'Add child' })).not.toBeInTheDocument()
    expect(cacheRow('Child one')).toHaveAttribute('aria-level', '2')
    expect(cacheRow('Child one')).toHaveAttribute('aria-selected', 'true')
  })

  it('disables edit, add child, and delete for deleted nodes', async () => {
    const { user, button, cacheRow } = await appWithCachedA()
    await user.click(cacheRow('A'))
    expect(button('Edit')).toBeEnabled()
    await user.click(button('Delete'))
    expect(button('Edit')).toBeDisabled()
    expect(button('Add child')).toBeDisabled()
    expect(button('Delete')).toBeDisabled()
    await user.dblClick(cacheRow('A'))
    expect(screen.queryByRole('textbox', { name: 'Edit value' })).not.toBeInTheDocument()
  })

  it('expands and collapses cached children without contacting the server', async () => {
    const { server, user, cacheTree, loadIntoCache, expandInDb, dbTree } = await appWithCachedA()
    await expandInDb('A')
    await within(dbTree).findByText('B')
    await loadIntoCache('B')
    const calls = server.calls.loadNode.length + server.calls.children.length
    await user.click(within(cacheTree).getByRole('button', { name: 'Collapse A' }))
    expect(within(cacheTree).queryByText('B')).not.toBeInTheDocument()
    await user.click(within(cacheTree).getByRole('button', { name: 'Expand A' }))
    expect(within(cacheTree).getByText('B')).toBeInTheDocument()
    expect(server.calls.loadNode.length + server.calls.children.length).toBe(calls)
  })
})
