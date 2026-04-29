import { computed, ref } from 'vue'
import type { DashboardService } from '~/services/DashboardService'
import type { MetricsService } from '~/services/MetricsService'
import {
  type DashboardDto,
  type DashboardWidgetDto,
  type SaveDashboardRequest
} from '~/services/types'
import type {
  WidgetConfig,
  WidgetItem,
  WidgetKind
} from './types'
import { useInstrumentCatalog } from './useInstrumentCatalog'
import { useDashboardList } from './composables/useDashboardList'
import { useDashboardEdit } from './composables/useDashboardEdit'
import { useDashboardLive } from './composables/useDashboardLive'
import { useDashboardIO } from './composables/useDashboardIO'
import { useMetricSeriesCache } from './useMetricSeriesCache'

/**
 * Page-level orchestrator. Composes four narrow composables — list, edit,
 * live polling, import/export — and wires them to the network: this file is
 * the only place that calls the dashboard service directly.
 *
 * Each sub-composable owns its slice of state and exposes a small,
 * testable API; this orchestrator does no business logic of its own. If
 * you're tempted to add a non-trivial helper here, it almost certainly
 * belongs in one of the sub-composables instead.
 */
export function useDashboardPage(service: DashboardService, metricsService: MetricsService) {
  const { t } = useI18n()
  const catalog = useInstrumentCatalog(metricsService)
  const seriesCache = useMetricSeriesCache(metricsService)

  // Persisted snapshot — replaced on save, used to detect dirty + revert cancel.
  const dashboard = ref<DashboardDto | null>(null)
  const rowVersion = ref<number>(0)

  const isLoading = ref(false)
  const isSaving = ref(false)
  const error = ref<string | null>(null)

  const edit = useDashboardEdit()

  const list = useDashboardList(service, {
    onSelected: async (_id) => {
      // Switching dashboards always discards in-progress edits — the page
      // surface decides upfront whether to confirm with the user.
      if (edit.isEditing.value) edit.cancelEdit()
      await load()
    }
  })

  async function load(silent = false): Promise<void> {
    if (!silent) {
      isLoading.value = true
      error.value = null
    }
    try {
      const dto = await service.getById(list.currentDashboardId.value)
      applyServerState(dto)
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      if (!silent) isLoading.value = false
    }
  }

  function applyServerState(dto: DashboardDto): void {
    dashboard.value = dto
    rowVersion.value = dto.rowVersion
    edit.applyPersisted(dto.widgets.map(widgetFromDto))
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

  async function save(): Promise<void> {
    if (!edit.isEditing.value || isSaving.value) return
    isSaving.value = true
    error.value = null
    try {
      const request: SaveDashboardRequest = {
        name: dashboard.value?.name ?? 'main',
        widgets: edit.layout.value.widgets.map(widgetToDto),
        rowVersion: rowVersion.value
      }
      const dto = await service.update(list.currentDashboardId.value, request)
      applyServerState(dto)
      list.syncEnvelope(dto)
      edit.ackSaved()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      isSaving.value = false
    }
  }

  async function createDashboardAndEdit(name: string): Promise<DashboardDto | null> {
    const dto = await list.createDashboard(name)
    if (!dto) {
      // Surface the list-level error on the page so the modal can stay open.
      error.value = list.error.value
      return null
    }
    applyServerState(dto)
    edit.enterEdit()
    return dto
  }

  // Live polling: refresh the catalog and the envelope, then bump the tick
  // counter so widgets re-fetch with the freshest data. Catalog refresh comes
  // *before* the bump so widgets see any newly-pushed instruments on the same
  // tick (otherwise late-binding would miss them until the next cycle). The
  // shared series cache is invalidated on every tick — widgets that share a
  // metric collapse into one network round-trip on the next fetch.
  async function liveTick(): Promise<void> {
    await catalog.refresh()
    seriesCache.invalidate()
    try {
      const dto = await service.getById(list.currentDashboardId.value)
      applyServerState(dto)
    } catch {
      /* keep current state */
    }
  }

  const live = useDashboardLive(liveTick, edit.isEditing, { intervalMs: 5000 })

  const io = useDashboardIO(metricsService)

  function exportLayout(): void {
    io.exportLayout(edit.layout.value, dashboard.value?.name ?? 'main')
  }

  /**
   * Replace the working layout with the contents of an imported JSON file.
   * Auto-enters edit mode on success so the user can review/save/cancel
   * before persisting — the server is not touched here.
   */
  async function importLayout(file: File): Promise<boolean> {
    const result = await io.importLayout(file)

    if (result.kind === 'invalid') {
      error.value = t('dashboard.errors.importInvalid')
      return false
    }
    if (result.kind === 'parse-error') {
      error.value = result.cause.message
      return false
    }

    if (!edit.isEditing.value) edit.enterEdit()
    edit.applyWorking(result.widgets)
    error.value = result.unresolvedBindings > 0
      ? t('dashboard.errors.importPartialMatch', { n: result.unresolvedBindings })
      : null
    return true
  }

  // Combine list and page errors into a single computed surface so the
  // template only watches one ref. The list composable surfaces network
  // failures from create/delete; the page surfaces failures from load/save.
  const combinedError = computed(() => error.value ?? list.error.value)

  // Initial fetch — list envelope and current dashboard in parallel.
  void Promise.all([list.loadList(), load()])

  return {
    // Persisted state
    dashboard,
    dashboards: list.dashboards,
    currentDashboardId: list.currentDashboardId,
    isCurrentDeletable: list.isCurrentDeletable,
    layout: edit.layout,
    rowVersion,
    isLoading,
    isSaving,
    isDirty: edit.isDirty,
    error: combinedError,

    // Multi-dashboard
    selectDashboard: list.selectDashboard,
    createDashboard: createDashboardAndEdit,
    deleteCurrentDashboard: list.deleteCurrentDashboard,

    // Edit lifecycle
    isEditing: edit.isEditing,
    enterEdit: edit.enterEdit,
    cancelEdit: edit.cancelEdit,
    save,

    // Widget mutations
    addWidget: edit.addWidget,
    removeWidget: edit.removeWidget,
    updateWidget: edit.updateWidget,
    updateWidgetConfig: edit.updateWidgetConfig,
    updateLayoutCoords: edit.updateLayoutCoords,

    // Widget config drawer
    editingWidgetId: edit.editingWidgetId,
    startWidgetConfig: edit.startWidgetConfig,
    finishWidgetConfig: edit.finishWidgetConfig,

    // Picker dialog
    pickerOpen: edit.pickerOpen,
    openPicker: edit.openPicker,
    closePicker: edit.closePicker,

    // Live mode
    isLive: live.isLive,
    toggleLive: live.toggleLive,
    liveTickCounter: live.liveTickCounter,

    // Import / export
    exportLayout,
    importLayout,

    // Actions
    reload: () => load(false)
  }
}
