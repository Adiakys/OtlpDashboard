<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import type { ParameterDecl } from '~/lib/htmlEngine/types'
import type { InstrumentDto } from '~/services/types'

/**
 * Renders one input for a typed parameter declared on a `spec`-engine
 * widget. Drives the per-instance config form: the user fills these
 * once, the widget's bindings expand the values into metric paths.
 *
 * Each variant emits a primitive (string / number / boolean) so the
 * parent can store the raw value in `HtmlInstanceConfig.parameters`.
 * Untyped parameters fall back to a plain string input so older
 * widgets keep working when a future type lands without a UI handler.
 */
const props = defineProps<{
  decl: ParameterDecl
  /** Current value from `HtmlInstanceConfig.parameters[decl.name]`. */
  modelValue: string | number | boolean | undefined
  /** Other parameter values — needed by `service_instance_id` to
   *  filter its dropdown by the picked `service_name`. */
  siblings: Record<string, string | number | boolean | undefined>
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string | number | boolean | undefined]
}>()

const { t } = useI18n()
const { $metricsService } = useNuxtApp()

const label = computed(() => props.decl.label ?? props.decl.name)
const description = computed(() => props.decl.description)

// --- service-name dropdown source ----------------------------------------
//
// The dashboard already exposes /api/v1/metrics/services and the
// catalog the dashboard page primes carries every recorded instrument's
// serviceName, so for `service_instance_id` we filter the catalog
// instead of adding a new endpoint.

const services = ref<string[]>([])
const instruments = ref<InstrumentDto[]>([])
const sourcesLoaded = ref(false)

async function ensureSourcesLoaded() {
  if (sourcesLoaded.value) return
  const t = props.decl.type
  if (t !== 'service_name' && t !== 'service_instance_id') {
    sourcesLoaded.value = true
    return
  }
  try {
    if (t === 'service_name') {
      services.value = await $metricsService.listServices()
    }
    else {
      instruments.value = await $metricsService.listInstruments()
    }
  }
  catch {
    // Best effort: keep the input usable as a free-form text field if
    // the backend isn't reachable. The placeholder makes the intent
    // clear; the user can still type a value by hand.
  }
  finally {
    sourcesLoaded.value = true
  }
}

onMounted(ensureSourcesLoaded)
watch(() => props.decl.type, ensureSourcesLoaded)

const serviceOptions = computed(() =>
  services.value.map(s => ({ value: s, label: s }))
)

const instanceOptions = computed(() => {
  if (props.decl.type !== 'service_instance_id') return []
  const decl = props.decl
  const dependsOn = decl.dependsOn
  const filterService = dependsOn ? props.siblings[dependsOn] : undefined
  const ids = new Set<string>()
  for (const i of instruments.value) {
    if (!i.serviceInstanceId) continue
    if (filterService && i.serviceName !== filterService) continue
    ids.add(i.serviceInstanceId)
  }
  return [...ids].sort().map(v => ({ value: v, label: v }))
})

// --- two-way binding helpers --------------------------------------------

function setString(v: string | number | undefined) {
  if (v === undefined || v === '') {
    emit('update:modelValue', undefined)
    return
  }
  emit('update:modelValue', String(v))
}

function setNumber(v: number | string | undefined) {
  if (v === undefined || v === '' || v === null) {
    emit('update:modelValue', undefined)
    return
  }
  const n = typeof v === 'number' ? v : Number(v)
  emit('update:modelValue', Number.isFinite(n) ? n : undefined)
}

function setBoolean(v: boolean) {
  emit('update:modelValue', v)
}

const stringValue = computed(() =>
  props.modelValue === undefined || props.modelValue === null
    ? ''
    : String(props.modelValue)
)

const numberValue = computed(() => {
  if (typeof props.modelValue === 'number') return props.modelValue
  if (typeof props.modelValue === 'string' && props.modelValue !== '') {
    const n = Number(props.modelValue)
    return Number.isFinite(n) ? n : undefined
  }
  return undefined
})

const booleanValue = computed(() => Boolean(props.modelValue))

// --- per-type config sugar ----------------------------------------------

const stringPlaceholder = computed(() =>
  props.decl.type === 'string' ? props.decl.placeholder : undefined
)

const stringMax = computed(() =>
  props.decl.type === 'string' ? props.decl.maxLength : undefined
)
</script>

<template>
  <UFormField
    :label="label"
    :hint="description"
    :required="decl.required"
  >
    <template v-if="decl.type === 'service_name'">
      <USelectMenu
        :model-value="stringValue || undefined"
        :items="serviceOptions"
        value-key="value"
        :placeholder="t('dashboard.config.htmlServicePlaceholder')"
        :loading="!sourcesLoaded"
        :search-input="{ placeholder: t('dashboard.config.htmlServicePlaceholder') }"
        class="w-full"
        @update:model-value="setString"
      />
    </template>

    <template v-else-if="decl.type === 'service_instance_id'">
      <USelectMenu
        :model-value="stringValue || undefined"
        :items="instanceOptions"
        value-key="value"
        :placeholder="t('dashboard.config.htmlServiceInstancePlaceholder')"
        :loading="!sourcesLoaded"
        class="w-full"
        @update:model-value="setString"
      />
    </template>

    <template v-else-if="decl.type === 'select'">
      <USelectMenu
        :model-value="stringValue || undefined"
        :items="decl.options.map(o => ({ value: o.value, label: o.label ?? o.value }))"
        value-key="value"
        :placeholder="t('dashboard.config.htmlSelectPlaceholder')"
        class="w-full"
        @update:model-value="setString"
      />
    </template>

    <template v-else-if="decl.type === 'number'">
      <UInput
        type="number"
        :model-value="numberValue"
        :min="decl.min"
        :max="decl.max"
        :step="decl.step ?? 1"
        @update:model-value="setNumber"
      />
    </template>

    <template v-else-if="decl.type === 'boolean'">
      <USwitch
        :model-value="booleanValue"
        @update:model-value="setBoolean"
      />
    </template>

    <template v-else>
      <UInput
        :model-value="stringValue"
        :placeholder="stringPlaceholder"
        :maxlength="stringMax"
        @update:model-value="setString"
      />
    </template>
  </UFormField>
</template>
