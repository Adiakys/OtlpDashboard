import type { AgChartTheme } from 'ag-charts-community'

/**
 * Vellum custom AG Charts theme. Builds on `ag-default` / `ag-default-dark`
 * via `baseTheme`, then overrides palette + typography + axis chrome to match
 * the rest of the app (Geist + Geist Mono, warm graphite axes, ember-led
 * 8-hue series palette).
 *
 * Returns a fresh object each call so we don't mutate the same theme between
 * dark and light renders. AG Charts diffs by reference, but theme overrides
 * are deep-cloned internally — cheap.
 */
export function vellumTheme(isDark: boolean): AgChartTheme {
  const fontSans = "'Geist Variable', sans-serif"
  const fontMono = "'Geist Mono Variable', ui-monospace, monospace"

  // Series palette — must round-trip through AG Charts' rgb parser, which
  // does not understand `oklch(...)`. We pre-resolve to hex equivalents.
  const palette = {
    fills: [
      '#E27A3F', // ember
      '#7AAA7A', // sage
      '#8C8AC8', // iris
      '#D9B566', // amber
      '#6FA8B8', // mist deep
      '#C57895', // rose dust
      '#B8B07A', // moss
      '#7B92C9'  // periwinkle
    ],
    strokes: [
      '#C9602F',
      '#5F8E5F',
      '#736FAB',
      '#BF994F',
      '#588D9D',
      '#A85C7B',
      '#9C9460',
      '#5E76B0'
    ]
  }

  const axisStroke = isDark ? '#3a3833' : '#c8c5be'
  const axisLabelColor = isDark ? '#7d7a73' : '#7e7b73'

  return {
    baseTheme: isDark ? 'ag-default-dark' : 'ag-default',
    palette,
    overrides: {
      common: {
        background: { visible: false },
        padding: { top: 6, right: 8, bottom: 6, left: 6 },
        legend: {
          item: {
            label: { fontFamily: fontSans, fontSize: 12, color: axisLabelColor }
          }
        },
        axes: {
          number: {
            line: { stroke: 'transparent' },
            tick: { stroke: axisStroke, width: 1 },
            label: { fontFamily: fontMono, fontSize: 11, color: axisLabelColor },
            gridLine: {
              style: [{ stroke: axisStroke, lineDash: [2, 4] }]
            },
            title: { fontFamily: fontSans, fontSize: 11, fontWeight: 600, color: axisLabelColor }
          },
          time: {
            line: { stroke: 'transparent' },
            tick: { stroke: axisStroke, width: 1 },
            label: { fontFamily: fontMono, fontSize: 11, color: axisLabelColor },
            gridLine: { style: [{ stroke: 'transparent' }] }
          },
          category: {
            line: { stroke: 'transparent' },
            tick: { stroke: axisStroke, width: 1 },
            label: { fontFamily: fontSans, fontSize: 11, color: axisLabelColor },
            gridLine: { style: [{ stroke: 'transparent' }] }
          }
        },
        tooltip: {
          enabled: true
        }
      },
      line: {
        series: { strokeWidth: 1.5, marker: { enabled: false } }
      },
      area: {
        series: { strokeWidth: 1.5, fillOpacity: 0.18 }
      },
      bar: {
        series: { cornerRadius: 1 }
      }
    }
  }
}
