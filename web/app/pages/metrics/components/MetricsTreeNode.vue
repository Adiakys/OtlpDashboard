<script setup lang="ts">
import MetricsTreeLeaf from './MetricsTreeLeaf.vue'
import type { MetricTreeNode } from '../buildTree'
import type { InstrumentDto } from '~/services/types'

const props = defineProps<{
  node: MetricTreeNode
  depth: number
  isExpanded: (path: string) => boolean
  isSelected: (instrument: InstrumentDto) => boolean
  isCompatible: (instrument: InstrumentDto) => boolean
}>()

const emit = defineEmits<{
  'toggle-expand': [path: string]
  'toggle-leaf': [instrument: InstrumentDto]
}>()

const expanded = computed(() => props.node.kind === 'branch' && props.isExpanded(props.node.path))
</script>

<template>
  <li v-if="node.kind === 'branch'" class="leading-none">
    <button
      type="button"
      class="w-full flex items-center gap-1.5 py-1 rounded-md text-left hover:bg-elevated transition-colors"
      :style="{ paddingLeft: `${0.25 + depth * 1}rem` }"
      :aria-expanded="expanded"
      @click="emit('toggle-expand', node.path)"
    >
      <UIcon
        :name="expanded ? 'i-ph-caret-down' : 'i-ph-caret-right'"
        class="size-3.5 text-muted shrink-0 transition-transform"
      />
      <UIcon
        :name="expanded ? 'i-ph-folder-open' : 'i-ph-folder'"
        class="size-3.5 text-muted shrink-0"
      />
      <span class="text-sm truncate">{{ node.label }}</span>
    </button>
    <Transition
      enter-active-class="transition-all duration-150 ease-out overflow-hidden"
      leave-active-class="transition-all duration-150 ease-out overflow-hidden"
      enter-from-class="max-h-0 opacity-0"
      enter-to-class="max-h-screen opacity-100"
      leave-from-class="max-h-screen opacity-100"
      leave-to-class="max-h-0 opacity-0"
    >
      <ul v-show="expanded" class="space-y-0.5">
        <MetricsTreeNode
          v-for="child in node.children"
          :key="child.kind === 'branch' ? `b:${child.path}` : `l:${child.key}`"
          :node="child"
          :depth="depth + 1"
          :is-expanded="isExpanded"
          :is-selected="isSelected"
          :is-compatible="isCompatible"
          @toggle-expand="(p) => emit('toggle-expand', p)"
          @toggle-leaf="(i) => emit('toggle-leaf', i)"
        />
      </ul>
    </Transition>
  </li>
  <li v-else class="leading-none">
    <MetricsTreeLeaf
      :instrument="node.instrument"
      :depth="depth"
      :selected="isSelected(node.instrument)"
      :disabled="!isCompatible(node.instrument)"
      @toggle="(i) => emit('toggle-leaf', i)"
    />
  </li>
</template>
