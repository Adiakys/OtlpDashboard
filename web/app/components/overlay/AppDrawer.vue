<script setup lang="ts">
const props = withDefaults(defineProps<{
  open: boolean
  /** Stable name used to persist width across reloads. */
  name: string
  title?: string
  defaultWidth?: number
  minWidth?: number
}>(), {
  defaultWidth: 480,
  minWidth: 360
})

const emit = defineEmits<{ 'update:open': [value: boolean] }>()

const { width, setWidth } = useDrawerWidth(props.name, props.defaultWidth, props.minWidth)
const isResizing = ref(false)

function startResize(e: MouseEvent) {
  e.preventDefault()
  isResizing.value = true
  const startX = e.clientX
  const startWidth = width.value

  function onMove(move: MouseEvent) {
    const dx = startX - move.clientX
    setWidth(startWidth + dx)
  }
  function onUp() {
    isResizing.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    document.body.style.userSelect = ''
    document.body.style.cursor = ''
  }

  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
  document.body.style.userSelect = 'none'
  document.body.style.cursor = 'col-resize'
}

function close() {
  emit('update:open', false)
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.open) close()
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="open"
        class="fixed inset-0 z-40"
        :style="{ background: 'oklch(0.115 0.006 40 / 0.55)' }"
        @click="close"
      />
    </Transition>
    <Transition name="drawer">
      <aside
        v-if="open"
        class="fixed inset-y-0 right-0 z-50 bg-default flex flex-col"
        :style="{
          width: `${width}px`,
          borderLeft: '1px solid color-mix(in oklab, var(--color-graphite-500) 22%, transparent)',
          boxShadow: 'var(--shadow-3), var(--shadow-inset-edge)'
        }"
        role="dialog"
        :aria-label="title"
      >
        <button
          type="button"
          class="vellum-drawer-handle"
          :class="isResizing ? 'vellum-drawer-handle--active' : ''"
          aria-label="Resize"
          @mousedown="startResize"
        />
        <header
          class="flex items-center justify-between gap-3 px-6 py-4"
          :style="{ borderBottom: '1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent)' }"
        >
          <h2 v-if="title" class="text-headline truncate">{{ title }}</h2>
          <slot name="header" />
          <UButton
            size="xs"
            color="neutral"
            variant="ghost"
            icon="i-ph-x"
            square
            class="vellum-tactile"
            @click="close"
          />
        </header>
        <div class="flex-1 min-h-0 overflow-y-auto p-6">
          <slot />
        </div>
        <footer
          v-if="$slots.footer"
          class="px-6 py-4"
          :style="{ borderTop: '1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent)' }"
        >
          <slot name="footer" />
        </footer>
      </aside>
    </Transition>
  </Teleport>
</template>

<style scoped>
.drawer-enter-active,
.drawer-leave-active {
  transition: transform var(--t-base) var(--ease-out), opacity var(--t-base) var(--ease-out);
}
.drawer-enter-from,
.drawer-leave-to {
  transform: translateX(100%);
  opacity: 0;
}

/* Resize handle: thin invisible bar that lights up ember on hover/drag. */
.vellum-drawer-handle {
  position: absolute;
  left: 0;
  top: 0;
  height: 100%;
  width: 4px;
  transform: translateX(-50%);
  cursor: col-resize;
  background: transparent;
  border: none;
  transition: background-color var(--t-instant) var(--ease-out);
}
.vellum-drawer-handle:hover {
  background: color-mix(in oklab, var(--color-ember-500) 40%, transparent);
}
.vellum-drawer-handle--active {
  background: var(--color-ember-500);
}

.vellum-tactile {
  transition:
    background-color var(--t-instant) var(--ease-out),
    color var(--t-instant) var(--ease-out),
    transform var(--t-instant) var(--ease-out);
}
.vellum-tactile:active:not(:disabled) {
  transform: translateY(1px);
}
</style>
