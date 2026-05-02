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

  if (!scopeName || !instrumentName || !kind) return null

  return {
    resourceHash: '',
    scopeName,
    instrumentName,
    kind,
    serviceName: serviceName === '' ? null : serviceName
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
