import type { NavItem } from '~/types/navigation'

/** Navigation entries shown in the app sidebar. Single source of truth. */
const items: NavItem[] = [
  { labelKey: 'nav.dashboard', icon: 'i-lucide-layout-dashboard', to: '/dashboard' },
  { labelKey: 'nav.traces', icon: 'i-lucide-waypoints', to: '/traces' },
  { labelKey: 'nav.logs', icon: 'i-lucide-file-text', to: '/logs' },
  { labelKey: 'nav.metrics', icon: 'i-lucide-chart-line', to: '/metrics' }
]

export function useNavigation() {
  return { items }
}
