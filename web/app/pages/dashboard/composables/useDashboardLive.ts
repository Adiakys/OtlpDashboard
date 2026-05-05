import { ref, watch } from 'vue'
import { useLivePolling } from '~/composables/useLivePolling'

/**
 * Live polling glue specific to the dashboard page. Owns the tick counter
 * watched by every widget, the pause-while-editing rule, and the toggle.
 *
 * The actual work that runs on each tick — refreshing the catalog, the
 * dashboard envelope, and bumping widgets — is supplied by the caller. This
 * composable only orchestrates the timer.
 */
export function useDashboardLive(
  tick: () => Promise<void>,
  isEditing: { value: boolean },
  options: { intervalMs?: number } = {}
) {
  const liveTickCounter = ref(0)

  async function onTick() {
    await tick()
    liveTickCounter.value++
  }

  // Auto-start: a dashboard is a "current state of the system" view, so the
  // expected default is that it's already updating when the user lands on
  // it. The pause-while-editing watcher below still guarantees background
  // refreshes don't fight with manual layout changes.
  const live = useLivePolling(onTick, { autoStart: true, intervalMs: options.intervalMs ?? 5000 })

  // Live polling vs edit mode:
  //  - On entering edit, stop the timer — background refreshes during a
  //    layout edit would either clobber the working copy or mask
  //    concurrency conflicts that the explicit save flow handles.
  //  - On leaving edit (save / cancel / switch dashboard), bring live
  //    back. The dashboard's baseline mode is "current state of the
  //    system"; pausing during edit is a transient exception, not a
  //    sticky setting.
  watch(() => isEditing.value, editing => {
    if (editing) {
      if (live.isLive.value) live.stop()
    } else {
      if (!live.isLive.value) live.toggle()
    }
  })

  function toggleLive(): void {
    if (isEditing.value) return
    live.toggle()
  }

  return {
    isLive: live.isLive,
    liveTickCounter,
    toggleLive,
    stop: live.stop
  }
}
