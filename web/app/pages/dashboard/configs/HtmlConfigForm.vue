<script setup lang="ts">
import { computed, inject } from 'vue'
import InstrumentPicker from '../components/InstrumentPicker.vue'
import RangePresetSelect from './RangePresetSelect.vue'
import { useWidgetCatalog } from '../catalog'
import { WIDGET_KIND_INJECTION_KEY } from '../injectionKeys'
import type {
  HtmlBindingDecl,
  HtmlInstanceConfig,
  HtmlSpec
} from '~/lib/htmlEngine/types'
import type { MetricBinding } from '../types'

/**
 * Per-instance config form for `engine: 'spec'` (HTML template) widgets.
 * iter 2 covers library widgets only — the template/styles live on the
 * definition and are surfaced read-only as a collapsed details block.
 * Custom-widget editing (template textarea, bindings declaration) lands
 * in iter 2b.
 */
const props = defineProps<{ modelValue: HtmlInstanceConfig }>()

const emit = defineEmits<{
  'update:modelValue': [value: HtmlInstanceConfig]
}>()

const { t } = useI18n()
const catalog = useWidgetCatalog()

const kindRef = inject(WIDGET_KIND_INJECTION_KEY, computed(() => '' as string))
const definition = computed(() => catalog.byKind(kindRef.value))

const spec = computed<HtmlSpec | null>(() => {
  const raw = definition.value?.spec
  if (!raw || typeof raw !== 'object') return null
  return raw as HtmlSpec
})

const metricDecls = computed<HtmlBindingDecl[]>(() =>
  (spec.value?.dataBindings ?? []).filter(
    b => b.type === 'metric' || b.type === 'metric-series'
  )
)

const templatePreview = computed(() => spec.value?.template?.trim() ?? '')

function setBinding(name: string, metric: MetricBinding | null) {
  emit('update:modelValue', {
    ...props.modelValue,
    bindings: {
      ...(props.modelValue.bindings ?? {}),
      [name]: metric
    }
  })
}

function patch(p: Partial<HtmlInstanceConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}
</script>

<template>
  <div class="flex flex-col gap-3 h-full min-h-0">
    <UFormField :label="t('dashboard.config.title')">
      <UInput
        :model-value="modelValue.title ?? ''"
        @update:model-value="(v) => patch({ title: v ? String(v) : undefined })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.range')">
      <RangePresetSelect
        :model-value="modelValue.range ?? 'last-1h'"
        @update:model-value="(v) => patch({ range: v })"
      />
    </UFormField>

    <div v-if="metricDecls.length === 0" class="text-mono-sm" style="color: var(--color-graphite-500);">
      {{ t('dashboard.config.htmlNoMetricBindings') }}
    </div>

    <section v-for="decl in metricDecls" :key="decl.name" class="flex flex-col gap-1.5 min-h-0 flex-1">
      <header class="flex items-baseline justify-between">
        <span class="text-overline" style="color: var(--color-graphite-500);">
          {{ decl.name }}
        </span>
        <span v-if="decl.description" class="text-mono-sm" style="color: var(--color-graphite-500);">
          {{ decl.description }}
        </span>
      </header>
      <div class="h-48 min-h-0">
        <InstrumentPicker
          mode="single"
          :model-value="modelValue.bindings?.[decl.name] ?? null"
          @update:model-value="(v) => setBinding(decl.name, (v as MetricBinding | null))"
        />
      </div>
    </section>

    <details v-if="templatePreview" class="vellum-html-spec">
      <summary class="text-overline cursor-pointer" style="color: var(--color-graphite-500);">
        {{ t('dashboard.config.htmlTemplatePreview') }}
      </summary>
      <pre class="vellum-html-spec__pre"><code>{{ templatePreview }}</code></pre>
    </details>
  </div>
</template>

<style scoped>
.vellum-html-spec {
  border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
  padding-top: 0.6rem;
}
.vellum-html-spec__pre {
  margin-top: 0.5rem;
  max-height: 14rem;
  overflow: auto;
  padding: 0.6rem 0.75rem;
  background: color-mix(in oklab, var(--color-graphite-500) 6%, transparent);
  border-radius: var(--radius-sm);
  font-family: var(--font-mono);
  font-size: 0.7rem;
  line-height: 1.5;
  color: var(--color-graphite-700);
}
:global(html.dark) .vellum-html-spec__pre { color: var(--color-graphite-300); }
</style>
