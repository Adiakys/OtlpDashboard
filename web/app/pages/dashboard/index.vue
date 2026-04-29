<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'
import CreateDashboardDialog from './components/CreateDashboardDialog.vue'
import DashboardEditToolbar from './components/DashboardEditToolbar.vue'
import DashboardGrid from './components/DashboardGrid.vue'
import DashboardSelector from './components/DashboardSelector.vue'
import WidgetConfigDrawer from './components/WidgetConfigDrawer.vue'
import WidgetPickerDialog from './components/WidgetPickerDialog.vue'
import { useDashboardPage } from './usePage'
import type { ActionDescriptor } from '~/types/toolbar'

const { t, locale } = useI18n()
const { $dashboardService, $metricsService } = useNuxtApp()
const page = useDashboardPage($dashboardService, $metricsService)

// Dashboard switching guards. Switching with unsaved edits would silently
// drop them, so we route the request through a confirmation dialog.
const pendingSwitchId = ref<string | null>(null)
const switchConfirmOpen = computed({
  get: () => pendingSwitchId.value !== null,
  set: (v: boolean) => { if (!v) pendingSwitchId.value = null }
})

function onSelectDashboard(id: string) {
  if (page.isDirty.value && page.isEditing.value) {
    pendingSwitchId.value = id
    return
  }
  void page.selectDashboard(id)
}

function confirmSwitch() {
  const id = pendingSwitchId.value
  pendingSwitchId.value = null
  if (id) void page.selectDashboard(id)
}

// Create-dashboard dialog
const createOpen = ref(false)
const isCreating = ref(false)

async function onCreateDashboard(name: string) {
  isCreating.value = true
  try {
    const created = await page.createDashboard(name)
    if (created) createOpen.value = false
  } finally {
    isCreating.value = false
  }
}

// Delete-dashboard confirmation
const deleteConfirmOpen = ref(false)
const isDeleting = ref(false)

async function onDeleteConfirmed() {
  if (!page.isCurrentDeletable.value) return
  isDeleting.value = true
  try {
    const ok = await page.deleteCurrentDashboard()
    if (ok) deleteConfirmOpen.value = false
  } finally {
    isDeleting.value = false
  }
}

const currentDashboardName = computed(() => page.dashboard.value?.name ?? '')

const actions = computed<ActionDescriptor[]>(() => {
  if (page.isEditing.value) {
    // Edit-mode actions live in the #filters-extra slot via DashboardEditToolbar.
    // The standard toolbar actions stay empty here so live/refresh icons don't
    // crowd the save/cancel buttons.
    return []
  }
  return [
    { kind: 'refresh', loading: page.isLoading, disabled: page.isLive, onClick: () => page.reload() },
    { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive },
    {
      kind: 'custom',
      labelKey: 'dashboard.actions.edit',
      icon: 'i-lucide-pencil',
      onClick: () => page.enterEdit(),
      variant: 'subtle',
      color: 'primary'
    }
  ]
})

const subtitle = computed(() => {
  if (page.isEditing.value) return t('dashboard.subtitle.editing')
  const updatedRaw = page.dashboard.value?.updatedAt
  const updatedLabel = updatedRaw
    ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(updatedRaw))
    : '—'
  return t('dashboard.subtitle.view', {
    count: page.layout.value.widgets.length,
    updated: updatedLabel
  })
})

const editingWidget = computed(() => {
  const id = page.editingWidgetId.value
  if (!id) return null
  return page.layout.value.widgets.find(w => w.id === id) ?? null
})

const drawerOpen = computed({
  get: () => editingWidget.value !== null,
  set: (v: boolean) => { if (!v) page.finishWidgetConfig() }
})

// Keyboard shortcuts active while editing the dashboard.
//   Cmd/Ctrl + S → save (only when there are pending changes)
//   Esc          → cancel edit (only when no inner overlay is open;
//                   the inner USlideover/UModal trap Esc themselves and we
//                   don't want a single keystroke to dismiss two layers).
function onKeyDown(e: KeyboardEvent) {
  if (!page.isEditing.value) return
  const meta = e.metaKey || e.ctrlKey
  if (meta && e.key.toLowerCase() === 's') {
    e.preventDefault()
    if (page.isDirty.value && !page.isSaving.value) void page.save()
    return
  }
  if (e.key === 'Escape') {
    const overlayOpen = drawerOpen.value || page.pickerOpen.value || createOpen.value
      || deleteConfirmOpen.value || switchConfirmOpen.value
    if (overlayOpen) return
    e.preventDefault()
    page.cancelEdit()
  }
}

onMounted(() => {
  if (typeof window !== 'undefined') window.addEventListener('keydown', onKeyDown)
})
onBeforeUnmount(() => {
  if (typeof window !== 'undefined') window.removeEventListener('keydown', onKeyDown)
})
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :title="t('dashboard.title')"
        :subtitle="subtitle"
        :actions="actions"
      >
        <template #filters-extra>
          <DashboardSelector
            :dashboards="page.dashboards.value"
            :current-id="page.currentDashboardId.value"
            @change="onSelectDashboard"
          />
          <DashboardEditToolbar
            v-if="page.isEditing.value"
            :is-dirty="page.isDirty.value"
            :is-saving="page.isSaving.value"
            :can-delete-dashboard="page.isCurrentDeletable.value"
            @add-widget="page.openPicker"
            @add-dashboard="createOpen = true"
            @delete-dashboard="deleteConfirmOpen = true"
            @save="page.save"
            @cancel="page.cancelEdit"
            @export-layout="page.exportLayout"
            @import-file="(file) => page.importLayout(file)"
          />
        </template>
      </AppToolbar>
    </template>

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-lucide-alert-triangle"
      :title="page.error.value"
      class="mb-4"
    />

    <AppEmptyState
      v-if="!page.isEditing.value && page.layout.value.widgets.length === 0"
      icon="i-lucide-layout-dashboard"
      :title="t('dashboard.emptyTitle')"
      :description="t('dashboard.emptyDescription')"
    >
      <template #actions>
        <UButton color="primary" icon="i-lucide-pencil" @click="page.enterEdit">
          {{ t('dashboard.startEditing') }}
        </UButton>
      </template>
    </AppEmptyState>

    <div v-else class="flex-1 min-h-0 overflow-y-auto -mx-6 px-6">
      <DashboardGrid
        :widgets="page.layout.value.widgets"
        :is-editing="page.isEditing.value"
        :live-tick="page.liveTickCounter.value"
        @layout-change="page.updateLayoutCoords"
        @edit="page.startWidgetConfig"
        @remove="page.removeWidget"
      />
    </div>

    <WidgetPickerDialog
      :open="page.pickerOpen.value"
      @update:open="(v) => v ? page.openPicker() : page.closePicker()"
      @select="(kind) => page.addWidget(kind)"
    />

    <WidgetConfigDrawer
      v-if="editingWidget"
      :widget="editingWidget"
      :open="drawerOpen"
      @update:open="(v) => drawerOpen = v"
      @apply="(c) => page.updateWidgetConfig(editingWidget!.id, c)"
    />

    <CreateDashboardDialog
      :open="createOpen"
      :is-creating="isCreating"
      @update:open="(v) => createOpen = v"
      @create="onCreateDashboard"
    />

    <ConfirmDialog
      :open="deleteConfirmOpen"
      :title="t('dashboard.delete.title')"
      :message="t('dashboard.delete.message', { name: currentDashboardName })"
      :confirm-label="t('dashboard.delete.submit')"
      destructive
      :busy="isDeleting"
      @update:open="(v) => deleteConfirmOpen = v"
      @confirm="onDeleteConfirmed"
    />

    <ConfirmDialog
      :open="switchConfirmOpen"
      :title="t('dashboard.switchConfirm.title')"
      :message="t('dashboard.switchConfirm.message')"
      :confirm-label="t('dashboard.switchConfirm.submit')"
      @update:open="(v) => switchConfirmOpen = v"
      @confirm="confirmSwitch"
    />
  </AppPage>
</template>
