<script setup lang="ts">
import { useTracePage } from './useTracePage'
import SpanList from './components/SpanList.vue'
import SpanDetailSlideover from './components/SpanDetailSlideover.vue'

const route = useRoute()
const traceId = computed(() => route.params.traceId as string)

const { $traceService } = useNuxtApp()
const page = useTracePage($traceService, traceId.value)

function formatTime(iso: string): string {
  return new Date(iso).toLocaleString()
}

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

const summary = computed(() => {
  const t = page.trace.value
  if (!t || t.spans.length === 0) return null
  const sorted = [...t.spans].sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
  const start = sorted[0]!.start
  const end = sorted.reduce((max, s) => new Date(s.end) > new Date(max) ? s.end : max, sorted[0]!.end)
  const durationMs = new Date(end).getTime() - new Date(start).getTime()
  const root = t.spans.find(s => !s.parentSpanId) ?? sorted[0]!
  return { start, end, durationMs, spanCount: t.spans.length, rootName: root.name }
})
</script>

<template>
  <div class="h-full flex flex-col gap-4">
    <div class="flex items-center gap-2">
      <UButton to="/traces" icon="i-lucide-arrow-left" color="neutral" variant="ghost" size="sm">
        Traces
      </UButton>

      <UButton
        v-if="page.trace.value && summary"
        :to="{
          path: '/logs',
          query: {
            traceId: page.trace.value.traceId,
            from: new Date(new Date(summary.start).getTime() - 60_000).toISOString(),
            to: new Date(new Date(summary.end).getTime() + 60_000).toISOString()
          }
        }"
        icon="i-lucide-file-text"
        color="neutral"
        variant="outline"
        size="sm"
      >
        View Logs
      </UButton>
    </div>

    <header v-if="page.trace.value && summary" class="space-y-1">
      <h1 class="text-xl font-semibold">
        {{ summary.rootName }}
      </h1>
      <p class="text-xs font-mono text-muted break-all">
        {{ page.trace.value.traceId }}
      </p>
      <div class="flex flex-wrap gap-4 text-xs text-muted">
        <span>{{ formatTime(summary.start) }} → {{ formatTime(summary.end) }}</span>
        <span>Duration: {{ formatDuration(summary.durationMs) }}</span>
        <span>{{ summary.spanCount }} span{{ summary.spanCount === 1 ? '' : 's' }}</span>
      </div>
    </header>

    <div v-if="page.isLoading.value" class="text-muted">
      Loading…
    </div>

    <UAlert
      v-else-if="page.notFound.value"
      color="warning"
      variant="subtle"
      icon="i-lucide-search-x"
      :title="`Trace ${traceId} not found`"
    />

    <UAlert
      v-else-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-lucide-alert-triangle"
      :title="page.error.value"
    />

    <template v-else-if="page.trace.value">
      <SpanList
        class="flex-1 min-h-0"
        :spans="page.trace.value.spans"
        @select="span => page.selected.value = span"
      />
      <SpanDetailSlideover v-model="page.selected.value" />
    </template>
  </div>
</template>
