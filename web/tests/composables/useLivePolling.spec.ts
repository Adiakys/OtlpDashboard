import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useLivePolling } from '~/composables/useLivePolling'

/**
 * The composable calls `document.addEventListener` when `document` exists.
 * Node has no DOM, so we install a minimal stub that captures the listener
 * and lets us simulate visibility changes.
 */
function installDocumentStub() {
  const listeners = new Map<string, Array<(e: unknown) => void>>()
  const state = { visibilityState: 'visible' as DocumentVisibilityState }

  const stub = {
    get visibilityState() { return state.visibilityState },
    addEventListener(name: string, fn: (e: unknown) => void) {
      const list = listeners.get(name) ?? []
      list.push(fn)
      listeners.set(name, list)
    },
    removeEventListener(name: string, fn: (e: unknown) => void) {
      const list = listeners.get(name) ?? []
      listeners.set(name, list.filter(x => x !== fn))
    }
  }

  vi.stubGlobal('document', stub)

  return {
    setVisibility(next: DocumentVisibilityState) {
      state.visibilityState = next
      for (const fn of listeners.get('visibilitychange') ?? []) fn({})
    },
    hasListener() {
      return (listeners.get('visibilitychange') ?? []).length > 0
    }
  }
}

describe('useLivePolling', () => {
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('does not tick until toggled on', () => {
    installDocumentStub()
    const tick = vi.fn(async () => {})

    useLivePolling(tick, { intervalMs: 1000 })
    vi.advanceTimersByTime(5000)

    expect(tick).not.toHaveBeenCalled()
  })

  it('fires an immediate tick on toggle and then one per interval', async () => {
    installDocumentStub()
    const tick = vi.fn(async () => {})

    const { toggle, stop } = useLivePolling(tick, { intervalMs: 1000 })
    toggle()

    // Immediate synchronous call after toggle (void runOnce()).
    await vi.advanceTimersByTimeAsync(0)
    expect(tick).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(1000)
    expect(tick).toHaveBeenCalledTimes(2)

    await vi.advanceTimersByTimeAsync(1000)
    expect(tick).toHaveBeenCalledTimes(3)

    stop()
  })

  it('skips overlapping ticks when the previous one has not finished', async () => {
    installDocumentStub()
    let resolveTick: (() => void) | null = null
    const tick = vi.fn(() => new Promise<void>(res => { resolveTick = res }))

    const { toggle, stop } = useLivePolling(tick, { intervalMs: 100 })
    toggle()
    // First (immediate) tick is now in-flight but unresolved.
    await vi.advanceTimersByTimeAsync(0)
    expect(tick).toHaveBeenCalledTimes(1)

    // Several intervals pass — all should be skipped.
    await vi.advanceTimersByTimeAsync(500)
    expect(tick).toHaveBeenCalledTimes(1)

    // Resolve the first tick; the next interval fires and triggers tick #2.
    resolveTick?.()
    await vi.advanceTimersByTimeAsync(100)
    expect(tick).toHaveBeenCalledTimes(2)

    stop()
  })

  it('pauses while the tab is hidden and resumes on focus', async () => {
    const dom = installDocumentStub()
    const tick = vi.fn(async () => {})

    const { toggle, stop } = useLivePolling(tick, { intervalMs: 1000 })
    toggle()
    await vi.advanceTimersByTimeAsync(0)
    expect(tick).toHaveBeenCalledTimes(1)

    dom.setVisibility('hidden')
    await vi.advanceTimersByTimeAsync(5000)
    expect(tick).toHaveBeenCalledTimes(1) // no ticks while hidden

    dom.setVisibility('visible')
    // Re-focus triggers an immediate catch-up tick.
    await vi.advanceTimersByTimeAsync(0)
    expect(tick).toHaveBeenCalledTimes(2)

    // Interval resumes normally.
    await vi.advanceTimersByTimeAsync(1000)
    expect(tick).toHaveBeenCalledTimes(3)

    stop()
  })

  it('autoStart enters live mode without firing an immediate tick', async () => {
    installDocumentStub()
    const tick = vi.fn(async () => {})

    const { isLive, stop } = useLivePolling(tick, { intervalMs: 1000, autoStart: true })
    expect(isLive.value).toBe(true)

    // No synchronous first tick — caller's own initial fetch is in flight.
    await vi.advanceTimersByTimeAsync(0)
    expect(tick).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1000)
    expect(tick).toHaveBeenCalledTimes(1)

    stop()
  })

  it('toggle off stops the interval and removes the visibility listener', async () => {
    const dom = installDocumentStub()
    const tick = vi.fn(async () => {})

    const { toggle, isLive } = useLivePolling(tick, { intervalMs: 1000 })
    toggle()
    await vi.advanceTimersByTimeAsync(0)
    expect(isLive.value).toBe(true)
    expect(dom.hasListener()).toBe(true)

    toggle()
    expect(isLive.value).toBe(false)
    expect(dom.hasListener()).toBe(false)

    const before = tick.mock.calls.length
    await vi.advanceTimersByTimeAsync(10_000)
    expect(tick).toHaveBeenCalledTimes(before)
  })
})
