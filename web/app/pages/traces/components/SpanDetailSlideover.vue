<script setup lang="ts">
import type { SpanDto } from '~/services/types'

const model = defineModel<SpanDto | null>({ required: true })

const open = computed({
  get: () => model.value !== null,
  set: (v: boolean) => { if (!v) model.value = null }
})

function formatTime(iso: string): string {
  return new Date(iso).toLocaleString()
}

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}
</script>

<template>
  <USlideover v-model:open="open" title="Span" side="right">
    <template #body>
      <div v-if="model" class="space-y-4 text-sm">
        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            {{ model.name }}
          </h3>
          <p class="text-xs text-muted">
            {{ model.kind }} · {{ formatDuration(model.durationMs) }}
          </p>
        </section>

        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Timing
          </h3>
          <p class="font-mono text-xs">
            {{ formatTime(model.start) }} → {{ formatTime(model.end) }}
          </p>
        </section>

        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Status
          </h3>
          <p>
            {{ model.statusCode }}
            <span v-if="model.statusMessage" class="text-muted"> — {{ model.statusMessage }}</span>
          </p>
        </section>

        <section class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            IDs
          </h3>
          <p class="font-mono text-xs text-muted">
            span: {{ model.spanId }}
          </p>
          <p v-if="model.parentSpanId" class="font-mono text-xs text-muted">
            parent: {{ model.parentSpanId }}
          </p>
        </section>

        <section v-if="model.scopeName" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Scope
          </h3>
          <p class="text-xs">
            {{ model.scopeName }}
            <span v-if="model.scopeVersion" class="text-muted">@ {{ model.scopeVersion }}</span>
          </p>
        </section>

        <section v-if="Object.keys(model.attributes).length > 0" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Attributes
          </h3>
          <dl class="text-xs space-y-0.5">
            <div v-for="(value, key) in model.attributes" :key="key" class="flex gap-2">
              <dt class="font-mono text-muted">
                {{ key }}
              </dt>
              <dd class="font-mono">
                {{ JSON.stringify(value) }}
              </dd>
            </div>
          </dl>
        </section>

        <section v-if="model.events.length > 0" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Events ({{ model.events.length }})
          </h3>
          <ul class="text-xs space-y-1">
            <li v-for="(e, i) in model.events" :key="i" class="bg-elevated px-2 py-1 rounded">
              <span class="font-mono text-muted">{{ formatTime(e.time) }}</span> — {{ e.name }}
            </li>
          </ul>
        </section>

        <section v-if="model.links.length > 0" class="space-y-1">
          <h3 class="text-xs uppercase tracking-wide text-muted">
            Links ({{ model.links.length }})
          </h3>
          <ul class="text-xs space-y-1 font-mono">
            <li v-for="(l, i) in model.links" :key="i" class="bg-elevated px-2 py-1 rounded">
              trace {{ l.traceId }} · span {{ l.spanId }}
            </li>
          </ul>
        </section>
      </div>
    </template>
  </USlideover>
</template>
