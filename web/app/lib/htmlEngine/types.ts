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
 * Single typed parameter declared by a library widget. The `type` drives
 * the input control rendered in the config form and (for typed kinds)
 * lets the form populate options from live state — e.g. `service_name`
 * uses the dashboard's metric-services endpoint.
 *
 * Unknown future types should fall back to a plain string input on
 * older clients; the discriminator is open.
 */
export type ParameterDecl =
  | StringParameter
  | ServiceNameParameter
  | ServiceInstanceIdParameter
  | SelectParameter
  | NumberParameter
  | BooleanParameter

export interface ParameterBase {
  name: string
  /** Label shown above the input. Falls back to `name` when omitted. */
  label?: string
  /** Optional one-line help text rendered under the input. */
  description?: string
  /** When true the form blocks Apply until the user picks a value. */
  required?: boolean
}

export interface StringParameter extends ParameterBase {
  type: 'string'
  default?: string
  /** Soft cap on input length (UI hint). */
  maxLength?: number
  /** Placeholder text in the empty input. */
  placeholder?: string
}

export interface ServiceNameParameter extends ParameterBase {
  type: 'service_name'
  default?: string
}

export interface ServiceInstanceIdParameter extends ParameterBase {
  type: 'service_instance_id'
  default?: string
  /** Filter the dropdown by another `service_name`-typed parameter. */
  dependsOn?: string
}

export interface SelectParameter extends ParameterBase {
  type: 'select'
  default?: string
  options: Array<{ value: string; label?: string }>
}

export interface NumberParameter extends ParameterBase {
  type: 'number'
  default?: number
  min?: number
  max?: number
  step?: number
}

export interface BooleanParameter extends ParameterBase {
  type: 'boolean'
  default?: boolean
}

/**
 * Concrete metric identity baked into the widget definition with optional
 * `${param}` placeholders. The resolver substitutes the placeholders from
 * {@link HtmlInstanceConfig.parameters} and produces a runtime
 * {@link MetricBinding}; the existing instrument-catalog late-binding
 * fills in `resourceHash` from the (scope, name, kind, serviceName) key.
 *
 * When a binding declares this template, the per-instance form may skip
 * the manual InstrumentPicker entirely — the user only fills the
 * top-level parameters.
 */
export interface MetricTemplate {
  scopeName: string
  instrumentName: string
  kind: string
  serviceName?: string | null
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
  /**
   * Pre-bound metric path with `${param}` placeholders. When present the
   * per-instance config form needs only the parameter inputs — the widget
   * resolves the binding automatically. A manual override in
   * {@link HtmlInstanceConfig.bindings} still wins, so users can pin a
   * specific instrument when the template doesn't fit.
   */
  metric?: MetricTemplate
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
  /** Map `bindingName -> resolved instrument`. Acts as the manual
   *  override path: a non-null entry here pins that binding regardless
   *  of whether the spec declares a {@link MetricTemplate}. Bindings
   *  without an entry fall through to the parameter-driven template. */
  bindings: Record<string, MetricBinding | null>
  /**
   * User-supplied values for the parameters declared by
   * {@link HtmlSpec.parameters}. Each entry is the raw input (string for
   * `string` / `service_name` / `service_instance_id` / `select`,
   * number for `number`, boolean for `boolean`). Substituted into
   * {@link MetricTemplate} placeholders when the binding resolves.
   */
  parameters?: Record<string, string | number | boolean>
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
