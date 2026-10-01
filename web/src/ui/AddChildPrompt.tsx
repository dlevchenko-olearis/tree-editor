/** Prompt for the value of a new child, validated like an edit before the child is created. */

import { useState } from 'react'
import type { TreeCache } from '../core/TreeCache.ts'
import type { NodeId } from '../core/types.ts'
import { useCacheNode } from './hooks.ts'

/** Props of the add-child prompt. */
export interface AddChildPromptProps {
  readonly cache: TreeCache
  readonly parentId: NodeId
  readonly onDone: (createdId: NodeId | null) => void
}

/** Asks for the child's value and creates the child only when the value is valid. */
export function AddChildPrompt({ cache, parentId, onDone }: AddChildPromptProps) {
  const parent = useCacheNode(cache, parentId)
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <form
      className="prompt"
      aria-label="Add child"
      onSubmit={(event) => {
        event.preventDefault()
        const result = cache.addChild(parentId, text)
        if (result.ok) {
          onDone(result.id)
        } else {
          setError(result.message)
        }
      }}
    >
      <label>
        New child of <strong>{parent?.value ?? ''}</strong>
        <input
          aria-label="Child value"
          aria-invalid={error !== null}
          autoFocus
          value={text}
          onChange={(event) => {
            setText(event.target.value)
            setError(null)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              onDone(null)
            }
          }}
        />
      </label>
      <button type="submit" className="primary">
        Add
      </button>
      <button
        type="button"
        onClick={() => {
          onDone(null)
        }}
      >
        Cancel
      </button>
      {error !== null && (
        <span role="alert" className="validation">
          {error}
        </span>
      )}
    </form>
  )
}
