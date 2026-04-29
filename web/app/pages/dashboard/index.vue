<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import DashboardEditToolbar from './components/DashboardEditToolbar.vue'
import DashboardGrid from './components/DashboardGrid.vue'
import WidgetConfigDrawer from './components/WidgetConfigDrawer.vue'
import WidgetPickerDialog from './components/WidgetPickerDialog.vue'
import { useDashboardPage } from './usePage'
import type { ActionDescriptor } from '~/types/toolbar'

const { t, locale } = useI18n()
const { $dashboardService } = useNuxtApp()
const page = useDashboardPage($dashboardService)

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
          <DashboardEditToolbar
            v-if="page.isEditing.value"
            :is-dirty="page.isDirty.value"
            :is-saving="page.isSaving.value"
            @add-widget="page.openPicker"
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

    <div v-else class="min-h-0">
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
  </AppPage>
</template>
