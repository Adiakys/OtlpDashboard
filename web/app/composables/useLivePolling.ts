import { getCurrentInstance, onUnmounted, readonly, ref } from 'vue'

/**
 * Shared polling primitive for the "live" mode on /logs, /traces, /metrics.
 *
 * Responsibilities are intentionally narrow:
 *  - owns the `setInterval` lifecycle (clears on unmount);
 *  - guards against overlapping ticks when the server is slower than the
 *    interval (a long tick makes us SKIP the next one, not queue it);
 *  - pauses while the browser tab is hidden and resumes on re-focus so we
 *    don't burn requests on a background tab.
 *
 * The composable knows nothing about logs/traces/metrics — each page passes
 * its own `tick` function. This keeps the transport as an internal detail
 * of each `usePage.ts` and leaves the door open to swap polling for SSE
 * later without touching the UI.
 */
export interface UseLivePollingOptions {
  /** Interval between ticks in ms. Default: 5000. */
  intervalMs?: number
  /**
   * Start polling immediately on mount. The first tick does NOT fire
   * synchronously (the page's own initial fetch is already in flight);
   * subsequent ticks follow `intervalMs`.
   */
  autoStart?: boolean
}

export function useLivePolling(tick: () => Promise<void>, options: UseLivePollingOptions = {}) {
  const intervalMs = options.intervalMs ?? 5000
  const isLive = ref(false)
  let timerId: ReturnType<typeof setInterval> | null = null
  let running = false

  async function runOnce() {
    if (running) return
    running = true
    try {
      await tick()
    } finally {
      running = false
    }
  }

  function start() {
    if (timerId !== null) return
    timerId = setInterval(() => { void runOnce() }, intervalMs)
  }

  function stopTimer() {
    if (timerId !== null) {
      clearInterval(timerId)
      timerId = null
    }
  }

  function onVisibilityChange() {
    if (!isLive.value) return
    if (typeof document === 'undefined') return
    if (document.visibilityState === 'hidden') {
      stopTimer()
    } else {
      // Fire an immediate tick on re-focus so the user sees fresh data
      // without waiting a full interval.
      start()
      void runOnce()
    }
  }

  function toggle() {
    if (isLive.value) {
      stop()
    } else {
      isLive.value = true
      if (typeof document !== 'undefined') {
        document.addEventListener('visibilitychange', onVisibilityChange)
      }
      // Immediate first tick so the UI reacts to the click.
      void runOnce()
      start()
    }
  }

  function stop() {
    if (!isLive.value) return
    isLive.value = false
    stopTimer()
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }

  // Auto-cleanup only when invoked inside a component (i.e. real Nuxt runtime).
  // In unit tests we construct the composable outside any component and rely
  // on the explicit `stop()` for teardown.
  if (getCurrentInstance()) {
    onUnmounted(stop)
  }

  if (options.autoStart) {
    // Start polling without firing an immediate tick: the caller's own
    // initial fetch is already populating the UI, so a second in-flight
    // request would just duplicate work.
    isLive.value = true
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', onVisibilityChange)
    }
    start()
  }

  return {
    isLive: readonly(isLive),
    toggle,
    stop
  }
}
