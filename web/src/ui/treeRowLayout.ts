/** Layout constants and helpers shared by both virtualized tree views. */

/** Fixed height of one tree row in pixels. */
export const ROW_HEIGHT = 28

/** Number of rows rendered beyond the visible area. */
export const OVERSCAN = 12

const INDENT_PX = 16
const MAX_INDENT_LEVELS = 40

/** Glyph of an expanded node's toggle. */
export const EXPANDED_GLYPH = String.fromCodePoint(0x25be)

/** Glyph of a collapsed node's toggle. */
export const COLLAPSED_GLYPH = String.fromCodePoint(0x25b8)

/** Left padding of a row; deep rows stop indenting and show their level instead. */
export function indentOf(depth: number): number {
  return Math.min(depth, MAX_INDENT_LEVELS) * INDENT_PX
}

/** Tells whether a row is deeper than the indentation can show. */
export function showsLevel(depth: number): boolean {
  return depth > MAX_INDENT_LEVELS
}
