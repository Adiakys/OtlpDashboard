import { useLivePolling } from '~/composables/useLivePolling'
import type { DashboardService } from '~/services/DashboardService'
import type { DashboardDto, SaveDashboardRequest } from '~/services/types'
import type {
  DashboardLayout,
  WidgetConfig,
  WidgetItem,
  WidgetKind
} from './types'
import { defaultConfigFor, defaultSizeFor } from './registry'

/**
 * Page state for `/dashboard`. Loads the singleton "default" dashboard,
 * keeps a working copy of the layout that diverges from the persisted
 * snapshot only in edit mode, and surfaces a single `liveTickCounter` ref
 * that widgets watch to re-fetch their data on every live tick.
 *
 * Live mode is disabled while editing (changing the layout while widgets
 * keep refetching would duplicate work and surprise the user).
 */
export function useDashboardPage(service: DashboardService) {
  // Persisted snapshot — replaced on save, used to detect dirty + revert cancel.
  const dashboard = ref<DashboardDto | null>(null)
  const persistedLayoutJson = ref<string>('{"widgets":[]}')
  const rowVersion = ref<number>(0)

  // Working copy. In view mode this mirrors the persisted layout; in edit
  // mode it diverges until save/cancel.
  const layout = ref<DashboardLayout>({ widgets: [] })

  const isLoading = ref(false)
  const isSaving = ref(false)
  const error = ref<string | null>(null)

  const isEditing = ref(false)
  const editingWidgetId = ref<string | null>(null)
  const pickerOpen = ref(false)

  // Bumped on every live tick. Widgets that need refresh import this ref
  // and `watch` it to trigger their own re-fetch — no event bus needed.
  const liveTickCounter = ref(0)

  const isDirty = computed(() => JSON.stringify(layout.value) !== persistedLayoutJson.value)

  async function load(silent = false) {
    if (!silent) {
      isLoading.value = true
      error.value = null
    }
    try {
      const dto = await service.getDefault()
      applyServerState(dto)
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      if (!silent) isLoading.value = false
    }
  }

  function applyServerState(dto: DashboardDto) {
    dashboard.value = dto
    rowVersion.value = dto.rowVersion
    const parsed = parseLayout(dto.layoutJson)
    persistedLayoutJson.value = JSON.stringify(parsed)
    if (!isEditing.value) {
      // Don't clobber the user's in-progress edits on a background refresh.
      layout.value = parsed
    }
  }

  function parseLayout(layoutJson: string): DashboardLayout {
    try {
      const obj = JSON.parse(layoutJson) as Partial<DashboardLayout>
      if (obj && Array.isArray(obj.widgets)) {
        return { widgets: obj.widgets as WidgetItem[] }
      }
    } catch {
      /* fallthrough */
    }
    return { widgets: [] }
  }

  function enterEdit() {
    if (isEditing.value) return
    // Snapshot the current persisted layout: cancel reverts to this state.
    persistedLayoutJson.value = JSON.stringify(layout.value)
    isEditing.value = true
  }

  function cancelEdit() {
    if (!isEditing.value) return
    layout.value = parseLayout(persistedLayoutJson.value)
    editingWidgetId.value = null
    pickerOpen.value = false
    isEditing.value = false
  }

  async function save() {
    if (!isEditing.value || isSaving.value) return
    isSaving.value = true
    error.value = null
    try {
      const request: SaveDashboardRequest = {
        name: dashboard.value?.name ?? 'Default',
        layoutJson: JSON.stringify(layout.value),
        rowVersion: rowVersion.value
      }
      const dto = await service.saveDefault(request)
      applyServerState(dto)
      isEditing.value = false
      editingWidgetId.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      isSaving.value = false
    }
  }

  function nextWidgetId(): string {
    if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
      return crypto.randomUUID()
    }
    return `w-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  }

  /** Where to drop a new widget so it doesn't overlap the current layout. */
  function nextRowFor(width: number): { x: number; y: number } {
    const widgets = layout.value.widgets
    if (widgets.length === 0) return { x: 0, y: 0 }
    let maxY = 0
    for (const w of widgets) {
      const bottom = w.y + w.h
      if (bottom > maxY) maxY = bottom
    }
    return { x: 0, y: maxY }
  }

  function addWidget(kind: WidgetKind) {
    if (!isEditing.value) return
    const size = defaultSizeFor(kind)
    const pos = nextRowFor(size.w)
    const item: WidgetItem = {
      id: nextWidgetId(),
      kind,
      x: pos.x,
      y: pos.y,
      w: size.w,
      h: size.h,
      config: defaultConfigFor(kind)
    }
    layout.value = { widgets: [...layout.value.widgets, item] }
    pickerOpen.value = false
    // Open the config drawer immediately — most widgets are useless without setup.
    editingWidgetId.value = item.id
  }

  function removeWidget(id: string) {
    if (!isEditing.value) return
    layout.value = { widgets: layout.value.widgets.filter(w => w.id !== id) }
    if (editingWidgetId.value === id) editingWidgetId.value = null
  }

  function updateWidget(id: string, patch: Partial<WidgetItem>) {
    layout.value = {
      widgets: layout.value.widgets.map(w => (w.id === id ? { ...w, ...patch } : w))
    }
  }

  function updateWidgetConfig(id: string, config: WidgetConfig) {
    updateWidget(id, { config })
  }

  function updateLayoutCoords(coords: Array<{ id: string; x: number; y: number; w: number; h: number }>) {
    if (!isEditing.value) return
    const byId = new Map(coords.map(c => [c.id, c]))
    let changed = false
    const nextWidgets = layout.value.widgets.map(w => {
      const c = byId.get(w.id)
      if (!c) return w
      if (c.x === w.x && c.y === w.y && c.w === w.w && c.h === w.h) return w
      changed = true
      return { ...w, x: c.x, y: c.y, w: c.w, h: c.h }
    })
    // Skip the mutation when nothing actually moved. Without this guard,
    // every no-op `layout-updated` from grid-layout-plus would still allocate
    // a fresh array, propagate down through DashboardGrid, and loop.
    if (!changed) return
    layout.value = { widgets: nextWidgets }
  }

  function startWidgetConfig(id: string) {
    editingWidgetId.value = id
  }

  function finishWidgetConfig() {
    editingWidgetId.value = null
  }

  function openPicker() {
    if (isEditing.value) pickerOpen.value = true
  }

  function closePicker() {
    pickerOpen.value = false
  }

  // Live polling: refresh the dashboard envelope (in case someone else saved
  // it) and bump the tick counter so widgets re-fetch.
  async function liveTick() {
    liveTickCounter.value++
    // Best-effort silent refresh of the envelope so concurrent edits surface
    // a fresh `rowVersion` next time the user enters edit mode.
    try {
      const dto = await service.getDefault()
      applyServerState(dto)
    } catch {
      /* keep current state */
    }
  }

  const live = useLivePolling(liveTick, { autoStart: false, intervalMs: 5000 })

  // Disable live polling while editing — the user is mutating the layout and
  // background refreshes would either clobber it (we guard against that with
  // applyServerState) or mask concurrency conflicts.
  watch(isEditing, editing => {
    if (editing && live.isLive.value) live.stop()
  })

  function toggleLive() {
    if (isEditing.value) return
    live.toggle()
  }

  void load()

  return {
    // State
    dashboard,
    layout,
    rowVersion,
    isLoading,
    isSaving,
    isDirty,
    error,

    // Edit lifecycle
    isEditing,
    enterEdit,
    cancelEdit,
    save,

    // Widget mutations
    addWidget,
    removeWidget,
    updateWidget,
    updateWidgetConfig,
    updateLayoutCoords,

    // Widget config drawer
    editingWidgetId,
    startWidgetConfig,
    finishWidgetConfig,

    // Picker dialog
    pickerOpen,
    openPicker,
    closePicker,

    // Live mode
    isLive: live.isLive,
    toggleLive,
    liveTickCounter,

    // Actions
    reload: () => load(false)
  }
}
