/** Node value rules shared with the server's `NodeValue`: the edge whitespace set, trimming, and validation. */

/** Maximum value length in UTF-16 code units. */
export const MAX_VALUE_LENGTH = 200

const SHARED_WHITESPACE_CODE_POINTS: readonly number[] = [
  0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0020, 0x0085, 0x00a0, 0x1680,
  0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a,
  0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff,
]

const sharedWhitespace: ReadonlySet<number> = new Set(SHARED_WHITESPACE_CODE_POINTS)

const NULL_CHARACTER = String.fromCharCode(0)

/** Every code point of the shared whitespace set, in ascending order. */
export const sharedWhitespaceCodePoints = SHARED_WHITESPACE_CODE_POINTS

/** Reasons a value is rejected. */
export type ValueError = 'empty' | 'tooLong' | 'surroundingWhitespace' | 'nullCharacter'

/** Result of preparing user input for staging. */
export type PreparedValue =
  | { readonly ok: true; readonly value: string }
  | { readonly ok: false; readonly error: ValueError; readonly message: string }

/** Tells whether a UTF-16 code unit belongs to the shared whitespace set. */
export function isSharedWhitespace(codeUnit: number): boolean {
  return sharedWhitespace.has(codeUnit)
}

/** Removes leading and trailing characters of the shared whitespace set. */
export function trimValue(raw: string): string {
  let start = 0
  let end = raw.length
  while (start < end && isSharedWhitespace(raw.charCodeAt(start))) {
    start++
  }
  while (end > start && isSharedWhitespace(raw.charCodeAt(end - 1))) {
    end--
  }
  return raw.slice(start, end)
}

/** Validates a value without transforming it, mirroring the server rules. */
export function validateValue(value: string): ValueError | null {
  if (value.length === 0) {
    return 'empty'
  }
  if (value.length > MAX_VALUE_LENGTH) {
    return 'tooLong'
  }
  if (isSharedWhitespace(value.charCodeAt(0)) || isSharedWhitespace(value.charCodeAt(value.length - 1))) {
    return 'surroundingWhitespace'
  }
  if (value.includes(NULL_CHARACTER)) {
    return 'nullCharacter'
  }
  return null
}

/** Trims user input and validates the result. */
export function prepareValue(raw: string): PreparedValue {
  const value = trimValue(raw)
  const error = validateValue(value)
  return error === null ? { ok: true, value } : { ok: false, error, message: describeValueError(error) }
}

/** Human-readable validation message. */
export function describeValueError(error: ValueError): string {
  switch (error) {
    case 'empty':
      return 'The value must not be empty.'
    case 'tooLong':
      return `The value must be at most ${MAX_VALUE_LENGTH} characters long.`
    case 'surroundingWhitespace':
      return 'The value must not start or end with whitespace.'
    case 'nullCharacter':
      return 'The value must not contain the NUL character (U+0000).'
  }
}
