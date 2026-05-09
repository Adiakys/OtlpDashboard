import type { MetricBinding } from '~/pages/dashboard/types'
import type { MetricTemplate, ParameterDecl } from './types'

/**
 * Substitute `${param}` placeholders in a metric template with values
 * from the per-instance parameters map and produce a runtime
 * `MetricBinding`. Returns `null` when any required field would be
 * empty after substitution — the caller should treat that as
 * "binding not configured yet" so the widget shows a clean empty
 * state instead of querying the API with garbage.
 *
 * `resourceHash` is left empty so the existing instrument-catalog
 * late-binding (`useInstrumentCatalog.resolve`) fills it in by logical
 * key (`scope + name + kind + serviceName`). That keeps demo
 * dashboards portable across deploys: a fresh container restart
 * produces a different hash but the logical key is stable.
 */
export function expandMetricTemplate(
  template: MetricTemplate,
  parameters: Record<string, unknown> | undefined
): MetricBinding | null {
  const scopeName = substitute(template.scopeName, parameters)
  const instrumentName = substitute(template.instrumentName, parameters)
  const kind = substitute(template.kind, parameters)
  const serviceName = substitute(template.serviceName ?? '', parameters)
  const serviceInstanceId = substitute(template.serviceInstanceId ?? '', parameters)

  if (!scopeName || !instrumentName || !kind) return null

  return {
    resourceHash: '',
    scopeName,
    instrumentName,
    kind,
    serviceName: serviceName === '' ? null : serviceName,
    serviceInstanceId: serviceInstanceId === '' ? null : serviceInstanceId
  }
}

/**
 * Replace every `${name}` token in `s` with the matching parameter
 * value (coerced to its string form). Tokens whose parameter is
 * missing or whose value is the empty string leave the literal `${name}`
 * intact — callers detect that via the truthy check on the returned
 * field and treat the binding as unresolved. Tokens with non-empty
 * primitive values substitute cleanly.
 */
function substitute(s: string, parameters: Record<string, unknown> | undefined): string {
  if (!s) return ''
  if (!parameters) return s.includes('${') ? unresolved(s) : s
  return s.replace(/\$\{(\w+)\}/g, (_match, key: string) => {
    const v = parameters[key]
    if (v === undefined || v === null) return ''
    return String(v)
  })
}

/**
 * If a placeholder remained because no parameters were provided, blank
 * the entire string so the caller's `!field` check correctly flags the
 * template as unresolved. Without this, a binding with no parameters
 * would round-trip the literal `${service}` to the API and produce a
 * confusing 404.
 */
function unresolved(s: string): string {
  return /\$\{\w+\}/.test(s) ? '' : s
}

/**
 * Resolve the effective default for a parameter declaration: the
 * user-supplied value if present, else the spec's own default. Returns
 * `undefined` when neither is set — the form treats that as "not yet
 * configured" and required parameters block Apply at that point.
 */
export function effectiveValue(
  decl: ParameterDecl,
  parameters: Record<string, unknown> | undefined
): string | number | boolean | undefined {
  const v = parameters?.[decl.name]
  if (v !== undefined && v !== null && v !== '') return v as string | number | boolean
  if (decl.type === 'number') return decl.default
  if (decl.type === 'boolean') return decl.default
  return decl.default
}

/**
 * Expand `${param}` placeholders inside the four logical-key fields of a
 * full `MetricBinding`. Mirrors `expandMetricTemplate` but operates on
 * the runtime binding shape, so it covers preset widgets that store the
 * template directly inside `WidgetConfig.metric` (alongside the catalog's
 * resourceHash late-binding).
 *
 * Returns `null` only when one of the required logical-key fields
 * collapses to empty — that case maps to "user hasn't filled the
 * required parameter yet" and the caller renders the widget's
 * unconfigured state instead of issuing a doomed request. A binding with
 * no placeholders at all comes back unchanged.
 */
export function expandMetricBinding(
  binding: import('~/pages/dashboard/types').MetricBinding | null | undefined,
  parameters: Record<string, unknown> | undefined
): import('~/pages/dashboard/types').MetricBinding | null {
  if (!binding) return null

  const scopeName = substitute(binding.scopeName, parameters)
  const instrumentName = substitute(binding.instrumentName, parameters)
  const kind = substitute(binding.kind, parameters)
  const serviceNameRaw = substitute(binding.serviceName ?? '', parameters)
  const serviceInstanceIdRaw = substitute(binding.serviceInstanceId ?? '', parameters)

  if (!scopeName || !instrumentName || !kind) return null

  return {
    ...binding,
    scopeName,
    instrumentName,
    kind,
    serviceName: serviceNameRaw === '' ? null : serviceNameRaw,
    serviceInstanceId: serviceInstanceIdRaw === '' ? null : serviceInstanceIdRaw
  }
}

/**
 * Apply {@link expandMetricBinding} across an array. Bindings that fail
 * to expand are dropped — they show up as gaps in the rendered series
 * rather than silently breaking neighbouring bindings.
 */
export function expandMetricBindings(
  bindings: ReadonlyArray<import('~/pages/dashboard/types').MetricBinding> | undefined,
  parameters: Record<string, unknown> | undefined
): import('~/pages/dashboard/types').MetricBinding[] {
  if (!bindings || bindings.length === 0) return []
  const out: import('~/pages/dashboard/types').MetricBinding[] = []
  for (const b of bindings) {
    const expanded = expandMetricBinding(b, parameters)
    if (expanded) out.push(expanded)
  }
  return out
}
