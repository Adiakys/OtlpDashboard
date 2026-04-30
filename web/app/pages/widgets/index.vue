<script setup lang="ts">
import { computed, ref } from 'vue'
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import { useWidgetCatalog } from '~/pages/dashboard/catalog'
import { WIDGET_REGISTRY } from '~/pages/dashboard/registry'
import type { BuiltinKind } from '~/pages/dashboard/types'

const { t } = useI18n()
const catalog = useWidgetCatalog()

// Pre-fetch the catalog on mount so the lists render with fresh data even
// if the user navigated here directly without first opening /dashboard.
onMounted(async () => {
  if (!catalog.hydrated.value) {
    await catalog.refreshCustom()
  }
})

const customDefs = catalog.bySource('custom')

const isDeleting = ref<string | null>(null)
const error = ref<string | null>(null)

async function refresh() {
  error.value = null
  try {
    await catalog.refreshCustom()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

async function deleteCustom(id: string) {
  if (isDeleting.value) return
  if (!confirm(t('widgets.deleteConfirm'))) return
  isDeleting.value = id
  error.value = null
  try {
    const { $widgetService } = useNuxtApp()
    // The id encoded in `kind` is `custom:<uuid>` — strip the prefix for
    // the REST call which addresses the row by raw UUID.
    await $widgetService.deleteCustom(id)
    await catalog.refreshCustom()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    isDeleting.value = null
  }
}

function customId(kind: string): string {
  // `custom:<uuid>` → `<uuid>`
  const colon = kind.indexOf(':')
  return colon < 0 ? kind : kind.slice(colon + 1)
}

function baseKindLabel(baseKind: string | undefined): string {
  if (!baseKind) return '—'
  if (baseKind in WIDGET_REGISTRY) {
    return t(WIDGET_REGISTRY[baseKind as BuiltinKind].titleKey)
  }
  return baseKind
}

const subtitle = computed(() =>
  t('widgets.subtitle', { count: customDefs.value.length })
)
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :title="t('widgets.title')"
        :subtitle="subtitle"
        :actions="[
          { kind: 'refresh', loading: ref(false), onClick: refresh }
        ]"
      />
    </template>

    <UAlert
      v-if="error"
      color="error"
      variant="subtle"
      icon="i-ph-warning"
      :title="error"
      class="mb-4"
    />

    <AppEmptyState
      v-if="customDefs.length === 0"
      icon="i-ph-puzzle-piece"
      :title="t('widgets.emptyTitle')"
      :description="t('widgets.emptyDescription')"
    />

    <ul v-else class="vellum-widget-list">
      <li
        v-for="def in customDefs"
        :key="def.kind"
        class="vellum-widget-row"
      >
        <UIcon :name="def.icon" class="size-5 shrink-0" style="color: var(--color-ember-500);" />

        <div class="min-w-0 flex-1">
          <div class="flex items-baseline gap-2">
            <span class="text-title truncate">{{ def.name }}</span>
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ baseKindLabel(def.baseKind) }}
            </span>
          </div>
          <p
            v-if="def.description"
            class="text-mono-sm truncate"
            style="color: var(--color-graphite-500);"
          >
            {{ def.description }}
          </p>
        </div>

        <div class="text-mono-sm shrink-0" style="color: var(--color-graphite-500);">
          {{ def.defaultSize.w }}×{{ def.defaultSize.h }}
        </div>

        <UButton
          color="error"
          variant="ghost"
          size="sm"
          icon="i-ph-trash"
          :loading="isDeleting === customId(def.kind)"
          :disabled="isDeleting !== null"
          :aria-label="t('widgets.delete')"
          @click="deleteCustom(customId(def.kind))"
        />
      </li>
    </ul>

    <p
      class="mt-6 text-mono-sm"
      style="color: var(--color-graphite-500);"
    >
      {{ t('widgets.howToCreate') }}
    </p>
  </AppPage>
</template>

<style scoped>
.vellum-widget-list {
  display: flex;
  flex-direction: column;
  border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
}
.vellum-widget-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 0.5rem;
  border-bottom: 1px solid color-mix(in oklab, var(--color-graphite-500) 10%, transparent);
}
</style>
