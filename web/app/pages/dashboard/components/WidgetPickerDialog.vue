<script setup lang="ts">
import { computed, ref } from 'vue'
import { useWidgetCatalog } from '../catalog'
import { WIDGET_REGISTRY } from '../registry'
import type { BuiltinKind, FQKind, WidgetDefinition } from '../types'
import { parseKind } from '../types'

defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  select: [kind: FQKind]
}>()

const { t } = useI18n()

const catalog = useWidgetCatalog()
const search = ref('')

const builtin = catalog.bySource('std')
const custom = catalog.bySource('custom')
const libraryGroups = catalog.byLibrary

/**
 * Display the i18n title key for builtin widgets and the user-saved name
 * for custom/library widgets. The static `STD_DEFINITIONS` carries the
 * bare kind in `name`, so we route through the registry's title key for a
 * properly localized label.
 */
function displayName(def: WidgetDefinition): string {
  if (def.source === 'std') {
    const parsed = parseKind(def.kind)
    if (parsed.id in WIDGET_REGISTRY) {
      return t(WIDGET_REGISTRY[parsed.id as BuiltinKind].titleKey)
    }
  }
  return def.name
}

function displayDescription(def: WidgetDefinition): string {
  if (def.source === 'std') {
    const parsed = parseKind(def.kind)
    if (parsed.id in WIDGET_REGISTRY) {
      return t(WIDGET_REGISTRY[parsed.id as BuiltinKind].descKey)
    }
  }
  return def.description ?? ''
}

function matchesSearch(def: WidgetDefinition): boolean {
  const q = search.value.trim().toLowerCase()
  if (!q) return true
  return displayName(def).toLowerCase().includes(q)
    || displayDescription(def).toLowerCase().includes(q)
}

const filteredBuiltin = computed(() => builtin.value.filter(matchesSearch))
const filteredCustom = computed(() => custom.value.filter(matchesSearch))
const filteredLibraries = computed(() => {
  const out: { id: string; widgets: WidgetDefinition[] }[] = []
  for (const [libId, list] of libraryGroups.value.entries()) {
    const matched = list.filter(matchesSearch)
    if (matched.length > 0) out.push({ id: libId, widgets: matched })
  }
  return out
})

function pick(def: WidgetDefinition) {
  emit('select', def.kind)
}
</script>

<template>
  <UModal
    :open="open"
    :title="t('dashboard.picker.title')"
    @update:open="(v) => emit('update:open', v)"
  >
    <template #body>
      <div class="flex flex-col gap-4">
        <!-- Search box. Mono input so monospace metric / instrument names
             paste cleanly without re-flowing. -->
        <UInput
          v-model="search"
          :placeholder="t('widgets.picker.search')"
          icon="i-ph-magnifying-glass"
          size="sm"
          autofocus
        />

        <!-- BUILTIN ------------------------------------------------ -->
        <section>
          <header class="mb-2 flex items-baseline justify-between">
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ t('widgets.picker.builtinSection') }}
            </span>
            <span class="text-mono-sm" style="color: var(--color-graphite-500);">
              {{ filteredBuiltin.length }}
            </span>
          </header>
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-2.5">
            <button
              v-for="def in filteredBuiltin"
              :key="def.kind"
              type="button"
              class="vellum-picker-card"
              @click="pick(def)"
            >
              <UIcon :name="def.icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
              <span class="font-medium text-sm truncate">{{ displayName(def) }}</span>
              <p class="vellum-picker-card__desc">{{ displayDescription(def) }}</p>
            </button>
          </div>
        </section>

        <!-- I MIEI WIDGET ----------------------------------------- -->
        <section>
          <header class="mb-2 flex items-baseline justify-between">
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ t('widgets.picker.customSection') }}
            </span>
            <span class="text-mono-sm" style="color: var(--color-graphite-500);">
              {{ filteredCustom.length }}
            </span>
          </header>
          <div
            v-if="filteredCustom.length === 0"
            class="text-mono-sm py-3 px-1"
            style="color: var(--color-graphite-500);"
          >
            {{ search.trim() ? t('widgets.picker.noMatch') : t('widgets.picker.customEmpty') }}
          </div>
          <div v-else class="grid grid-cols-2 sm:grid-cols-3 gap-2.5">
            <button
              v-for="def in filteredCustom"
              :key="def.kind"
              type="button"
              class="vellum-picker-card"
              @click="pick(def)"
            >
              <UIcon :name="def.icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
              <span class="font-medium text-sm truncate">{{ def.name }}</span>
              <p class="vellum-picker-card__desc">{{ def.description ?? '' }}</p>
            </button>
          </div>
        </section>

        <!-- LIBRERIE ----------------------------------------------- -->
        <section v-for="lib in filteredLibraries" :key="lib.id">
          <header class="mb-2 flex items-baseline justify-between">
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ lib.id }}
            </span>
            <span class="text-mono-sm" style="color: var(--color-graphite-500);">
              {{ lib.widgets.length }}
            </span>
          </header>
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-2.5">
            <button
              v-for="def in lib.widgets"
              :key="def.kind"
              type="button"
              class="vellum-picker-card"
              @click="pick(def)"
            >
              <UIcon :name="def.icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
              <span class="font-medium text-sm truncate">{{ def.name }}</span>
              <p class="vellum-picker-card__desc">{{ def.description ?? '' }}</p>
            </button>
          </div>
        </section>
      </div>
    </template>
  </UModal>
</template>

<style scoped>
.vellum-picker-card {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.75rem;
  text-align: left;
  background: transparent;
  border: 1px solid color-mix(in oklab, var(--color-graphite-500) 18%, transparent);
  border-radius: var(--radius-md);
  transition:
    border-color var(--t-instant) var(--ease-out),
    background-color var(--t-instant) var(--ease-out),
    transform var(--t-instant) var(--ease-out);
}
.vellum-picker-card:hover {
  border-color: color-mix(in oklab, var(--color-ember-500) 40%, transparent);
  background: color-mix(in oklab, var(--color-graphite-500) 5%, transparent);
}
.vellum-picker-card:active {
  transform: translateY(1px);
}
.vellum-picker-card:focus-visible {
  outline: 2px solid var(--color-ember-500);
  outline-offset: 2px;
}

.vellum-picker-card__desc {
  font-size: 0.72rem;
  line-height: 1.4;
  color: var(--color-graphite-500);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
