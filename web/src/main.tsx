import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HttpTreeApi } from './adapters/HttpTreeApi.ts'
import { TreeSession } from './core/TreeSession.ts'
import { App } from './ui/App.tsx'

const container = document.getElementById('root')
if (container === null) {
  throw new Error('Missing #root element')
}

const api = new HttpTreeApi()
const session = new TreeSession({ treeApi: api, dbTreeApi: api, resetApi: api })

createRoot(container).render(
  <StrictMode>
    <App session={session} />
  </StrictMode>,
)
