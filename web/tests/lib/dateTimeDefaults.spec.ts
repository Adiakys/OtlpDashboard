import { describe, expect, it } from 'vitest'
import type { AgChartOptions } from 'ag-charts-community'
import { applyDateTimeDefaults } from '~/lib/agcharts/dateTimeDefaults'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

// Minimal structural views so we can read what the normalizer injected without
// fighting AG Charts' option union types.
interface AxisView { type?: string; label?: { format?: string; formatter?: unknown } }
interface SeriesView { xKey?: string; tooltip?: { renderer?: unknown } }
function axes(o: AgChartOptions): AxisView[] {
  return (o as { axes?: AxisView[] }).axes ?? []
}
function series(o: AgChartOptions): SeriesView[] {
  return (o as { series?: SeriesView[] }).series ?? []
}

function timeChart(overrides: Partial<Record<string, unknown>> = {}): AgChartOptions {
  return {
    data: [{ time: new Date('2026-05-30T13:35:02Z'), value: 14 }],
    series: [{ type: 'line', xKey: 'time', yKey: 'value' }],
    axes: [
      { type: 'time', position: 'bottom' },
      { type: 'number', position: 'left' }
    ],
    ...overrides
  } as AgChartOptions
}

describe('applyDateTimeDefaults', () => {
  it('injects a formatter on a time axis that has none', () => {
    const out = applyDateTimeDefaults(timeChart())
    const timeAxis = axes(out).find(a => a.type === 'time')
    expect(typeof timeAxis?.label?.formatter).toBe('function')
  })

  it('leaves the number axis untouched', () => {
    const out = applyDateTimeDefaults(timeChart())
    const numberAxis = axes(out).find(a => a.type === 'number')
    expect(numberAxis?.label?.formatter).toBeUndefined()
  })

  it('injects a tooltip renderer on a series that has none', () => {
    const out = applyDateTimeDefaults(timeChart())
    expect(typeof series(out)[0]?.tooltip?.renderer).toBe('function')
  })

  it('does not overwrite an existing series tooltip renderer', () => {
    const existing = () => 'mine'
    const out = applyDateTimeDefaults(timeChart({
      series: [{ type: 'line', xKey: 'time', yKey: 'value', tooltip: { renderer: existing } }]
    }))
    expect(series(out)[0]?.tooltip?.renderer).toBe(existing)
  })

  it('does not overwrite an existing time axis format/formatter', () => {
    const out = applyDateTimeDefaults(timeChart({
      axes: [{ type: 'time', position: 'bottom', label: { format: '%H:%M:%S' } }]
    }))
    const timeAxis = axes(out).find(a => a.type === 'time')
    expect(timeAxis?.label?.formatter).toBeUndefined()
    expect(timeAxis?.label?.format).toBe('%H:%M:%S')
  })

  it('is a no-op for charts without a time axis (e.g. pie)', () => {
    const pie = { data: [], series: [{ type: 'pie', angleKey: 'value' }] } as AgChartOptions
    expect(applyDateTimeDefaults(pie)).toBe(pie)
  })

  it('the injected tooltip returns the structured shape with a helper-formatted heading', () => {
    const out = applyDateTimeDefaults(timeChart())
    const renderer = series(out)[0]?.tooltip?.renderer as
      (p: { datum: Record<string, unknown>; yValue?: unknown; yName?: string }) => { heading: string; data: { value: string }[] }
    const time = new Date('2026-05-30T13:35:02Z')
    const result = renderer({ datum: { time, value: 14 }, yValue: 14 })
    // Object form (not a raw string) keeps AG's default chrome; the heading must
    // come from the central helper (OS hour cycle), not AG's own default.
    expect(result.heading).toBe(dateTimeFormat(time, 'time-seconds'))
    expect(result.data[0]?.value).toContain('14')
  })
})
