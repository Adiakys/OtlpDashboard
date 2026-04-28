import type { Ref } from 'vue'
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

export interface ApplicationFilterDescriptor {
  kind: 'application'
  modelValue: Ref<string | null>
  options: Ref<string[]>
  includeAll?: boolean
  disabled?: Ref<boolean>
}

export interface TimeRangeFilterDescriptor {
  kind: 'time-range'
  modelValue: Ref<TimeWindow>
  disabled?: Ref<boolean>
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

export type ActionDescriptor =
  | RefreshActionDescriptor
  | LiveActionDescriptor
  | CustomActionDescriptor

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

export interface BreadcrumbItem {
  labelKey?: string
  label?: string
  icon?: string
  to?: string
}
