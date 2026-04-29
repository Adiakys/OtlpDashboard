import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'

/**
 * Reactive width/height of a DOM element via ResizeObserver. Mirrors the
 * VueUse helper of the same name; defined locally because the project doesn't
 * pull in VueUse just for this. Returns zeros on the server (no DOM).
 *
 * The element ref is taken as a getter so callers can pass `() => el.value`
 * without losing reactivity when the template ref resolves later.
 */
export function useElementSize(target: () => HTMLElement | null) {
  const width = ref(0)
  const height = ref(0)
  let observer: ResizeObserver | null = null

  function attach(el: HTMLElement) {
    observer?.disconnect()
    observer = new ResizeObserver(entries => {
      const cr = entries[0]?.contentRect
      if (!cr) return
      width.value = cr.width
      height.value = cr.height
    })
    observer.observe(el)
    // Seed values synchronously so the first render doesn't see (0, 0).
    const rect = el.getBoundingClientRect()
    width.value = rect.width
    height.value = rect.height
  }

  onMounted(() => {
    const el = target()
    if (el) attach(el)
  })

  onBeforeUnmount(() => {
    observer?.disconnect()
    observer = null
  })

  return { width: width as Readonly<Ref<number>>, height: height as Readonly<Ref<number>> }
}
