import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 800 })
Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 600 })

afterEach(() => {
  cleanup()
})
