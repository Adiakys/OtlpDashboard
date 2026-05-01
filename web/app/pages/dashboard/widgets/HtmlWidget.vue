<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useId, watch } from 'vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import { TEMPLATE_HELPERS } from '~/lib/htmlEngine/helpers'
import { renderTemplate } from '~/lib/htmlEngine/templateRenderer'
import { scopeCss } from '~/lib/htmlEngine/scopeCss'
import { reduce } from '~/lib/units/calc'
import { groupPoints } from '~/lib/agcharts/seriesGrouping'
import type {
  HtmlBindingDecl,
  HtmlInstanceConfig,
  HtmlMetricBinding,
  HtmlSpec
} from '~/lib/htmlEngine/types'
import type { MetricBinding } from '../types'

/**
 * Host for `engine: 'spec'` widgets, repurposed in iter 2 to render
 * sandboxed HTML+SVG+CSS templates with named metric bindings. The
 * runtime resolves each binding into a scalar or list, hands the
 * resulting scope to the Mustache-light renderer, and mounts the
 * sanitised output via `v-html`.
 *
 * Three layers of safety, in order of execution:
 *   1. Mustache-light renderer escapes every interpolation and never
 *      eval()s user expressions — only whitelisted helpers run.
 *   2. DOMPurify (lazy-loaded) sanitises the final HTML, dropping
 *      `<script>`, inline event handlers, `javascript:` URLs.
 *   3. CSS is auto-prefixed with the widget's instance scope so styles
 *      can't leak into the rest of the SPA, and dangerous at-rules /
 *      `expression()` are stripped before scoping.
 */
const props = withDefaults(defineProps<{
  config: HtmlInstanceConfig
  /** The library/custom-widget spec carried on the definition. */
  spec?: HtmlSpec | null
  isEditing: boolean
  liveTick: number
  preview?: boolean
  title?: string
  icon?: string
}>(), { preview: false, spec: null, title: 'Widget', icon: 'i-ph-shapes' })

defineEmits<{ edit: []; remove: [] }>()

const { t } = useI18n()
const { $metricsService } = useNuxtApp()

const instanceId = useId().replace(/[^a-zA-Z0-9_-]/g, '')
const scopeSelector = `.widget-html-${instanceId}`

// --- bindings → MetricBinding[] for useWidgetSeries -----------------

interface MetricBindingPair {
  decl: HtmlMetricBinding
  metric: MetricBinding
}

const metricPairs = computed<MetricBindingPair[]>(() => {
  const decls = (props.spec?.dataBindings ?? []).filter(
    (b): b is HtmlMetricBinding => b.type === 'metric' || b.type === 'metric-series'
  ) as HtmlMetricBinding[]
  const out: MetricBindingPair[] = []
  for (const decl of decls) {
    const metric = props.config.bindings?.[decl.name] ?? null
    if (metric) out.push({ decl, metric })
  }
  return out
})

const metricsForFetch = computed(() => metricPairs.value.map(p => p.metric))

// All bindings ask for attributes — the splitBy logic below needs them
// and the cost is bounded for the small N a single template requires.
const range = computed(() => props.config.range ?? 'last-1h')
const { series, loading, error, hasLoaded } = useWidgetSeries(
  $metricsService, metricsForFetch, range, () => props.liveTick,
  { includeAttributes: true }
)

// --- bindings → template scope ------------------------------------------

/** The named scope the template renderer evaluates its expressions
 *  against. One key per binding declaration; the shape is documented
 *  on `HtmlSpec.dataBindings` and varies per `type`. */
const scope = computed<Record<string, unknown>>(() => {
  const out: Record<string, unknown> = {}
  const decls = props.spec?.dataBindings ?? []
  for (const decl of decls) {
    out[decl.name] = resolveBinding(decl)
  }
  return out
})

function resolveBinding(decl: HtmlBindingDecl): unknown {
  if (decl.type === 'metric') {
    const pair = metricPairs.value.find(p => p.decl.name === decl.name)
    if (!pair) return { value: null, configured: false }
    const matching = series.value.find(s =>
      s.instrument.scopeName === pair.metric.scopeName &&
      s.instrument.name === pair.metric.instrumentName &&
      s.instrument.kind === pair.metric.kind)
    if (!matching || matching.points.length === 0) return { value: null, configured: true }

    if (decl.splitBy) {
      const groups = groupPoints(matching.points, [decl.splitBy])
      return groups.map(g => ({
        key: String(g.attrs[decl.splitBy!] ?? ''),
        value: reduce(g.points.map(p => Number(p.value)), decl.calc ?? 'last'),
        attrs: g.attrs,
        unitKind: decl.unitKind,
        thresholds: decl.thresholds ?? []
      }))
    }
    const all = matching.points.map(p => Number(p.value))
    return {
      value: reduce(all, decl.calc ?? 'last'),
      unit: pair.metric.unit ?? '',
      unitKind: decl.unitKind,
      thresholds: decl.thresholds ?? [],
      configured: true
    }
  }
  if (decl.type === 'metric-series') {
    const pair = metricPairs.value.find(p => p.decl.name === decl.name)
    if (!pair) return []
    const matching = series.value.find(s =>
      s.instrument.scopeName === pair.metric.scopeName &&
      s.instrument.name === pair.metric.instrumentName &&
      s.instrument.kind === pair.metric.kind)
    return matching?.points ?? []
  }
  // recent-traces / recent-logs land in iter 2b; for now they expose
  // an empty array so templates fail gracefully (no crash, no data).
  return []
}

// --- render -------------------------------------------------------------

const renderError = ref<string | null>(null)

/**
 * The DOMPurify default returns the `DOMPurify` namespace object — the
 * actual `sanitize` function lives on it. We cache the loader promise
 * so multiple instances share the same dynamic import.
 */
let purifyPromise: Promise<typeof import('dompurify')['default']> | null = null
function loadPurify() {
  if (!purifyPromise) {
    purifyPromise = import('dompurify').then(m => m.default)
  }
  return purifyPromise
}

const renderedHtml = ref<string>('')
const scopedStyles = computed(() => scopeCss(props.spec?.styles ?? '', scopeSelector))

async function renderOnce() {
  const tpl = props.spec?.template
  if (!tpl) {
    renderedHtml.value = ''
    renderError.value = null
    return
  }
  let raw: string
  try {
    raw = renderTemplate(tpl, scope.value, { helpers: TEMPLATE_HELPERS })
    renderError.value = null
  } catch (e) {
    renderError.value = e instanceof Error ? e.message : String(e)
    renderedHtml.value = ''
    return
  }
  const purify = await loadPurify()
  // SVG/MathML allowed for illustrations; force `target="_self"` in
  // case a template authors a link.
  renderedHtml.value = purify.sanitize(raw, {
    USE_PROFILES: { html: true, svg: true, svgFilters: true },
    FORBID_TAGS: ['style', 'script', 'iframe', 'object', 'embed', 'link', 'meta'],
    FORBID_ATTR: ['onerror', 'onload', 'onclick', 'onmouseover', 'onfocus', 'onblur']
  })
}

watch([scope, () => props.spec?.template], renderOnce, { immediate: true })
onBeforeUnmount(() => { purifyPromise = null })

// --- placeholders -------------------------------------------------------

const isConfigured = computed(() => {
  const decls = (props.spec?.dataBindings ?? []).filter(
    b => b.type === 'metric' || b.type === 'metric-series'
  )
  if (decls.length === 0) return true // template without metric bindings
  return decls.every(d => props.config.bindings?.[d.name])
})

const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)

const placeholderText = computed(() => {
  if (!props.spec) return t('dashboard.widgets.htmlSpecMissing')
  if (!isConfigured.value) return t('dashboard.widgets.notConfigured')
  return null
})
</script>

<template>
  <BaseWidget
    :title="title"
    :icon="icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error ?? renderError"
    :show-skeleton="showSkeleton"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <div class="vellum-preview-html">
        <div class="vellum-preview-html__chip" />
        <div class="vellum-preview-html__row" />
        <div class="vellum-preview-html__row vellum-preview-html__row--mid" />
      </div>
    </template>

    <div
      v-if="placeholderText"
      class="flex-1 min-h-0 flex items-center justify-center text-mono-sm text-muted px-3 text-center"
    >
      {{ placeholderText }}
    </div>
    <div v-else class="flex-1 min-h-0 min-w-0 overflow-auto">
      <!-- Authored styles (scoped to the instance) -->
      <component :is="'style'" v-if="scopedStyles">{{ scopedStyles }}</component>
      <!-- Sanitised template output -->
      <!-- eslint-disable-next-line vue/no-v-html -->
      <div :class="`widget-html-${instanceId}`" v-html="renderedHtml" />
    </div>
  </BaseWidget>
</template>

<style scoped>
.vellum-preview-html {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  padding: 0.5rem 0.6rem;
  justify-content: center;
}
.vellum-preview-html__chip {
  width: 2.4rem;
  height: 0.55rem;
  background: var(--color-ember-500);
  border-radius: var(--radius-pill);
}
.vellum-preview-html__row {
  height: 0.4rem;
  width: 100%;
  border-radius: var(--radius-pill);
  background: color-mix(in oklab, var(--color-graphite-500) 20%, transparent);
}
.vellum-preview-html__row--mid { width: 65%; }
</style>
