import { computed, ref, type Ref } from 'vue'
import type {
  DashboardLayout,
  FQKind,
  WidgetConfig,
  WidgetItem
} from '../types'
import { normalizeKind } from '../types'
import { defaultConfigForDefinition, useWidgetCatalog } from '../catalog'
import { newGuid } from '~/lib/uuid'

/**
 * Edit-mode state machine. Holds the mutable working copy of the layout, a
 * persisted snapshot used to detect dirtiness and revert on cancel, and the
 * widget-mutation primitives (add / remove / update / move).
 *
 * The persisted snapshot is *replaced* externally via `applyPersisted` after
 * a successful load or save — this composable doesn't know about the
 * network. That separation keeps the edit logic synchronous and trivially
 * testable.
 */
export function useDashboardEdit() {
  const layout = ref<DashboardLayout>({ widgets: [] })
  const persistedLayoutJson = ref<string>('{"widgets":[]}')

  const isEditing = ref(false)
  const editingWidgetId = ref<string | null>(null)
  const pickerOpen = ref(false)

  const isDirty = computed(() => JSON.stringify(layout.value) !== persistedLayoutJson.value)

  /**
   * Replace the persisted snapshot (e.g. after a save round-trips). When the
   * user is *not* editing the working copy is updated in lock-step; while
   * editing we keep the user's in-progress edits intact.
   */
  function applyPersisted(widgets: WidgetItem[]): void {
    persistedLayoutJson.value = JSON.stringify({ widgets })
    if (!isEditing.value) {
      layout.value = { widgets }
    }
  }

  /**
   * Replace the working layout *without* updating the snapshot. Used when an
   * imported layout should sit in front of the user as an unsaved diff so
   * they can review before persisting.
   */
  function applyWorking(widgets: WidgetItem[]): void {
    layout.value = { widgets }
  }

  function enterEdit(): void {
    if (isEditing.value) return
    // Snapshot the current persisted layout: cancel reverts to this state.
    persistedLayoutJson.value = JSON.stringify(layout.value)
    isEditing.value = true
  }

  function cancelEdit(): void {
    if (!isEditing.value) return
    const snapshot = JSON.parse(persistedLayoutJson.value) as DashboardLayout
    layout.value = snapshot
    editingWidgetId.value = null
    pickerOpen.value = false
    isEditing.value = false
  }

  /** Acknowledge a successful save: editing flag drops, drawer closes.
   *  The persisted snapshot is refreshed by the caller via `applyPersisted`. */
  function ackSaved(): void {
    isEditing.value = false
    editingWidgetId.value = null
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

  /**
   * Add a fresh widget instance to the working layout. `kind` is a fully
   * qualified value (`std:metric-stat` / `custom:<uuid>` / `library:<id>/<kindId>`);
   * legacy bare-kind values are tolerated via `normalizeKind`. The catalog
   * supplies `defaultSize` and seed config — for `custom`/`library` widgets
   * this is the user-saved preset; for `std` it's the registry baseline.
   */
  function addWidget(kind: FQKind): void {
    if (!isEditing.value) return
    const fq = normalizeKind(kind)
    const catalog = useWidgetCatalog()
    const def = catalog.byKind(fq)
    if (!def) return // unknown kind — no-op rather than crash
    const size = def.defaultSize
    const pos = nextRowFor(size.w)
    const item: WidgetItem = {
      id: newGuid(),
      kind: fq,
      x: pos.x,
      y: pos.y,
      w: size.w,
      h: size.h,
      config: defaultConfigForDefinition(def)
    }
    layout.value = { widgets: [...layout.value.widgets, item] }
    pickerOpen.value = false
    // Open the config drawer immediately — most widgets are useless without setup.
    editingWidgetId.value = item.id
  }

  function removeWidget(id: string): void {
    if (!isEditing.value) return
    layout.value = { widgets: layout.value.widgets.filter(w => w.id !== id) }
    if (editingWidgetId.value === id) editingWidgetId.value = null
  }

  function updateWidget(id: string, patch: Partial<WidgetItem>): void {
    layout.value = {
      widgets: layout.value.widgets.map(w => (w.id === id ? { ...w, ...patch } : w))
    }
  }

  function updateWidgetConfig(id: string, config: WidgetConfig): void {
    updateWidget(id, { config })
  }

  function updateLayoutCoords(
    coords: Array<{ id: string; x: number; y: number; w: number; h: number }>
  ): void {
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

  function startWidgetConfig(id: string): void {
    editingWidgetId.value = id
  }

  function finishWidgetConfig(): void {
    editingWidgetId.value = null
  }

  function openPicker(): void {
    if (isEditing.value) pickerOpen.value = true
  }

  function closePicker(): void {
    pickerOpen.value = false
  }

  return {
    layout: layout as Ref<DashboardLayout>,
    isEditing,
    editingWidgetId,
    pickerOpen,
    isDirty,

    applyPersisted,
    applyWorking,
    enterEdit,
    cancelEdit,
    ackSaved,

    addWidget,
    removeWidget,
    updateWidget,
    updateWidgetConfig,
    updateLayoutCoords,

    startWidgetConfig,
    finishWidgetConfig,
    openPicker,
    closePicker
  }
}
