<script setup lang="ts">
import { computed, inject } from 'vue'
import InstrumentPicker from '../components/InstrumentPicker.vue'
import ParametersSection from '../components/ParametersSection.vue'
import RangePresetSelect from './RangePresetSelect.vue'
import { useWidgetCatalog } from '../catalog'
import { WIDGET_KIND_INJECTION_KEY } from '../injectionKeys'
import type {
  HtmlBindingDecl,
  HtmlInstanceConfig,
  HtmlSpec
} from '~/lib/htmlEngine/types'
import type { ParameterDecl } from '~/lib/htmlEngine/types'
import type { MetricBinding } from '../types'

/**
 * Per-instance config form for `engine: 'spec'` (HTML template) widgets.
 *
 * Layout — top-to-bottom in a scrollable column so a widget with many
 * bindings doesn't squash the metric picker into a postage stamp:
 *
 *  1. Title + Range (always shown)
 *  2. Parameters — one input per definition's `parameters[]`. The
 *     widget's binding templates expand `${name}` from these values, so
 *     a library widget with the metric path baked in only needs the
 *     application name from the user.
 *  3. Per-binding overrides — collapsible. For each declared metric
 *     binding the user can pin a specific instrument, bypassing the
 *     parameter-driven template. Only relevant when the parametric
 *     match doesn't fit (different scope, different service, …).
 *
 * Backwards compatibility: a definition without `parameters[]` and
 * without `metric` templates falls back to the previous "one picker per
 * binding" experience — the Overrides section opens by default.
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

const parameters = computed<ParameterDecl[]>(() => definition.value?.parameters ?? [])

const metricDecls = computed<HtmlBindingDecl[]>(() =>
  (spec.value?.dataBindings ?? []).filter(
    b => b.type === 'metric' || b.type === 'metric-series'
  )
)

/** Bindings that can be resolved purely from parameters — used to decide
 *  whether the Overrides section is actually needed. */
const hasTemplatedBinding = computed(() =>
  metricDecls.value.some(b => b.type === 'metric' && b.metric != null)
)

/** Default-open the overrides section only when there's no parameter-
 *  driven path: the user has to use the picker to bind anything. */
const overridesOpen = computed(() => parameters.value.length === 0 && !hasTemplatedBinding.value)

const templatePreview = computed(() => spec.value?.template?.trim() ?? '')

function setParameters(next: Record<string, string | number | boolean>) {
  emit('update:modelValue', { ...props.modelValue, parameters: next })
}

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
  <div class="vellum-html-form">
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

    <ParametersSection
      :model-value="modelValue.parameters"
      @update:model-value="setParameters"
    />

    <div
      v-if="metricDecls.length === 0"
      class="text-mono-sm"
      style="color: var(--color-graphite-500);"
    >
      {{ t('dashboard.config.htmlNoMetricBindings') }}
    </div>

    <details
      v-else
      class="vellum-html-form__section"
      :open="overridesOpen"
    >
      <summary class="text-overline cursor-pointer" style="color: var(--color-graphite-500);">
        {{ t('dashboard.config.htmlOverrides') }}
      </summary>
      <p
        v-if="parameters.length > 0"
        class="text-mono-sm mt-1 mb-2"
        style="color: var(--color-graphite-500);"
      >
        {{ t('dashboard.config.htmlOverridesHint') }}
      </p>

      <div class="flex flex-col gap-4">
        <section
          v-for="decl in metricDecls"
          :key="decl.name"
          class="flex flex-col gap-1.5"
        >
          <header class="flex items-baseline justify-between">
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ decl.name }}
            </span>
            <span
              v-if="decl.description"
              class="text-mono-sm"
              style="color: var(--color-graphite-500);"
            >
              {{ decl.description }}
            </span>
          </header>
          <div class="vellum-html-form__picker">
            <InstrumentPicker
              mode="single"
              :model-value="modelValue.bindings?.[decl.name] ?? null"
              @update:model-value="(v) => setBinding(decl.name, (v as MetricBinding | null))"
            />
          </div>
        </section>
      </div>
    </details>

    <details v-if="templatePreview" class="vellum-html-form__section">
      <summary class="text-overline cursor-pointer" style="color: var(--color-graphite-500);">
        {{ t('dashboard.config.htmlTemplatePreview') }}
      </summary>
      <pre class="vellum-html-form__pre"><code>{{ templatePreview }}</code></pre>
    </details>
  </div>
</template>

<style scoped>
/* The drawer body is `h-full flex-col min-h-0`; this wrapper is the
   actual scroll surface. Without `overflow-y-auto` here a tall list of
   bindings + the template preview would push the slideover footer off
   screen and squash the metric pickers. */
.vellum-html-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  height: 100%;
  min-height: 0;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.vellum-html-form__section {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding-top: 0.6rem;
  border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
}

/* Keep the metric picker tall enough to show the tree even when several
   bindings stack up — the parent container scrolls, the picker doesn't
   need to. */
.vellum-html-form__picker {
  height: 18rem;
  min-height: 0;
}

.vellum-html-form__pre {
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
:global(html.dark) .vellum-html-form__pre {
  color: var(--color-graphite-300);
}
</style>
