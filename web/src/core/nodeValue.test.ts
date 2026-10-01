import { MAX_VALUE_LENGTH, prepareValue, sharedWhitespaceCodePoints, trimValue, validateValue } from './nodeValue.ts'

const char = (codePoint: number) => String.fromCharCode(codePoint)

describe('shared whitespace set', () => {
  it('contains exactly the characters shared with the server', () => {
    const expected = [
      0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, 0x85, 0xa0, 0x1680,
      ...Array.from({ length: 11 }, (_, index) => 0x2000 + index),
      0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff,
    ]
    expect([...sharedWhitespaceCodePoints]).toEqual(expected)
  })

  it.each(sharedWhitespaceCodePoints.map((codePoint) => [codePoint.toString(16), codePoint]))(
    'trims U+%s from both edges',
    (_, codePoint) => {
      const space = char(codePoint)
      expect(trimValue(`${space}${space}abc${space}`)).toBe('abc')
      expect(validateValue(`${space}abc`)).toBe('surroundingWhitespace')
      expect(validateValue(`abc${space}`)).toBe('surroundingWhitespace')
    },
  )

  it('trims U+0085, which String.prototype.trim keeps', () => {
    const nextLine = char(0x85)
    expect(`${nextLine}abc`.trim()).toBe(`${nextLine}abc`)
    expect(trimValue(`${nextLine}abc`)).toBe('abc')
  })

  it('trims U+FEFF, which the .NET Trim keeps', () => {
    expect(trimValue(`${char(0xfeff)}abc${char(0xfeff)}`)).toBe('abc')
  })

  it('keeps inner whitespace and characters outside the set', () => {
    expect(trimValue(' a b ')).toBe('a b')
    expect(trimValue(`${char(0x200b)}abc`)).toBe(`${char(0x200b)}abc`)
    expect(validateValue(`a${char(0x3000)}b`)).toBeNull()
  })
})

describe('value validation', () => {
  it('stages "  abc  " as "abc"', () => {
    expect(prepareValue('  abc  ')).toEqual({ ok: true, value: 'abc' })
  })

  it('rejects an empty value and a value that is empty after trimming', () => {
    expect(prepareValue('')).toMatchObject({ ok: false, error: 'empty' })
    expect(prepareValue(`  ${char(0x3000)} `)).toMatchObject({ ok: false, error: 'empty' })
  })

  it('accepts 200 UTF-16 code units and rejects 201', () => {
    expect(validateValue('x'.repeat(MAX_VALUE_LENGTH))).toBeNull()
    expect(validateValue('x'.repeat(MAX_VALUE_LENGTH + 1))).toBe('tooLong')
    expect(prepareValue('x'.repeat(201))).toMatchObject({ ok: false, error: 'tooLong' })
  })

  it('counts a surrogate pair as two code units', () => {
    const emoji = String.fromCodePoint(0x1f600)
    expect(validateValue(emoji.repeat(100))).toBeNull()
    expect(validateValue(`${emoji.repeat(100)}x`)).toBe('tooLong')
  })

  it('rejects the NUL character anywhere, as PostgreSQL text cannot store it', () => {
    expect(validateValue(`a${char(0)}b`)).toBe('nullCharacter')
    expect(validateValue(`${char(0)}ab`)).toBe('nullCharacter')
    expect(prepareValue(`ab${char(0)}`)).toMatchObject({ ok: false, error: 'nullCharacter' })
  })

  it('never transforms a valid value', () => {
    expect(validateValue(' abc')).toBe('surroundingWhitespace')
    expect(prepareValue('abc')).toEqual({ ok: true, value: 'abc' })
  })
})
