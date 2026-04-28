const STORAGE_KEY = 'oteldash-sidebar-collapsed'

/**
 * Persistent collapsed state for the app sidebar. Shared across the SPA via
 * useState so the toggle in the sidebar footer affects every page that
 * mounts the shell.
 */
export function useSidebarState() {
  const collapsed = useState<boolean>('app-sidebar-collapsed', () => {
    if (import.meta.server) return false
    return window.localStorage.getItem(STORAGE_KEY) === '1'
  })

  function toggle() {
    collapsed.value = !collapsed.value
  }

  if (import.meta.client) {
    watch(collapsed, (value) => {
      window.localStorage.setItem(STORAGE_KEY, value ? '1' : '0')
    })
  }

  return { collapsed, toggle }
}
