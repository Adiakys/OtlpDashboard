import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import {
  buildWidgetCatalog,
  dtoToDefinition,
  libraryDtoToDefinitions,
  STD_DEFINITIONS
} from '~/pages/dashboard/catalog'
import { formatKind, normalizeKind, parseKind } from '~/pages/dashboard/types'
import type { WidgetDefinition } from '~/pages/dashboard/types'
import type { WidgetDefinitionDto, WidgetLibraryDto } from '~/services/types'

describe('FQ kind parsing', () => {
  it('treats a bare builtin kind as std', () => {
    expect(parseKind('metric-stat')).toEqual({ source: 'std', id: 'metric-stat' })
  })

  it('parses std prefix', () => {
    expect(parseKind('std:metric-line')).toEqual({ source: 'std', id: 'metric-line' })
  })

  it('parses custom prefix', () => {
    expect(parseKind('custom:abc-uuid')).toEqual({ source: 'custom', id: 'abc-uuid' })
  })

  it('parses library prefix and keeps libId', () => {
    const parsed = parseKind('library:team-pack/sla-tracker')
    expect(parsed).toEqual({ source: { library: 'team-pack' }, id: 'team-pack/sla-tracker' })
  })

  it('formatKind round-trips std', () => {
    expect(formatKind({ source: 'std', id: 'metric-stat' })).toBe('std:metric-stat')
  })

  it('formatKind round-trips custom', () => {
    expect(formatKind({ source: 'custom', id: 'uid-1' })).toBe('custom:uid-1')
  })

  it('formatKind round-trips library (id already contains slash)', () => {
    expect(formatKind({ source: { library: 'pack' }, id: 'pack/widget-x' })).toBe('library:pack/widget-x')
  })
})

describe('normalizeKind', () => {
  it('upgrades a bare kind to std FQ', () => {
    expect(normalizeKind('metric-stat')).toBe('std:metric-stat')
  })

  it('is idempotent on already-prefixed kinds', () => {
    expect(normalizeKind('std:metric-stat')).toBe('std:metric-stat')
    expect(normalizeKind('custom:uid')).toBe('custom:uid')
    expect(normalizeKind('library:pack/x')).toBe('library:pack/x')
  })
})

describe('STD_DEFINITIONS', () => {
  it('exposes all 10 builtin kinds', () => {
    const expected = [
      'metric-stat', 'metric-line', 'metric-sparkline',
      'metric-gauge', 'metric-bar-gauge', 'metric-pie',
      'metric-heatmap', 'recent-traces', 'logs-stream', 'text'
    ]
    expect(Object.keys(STD_DEFINITIONS).sort()).toEqual(expected.sort())
  })

  it('every std definition is a preset wrapping itself', () => {
    for (const [kind, def] of Object.entries(STD_DEFINITIONS)) {
      expect(def.source).toBe('std')
      expect(def.engine).toBe('preset')
      expect(def.baseKind).toBe(kind)
      expect(def.kind).toBe(`std:${kind}`)
    }
  })
})

describe('catalog merge', () => {
  function makeCustom(id: string, baseKind: string): WidgetDefinition {
    return {
      kind: `custom:${id}`,
      source: 'custom',
      name: `custom-${id}`,
      icon: 'i-ph-puzzle-piece',
      engine: 'preset',
      defaultSize: { w: 3, h: 3 },
      baseKind: baseKind as 'metric-stat',
      defaultConfig: {} as never
    }
  }

  it('byKind resolves builtin', () => {
    const catalog = buildWidgetCatalog(ref([]), ref([]))
    const def = catalog.byKind('std:metric-stat')
    expect(def?.source).toBe('std')
    expect(def?.kind).toBe('std:metric-stat')
  })

  it('byKind resolves custom from the reactive list', () => {
    const customs = ref<WidgetDefinition[]>([makeCustom('uid-1', 'metric-stat')])
    const catalog = buildWidgetCatalog(customs, ref([]))
    expect(catalog.byKind('custom:uid-1')?.name).toBe('custom-uid-1')
  })

  it('byKind tolerates legacy bare kinds', () => {
    const catalog = buildWidgetCatalog(ref([]), ref([]))
    expect(catalog.byKind('metric-stat')?.kind).toBe('std:metric-stat')
  })

  it('byKind returns null for unknown kinds', () => {
    const catalog = buildWidgetCatalog(ref([]), ref([]))
    expect(catalog.byKind('custom:nope')).toBeNull()
    expect(catalog.byKind('library:foo/bar')).toBeNull()
  })

  it('bySource segments correctly', () => {
    const customs = ref<WidgetDefinition[]>([makeCustom('a', 'metric-stat')])
    const catalog = buildWidgetCatalog(customs, ref([]))
    expect(catalog.bySource('std').value.length).toBe(10)
    expect(catalog.bySource('custom').value.length).toBe(1)
    expect(catalog.bySource('library').value.length).toBe(0)
  })

  it('reactive: adding a custom updates the catalog', () => {
    const customs = ref<WidgetDefinition[]>([])
    const catalog = buildWidgetCatalog(customs, ref([]))
    expect(catalog.byKind('custom:uid-2')).toBeNull()

    customs.value = [makeCustom('uid-2', 'metric-line')]
    expect(catalog.byKind('custom:uid-2')?.name).toBe('custom-uid-2')
  })
})

describe('libraryDtoToDefinitions', () => {
  function makeLibrary(): WidgetLibraryDto {
    return {
      id: 'team-pack',
      name: 'Team Pack',
      version: '1.2.0',
      author: null,
      license: null,
      description: null,
      installSource: 'Filesystem',
      gitUrl: null,
      gitRef: null,
      gitRefResolved: null,
      installedAt: null,
      removable: true,
      widgets: [
        {
          kindId: 'sla-tracker',
          name: 'SLA Tracker',
          description: 'p99 latency',
          icon: 'i-ph-target',
          engine: 'Preset',
          baseKind: 'metric-stat',
          config: { calc: 'last' },
          spec: null,
          defaultW: 4,
          defaultH: 3
        },
        {
          kindId: 'trace-heatmap',
          name: 'Trace heatmap',
          description: null,
          icon: 'i-ph-grid-four',
          engine: 'Spec',
          baseKind: null,
          config: null,
          spec: { mark: 'rect' },
          defaultW: 6,
          defaultH: 4
        }
      ]
    }
  }

  it('flattens widgets and namespaces kinds with the library id', () => {
    const defs = libraryDtoToDefinitions(makeLibrary())

    expect(defs).toHaveLength(2)
    expect(defs[0]!.kind).toBe('library:team-pack/sla-tracker')
    expect(defs[0]!.source).toEqual({ library: 'team-pack' })
    expect(defs[0]!.engine).toBe('preset')
    expect(defs[0]!.baseKind).toBe('metric-stat')

    expect(defs[1]!.kind).toBe('library:team-pack/trace-heatmap')
    expect(defs[1]!.engine).toBe('spec')
    expect(defs[1]!.spec).toEqual({ mark: 'rect' })
  })

  it('library widgets resolve through the catalog and are bucketed by library', () => {
    const defs = libraryDtoToDefinitions(makeLibrary())
    const catalog = buildWidgetCatalog(ref([]), ref(defs))

    expect(catalog.byKind('library:team-pack/sla-tracker')?.name).toBe('SLA Tracker')
    expect(catalog.bySource('library').value.length).toBe(2)

    const grouped = catalog.byLibrary.value
    expect(grouped.size).toBe(1)
    expect(grouped.get('team-pack')?.length).toBe(2)
  })
})

describe('dtoToDefinition', () => {
  it('maps a server DTO into the catalog shape', () => {
    const dto: WidgetDefinitionDto = {
      id: 'abc',
      name: 'p99 latency',
      description: 'p99 of http.server.duration',
      icon: 'i-ph-target',
      engine: 'Preset',
      baseKind: 'metric-stat',
      config: { calc: 'last', unitKind: 'ms' },
      spec: null,
      defaultW: 4,
      defaultH: 3,
      updatedAt: '2026-04-30T12:00:00Z',
      rowVersion: 1
    }

    const def = dtoToDefinition(dto)

    expect(def.kind).toBe('custom:abc')
    expect(def.source).toBe('custom')
    expect(def.engine).toBe('preset')
    expect(def.baseKind).toBe('metric-stat')
    expect(def.defaultSize).toEqual({ w: 4, h: 3 })
  })
})
