import { useLivePolling } from '~/composables/useLivePolling'
import type { DashboardService } from '~/services/DashboardService'
import type { MetricsService } from '~/services/MetricsService'
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
import { DashboardLayoutIO } from './dashboardLayoutIO'
import { defaultConfigFor, defaultSizeFor } from './registry'
import { useInstrumentCatalog } from './useInstrumentCatalog'

/**
 * Page state for `/dashboard`. Loads the seeded "default" dashboard, keeps a
 * working copy of the layout that diverges from the persisted snapshot only
 * in edit mode, and surfaces a single `liveTickCounter` ref that widgets
 * watch to re-fetch their data on every live tick.
 *
 * Live mode is disabled while editing (changing the layout while widgets
 * keep refetching would duplicate work and surprise the user).
 */
export function useDashboardPage(service: DashboardService, metricsService: MetricsService) {
  const { t } = useI18n()
  const layoutIO = new DashboardLayoutIO(metricsService)
  const catalog = useInstrumentCatalog(metricsService)

  // List of all dashboards (envelope only — widgets are loaded on selection).
  // Drives the toolbar selector and the delete-disabled state for the default.
  const dashboards = ref<DashboardDto[]>([])
  const currentDashboardId = ref<string>(DEFAULT_DASHBOARD_ID)

  // Persisted snapshot — replaced on save, used to detect dirty + revert cancel.
  const dashboard = ref<DashboardDto | null>(null)
  const persistedLayoutJson = ref<string>('{"widgets":[]}')
  const rowVersion = ref<number>(0)

  const isCurrentDeletable = computed(() => currentDashboardId.value !== DEFAULT_DASHBOARD_ID)

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
      const dto = await service.getById(currentDashboardId.value)
      applyServerState(dto)
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      if (!silent) isLoading.value = false
    }
  }

  async function loadList(silent = false) {
    try {
      dashboards.value = await service.list()
    } catch (e) {
      if (!silent) error.value = e instanceof Error ? e.message : String(e)
    }
  }

  /**
   * Switch to a different dashboard. Caller is responsible for confirming
   * any pending dirty edits — the page itself simply discards the working
   * copy when the requested ID differs from the current one.
   */
  async function selectDashboard(id: string) {
    if (id === currentDashboardId.value) return
    if (isEditing.value) cancelEdit()
    currentDashboardId.value = id
    await load()
  }

  /**
   * Create an empty dashboard with the given name, switch to it, and enter
   * edit mode so the user can immediately populate widgets.
   */
  async function createDashboard(name: string): Promise<DashboardDto | null> {
    error.value = null
    try {
      const dto = await service.create({ name, widgets: [], rowVersion: 0 })
      dashboards.value = [...dashboards.value, dto]
      currentDashboardId.value = dto.id
      applyServerState(dto)
      enterEdit()
      return dto
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
      return null
    }
  }

  /**
   * Delete the currently selected dashboard and fall back to the default.
   * The default dashboard is protected server-side; we also guard here so
   * the UI doesn't surface a 400 needlessly.
   */
  async function deleteCurrentDashboard(): Promise<boolean> {
    if (!isCurrentDeletable.value) return false
    error.value = null
    const id = currentDashboardId.value
    try {
      await service.delete(id)
      dashboards.value = dashboards.value.filter(d => d.id !== id)
      currentDashboardId.value = DEFAULT_DASHBOARD_ID
      if (isEditing.value) cancelEdit()
      await load()
      return true
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
      return false
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
      const dto = await service.update(currentDashboardId.value, request)
      applyServerState(dto)
      // Keep the list envelope in sync (name/updatedAt may have changed).
      dashboards.value = dashboards.value.map(d => (d.id === dto.id ? dto : d))
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

  // Live polling: refresh the instrument catalog and the dashboard envelope,
  // then bump the tick counter so widgets re-fetch with the freshest data.
  // The catalog refresh comes before the bump so widgets see any newly-pushed
  // instruments on the same tick (otherwise late-binding would miss them
  // until the next cycle).
  async function liveTick() {
    await catalog.refresh()
    liveTickCounter.value++
    try {
      const dto = await service.getById(currentDashboardId.value)
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

  function exportLayout() {
    layoutIO.exportToFile(layout.value, dashboard.value?.name ?? 'main')
  }

  /**
   * Replace the working layout with the contents of an imported JSON file.
   * Auto-enters edit mode on success so the user can review/save/cancel
   * before persisting — the server is not touched here.
   */
  async function importLayout(file: File): Promise<boolean> {
    const result = await layoutIO.importFromFile(file)

    if (result.kind === 'invalid') {
      error.value = t('dashboard.errors.importInvalid')
      return false
    }
    if (result.kind === 'parse-error') {
      error.value = result.cause.message
      return false
    }

    if (!isEditing.value) {
      // Snapshot the current layout so `cancelEdit` can revert if the user
      // changes their mind after seeing the imported version.
      persistedLayoutJson.value = JSON.stringify(layout.value)
      isEditing.value = true
    }
    layout.value = { widgets: result.widgets }
    error.value = result.unresolvedBindings > 0
      ? t('dashboard.errors.importPartialMatch', { n: result.unresolvedBindings })
      : null
    return true
  }

  void Promise.all([loadList(), load()])

  return {
    // State
    dashboard,
    dashboards,
    currentDashboardId,
    isCurrentDeletable,
    layout,
    rowVersion,
    isLoading,
    isSaving,
    isDirty,
    error,

    // Multi-dashboard
    selectDashboard,
    createDashboard,
    deleteCurrentDashboard,

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
