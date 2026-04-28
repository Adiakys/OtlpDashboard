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
        class="fixed inset-0 z-40 bg-black/30"
        @click="close"
      />
    </Transition>
    <Transition name="drawer">
      <aside
        v-if="open"
        class="fixed inset-y-0 right-0 z-50 bg-default border-l border-default shadow-xl flex flex-col"
        :style="{ width: `${width}px` }"
        role="dialog"
        :aria-label="title"
      >
        <button
          type="button"
          class="absolute left-0 top-0 h-full w-1.5 -translate-x-1/2 cursor-col-resize bg-transparent hover:bg-primary/40 transition-colors"
          :class="isResizing ? 'bg-primary/60' : ''"
          aria-label="Resize"
          @mousedown="startResize"
        />
        <header class="flex items-center justify-between gap-3 px-5 py-3 border-b border-default">
          <h2 v-if="title" class="text-title truncate">{{ title }}</h2>
          <slot name="header" />
          <UButton
            size="xs"
            color="neutral"
            variant="ghost"
            icon="i-lucide-x"
            square
            @click="close"
          />
        </header>
        <div class="flex-1 min-h-0 overflow-y-auto p-5">
          <slot />
        </div>
        <footer v-if="$slots.footer" class="border-t border-default px-5 py-3">
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
</style>
