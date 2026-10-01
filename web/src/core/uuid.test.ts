import { createUuidV7Generator, isUuidV7 } from './uuid.ts'

describe('UUIDv7', () => {
  it('produces lowercase version 7 UUIDs with the RFC variant', () => {
    const create = createUuidV7Generator()
    for (let index = 0; index < 100; index++) {
      expect(isUuidV7(create())).toBe(true)
    }
  })

  it('encodes the timestamp in the first 48 bits', () => {
    const create = createUuidV7Generator(
      () => 0x0123456789ab,
      (bytes) => {
        bytes.fill(0)
      },
    )
    expect(create().slice(0, 13)).toBe('01234567-89ab')
  })

  it('increases monotonically within one millisecond and when the clock goes back', () => {
    const times = [1000, 1000, 1000, 999, 1001]
    let index = 0
    const create = createUuidV7Generator(
      () => times[Math.min(index++, times.length - 1)] ?? 0,
      (bytes) => {
        bytes.fill(0xff)
      },
    )
    const ids = Array.from({ length: 5 }, () => create())
    expect([...ids].sort()).toEqual(ids)
    expect(new Set(ids).size).toBe(5)
  })

  it('rejects other versions, the nil UUID, and uppercase text', () => {
    expect(isUuidV7('0f8fad5b-d9cb-469f-a165-70867728950e')).toBe(false)
    expect(isUuidV7('00000000-0000-0000-0000-000000000000')).toBe(false)
    expect(isUuidV7('01890A5D-AC96-774B-BCCE-B302099A8057')).toBe(false)
    expect(isUuidV7('01890a5d-ac96-774b-7cce-b302099a8057')).toBe(false)
    expect(isUuidV7('01890a5d-ac96-774b-bcce-b302099a8057')).toBe(true)
  })
})
