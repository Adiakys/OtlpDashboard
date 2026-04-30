/**
 * Vellum AG Grid theme — built on top of `themeQuartz` via the v33 Theming API.
 *
 * v33 made the JS theme objects authoritative for everything AG Grid renders;
 * the legacy `.ag-theme-quartz { --ag-* }` pattern still parses but is
 * shadowed for any param the API owns (row height, padding, fonts, borders,
 * row hover, selection, focus). For things the API doesn't expose — sort
 * indicators, scrollbar — we ship targeted CSS in `assets/css/aggrid.css`.
 *
 * Light + dark are registered as named modes; `AppDataGrid` flips the
 * `data-ag-theme-mode` attribute on the wrapper and AG Grid swaps params.
 */
import { themeQuartz, type Theme } from 'ag-grid-community'

// Hex tokens — AG Grid's param parser doesn't accept `oklch()` yet, so we
// keep visual-equivalents in lockstep with `tokens.css`.
const graphite = {
  50:  '#f7f5f1',
  100: '#eeebe3',
  200: '#dcd7c9',
  300: '#bdb6a3',
  400: '#8d877a',
  500: '#666258',
  600: '#4f4c44',
  700: '#3c3a33',
  800: '#2a2823',
  900: '#1d1c19',
  950: '#151412'
}
const ember = {
  300: '#f2a87e',
  400: '#e8895c',
  500: '#d7723f',
  600: '#c9602f',
  700: '#a24e27'
}

const fontSans = '"Geist Variable", ui-sans-serif, system-ui, sans-serif'
const fontMono = '"Geist Mono Variable", ui-monospace, "SF Mono", Menlo, monospace'

// Params shared between light and dark. Anything that's a colour goes in the
// per-mode blocks below.
const sharedParams = {
  fontFamily: fontSans,
  fontSize: 12.5,

  // Density: cockpit-tight by default, k9s/htop ergonomics. Pages can pass
  // a per-grid `:row-height` prop (logs page does, for legibility on long
  // bodies). Header is intentionally shorter than rows — the overline label
  // doesn't need the same vertical breathing room as data.
  rowHeight: 26,
  headerHeight: 28,
  listItemHeight: 26,

  headerFontFamily: fontMono,
  headerFontSize: 10.5,
  headerFontWeight: 500,
  dataFontFamily: fontSans,

  cellHorizontalPadding: 12,

  // Borders. Row hairline = the only horizontal rule (so dense rows stay
  // visually parsable). No column rules in the body — column separation
  // comes from header dividers + tabular alignment, not internal grid lines.
  rowBorder: true,
  columnBorder: false as const,
  headerColumnBorder: false as const,
  wrapperBorder: false as const,
  sidePanelBorder: false as const,

  accentColor: ember[500],
  spacing: 5,
  iconSize: 14
} as const

const lightParams = {
  ...sharedParams,

  backgroundColor: '#ffffff',
  foregroundColor: '#26241f',
  textColor: '#26241f',
  chromeBackgroundColor: graphite[50],

  headerBackgroundColor: '#ffffff',
  headerTextColor: graphite[500],

  oddRowBackgroundColor: '#ffffff',
  rowHoverColor: 'color-mix(in oklab, ' + graphite[500] + ' 5%, transparent)',
  selectedRowBackgroundColor: 'color-mix(in oklab, ' + ember[500] + ' 8%, transparent)',
  columnHoverColor: 'color-mix(in oklab, ' + graphite[500] + ' 3%, transparent)',

  rangeSelectionBackgroundColor: 'color-mix(in oklab, ' + ember[500] + ' 12%, transparent)',
  rangeSelectionBorderColor: ember[500],

  inputFocusBorderColor: ember[500],
  inputFocusBoxShadow: '0 0 0 2px color-mix(in oklab, ' + ember[500] + ' 30%, transparent)',

  borderColor: graphite[200]
}

const darkParams = {
  ...sharedParams,

  backgroundColor: graphite[950],
  foregroundColor: '#dcd9d2',
  textColor: '#dcd9d2',
  chromeBackgroundColor: graphite[900],

  headerBackgroundColor: graphite[950],
  headerTextColor: graphite[400],

  oddRowBackgroundColor: graphite[950],
  rowHoverColor: 'color-mix(in oklab, ' + graphite[400] + ' 7%, transparent)',
  selectedRowBackgroundColor: 'color-mix(in oklab, ' + ember[500] + ' 10%, transparent)',
  columnHoverColor: 'color-mix(in oklab, ' + graphite[400] + ' 4%, transparent)',

  rangeSelectionBackgroundColor: 'color-mix(in oklab, ' + ember[500] + ' 14%, transparent)',
  rangeSelectionBorderColor: ember[500],

  inputFocusBorderColor: ember[500],
  inputFocusBoxShadow: '0 0 0 2px color-mix(in oklab, ' + ember[500] + ' 35%, transparent)',

  borderColor: graphite[800]
}

export const vellumGridTheme: Theme = themeQuartz
  .withParams(lightParams, 'light')
  .withParams(darkParams, 'dark')
