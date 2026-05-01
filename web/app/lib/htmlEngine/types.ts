import type { CalcMode } from '~/lib/units/calc'
import type { UnitKind } from '~/lib/units/format'
import type { ThresholdStop } from '~/lib/units/thresholds'
import type { MetricBinding, RangePreset } from '~/pages/dashboard/types'

/**
 * Shape of the `def.spec` payload the SPA expects for `engine: 'spec'`
 * widgets. The backend stores it as opaque JSON ≤ 256 KB; we read it
 * straight into this type. Three top-level keys carry the entire
 * widget contract:
 *
 *   - `template`: HTML+SVG with Mustache-light placeholders
 *   - `styles`:   CSS scoped to the widget instance at render time
 *   - `dataBindings`: schema of the named values the template can
 *     reference (one binding produces a scalar or list under `name`)
 *
 * Library widgets ship this immutable; custom widgets edit it via the
 * config form (iter 2b — for now only the bindings configuration is
 * editable per-instance).
 */
export interface HtmlSpec {
  template: string
  styles?: string
  dataBindings: HtmlBindingDecl[]
}

/**
 * A single named slot the template can reference. The widget runtime
 * resolves each declaration into a concrete value (scalar / array)
 * exposed under `name` in the template's scope.
 */
export type HtmlBindingDecl =
  | HtmlMetricBinding
  | HtmlMetricSeriesBinding
  | HtmlRecentTracesBinding
  | HtmlRecentLogsBinding

export interface HtmlBindingBase {
  name: string
  /** Optional UI hint shown next to the per-instance config field. */
  description?: string
}

/**
 * Scalar from a metric: load one or more series, reduce per-binding
 * with `calc`. The instance config supplies the actual `MetricBinding`
 * (resourceHash, scopeName, instrumentName, kind).
 */
export interface HtmlMetricBinding extends HtmlBindingBase {
  type: 'metric'
  /** Reduction over the loaded points. Default `'last'`. */
  calc?: CalcMode
  /** Optional default range when the user hasn't set one. */
  range?: RangePreset
  /** When set, the binding produces a list of `{ key, value, attrs }`
   *  one entry per group, instead of a single scalar. The template can
   *  iterate with `{{#each name as item}}`. */
  splitBy?: string | null
  /** Display unit for `format` helpers. Carried along to the template
   *  scope as `<name>.unitKind`. */
  unitKind?: UnitKind
  /** Threshold list reachable from helpers (`thresholdClass <name>.value <name>.thresholds`). */
  thresholds?: ThresholdStop[]
}

/**
 * Array of raw `MetricPointDto` rows. Useful when the template wants
 * to draw something custom from the points (mini-bars, sparkline path).
 */
export interface HtmlMetricSeriesBinding extends HtmlBindingBase {
  type: 'metric-series'
  range?: RangePreset
  /** Per-point attribute key for grouping on the template side. */
  splitBy?: string | null
}

/** Array of recent trace summaries. */
export interface HtmlRecentTracesBinding extends HtmlBindingBase {
  type: 'recent-traces'
  range?: RangePreset
  service?: string | null
  limit?: number
}

/** Array of recent log records. */
export interface HtmlRecentLogsBinding extends HtmlBindingBase {
  type: 'recent-logs'
  range?: RangePreset
  service?: string | null
  minSeverity?: number
  limit?: number
}

/**
 * Per-instance state for an `engine: spec` widget. Carries one entry
 * per binding declared in `def.spec.dataBindings`. The user-facing
 * config form edits the values here; the spec itself stays immutable
 * for library widgets (and is editable in iter 2b for `custom` widgets).
 */
export interface HtmlInstanceConfig {
  /** Map `bindingName -> resolved instrument`. `null` until configured. */
  bindings: Record<string, MetricBinding | null>
  /** Override the per-binding defaults. Falls back to the binding decl
   *  values when missing. */
  range?: RangePreset
  /** Optional override for the widget header. */
  title?: string
}

/**
 * Output of the binding resolver — what the template's scope sees.
 * Each binding name resolves to one of these shapes depending on
 * `type`. The renderer doesn't care about the discriminator: it just
 * walks the dot-path in the scope tree.
 */
export type ResolvedBinding =
  | { kind: 'scalar'; value: number | null; unit?: string; unitKind?: UnitKind; thresholds?: ThresholdStop[] }
  | { kind: 'list';   items: Array<Record<string, unknown>> }
  | { kind: 'error';  error: string }
