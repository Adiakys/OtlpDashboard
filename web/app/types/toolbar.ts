import type { Ref } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import type { TimeWindow } from '~/services/types'
import type { DurationRange, SeverityBucket, TraceStatusFilter } from './filters'

/**
 * Declarative description of a toolbar filter. Pages build an array of these
 * and pass it to <AppToolbar :filters="..." />, which knows how to render
 * each kind. Custom filters go in the #filters-extra slot instead.
 */
export type FilterDescriptor =
  | ApplicationFilterDescriptor
  | TimeRangeFilterDescriptor
  | LimitFilterDescriptor
  | SeverityFilterDescriptor
  | StatusFilterDescriptor
  | DurationFilterDescriptor
  | SearchFilterDescriptor
  | AttributesFilterDescriptor

export interface ApplicationFilterDescriptor {
  kind: 'application'
  /** Allow-list of `service.name` values; an empty array means "all
   *  applications" (no filter). The component renders an "All" pseudo
   *  option that resets to `[]`. */
  modelValue: Ref<string[]>
  options: Ref<string[]>
  /** Optional any-span match toggle. Bound only by pages that want
   *  to expose the discovery alternative ("traces that touch X
   *  anywhere"); when unset the picker hides the toggle and the
   *  filter stays root-anchored. */
  matchMode?: Ref<'root' | 'any'>
  disabled?: Ref<boolean>
}

export interface TimeRangeFilterDescriptor {
  kind: 'time-range'
  modelValue: Ref<TimeWindow>
  /**
   * Active rolling preset key (e.g. `'1h'`), or `null` for a custom
   * window. Optional — pages that want their time filter to roll across
   * back-navigation bind this so the URL can persist `?range=1h`
   * instead of a frozen pair of timestamps. Omitted by pages where
   * the time window is always absolute.
   */
  preset?: Ref<string | null>
  disabled?: Ref<boolean>
  /**
   * Server-configured retention window for the data this picker
   * targets. When present and > 0, the picker surfaces an "info" icon
   * inside the popover with a tooltip explaining the cutoff. Pages
   * source it from `$telemetryLimits.maxLogDays` / `maxTraceDays` /
   * `maxMetricDays` after the `/v1/info` call. `null` for
   * unauthenticated states.
   */
  retentionDays?: Ref<number | null>
  /**
   * Maximum query window (in hours, as the server reports it) the
   * read-side API will honour. Same auth gate as `retentionDays`.
   * Pages pass `$queryMaxWindowHours` directly — the picker converts
   * to days internally so the unit mismatch doesn't leak across the
   * three feature pages.
   */
  maxWindowHours?: Ref<number | null>
}

export interface LimitFilterDescriptor {
  kind: 'limit'
  modelValue: Ref<number>
  options?: number[]
  disabled?: Ref<boolean>
}

export interface SeverityFilterDescriptor {
  kind: 'severity'
  modelValue: Ref<SeverityBucket[]>
  disabled?: Ref<boolean>
}

export interface StatusFilterDescriptor {
  kind: 'status'
  modelValue: Ref<TraceStatusFilter>
  disabled?: Ref<boolean>
}

export interface DurationFilterDescriptor {
  kind: 'duration'
  modelValue: Ref<DurationRange>
  disabled?: Ref<boolean>
}

export interface SearchFilterDescriptor {
  kind: 'search'
  modelValue: Ref<string>
  placeholder?: string
  disabled?: Ref<boolean>
}

/**
 * Multi-value attribute filter. Each entry is a `key:value` string
 * (string-typed match — see `PageQuery.attr`). Pages pass a writeable
 * ref the picker mutates directly when the user adds or removes a
 * pair via the popover.
 */
export interface AttributesFilterDescriptor {
  kind: 'attributes'
  modelValue: Ref<string[]>
  disabled?: Ref<boolean>
}

export type ActionDescriptor =
  | RefreshActionDescriptor
  | LiveActionDescriptor
  | CustomActionDescriptor
  | SplitActionDescriptor

export interface RefreshActionDescriptor {
  kind: 'refresh'
  loading: Ref<boolean>
  disabled?: Ref<boolean>
  onClick: () => void
}

export interface LiveActionDescriptor {
  kind: 'live'
  isLive: Ref<boolean>
  onToggle: () => void
}

export interface CustomActionDescriptor {
  kind: 'custom'
  labelKey: string
  icon: string
  onClick: () => void
  variant?: 'solid' | 'subtle' | 'ghost' | 'outline'
  color?: 'primary' | 'neutral' | 'success' | 'warning' | 'error'
  loading?: Ref<boolean>
  disabled?: Ref<boolean>
}

/**
 * Split button: primary action on the left, dropdown caret on the right
 * exposing secondary actions. Modelled after the Azure DevOps PR action
 * button — one click for the most common case, one extra click for the
 * variants. Loading/disabled apply to the whole control so the user can't
 * trigger the primary or a secondary while one is already in flight.
 */
export interface SplitActionDescriptor {
  kind: 'split'
  labelKey: string
  icon: string
  onClick: () => void
  items: Array<{ labelKey: string; icon?: string; onClick: () => void }>
  variant?: 'solid' | 'subtle' | 'ghost' | 'outline'
  color?: 'primary' | 'neutral' | 'success' | 'warning' | 'error'
  loading?: Ref<boolean>
  disabled?: Ref<boolean>
}

export interface BreadcrumbItem {
  labelKey?: string
  label?: string
  icon?: string
  /** A path string or a Vue Router location (`{ path, query, ... }`) so
   *  callers can preserve query state on the back-link. */
  to?: string | RouteLocationRaw
}
