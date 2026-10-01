/** UUIDv7 generation and validation for client-side node identifiers. */

const UUID_V7_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

const COUNTER_MAX = 0xfff
const COUNTER_SEED_MAX = 0x7ff

/** Produces lowercase UUIDv7 strings that increase monotonically, even within one millisecond. */
export type UuidV7Generator = () => string

/** Creates a UUIDv7 generator; the clock and the random source can be replaced in tests. */
export function createUuidV7Generator(
  now: () => number = () => Date.now(),
  fillRandom: (bytes: Uint8Array<ArrayBuffer>) => void = (bytes) => {
    crypto.getRandomValues(bytes)
  },
): UuidV7Generator {
  let lastMillis = -1
  let counter = 0
  const random = new Uint8Array(10)

  return () => {
    fillRandom(random)
    const current = Math.floor(now())
    if (current > lastMillis) {
      lastMillis = current
      counter = (((random[0] ?? 0) << 8) | (random[1] ?? 0)) & COUNTER_SEED_MAX
    } else if (counter < COUNTER_MAX) {
      counter++
    } else {
      lastMillis++
      counter = 0
    }
    return format(lastMillis, counter, random)
  }
}

/** Default generator used by the application. */
export const createUuidV7: UuidV7Generator = createUuidV7Generator()

/** Tells whether the text is a lowercase version 7 UUID with the RFC variant. */
export function isUuidV7(text: string): boolean {
  return UUID_V7_PATTERN.test(text)
}

function format(millis: number, counter: number, random: Uint8Array<ArrayBuffer>): string {
  const bytes = new Uint8Array(16)
  let remaining = millis
  for (let index = 5; index >= 0; index--) {
    bytes[index] = remaining % 256
    remaining = Math.floor(remaining / 256)
  }
  bytes[6] = 0x70 | (counter >> 8)
  bytes[7] = counter & 0xff
  for (let index = 8; index < 16; index++) {
    bytes[index] = random[index - 6] ?? 0
  }
  bytes[8] = 0x80 | ((bytes[8] ?? 0) & 0x3f)
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
