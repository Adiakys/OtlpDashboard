import { describe, expect, it } from 'vitest'
import {
  effectiveValue,
  expandMetricTemplate
} from '~/lib/htmlEngine/parameterExpansion'
import type { MetricTemplate, ParameterDecl } from '~/lib/htmlEngine/types'

describe('expandMetricTemplate', () => {
  const template: MetricTemplate = {
    scopeName: 'System.Runtime',
    instrumentName: 'dotnet.gc.last_collection.heap.size',
    kind: 'Sum',
    serviceName: '${service}'
  }

  it('substitutes ${service} from parameters and leaves resourceHash empty for late-binding', () => {
    const out = expandMetricTemplate(template, { service: 'sample-server' })
    expect(out).toEqual({
      resourceHash: '',
      scopeName: 'System.Runtime',
      instrumentName: 'dotnet.gc.last_collection.heap.size',
      kind: 'Sum',
      serviceName: 'sample-server'
    })
  })

  it('expands a template whose placeholders are confined to serviceName even with no parameters supplied (serviceName comes back null)', () => {
    expect(expandMetricTemplate(template, undefined)).toEqual({
      resourceHash: '',
      scopeName: 'System.Runtime',
      instrumentName: 'dotnet.gc.last_collection.heap.size',
      kind: 'Sum',
      serviceName: null
    })
  })

  it('returns null when a required field would be empty after substitution', () => {
    const partial: MetricTemplate = {
      scopeName: '${scope}',
      instrumentName: 'x',
      kind: 'Sum'
    }
    expect(expandMetricTemplate(partial, { scope: '' })).toBeNull()
  })

  it('coerces numeric and boolean parameter values via String()', () => {
    const t: MetricTemplate = {
      scopeName: 'pool-${id}',
      instrumentName: 'x',
      kind: 'Sum'
    }
    const out = expandMetricTemplate(t, { id: 7 })
    expect(out?.scopeName).toBe('pool-7')
  })

  it('treats serviceName empty string as null so the catalog skips the filter', () => {
    const t: MetricTemplate = {
      scopeName: 's',
      instrumentName: 'x',
      kind: 'Sum',
      serviceName: '${maybeService}'
    }
    const out = expandMetricTemplate(t, { maybeService: '' })
    expect(out?.serviceName).toBeNull()
  })
})

describe('effectiveValue', () => {
  const decl: ParameterDecl = {
    name: 'service',
    type: 'service_name',
    default: 'fallback'
  }

  it('returns the user value when present and non-empty', () => {
    expect(effectiveValue(decl, { service: 'real' })).toBe('real')
  })

  it('falls back to the spec default when the user value is missing', () => {
    expect(effectiveValue(decl, undefined)).toBe('fallback')
  })

  it('falls back to the spec default when the user value is empty string', () => {
    expect(effectiveValue(decl, { service: '' })).toBe('fallback')
  })

  it('returns undefined when no default is declared and no user value is set', () => {
    const noDefault: ParameterDecl = { name: 'service', type: 'service_name' }
    expect(effectiveValue(noDefault, undefined)).toBeUndefined()
  })
})
