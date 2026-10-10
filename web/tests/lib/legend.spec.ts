import { describe, expect, it } from 'vitest'
import { buildChartOptions } from '~/lib/agcharts/chartStrategy'
import { formatLegend, groupPoints } from '~/lib/agcharts/seriesGrouping'
import type { InstrumentDto, MetricPointDto, MetricSeriesDto } from '~/services/types'

function point(attributes: Record<string, unknown>, value = 1): MetricPointDto {
  return { time: '2030-01-01T00:00:00Z', startTime: '2030-01-01T00:00:00Z', value, attributes } as MetricPointDto
}

function seriesOf(name: string, points: MetricPointDto[]): MetricSeriesDto {
  const instrument = { name, scopeName: 'tests', kind: 'Gauge', unit: null, serviceName: 'api', serviceInstanceId: null } as unknown as InstrumentDto
  return { instrument, points, truncated: false } as unknown as MetricSeriesDto
}

const points = [
  point({ method: 'GET', host: 'a' }),
  point({ method: 'POST', host: 'a' })
]

describe('formatLegend', () => {
  it('replaces placeholders with attribute values', () => {
    const [get] = groupPoints(points, ['method'])
    expect(formatLegend('Requests {method} on {host}', get!, 2)).toBe('Requests GET on a')
  })

  it('appends the group when a fixed label covers several series', () => {
    const [get] = groupPoints(points, ['method'])
    expect(formatLegend('Requests', get!, 2)).toBe('Requests {method=GET}')
    expect(formatLegend('Requests', get!, 1)).toBe('Requests')
  })

  it('falls back to the group description when the result is empty', () => {
    const [get] = groupPoints(points, ['method'])
    expect(formatLegend('{missing}', get!, 2)).toBe('{method=GET}')
  })
})

describe('buildChartOptions legend', () => {
  it('names series from the legend template and keeps the default otherwise', () => {
    const options = buildChartOptions({
      series: [seriesOf('http.client.active_requests', points), seriesOf('other.metric', [point({})])],
      chartType: 'line',
      splitBy: ['method'],
      locale: 'en',
      isDark: false,
      legendFor: i => i.name === 'http.client.active_requests' ? 'Active {method}' : null
    })
    const names = (options.series as { yName: string }[]).map(s => s.yName)
    expect(names).toEqual(['Active GET', 'Active POST', 'other.metric (no attrs)'])
  })
})
