<script setup lang="ts">
import type { MetricTreeNode } from '~/pages/metrics/buildTree'
import type { InstrumentDto } from '~/services/types'

const props = defineProps<{
  node: MetricTreeNode
  depth: number
  mode: 'single' | 'multi'
  isExpanded: (path: string) => boolean
  isSelected: (i: InstrumentDto) => boolean
  isCompatible: (i: InstrumentDto) => boolean
}>()

const emit = defineEmits<{
  'toggle-branch': [path: string]
  'toggle-leaf': [instrument: InstrumentDto]
}>()

defineOptions({ name: 'InstrumentPickerNode' })

const indentStyle = computed(() => ({ paddingLeft: `${props.depth * 12 + 8}px` }))
</script>

<template>
  <li v-if="node.kind === 'leaf'"
    class="flex items-center gap-2 py-1 pr-2 text-xs cursor-pointer hover:bg-elevated/50"
    :class="{
      'opacity-50 cursor-not-allowed': !isCompatible(node.instrument),
      'bg-primary/10 text-primary': isSelected(node.instrument)
    }"
    :style="indentStyle"
    @click="isCompatible(node.instrument) && emit('toggle-leaf', node.instrument)"
  >
    <span v-if="mode === 'multi'"
      class="inline-flex items-center justify-center size-3.5 rounded border text-[10px]"
      :class="isSelected(node.instrument)
        ? 'bg-primary border-primary text-white'
        : 'border-default'"
    >
      <UIcon v-if="isSelected(node.instrument)" name="i-ph-check" class="size-3" />
    </span>
    <span class="font-mono truncate">{{ node.instrument.name }}</span>
    <span class="ml-auto text-[10px] text-muted shrink-0">{{ node.instrument.kind }}</span>
  </li>
  <li v-else>
    <div
      class="flex items-center gap-1 py-1 cursor-pointer hover:bg-elevated/50 text-xs"
      :style="indentStyle"
      @click="emit('toggle-branch', node.path)"
    >
      <UIcon
        :name="isExpanded(node.path) ? 'i-ph-caret-down' : 'i-ph-caret-right'"
        class="size-3 text-muted shrink-0"
      />
      <span class="font-medium truncate">{{ node.label }}</span>
      <span class="ml-auto text-[10px] text-muted shrink-0">{{ node.children.length }}</span>
    </div>
    <ul v-if="isExpanded(node.path)">
      <InstrumentPickerNode
        v-for="child in node.children"
        :key="child.kind === 'branch' ? `b:${child.path}` : `l:${child.key}`"
        :node="child"
        :depth="depth + 1"
        :mode="mode"
        :is-expanded="isExpanded"
        :is-selected="isSelected"
        :is-compatible="isCompatible"
        @toggle-branch="(p) => emit('toggle-branch', p)"
        @toggle-leaf="(i) => emit('toggle-leaf', i)"
      />
    </ul>
  </li>
</template>
