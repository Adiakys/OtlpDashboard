<script setup lang="ts">
import MetricsTreeNode from './MetricsTreeNode.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppSkeleton from '~/components/ui/AppSkeleton.vue'
import {
  collectBranchPaths,
  collectInstruments,
  countLeaves,
  type MetricTreeNode as TreeNode
} from '../buildTree'
import { useMetricsTreeState } from '../useMetricsTreeState'
import type { InstrumentDto } from '~/services/types'

const props = defineProps<{
  tree: TreeNode[]
  loading: boolean
  isSelected: (i: InstrumentDto) => boolean
  isCompatible: (i: InstrumentDto) => boolean
}>()

const emit = defineEmits<{ 'toggle-leaf': [instrument: InstrumentDto] }>()

const { t } = useI18n()
const state = useMetricsTreeState()

onMounted(() => state.bind(null))

const total = computed(() => countLeaves(props.tree))

function expandAll() {
  state.expandAll(collectBranchPaths(props.tree))
}

/**
 * Download the flattened instrument list as JSON. Mirrors the visible
 * tree (so any active search filter narrows the export too — matches
 * the principle of least surprise: "export what I'm looking at").
 * Filename carries an ISO timestamp so repeated exports don't clobber
 * each other in the browser's download folder.
 */
function exportJson() {
  const instruments = collectInstruments(props.tree)
  const payload = {
    exportedAt: new Date().toISOString(),
    count: instruments.length,
    instruments
  }
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)
  link.download = `metrics-${stamp}.json`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <div class="flex flex-col h-full min-h-0 border border-default rounded-lg bg-default overflow-hidden">
    <header class="px-3 py-2 flex items-center justify-between gap-2 border-b border-default bg-elevated/50">
      <div class="flex items-center gap-2 min-w-0">
        <UIcon name="i-ph-tree-structure" class="size-4 text-muted shrink-0" />
        <h2 class="text-xs uppercase tracking-wide text-muted truncate">
          {{ t('metrics.tree.title') }}
        </h2>
        <span class="text-xs text-muted shrink-0">
          {{ t('metrics.tree.count', { n: total }) }}
        </span>
      </div>
      <div class="flex items-center gap-1">
        <button
          type="button"
          class="text-xs text-muted hover:text-default transition-colors p-1 rounded hover:bg-default"
          :title="t('metrics.tree.expandAll')"
          :disabled="total === 0"
          @click="expandAll"
        >
          <UIcon name="i-ph-arrows-out-line-vertical" class="size-3.5" />
        </button>
        <button
          type="button"
          class="text-xs text-muted hover:text-default transition-colors p-1 rounded hover:bg-default"
          :title="t('metrics.tree.collapseAll')"
          :disabled="total === 0"
          @click="state.collapseAll"
        >
          <UIcon name="i-ph-arrows-in-line-vertical" class="size-3.5" />
        </button>
        <button
          type="button"
          class="ml-1 inline-flex items-center gap-1.5 text-xs text-muted hover:text-default transition-colors px-2 py-1 rounded hover:bg-default disabled:opacity-50 disabled:cursor-not-allowed"
          :title="t('metrics.tree.exportJsonHint')"
          :disabled="total === 0"
          @click="exportJson"
        >
          <UIcon name="i-ph-download-simple" class="size-3.5" />
          <span>{{ t('metrics.tree.exportJson') }}</span>
        </button>
      </div>
    </header>

    <div class="flex-1 min-h-0 overflow-y-auto p-2">
      <div v-if="loading && total === 0" class="p-2">
        <AppSkeleton :rows="6" row-class="h-5" />
      </div>
      <div v-else-if="total === 0" class="h-full flex items-center justify-center">
        <AppEmptyState
          icon="i-ph-pulse"
          :title="t('metrics.tree.empty')"
        />
      </div>
      <ul v-else class="space-y-0.5">
        <MetricsTreeNode
          v-for="node in tree"
          :key="node.kind === 'branch' ? `b:${node.path}` : `l:${node.key}`"
          :node="node"
          :depth="0"
          :is-expanded="state.isExpanded"
          :is-selected="isSelected"
          :is-compatible="isCompatible"
          @toggle-expand="state.toggle"
          @toggle-leaf="(i) => emit('toggle-leaf', i)"
        />
      </ul>
    </div>
  </div>
</template>
