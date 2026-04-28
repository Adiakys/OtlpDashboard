<script setup lang="ts">
const props = withDefaults(defineProps<{
  /** Stable name used to persist split ratio across reloads. */
  name: string
  /** 'horizontal' splits left/right (default), 'vertical' splits top/bottom. */
  orientation?: 'horizontal' | 'vertical'
  defaultRatio?: number
  minRatio?: number
  maxRatio?: number
}>(), {
  orientation: 'horizontal',
  defaultRatio: 0.5,
  minRatio: 0.2,
  maxRatio: 0.8
})

const { ratio, setRatio } = useSplitRatio(props.name, props.defaultRatio, props.minRatio, props.maxRatio)
const root = ref<HTMLElement | null>(null)
const isDragging = ref(false)

function startDrag(e: MouseEvent) {
  e.preventDefault()
  if (!root.value) return
  isDragging.value = true
  const rect = root.value.getBoundingClientRect()

  function onMove(move: MouseEvent) {
    if (!root.value) return
    const r = root.value.getBoundingClientRect()
    const value = props.orientation === 'horizontal'
      ? (move.clientX - r.left) / r.width
      : (move.clientY - r.top) / r.height
    setRatio(value)
  }
  function onUp() {
    isDragging.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    document.body.style.userSelect = ''
    document.body.style.cursor = ''
  }

  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
  document.body.style.userSelect = 'none'
  document.body.style.cursor = props.orientation === 'horizontal' ? 'col-resize' : 'row-resize'
  void rect
}

const firstSize = computed(() => `${ratio.value * 100}%`)
const secondSize = computed(() => `${(1 - ratio.value) * 100}%`)
</script>

<template>
  <div
    ref="root"
    class="flex-1 min-h-0 min-w-0 flex"
    :class="orientation === 'horizontal' ? 'flex-row' : 'flex-col'"
  >
    <div
      class="min-h-0 min-w-0 overflow-hidden"
      :style="orientation === 'horizontal' ? { width: firstSize } : { height: firstSize }"
    >
      <slot name="first" />
    </div>
    <button
      type="button"
      aria-label="Resize"
      class="shrink-0 group transition-colors"
      :class="[
        orientation === 'horizontal' ? 'w-1 cursor-col-resize hover:bg-primary/40' : 'h-1 cursor-row-resize hover:bg-primary/40',
        isDragging ? 'bg-primary/60' : 'bg-default'
      ]"
      @mousedown="startDrag"
    />
    <div
      class="min-h-0 min-w-0 overflow-hidden"
      :style="orientation === 'horizontal' ? { width: secondSize } : { height: secondSize }"
    >
      <slot name="second" />
    </div>
  </div>
</template>
