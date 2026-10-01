import { SessionClock } from './SessionClock.ts'
import type { ResetReason } from './SessionClock.ts'

function createClock(generation: number) {
  const clock = new SessionClock()
  const resets: ResetReason[] = []
  clock.onReset((reason) => resets.push(reason))
  clock.accept(clock.epoch, generation)
  return { clock, resets }
}

describe('SessionClock', () => {
  it('adopts the first generation and accepts responses that carry it', () => {
    const { clock, resets } = createClock(3)
    expect(clock.generation).toBe(3)
    expect(clock.accept(clock.epoch, 3)).toBe(true)
    expect(clock.accept(clock.epoch, null)).toBe(true)
    expect(resets).toEqual([])
  })

  it('drops a response to a request sent before the last reset', () => {
    const { clock } = createClock(3)
    const epoch = clock.epoch
    clock.reset(4, 'local')
    expect(clock.accept(epoch, 4)).toBe(false)
  })

  it('runs the Reset handling for a newer generation and drops the response', () => {
    const { clock, resets } = createClock(3)
    expect(clock.accept(clock.epoch, 4)).toBe(false)
    expect(clock.generation).toBe(4)
    expect(resets).toEqual(['remote'])
  })

  it('runs the Reset handling for an older generation, as after the database was recreated', () => {
    const { clock, resets } = createClock(5)
    expect(clock.accept(clock.epoch, 1)).toBe(false)
    expect(clock.generation).toBe(1)
    expect(resets).toEqual(['remote'])
    expect(clock.accept(clock.epoch, 1)).toBe(true)
  })

  it("adopts the generation of the session's own Reset", () => {
    const { clock, resets } = createClock(3)
    clock.resetLocally(clock.epoch, 4)
    expect(clock.generation).toBe(4)
    expect(resets).toEqual(['local'])
  })

  it('keeps a newer generation that arrived while its own Reset was in flight', () => {
    const { clock, resets } = createClock(3)
    const sentIn = clock.epoch
    clock.accept(sentIn, 5)
    clock.resetLocally(sentIn, 4)
    expect(clock.generation).toBe(5)
    expect(resets).toEqual(['remote'])
  })

  it('adopts its own Reset over an older one by another session that arrived first', () => {
    const { clock, resets } = createClock(3)
    const sentIn = clock.epoch
    clock.accept(sentIn, 4)
    clock.resetLocally(sentIn, 5)
    expect(clock.generation).toBe(5)
    expect(resets).toEqual(['remote', 'local'])
  })
})
