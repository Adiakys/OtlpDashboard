<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch, type Component } from 'vue'
import { defaultConfigForDefinition, useWidgetCatalog } from '../catalog'
import { WIDGET_REGISTRY } from '../registry'
import InstallLibraryDialog from './InstallLibraryDialog.vue'
import SaveAsTemplateDialog from './SaveAsTemplateDialog.vue'
import type { BuiltinKind, FQKind, WidgetConfig, WidgetDefinition } from '../types'
import { parseKind } from '../types'

const props = defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  select: [kind: FQKind]
}>()

const { t } = useI18n()
const { $widgetService } = useNuxtApp()

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

/**
 * Search matches across name, description, and source attribution. The
 * source token is the first thing we check so users can type "std" /
 * "custom" / "<libraryId>" to filter a whole bucket at once.
 */
function matchesSearch(def: WidgetDefinition): boolean {
  const q = search.value.trim().toLowerCase()
  if (!q) return true
  const sourceLabel = typeof def.source === 'object' ? def.source.library : def.source
  if (sourceLabel.toLowerCase().includes(q)) return true
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

/**
 * Resolve the preview component + props for a definition. Only `preset`
 * widgets whose underlying builtin opted in via `hasPreview = true` get
 * a live miniature; everything else (custom widgets that wrap a kind
 * without preview, library spec/composite widgets) returns `null` and
 * the card falls back to icon + name + description.
 */
interface PreviewBinding {
  component: Component
  config: WidgetConfig
}

function resolvePreview(def: WidgetDefinition): PreviewBinding | null {
  if (def.engine !== 'preset' || !def.baseKind) return null
  const meta = WIDGET_REGISTRY[def.baseKind]
  if (!meta?.hasPreview) return null
  return {
    component: meta.component,
    // Custom and library presets bring their own seeded config, which can
    // make the mini look more representative; std widgets fall back to
    // the kind's default config.
    config: def.defaultConfig ?? defaultConfigForDefinition(def)
  }
}

function close() {
  emit('update:open', false)
}

// ----- Drag the panel around -----

// Translation applied to the panel relative to its initial centered
// position. Reset every time the modal re-opens so power users who drag
// it across the screen don't carry the offset over.
const offset = ref({ x: 0, y: 0 })
const isDragging = ref(false)
let dragStart: { mouseX: number; mouseY: number; startX: number; startY: number } | null = null

function startDrag(e: MouseEvent) {
  // Only react to the primary button so right-click / middle-click stay free.
  if (e.button !== 0) return
  // Don't start a drag from buttons or controls inside the header — those
  // need their own click handling.
  const target = e.target as HTMLElement | null
  if (target?.closest('button, input, [role="button"]')) return
  e.preventDefault()
  isDragging.value = true
  dragStart = {
    mouseX: e.clientX,
    mouseY: e.clientY,
    startX: offset.value.x,
    startY: offset.value.y
  }
  window.addEventListener('mousemove', onDrag)
  window.addEventListener('mouseup', endDrag)
  // Suppress text selection while dragging so the cursor doesn't flicker
  // between grabby and I-beam.
  document.body.style.userSelect = 'none'
}

function onDrag(e: MouseEvent) {
  if (!dragStart) return
  offset.value = {
    x: dragStart.startX + (e.clientX - dragStart.mouseX),
    y: dragStart.startY + (e.clientY - dragStart.mouseY)
  }
}

function endDrag() {
  isDragging.value = false
  dragStart = null
  window.removeEventListener('mousemove', onDrag)
  window.removeEventListener('mouseup', endDrag)
  document.body.style.userSelect = ''
}

// Reset position when the dialog re-opens — feels less surprising than
// "remember where I left it" when the user comes back from a different
// dashboard.
watch(() => props.open, (isOpen) => {
  if (isOpen) offset.value = { x: 0, y: 0 }
})

// Esc closes — same affordance UModal gave for free.
function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.open) close()
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
  // Defensive cleanup if the component unmounts mid-drag.
  window.removeEventListener('mousemove', onDrag)
  window.removeEventListener('mouseup', endDrag)
  document.body.style.userSelect = ''
})

const shellStyle = computed(() => ({
  // Compose the centering offset (-50%, -50%) with the user's drag delta
  // in a single transform. Two separate transforms can't co-exist on the
  // same element — the inline binding always wins, so we pre-fold them.
  transform: `translate(calc(-50% + ${offset.value.x}px), calc(-50% + ${offset.value.y}px))`
}))

// ----- Inline edit / delete on custom cards -----

const editingDef = ref<WidgetDefinition | null>(null)
const editOpen = ref(false)
const isDeleting = ref<string | null>(null)
const error = ref<string | null>(null)

function startEdit(def: WidgetDefinition, e: Event) {
  // Stop the click from bubbling to the card (which would emit 'select').
  e.stopPropagation()
  editingDef.value = def
  editOpen.value = true
}

async function deleteCustom(def: WidgetDefinition, e: Event) {
  e.stopPropagation()
  if (isDeleting.value) return
  if (!confirm(t('widgets.deleteConfirm'))) return
  const id = parseKind(def.kind).id
  isDeleting.value = id
  error.value = null
  try {
    await $widgetService.deleteCustom(id)
    await catalog.refreshCustom()
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    isDeleting.value = null
  }
}

function customId(kind: FQKind): string {
  return parseKind(kind).id
}

// ----- Reload libraries -----

const isReloadingLibraries = ref(false)

async function reloadLibraries() {
  if (isReloadingLibraries.value) return
  isReloadingLibraries.value = true
  error.value = null
  try {
    await $widgetService.reloadLibraries()
    await catalog.refreshLibraries()
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    isReloadingLibraries.value = false
  }
}

// ----- Uninstall library -----

const isUninstalling = ref<string | null>(null)

async function uninstallLibrary(libId: string) {
  if (isUninstalling.value) return
  if (!confirm(t('widgets.picker.uninstallLibraryConfirm', { id: libId }))) return
  isUninstalling.value = libId
  error.value = null
  try {
    await $widgetService.uninstallLibrary(libId)
    await catalog.refreshLibraries()
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    isUninstalling.value = null
  }
}

// ----- Install / update from git -----

const installDialogOpen = ref(false)
const isUpdating = ref<string | null>(null)

async function onInstalled() {
  // The install endpoint returned 201; still hit refreshLibraries() so
  // the picker reflects the freshly registered DTO (including
  // `installSource: 'Git'` which gates the Update button).
  await catalog.refreshLibraries()
}

async function updateLibrary(libId: string) {
  if (isUpdating.value) return
  isUpdating.value = libId
  error.value = null
  try {
    await $widgetService.updateLibrary(libId)
    await catalog.refreshLibraries()
  } catch (err) {
    const detail = (err as { data?: { detail?: unknown } } | undefined)?.data?.detail
    error.value = typeof detail === 'string'
      ? detail
      : (err instanceof Error ? err.message : String(err))
  } finally {
    isUpdating.value = null
  }
}
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="open"
        class="vellum-picker-overlay"
        @mousedown.self="close"
      />
    </Transition>
    <Transition name="picker-pop">
      <div
        v-if="open"
        class="vellum-picker-shell bg-default text-default"
        :class="{ 'vellum-picker-shell--dragging': isDragging }"
        :style="shellStyle"
        role="dialog"
        :aria-label="t('dashboard.picker.title')"
      >
        <header
          class="vellum-picker-headbar"
          @mousedown="startDrag"
        >
          <UIcon name="i-ph-dots-six" class="size-4 shrink-0 vellum-picker-grip" />
          <h2 class="text-headline truncate flex-1">{{ t('dashboard.picker.title') }}</h2>
          <UButton
            size="xs"
            color="neutral"
            variant="ghost"
            icon="i-ph-x"
            square
            :aria-label="t('common.close')"
            @click="close"
          />
        </header>

        <div class="vellum-picker-body">
          <div class="flex items-center gap-2">
            <UInput
              v-model="search"
              class="flex-1"
              :placeholder="t('widgets.picker.search')"
              icon="i-ph-magnifying-glass"
              size="sm"
              autofocus
            />
            <UButton
              color="neutral"
              variant="outline"
              size="sm"
              icon="i-ph-cloud-arrow-down"
              :label="t('widgets.picker.installFromGit')"
              @click="installDialogOpen = true"
            />
            <UButton
              color="neutral"
              variant="ghost"
              size="sm"
              icon="i-ph-arrows-clockwise"
              :loading="isReloadingLibraries"
              :aria-label="t('widgets.picker.reloadLibraries')"
              @click="reloadLibraries"
            />
          </div>

          <p
            v-if="error"
            role="alert"
            class="text-mono-sm"
            style="color: var(--color-rust-700);"
          >
            {{ error }}
          </p>

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
            <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2.5">
              <button
                v-for="def in filteredBuiltin"
                :key="def.kind"
                type="button"
                class="vellum-picker-card"
                @click="pick(def)"
              >
                <div v-if="resolvePreview(def)" class="vellum-picker-card__preview">
                  <component
                    :is="resolvePreview(def)!.component"
                    :config="resolvePreview(def)!.config"
                    :is-editing="false"
                    :live-tick="0"
                    :preview="true"
                  />
                </div>
                <div class="vellum-picker-card__head">
                  <UIcon :name="def.icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
                  <span class="font-medium text-sm truncate">{{ displayName(def) }}</span>
                </div>
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
            <div v-else class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2.5">
              <button
                v-for="def in filteredCustom"
                :key="def.kind"
                type="button"
                class="vellum-picker-card vellum-picker-card--actionable"
                @click="pick(def)"
              >
                <div v-if="resolvePreview(def)" class="vellum-picker-card__preview">
                  <component
                    :is="resolvePreview(def)!.component"
                    :config="resolvePreview(def)!.config"
                    :is-editing="false"
                    :live-tick="0"
                    :preview="true"
                  />
                </div>
                <div class="vellum-picker-card__head">
                  <UIcon :name="def.icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
                  <span class="font-medium text-sm truncate flex-1">{{ def.name }}</span>
                  <span class="vellum-picker-card__actions">
                    <UButton
                      color="neutral"
                      variant="ghost"
                      size="xs"
                      icon="i-ph-pencil-simple"
                      square
                      :aria-label="t('widgets.edit.action')"
                      @click="(e) => startEdit(def, e)"
                    />
                    <UButton
                      color="error"
                      variant="ghost"
                      size="xs"
                      icon="i-ph-trash"
                      square
                      :loading="isDeleting === customId(def.kind)"
                      :disabled="isDeleting !== null"
                      :aria-label="t('widgets.delete')"
                      @click="(e) => deleteCustom(def, e)"
                    />
                  </span>
                </div>
                <p class="vellum-picker-card__desc">{{ def.description ?? '' }}</p>
              </button>
            </div>
          </section>

          <!-- LIBRERIE ----------------------------------------------- -->
          <section v-for="lib in filteredLibraries" :key="lib.id">
            <header class="mb-2 flex items-baseline justify-between gap-2">
              <span class="text-overline truncate" style="color: var(--color-graphite-500);">
                {{ lib.id }}
              </span>
              <span class="flex items-center gap-1.5">
                <span class="text-mono-sm" style="color: var(--color-graphite-500);">
                  {{ lib.widgets.length }}
                </span>
                <UButton
                  v-if="catalog.libraryById(lib.id)?.installSource === 'Git'"
                  color="neutral"
                  variant="ghost"
                  size="xs"
                  icon="i-ph-arrow-clockwise"
                  square
                  :loading="isUpdating === lib.id"
                  :disabled="isUpdating !== null"
                  :aria-label="t('widgets.picker.updateLibrary')"
                  @click="updateLibrary(lib.id)"
                />
                <UButton
                  v-if="catalog.libraryById(lib.id)?.removable"
                  color="error"
                  variant="ghost"
                  size="xs"
                  icon="i-ph-trash"
                  square
                  :loading="isUninstalling === lib.id"
                  :disabled="isUninstalling !== null"
                  :aria-label="t('widgets.picker.uninstallLibrary')"
                  @click="uninstallLibrary(lib.id)"
                />
              </span>
            </header>
            <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2.5">
              <button
                v-for="def in lib.widgets"
                :key="def.kind"
                type="button"
                class="vellum-picker-card"
                @click="pick(def)"
              >
                <div v-if="resolvePreview(def)" class="vellum-picker-card__preview">
                  <component
                    :is="resolvePreview(def)!.component"
                    :config="resolvePreview(def)!.config"
                    :is-editing="false"
                    :live-tick="0"
                    :preview="true"
                  />
                </div>
                <div class="vellum-picker-card__head">
                  <UIcon :name="def.icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
                  <span class="font-medium text-sm truncate">{{ def.name }}</span>
                </div>
                <p class="vellum-picker-card__desc">{{ def.description ?? '' }}</p>
              </button>
            </div>
          </section>
        </div>
      </div>
    </Transition>
  </Teleport>

  <SaveAsTemplateDialog
    v-model:open="editOpen"
    :existing="editingDef ?? undefined"
  />

  <InstallLibraryDialog
    v-model:open="installDialogOpen"
    @installed="onInstalled"
  />
</template>

<style scoped>
/* Click-to-close layer behind the panel. Fully transparent — the
   dashboard underneath stays visible at rest. The shell itself supplies
   the solid background; only it dims while the user drags it.
   z-index sits below Nuxt UI's `UModal` (z-50) so dialogs spawned from
   inside the picker (Save-as-template, Install-from-git) stack on top. */
.vellum-picker-overlay {
  position: fixed;
  inset: 0;
  z-index: 30;
  background: transparent;
}

/* The draggable shell. Centered initially via flex-pinned anchor, then
   shifted via `transform: translate(...)` while the user drags. */
.vellum-picker-shell {
  position: fixed;
  /* Anchor at the viewport centre; the inline `transform` (set in JS)
     folds the centering offset (-50%, -50%) with the drag delta. */
  top: 50%;
  left: 50%;
  /* Sits below Nuxt UI's `UModal` (z-50) — see overlay rule above. */
  z-index: 40;
  display: flex;
  flex-direction: column;
  width: min(1100px, 92vw);
  max-height: 85vh;
  /* Background + text colour come from the `bg-default text-default`
     utility classes on the element so they track theme switches; this
     rule only owns layout and the drag-time opacity dimming. */
  border: 1px solid color-mix(in oklab, var(--color-graphite-500) 22%, transparent);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-3), var(--shadow-inset-edge);
  overflow: hidden;
  transition: opacity var(--t-instant) var(--ease-out);
}

/* While dragging, the panel turns semi-transparent so the user can see
   the dashboard underneath and judge where to drop it. */
.vellum-picker-shell--dragging {
  opacity: 0.55;
  transition: opacity 0ms;
}

.vellum-picker-headbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  cursor: grab;
  user-select: none;
  border-bottom: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
}
.vellum-picker-headbar:active {
  cursor: grabbing;
}
.vellum-picker-grip {
  color: var(--color-graphite-500);
}

.vellum-picker-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

/* The shell uses `transform` for both centering and drag offset, so we
   can't rely on a plain `transform` transition (it would fight the
   inline style). Animate opacity + scale via a wrapper-like trick. */
.picker-pop-enter-active,
.picker-pop-leave-active {
  transition: opacity var(--t-base) var(--ease-out);
}
.picker-pop-enter-from,
.picker-pop-leave-to {
  opacity: 0;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity var(--t-base) var(--ease-out);
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

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

.vellum-picker-card__head {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  min-width: 0;
}

/* Preview surface: fixed-size canvas that hosts the widget runtime in
   preview mode. Aspect ratio chosen to suit the most common widget
   shapes (stat, line, gauge); individual previews use 100% w/h via
   `.vellum-widget-preview` so they fill the box. */
.vellum-picker-card__preview {
  width: 100%;
  aspect-ratio: 16 / 7;
  margin: -0.25rem -0.25rem 0.25rem -0.25rem;
  background: color-mix(in oklab, var(--color-graphite-500) 6%, transparent);
  border-radius: var(--radius-sm);
  overflow: hidden;
  display: flex;
  /* Click bubbles up to the card; child preview is `pointer-events: none`. */
  pointer-events: none;
}

/* Inline action buttons fade in on hover so the card stays calm at rest. */
.vellum-picker-card__actions {
  display: flex;
  align-items: center;
  gap: 0.125rem;
  margin-left: auto;
  opacity: 0;
  transition: opacity var(--t-instant) var(--ease-out);
}
.vellum-picker-card--actionable:hover .vellum-picker-card__actions,
.vellum-picker-card--actionable:focus-within .vellum-picker-card__actions {
  opacity: 1;
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
