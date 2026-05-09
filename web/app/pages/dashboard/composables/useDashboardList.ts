import { computed, ref, type Ref } from 'vue'
import type { DashboardService } from '~/services/DashboardService'
import {
  DEFAULT_DASHBOARD_ID,
  type DashboardDto,
  type SaveDashboardRequest
} from '~/services/types'

/**
 * List + selection of dashboards. Owns the envelope-only collection
 * (`DashboardDto[]` minus widgets), the currently selected ID, and the
 * derived "is the current dashboard deletable" flag (the seeded default
 * is protected server-side; mirrored here so the UI can disable the button).
 *
 * Loading the *contents* of a dashboard is delegated to the caller via
 * `onSelected(id)` — this composable only tracks identity.
 */
export function useDashboardList(
  service: DashboardService,
  options: { onSelected: (id: string) => Promise<void> | void }
) {
  const dashboards = ref<DashboardDto[]>([])
  const currentDashboardId = ref<string>(DEFAULT_DASHBOARD_ID)
  const error = ref<string | null>(null)

  const isCurrentDeletable = computed(() => currentDashboardId.value !== DEFAULT_DASHBOARD_ID)

  async function loadList(silent = false): Promise<void> {
    try {
      dashboards.value = await service.list()
    } catch (e) {
      if (!silent) error.value = e instanceof Error ? e.message : String(e)
    }
  }

  /**
   * Switch to a different dashboard. The caller is responsible for confirming
   * any pending dirty edits *before* invoking this — the composable simply
   * delegates to `onSelected(id)` once the ID is committed.
   */
  async function selectDashboard(id: string): Promise<void> {
    if (id === currentDashboardId.value) return
    currentDashboardId.value = id
    await options.onSelected(id)
  }

  async function createDashboard(name: string): Promise<DashboardDto | null> {
    error.value = null
    try {
      const request: SaveDashboardRequest = { name, widgets: [], rowVersion: 0 }
      const dto = await service.create(request)
      dashboards.value = [...dashboards.value, dto]
      currentDashboardId.value = dto.id
      return dto
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
      return null
    }
  }

  async function deleteCurrentDashboard(): Promise<boolean> {
    if (!isCurrentDeletable.value) return false
    error.value = null
    const id = currentDashboardId.value
    const target = dashboards.value.find(d => d.id === id)
    if (!target) return false
    try {
      await service.delete(id, target.rowVersion)
      dashboards.value = dashboards.value.filter(d => d.id !== id)
      currentDashboardId.value = DEFAULT_DASHBOARD_ID
      await options.onSelected(DEFAULT_DASHBOARD_ID)
      return true
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
      return false
    }
  }

  /** Replace one envelope after a save (`updatedAt`/`name`/`rowVersion` may
   *  have changed). Keeps `dashboards` in sync without a full re-list. */
  function syncEnvelope(dto: DashboardDto): void {
    dashboards.value = dashboards.value.map(d => (d.id === dto.id ? dto : d))
  }

  /** Append an envelope and switch to it. Used after `createDashboard`
   *  succeeds and the caller wants to immediately enter edit mode. */
  function appendEnvelope(dto: DashboardDto): void {
    if (!dashboards.value.some(d => d.id === dto.id)) {
      dashboards.value = [...dashboards.value, dto]
    }
    currentDashboardId.value = dto.id
  }

  return {
    dashboards: dashboards as Ref<DashboardDto[]>,
    currentDashboardId,
    isCurrentDeletable,
    error,
    loadList,
    selectDashboard,
    createDashboard,
    deleteCurrentDashboard,
    syncEnvelope,
    appendEnvelope
  }
}
