<script setup lang="ts">
import { GridLayout, type Layout } from 'grid-layout-plus'
import type { WidgetItem } from '../types'
import WidgetSlot from './WidgetSlot.vue'

const props = defineProps<{
  widgets: WidgetItem[]
  isEditing: boolean
  liveTick: number
}>()

const emit = defineEmits<{
  'layout-change': [coords: Array<{ id: string; x: number; y: number; w: number; h: number }>]
  edit: [id: string]
  remove: [id: string]
}>()

// Local copy of the layout in grid-layout-plus' shape: { i, x, y, w, h }.
// We sync OUT to the parent on layout-updated, and sync IN whenever the
// widgets prop changes (e.g. add/remove, save reverts).
const layout = ref<Layout>([])

function widgetsToLayout(items: ReadonlyArray<WidgetItem>): Layout {
  return items.map(w => ({ i: w.id, x: w.x, y: w.y, w: w.w, h: w.h }))
}

function sameLayout(a: Layout, b: Layout): boolean {
  if (a.length !== b.length) return false
  // Order-independent compare keyed by `i`. GridLayout may re-emit the same
  // layout in a different order after compaction.
  const byId = new Map(a.map(l => [String(l.i), l]))
  for (const item of b) {
    const prev = byId.get(String(item.i))
    if (!prev) return false
    if (prev.x !== item.x || prev.y !== item.y || prev.w !== item.w || prev.h !== item.h) return false
  }
  return true
}

watch(() => props.widgets, items => {
  const next = widgetsToLayout(items)
  // Skip the assignment when the props-derived layout matches what GridLayout
  // already has — otherwise the new array reference triggers a fresh render
  // that re-emits `layout-updated`, which feeds back into the parent and
  // loops forever.
  if (sameLayout(layout.value, next)) return
  layout.value = next
}, { immediate: true, deep: true })

function onLayoutUpdated(next: Layout) {
  // grid-layout-plus emits `layout-updated` after every reactive pass —
  // including the no-op pass triggered by our own props sync. Compare against
  // the props' canonical layout and bail when nothing actually moved.
  const fromProps = widgetsToLayout(props.widgets)
  if (sameLayout(fromProps, next)) return
  emit('layout-change', next.map(l => ({
    id: String(l.i),
    x: l.x,
    y: l.y,
    w: l.w,
    h: l.h
  })))
}

const widgetById = computed(() => {
  const map = new Map<string, WidgetItem>()
  for (const w of props.widgets) map.set(w.id, w)
  return map
})
</script>

<template>
  <GridLayout
    v-model:layout="layout"
    :col-num="12"
    :row-height="60"
    :margin="[12, 12]"
    :is-draggable="isEditing"
    :is-resizable="isEditing"
    :vertical-compact="true"
    :use-css-transforms="true"
    @layout-updated="onLayoutUpdated"
  >
    <template #item="{ item }">
      <WidgetSlot
        v-if="widgetById.get(String(item.i))"
        :item="widgetById.get(String(item.i))!"
        :is-editing="isEditing"
        :live-tick="liveTick"
        @edit="emit('edit', String(item.i))"
        @remove="emit('remove', String(item.i))"
      />
    </template>
  </GridLayout>
</template>

<style>
/* Subtle drop-target outline while dragging. The library exposes the
   placeholder via .vgl-item--placeholder. */
.vgl-layout {
  background: transparent;
}
.vgl-item--placeholder {
  background: rgba(13, 148, 136, 0.18);
  border-radius: 0.5rem;
  opacity: 1 !important;
}
</style>
