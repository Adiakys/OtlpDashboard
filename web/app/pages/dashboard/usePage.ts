import { useLivePolling } from '~/composables/useLivePolling'
import type { DashboardService } from '~/services/DashboardService'
import {
  DEFAULT_DASHBOARD_ID,
  type DashboardDto,
  type DashboardWidgetDto,
  type SaveDashboardRequest
} from '~/services/types'
import type {
  DashboardLayout,
  WidgetConfig,
  WidgetItem,
  WidgetKind
} from './types'
import { defaultConfigFor, defaultSizeFor } from './registry'

/**
 * Page state for `/dashboard`. Loads the seeded "default" dashboard, keeps a
 * working copy of the layout that diverges from the persisted snapshot only
 * in edit mode, and surfaces a single `liveTickCounter` ref that widgets
 * watch to re-fetch their data on every live tick.
 *
 * Live mode is disabled while editing (changing the layout while widgets
 * keep refetching would duplicate work and surprise the user).
 */
export function useDashboardPage(service: DashboardService) {
  const { t } = useI18n()

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
      const dto = await service.getById(DEFAULT_DASHBOARD_ID)
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
    const widgets = dto.widgets.map(widgetFromDto)
    persistedLayoutJson.value = JSON.stringify({ widgets })
    if (!isEditing.value) {
      // Don't clobber the user's in-progress edits on a background refresh.
      layout.value = { widgets }
    }
  }

  function widgetFromDto(dto: DashboardWidgetDto): WidgetItem {
    // Server stores config as opaque JSON; the SPA owns the per-kind shape.
    return {
      id: dto.id,
      kind: dto.kind as WidgetKind,
      x: dto.x,
      y: dto.y,
      w: dto.w,
      h: dto.h,
      config: dto.config as unknown as WidgetConfig
    }
  }

  function widgetToDto(item: WidgetItem): DashboardWidgetDto {
    return {
      id: item.id,
      kind: item.kind,
      x: item.x,
      y: item.y,
      w: item.w,
      h: item.h,
      config: item.config as unknown as Record<string, unknown>
    }
  }

  function enterEdit() {
    if (isEditing.value) return
    // Snapshot the current persisted layout: cancel reverts to this state.
    persistedLayoutJson.value = JSON.stringify(layout.value)
    isEditing.value = true
  }

  function cancelEdit() {
    if (!isEditing.value) return
    const snapshot = JSON.parse(persistedLayoutJson.value) as DashboardLayout
    layout.value = snapshot
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
        name: dashboard.value?.name ?? 'main',
        widgets: layout.value.widgets.map(widgetToDto),
        rowVersion: rowVersion.value
      }
      const dto = await service.update(DEFAULT_DASHBOARD_ID, request)
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
  function nextRowFor(_width: number): { x: number; y: number } {
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
      const dto = await service.getById(DEFAULT_DASHBOARD_ID)
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

  /**
   * Serialize the current working layout to a JSON file and trigger a
   * download. Excludes server-managed fields (`id`, `rowVersion`,
   * `updatedAt`) — those are reassigned by the server on save and should
   * never be transplanted between dashboards. The widget list is kept
   * verbatim, including widget IDs, so re-importing one's own export round-
   * trips cleanly.
   */
  function exportLayout() {
    const payload = {
      version: 1 as const,
      exportedAt: new Date().toISOString(),
      name: dashboard.value?.name ?? 'main',
      widgets: layout.value.widgets
    }
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `dashboard-${payload.name}-${new Date().toISOString().slice(0, 10)}.json`
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  }

  /**
   * Read a JSON file produced by `exportLayout` (or an externally-authored
   * one matching the same shape) and replace the working layout. Editing
   * mode is auto-entered when the import succeeds so the user can review
   * before saving — `Save` becomes enabled because the working layout
   * differs from the persisted snapshot, `Cancel` reverts back. The server
   * is not touched here.
   *
   * Validation is intentionally permissive on `kind`: an unknown kind from
   * a future build is accepted and the grid will simply skip rendering it,
   * rather than rejecting the whole file.
   */
  async function importLayout(file: File): Promise<boolean> {
    try {
      const text = await file.text()
      const data = JSON.parse(text) as unknown
      if (!isValidExport(data)) {
        error.value = t('dashboard.errors.importInvalid')
        return false
      }
      if (!isEditing.value) {
        // Snapshot the current layout so `cancelEdit` can revert if the user
        // changes their mind after seeing the imported version.
        persistedLayoutJson.value = JSON.stringify(layout.value)
        isEditing.value = true
      }
      layout.value = { widgets: data.widgets as WidgetItem[] }
      error.value = null
      return true
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
      return false
    }
  }

  function isValidExport(data: unknown): data is { name?: string; widgets: WidgetItem[] } {
    if (!data || typeof data !== 'object') return false
    const obj = data as { widgets?: unknown }
    if (!Array.isArray(obj.widgets)) return false
    for (const w of obj.widgets) {
      if (!w || typeof w !== 'object') return false
      const item = w as Record<string, unknown>
      if (typeof item.id !== 'string') return false
      if (typeof item.kind !== 'string') return false
      if (typeof item.x !== 'number' || typeof item.y !== 'number') return false
      if (typeof item.w !== 'number' || typeof item.h !== 'number') return false
      if (!item.config || typeof item.config !== 'object') return false
    }
    return true
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

    // Import / export
    exportLayout,
    importLayout,

    // Actions
    reload: () => load(false)
  }
}
