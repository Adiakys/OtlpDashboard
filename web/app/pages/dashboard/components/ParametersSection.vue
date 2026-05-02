<script setup lang="ts">
import { computed, inject } from 'vue'
import ParameterInput from './ParameterInput.vue'
import { useWidgetCatalog } from '../catalog'
import { WIDGET_KIND_INJECTION_KEY } from '../injectionKeys'
import { effectiveValue } from '~/lib/htmlEngine/parameterExpansion'
import type { ParameterDecl } from '~/lib/htmlEngine/types'

/**
 * Renders the typed-parameter inputs declared by a widget definition.
 * Shared between every metric-flavoured config form (Stat, Line,
 * Sparkline, Gauge, BarGauge, Pie, Heatmap + the spec-engine
 * HtmlConfigForm). Each form mounts this at the top so a library widget
 * that ships with `parameters: [{ name: 'service', … }]` asks the user
 * only for the application name; the metric path stays bound by the
 * definition's `${param}` template.
 *
 * The component looks up its own definition through `WIDGET_KIND_INJECTION_KEY`
 * (provided by `WidgetConfigSlot`) so each form just needs `<ParametersSection
 * v-model="modelValue.parameters" />` — no per-form catalog plumbing. Renders
 * nothing when the definition has no `parameters[]` declared.
 */
const props = defineProps<{
  modelValue: Record<string, string | number | boolean> | undefined
}>()

const emit = defineEmits<{
  'update:modelValue': [value: Record<string, string | number | boolean>]
}>()

const { t } = useI18n()
const catalog = useWidgetCatalog()

const kindRef = inject(WIDGET_KIND_INJECTION_KEY, computed(() => '' as string))
const definition = computed(() => catalog.byKind(kindRef.value))
const decls = computed<ParameterDecl[]>(() => definition.value?.parameters ?? [])

const values = computed<Record<string, string | number | boolean | undefined>>(() => {
  const out: Record<string, string | number | boolean | undefined> = {}
  for (const decl of decls.value) {
    out[decl.name] = effectiveValue(decl, props.modelValue)
  }
  return out
})

function setParameter(name: string, value: string | number | boolean | undefined) {
  const next = { ...(props.modelValue ?? {}) }
  if (value === undefined || value === '') {
    delete next[name]
  }
  else {
    next[name] = value
  }
  emit('update:modelValue', next)
}
</script>

<template>
  <section v-if="decls.length > 0" class="vellum-params">
    <header class="text-overline" style="color: var(--color-graphite-500);">
      {{ t('dashboard.config.htmlParameters') }}
    </header>
    <div class="flex flex-col gap-3">
      <ParameterInput
        v-for="decl in decls"
        :key="decl.name"
        :decl="decl"
        :model-value="values[decl.name]"
        :siblings="values"
        @update:model-value="(v) => setParameter(decl.name, v)"
      />
    </div>
  </section>
</template>

<style scoped>
.vellum-params {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding-top: 0.6rem;
  border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
}
</style>
