<script setup lang="ts">
import { buildTree, filterTree, type MetricTreeNode } from '~/pages/metrics/buildTree'
import type { InstrumentDto } from '~/services/types'
import { instrumentToBinding, type MetricBinding } from '../types'
import InstrumentPickerNode from './InstrumentPickerNode.vue'

const props = defineProps<{
  /** Single mode picks one instrument. Multi-mode constrains selection to a single `kind`. */
  mode: 'single' | 'multi'
  modelValue: MetricBinding | MetricBinding[] | null
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricBinding | MetricBinding[] | null]
}>()

const { t } = useI18n()
const { $metricsService } = useNuxtApp()

const instruments = ref<InstrumentDto[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const search = ref('')

async function load() {
  loading.value = true
  error.value = null
  try {
    instruments.value = await $metricsService.listInstruments()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}
void load()

const tree = computed<MetricTreeNode[]>(() => filterTree(buildTree(instruments.value), search.value))

function bindingKey(b: { resourceHash: string; scopeName: string; instrumentName: string; kind: string }): string {
  return `${b.resourceHash}|${b.scopeName}|${b.instrumentName}|${b.kind}`
}

function instrumentToKey(i: InstrumentDto): string {
  return bindingKey({ resourceHash: i.resourceHash, scopeName: i.scopeName, instrumentName: i.name, kind: i.kind })
}

const selectedKeys = computed<Set<string>>(() => {
  const out = new Set<string>()
  if (!props.modelValue) return out
  if (Array.isArray(props.modelValue)) {
    for (const b of props.modelValue) out.add(bindingKey(b))
  } else {
    out.add(bindingKey(props.modelValue))
  }
  return out
})

const selectedKind = computed<string | null>(() => {
  if (!props.modelValue) return null
  if (Array.isArray(props.modelValue)) return props.modelValue[0]?.kind ?? null
  return props.modelValue.kind
})

function isCompatible(instrument: InstrumentDto): boolean {
  if (props.mode === 'single') return true
  if (selectedKind.value === null) return true
  return instrument.kind === selectedKind.value
}

function isSelected(instrument: InstrumentDto): boolean {
  return selectedKeys.value.has(instrumentToKey(instrument))
}

function toggle(instrument: InstrumentDto) {
  if (props.mode === 'single') {
    emit('update:modelValue', instrumentToBinding(instrument))
    return
  }
  if (!isCompatible(instrument)) return
  const current = Array.isArray(props.modelValue) ? props.modelValue : []
  const key = instrumentToKey(instrument)
  const without = current.filter(b => bindingKey(b) !== key)
  if (without.length === current.length) {
    emit('update:modelValue', [...current, instrumentToBinding(instrument)])
  } else {
    emit('update:modelValue', without)
  }
}

const expanded = ref<Set<string>>(new Set())
function toggleBranch(path: string) {
  const next = new Set(expanded.value)
  if (next.has(path)) next.delete(path)
  else next.add(path)
  expanded.value = next
}
function isExpanded(path: string) { return expanded.value.has(path) }

// Auto-expand the first level so the picker isn't empty on open.
watch(tree, treeNodes => {
  if (expanded.value.size > 0) return
  for (const n of treeNodes) {
    if (n.kind === 'branch') expanded.value.add(n.path)
  }
}, { immediate: true })
</script>

<template>
  <div class="flex flex-col h-full min-h-0 gap-2">
    <UInput
      v-model="search"
      :placeholder="t('metrics.tree.search')"
      icon="i-lucide-search"
      size="sm"
    />

    <UAlert v-if="error" color="error" variant="subtle" :title="error" />

    <div class="flex-1 min-h-0 overflow-auto border border-default rounded">
      <div v-if="loading && instruments.length === 0" class="p-3 text-xs text-muted">
        {{ t('common.loading') }}
      </div>
      <div v-else-if="tree.length === 0" class="p-3 text-xs text-muted">
        {{ t('metrics.tree.empty') }}
      </div>
      <ul v-else>
        <InstrumentPickerNode
          v-for="node in tree"
          :key="node.kind === 'branch' ? `b:${node.path}` : `l:${node.key}`"
          :node="node"
          :depth="0"
          :mode="mode"
          :is-expanded="isExpanded"
          :is-selected="isSelected"
          :is-compatible="isCompatible"
          @toggle-branch="toggleBranch"
          @toggle-leaf="toggle"
        />
      </ul>
    </div>
  </div>
</template>
