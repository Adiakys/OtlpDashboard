export interface NavItem {
  /** i18n key used to look up the visible label (e.g. 'nav.dashboard'). */
  labelKey: string
  /** Iconify name (Lucide). */
  icon: string
  /** Target route. */
  to: string
}
