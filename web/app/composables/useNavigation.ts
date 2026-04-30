import type { NavItem } from '~/types/navigation'

/** Navigation entries shown in the app sidebar. Single source of truth. */
const items: NavItem[] = [
  { labelKey: 'nav.dashboard', icon: 'i-ph-squares-four', to: '/dashboard' },
  { labelKey: 'nav.traces', icon: 'i-ph-tree-structure', to: '/traces' },
  { labelKey: 'nav.logs', icon: 'i-ph-file-text', to: '/logs' },
  { labelKey: 'nav.metrics', icon: 'i-ph-chart-line', to: '/metrics' }
]

export function useNavigation() {
  return { items }
}
